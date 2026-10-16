using System;
using System.IO;
using GpuThermalController.Core;
using Xunit;

namespace GpuPowerControl.Tests;

public class CrashLoggerTests : IDisposable
{
    private readonly string _originalPath = CrashLogger.LogFilePath;
    private readonly string _testPath;

    public CrashLoggerTests()
    {
        _testPath = Path.Combine(Path.GetTempPath(), $"crash-logger-test-{Guid.NewGuid():N}.txt");
        CrashLogger.LogFilePath = _testPath;
    }

    public void Dispose()
    {
        CrashLogger.LogFilePath = _originalPath;
        try { if (File.Exists(_testPath)) File.Delete(_testPath); } catch { /* best effort */ }
    }

    [Fact]
    public void Log_AppendsContextAndExceptionDetails()
    {
        CrashLogger.Log(new InvalidOperationException("boom"), "Test context");

        Assert.True(File.Exists(_testPath));
        string content = File.ReadAllText(_testPath);
        Assert.Contains("Test context", content);
        Assert.Contains("boom", content);
    }

    [Fact]
    public void Log_WithNullException_WritesPlaceholder()
    {
        CrashLogger.Log(null, "Null context");

        string content = File.ReadAllText(_testPath);
        Assert.Contains("Null context", content);
        Assert.Contains("(no exception details available)", content);
    }

    [Fact]
    public void Log_NeverThrowsWhenPathIsInvalid()
    {
        CrashLogger.LogFilePath = Path.Combine(Path.GetTempPath(), "no-such-dir-xyz", "sub", "crash.txt");

        // If this throws, the test fails.
        CrashLogger.Log(new Exception("x"), "ctx");
    }
}