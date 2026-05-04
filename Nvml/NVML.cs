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
        public static extern int nvmlDeviceGetName(IntPtr device, [Out] StringBuilder name, int length);

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

        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern string nvmlErrorString(int result);

        /// <summary>
        /// Common NVML error codes.
        /// </summary>
        public const int NVML_SUCCESS = 0;
        public const int NVML_ERROR_UNINITIALIZED = 1;
        public const int NVML_ERROR_INVALID_ARGUMENT = 2;
        public const int NVML_ERROR_NOT_SUPPORTED = 3;
        public const int NVML_ERROR_NOT_PERMITTED = 4;
        public const int NVML_ERROR_NO_DATA = 5;
        public const int NVML_ERROR_INSUFFICIENT_SIZE = 6;
        public const int NVML_ERROR_INSUFFICIENT_POWER = 7;
        public const int NVML_ERROR_DRIVER_NOT_LOADED = 8;
        public const int NVML_ERROR_TIMEOUT = 9;
        public const int NVML_ERROR_IRQ_DISABLERDY = 10;
        public const int NVML_ERROR_LIBRARY_MINOR_MISMATCH = 11;
        public const int NVML_ERROR_LIBRARY_MAJOR_MISMATCH = 12;
        public const int NVML_ERROR_GPU_IS_LOST = 13;
        public const int NVML_ERROR_RESET_REQUIRED = 14;
        public const int NVML_ERROR_OPERATING_SYSTEM = 15;
        public const int NVML_ERROR_LIB_RM_VERSION_MISMATCH = 16;
        public const int NVML_ERROR_IN_USE = 17;
        public const int NVML_ERROR_MEMORY = 18;
        public const int NVML_ERROR_CORRUPTED_INFOROM = 19;
        public const int NVML_ERROR_ARGUMENT_VERSION_MISMATCH = 20;
        public const int NVML_ERROR_DEPRECATED = 21;
        public const int NVML_ERROR_UNKNOWN = 999;

        /// <summary>
        /// Converts an NVML error code to a human-readable string.
        /// Falls back to the numeric code if nvmlErrorString is unavailable.
        /// </summary>
        public static string GetErrorMessage(int result)
        {
            if (result == NVML_SUCCESS)
            {
                return "Success";
            }

            try
            {
                string msg = nvmlErrorString(result);
                if (!string.IsNullOrEmpty(msg))
                {
                    return msg;
                }
            }
            catch
            {
                // nvmlErrorString may not be available in all environments
            }

            return $"NVML error code {result}";
        }
    }
}
