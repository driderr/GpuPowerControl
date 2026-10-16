namespace GpuPowerControl.Devices
{
    /// <summary>
    /// Outcome of the startup power-control capability probe: a live round-trip
    /// (set a test power limit, read it back, restore the original limit) that confirms
    /// this GPU and driver actually accept power limit changes before the control loop
    /// is allowed to rely on them.
    /// </summary>
    public sealed class PowerProbeResult
    {
        /// <summary>
        /// True when the GPU accepted the test power limit change and the read-back
        /// confirmed it, i.e. thermal power protection can actually be applied here.
        /// </summary>
        public bool CanControlPower { get; init; }

        /// <summary>Human-readable explanation of what the probe found (shown to the user).</summary>
        public string Detail { get; init; } = string.Empty;

        public static PowerProbeResult Success(string detail)
            => new() { CanControlPower = true, Detail = detail };

        public static PowerProbeResult Failure(string detail)
            => new() { CanControlPower = false, Detail = detail };
    }
}