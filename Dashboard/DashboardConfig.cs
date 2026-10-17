namespace GpuThermalController.Dashboard;

/// <summary>
/// Static configuration snapshot created once at startup.
/// Contains values that do not change during runtime.
/// </summary>
public record DashboardConfig(
    string GpuName,
    int MinPower,
    int MaxPower,
    uint TargetTemp,
    uint TriggerTemp,
    uint EmergencyTemp,
    double Kp,
    double Ki,
    double Kd,
    int HwMinPower = 0,
    int HwMaxPower = 0,
    bool PowerControlVerified = false
);