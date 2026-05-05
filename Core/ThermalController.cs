using System;
using System.Threading;
using System.Threading.Tasks;
using GpuThermalController.Interfaces;

namespace GpuThermalController.Core
{
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
        private int _consecutiveReadFailures;
        private int _pidCycles;
        private int _stateTransitions;
        private CancellationTokenSource? _cts;

        public bool IsControlling => _isControlling;
        public int CurrentPowerLimit => _currentPowerLimit;
        public uint LastKnownGoodTemp => _lastKnownGoodTemp;
        public PidController PidController => _pidController;
        public ThermalControllerConfig Config => _config;
        public int ConsecutiveReadFailures => _consecutiveReadFailures;
        public int PidCycles => _pidCycles;
        public int StateTransitions => _stateTransitions;

        /// <summary>Raised on significant state changes (trigger engage/disengage, power changes, emergencies).</summary>
        public event EventHandler<ThermalControllerEventArgs>? OnStateChange;

        /// <summary>Raised after each step completes. Used by the dashboard data provider.</summary>
        public event EventHandler<StepEventArgs>? OnStep;

        public record StepEventArgs(uint Temperature, double Derivative, bool IsControlling);

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
            _consecutiveReadFailures = 0;
            _pidCycles = 0;
            _stateTransitions = 0;
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
        public void Step(double? simulatedTemp = null, double dt = -1)
        {
            // Use config default when caller doesn't specify dt
            if (dt < 0) dt = _config.DefaultDt;
            if (dt <= 0) dt = _config.MinimumDt;

            uint currentTemp = simulatedTemp.HasValue
                ? (uint)Math.Round(simulatedTemp.Value)
                : SafeGetTemperature();

            // 1. EMERGENCY SAFETY
            if (currentTemp >= _config.EmergencyTemp)
            {
                SetPowerLimit(_device.MinPower);
                RaiseEvent(currentTemp, _currentPowerLimit, _isControlling,
                    ControllerEventType.Emergency,
                    $"[EMERGENCY] Temp hit {currentTemp}C! Forcing minimum power limit ({_device.MinPower}W).");
                _lastKnownGoodTemp = currentTemp;
                OnStep?.Invoke(this, new StepEventArgs(currentTemp, 0, _isControlling));
                return;
            }

            // Calculate derivative
            double derivative = (currentTemp - _lastKnownGoodTemp) / dt;

            // 2. TRIGGER EVALUATION
            if (!_isControlling)
            {
                TriggerResult trigger = _triggerEvaluator.Evaluate(currentTemp, derivative, _isControlling);

                if (trigger != TriggerResult.None)
                    {
                    _isControlling = true;
                    _stateTransitions++;
                    _pidController.Reset();

                    string msg = trigger == TriggerResult.Predictive
                        ? $"[PREDICTIVE TRIGGER] Temp {currentTemp}C rising at {derivative:F1}C/s. Engaging early."
                        : $"[SAFETY TRIGGER] Temp hit {currentTemp}C. Engaging PID control.";

                    RaiseEvent(currentTemp, _currentPowerLimit, _isControlling, ControllerEventType.Trigger, msg);
                }
            }

            // 3. PID CONTROL
            if (_isControlling)
            {
                _pidCycles++;
                int newPower = _pidController.CalculatePowerLimit(currentTemp, _lastKnownGoodTemp, dt);

                if (newPower != _currentPowerLimit)
                {
                    SetPowerLimit(newPower);
                    RaiseEvent(currentTemp, _currentPowerLimit, _isControlling,
                        ControllerEventType.Info,
                        $"[{DateTime.Now:HH:mm:ss}] Temp: {currentTemp}C | Target: {_config.TargetTemp}C | Limiting Power to: {newPower}W");
                }

                // Exit condition: temp sufficiently below target AND power fully restored
                if (currentTemp <= _config.TargetTemp - _config.ExitHysteresis && _currentPowerLimit >= _device.MaxPower)
                    {
                    _isControlling = false;
                    _stateTransitions++;
                    _pidController.Reset();
                    RaiseEvent(currentTemp, _currentPowerLimit, _isControlling,
                        ControllerEventType.Stable,
                        $"[STABLE] Temp settled at {currentTemp}C. Full power restored. Returning to idle monitoring.");
                }
            }

            _lastKnownGoodTemp = currentTemp;

            // Raise step event for dashboard data provider
            OnStep?.Invoke(this, new StepEventArgs(currentTemp, derivative, _isControlling));
        }

        private void MainLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                Step();

                int sleepMs = _isControlling ? _config.ControllingSleepMs : _config.IdleSleepMs;
                Thread.Sleep(sleepMs);
            }
        }

        /// <summary>
        /// Reads temperature from the device. If the device fails to respond
        /// for MaxConsecutiveReadFailures in a row, returns the last known good
        /// temperature instead of continuing to hammer the failing device.
        /// </summary>
        private uint SafeGetTemperature()
        {
            if (_device.GetTemperature(out uint temp))
            {
                _consecutiveReadFailures = 0;
                _lastKnownGoodTemp = temp;
                return temp;
            }
            else
            {
                _consecutiveReadFailures++;

                if (_consecutiveReadFailures >= _config.MaxConsecutiveReadFailures)
                {
                    RaiseEvent(_lastKnownGoodTemp, _currentPowerLimit, _isControlling,
                        ControllerEventType.Warning,
                        $"Warning: Temperature read failed {_consecutiveReadFailures} consecutive times. Using last known temp ({_lastKnownGoodTemp}C).");
                    return _lastKnownGoodTemp;
                }

                return _lastKnownGoodTemp;
            }
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
                RaiseEvent(_lastKnownGoodTemp, _currentPowerLimit, _isControlling,
                    ControllerEventType.Warning,
                    $"Warning: Failed to set power limit to {watts}W: {error}");
            }
        }

        private void RestoreMaxPower()
        {
            _device.SetPowerLimit(_device.MaxPower, out _);
            _currentPowerLimit = _device.MaxPower;
        }

        private void RaiseEvent(uint temperature, int powerLimit, bool isControlling, ControllerEventType eventType, string? message)
        {
            OnStateChange?.Invoke(this, new ThermalControllerEventArgs(temperature, powerLimit, isControlling, eventType, message));
        }
    }
}