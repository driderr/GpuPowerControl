namespace GpuThermalController.Core
{
    /// <summary>
    /// Single source of truth for all thermal controller parameters.
    /// </summary>
    public class ThermalControllerConfig
    {
        // Temperature thresholds
        public uint TriggerTemp { get; set; } = 80;
        public uint TargetTemp { get; set; } = 75;
        public uint EmergencyTemp { get; set; } = 90;

        // PID gains
        public double Kp { get; set; } = 8.0;
        public double Ki { get; set; } = 0.5;
        public double Kd { get; set; } = 2.5;

        // PID anti-windup limits
        public double IntegralMax { get; set; } = 250;
        public double IntegralMin { get; set; } = -50;

        // Timing
        public double DefaultDt { get; set; } = 0.25;
        public double MinimumDt { get; set; } = 0.001;
        public int ControllingSleepMs { get; set; } = 250;
        public int IdleSleepMs { get; set; } = 1500;

        // Predictive trigger
        public double LookaheadSeconds { get; set; } = 1.5;
        public double PredictiveFloor { get; set; } = 70;

        // Exit condition hysteresis (exit when temp <= TargetTemp - ExitHysteresis)
        public uint ExitHysteresis { get; set; } = 2;

        // Fault tolerance
        public int MaxConsecutiveReadFailures { get; set; } = 5;

        // Default hardware power limits
        public int DefaultMaxPower { get; set; } = 600;
        public int DefaultMinPower { get; set; } = 150;
    }
}
