using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using GpuThermalController.Core;
using GpuThermalController.Dashboard;
using Xunit;

namespace GpuPowerControl.Tests;

/// <summary>
/// Tests for CsvExporter class using in-memory MockFileSystem (zero disk I/O).
/// </summary>
public class CsvExporterTests
{
    private MockFileSystem _mockFs;
    private CsvExporter _exporter;

    public CsvExporterTests()
    {
        _mockFs = new MockFileSystem();
        _exporter = new CsvExporter(_mockFs);
    }

    private DashboardEvent CreateEvent(
        DateTime? timestamp = null,
        double temperature = 75.0,
        int powerLimit = 500,
        bool isControlling = false,
        ControllerEventType eventType = ControllerEventType.Info,
        string? message = null)
    {
        return new DashboardEvent(
            timestamp ?? DateTime.Now,
            temperature,
            powerLimit,
            isControlling,
            eventType,
            message
        );
    }

    // --- Happy Path Tests ---

    [Fact]
    public void ExportToCsv_SingleEvent_ProducesValidCsv()
    {
        // Arrange
        var events = new List<DashboardEvent> { CreateEvent(message: "System started") };
        var filePath = "temp/export_test_001.csv";

        // Act
        _exporter.ExportToCsv(events, filePath);

        // Assert
        Assert.True(_mockFs.File.Exists(filePath));
        var lines = _mockFs.File.ReadAllLines(filePath);
        Assert.Equal(2, lines.Length); // header + 1 data line
        Assert.Equal("Timestamp,Temperature,PowerLimit,IsControlling,EventType,Message", lines[0]);
        Assert.Contains("System started", lines[1]);
    }

    [Fact]
    public void ExportToCsv_MultipleEvents_ProducesCorrectLineCount()
    {
        // Arrange
        var events = new List<DashboardEvent>
        {
            CreateEvent(temperature: 60, message: "Cool"),
            CreateEvent(temperature: 80, message: "Hot"),
            CreateEvent(temperature: 95, message: "Emergency")
        };
        var filePath = "temp/export_test_002.csv";

        // Act
        _exporter.ExportToCsv(events, filePath);

        // Assert
        var lines = _mockFs.File.ReadAllLines(filePath);
        Assert.Equal(4, lines.Length); // header + 3 data lines
    }

    // --- Edge Cases ---

    [Fact]
    public void ExportToCsv_EmptyEvents_OnlyHeader()
    {
        // Arrange
        var events = new List<DashboardEvent>();
        var filePath = "temp/export_test_003.csv";

        // Act
        _exporter.ExportToCsv(events, filePath);

        // Assert
        var lines = _mockFs.File.ReadAllLines(filePath);
        Assert.Single(lines);
        Assert.Equal("Timestamp,Temperature,PowerLimit,IsControlling,EventType,Message", lines[0]);
    }

    [Fact]
    public void ExportToCsv_MessageWithQuotes_IsEscaped()
    {
        // Arrange
        var events = new List<DashboardEvent>
        {
            CreateEvent(message: "He said \"hello\"")
        };
        var filePath = "temp/export_test_004.csv";

        // Act
        _exporter.ExportToCsv(events, filePath);

        // Assert
        var lines = _mockFs.File.ReadAllLines(filePath);
        var dataLine = lines[1];
        Assert.Contains("\"He said \"\"hello\"\"\"", dataLine);
    }

    [Fact]
    public void ExportToCsv_MessageWithCommas_IsQuoted()
    {
        // Arrange
        var events = new List<DashboardEvent>
        {
            CreateEvent(message: "temp high, reducing power")
        };
        var filePath = "temp/export_test_005.csv";

        // Act
        _exporter.ExportToCsv(events, filePath);

        // Assert
        var lines = _mockFs.File.ReadAllLines(filePath);
        var dataLine = lines[1];
        Assert.Contains("\"temp high, reducing power\"", dataLine);
    }

    [Fact]
    public void ExportToCsv_NullMessage_HandlesGracefully()
    {
        // Arrange
        var events = new List<DashboardEvent>
        {
            CreateEvent(message: null)
        };
        var filePath = "temp/export_test_006.csv";

        // Act
        _exporter.ExportToCsv(events, filePath);

        // Assert
        var lines = _mockFs.File.ReadAllLines(filePath);
        var dataLine = lines[1];
        Assert.Contains("\"\"", dataLine);
    }

    [Fact]
    public void ExportToCsv_CreatesDirectoryIfNotExists()
    {
        // Arrange
        var filePath = "new_dir/output.csv";
        var events = new List<DashboardEvent> { CreateEvent() };
        Assert.False(_mockFs.Directory.Exists("new_dir"));

        // Act
        _exporter.ExportToCsv(events, filePath);

        // Assert
        Assert.True(_mockFs.Directory.Exists("new_dir"));
        Assert.True(_mockFs.File.Exists(filePath));
    }

    [Fact]
    public void ExportToCsv_IsControlling_True()
    {
        // Arrange
        var events = new List<DashboardEvent>
        {
            CreateEvent(isControlling: true, temperature: 82, powerLimit: 350, eventType: ControllerEventType.Trigger)
        };
        var filePath = "temp/export_test_009.csv";

        // Act
        _exporter.ExportToCsv(events, filePath);

        // Assert
        var lines = _mockFs.File.ReadAllLines(filePath);
        var dataLine = lines[1];
        Assert.Contains(",True,", dataLine);
    }

    [Fact]
    public void ExportToCsv_IsControlling_False()
    {
        // Arrange
        var events = new List<DashboardEvent>
        {
            CreateEvent(isControlling: false)
        };
        var filePath = "temp/export_test_010.csv";

        // Act
        _exporter.ExportToCsv(events, filePath);

        // Assert
        var lines = _mockFs.File.ReadAllLines(filePath);
        var dataLine = lines[1];
        Assert.Contains(",False,", dataLine);
    }

    [Fact]
    public void ExportToCsv_EventType_IsSerialized()
    {
        // Arrange
        var events = new List<DashboardEvent>
        {
            CreateEvent(eventType: ControllerEventType.Warning)
        };
        var filePath = "temp/export_test_011.csv";

        // Act
        _exporter.ExportToCsv(events, filePath);

        // Assert
        var lines = _mockFs.File.ReadAllLines(filePath);
        var dataLine = lines[1];
        Assert.Contains(",Warning,", dataLine);
    }

    [Fact]
    public void ExportToCsv_Timestamp_IsIsoFormat()
    {
        // Arrange
        var specificTime = new DateTime(2025, 3, 15, 10, 30, 0);
        var events = new List<DashboardEvent>
        {
            CreateEvent(timestamp: specificTime)
        };
        var filePath = "temp/export_test_012.csv";

        // Act
        _exporter.ExportToCsv(events, filePath);

        // Assert
        var lines = _mockFs.File.ReadAllLines(filePath);
        var dataLine = lines[1];
        Assert.Contains("2025-03-15T10:30:00", dataLine);
    }
}