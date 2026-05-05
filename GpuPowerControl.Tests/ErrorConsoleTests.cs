using System;
using System.IO;
using GpuThermalController;
using Xunit;

namespace GpuPowerControl.Tests;

/// <summary>
/// Verifies that ErrorConsole writes to stderr and includes expected content.
/// </summary>
public class ErrorConsoleTests
{
    [Fact]
    public void Error_WritesToStderr()
    {
        // Arrange: redirect Console.Error (StringWriter is a TextWriter)
        var original = Console.Error;
        var stringWriter = new StringWriter();
        Console.SetError(stringWriter);

        try
        {
            // Act
            ErrorConsole.Error("test error message");

            // Assert
            string output = stringWriter.ToString();
            Assert.Contains("ERROR", output);
            Assert.Contains("test error message", output);
        }
        finally
        {
            // Cleanup: restore original stderr
            stringWriter.Dispose();
            Console.SetError(original);
        }
    }

    [Fact]
    public void Warning_WritesToStderr()
    {
        // Arrange
        var original = Console.Error;
        var stringWriter = new StringWriter();
        Console.SetError(stringWriter);

        try
        {
            // Act
            ErrorConsole.Warning("test warning message");

            // Assert
            string output = stringWriter.ToString();
            Assert.Contains("WARN", output);
            Assert.Contains("test warning message", output);
        }
        finally
        {
            // Cleanup
            stringWriter.Dispose();
            Console.SetError(original);
        }
    }
}