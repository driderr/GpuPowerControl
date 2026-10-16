using System;
using System.IO;

namespace GpuThermalController.Core;

/// <summary>
/// Appends fatal/unexpected exception details to a persistent log file so crashes can be
/// diagnosed even when console output is unavailable (e.g. the Live dashboard owns the console).
/// The log is written next to the executable and is safe to attach to GitHub issues.
/// </summary>
public static class CrashLogger
{
    private static readonly object _lock = new();

    /// <summary>Path of the crash log file (next to the executable). Settable for tests.</summary>
    public static string LogFilePath { get; internal set; } =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash-log.txt");

    /// <summary>
    /// Appends an exception entry to the crash log. Never throws.
    /// </summary>
    public static void Log(Exception? exception, string context)
    {
        try
        {
            lock (_lock)
            {
                string entry =
                    $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ===\n" +
                    $"Context: {context}\n" +
                    (exception != null ? exception.ToString() : "(no exception details available)") +
                    "\n\n";
                File.AppendAllText(LogFilePath, entry);
            }
        }
        catch
        {
            // Crash logging must never throw - there is nowhere else to report it.
        }
    }
}