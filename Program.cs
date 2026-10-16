using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Security.Principal;
using System.Threading;
using Spectre.Console;
using GpuThermalController.Core;
using GpuThermalController.Dashboard;
using GpuPowerControl.Devices;
using GpuThermalController.Nvml;
using GpuThermalController.Notifications;

namespace GpuThermalController
{
    class Program
    {
        static void Main(string[] args)
        {
            // Set console title for profiling scripts to identify this process
            Console.Title = "GpuPowerControl";

            // Top-level crash capture: unhandled exceptions (e.g. from the control loop or
            // background threads) are written to crash-log.txt next to the executable so
            // they can be diagnosed even when the Live dashboard owns the console.
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                CrashLogger.Log(e.ExceptionObject as Exception,
                    $"Unhandled exception (AppDomain, terminating={e.IsTerminating})");
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                e.SetObserved();
                CrashLogger.Log(e.Exception, "Unobserved task exception");
            };
            
            // Parse command-line arguments
            bool simulateMode = false;
            bool disableNotifications = false;
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
                    case "--no-notifications":
                        disableNotifications = true;
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

            // Load external configuration (appsettings.json)
            ThermalControllerConfig config = ThermalControllerConfig.Load();

            // Resolve the operating power envelope: configured limits clamped to the
            // hardware range reported by NVML. Desktop defaults on a small-GPU laptop
            // (e.g. 150W-600W on a 1W-95W card) would otherwise make the PID controller
            // clamp to the hardware floor on its first cycle.
            int maxPower = config.EffectiveMaxPower(device.MaxPower);
            int minPower = config.EffectiveMinPower(device.MinPower, device.MaxPower);

            // Initialize toast notifications (default ON, use --no-notifications to disable)
            ToastNotificationService.Initialize();
            bool notificationsEnabled = !disableNotifications;
            ToastNotificationService.SetConfig(new NotificationConfig { Enabled = notificationsEnabled });

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Started Thermal Controller.");
            Console.WriteLine($"Device: {device.Name}");
            Console.WriteLine($"Trigger: {config.TriggerTemp}C | Target Stability: {config.TargetTemp}C | Emergency Limit: {config.EmergencyTemp}C");
            Console.WriteLine($"Power Range (hardware): {device.MinPower}W - {device.MaxPower}W");
            Console.WriteLine($"Power Range (operating): {minPower}W - {maxPower}W");
            Console.ResetColor();

            if (maxPower != config.DefaultMaxPower)
                Console.WriteLine($"Note: defaultMaxPower ({config.DefaultMaxPower}W) clamped to {maxPower}W to fit the hardware range.");
            if (minPower != config.DefaultMinPower)
                Console.WriteLine($"Note: defaultMinPower ({config.DefaultMinPower}W) clamped to {minPower}W to fit the hardware range.");
            if (minPower >= maxPower)
                Console.WriteLine($"Warning: zero-width operating envelope ({minPower}W - {maxPower}W): defaultMinPower " +
                    $"({config.DefaultMinPower}W) is at or above this GPU's effective maximum ({maxPower}W), so PID control " +
                    $"cannot lower the power limit at all. Lower defaultMinPower in appsettings.json to enable control.");

