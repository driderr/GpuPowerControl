using System;
using System.IO;
using System.Threading;

namespace GpuThermalController.Dashboard;

/// <summary>
/// Handles keyboard shortcuts for the dashboard.
/// Runs on a dedicated thread that checks for key presses without blocking.
/// </summary>
public class KeyHandler : IDisposable
{
    private readonly object _lock = new();
    private volatile bool _isRunning;
    private Thread? _keyThread;

    /// <summary>Raised when the user requests to quit.</summary>
    public event Action? QuitRequested;

    /// <summary>Raised when the user toggles event log visibility.</summary>
    public event Action? ToggleLogRequested;

    /// <summary>Raised when the user toggles JSON publishing.</summary>
    public event Action? ToggleJsonRequested;

    /// <summary>Raised when the user requests CSV export.</summary>
    public event Action? ExportCsvRequested;

    /// <summary>Raised when the user toggles config display.</summary>
    public event Action? ToggleConfigRequested;

    /// <summary>Raised when the user requests a test error output (for diagnostics).</summary>
    public event Action? TestErrorRequested;

    public void Start()
    {
        _isRunning = true;
        _keyThread = new Thread(() =>
        {
            while (_isRunning)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).Key;
                    switch (key)
                    {
                        case ConsoleKey.Q:
                            QuitRequested?.Invoke();
                            break;
                        case ConsoleKey.L:
                            ToggleLogRequested?.Invoke();
                            break;
                        case ConsoleKey.J:
                            ToggleJsonRequested?.Invoke();
                            break;
                        case ConsoleKey.E:
                            ExportCsvRequested?.Invoke();
                            break;
                        case ConsoleKey.H:
                            ToggleConfigRequested?.Invoke();
                            break;
                        case ConsoleKey.T:
                            TestErrorRequested?.Invoke();
                            break;
                    }
                }
                Thread.Sleep(50);
            }
        })
        {
            IsBackground = true,
            Name = "KeyHandler"
        };
        _keyThread.Start();
    }

    public void Dispose()
    {
        _isRunning = false;
        _keyThread?.Join(2000);
        _keyThread = null;
    }
}