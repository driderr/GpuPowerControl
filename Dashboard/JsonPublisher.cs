using System;
using System.IO;
using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace GpuThermalController.Dashboard;

/// <summary>
/// Publishes metrics snapshots to JSON files for external consumption (web server, monitoring tools).
/// Togglable on/off to avoid SSD write wear when not needed.
/// </summary>
public class JsonPublisher : IDisposable
{
    private readonly string _metricsFile;
    private readonly string _historyFile;
    private readonly IDashboardDataProvider _provider;
    private readonly IFileSystem _fileSystem;
    private readonly object _writeLock = new();
    private volatile bool _isEnabled;
    private volatile bool _isRunning;
    private Thread? _pollThread;

    /// <summary>Whether JSON publishing is currently active.</summary>
    public bool IsEnabled => _isEnabled;

    public JsonPublisher(IDashboardDataProvider provider, IFileSystem fileSystem, string outputDirectory = "data")
    {
        _provider = provider;
        _fileSystem = fileSystem;
        _metricsFile = _fileSystem.Path.Combine(outputDirectory, "metrics.json");
        _historyFile = _fileSystem.Path.Combine(outputDirectory, "history.json");

        // Ensure output directory exists
        if (!_fileSystem.Directory.Exists(outputDirectory))
            _fileSystem.Directory.CreateDirectory(outputDirectory);
    }

    /// <summary>Start the publisher (default: disabled, toggle with Enable/Disable).</summary>
    public void Start(bool enabled = false)
    {
        _isEnabled = enabled;
        _isRunning = true;

        // Poll every 500ms to write files - decoupled from controller step rate
        _pollThread = new Thread(() =>
        {
            while (_isRunning)
            {
                Thread.Sleep(500);
                if (_isEnabled)
                    WriteFiles();
            }
        })
        {
            IsBackground = true,
            Name = "JsonPublisher"
        };
        _pollThread.Start();
    }

    /// <summary>Enable JSON publishing (start writing to disk).</summary>
    public void Enable()
    {
        _isEnabled = true;
    }

    /// <summary>Disable JSON publishing (stop writing to disk).</summary>
    public void Disable()
    {
        _isEnabled = false;
    }

    /// <summary>Toggle JSON publishing on/off.</summary>
    public void Toggle()
    {
        _isEnabled = !_isEnabled;
    }

    private void WriteFiles()
    {
        lock (_writeLock)
        {
            try
            {
                // Write current metrics snapshot
                var current = _provider.Current;
                var metricsJson = JsonSerializer.Serialize(current, JsonOptions);
                _fileSystem.File.WriteAllText(_metricsFile, metricsJson);

                // Write history (last 2400 samples)
                var history = _provider.GetHistory(2400);
                var historyJson = JsonSerializer.Serialize(history, JsonOptions);
                _fileSystem.File.WriteAllText(_historyFile, historyJson);
            }
            catch
            {
                // Silently ignore write errors - don't crash the app
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public void Dispose()
    {
        _isRunning = false;
        _pollThread?.Join(2000);
        _pollThread = null;
    }
}