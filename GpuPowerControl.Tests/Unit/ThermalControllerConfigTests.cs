using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using System.Text.Json;
using GpuThermalController.Core;
using Xunit;

namespace GpuPowerControl.Tests.Unit;

/// <summary>
/// Tests for ThermalControllerConfig using in-memory MockFileSystem (zero disk I/O).
/// </summary>
public class ThermalControllerConfigTests
{
    private readonly MockFileSystem _mockFs;
    private readonly string _configPath;

    public ThermalControllerConfigTests()
    {
        _mockFs = new MockFileSystem();
        _configPath = "appsettings.json";
    }

    private void WriteJson(string jsonContent)
    {
        _mockFs.File.WriteAllText(_configPath, jsonContent);
    }

    // Helper JSON with ALL sections having custom values
    private string FullOverrideJson()
    {
        return """
        {
          "thermal": {
            "triggerTemp": 70,
            "targetTemp": 65,
            "emergencyTemp": 85,
            "exitHysteresis": 3,
            "lookaheadSeconds": 2.0,
            "predictiveFloor": 60
          },
          "pid": {
            "kp": 30.0,
            "ki": 1.0,
            "kd": 5.0,
            "integralMax": 400,
            "integralMin": -400
          },
          "power": {
            "defaultMaxPower": 500,
            "defaultMinPower": 100,
            "minPowerDeltaFarW": 8,
            "minPowerDeltaNearW": 2,
            "nearTargetThreshold": 5,
            "minAdjustmentIntervalMs": 3000,
            "intervalBypassDerivative": 3.0,
            "normalMaxPowerIncreaseRateWps": 20,
            "emergencyRecoveryRateWps": 3,
            "emergencyHoldMs": 7000
          },
          "timing": {
            "defaultDt": 0.5,
            "controllingSleepMs": 500,
            "idleSleepMs": 2000
          },
          "faultTolerance": {
            "maxConsecutiveReadFailures": 10
          },
          "notifications": {
            "enabled": true
          },
          "dashboard": {
            "jsonPublisherEnabled": true,
            "csvExportEnabled": false
          }
        }
        """;
    }

    // === DEFAULT VALUE TESTS ===

    [Fact]
    public void Defaults_AreCorrect()
    {
        var config = new ThermalControllerConfig();

        // Temperature thresholds
        Assert.Equal(80u, config.TriggerTemp);
        Assert.Equal(75u, config.TargetTemp);
        Assert.Equal(90u, config.EmergencyTemp);

        // PID gains
        Assert.Equal(20.0, config.Kp);
        Assert.Equal(0.5, config.Ki);
        Assert.Equal(2.5, config.Kd);

        // Anti-windup
        Assert.Equal(500.0, config.IntegralMax);
        Assert.Equal(-50.0, config.IntegralMin);

        // Timing
        Assert.Equal(0.25, config.DefaultDt);
        Assert.Equal(0.001, config.MinimumDt);
        Assert.Equal(250, config.ControllingSleepMs);
        Assert.Equal(1500, config.IdleSleepMs);

        // Power limits
        Assert.Equal(600, config.DefaultMaxPower);
        Assert.Equal(150, config.DefaultMinPower);

        // Power gating
        Assert.Equal(10, config.MinPowerDeltaFarW);
        Assert.Equal(3, config.MinPowerDeltaNearW);
        Assert.Equal(3u, config.NearTargetThreshold);
        Assert.Equal(2500, config.MinAdjustmentIntervalMs);

        // Hysteresis
        Assert.Equal(5u, config.ExitHysteresis);

        // Fault tolerance
        Assert.Equal(5, config.MaxConsecutiveReadFailures);

        // Predictive
        Assert.Equal(1.5, config.LookaheadSeconds);
        Assert.Equal(70.0, config.PredictiveFloor);

        // Emergency recovery
        Assert.Equal(5000, config.EmergencyHoldMs);
        Assert.Equal(5.0, config.EmergencyRecoveryRateWps);
        Assert.Equal(15.0, config.NormalMaxPowerIncreaseRateWps);
        Assert.Equal(2.0, config.IntervalBypassDerivative);
    }

