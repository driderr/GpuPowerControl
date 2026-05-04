using System;
using System.Threading;
using System.Threading.Tasks;
using GpuThermalController.Interfaces;

namespace GpuThermalController.Core
{
    /// <summary>
    /// Configuration for the thermal controller.
    /// </summary>
    public class ThermalControllerConfig
    {
        public uint TriggerTemp { get; set; } = 80;
        public uint TargetTemp { get; set; } = 75;
        public uint EmergencyTemp { get; set; } = 95;
        public double Kp { get; set; } = 8.0;
        public double Ki { get; set; } = 0.5;
        public double Kd { get; set; } = 2.5;
        public double LookaheadSeconds { get; set; } = 1.5;
        public double PredictiveFloor { get; set; } = 70;
        public int ControllingSleepMs { get; set; } = 250;
        public int IdleSleepMs { get; set; } = 1500;
        public int MaxConsecutiveReadFailures { get; set; } = 5;
    }

    /// <summary>
    /// Event arguments raised when the controller state changes.
    /// </summary>
    public class ThermalControllerEventArgs : EventArgs
    {
        public uint Temperature { get; }
        public int PowerLimit { get; }
        public bool IsControlling { get; }
        public string? Message { get; }

        public ThermalControllerEventArgs(uint temperature, int powerLimit, bool isControlling, string? message = null)
        {
            Temperature = temperature;
            PowerLimit = powerLimit;
            IsControlling = isControlling;
            Message = message;
        }
    }

    /// <summary>
    /// Main orchestration class that runs the thermal control loop.
    /// Receives dependencies via constructor for testability.
    /// </summary>
    public class ThermalController
    {
        private readonly IGpuDevice _device;
        private readonly PidController _pidController;
        private readonly TriggerEvaluator _triggerEvaluator;
        private readonly ThermalControllerConfig _config;

        private bool _isControlling = false;
        private uint _lastKnownGoodTemp;
        private int _currentPowerLimit;
        private CancellationTokenSource? _cts;

        public bool IsControlling => _isControlling;
        public int CurrentPowerLimit => _currentPowerLimit;
        public uint LastKnownGoodTemp => _lastKnownGoodTemp;

        /// <summary>Raised on significant state changes (trigger engage/disengage, power changes, emergencies).</summary>
        public event EventHandler<ThermalControllerEventArgs>? OnStateChange;

        public ThermalController(
            IGpuDevice device,
            PidController pidController,
            TriggerEvaluator triggerEvaluator,
            ThermalControllerConfig config)
        {
            _device = device;
            _pidController = pidController;
            _triggerEvaluator = triggerEvaluator;
            _config = config;
            _lastKnownGoodTemp = config.TargetTemp;
            _currentPowerLimit = device.MaxPower;
        }

        /// <summary>
        /// Starts the control loop asynchronously.
        /// </summary>
        public Task RunAsync(CancellationToken cancellationToken = default)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            return Task.Run(() => MainLoop(_cts.Token), _cts.Token);
        }

