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
    double Kd
);