    // === JSON OVERRIDE TESTS ===

    [Fact]
    public void Load_JsonOverridesDefaultValues_FullOverride()
    {
        WriteJson(FullOverrideJson());
        var config = ThermalControllerConfig.Load(_mockFs);

        // Thermal section
        Assert.Equal(70u, config.TriggerTemp);
        Assert.Equal(65u, config.TargetTemp);
        Assert.Equal(85u, config.EmergencyTemp);
        Assert.Equal(3u, config.ExitHysteresis);
        Assert.Equal(2.0, config.LookaheadSeconds);
        Assert.Equal(60.0, config.PredictiveFloor);

        // PID section
        Assert.Equal(30.0, config.Kp);
        Assert.Equal(1.0, config.Ki);
        Assert.Equal(5.0, config.Kd);
        Assert.Equal(400.0, config.IntegralMax);
        Assert.Equal(-400.0, config.IntegralMin);

        // Power section
        Assert.Equal(500, config.DefaultMaxPower);
        Assert.Equal(100, config.DefaultMinPower);
        Assert.Equal(8, config.MinPowerDeltaFarW);
        Assert.Equal(2, config.MinPowerDeltaNearW);
        Assert.Equal(5u, config.NearTargetThreshold);
        Assert.Equal(3000, config.MinAdjustmentIntervalMs);
        Assert.Equal(3.0, config.IntervalBypassDerivative);
        Assert.Equal(20.0, config.NormalMaxPowerIncreaseRateWps);
        Assert.Equal(3.0, config.EmergencyRecoveryRateWps);
        Assert.Equal(7000, config.EmergencyHoldMs);

        // Timing section
        Assert.Equal(0.5, config.DefaultDt);
        Assert.Equal(500, config.ControllingSleepMs);
        Assert.Equal(2000, config.IdleSleepMs);

        // Fault tolerance
        Assert.Equal(10, config.MaxConsecutiveReadFailures);
    }

    [Fact]
    public void Load_JsonOverridesDefaultValues_PartialOverride()
    {
        string json = """
        {
          "thermal": {
            "triggerTemp": 70
          }
        }
        """;
        WriteJson(json);
        var config = ThermalControllerConfig.Load(_mockFs);

        // Overridden value
        Assert.Equal(70u, config.TriggerTemp);
        // Default values remain for non-specified properties
        Assert.Equal(75u, config.TargetTemp);
        Assert.Equal(90u, config.EmergencyTemp);
        Assert.Equal(20.0, config.Kp); // PID defaults remain
    }

    // === COMMENT STRIPPING TESTS ===

    [Fact]
    public void Load_CommentStripping_LineComments()
    {
        string json = """
        {
          // This is a line comment
          "thermal": {
            // triggerTemp value
            "triggerTemp": 70
          }
        }
        """;
        WriteJson(json);
        var config = ThermalControllerConfig.Load(_mockFs);
        Assert.Equal(70u, config.TriggerTemp);
    }

    [Fact]
    public void Load_CommentStripping_BlockComments()
    {
        string json = """
        {
          /* Block comment */
          "thermal": {
            "triggerTemp": 70 /* inline block */
          }
        }
        """;
        WriteJson(json);
        var config = ThermalControllerConfig.Load(_mockFs);
        Assert.Equal(70u, config.TriggerTemp);
    }

    [Fact]
    public void Load_JsonWithComments_FullFileWithComments()
    {
        string json = """
        /* Full file with comments */
        {
          // Thermal settings
          "thermal": {
            // Trigger temperature
            "triggerTemp": 70,
            "targetTemp": 65 /* target temp */
          },
          "pid": {
            "kp": 30.0
          }
        }
        """;
        WriteJson(json);
        var config = ThermalControllerConfig.Load(_mockFs);
        Assert.Equal(70u, config.TriggerTemp);
        Assert.Equal(65u, config.TargetTemp);
        Assert.Equal(30.0, config.Kp);
    }

