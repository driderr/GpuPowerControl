using System;
using GpuThermalController.Core;

namespace GpuThermalController.Dashboard;

/// <summary>
/// A single event log entry. Serializable for CSV/JSON export.
/// </summary>
public record DashboardEvent(
    DateTime Timestamp,
    double Temperature,
    int PowerLimit,
    bool IsControlling,
    ControllerEventType EventType,
    string? Message
);