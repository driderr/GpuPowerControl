using System;
using GpuThermalController.Interfaces;

namespace GpuThermalController.Core
{
    /// <summary>
    /// Preset temperature scenarios for simulated GPU operation.
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
    /// Simulated GPU device that generates realistic, time-varying temperature readings.
    /// Temperature reacts to power limit changes: reducing power causes cooling,
    /// restoring power allows temperatures to rise again.
    /// </summary>
    public class SimulatedGpuDevice : IGpuDevice
    {
        private readonly Random _random;
        private readonly DateTime _startTime;
        private readonly SimulatedGpuConfig _config;

        // Power reactivity state
        private int _currentPowerLimit;
        private readonly int _minPower;
        private readonly int _maxPower;

        // Temperature state
        private double _currentTemp;

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

            _minPower = 150;
            _maxPower = 600;
            _currentPowerLimit = _maxPower;

            Name = "Simulated RTX 4090";

            // Initialize temperature based on scenario base
            _currentTemp = GetBaseTemp();
        }

        public bool GetTemperature(out uint temperature)
        {
            // Always succeed in simulation
            double simulated = CalculateTemperature();
            _currentTemp = simulated;
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
            double elapsedSeconds = (DateTime.UtcNow - _startTime).TotalSeconds;
            double baseTemp = GetBaseTemp();
            double peakTemp = GetPeakTemp();

            // Power factor: 0.0 (min power, max cooling) to 1.0 (max power, no cooling effect)
            double powerFactor = (double)(_currentPowerLimit - _minPower) / (_maxPower - _minPower);

            double scenarioTemp = ScenarioTemperature(elapsedSeconds, baseTemp, peakTemp);

            // Power reactivity: when power is reduced, push temperature downward
            // When power is at max, no cooling effect (workload generates full heat)
            // The cooling effect is gradual - simulates thermal mass
            double coolingEffect = (1.0 - powerFactor) * (scenarioTemp - baseTemp) * 0.6;
            double result = scenarioTemp - coolingEffect;

            // Add small noise for realism (±0.3°C)
            double noise = (_random.NextDouble() - 0.5) * 0.6;
            result += noise;

            // Clamp to reasonable range
            return Math.Clamp(result, baseTemp - 5, peakTemp + 5);
        }

        private double ScenarioTemperature(double elapsed, double baseTemp, double peakTemp)
        {
            return _config.Scenario switch
            {
                SimulationScenario.Idle => ScenarioIdle(elapsed, baseTemp, peakTemp),
                SimulationScenario.Spike => ScenarioSpike(elapsed, baseTemp, peakTemp),
                SimulationScenario.SustainedLoad => ScenarioSustainedLoad(elapsed, baseTemp, peakTemp),
                SimulationScenario.Emergency => ScenarioEmergency(elapsed, baseTemp, peakTemp),
                SimulationScenario.GradualWarmup => ScenarioGradualWarmup(elapsed, baseTemp, peakTemp),
                _ => ScenarioDefault(elapsed, baseTemp, peakTemp),
            };
        }

