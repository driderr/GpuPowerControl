using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace GpuThermalController
{
    class Program
    {
        // --- CONFIGURATION ---
        const uint TriggerTemp = 80;       // Temperature that triggers the control loop
        const uint TargetTemp = 75;        // Temperature the PID loop will attempt to maintain
        const uint EmergencyTemp = 95;     // Emergency hard-limit — force minimum power immediately
        const int MaxPower = 600;          // Max power limit (Watts)
        const int MinPower = 150;          // Min power limit (Watts)

        // --- PID TUNING ---
        const double Kp = 8.0;             // Proportional: Watts to drop per 1°C over TargetTemp
        const double Ki = 0.5;             // Integral: Sustained reaction to eliminate steady-state error
        const double Kd = 2.5;             // Derivative: Reaction to the speed of temperature change
        const double LookaheadSeconds = 1.5; // How far into the future to predict for early triggering

        // --- STATE VARIABLES ---
        static double integral = 0;
        static int currentPowerLimit = MaxPower;
        static bool isControlling = false;
        static uint lastKnownGoodTemp = TargetTemp; // Cache last successful temperature reading; default to TargetTemp so a first-read failure doesn't result in 0°C
        static int consecutiveReadFailures = 0; // Tracks consecutive NVML temperature read failures for fail-safe behavior

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
                Console.WriteLine($"Failed to initialize NVML (error code: {nvmlResult}). Ensure NVIDIA drivers are installed.");
                return;
            }

            IntPtr gpuHandle = SelectGpu();

            Console.WriteLine("Enabling Persistence Mode...");
            SetPowerLimitCommandLine("-pm 1", out string? pmError);
            if (!string.IsNullOrEmpty(pmError))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Warning: Could not enable persistence mode: {pmError}");
                Console.ResetColor();
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Started Thermal Controller.");
            Console.WriteLine($"Trigger: {TriggerTemp}°C | Target Stability: {TargetTemp}°C | Emergency Limit: {EmergencyTemp}°C | Max Power: {MaxPower}W");
            Console.ResetColor();

            DateTime lastTime = DateTime.UtcNow;
            uint lastTemp = GetTemperature(gpuHandle);

            // Setup graceful exit
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true; // Prevent default termination so cleanup can run
                Console.WriteLine("\nExiting... Restoring Max Power.");
                SetPowerLimitCommandLine($"-pl {MaxPower}", out _);
            };

            // MAIN LOOP — wrapped in try/finally to ensure NVML shutdown
            try
            {
                while (true)
                {
                    uint currentTemp = GetTemperature(gpuHandle);
                    DateTime now = DateTime.UtcNow;
                    double dt = (now - lastTime).TotalSeconds;
                    if (dt <= 0) dt = 0.001; // Prevent division by zero

                    double derivative = (currentTemp - lastTemp) / dt;

                    // 0. EMERGENCY SAFETY — force minimum power at extreme temperatures
                    if (currentTemp >= EmergencyTemp)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"\n[EMERGENCY] Temp hit {currentTemp}°C! Forcing minimum power limit ({MinPower}W).");
                        Console.ResetColor();
                        SetPowerLimitCommandLine($"-pl {MinPower}", out string? emError);
                        if (!string.IsNullOrEmpty(emError))
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"[EMERGENCY ERROR] Failed to set power limit: {emError}");
                            Console.ResetColor();
                        }
                        currentPowerLimit = MinPower;
                        Thread.Sleep(500);
                        lastTemp = currentTemp;
                        lastTime = now;
                        continue;
                    }

                    // 1. PREDICTIVE & SAFETY TRIGGERS
                    if (!isControlling)
                    {
                        bool safetyTrigger = currentTemp >= TriggerTemp;
                        bool predictiveTrigger = currentTemp >= 70 && (currentTemp + (derivative * LookaheadSeconds)) >= TriggerTemp;

                        if (safetyTrigger || predictiveTrigger)
                        {
                            isControlling = true;
                            integral = 0; // Reset integral to prevent initial over-correction spike
                            Console.ForegroundColor = ConsoleColor.Magenta;
                            if (predictiveTrigger && !safetyTrigger)
                                Console.WriteLine($"\n[PREDICTIVE TRIGGER] Temp {currentTemp}°C rising at {derivative:F1}°C/s. Engaging early.");
                            else
                                Console.WriteLine($"\n[SAFETY TRIGGER] Temp hit {currentTemp}°C. Engaging PID control.");
                            Console.ResetColor();
                        }
                    }

                    // 2. PID CONTROL EXECUTION
                    if (isControlling)
                    {
                    // Error is calculated against the Target (75), NOT the Trigger (80)
                    double error = currentTemp - TargetTemp;

                    double P = Kp * error;

                    integral += (error * dt);
                    // Anti-windup: Prevent the integral from accumulating too much extreme correction
                    if (integral > 250) integral = 250;
                    if (integral < -50) integral = -50;
                    double I = Ki * integral;

                    // Only apply derivative if temp is rising (we don't want to over-correct while cooling down)
                    double D = (derivative > 0) ? (Kd * derivative) : 0;

                    // Calculate total power reduction needed
                    double powerReduction = P + I + D;
                    int newPower = (int)Math.Floor(MaxPower - powerReduction);

                    // Clamp to hardware limits
                    if (newPower > MaxPower) newPower = MaxPower;
                    if (newPower < MinPower) newPower = MinPower;

                    // Apply if changed
                    if (newPower != currentPowerLimit)
                    {
                        bool success = SetPowerLimitCommandLine($"-pl {newPower}", out string? plError);
                        if (success)
                        {
                            currentPowerLimit = newPower;

                            ConsoleColor color = currentTemp >= TargetTemp ? ConsoleColor.Yellow : ConsoleColor.Cyan;
                            Console.ForegroundColor = color;
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Temp: {currentTemp}°C | Target: {TargetTemp}°C | Limiting Power to: {newPower}W");
                            Console.ResetColor();
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Warning: Failed to set power limit to {newPower}W: {plError}");
                            Console.ResetColor();
                        }
                    }

                    // Exit condition: Check regardless of whether the nvidia-smi call succeeded
                    if (currentTemp <= TargetTemp - 2 && currentPowerLimit == MaxPower)
                    {
                        isControlling = false;
                        integral = 0; // Reset integral state
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"\n[STABLE] Temp settled at {currentTemp}°C. Full power restored. Returning to idle monitoring.");
                        Console.ResetColor();
                    }

                    Thread.Sleep(250); // Fast loop when controlling
                    }
                    else
                    {
                        Thread.Sleep(1500); // Slow, low-overhead loop when idle
                    }

                    lastTemp = currentTemp;
                    lastTime = now;
                }
            }
            finally
            {
                Console.WriteLine("Shutting down NVML...");
                NVML.nvmlShutdown();
            }
        }

        // --- GPU SELECTION ---

        static IntPtr SelectGpu()
        {
            uint deviceCount;
            int result = NVML.nvmlDeviceGetCount_v2(out deviceCount);
            if (result != 0)
            {
                Console.WriteLine($"Failed to get GPU device count (error code: {result}).");
                NVML.nvmlShutdown();
                Environment.Exit(1);
                return IntPtr.Zero; // Unreachable, but satisfies compiler
            }

            var gpus = new List<(int index, string name)>();

            for (uint i = 0; i < deviceCount; i++)
            {
                IntPtr handle;
                result = NVML.nvmlDeviceGetHandleByIndex_v2(i, out handle);
                if (result != 0)
                    continue;

                StringBuilder nameBuilder = new StringBuilder(64);
                result = NVML.nvmlDeviceGetName_v2(handle, nameBuilder, nameBuilder.Capacity);
                string name = result == 0 ? nameBuilder.ToString() : $"Device {i}";

                gpus.Add(((int)i, name));
            }

            if (gpus.Count == 0)
            {
                Console.WriteLine("No NVIDIA GPUs found.");
                NVML.nvmlShutdown();
                Environment.Exit(1);
                return IntPtr.Zero;
            }

            IntPtr selectedHandle;

            if (gpus.Count == 1)
            {
                // Auto-select the single GPU
                Console.WriteLine($"Found 1 NVIDIA GPU: {gpus[0].name}");
                result = NVML.nvmlDeviceGetHandleByIndex_v2((uint)gpus[0].index, out selectedHandle);
                if (result != 0)
                {
                    Console.WriteLine($"Failed to get handle for GPU at index {gpus[0].index}.");
                    NVML.nvmlShutdown();
                    Environment.Exit(1);
                }
            }
            else
            {
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

                int gpuIndex = gpus[selectedIndex - 1].index;
                Console.WriteLine($"Selected: {gpus[selectedIndex - 1].name}");
                result = NVML.nvmlDeviceGetHandleByIndex_v2((uint)gpuIndex, out selectedHandle);
                if (result != 0)
                {
                    Console.WriteLine($"Failed to get handle for selected GPU at index {gpuIndex}.");
                    NVML.nvmlShutdown();
                    Environment.Exit(1);
                }
            }

            return selectedHandle;
        }

        // --- HELPER METHODS ---

        static uint GetTemperature(IntPtr gpuHandle)
        {
            uint temp = 0;
            int result = NVML.nvmlDeviceGetTemperature(gpuHandle, 0, out temp); // 0 = NVML_TEMPERATURE_GPU
            if (result == 0)
            {
                lastKnownGoodTemp = temp;
                consecutiveReadFailures = 0; // Reset failure counter on successful read
                return temp;
            }
            else
            {
                consecutiveReadFailures++;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Warning: Failed to read GPU temperature (NVML error: {result}). Using last known temperature: {lastKnownGoodTemp}°C (consecutive failures: {consecutiveReadFailures})");
                Console.ResetColor();

                // Fail-safe: after 5 consecutive read failures, return EmergencyTemp to trigger emergency minimum power
                if (consecutiveReadFailures >= 5)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[FAILSAFE] {consecutiveReadFailures} consecutive temperature read failures. Assuming worst-case and returning EmergencyTemp.");
                    Console.ResetColor();
                    return EmergencyTemp;
                }

                return lastKnownGoodTemp;
            }
        }

        static bool SetPowerLimitCommandLine(string arguments, out string? errorMessage)
        {
            errorMessage = null;

            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo.FileName = "nvidia-smi";
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardError = true;

                    process.Start();
                    string? errorOutput = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode != 0 || !string.IsNullOrEmpty(errorOutput))
                    {
                        errorMessage = string.IsNullOrEmpty(errorOutput)
                            ? $"nvidia-smi exited with code {process.ExitCode}"
                            : errorOutput.Trim();
                        return false;
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
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

    // --- NVML P/Invoke Definitions ---
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
    }
}