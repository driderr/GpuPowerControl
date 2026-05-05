using Spectre.Console;

namespace GpuThermalController;

/// <summary>
/// Centralized error/warning output using Spectre.Console.
/// Writes to stderr so it does not interfere with stdout pipelines.
/// </summary>
public static class ErrorConsole
{
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
    /// </summary>
    public static void Warning(string message)
        => CreateConsole().MarkupLine($"[bold yellow]⚠ WARN:[/] {message}");

    /// <summary>
    /// Writes an error message with a red icon and bold prefix.
    /// </summary>
    public static void Error(string message)
        => CreateConsole().MarkupLine($"[bold red]✕ ERROR:[/] {message}");
}
