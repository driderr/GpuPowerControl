using Spectre.Console;

namespace GpuThermalController;

/// <summary>
/// Centralized error/warning output using Spectre.Console.
/// Writes to stderr so it does not interfere with stdout pipelines.
/// </summary>
public static class ErrorConsole
{
    private static readonly IAnsiConsole _console = AnsiConsole.Create(
        new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Console.Error)
        });

    /// <summary>
    /// Writes a warning message with a yellow icon and bold prefix.
    /// </summary>
    public static void Warning(string message)
        => _console.MarkupLine($"[bold yellow]⚠ WARN:[/] {message}");

    /// <summary>
    /// Writes an error message with a red icon and bold prefix.
    /// </summary>
    public static void Error(string message)
        => _console.MarkupLine($"[bold red]✕ ERROR:[/] {message}");
}