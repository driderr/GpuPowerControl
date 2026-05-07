using System;
using System.IO;
using GpuThermalController.Dashboard;
using Xunit;

namespace GpuPowerControl.Tests;

/// <summary>
/// Verifies that ErrorConsole writes to stderr and queues entries for the dashboard.
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

    [Fact]
    public void Error_QueueAndDrain_Works()
    {
        // Arrange: redirect stderr so we don't pollute test output
        var original = Console.Error;
        var stringWriter = new StringWriter();
        Console.SetError(stringWriter);

        try
        {
            // Drain any leftover entries from previous tests
            ErrorConsole.DrainPending();

            // Act
            ErrorConsole.Error("queued error 1");
            ErrorConsole.Error("queued error 2");

            // Assert
            var drained = ErrorConsole.DrainPending();
            Assert.Equal(2, drained.Count);
            Assert.Equal("ERROR", drained[0].Level);
            Assert.Equal("queued error 1", drained[0].Message);
            Assert.Equal("ERROR", drained[1].Level);
            Assert.Equal("queued error 2", drained[1].Message);

            // Queue should be empty after drain
            var drainedAgain = ErrorConsole.DrainPending();
            Assert.Empty(drainedAgain);
        }
        finally
        {
            stringWriter.Dispose();
            Console.SetError(original);
            // Clean up any remaining entries
            ErrorConsole.DrainPending();
        }
    }

    [Fact]
    public void Warning_QueueAndDrain_Works()
    {
        // Arrange
        var original = Console.Error;
        var stringWriter = new StringWriter();
        Console.SetError(stringWriter);

        try
        {
            ErrorConsole.DrainPending();

            // Act
            ErrorConsole.Warning("queued warning");

            // Assert
            var drained = ErrorConsole.DrainPending();
            Assert.Single(drained);
            Assert.Equal("WARN", drained[0].Level);
            Assert.Equal("queued warning", drained[0].Message);
        }
        finally
        {
            stringWriter.Dispose();
            Console.SetError(original);
            ErrorConsole.DrainPending();
        }
    }

    [Fact]
    public void ErrorEntry_HasTimestamp()
    {
        // Arrange
        var original = Console.Error;
        var stringWriter = new StringWriter();
        Console.SetError(stringWriter);

        try
        {
            ErrorConsole.DrainPending();

            // Act
            ErrorConsole.Error("timestamp test");

            // Assert
            var drained = ErrorConsole.DrainPending();
            Assert.Single(drained);
            Assert.NotEqual(default(DateTime), drained[0].Timestamp);
        }
        finally
        {
            stringWriter.Dispose();
            Console.SetError(original);
            ErrorConsole.DrainPending();
        }
    }
}
