namespace GpuThermalController.Notifications;

/// <summary>
/// Defines the severity levels for toast notifications.
/// </summary>
public enum NotificationSeverity
{
    /// <summary>
    /// Informational notification (silent, no sound).
    /// </summary>
    Info,

    /// <summary>
    /// Warning notification (plays warning sound).
    /// </summary>
    Warning,

    /// <summary>
    /// Error notification (plays error sound).
    /// </summary>
    Error,

    /// <summary>
    /// Alert notification (plays urgent sound, red badge).
    /// </summary>
    Alert
}