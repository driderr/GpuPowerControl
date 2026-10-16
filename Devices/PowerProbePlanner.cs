namespace GpuPowerControl.Devices
{
    /// <summary>
    /// Pure decision logic for the startup power-control probe, kept separate from the
    /// NVML calls so the value-selection edge cases can be unit tested.
    /// </summary>
    public static class PowerProbePlanner
    {
        /// <summary>
        /// Chooses the temporary test value for the probe: <paramref name="stepW"/> below
        /// the restore target when that stays inside the hardware range (lowering power
        /// is the direction the controller works in), otherwise just above it. Returns
        /// the only possible value when the range is zero-width (min == max).
        /// </summary>
        public static int ChooseProbeTarget(int restoreW, int minW, int maxW, int stepW)
        {
            int probe = Math.Clamp(restoreW - stepW, minW, maxW);
            if (probe == restoreW)
                probe = Math.Clamp(restoreW + stepW, minW, maxW);
            return probe;
        }

        /// <summary>
        /// True when a read-back power limit matches the requested value within a
        /// rounding tolerance.
        /// </summary>
        public static bool ReadbackMatches(int readW, int requestedW, int toleranceW = 1)
            => Math.Abs(readW - requestedW) <= toleranceW;
    }
}