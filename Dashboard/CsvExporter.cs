using System;
using System.IO;
using System.Text;

namespace GpuThermalController.Dashboard;

/// <summary>
/// Exports event log to a CSV file on demand.
/// </summary>
public static class CsvExporter
{
    /// <summary>
    /// Exports the given events to a CSV file at the specified path.
    /// </summary>
    public static void ExportToCsv(IReadOnlyList<DashboardEvent> events, string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,Temperature,PowerLimit,IsControlling,EventType,Message");

        foreach (var evt in events)
        {
            // Escape message for CSV (wrap in quotes, double any internal quotes)
            var message = evt.Message ?? "";
            var escapedMessage = "\"" + message.Replace("\"", "\"\"") + "\"";

            sb.AppendLine($"{evt.Timestamp:O},{evt.Temperature},{evt.PowerLimit},{evt.IsControlling},{evt.EventType},{escapedMessage}");
        }

        // Ensure directory exists
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(filePath, sb.ToString());
    }
}