    // === PER-SECTION MERGE TESTS ===

    [Fact]
    public void Load_JsonNestedSections_Thermal()
    {
        string json = """
        {
          "thermal": {
            "triggerTemp": 70,
            "targetTemp": 65,
            "emergencyTemp": 85,
            "exitHysteresis": 3,
            "lookaheadSeconds": 2.0,
            "predictiveFloor": 60
          }
        }
        """;
        WriteJson(json);
        var config = ThermalControllerConfig.Load(_mockFs);
        Assert.Equal(70u, config.TriggerTemp);
        Assert.Equal(65u, config.TargetTemp);
        Assert.Equal(85u, config.EmergencyTemp);
        Assert.Equal(3u, config.ExitHysteresis);
        Assert.Equal(2.0, config.LookaheadSeconds);
        Assert.Equal(60.0, config.PredictiveFloor);
    }

    [Fact]
    public void Load_JsonNestedSections_Pid()
    {
        string json = """
        {
          "pid": {
            "kp": 30.0,
            "ki": 1.0,
            "kd": 5.0,
            "integralMax": 400,
            "integralMin": -400
          }
        }
        """;
        WriteJson(json);
        var config = ThermalControllerConfig.Load(_mockFs);
        Assert.Equal(30.0, config.Kp);
        Assert.Equal(1.0, config.Ki);
        Assert.Equal(5.0, config.Kd);
        Assert.Equal(400.0, config.IntegralMax);
        Assert.Equal(-400.0, config.IntegralMin);
    }

    [Fact]
    public void Load_JsonNestedSections_Power()
    {
        string json = """
        {
          "power": {
            "defaultMaxPower": 500,
            "defaultMinPower": 100,
            "minPowerDeltaFarW": 8,
            "minPowerDeltaNearW": 2,
            "nearTargetThreshold": 5,
            "minAdjustmentIntervalMs": 3000,
            "intervalBypassDerivative": 3.0,
            "normalMaxPowerIncreaseRateWps": 20,
            "emergencyRecoveryRateWps": 3,
            "emergencyHoldMs": 7000
          }
        }
        """;
        WriteJson(json);
        var config = ThermalControllerConfig.Load(_mockFs);
        Assert.Equal(500, config.DefaultMaxPower);
        Assert.Equal(100, config.DefaultMinPower);
        Assert.Equal(8, config.MinPowerDeltaFarW);
        Assert.Equal(2, config.MinPowerDeltaNearW);
        Assert.Equal(5u, config.NearTargetThreshold);
        Assert.Equal(3000, config.MinAdjustmentIntervalMs);
        Assert.Equal(3.0, config.IntervalBypassDerivative);
        Assert.Equal(20.0, config.NormalMaxPowerIncreaseRateWps);
        Assert.Equal(3.0, config.EmergencyRecoveryRateWps);
        Assert.Equal(7000, config.EmergencyHoldMs);
    }

    [Fact]
    public void Load_JsonNestedSections_Timing()
    {
        string json = """
        {
          "timing": {
            "defaultDt": 0.5,
            "controllingSleepMs": 500,
            "idleSleepMs": 2000
          }
        }
        """;
        WriteJson(json);
        var config = ThermalControllerConfig.Load(_mockFs);
        Assert.Equal(0.5, config.DefaultDt);
        Assert.Equal(500, config.ControllingSleepMs);
        Assert.Equal(2000, config.IdleSleepMs);
    }

    [Fact]
    public void Load_JsonNestedSections_FaultTolerance()
    {
        string json = """
        {
          "faultTolerance": {
            "maxConsecutiveReadFailures": 10
          }
        }
        """;
        WriteJson(json);
        var config = ThermalControllerConfig.Load(_mockFs);
        Assert.Equal(10, config.MaxConsecutiveReadFailures);
    }

    // === TYPE CONVERSION TESTS ===

