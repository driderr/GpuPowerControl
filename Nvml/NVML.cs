using System.Runtime.InteropServices;
using System.Text;

namespace GpuThermalController.Nvml
{
    /// <summary>
    /// NVML P/Invoke definitions for interacting with NVIDIA Management Library.
    /// </summary>
    public static class NVML
    {
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlInit_v2();

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlShutdown();

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temp);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetCount_v2(out uint count);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetName_v2(IntPtr device, [Out] StringBuilder name, int length);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceSetPowerManagementLimit(IntPtr device, uint limitMw);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetPowerManagementLimit(IntPtr device, out uint limitMw);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetPowerManagementLimitConstraints(
            IntPtr device, out uint minLimitMw, out uint maxLimitMw);

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int nvmlDeviceGetPowerManagementDefaultLimit(
            IntPtr device, out uint defaultLimitMw);
    }
}