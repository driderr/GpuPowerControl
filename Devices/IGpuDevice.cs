namespace GpuPowerControl.Devices
{
    /// <summary>
    /// Abstracts a GPU device that can report temperature and have its power limit adjusted.
    /// </summary>
    public interface IGpuDevice
    {
        /// <summary>Display name of the GPU.</summary>
        string Name { get; }

        /// <summary>Minimum power limit the GPU supports (Watts).</summary>
        int MinPower { get; }

        /// <summary>Maximum power limit the GPU supports (Watts).</summary>
        int MaxPower { get; }

        /// <summary>
        /// Gets the current GPU temperature in degrees Celsius.
        /// </summary>
        /// <param name="temperature">The current temperature on success (raw double, not rounded).</param>
        /// <returns>True if the read succeeded, false otherwise.</returns>
        bool GetTemperature(out double temperature);

        /// <summary>
        /// Sets the power limit for the GPU.
        /// </summary>
        /// <param name="watts">Desired power limit in Watts.</param>
        /// <param name="errorMessage">Error message if the operation fails.</param>
        /// <returns>True if successful, false otherwise.</returns>
        bool SetPowerLimit(int watts, out string? errorMessage);

        /// <summary>
        /// Startup capability probe: attempts a small power-limit change, confirms it
        /// by reading the limit back, and restores the original limit. The program
        /// refuses to start the control loop when power control is not available, so
        /// the user is never given false confidence that their GPU is protected.
        /// </summary>
        PowerProbeResult ProbePowerControl();
    }
}