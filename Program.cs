using System;
using System.Collections.Generic;
using System.Security.Principal;
using GpuThermalController.Core;
using GpuThermalController.Interfaces;
using GpuThermalController.Nvml;

namespace GpuThermalController
{
    class Program
    {
        static void Main(string[] args)
        {
            if (!IsAdministrator())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("ERROR: This application must be run as Administrator to set power limits.");
                Console.ResetColor();
                return;
            }

            Console.WriteLine("Initializing NVIDIA Management Library (NVML)...");
            int nvmlResult = NVML.nvmlInit_v2();
            if (nvmlResult != 0)
            {
                Console.WriteLine($"Failed to initialize NVML: {NVML.GetErrorMessage(nvmlResult)}. Ensure NVIDIA drivers are installed.");
                return;
            }

            IGpuDevice device = SelectGpu();

            var config = new ThermalControllerConfig();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Started Thermal Controller.");
            Console.WriteLine($"Device: {device.Name}");
            Console.WriteLine($"Trigger: {config.TriggerTemp}C | Target Stability: {config.TargetTemp}C | Emergency Limit: {config.EmergencyTemp}C");
            Console.WriteLine($"Power Range: {device.MinPower}W - {device.MaxPower}W");
            Console.ResetColor();

            var pidController = new PidController(
                config.Kp, config.Ki, config.Kd,
                config.TargetTemp, device.MaxPower, device.MinPower,
                config.IntegralMax, config.IntegralMin, config.MinimumDt);

            var triggerEvaluator = new TriggerEvaluator(
                config.TriggerTemp, config.PredictiveFloor, config.LookaheadSeconds);

            var controller = new ThermalController(device, pidController, triggerEvaluator, config);

            controller.OnStateChange += (sender, e) =>
            {
                if (e.Message == null) return;

                Console.ForegroundColor = e.EventType switch
                {
                    ControllerEventType.Emergency => ConsoleColor.Red,
                    ControllerEventType.Stable    => ConsoleColor.Green,
                    ControllerEventType.Trigger   => ConsoleColor.Magenta,
                    ControllerEventType.Warning   => ConsoleColor.Yellow,
                    _ => e.Temperature >= config.TargetTemp ? ConsoleColor.Yellow : ConsoleColor.Cyan
                };

                bool isNewLine = e.EventType is ControllerEventType.Emergency or ControllerEventType.Stable or ControllerEventType.Trigger;
                Console.Write(isNewLine ? "\n" : "");
                Console.WriteLine(e.Message);
                Console.ResetColor();
            };

            // Setup graceful exit
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\nExiting... Restoring Max Power.");
                controller.Stop();
            };

            try
            {
                controller.RunAsync().Wait();
            }
            finally
            {
                Console.WriteLine("Shutting down NVML...");
                NVML.nvmlShutdown();
            }
        }

        static IGpuDevice SelectGpu()
        {
            uint deviceCount;
            int result = NVML.nvmlDeviceGetCount_v2(out deviceCount);
            if (result != 0)
            {
                Console.WriteLine($"Failed to get GPU device count: {NVML.GetErrorMessage(result)}.");
                NVML.nvmlShutdown();
                Environment.Exit(1);
            }

            var gpus = new List<(uint index, string name, NvmlGpuDevice? device)>();

            for (uint i = 0; i < deviceCount; i++)
            {
                NvmlGpuDevice? device = NvmlGpuDevice.Create(i);
                if (device != null)
                {
                    gpus.Add((i, device.Name, device));
                }
            }

            if (gpus.Count == 0)
            {
                Console.WriteLine("No NVIDIA GPUs found.");
                NVML.nvmlShutdown();
                Environment.Exit(1);
            }

            if (gpus.Count == 1)
            {
                Console.WriteLine($"Found 1 NVIDIA GPU: {gpus[0].name}");
                return gpus[0].device!;
            }

            // Multiple GPUs — prompt user to select
            Console.WriteLine($"Found {gpus.Count} NVIDIA GPUs:");
            for (int i = 0; i < gpus.Count; i++)
            {
                Console.WriteLine($"  [{i + 1}] {gpus[i].name} (index {gpus[i].index})");
            }

            int selectedIndex;
            while (true)
            {
                Console.Write($"Select GPU (1-{gpus.Count}): ");
                string? input = Console.ReadLine();
                if (int.TryParse(input, out selectedIndex) && selectedIndex >= 1 && selectedIndex <= gpus.Count)
                    break;
                Console.WriteLine("Invalid selection. Please try again.");
            }

            int gpuIndex = selectedIndex - 1;
            Console.WriteLine($"Selected: {gpus[gpuIndex].name}");
            return gpus[gpuIndex].device!;
        }

        static bool IsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
    }
}