using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace GpuThermalController
{
    class Program
    {
        // --- CONFIGURATION ---
        const uint TriggerTemp = 80;       // Temperature that triggers the control loop
        const uint TargetTemp = 75;        // Temperature the PID loop will attempt to maintain
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
            if (NVML.nvmlInit_v2() != 0)
            {
                Console.WriteLine("Failed to initialize NVML. Ensure NVIDIA drivers are installed.");
                return;
            }

            IntPtr gpuHandle;
            if (NVML.nvmlDeviceGetHandleByIndex_v2(0, out gpuHandle) != 0)
            {
                Console.WriteLine("Failed to get GPU handle.");
                return;
            }

            Console.WriteLine("Enabling Persistence Mode...");
            SetPowerLimitCommandLine("-pm 1"); // Set persistence mode via nvidia-smi

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Started Thermal Controller.");
            Console.WriteLine($"Trigger: {TriggerTemp}°C | Target Stability: {TargetTemp}°C | Max Power: {MaxPower}W");
            Console.ResetColor();

            DateTime lastTime = DateTime.UtcNow;
            uint lastTemp = GetTemperature(gpuHandle);

            // Setup graceful exit
            Console.CancelKeyPress += (sender, e) =>
            {
                Console.WriteLine("\nExiting... Restoring Max Power.");
                SetPowerLimitCommandLine($"-pl {MaxPower}");
                NVML.nvmlShutdown();
            };

            // MAIN LOOP
            while (true)
            {
                uint currentTemp = GetTemperature(gpuHandle);
                DateTime now = DateTime.UtcNow;
                double dt = (now - lastTime).TotalSeconds;
                if (dt <= 0) dt = 0.001; // Prevent division by zero

                double derivative = (currentTemp - lastTemp) / dt;

                // 1. PREDICTIVE & SAFETY TRIGGERS
                if (!isControlling)
                {
                    bool safetyTrigger = currentTemp >= TriggerTemp;
                    bool predictiveTrigger = currentTemp >= 70 && (currentTemp + (derivative * LookaheadSeconds)) >= TriggerTemp;

                    if (safetyTrigger || predictiveTrigger)
                    {
                        isControlling = true;
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
                    int newPower = (int)Math.Round(MaxPower - powerReduction);

                    // Clamp to hardware limits
                    if (newPower > MaxPower) newPower = MaxPower;
                    if (newPower < MinPower) newPower = MinPower;

                    // Apply if changed
                    if (newPower != currentPowerLimit)
                    {
                        SetPowerLimitCommandLine($"-pl {newPower}");
                        currentPowerLimit = newPower;
                        
                        ConsoleColor color = currentTemp >= TargetTemp ? ConsoleColor.Yellow : ConsoleColor.Cyan;
                        Console.ForegroundColor = color;
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Temp: {currentTemp}°C | Target: {TargetTemp}°C | Limiting Power to: {newPower}W");
                        Console.ResetColor();
                    }

                    // Exit condition: If we are fully cooled below target AND power is fully restored to max
                    if (currentTemp <= TargetTemp - 2 && newPower == MaxPower)
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

        // --- HELPER METHODS ---

        static uint GetTemperature(IntPtr gpuHandle)
        {
            uint temp = 0;
            NVML.nvmlDeviceGetTemperature(gpuHandle, 0, out temp); // 0 = NVML_TEMPERATURE_GPU
            return temp;
        }

        static void SetPowerLimitCommandLine(string arguments)
        {
            // We use standard Process.Start to call nvidia-smi for actuation. 
            // This is safer than messing with NVML power actuation which requires higher-level elevation logic.
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "nvidia-smi";
                process.StartInfo.Arguments = arguments;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                process.WaitForExit();
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

    // --- NVML P/INVOKE DEFINITIONS ---
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
    }
}