using GpuThermalController.Interfaces;

namespace GpuPowerControl.Tests.Mocks;

/// <summary>
/// Mock GPU device that allows injecting temperature values and recording power limit calls.
/// </summary>
public class MockGpuDevice : IGpuDevice
{
    private Func<double>? _temperatureProvider;
    private double _constantTemperature;

    public string Name { get; }
    public int MinPower { get; }
    public int MaxPower { get; }

    /// <summary>List of all power limits that were set, in order.</summary>
    public List<int> PowerLimitCalls { get; } = new List<int>();

    /// <summary>Most recent power limit set, or null if never set.</summary>
    public int? LastPowerLimit => PowerLimitCalls.Count > 0 ? PowerLimitCalls.Last() : (int?)null;

    public bool SetPowerLimitShouldFail { get; set; }
    public string? FailErrorMessage { get; set; }

    /// <summary>When true, GetTemperature returns false (simulates device read failure).</summary>
    public bool GetTemperatureShouldFail { get; set; }

    public MockGpuDevice(
        string name = "Mock GPU",
        int minPower = 150,
        int maxPower = 600)
    {
        Name = name;
        MinPower = minPower;
        MaxPower = maxPower;
        _constantTemperature = 0;
    }

    /// <summary>
    /// Sets a constant temperature that will be returned by GetTemperature().
    /// </summary>
    public void SetConstantTemperature(double temp)
    {
        _temperatureProvider = null;
        _constantTemperature = temp;
    }

    /// <summary>
    /// Sets a sequence of temperatures that will be returned in order by GetTemperature().
    /// When exhausted, the last value is repeated.
    /// </summary>
    public void SetTemperatureSequence(IEnumerable<double> temps)
    {
        var list = temps.ToList();
        int index = 0;
        _temperatureProvider = () =>
        {
            if (index < list.Count)
                return list[index++];
            return list[list.Count - 1]; // Repeat last value
        };
        _constantTemperature = 0;
    }

    /// <summary>
    /// Sets a custom function that provides temperature on each GetTemperature() call.
    /// </summary>
    public void SetTemperatureProvider(Func<double> provider)
    {
        _temperatureProvider = provider;
    }

    public bool GetTemperature(out double temperature)
    {
        if (GetTemperatureShouldFail)
        {
            temperature = 0;
            return false;
        }

        if (_temperatureProvider != null)
            temperature = _temperatureProvider();
        else
            temperature = _constantTemperature;

        return true;
    }

    public bool SetPowerLimit(int watts, out string? errorMessage)
    {
        PowerLimitCalls.Add(watts);

        if (SetPowerLimitShouldFail)
        {
            errorMessage = FailErrorMessage ?? "Mock failure";
            return false;
        }

        errorMessage = null;
        return true;
    }

    /// <summary>Clear the recorded power limit calls.</summary>
    public void ClearPowerLimitCalls()
    {
        PowerLimitCalls.Clear();
    }
}