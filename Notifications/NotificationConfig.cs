namespace GpuThermalController.Notifications;

/// <summary>
/// Configuration for toast notification behavior.
/// </summary>
public class NotificationConfig
{
    /// <summary>
    /// Whether toast notifications are enabled. Default: false (to prevent test spam).
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// AppUserModelId for the application.
    /// </summary>
    public string AppUserModelId { get; set; } = "com.gpupowercontrol.app";

    /// <summary>
    /// Whether to play audio for Info-level notifications. Default: false (silent).
    /// </summary>
    public bool InfoSoundEnabled { get; set; } = false;

    /// <summary>
    /// Custom sound path for non-Info notifications.
    /// If empty, uses Windows default notification sound.
    /// </summary>
    public string WarningSoundPath { get; set; } = "";

    /// <summary>
    /// Maximum number of toasts that can be queued. Default: 10.
    /// </summary>
    public int MaxQueuedToasts { get; set; } = 10;
}