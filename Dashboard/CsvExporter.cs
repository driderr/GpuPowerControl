using System;
using System.IO;
using System.IO.Abstractions;
using System.Text;

namespace GpuThermalController.Dashboard;

/// <summary>
/// Exports event log to a CSV file on demand.
/// </summary>
public class CsvExporter
{
    private readonly IFileSystem _fileSystem;

    public CsvExporter(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Exports the given events to a CSV file at the specified path.
    /// </summary>
    public void ExportToCsv(IReadOnlyList<DashboardEvent> events, string filePath)
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
        var dir = _fileSystem.Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !_fileSystem.Directory.Exists(dir))
            _fileSystem.Directory.CreateDirectory(dir);

        _fileSystem.File.WriteAllText(filePath, sb.ToString());
    }
}