    [Fact]
    public void Load_JsonTypeConversion_AllTypes()
    {
        string json = """
        {
          "thermal": {
            "triggerTemp": 70,
            "lookaheadSeconds": 2.5
          },
          "power": {
            "defaultMaxPower": 500,
            "normalMaxPowerIncreaseRateWps": 20.0
          }
        }
        """;
        WriteJson(json);
        var config = ThermalControllerConfig.Load(_mockFs);
        Assert.Equal(70u, config.TriggerTemp);
        Assert.Equal(2.5, config.LookaheadSeconds);
        Assert.Equal(500, config.DefaultMaxPower);
        Assert.Equal(20.0, config.NormalMaxPowerIncreaseRateWps);
    }

    // === ERROR HANDLING TESTS ===

    [Fact]
    public void Load_FromMissingFile_ReturnsDefaults()
    {
        // Don't write a config file - MockFileSystem will have no file at _configPath
        Assert.False(_mockFs.File.Exists(_configPath));
        var config = ThermalControllerConfig.Load(_mockFs);
        Assert.Equal(80u, config.TriggerTemp);
        Assert.Equal(75u, config.TargetTemp);
        Assert.Equal(20.0, config.Kp);
    }

    [Fact]
    public void Load_FromMalformedJson_ReturnsDefaults()
    {
        // Suppress the warning output for this expected failure path
        var originalOut = Console.Out;
        var stringWriter = new System.IO.StringWriter();
        Console.SetOut(stringWriter);

        try
        {
            // Write malformed JSON
            _mockFs.File.WriteAllText(_configPath, "{ invalid json content };");
            var config = ThermalControllerConfig.Load(_mockFs);
            Assert.Equal(80u, config.TriggerTemp);
            Assert.Equal(75u, config.TargetTemp);
            Assert.Equal(20.0, config.Kp);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Load_EmptyJsonObject_ReturnsDefaults()
    {
        WriteJson("{}");
        var config = ThermalControllerConfig.Load(_mockFs);
        Assert.Equal(80u, config.TriggerTemp);
        Assert.Equal(75u, config.TargetTemp);
    }

    [Fact]
    public void Load_JsonUnknownKeys_Ignored()
    {
        string json = """
        {
          "thermal": {
            "triggerTemp": 70,
            "unknownKey": "someValue"
          },
          "nonExistentSection": {
            "someProp": 123
          }
        }
        """;
        WriteJson(json);
        var config = ThermalControllerConfig.Load(_mockFs);
        Assert.Equal(70u, config.TriggerTemp);
    }

    [Fact]
    public void Load_JsonTypeMismatch_HandledGracefully()
    {
        string json = """
        {
          "thermal": {
            "triggerTemp": "not_a_number",
            "targetTemp": 75
          }
        }
        """;
        WriteJson(json);
        var config = ThermalControllerConfig.Load(_mockFs);
        // triggerTemp should remain at default (80) since "not_a_number" can't convert to uint
        Assert.Equal(80u, config.TriggerTemp);
        // targetTemp should be overridden successfully
        Assert.Equal(75u, config.TargetTemp);
    }

    // === EDGE CASES ===

    [Fact]
    public void Load_JsonWithMixedComments()
    {
        string json = """
        /* Header comment */
        {
          // Section comment
          "thermal": {
            /* Inline block */ "triggerTemp": 70,
            "targetTemp": 65 /* mid comment */
          }
        }
        // Trailing section comment
        """;
        WriteJson(json);
        var config = ThermalControllerConfig.Load(_mockFs);
        Assert.Equal(70u, config.TriggerTemp);
        Assert.Equal(65u, config.TargetTemp);
    }

    [Fact]
    public void Load_JsonWithNullValues_Ignored()
    {
        string json = """
        {
          "thermal": {
            "triggerTemp": null,
            "targetTemp": 65
          }
        }
        """;
        WriteJson(json);
        var config = ThermalControllerConfig.Load(_mockFs);
        // null values should be ignored (keep default)
        Assert.Equal(80u, config.TriggerTemp);
        Assert.Equal(65u, config.TargetTemp);
    }
}