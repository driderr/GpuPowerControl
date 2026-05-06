namespace GpuThermalController.Interfaces
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
    }
}