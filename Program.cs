using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Threading;
using GpuThermalController.Core;
using GpuThermalController.Dashboard;
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

            // === DASHBOARD WIRING ===

            // 1. Create the data provider (collects metrics from controller via events)
            var dataProvider = new DashboardDataProvider(device, config);
            dataProvider.Subscribe(controller);

            // 2. Create the console dashboard (renders to terminal)
            var dashboard = new ConsoleDashboard(dataProvider);

            // 3. Create the JSON publisher (writes to disk, togglable)
            var jsonPublisher = new JsonPublisher(dataProvider);
            jsonPublisher.Start(enabled: false);

            // 4. Create the key handler (keyboard shortcuts)
            var keyHandler = new KeyHandler();

            // Create cancellation token source before wiring events that capture it
            var cancellationTokenSource = new CancellationTokenSource();

            // Wire up key handler events
            keyHandler.QuitRequested += () =>
            {
                Console.CancelKeyPress -= CancelKeyHandler;
                controller.Stop();
                cancellationTokenSource.Cancel();
            };

            keyHandler.ToggleLogRequested += () =>
            {
                dashboard.ToggleLog();
            };

            keyHandler.ToggleJsonRequested += () =>
            {
                jsonPublisher.Toggle();
                dashboard.SetJsonStatus(jsonPublisher.IsEnabled);
            };

            keyHandler.ExportCsvRequested += () =>
            {
                var events = dataProvider.GetEvents(500);
                try
                {
                    var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
                    var filePath = $"data/export-{timestamp}.csv";
                    CsvExporter.ExportToCsv(events, filePath);
                }
                catch
                {
                    // Silently ignore export errors
                }
            };

            keyHandler.ToggleConfigRequested += () =>
            {
                dashboard.ToggleConfig();
            };

            // Setup graceful exit
            Console.CancelKeyPress += CancelKeyHandler;
            void CancelKeyHandler(object? sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                controller.Stop();
                cancellationTokenSource.Cancel();
            }

            try
            {
                // Start dashboard first (it renders immediately with initial empty data)
                dashboard.Start();
                keyHandler.Start();

                // Small delay to let dashboard initialize
                Thread.Sleep(500);

                // Then start the controller
                controller.RunAsync(cancellationTokenSource.Token).Wait();
            }
            finally
            {
                // Cleanup dashboard resources
                keyHandler.Dispose();
                dashboard.Dispose();
                jsonPublisher.Dispose();
                dataProvider.Dispose();

                Console.WriteLine("\nShutting down NVML...");
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