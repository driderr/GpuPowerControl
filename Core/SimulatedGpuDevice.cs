using System;
using GpuThermalController.Interfaces;

namespace GpuThermalController.Core
{
    /// <summary>
    /// Preset workload profiles for simulated GPU operation.
    /// Each profile defines a target temperature the workload "wants" to reach
    /// when running at full power (600W).
    /// </summary>
    public enum SimulationScenario
    {
        Default,
        Idle,
        Spike,
        SustainedLoad,
        Emergency,
        GradualWarmup
    }

    /// <summary>
    /// Configuration for a simulated GPU device.
    /// </summary>
    public class SimulatedGpuConfig
    {
        public SimulationScenario Scenario { get; set; } = SimulationScenario.Default;
        public int? BaseTemp { get; set; }      // Override base idle temperature (°C)
        public int? PeakTemp { get; set; }      // Override peak temperature (°C)
        public int? Seed { get; set; }          // Random seed for reproducibility
    }

    /// <summary>
    /// Simulated GPU device with a physics-based thermal mass model.
    /// 
    /// Temperature evolves based on:
    ///   dT/dt = (PowerGenerated - PowerDissipated) / ThermalCapacity
    /// 
    /// Where:
    ///   - PowerGenerated = workload intensity * (currentPowerLimit / maxPower)
    ///   - PowerDissipated = thermalConductance * (currentTemp - ambientTemp)
    ///   - ThermalCapacity determines how fast temperature changes (thermal inertia)
    /// 
    /// This means:
    ///   - Reducing power actually cools the GPU (with realistic thermal lag)
    ///   - Temperature cannot jump instantaneously (thermal mass absorbs energy)
    ///   - At equilibrium, power generated equals power dissipated
    /// </summary>
    public class SimulatedGpuDevice : IGpuDevice
    {
        private readonly Random _random;
        private readonly DateTime _startTime;
        private readonly SimulatedGpuConfig _config;

        // Power state
        private int _currentPowerLimit;
        private readonly int _minPower;
        private readonly int _maxPower;

        // Thermal model state
        private double _currentTemp;
        private DateTime _lastUpdateTime;

        // Thermal model parameters
        private const double AmbientTemp = 25.0;        // Room temperature in °C
        private const double ThermalConductance = 8.0;  // W/°C — how fast heat escapes to ambient
        private const double ThermalCapacity = 75.0;    // J/°C — thermal inertia (lower = faster temp changes, more responsive)

        public string Name { get; }
        public int MinPower => _minPower;
        public int MaxPower => _maxPower;

        /// <summary>List of all power limits that were set, in order.</summary>
        public List<int> PowerLimitCalls { get; } = new List<int>();

        public SimulatedGpuDevice(SimulatedGpuConfig? config = null)
        {
            _config = config ?? new SimulatedGpuConfig();
            _random = _config.Seed.HasValue
                ? new Random(_config.Seed.Value)
                : new Random();
            _startTime = DateTime.UtcNow;
            _lastUpdateTime = _startTime;

            _minPower = 150;
            _maxPower = 600;
            _currentPowerLimit = _maxPower;

            Name = "Simulated RTX 4090";

            // Initialize temperature at idle (near base temp)
            _currentTemp = GetBaseTemp();
        }

        public bool GetTemperature(out uint temperature)
        {
            // Always succeed in simulation
            double simulated = CalculateTemperature();
            temperature = (uint)Math.Max(0, Math.Round(simulated));
            return true;
        }

        public bool SetPowerLimit(int watts, out string? errorMessage)
        {
            errorMessage = null;
            _currentPowerLimit = Math.Clamp(watts, _minPower, _maxPower);
            PowerLimitCalls.Add(_currentPowerLimit);
            return true;
        }

