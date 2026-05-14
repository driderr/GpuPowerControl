using System.Collections.Generic;
using System.IO;
using System.Linq;
using GpuThermalController.Core;
using GpuThermalController.Dashboard;
using Xunit;

namespace GpuPowerControl.Tests;

/// <summary>
/// Tests for CsvExporter static class.
/// </summary>
public class CsvExporterTests
{
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
        var tempFile = Path.Combine(Path.GetTempPath(), $"csv_export_test_{Guid.NewGuid():N}.csv");

        try
        {
            // Act
            CsvExporter.ExportToCsv(events, tempFile);

            // Assert
            Assert.True(File.Exists(tempFile));
            var lines = File.ReadAllLines(tempFile);
            Assert.Equal(2, lines.Length); // header + 1 data line
            Assert.Equal("Timestamp,Temperature,PowerLimit,IsControlling,EventType,Message", lines[0]);
            Assert.Contains("System started", lines[1]);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
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
        var tempFile = Path.Combine(Path.GetTempPath(), $"csv_export_test_{Guid.NewGuid():N}.csv");

        try
        {
            // Act
            CsvExporter.ExportToCsv(events, tempFile);

            // Assert
            var lines = File.ReadAllLines(tempFile);
            Assert.Equal(4, lines.Length); // header + 3 data lines
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // --- Edge Cases ---

    [Fact]
    public void ExportToCsv_EmptyEvents_OnlyHeader()
    {
        // Arrange
        var events = new List<DashboardEvent>();
        var tempFile = Path.Combine(Path.GetTempPath(), $"csv_export_test_{Guid.NewGuid():N}.csv");

        try
        {
            // Act
            CsvExporter.ExportToCsv(events, tempFile);

            // Assert
            var lines = File.ReadAllLines(tempFile);
            Assert.Single(lines);
            Assert.Equal("Timestamp,Temperature,PowerLimit,IsControlling,EventType,Message", lines[0]);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ExportToCsv_MessageWithQuotes_IsEscaped()
    {
        // Arrange
        var events = new List<DashboardEvent>
        {
            CreateEvent(message: "He said \"hello\"")
        };
        var tempFile = Path.Combine(Path.GetTempPath(), $"csv_export_test_{Guid.NewGuid():N}.csv");

        try
        {
            // Act
            CsvExporter.ExportToCsv(events, tempFile);

            // Assert
            var lines = File.ReadAllLines(tempFile);
            var dataLine = lines[1];
            // The message should be wrapped in quotes, with internal quotes doubled
            Assert.Contains("\"He said \"\"hello\"\"\"", dataLine);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ExportToCsv_MessageWithCommas_IsQuoted()
    {
        // Arrange
        var events = new List<DashboardEvent>
        {
            CreateEvent(message: "temp high, reducing power")
        };
        var tempFile = Path.Combine(Path.GetTempPath(), $"csv_export_test_{Guid.NewGuid():N}.csv");

        try
        {
            // Act
            CsvExporter.ExportToCsv(events, tempFile);

            // Assert
            var lines = File.ReadAllLines(tempFile);
            var dataLine = lines[1];
            Assert.Contains("\"temp high, reducing power\"", dataLine);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ExportToCsv_NullMessage_HandlesGracefully()
    {
        // Arrange
        var events = new List<DashboardEvent>
        {
            CreateEvent(message: null)
        };
        var tempFile = Path.Combine(Path.GetTempPath(), $"csv_export_test_{Guid.NewGuid():N}.csv");

        try
        {
            // Act
            CsvExporter.ExportToCsv(events, tempFile);

            // Assert
            var lines = File.ReadAllLines(tempFile);
            var dataLine = lines[1];
            Assert.Contains("\"\"", dataLine);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ExportToCsv_CreatesDirectoryIfNotExists()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"csv_test_dir_{Guid.NewGuid():N}");
        var tempFile = Path.Combine(tempDir, "output.csv");
        var events = new List<DashboardEvent> { CreateEvent() };

        try
        {
            // Act
            CsvExporter.ExportToCsv(events, tempFile);

            // Assert
            Assert.True(Directory.Exists(tempDir));
            Assert.True(File.Exists(tempFile));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ExportToCsv_IsControlling_True()
    {
        // Arrange
        var events = new List<DashboardEvent>
        {
            CreateEvent(isControlling: true, temperature: 82, powerLimit: 350, eventType: ControllerEventType.Trigger)
        };
        var tempFile = Path.Combine(Path.GetTempPath(), $"csv_export_test_{Guid.NewGuid():N}.csv");

        try
        {
            // Act
            CsvExporter.ExportToCsv(events, tempFile);

            // Assert
            var lines = File.ReadAllLines(tempFile);
            var dataLine = lines[1];
            Assert.Contains(",True,", dataLine);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ExportToCsv_IsControlling_False()
    {
        // Arrange
        var events = new List<DashboardEvent>
        {
            CreateEvent(isControlling: false)
        };
        var tempFile = Path.Combine(Path.GetTempPath(), $"csv_export_test_{Guid.NewGuid():N}.csv");

        try
        {
            // Act
            CsvExporter.ExportToCsv(events, tempFile);

            // Assert
            var lines = File.ReadAllLines(tempFile);
            var dataLine = lines[1];
            Assert.Contains(",False,", dataLine);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ExportToCsv_EventType_IsSerialized()
    {
        // Arrange
        var events = new List<DashboardEvent>
        {
            CreateEvent(eventType: ControllerEventType.Warning)
        };
        var tempFile = Path.Combine(Path.GetTempPath(), $"csv_export_test_{Guid.NewGuid():N}.csv");

        try
        {
            // Act
            CsvExporter.ExportToCsv(events, tempFile);

            // Assert
            var lines = File.ReadAllLines(tempFile);
            var dataLine = lines[1];
            Assert.Contains(",Warning,", dataLine);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
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
        var tempFile = Path.Combine(Path.GetTempPath(), $"csv_export_test_{Guid.NewGuid():N}.csv");

        try
        {
            // Act
            CsvExporter.ExportToCsv(events, tempFile);

            // Assert
            var lines = File.ReadAllLines(tempFile);
            var dataLine = lines[1];
            // Timestamp is formatted with :O (round-trip ISO format)
            Assert.Contains("2025-03-15T10:30:00", dataLine);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}