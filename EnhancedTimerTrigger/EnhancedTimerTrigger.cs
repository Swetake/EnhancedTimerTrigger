using System;
using System.Activities;
using System.Activities.Statements;
using System.Activities.Validation;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UiPath.Platform.Triggers;

namespace EnhancedTimerTrigger
{
    public class EnhancedTimerTrigger : TriggerBase<EnhancedTimerTriggerArgs>
    {
        private const Int32 DEFAULT_POLING_TIME = 250;

        [Category("Input")]
        [Description("Initial target DateTime.If not set, DateTime.Now will be applied.")]
        public InArgument<DateTime> InitialTargetDateTime { get; set; }

        [Category("Input")]
        [RequiredArgument]
        [Description("Elapsed time to run next time.(sec)")]
        public InArgument<Int32> Interval { get; set; }

        [Category("Input")]
        [Description("If set True, next target time will be add Interval to actual triggered time. Default/False is to add Interval to current target time without random addiiton time.")]
        public InArgument<bool> TargetActualTimeMode { get; set; }

        [Category("Random")]
        [Description("If set True, random seconds will be added each target time")]
        public InArgument<bool> AddRandom { get; set; }

        [Category("Random")]
        [Description("Lower offset value for additonal random seconds.")]
        public InArgument<Int32> LowerOffsetLimit { get; set; }

        [Category("Random")]
        [Description("Upper offset value for additonal random seconds.")]
        public InArgument<Int32> UpperOffsetLimit { get; set; }

        [Category("NormalDistRandom")]
        [Description("If set True, random seconds based normal distribution will be added each target time.")]
        public InArgument<bool> AddNormalDistRandom { get; set; }

        [Category("NormalDistRandom")]
        [Description("Sigma for normal distribution random.(sec)")]
        public InArgument<double> Sigma { get; set; }

        [Category("Advanced")]
        [Description("Poling Interval (msec) Default value is 250msec")]
        public InArgument<Int32> PolingInterval { get; set; }


        private Task task;
        private CancellationTokenSource tokenSource;
        private CancellationToken cancelToken;

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            base.CacheMetadata(metadata);
        }
        protected override void StartMonitor(NativeActivityContext context, Action<EnhancedTimerTriggerArgs> sendTrigger)
        {
            tokenSource = new CancellationTokenSource();
            cancelToken = tokenSource.Token;

            var interval = Interval.Get(context);
            var targetDt = InitialTargetDateTime.Get(context);
            var targetMode = TargetActualTimeMode.Get(context);
            var addRandom = AddRandom.Get(context);
            var lowerOffsetLimit = LowerOffsetLimit.Get(context);
            var upperOffsetLimit = UpperOffsetLimit.Get(context);
            var addNormalDistRandom = AddNormalDistRandom.Get(context);
            var sigma = Sigma.Get(context);
            var polingInterval = PolingInterval.Get(context);

            if (interval <= 0) { throw new ArgumentOutOfRangeException("intrval must be positive value."); }
            if (targetDt < DateTime.Now.AddYears(-1)) { targetDt = DateTime.Now; }
            if (polingInterval <= 0) { polingInterval = DEFAULT_POLING_TIME; }
            if (upperOffsetLimit <= lowerOffsetLimit) { addRandom = false; }
            if (sigma <= 0) { addNormalDistRandom = false; }

            var inargs = new InArgs(targetDt, interval, targetMode, addRandom, lowerOffsetLimit, upperOffsetLimit, addNormalDistRandom, sigma, polingInterval);
            task = Task.Run(() =>
            {
                Poling(DoTrigger, inargs, cancelToken);
            });

            return;
            void DoTrigger(OutArgs outArgs) => sendTrigger(new EnhancedTimerTriggerArgs(outArgs));
        }

        protected override void StopMonitor(System.Activities.ActivityContext context) { tokenSource.Cancel(); }


