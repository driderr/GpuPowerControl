using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GpuThermalController.Core;

/// <summary>
/// Single source of truth for all thermal controller parameters.
/// </summary>
public class ThermalControllerConfig
{
    // Temperature thresholds
    public uint TriggerTemp { get; set; } = 80;
    public uint TargetTemp { get; set; } = 75;
    public uint EmergencyTemp { get; set; } = 90;

    // PID gains
    public double Kp { get; set; } = 20.0;
    public double Ki { get; set; } = 0.5;
    public double Kd { get; set; } = 2.5;

    // PID anti-windup limits
    public double IntegralMax { get; set; } = 500;
    public double IntegralMin { get; set; } = -50;

    // Conditional integration: only accumulate integral within this band around target
    public double IntegralBand { get; set; } = 15;

    // Timing
    public double DefaultDt { get; set; } = 0.25;
    public double MinimumDt { get; set; } = 0.001;
    public int ControllingSleepMs { get; set; } = 250;
    public int IdleSleepMs { get; set; } = 1500;

    // Predictive trigger
    public double LookaheadSeconds { get; set; } = 1.5;
    public double PredictiveFloor { get; set; } = 70;

    // Exit condition hysteresis
    public uint ExitHysteresis { get; set; } = 5;

    // Fault tolerance
    public int MaxConsecutiveReadFailures { get; set; } = 5;

    // Default hardware power limits
    public int DefaultMaxPower { get; set; } = 600;
    public int DefaultMinPower { get; set; } = 150;

    // Power adjustment gating
    public int MinPowerDeltaFarW { get; set; } = 10;
    public int MinPowerDeltaNearW { get; set; } = 3;
    public uint NearTargetThreshold { get; set; } = 3;
    public int MinAdjustmentIntervalMs { get; set; } = 2500;
    public double IntervalBypassDerivative { get; set; } = 2.0;
    public double NormalMaxPowerIncreaseRateWps { get; set; } = 15;

    // Emergency recovery
    public int EmergencyHoldMs { get; set; } = 5000;
    public double EmergencyRecoveryRateWps { get; set; } = 5;

    /// <summary>
    /// Loads appsettings.json, strips comments, and merges with defaults.
    /// Uses JsonDocument for direct JSON tree traversal with reflection-based merge.
    /// </summary>
    public static ThermalControllerConfig Load()
    {
        var config = new ThermalControllerConfig();

        try
        {
            string? raw = LoadRawConfig();
            if (raw != null)
            {
                string stripped = Regex.Replace(raw, @"(?m)^[ \t]*//.*$", "");
                stripped = Regex.Replace(stripped, @"(?s)/\*.*?\*/", "");
                using var doc = JsonDocument.Parse(stripped);
                MergeJsonElement(doc.RootElement, config);
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Warning: Failed to load appsettings.json: {ex.Message}");
            Console.WriteLine("Using hardcoded defaults.");
            Console.ResetColor();
        }

        return config;
    }

    /// <summary>
    /// Recursively walks a JsonElement and merges values into ThermalControllerConfig.
    /// For nested objects, converts camelCase keys to PascalCase to match config properties.
    /// </summary>
    static void MergeJsonElement(JsonElement element, ThermalControllerConfig config)
    {
        var configType = typeof(ThermalControllerConfig);

        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                // Nested section object - merge its properties directly
                foreach (var nestedProp in prop.Value.EnumerateObject())
                {
                    string configPropName = char.ToUpper(nestedProp.Name[0]) + nestedProp.Name.Substring(1);
                    var configProp = configType.GetProperty(configPropName);
                    if (configProp != null)
                    {
                        object? value = ConvertJsonValue(nestedProp.Value, configProp.PropertyType);
                        if (value != null)
                            configProp.SetValue(config, value);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Converts a JsonValue to the target property type.
    /// </summary>
    static object? ConvertJsonValue(JsonElement value, Type targetType)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return null;

        try
        {
            if (targetType == typeof(int) || targetType == typeof(int?))
                return value.GetInt32();
            if (targetType == typeof(uint) || targetType == typeof(uint?))
                return (uint)value.GetInt32();
            if (targetType == typeof(double) || targetType == typeof(double?))
                return value.GetDouble();
            if (targetType == typeof(bool) || targetType == typeof(bool?))
                return value.GetBoolean();
            if (targetType == typeof(string))
                return value.GetString();
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string? LoadRawConfig()
    {
        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        string configPath = Path.Combine(exeDir, "appsettings.json");

        if (File.Exists(configPath))
            return File.ReadAllText(configPath);

        if (File.Exists("appsettings.json"))
            return File.ReadAllText("appsettings.json");

        return null;
    }
}

