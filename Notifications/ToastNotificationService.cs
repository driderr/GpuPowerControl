using System;
using System.Xml;
using Microsoft.Win32;
using CommunityToolkit.WinUI.Notifications;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;

namespace GpuThermalController.Notifications;

/// <summary>
/// Service for displaying Windows toast notifications using Community Toolkit.
/// Uses ToastNotificationManagerCompat for console application compatibility.
/// </summary>
public static class ToastNotificationService
{
    private const string AppUserModelId = "com.gpupowercontrol.app";
    private static bool _initialized = false;
    private static NotificationConfig _config = new();

    /// <summary>
    /// Initialize the notification service.
    /// Registers AppUserModelId in the Windows Registry for console app compatibility.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        // Register AppUserModelId in the Windows Registry
        const string regPath = @"SOFTWARE\Classes\AppUserModelId";
        using (var key = Registry.CurrentUser.CreateSubKey(regPath + "\\" + AppUserModelId, RegistryKeyPermissionCheck.Default))
        {
            string? existing = (string?)key.GetValue("");
            if (existing != AppUserModelId)
            {
                key.SetValue("", AppUserModelId);
            }
        }
    }

    /// <summary>
    /// Set the notification configuration.
    /// </summary>
    public static void SetConfig(NotificationConfig config)
    {
        _config = config ?? new NotificationConfig();
    }

    /// <summary>
    /// Displays a toast notification with the specified title, message, and severity.
    /// </summary>
    public static void ShowToast(string title, string message,
                                  NotificationSeverity severity = NotificationSeverity.Info)
    {
        if (!_initialized) Initialize();
        if (!_config.Enabled) return;

        var toastContent = new ToastContentBuilder()
            .AddText(title)
            .AddText(message)
            .AddButton("Dismiss", ToastActivationType.Protocol, "dismiss")
            .GetToastContent();

        var xml = toastContent.GetXml();
        var toast = new ToastNotification(xml);
        // Set a unique tag so Windows does not suppress this toast as a duplicate
        toast.Tag = AppUserModelId + ":" + Guid.NewGuid().ToString("N");

        var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
        if (notifier != null)
            notifier.Show(toast);
    }

    /// <summary>
    /// Displays a toast notification with safe error handling.
    /// Falls back to console output if toast fails.
    /// </summary>
    public static void ShowToastSafe(string title, string message,
                                      NotificationSeverity severity = NotificationSeverity.Info)
    {
        try
        {
            ShowToast(title, message, severity);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TOAST FAILED] {title}: {message} - {ex.Message}");
        }
    }

    /// <summary>
    /// Convenience method for Info notifications (silent).
    /// </summary>
    public static void ShowInfoToast(string title, string message)
    {
        ShowToastSafe(title, message, NotificationSeverity.Info);
    }

    /// <summary>
    /// Convenience method for Warning notifications.
    /// </summary>
    public static void ShowWarningToast(string title, string message)
    {
        ShowToastSafe(title, message, NotificationSeverity.Warning);
    }

    /// <summary>
    /// Convenience method for Error notifications.
    /// </summary>
    public static void ShowErrorToast(string title, string message)
    {
        ShowToastSafe(title, message, NotificationSeverity.Error);
    }

    /// <summary>
    /// Convenience method for Alert notifications.
    /// </summary>
    public static void ShowAlertToast(string title, string message)
    {
        ShowToastSafe(title, message, NotificationSeverity.Alert);
    }

    /// <summary>
    /// Displays a toast with rich content including hero image.
    /// </summary>
    public static void ShowRichToast(string title, string message, Uri? heroImageUri = null,
                                      NotificationSeverity severity = NotificationSeverity.Info)
    {
        if (!_initialized) Initialize();
        if (!_config.Enabled) return;

        var builder = new ToastContentBuilder()
            .AddText(title)
            .AddText(message);

        if (heroImageUri != null)
            builder.AddHeroImage(heroImageUri);

        builder.AddButton("Dismiss", ToastActivationType.Protocol, "dismiss");

        var toastContent = builder.GetToastContent();

        var xml = toastContent.GetXml();
        var toast = new ToastNotification(xml);
        toast.Tag = AppUserModelId + ":" + Guid.NewGuid().ToString("N");
        var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
        if (notifier != null)
            notifier.Show(toast);
    }

    /// <summary>
    /// Clear all notification history.
    /// </summary>
    public static void ClearNotificationHistory()
    {
        ToastNotificationManagerCompat.History.Clear();
    }
}