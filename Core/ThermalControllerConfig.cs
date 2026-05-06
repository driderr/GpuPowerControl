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
        public uint ExitHysteresis { get; set; } = 5;

        // Fault tolerance
        public int MaxConsecutiveReadFailures { get; set; } = 5;

        // Default hardware power limits
        public int DefaultMaxPower { get; set; } = 600;
        public int DefaultMinPower { get; set; } = 150;

        // Power adjustment gating
        public int MinPowerDeltaFarW { get; set; } = 10;        // Minimum change when far from target
        public int MinPowerDeltaNearW { get; set; } = 3;        // Minimum change when near target
        public uint NearTargetThreshold { get; set; } = 3;      // Degrees from target to switch to fine-tuning
        public int MinAdjustmentIntervalMs { get; set; } = 2500; // Cooldown between power changes
        public double IntervalBypassDerivative { get; set; } = 2.0; // °C/s - bypass interval when rising faster
        public double NormalMaxPowerIncreaseRateWps { get; set; } = 15; // Max power increase rate during normal PID (Watts/second)

        // Emergency recovery
        public int EmergencyHoldMs { get; set; } = 5000;        // Force minimum power for this duration after emergency (ms)
        public double EmergencyRecoveryRateWps { get; set; } = 5;  // Max power increase rate during emergency recovery (Watts/second)
    }
}
