using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Spectre.Console;

namespace GpuThermalController;

/// <summary>
/// Represents a pending error or warning entry.
/// </summary>
public record ErrorEntry(string Level, string Message, DateTime Timestamp = default)
{
    public ErrorEntry(string level, string message)
        : this(level, message, DateTime.UtcNow) { }
}

/// <summary>
/// Centralized error/warning output using Spectre.Console.
/// Dual-channel: writes to stderr immediately, and queues entries for the dashboard
/// to render inside the Live display so they are not cleared by the next frame.
/// </summary>
public static class ErrorConsole
{
    private static readonly ConcurrentQueue<ErrorEntry> _pending = new();
    private const int MaxPending = 50;

    /// <summary>
    /// Creates an IAnsiConsole targeting the current Console.Error.
    /// Created per-call so tests can redirect Console.Error before invoking.
    /// </summary>
    private static IAnsiConsole CreateConsole()
        => AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Console.Error)
        });

    /// <summary>
    /// Writes a warning message with a yellow icon and bold prefix.
    /// Also queues the entry for the dashboard to render.
    /// </summary>
    public static void Warning(string message)
    {
        var entry = new ErrorEntry("WARN", message);
        _pending.Enqueue(entry);
        TrimPending();
        CreateConsole().MarkupLine($"[bold yellow]⚠ WARN:[/] {message}");
    }

    /// <summary>
    /// Writes an error message with a red icon and bold prefix.
    /// Also queues the entry for the dashboard to render.
    /// </summary>
    public static void Error(string message)
    {
        var entry = new ErrorEntry("ERROR", message);
        _pending.Enqueue(entry);
        TrimPending();
        CreateConsole().MarkupLine($"[bold red]✕ ERROR:[/] {message}");
    }

    /// <summary>
    /// Drains all pending error/warning entries from the queue.
    /// Called by the dashboard during each render frame.
    /// </summary>
    public static List<ErrorEntry> DrainPending()
    {
        var result = new List<ErrorEntry>();
        while (_pending.TryDequeue(out var entry))
            result.Add(entry);
        return result;
    }

    private static void TrimPending()
    {
        while (_pending.Count > MaxPending)
            _pending.TryDequeue(out _);
    }
}
