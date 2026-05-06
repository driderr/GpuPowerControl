namespace GpuThermalController.Core
{
    public class TriggerEvaluator
    {
        private readonly uint _triggerTemp;
        private readonly double _predictiveFloor;
        private readonly double _lookaheadSeconds;

        public TriggerEvaluator(uint triggerTemp, double predictiveFloor, double lookaheadSeconds)
        {
            _triggerTemp = triggerTemp;
            _predictiveFloor = predictiveFloor;
            _lookaheadSeconds = lookaheadSeconds;
        }

        public TriggerResult Evaluate(double currentTemp, double derivative, bool isControlling)
        {
            if (isControlling)
                return TriggerResult.None;

            if (currentTemp >= _triggerTemp)
                return TriggerResult.Safety;

            if (currentTemp >= _predictiveFloor)
            {
                double predictedTemp = currentTemp + (derivative * _lookaheadSeconds);
                if (predictedTemp >= _triggerTemp)
                    return TriggerResult.Predictive;
            }

            return TriggerResult.None;
        }
    }

    public enum TriggerResult
    {
        None,
        Safety,
        Predictive
    }
}
