namespace GpuThermalController.Core
{
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

        /// <summary>Current accumulated integral value (exposed for testing and state management).</summary>
        public double Integral { get; private set; }

        public PidController(
            double kp,
            double ki,
            double kd,
            uint targetTemp,
            int maxPower,
            int minPower)
        {
            _kp = kp;
            _ki = ki;
            _kd = kd;
            _targetTemp = targetTemp;
            _maxPower = maxPower;
            _minPower = minPower;
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
            if (dt <= 0) dt = 0.001;

            double error = currentTemp - _targetTemp;

            // Proportional term
            double P = _kp * error;

            // Integral term with anti-windup
            Integral += (error * dt);
            if (Integral > 250) Integral = 250;
            if (Integral < -50) Integral = -50;
            double I = _ki * Integral;

            // Derivative term (only when temperature is rising)
            double derivative = (currentTemp - lastTemp) / dt;
            double D = (derivative > 0) ? (_kd * derivative) : 0;

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
        }
    }
}