        private double CalculateTemperature()
        {
            DateTime now = DateTime.UtcNow;
            double dt = (now - _lastUpdateTime).TotalSeconds;
            _lastUpdateTime = now;

            // Guard against negative or zero dt
            if (dt <= 0) dt = 0.01;
            if (dt > 5) dt = 5; // Cap at 5 seconds to prevent huge jumps

            // 1. Get workload intensity (0.0 to 1.0) based on scenario and elapsed time
            double elapsed = (now - _startTime).TotalSeconds;
            double workloadIntensity = GetWorkloadIntensity(elapsed);

            // 2. Calculate actual power being dissipated as heat
            // The workload generates heat proportional to the power limit.
            // At full power limit with full workload, GPU generates max heat.
            // At reduced power limit, less heat is generated.
            double powerRatio = (double)_currentPowerLimit / _maxPower;
            double powerGenerated = workloadIntensity * powerRatio * _maxPower; // Watts

            // 3. Calculate heat dissipation (Newton's law of cooling)
            // Heat flows from GPU to ambient proportional to temperature difference
            double tempDiff = _currentTemp - AmbientTemp;
            double powerDissipated = ThermalConductance * tempDiff; // Watts

            // 4. Calculate temperature change: dT = (powerIn - powerOut) * dt / capacity
            double netPower = powerGenerated - powerDissipated;
            double dT = (netPower * dt) / ThermalCapacity;

            // 5. Apply temperature change
            _currentTemp += dT;

            // 6. Add small noise for realism (±0.2°C)
            double noise = (_random.NextDouble() - 0.5) * 0.4;
            double result = _currentTemp + noise;

            // 7. Clamp to reasonable range
            return Math.Clamp(result, AmbientTemp, 110);
        }

        /// <summary>
        /// Returns workload intensity (0.0 to 1.0) based on scenario and elapsed time.
        /// Workload intensity represents how much computational load the GPU is under
        /// at full power. The actual heat generated is scaled by the power limit.
        /// </summary>
        private double GetWorkloadIntensity(double elapsed)
        {
            double baseTemp = GetBaseTemp();
            double peakTemp = GetPeakTemp();

            // The peakTemp represents what the GPU would reach at full power (600W)
            // with full workload. We can reverse-calculate the workload intensity that
            // would produce this equilibrium temperature.
            // At equilibrium: powerGenerated = powerDissipated
            // workload * maxPower = conductance * (peakTemp - ambient)
            // workload = conductance * (peakTemp - ambient) / maxPower
            double maxWorkloadIntensity = ThermalConductance * (peakTemp - AmbientTemp) / _maxPower;

            // Get scenario-based intensity multiplier (0.0 to 1.0)
            double scenarioMultiplier = GetScenarioMultiplier(elapsed, baseTemp, peakTemp);

            return maxWorkloadIntensity * scenarioMultiplier;
        }

        private double GetScenarioMultiplier(double elapsed, double baseTemp, double peakTemp)
        {
            return _config.Scenario switch
            {
                SimulationScenario.Idle => ScenarioIdleMultiplier(elapsed),
                SimulationScenario.Spike => ScenarioSpikeMultiplier(elapsed),
                SimulationScenario.SustainedLoad => ScenarioSustainedLoadMultiplier(elapsed),
                SimulationScenario.Emergency => ScenarioEmergencyMultiplier(elapsed),
                SimulationScenario.GradualWarmup => ScenarioGradualWarmupMultiplier(elapsed),
                _ => ScenarioDefaultMultiplier(elapsed),
            };
        }

        /// <summary>
        /// Default: rich variety of patterns cycling over ~5 minutes.
        /// </summary>
        private double ScenarioDefaultMultiplier(double elapsed)
        {
            // Layer 1: Slow 3-minute cycle
            double slowCycle = Math.Sin(elapsed / 300.0 * Math.PI * 2) * 0.3;

            // Layer 2: Medium 60-second load pattern
            double mediumCycle = Math.Sin(elapsed / 60.0 * Math.PI * 2) * 0.25;

            // Layer 3: Fast 10-second micro-variations
            double fastCycle = Math.Sin(elapsed / 10.0 * Math.PI * 2) * 0.1;

            // Layer 4: Periodic spike every ~90 seconds
            double spikePhase = (elapsed % 90.0) / 90.0;
            double spike = 0;
            if (spikePhase > 0.7 && spikePhase < 0.85)
            {
                double spikeProgress = (spikePhase - 0.7) / 0.15;
                spike = Math.Sin(spikeProgress * Math.PI) * 0.35;
            }

            // Layer 5: Occasional emergency spike every ~3 minutes
            double emergencyPhase = (elapsed % 180.0) / 180.0;
            double emergency = 0;
            if (emergencyPhase > 0.85 && emergencyPhase < 0.9)
            {
                double emergencyProgress = (emergencyPhase - 0.85) / 0.05;
                emergency = Math.Sin(emergencyProgress * Math.PI) * 0.15;
            }

            double combined = slowCycle + mediumCycle + fastCycle + spike + emergency;
            return Math.Clamp(combined / 1.1 + 0.5, 0, 1); // Center around 0.5
        }

