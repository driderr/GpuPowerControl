using System;
using GpuThermalController.Core;

namespace GpuThermalController.Dashboard;

/// <summary>
/// Dynamic metrics snapshot updated on every controller step.
/// Contains only values that change during runtime.
/// </summary>
public record MetricsSnapshot(
    DateTime Timestamp,
    TimeSpan Uptime,
    double Temperature,
    int CurrentPowerLimit,
    bool IsControlling,
    double Derivative,
    int PidCycles,
    int StateTransitions,
    int ReadFailures,
    double PollingIntervalMs,
    double PidP,
    double PidI,
    double PidD,
    double PidIntegral,
    ControllerEventType? EventType,
    string? EventMessage
);