            // Single exit path for post-init startup failures: NVML is shut down exactly
            // once and the process exits non-zero (ExitCode rather than Exit() so a
            // posted toast notification still gets the chance to display).
            void ExitAfterStartupFailure()
            {
                if (nvmlInitialized)
                    NVML.nvmlShutdown();
                Environment.ExitCode = 1;
            }
            // A configured maximum below the hardware minimum leaves no valid operating
            // envelope - refuse to start with an actionable message.
            if (maxPower < device.MinPower)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"ERROR: Invalid power envelope: defaultMaxPower ({config.DefaultMaxPower}W) is below the " +
                    $"GPU's hardware minimum ({device.MinPower}W). Increase defaultMaxPower to at least {device.MinPower}W in appsettings.json.");
                Console.ResetColor();
                ToastNotificationService.ShowErrorToast("Invalid power envelope",
                    $"defaultMaxPower ({config.DefaultMaxPower}W) is below the GPU's hardware minimum ({device.MinPower}W). Increase defaultMaxPower in appsettings.json.");
                ExitAfterStartupFailure();
                return;
            }

            var pidController = new PidController(
                config.Kp, config.Ki, config.Kd,
                config.TargetTemp, maxPower, minPower,
                config.IntegralMax, config.IntegralMin, config.IntegralBand, config.MinimumDt);

            // Warn when the proportional gain alone would drive power to the operating
            // floor on the very first PID cycle (common on low-power GPUs using desktop
            // gains). Advisory only - control still clamps safely within the envelope.
            int envelopeWidth = maxPower - minPower;
            // Guard against a nonsensical config (trigger below target): a uint
            // subtraction would underflow and produce garbage in the advisory text.
            if (config.TriggerTemp > config.TargetTemp)
            {
                double firstCycleCut = config.Kp * (config.TriggerTemp - config.TargetTemp);
                if (envelopeWidth > 0 && firstCycleCut >= envelopeWidth)
                {
                    double suggestedKp = Math.Round(envelopeWidth / (double)(config.TriggerTemp - config.TargetTemp), 1);
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Warning: with Kp={config.Kp} the first PID cycle will drive power to the " +
                        $"{minPower}W floor (Kp x {config.TriggerTemp - config.TargetTemp}C = {firstCycleCut:F0}W on a " +
                        $"{envelopeWidth}W envelope). Consider lowering Kp to ~{suggestedKp} for smoother control.");
                    Console.ResetColor();
                }
            }

            // Startup capability probe: confirm this GPU can actually be power-limited
            // (set a test limit, read it back, restore it) before the control loop is
            // allowed to run. We do not want to discover at the moment of a thermal
            // emergency that power control was never available on this machine.
            PowerProbeResult probe;
            try
            {
                probe = device.ProbePowerControl();
            }
            catch (Exception ex)
            {
                // A device implementation may throw (the current NvmlGpuDevice wraps all
                // P/Invokes, but future implementations may not). Never let that crash
                // Main with NVML still initialized - route it through the normal
                // probe-failure path instead.
                probe = PowerProbeResult.Failure($"The power control check threw an exception: {ex.Message}");
            }
            if (!probe.CanControlPower)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("==========================================================");
                Console.WriteLine("  !! POWER CONTROL IS NOT AVAILABLE ON THIS GPU !!");
                Console.WriteLine("==========================================================");
                Console.WriteLine($"  {probe.Detail}");
                Console.WriteLine();
                Console.WriteLine("  This program exists to lower GPU power when the GPU gets");
                Console.WriteLine("  too hot. Since power limiting does not work on this machine,");
                Console.WriteLine("  starting it would give false confidence that the GPU is");
                Console.WriteLine("  protected. The control loop will NOT start.");
                Console.WriteLine("==========================================================");
                Console.ResetColor();
                ToastNotificationService.ShowErrorToast("Power control unavailable", probe.Detail);
                ExitAfterStartupFailure();
                return;
            }
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Power control check: OK - {probe.Detail}.");
            Console.ResetColor();
            var triggerEvaluator = new TriggerEvaluator(
                config.TriggerTemp, config.PredictiveFloor, config.LookaheadSeconds);

            var controller = new ThermalController(device, pidController, triggerEvaluator, config);

            // === DASHBOARD WIRING ===

            // 1. Create the data provider (collects metrics from controller via events)
            var dataProvider = new DashboardDataProvider(device, config);
            dataProvider.Subscribe(controller);

            // 2. Create the console dashboard (renders to terminal)
            var dashboard = new ConsoleDashboard(dataProvider);

            // Create file system abstraction
            IFileSystem fileSystem = new FileSystem();

            // 3. Create the JSON publisher (writes to disk, togglable)
            var jsonPublisher = new JsonPublisher(dataProvider, fileSystem);
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

            // Create CSV exporter instance
            var csvExporter = new CsvExporter(fileSystem);

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
                        csvExporter.ExportToCsv(events, filePath);
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

                // Show startup toast after dashboard is running
                //ToastNotificationService.ShowInfoToast("Dashboard Active", "Press ESC to quit, P to adjust PID");

                // Small delay to let dashboard initialize
                Thread.Sleep(500);

                // Then start the controller. The loop is fault-isolated internally, but
                // guard here too so any escapee is crash-logged instead of killing the app.
                try
                {
                    controller.RunAsync(cancellationTokenSource.Token).Wait();
                }
                catch (Exception ex)
                {
                    CrashLogger.Log(ex, "Control loop task fault");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"The control loop stopped due to an error: {ex.Message}");
                    Console.WriteLine($"Details written to {CrashLogger.LogFilePath}");
                    Console.ResetColor();
                }
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

                 // Show shutdown toast
                 ToastNotificationService.ShowInfoToast("GpuPowerControl", "Application shut down");
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