        /// <summary>
        /// Default: rich variety of patterns cycling over ~5 minutes.
        /// Idle → gradual warm-up → load spikes → cool-down → occasional emergency spike.
        /// </summary>
        private double ScenarioDefault(double elapsed, double baseTemp, double peakTemp)
        {
            double range = peakTemp - baseTemp;

            // Layer 1: Slow 3-minute cycle (idle to moderate load)
            double slowCycle = Math.Sin(elapsed / 300.0 * Math.PI * 2) * 0.3;

            // Layer 2: Medium 60-second load pattern
            double mediumCycle = Math.Sin(elapsed / 60.0 * Math.PI * 2) * 0.25;

            // Layer 3: Fast 10-second micro-variations
            double fastCycle = Math.Sin(elapsed / 10.0 * Math.PI * 2) * 0.1;

            // Layer 4: Periodic spike every ~90 seconds (sharp, short)
            double spikePhase = (elapsed % 90.0) / 90.0;
            double spike = 0;
            if (spikePhase > 0.7 && spikePhase < 0.85)
            {
                // Rising edge of spike
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
            // Normalize to 0-1 range (worst case sum ≈ 1.1, so divide by 1.1)
            double normalized = Math.Clamp(combined / 1.1, 0, 1);

            return baseTemp + range * normalized;
        }

        /// <summary>
        /// Idle: stays around baseTemp with small fluctuations. Never triggers control.
        /// </summary>
        private double ScenarioIdle(double elapsed, double baseTemp, double peakTemp)
        {
            // Stay within baseTemp ± 5°C
            double fluctuation = Math.Sin(elapsed / 8.0 * Math.PI * 2) * 3;
            fluctuation += Math.Sin(elapsed / 3.0 * Math.PI * 2) * 1.5;
            return baseTemp + fluctuation;
        }

        /// <summary>
        /// Spike: quick temperature spike from base to peak in ~10 seconds, then cooldown.
        /// Repeats every 30 seconds.
        /// </summary>
        private double ScenarioSpike(double elapsed, double baseTemp, double peakTemp)
        {
            double range = peakTemp - baseTemp;
            double cyclePhase = (elapsed % 30.0) / 30.0;

            if (cyclePhase < 0.3)
            {
                // Rising: 0-30% of cycle = rapid climb
                double progress = cyclePhase / 0.3;
                return baseTemp + range * Math.Sin(progress * Math.PI * 0.5);
            }
            else if (cyclePhase < 0.5)
            {
                // Hold near peak briefly
                return peakTemp - range * 0.05;
            }
            else
            {
                // Cooling: 50-100% of cycle = gradual cooldown
                double progress = (cyclePhase - 0.5) / 0.5;
                return peakTemp - range * 0.95 * (1.0 - Math.Cos(progress * Math.PI * 0.5));
            }
        }

        /// <summary>
        /// Sustained load: gradually warms to peak and stays there for 30+ seconds.
        /// </summary>
        private double ScenarioSustainedLoad(double elapsed, double baseTemp, double peakTemp)
        {
            double range = peakTemp - baseTemp;

            if (elapsed < 15)
            {
                // Warm-up phase: 15 seconds to reach peak
                double progress = elapsed / 15.0;
                return baseTemp + range * (1.0 - Math.Cos(progress * Math.PI * 0.5));
            }
            else
            {
                // Sustained: hover around peak with small fluctuations
                double fluctuation = Math.Sin(elapsed / 5.0 * Math.PI * 2) * 1.5;
                return peakTemp + fluctuation;
            }
        }

        /// <summary>
        /// Emergency: rapidly climbs to emergency territory (95°C) then holds.
        /// </summary>
        private double ScenarioEmergency(double elapsed, double baseTemp, double peakTemp)
        {
            double emergencyTemp = Math.Max(peakTemp, 95.0);
            double range = emergencyTemp - baseTemp;

            if (elapsed < 8)
            {
                // Rapid climb in 8 seconds
                double progress = elapsed / 8.0;
                return baseTemp + range * progress * progress; // Accelerating
            }
            else
            {
                // Hold near emergency temp
                double fluctuation = Math.Sin(elapsed / 2.0 * Math.PI * 2) * 1;
                return emergencyTemp + fluctuation;
            }
        }

        /// <summary>
        /// Gradual warmup: slow linear climb from base to peak over 2 minutes.
        /// </summary>
        private double ScenarioGradualWarmup(double elapsed, double baseTemp, double peakTemp)
        {
            double range = peakTemp - baseTemp;

            if (elapsed < 120)
            {
                // 2-minute linear climb
                double progress = elapsed / 120.0;
                return baseTemp + range * progress;
            }
            else
            {
                // Hold at peak with slight fluctuation
                double fluctuation = Math.Sin(elapsed / 6.0 * Math.PI * 2) * 1;
                return peakTemp + fluctuation;
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