        /// <summary>
        /// Idle: low workload, stays cool.
        /// </summary>
        private double ScenarioIdleMultiplier(double elapsed)
        {
            // Small fluctuations around 0.15 intensity
            double fluctuation = Math.Sin(elapsed / 8.0 * Math.PI * 2) * 0.05;
            return Math.Clamp(0.15 + fluctuation, 0, 1);
        }

        /// <summary>
        /// Spike: ramp to full workload, hold, then cooldown.
        /// Repeats every 45 seconds (15s rising + 10s hold + 20s cooling).
        /// </summary>
        private double ScenarioSpikeMultiplier(double elapsed)
        {
            double cyclePhase = (elapsed % 45.0) / 45.0;

            if (cyclePhase < 0.33)
            {
                // Rising: 15s ramp to full workload
                double progress = cyclePhase / 0.33;
                return Math.Sin(progress * Math.PI * 0.5);
            }
            else if (cyclePhase < 0.56)
            {
                // Hold near full workload for ~10s
                return 0.95;
            }
            else
            {
                // Cooling: 20s cooldown to zero workload
                double progress = (cyclePhase - 0.56) / 0.44;
                return 1.0 - (1.0 - Math.Cos(progress * Math.PI * 0.5));
            }
        }

        /// <summary>
        /// Sustained load: gradually ramps to full workload and stays there.
        /// </summary>
        private double ScenarioSustainedLoadMultiplier(double elapsed)
        {
            if (elapsed < 15)
            {
                // Warm-up: 15 seconds to reach full workload
                double progress = elapsed / 15.0;
                return 1.0 - Math.Cos(progress * Math.PI * 0.5);
            }
            else
            {
                // Sustained: full workload with small fluctuations
                double fluctuation = Math.Sin(elapsed / 5.0 * Math.PI * 2) * 0.03;
                return Math.Clamp(1.0 + fluctuation, 0, 1);
            }
        }

        /// <summary>
        /// Emergency: rapidly ramps to extreme workload.
        /// </summary>
        private double ScenarioEmergencyMultiplier(double elapsed)
        {
            if (elapsed < 8)
            {
                // Rapid climb — accelerating workload
                double progress = elapsed / 8.0;
                return progress * progress;
            }
            else
            {
                // Hold near maximum workload
                double fluctuation = Math.Sin(elapsed / 2.0 * Math.PI * 2) * 0.02;
                return Math.Clamp(1.0 + fluctuation, 0, 1);
            }
        }

        /// <summary>
        /// Gradual warmup: slow linear increase in workload over 2 minutes.
        /// </summary>
        private double ScenarioGradualWarmupMultiplier(double elapsed)
        {
            if (elapsed < 120)
            {
                // 2-minute linear ramp to full workload
                return elapsed / 120.0;
            }
            else
            {
                // Hold at full workload with slight fluctuation
                double fluctuation = Math.Sin(elapsed / 6.0 * Math.PI * 2) * 0.02;
                return Math.Clamp(1.0 + fluctuation, 0, 1);
            }
        }

        private double GetBaseTemp()
        {
            if (_config.BaseTemp.HasValue)
                return _config.BaseTemp.Value;

            return _config.Scenario switch
            {
                SimulationScenario.Idle => 42.0,
                SimulationScenario.Spike => 45.0,
                SimulationScenario.SustainedLoad => 40.0,
                SimulationScenario.Emergency => 45.0,
                SimulationScenario.GradualWarmup => 35.0,
                _ => 45.0, // Default
            };
        }

        private double GetPeakTemp()
        {
            if (_config.PeakTemp.HasValue)
                return _config.PeakTemp.Value;

            return _config.Scenario switch
            {
                SimulationScenario.Idle => 48.0,
                SimulationScenario.Spike => 85.0,
                SimulationScenario.SustainedLoad => 82.0,
                SimulationScenario.Emergency => 95.0,
                SimulationScenario.GradualWarmup => 80.0,
                _ => 80.0, // Default
            };
        }
    }
}