        static void Poling(Action<OutArgs> callback, InArgs inargs, CancellationToken token)
        {
            var initialTargetDt = inargs.initialTargetDateTime;
            var interval = inargs.interval;
            var targetMode = inargs.mode;
            var isAddRandom = inargs.addRandom;
            var lowerLimit = inargs.lowerLimit;
            var upperLimit = inargs.upperLimit;
            var isAddNormalDist = inargs.addNormalDistRandom;
            var sigma = inargs.sigma;
            var polingInterval = inargs.polingInterval;

            int seed = (Environment.TickCount + Int32.Parse(DateTime.Now.ToString("ssMMHH")) + Environment.MachineName.Select((x, i) => (Int32)(x) + i * 8).Sum(v => v));
            var rnd = new Random(seed);

            DateTime currentTargetDateTime;
            DateTime nextTargetDateTime;
            DateTime currentStandardTargetDateTime;
            DateTime nextStandardTargetDateTime;
            DateTime triggeredDateTime;

            currentStandardTargetDateTime = initialTargetDt;
            currentTargetDateTime = AddRandomSec(currentStandardTargetDateTime, isAddRandom, lowerLimit, upperLimit, isAddNormalDist, sigma, rnd);

            while (true)
            {
                if (token.IsCancellationRequested) break;
                Thread.Sleep(polingInterval);
                if (DateTime.Now > currentTargetDateTime)
                {
                    triggeredDateTime = DateTime.Now;
                    if (targetMode)
                    {
                        nextStandardTargetDateTime = triggeredDateTime.AddSeconds(interval);
                    }
                    else
                    {
                        nextStandardTargetDateTime = currentStandardTargetDateTime.AddSeconds(interval);

                    }
                    while (nextStandardTargetDateTime < DateTime.Now)
                    {
                        nextStandardTargetDateTime = nextStandardTargetDateTime.AddSeconds(interval);
                    }
                    nextTargetDateTime = AddRandomSec(nextStandardTargetDateTime, isAddRandom, lowerLimit, upperLimit, isAddNormalDist, sigma, rnd);
                    callback(new OutArgs(currentTargetDateTime, currentStandardTargetDateTime, nextTargetDateTime, triggeredDateTime));
                    currentTargetDateTime = nextTargetDateTime;
                    currentStandardTargetDateTime = nextStandardTargetDateTime;
                }
            }
        }

        static DateTime AddRandomSec(DateTime stdDateTime, bool isAddRandom, Int32 lower, Int32 upper, bool isAddNormDist, double sigma, Random rnd)
        {
            DateTime result = stdDateTime;
            if (isAddRandom)
            {
                result = result.AddSeconds(rnd.NextDouble() * (double)(upper - lower) + (double)lower);
            }

            if (isAddNormDist)
            {
                double d1 = 1.0 - rnd.NextDouble();
                double d2 = 1.0 - rnd.NextDouble();
                result = result.AddSeconds(sigma * Math.Sqrt(-2.0 * Math.Log(d1)) * Math.Sin(2.0 * Math.PI * d2));
            }

            return result;
        }

    }
    public class EnhancedTimerTriggerArgs : TriggerArgs
    {
        public DateTime CurrentTargetDateTime { get; }
        public DateTime CurrentStandardTargetDateTime { get; }
        public DateTime NextTargetDateTime { get; }

        public DateTime ActualTriggeredDateTime { get; }

        public EnhancedTimerTriggerArgs(OutArgs outargs)
        {
            CurrentTargetDateTime = outargs.currentTargetDateTime;
            CurrentStandardTargetDateTime = outargs.currentStandardtargetDateTime;
            NextTargetDateTime = outargs.nextTargetDateTime;
            ActualTriggeredDateTime = outargs.actualTriggeredDateTime;
        }

    }

    public class InArgs
    {
        internal DateTime initialTargetDateTime;
        internal Int32 interval;
        internal bool mode;
        internal bool addRandom;
        internal Int32 lowerLimit;
        internal Int32 upperLimit;
        internal bool addNormalDistRandom;
        internal double sigma;
        internal Int32 polingInterval;

        internal InArgs(DateTime _targetDt, Int32 _interval, bool _mode, bool _addRandom, Int32 _lowerLimit, Int32 _upperLimit, bool _addNormalDistRandom, double _sigma, Int32 _polingInterval)
        {
            initialTargetDateTime = _targetDt;
            interval = _interval;
            mode = _mode;
            addRandom = _addRandom;
            lowerLimit = _lowerLimit;
            upperLimit = _upperLimit;
            addNormalDistRandom = _addNormalDistRandom;
            sigma = _sigma;
            polingInterval = _polingInterval;
        }

    }
    public class OutArgs
    {
        public DateTime currentTargetDateTime;
        public DateTime currentStandardtargetDateTime;
        public DateTime actualTriggeredDateTime;
        public DateTime nextTargetDateTime;
        public OutArgs(DateTime _currentDt, DateTime _curentStdDt, DateTime _nextDt, DateTime _actualDt)
        {
            currentTargetDateTime = _currentDt;
            currentStandardtargetDateTime = _curentStdDt;
            nextTargetDateTime = _nextDt;
            actualTriggeredDateTime = _actualDt;
        }
    }


}