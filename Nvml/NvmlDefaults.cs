namespace GpuThermalController.Nvml;

/// <summary>
/// Default fallback values used when NVML cannot query hardware constraints.
/// These represent a safe desktop GPU range (150W–600W) and should only
/// be hit when nvmlDeviceGetPowerManagementLimitConstraints fails.
/// </summary>
public static class NvmlDefaults
{
    /// <summary>Default minimum power limit in watts. Used when NVML constraint query fails.</summary>
    public const int FallbackMinPowerWatts = 150;

    /// <summary>Default maximum power limit in watts. Used when NVML constraint query fails.</summary>
    public const int FallbackMaxPowerWatts = 600;
}