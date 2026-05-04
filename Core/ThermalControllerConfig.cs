namespace GpuThermalController.Core
{
    /// <summary>
    /// Configuration for the thermal controller.
    /// </summary>
    public class ThermalControllerConfig
    {
        public uint TriggerTemp { get; set; } = 80;
        public uint TargetTemp { get; set; } = 75;
        public uint EmergencyTemp { get; set; } = 90;
        public double Kp { get; set; } = 8.0;
        public double Ki { get; set; } = 0.5;
        public double Kd { get; set; } = 2.5;
        public double LookaheadSeconds { get; set; } = 1.5;
        public double PredictiveFloor { get; set; } = 70;
        public int ControllingSleepMs { get; set; } = 250;
        public int IdleSleepMs { get; set; } = 1500;
        public int MaxConsecutiveReadFailures { get; set; } = 5;
    }
}