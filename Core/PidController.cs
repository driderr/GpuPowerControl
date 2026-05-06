namespace GpuThermalController.Core
{
    /// <summary>
    /// Captures the individual P, I, D contributions from the last PID calculation.
    /// Used by the dashboard for displaying PID breakdown.
    /// </summary>
    public record PidComponents(double P, double I, double D, double Error, double Derivative);

    /// <summary>
    /// Pure PID controller for thermal management.
    /// Contains no NVML calls, no side effects, and no I/O.
    /// Fully deterministic given the same inputs.
    /// </summary>
    public class PidController
    {
        private readonly double _kp;
        private readonly double _ki;
        private readonly double _kd;
        private readonly int _maxPower;
        private readonly int _minPower;
        private readonly uint _targetTemp;
        private readonly double _integralMax;
        private readonly double _integralMin;
        private readonly double _integralBand;
        private readonly double _minimumDt;

        /// <summary>Current accumulated integral value (exposed for testing and state management).</summary>
        public double Integral { get; private set; }

        /// <summary>Individual P, I, D contributions from the last calculation. Null if no calculation has occurred.</summary>
        public PidComponents? LastComponents { get; private set; }

        public PidController(
            double kp,
            double ki,
            double kd,
            uint targetTemp,
            int maxPower,
            int minPower,
            double integralMax,
            double integralMin,
            double integralBand,
            double minimumDt)
        {
            _kp = kp;
            _ki = ki;
            _kd = kd;
            _targetTemp = targetTemp;
            _maxPower = maxPower;
            _minPower = minPower;
            _integralMax = integralMax;
            _integralMin = integralMin;
            _integralBand = integralBand;
            _minimumDt = minimumDt;
            Integral = 0;
        }

        /// <summary>
        /// Calculates the new power limit based on current temperature readings.
        /// </summary>
        /// <param name="currentTemp">Current GPU temperature in °C.</param>
        /// <param name="lastTemp">Previous temperature reading in °C.</param>
        /// <param name="dt">Time elapsed since last reading in seconds.</param>
        /// <returns>The calculated power limit in Watts, clamped to [MinPower, MaxPower].</returns>
        public int CalculatePowerLimit(double currentTemp, double lastTemp, double dt)
        {
            // Ensure dt is never zero or negative
            if (dt <= 0) dt = _minimumDt;

            double error = currentTemp - _targetTemp;

            // Proportional term
            double P = _kp * error;

            // Conditional integration: only accumulate when near target.
            // When far from target, P handles the large error and D handles the rate of change.
            // Reset integral when out of band so it starts fresh when approaching target.
            double absError = Math.Abs(error);
            if (absError > _integralBand)
            {
                Integral = 0;
            }
            else
            {
                Integral += (error * dt);
                if (Integral > _integralMax) Integral = _integralMax;
                if (Integral < _integralMin) Integral = _integralMin;
            }
            double I = _ki * Integral;

            // Derivative term (only when temperature is rising)
            double derivative = (currentTemp - lastTemp) / dt;
            double D = (derivative > 0) ? (_kd * derivative) : 0;

            // Store components for dashboard
            LastComponents = new PidComponents(P, I, D, error, derivative);

            // Calculate total power reduction
            double powerReduction = P + I + D;
            int newPower = (int)Math.Floor(_maxPower - powerReduction);

            // Clamp to hardware limits
            if (newPower > _maxPower) newPower = _maxPower;
            if (newPower < _minPower) newPower = _minPower;

            return newPower;
        }

        /// <summary>
        /// Resets the integral accumulator. Call when entering or exiting control mode.
        /// </summary>
        public void Reset()
        {
            Integral = 0;
            LastComponents = null;
        }
    }
}