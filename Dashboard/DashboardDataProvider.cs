using System;
using System.Collections.Generic;
using System.Linq;
using GpuThermalController.Core;
using GpuThermalController.Interfaces;

namespace GpuThermalController.Dashboard;

/// <summary>
/// Collects metrics from the ThermalController via events, maintains ring buffers for history,
/// and exposes data via IDashboardDataProvider for any consumer (console, JSON, web).
/// </summary>
public class DashboardDataProvider : IDashboardDataProvider
{
    private readonly object _lock = new();
    private readonly int _historyCapacity;
    private readonly int _eventCapacity;

    private readonly Queue<MetricsSnapshot> _history = new();
    private readonly Queue<DashboardEvent> _events = new();

    private MetricsSnapshot _current;
    private DashboardConfig _config;

    private readonly DateTime _startTime;
    private readonly IGpuDevice _device;
    private readonly ThermalControllerConfig _controllerConfig;

    public event EventHandler<MetricsSnapshot>? MetricsUpdated;
    public event EventHandler<DashboardEvent>? EventLogged;

    public DashboardConfig Config => _config;
    public MetricsSnapshot Current => _current;

    public DashboardDataProvider(
        IGpuDevice device,
        ThermalControllerConfig controllerConfig,
        int historyCapacity = 2400,
        int eventCapacity = 500)
    {
        _device = device;
        _controllerConfig = controllerConfig;
        _historyCapacity = historyCapacity;
        _eventCapacity = eventCapacity;
        _startTime = DateTime.UtcNow;

        // Build static config once
        _config = new DashboardConfig(
            GpuName: device.Name,
            MinPower: device.MinPower,
            MaxPower: device.MaxPower,
            TargetTemp: controllerConfig.TargetTemp,
            TriggerTemp: controllerConfig.TriggerTemp,
            EmergencyTemp: controllerConfig.EmergencyTemp,
            Kp: controllerConfig.Kp,
            Ki: controllerConfig.Ki,
            Kd: controllerConfig.Kd
        );

        // Initial empty snapshot
        _current = new MetricsSnapshot(
            Timestamp: DateTime.UtcNow,
            Uptime: TimeSpan.Zero,
            Temperature: controllerConfig.TargetTemp,
            CurrentPowerLimit: device.MaxPower,
            IsControlling: false,
            Derivative: 0.0,
            PidCycles: 0,
            StateTransitions: 0,
            ReadFailures: 0,
            PollingIntervalMs: controllerConfig.IdleSleepMs,
            PidP: 0,
            PidI: 0,
            PidD: 0,
            PidIntegral: 0,
            EventType: null,
            EventMessage: null
        );
    }

    /// <summary>
    /// Subscribe to the ThermalController's events.
    /// </summary>
    public void Subscribe(ThermalController controller)
    {
        controller.OnStep += OnControllerStep;
        controller.OnStateChange += OnControllerEvent;
    }

    private void OnControllerStep(object? sender, ThermalController.StepEventArgs e)
    {
        if (sender is not ThermalController controller) return;

        var components = controller.PidController.LastComponents;
        var snapshot = new MetricsSnapshot(
            Timestamp: DateTime.UtcNow,
            Uptime: DateTime.UtcNow - _startTime,
            Temperature: e.Temperature,
            CurrentPowerLimit: controller.CurrentPowerLimit,
            IsControlling: e.IsControlling,
            Derivative: e.Derivative,
            PidCycles: controller.PidCycles,
            StateTransitions: controller.StateTransitions,
            ReadFailures: controller.ConsecutiveReadFailures,
            PollingIntervalMs: e.IsControlling
                ? _controllerConfig.ControllingSleepMs
                : _controllerConfig.IdleSleepMs,
            PidP: components?.P ?? 0,
            PidI: components?.I ?? 0,
            PidD: components?.D ?? 0,
            PidIntegral: controller.PidController.Integral,
            EventType: null,
            EventMessage: null
        );

        lock (_lock)
        {
            _current = snapshot;
            _history.Enqueue(snapshot);
            if (_history.Count > _historyCapacity)
                _history.Dequeue();
        }

        MetricsUpdated?.Invoke(this, snapshot);
    }

    private void OnControllerEvent(object? sender, ThermalControllerEventArgs args)
    {
        var evt = new DashboardEvent(
            Timestamp: DateTime.UtcNow,
            Temperature: args.Temperature,
            PowerLimit: args.PowerLimit,
            IsControlling: args.IsControlling,
            EventType: args.EventType,
            Message: args.Message
        );

        lock (_lock)
        {
            _events.Enqueue(evt);
            if (_events.Count > _eventCapacity)
                _events.Dequeue();

            // Also update the current snapshot with event info
            _current = _current with
            {
                EventType = args.EventType,
                EventMessage = args.Message
            };
        }

        EventLogged?.Invoke(this, evt);
    }

    public IReadOnlyList<MetricsSnapshot> GetHistory(int count)
    {
        lock (_lock)
        {
            return _history.TakeLast(count).ToList();
        }
    }

    public IReadOnlyList<DashboardEvent> GetEvents(int count)
    {
        lock (_lock)
        {
            return _events.TakeLast(count).ToList();
        }
    }

    /// <summary>
    /// Updates the PID coefficients in the dashboard config snapshot.
    /// Call this when the user adjusts Kp/Ki/Kd from the dashboard.
    /// </summary>
    public void UpdatePidCoefficients(double kp, double ki, double kd)
    {
        lock (_lock)
        {
            _config = _config with { Kp = kp, Ki = ki, Kd = kd };
        }
    }

    public void Dispose()
    {
        // No unmanaged resources to dispose
    }
}