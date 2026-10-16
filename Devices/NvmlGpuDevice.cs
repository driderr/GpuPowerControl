using System.Text;
using GpuPowerControl.Devices;
using GpuThermalController.Nvml;
using GpuThermalController.Dashboard;


namespace GpuPowerControl.Devices
{
    /// <summary>
    /// Real NVML implementation of IGpuDevice.
    /// Wraps the NVML P/Invoke calls for temperature reading and power limit setting.
    /// </summary>
    public class NvmlGpuDevice : IGpuDevice
    {
        private readonly IntPtr _handle;
        private readonly uint _index;

        public string Name { get; }
        public int MinPower { get; }
        public int MaxPower { get; }

        public NvmlGpuDevice(uint index, IntPtr handle, string name, int minPower, int maxPower)
        {
            _index = index;
            _handle = handle;
            Name = name;
            MinPower = minPower;
            MaxPower = maxPower;
        }

        /// <summary>
        /// Creates an NvmlGpuDevice by querying NVML for device info.
        /// </summary>
        public static NvmlGpuDevice? Create(uint index)
        {
            IntPtr handle;
            int result = NVML.nvmlDeviceGetHandleByIndex_v2(index, out handle);
            if (result != 0)
                return null;

            // Get name
            StringBuilder nameBuilder = new StringBuilder(64);
            result = NVML.nvmlDeviceGetName(handle, nameBuilder, nameBuilder.Capacity);
            string name = result == 0 ? nameBuilder.ToString() : $"Device {index}";

            // Get power constraints
            int minPower;
            int maxPower;

            result = NVML.nvmlDeviceGetPowerManagementLimitConstraints(handle, out uint minLimitMw, out uint maxLimitMw);
            if (result == 0)
            {
                minPower = (int)(minLimitMw / 1000);
                maxPower = (int)(maxLimitMw / 1000);
            }
            else
            {
                ErrorConsole.Warning(
                    $"Cannot query power constraints for GPU {index} ({NVML.GetErrorMessage(result)}). " +
                    $"Using fallback range: {NvmlDefaults.FallbackMinPowerWatts}W-{NvmlDefaults.FallbackMaxPowerWatts}W");
                minPower = NvmlDefaults.FallbackMinPowerWatts;
                maxPower = NvmlDefaults.FallbackMaxPowerWatts;
            }

            return new NvmlGpuDevice(index, handle, name, minPower, maxPower);
        }

        public bool GetTemperature(out double temperature)
        {
            temperature = 0;
            try
            {
                uint rawTemp = 0;
                int result = NVML.nvmlDeviceGetTemperature(_handle, 0, out rawTemp); // 0 = NVML_TEMPERATURE_GPU
                temperature = rawTemp; // NVML returns integer precision, cast to double for controller
                return result == 0;
            }
            catch (Exception ex)
            {
                // Driver-level failure (e.g. lost GPU, P/Invoke exception). Report as a read
                // failure instead of crashing; the controller's fault tolerance handles repeats.
                ErrorConsole.Warning($"NVML temperature read failed: {ex.Message}");
                return false;
            }
        }

        public bool SetPowerLimit(int watts, out string? errorMessage)
        {
            errorMessage = null;
            try
            {
                uint limitMw = (uint)(watts * 1000);

                int result = NVML.nvmlDeviceSetPowerManagementLimit(_handle, limitMw);
                if (result != 0)
                {
                    errorMessage = $"NVML nvmlDeviceSetPowerManagementLimit failed: {NVML.GetErrorMessage(result)}";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                // P/Invoke or driver-level exception (e.g. GPU lost, OEM power management lock).
                // Never let this propagate: it would kill the control loop thread and the app.
                errorMessage = $"NVML nvmlDeviceSetPowerManagementLimit threw: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Live capability probe (see IGpuDevice.ProbePowerControl). The set result is
        /// authoritative; read-back confirmation only runs when the driver supports
        /// reading the current limit (some notebook GPUs do not). The original limit is
        /// always restored before the result is reported.
        /// </summary>
        public PowerProbeResult ProbePowerControl()
        {
            const int stepW = 5; // probe this many watts away from the restore target

            // 1) Limit to restore to: current value when readable, otherwise the driver
            //    default, otherwise the hardware maximum (leave the GPU as permissive as
            //    possible when the current limit is unknown).
            int? currentW = TryReadLimitWatts();
            int restoreW = currentW ?? TryReadDefaultLimitWatts() ?? MaxPower;

            // 2) Attempt the test set (slightly below the restore target when possible,
            //    since lowering power is the direction the controller works in).
            int probeW = PowerProbePlanner.ChooseProbeTarget(restoreW, MinPower, MaxPower, stepW);
            if (!SetPowerLimit(probeW, out string? setError))
            {
                string hint = string.Empty;
                if (setError != null && setError.Contains("NOT_SUPPORTED"))
                    hint = " On many laptop GPUs the power budget is owned by firmware (NVIDIA Dynamic Boost or OEM performance profiles) and cannot be changed by software.";
                else if (setError != null && setError.Contains("NOT_PERMITTED"))
                    hint = " Try running this application as Administrator.";
                return PowerProbeResult.Failure(
                    $"The driver rejected the test power limit change: {setError}.{hint} " +
                    "This GPU cannot be power-limited, so thermal protection is not possible on this machine.");
            }

            // 3) Read the limit back to confirm the change actually took effect.
            int? readBackW = TryReadLimitWatts();

            // 4) Restore the original limit regardless of the read-back outcome.
            if (!SetPowerLimit(restoreW, out string? restoreError))
            {
                return PowerProbeResult.Failure(
                    $"The power limit change was accepted, but the original limit ({restoreW}W) could not be restored: {restoreError}. " +
                    $"The GPU is left at {probeW}W. Reset it manually before relying on this program.");
            }

            if (readBackW is null)
            {
                return PowerProbeResult.Failure(
                    "The driver accepted the power limit change but does not report the current limit, " +
                    "so the change could not be confirmed. Power protection cannot be guaranteed on this machine.");
            }

            if (!PowerProbePlanner.ReadbackMatches(readBackW.Value, probeW))
            {
                return PowerProbeResult.Failure(
                    $"The driver reported success, but the power limit reads back as {readBackW}W instead of {probeW}W. " +
                    "Firmware or the driver is overriding the limit, so power protection cannot be guaranteed on this machine.");
            }

            return PowerProbeResult.Success(
                $"confirmed on {Name} (set {probeW}W, read back {readBackW}W, restored to {restoreW}W)");
        }

        /// <summary>Reads the current power limit in watts. Returns null when the driver does not support it.</summary>
        private int? TryReadLimitWatts()
        {
            try
            {
                if (NVML.nvmlDeviceGetPowerManagementLimit(_handle, out uint limitMw) == NVML.NVML_SUCCESS)
                    return (int)(limitMw / 1000u);
            }
            catch (Exception ex)
            {
                ErrorConsole.Warning($"NVML current power limit read failed: {ex.Message}");
            }
            return null;
        }

        /// <summary>Reads the driver default power limit in watts. Returns null when the driver does not support it.</summary>
        private int? TryReadDefaultLimitWatts()
        {
            try
            {
                if (NVML.nvmlDeviceGetPowerManagementDefaultLimit(_handle, out uint defaultMw) == NVML.NVML_SUCCESS)
                    return (int)(defaultMw / 1000u);
            }
            catch (Exception ex)
            {
                ErrorConsole.Warning($"NVML default power limit read failed: {ex.Message}");
            }
            return null;
        }
    }
}