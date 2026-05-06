using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Threading;
using Spectre.Console;
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
            // Set console title for profiling scripts to identify this process
            Console.Title = "GpuPowerControl";
            
            // Parse command-line arguments
            bool simulateMode = false;
            string? scenarioArg = null;
            int? baseTempArg = null;
            int? peakTempArg = null;
            int? seedArg = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--simulate":
                        simulateMode = true;
                        break;
                    case "--scenario":
                        if (i + 1 < args.Length) scenarioArg = args[i + 1];
                        break;
                    case "--base-temp":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int baseTemp)) baseTempArg = baseTemp;
                        break;
                    case "--peak-temp":
                        if (i + 1 < args.Length) if (int.TryParse(args[i + 1], out int pt)) peakTempArg = pt;
                        break;
                    case "--seed":
                        if (i + 1 < args.Length) if (int.TryParse(args[i + 1], out int sd)) seedArg = sd;
                        break;
                    case "--test-error":
                        ErrorConsole.Error("This is a test error message");
                        ErrorConsole.Warning("This is a test warning message");
                        return;
                }
            }

            IGpuDevice device;
            bool nvmlInitialized = false;

            if (simulateMode)
            {
                // === SIMULATION MODE ===
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("==========================================================");
                Console.WriteLine("*** SIMULATION MODE - No real GPU affected ***");
                Console.WriteLine("==========================================================");
                Console.ResetColor();

                // Build simulation config
                var simConfig = new SimulatedGpuConfig();

                if (scenarioArg != null)
                {
                    if (Enum.TryParse<SimulationScenario>(scenarioArg, true, out var scenario))
                        simConfig.Scenario = scenario;
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Invalid scenario: {scenarioArg}. Valid: Default, Idle, Spike, SustainedLoad, Emergency, GradualWarmup");
                        Console.ResetColor();
                        return;
                    }
                }

                simConfig.BaseTemp = baseTempArg;
                simConfig.PeakTemp = peakTempArg;
                simConfig.Seed = seedArg;

                device = new SimulatedGpuDevice(simConfig);

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"Scenario: {simConfig.Scenario}");
                if (baseTempArg.HasValue) Console.WriteLine($"Base Temp: {baseTempArg}C");
                if (peakTempArg.HasValue) Console.WriteLine($"Peak Temp: {peakTempArg}C");
                if (seedArg.HasValue) Console.WriteLine($"Random Seed: {seedArg}");
                Console.ResetColor();
            }
            else
            {
                // === REAL GPU MODE ===
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
                nvmlInitialized = true;

                device = SelectGpu();
            }

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
                config.IntegralMax, config.IntegralMin, config.IntegralBand, config.MinimumDt);

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

            // P4: CSV export on background thread to avoid blocking the key handler thread
            keyHandler.ExportCsvRequested += () =>
            {
                var events = dataProvider.GetEvents(500);
                Task.Run(() =>
                {
                    try
                    {
                        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
                        var filePath = $"data/export-{timestamp}.csv";
                        CsvExporter.ExportToCsv(events, filePath);
                    }
                    catch (Exception ex)
                    {
                        ErrorConsole.Error($"CSV export failed: {ex.Message}");
                    }
                });
            };

            keyHandler.ToggleConfigRequested += () =>
            {
                dashboard.ToggleConfig();
            };

            // Test error output (press T during dashboard to verify ErrorConsole works)
            keyHandler.TestErrorRequested += () =>
            {
                ErrorConsole.Error("Test error triggered (press T during runtime)");
                ErrorConsole.Warning("Test warning triggered (press T during runtime)");
            };

            // PID coefficient adjustment (press P during dashboard)
            keyHandler.AdjustPidRequested += () =>
            {
                // Pause dashboard Live display so TextPrompt can safely use the console
                dashboard.Pause();
                try
                {
                    AnsiConsole.MarkupLine("");
                    AnsiConsole.MarkupLine("[bold cyan]Adjust PID Coefficients[/] [gray](press Enter to keep current value)[/]");

                    var newKp = AnsiConsole.Ask<double>($"  Kp [gray](current: {config.Kp:F1})[/]:", config.Kp);
                    var newKi = AnsiConsole.Ask<double>($"  Ki [gray](current: {config.Ki:F1})[/]:", config.Ki);
                    var newKd = AnsiConsole.Ask<double>($"  Kd [gray](current: {config.Kd:F1})[/]:", config.Kd);

                    // Update the PID controller (also resets integral to prevent windup)
                    pidController.SetCoefficients(newKp, newKi, newKd);

                    // Keep config in sync
                    config.Kp = newKp;
                    config.Ki = newKi;
                    config.Kd = newKd;

                    // Update dashboard config display
                    dataProvider.UpdatePidCoefficients(newKp, newKi, newKd);

                    AnsiConsole.MarkupLine($"[green]PID updated: Kp={newKp:F1}, Ki={newKi:F1}, Kd={newKd:F1}[/]");
                }
                finally
                {
                    dashboard.Resume();
                }
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

                if (nvmlInitialized)
                {
                    Console.WriteLine("\nShutting down NVML...");
                    NVML.nvmlShutdown();
                }
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