        /// <summary>
        /// Stops the control loop and restores maximum power.
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            RestoreMaxPower();
        }

        /// <summary>
        /// Executes a single step of the control loop.
        /// Used by tests to drive the controller without real-time sleeps.
        /// </summary>
        /// <param name="simulatedTemp">Optional simulated temperature (for testing). If not provided, reads from device.</param>
        /// <param name="dt">Time delta in seconds since last step (for testing). Default 0.25s.</param>
        public void Step(double? simulatedTemp = null, double dt = 0.25)
        {
            uint currentTemp = simulatedTemp.HasValue ? (uint)Math.Round(simulatedTemp.Value) : SafeGetTemperature();

            // 1. EMERGENCY SAFETY
            if (currentTemp >= _config.EmergencyTemp)
            {
                string? msg = $"[EMERGENCY] Temp hit {currentTemp}C! Forcing minimum power limit ({_device.MinPower}W).";
                SetPowerLimit(_device.MinPower);
                RaiseEvent(currentTemp, _currentPowerLimit, _isControlling, msg);
                _lastKnownGoodTemp = currentTemp;
                return;
            }

            // Calculate derivative
            double derivative = (currentTemp - _lastKnownGoodTemp) / dt;
            if (dt <= 0) dt = 0.001;

            // 2. TRIGGER EVALUATION
            if (!_isControlling)
            {
                TriggerResult trigger = _triggerEvaluator.Evaluate(currentTemp, derivative, _isControlling);

                if (trigger != TriggerResult.None)
                {
                    _isControlling = true;
                    _pidController.Reset();

                    string? msg = trigger == TriggerResult.Predictive
                        ? $"[PREDICTIVE TRIGGER] Temp {currentTemp}C rising at {derivative:F1}C/s. Engaging early."
                        : $"[SAFETY TRIGGER] Temp hit {currentTemp}C. Engaging PID control.";

                    RaiseEvent(currentTemp, _currentPowerLimit, _isControlling, msg);
                }
            }

            // 3. PID CONTROL
            if (_isControlling)
            {
                int newPower = _pidController.CalculatePowerLimit(currentTemp, _lastKnownGoodTemp, dt);

                if (newPower != _currentPowerLimit)
                {
                    SetPowerLimit(newPower);
                    string? msg = $"[{DateTime.Now:HH:mm:ss}] Temp: {currentTemp}C | Target: {_config.TargetTemp}C | Limiting Power to: {newPower}W";
                    RaiseEvent(currentTemp, _currentPowerLimit, _isControlling, msg);
                }

                // Exit condition: temp sufficiently below target AND power fully restored
                if (currentTemp <= _config.TargetTemp - 2 && _currentPowerLimit >= _device.MaxPower)
                {
                    _isControlling = false;
                    _pidController.Reset();
                    string? msg = $"[STABLE] Temp settled at {currentTemp}C. Full power restored. Returning to idle monitoring.";
                    RaiseEvent(currentTemp, _currentPowerLimit, _isControlling, msg);
                }
            }

            _lastKnownGoodTemp = currentTemp;
        }

        private void MainLoop(CancellationToken token)
        {
            double lastTemp = SafeGetTemperature();
            DateTime lastTime = DateTime.UtcNow;

            while (!token.IsCancellationRequested)
            {
                uint currentTemp = SafeGetTemperature();
                DateTime now = DateTime.UtcNow;
                double dt = (now - lastTime).TotalSeconds;
                if (dt <= 0) dt = 0.001;

                // Determine sleep duration before processing
                int sleepMs = _isControlling ? _config.ControllingSleepMs : _config.IdleSleepMs;

                // Use the step-like logic inline for the real loop
                // (Mirrors Step() logic but with real temperature reads)

                // 1. EMERGENCY SAFETY
                if (currentTemp >= _config.EmergencyTemp)
                {
                    string? msg = $"[EMERGENCY] Temp hit {currentTemp}C! Forcing minimum power limit ({_device.MinPower}W).";
                    SetPowerLimit(_device.MinPower);
                    RaiseEvent(currentTemp, _currentPowerLimit, _isControlling, msg);
                    Thread.Sleep(500);
                    lastTemp = currentTemp;
                    lastTime = now;
                    continue;
                }

                double derivative = (currentTemp - lastTemp) / dt;

                // 2. TRIGGER EVALUATION
                if (!_isControlling)
                {
                    TriggerResult trigger = _triggerEvaluator.Evaluate(currentTemp, derivative, _isControlling);

                    if (trigger != TriggerResult.None)
                    {
                        _isControlling = true;
                        _pidController.Reset();

                        string? triggerMsg = trigger == TriggerResult.Predictive
                            ? $"[PREDICTIVE TRIGGER] Temp {currentTemp}C rising at {derivative:F1}C/s. Engaging early."
                            : $"[SAFETY TRIGGER] Temp hit {currentTemp}C. Engaging PID control.";

                        RaiseEvent(currentTemp, _currentPowerLimit, _isControlling, triggerMsg);
                    }
                }

                // 3. PID CONTROL
                if (_isControlling)
                {
                    int newPower = _pidController.CalculatePowerLimit(currentTemp, lastTemp, dt);

                    if (newPower != _currentPowerLimit)
                    {
                        SetPowerLimit(newPower);
                        string? msg = $"[{DateTime.Now:HH:mm:ss}] Temp: {currentTemp}C | Target: {_config.TargetTemp}C | Limiting Power to: {newPower}W";
                        RaiseEvent(currentTemp, _currentPowerLimit, _isControlling, msg);
                    }

                    // Exit condition
                    if (currentTemp <= _config.TargetTemp - 2 && _currentPowerLimit >= _device.MaxPower)
                    {
                        _isControlling = false;
                        _pidController.Reset();
                        string? msg = $"[STABLE] Temp settled at {currentTemp}C. Full power restored. Returning to idle monitoring.";
                        RaiseEvent(currentTemp, _currentPowerLimit, _isControlling, msg);
                    }

                    Thread.Sleep(_config.ControllingSleepMs);
                }
                else
                {
                    Thread.Sleep(_config.IdleSleepMs);
                }

                lastTemp = currentTemp;
                lastTime = now;
            }
        }

        private uint SafeGetTemperature()
        {
            uint temp = _device.GetTemperature();
            _lastKnownGoodTemp = temp;
            return temp;
        }

        private void SetPowerLimit(int watts)
        {
            bool success = _device.SetPowerLimit(watts, out string? error);
            if (success)
            {
                _currentPowerLimit = watts;
            }
            else if (error != null)
            {
                RaiseEvent(_lastKnownGoodTemp, _currentPowerLimit, _isControlling, $"Warning: Failed to set power limit to {watts}W: {error}");
            }
        }

        private void RestoreMaxPower()
        {
            _device.SetPowerLimit(_device.MaxPower, out _);
            _currentPowerLimit = _device.MaxPower;
        }

        private void RaiseEvent(uint temperature, int powerLimit, bool isControlling, string? message)
        {
            OnStateChange?.Invoke(this, new ThermalControllerEventArgs(temperature, powerLimit, isControlling, message));
        }
    }
}