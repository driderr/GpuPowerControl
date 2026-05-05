using System;

namespace GpuThermalController.Dashboard;

/// <summary>
/// Decouples the controller from display/publishing consumers.
/// Dashboard, JSON publisher, and future web server all consume this interface.
/// </summary>
public interface IDashboardDataProvider : IDisposable
{
    /// <summary>Raised when a new metrics snapshot is available.</summary>
    event EventHandler<MetricsSnapshot>? MetricsUpdated;

    /// <summary>Raised when a significant event is logged.</summary>
    event EventHandler<DashboardEvent>? EventLogged;

    /// <summary>The static configuration (never changes after creation).</summary>
    DashboardConfig Config { get; }

    /// <summary>The most recent dynamic metrics snapshot.</summary>
    MetricsSnapshot Current { get; }

    /// <summary>Get the last N metrics snapshots from the ring buffer.</summary>
    IReadOnlyList<MetricsSnapshot> GetHistory(int count);

    /// <summary>Get the last N events from the ring buffer.</summary>
    IReadOnlyList<DashboardEvent> GetEvents(int count);
}