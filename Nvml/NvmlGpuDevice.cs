using System.Text;
using GpuThermalController.Interfaces;

namespace GpuThermalController.Nvml
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
            int minPower = 150; // Default fallback
            int maxPower = 600; // Default fallback

            result = NVML.nvmlDeviceGetPowerManagementLimitConstraints(handle, out uint minLimitMw, out uint maxLimitMw);
            if (result == 0)
            {
                minPower = (int)(minLimitMw / 1000);
                maxPower = (int)(maxLimitMw / 1000);
            }

            return new NvmlGpuDevice(index, handle, name, minPower, maxPower);
        }

        public uint GetTemperature()
        {
            uint temp = 0;
            NVML.nvmlDeviceGetTemperature(_handle, 0, out temp); // 0 = NVML_TEMPERATURE_GPU
            return temp;
        }

        public bool SetPowerLimit(int watts, out string? errorMessage)
        {
            errorMessage = null;
            uint limitMw = (uint)(watts * 1000);

            int result = NVML.nvmlDeviceSetPowerManagementLimit(_handle, limitMw);
            if (result != 0)
            {
                errorMessage = $"NVML nvmlDeviceSetPowerManagementLimit failed: {NVML.GetErrorMessage(result)}";
                return false;
            }

            return true;
        }
    }
}