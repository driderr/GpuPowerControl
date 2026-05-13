# GpuPowerControl

[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/en-us/download)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Build: Passing](https://img.shields.io/badge/build-passing-brightgreen.svg)](#)

A real-time GPU thermal management utility that dynamically controls NVIDIA GPU power limits using a PID controller based on real-time temperature monitoring.

## Download

- **[GpuPowerControl v1.0](https://github.com/driderr/GpuPowerControl/releases/tag/v1.0)** - Latest release

## Table of Contents

- [Download](#download)
- [Description](#description)
- [Features](#features)
- [How It Works](#how-it-works)
  - [Control Flow](#control-flow)
  - [State Transitions](#state-transitions)
  - [Safety Mechanisms](#safety-mechanisms)
  - [PID Control Logic](#pid-control-logic)
- [Installation](#installation)
- [Usage](#usage)
  - [Simulation Scenarios](#simulation-scenarios)
  - [Dashboard Controls](#dashboard-controls)
- [Configuration](#configuration)
  - [External Configuration File (appsettings.json)](#external-configuration-file-appsettingsjson)
- [Architecture](#architecture)
- [Testing](#testing)
- [Contributing](#contributing)
- [License](#license)
- [Acknowledgments](#acknowledgments)

## Description

GpuPowerControl solves the problem of automatic thermal management for consumer NVIDIA GPUs that lack native temperature limiting. Instead of static power limits or manual adjustment, it uses a PID (Proportional-Integral-Derivative) control loop to smoothly regulate GPU power in response to temperature changes.

When the GPU temperature exceeds a configurable trigger threshold, the PID controller engages and reduces power to cool the GPU. Once temperature returns to safe levels, full power is gradually restored.

## Features

- **PID-Based Thermal Control**: Configurable Kp/Ki/Kd gains for precise temperature regulation
- **Predictive Triggers**: Derivative-based temperature prediction activates control before hitting threshold
- **Emergency Safety**: Automatic minimum power enforcement at critical temperatures (default 90°C)
- **Real-Time Dashboard**: Spectre.Console TUI with live metrics, PID breakdown, and state visualization
- **Toast Notifications**: Windows 11 toast notifications for thermal events
- **Data Export**: CSV and JSON logging of thermal data
- **Simulated GPU Mode**: Test without hardware using a physics-based thermal model
- **NVIDIA NVML Integration**: Direct GPU hardware access via NVML P/Invoke

## How It Works

The thermal controller operates in distinct states:

### Control Flow

```
[1] IDLE - Slow temperature monitoring
        │
        ▼
[2] TRIGGER - Temperature or derivative exceeds threshold
        │  (Safety: temp ≥ 80°C OR Predictive: rising fast)
        ▼
[3] CONTROLLING - PID controller adjusts power limit
        │
        ▼
[4] EMERGENCY - Temperature ≥ 90°C
        │  Forces minimum power (150W) for 5 seconds
        ▼
[5] EMERGENCY RECOVERY - Rate-limited PID recovery (5 W/s)
        │
        ▼
[6] STABLE - Temperature ≤ Target - Hysteresis AND power at max
        │  Returns to idle monitoring
```

### State Transitions

| From | To | Condition |
|------|-----|-----------|
| Idle | Controlling | Temp ≥ 80°C or predictive trigger fires |
| Controlling | Emergency | Temp ≥ 90°C |
| Emergency | Recovery | Emergency hold timer expires |
| Recovery | Controlling | Normal PID resumes |
| Controlling/Recovery | Stable | Temp ≤ TargetTemp - ExitHysteresis AND power at max |

### Safety Mechanisms

- **Consecutive Read Failures**: After 5 failed temperature reads, forces emergency temperature to trigger safety path
- **Power Increase Rate Limit**: Maximum 15 W/s during normal PID, 5 W/s during emergency recovery
- **Power Increase Gate**: Blocks power increases while temperature is at or above trigger threshold
- **Minimum Delta Gate**: Prevents tiny power reductions from noise (3W near target, 10W far from target)
- **Minimum Adjustment Interval**: 2.5s cooldown between power changes (bypassed if temp rising ≥ 2°C/s)

### PID Control Logic

The PID controller calculates power adjustments using:
- **Proportional (Kp)**: Responds to current temperature error
- **Integral (Ki)**: Accumulates past temperature errors
- **Derivative (Kd)**: Responds to temperature rate of change

```
Power Limit = Base Power - Kp·error - Ki·∫error·dt - Kd·d(error)/dt
```

## Installation

### Prerequisites

- **.NET 10 SDK** (https://dotnet.microsoft.com/download)
- **NVIDIA GPU** with up-to-date drivers (for NVML support)
- **Windows 10/11** (for toast notifications)

### Build & Run

```bash
# Clone the repository
git clone https://github.com/driderr/GpuPowerControl.git
cd GpuPowerControl

# Restore dependencies
dotnet restore

# Build
dotnet build

# Run with real GPU
dotnet run

# Run with simulated GPU (no hardware needed)
dotnet run --simulate

# Build standalone executable
build-exe.cmd
```

## Usage

### Command Line Options

```bash
# Use simulated GPU device
dotnet run --simulate

# Run with a specific scenario
dotnet run --simulate --scenario Default
dotnet run --simulate --scenario Idle
dotnet run --simulate --scenario Spike
dotnet run --simulate --scenario SustainedLoad
dotnet run --simulate --scenario Emergency
dotnet run --simulate --scenario GradualWarmup

# Custom simulation parameters
dotnet run --simulate --scenario Default --base-temp 40 --peak-temp 85 --seed 42
```

### Simulation Scenarios

| Scenario | Description | Peak Temp |
|-----------|-------------|-----------|
| `Default` | Rich multi-layer pattern with slow/fast cycles and periodic spikes | 80°C |
| `Idle` | Low workload, stays cool | 48°C |
| `Spike` | 15s ramp → 10s hold → 20s cooldown (45s cycle) | 85°C |
| `SustainedLoad` | Gradual warmup then sustained full workload | 82°C |
| `Emergency` | Rapid 8s climb to extreme temperatures | 120°C |
| `GradualWarmup` | 2-minute linear warmup to full load | 80°C |

### Dashboard Controls

When the console dashboard is active:
- **ESC**: Exit application
- **P**: Adjust PID coefficients interactively
- **T**: Test error/warning console output
- **L**: Toggle log panel visibility
- **J**: Toggle JSON logging
- **C**: Toggle config panel visibility

## Configuration

GpuPowerControl uses `ThermalControllerConfig` for all settings. Key parameters:

| Parameter | Default | Description |
|-----------|---------|-------------|
| `TriggerTemp` | 80°C | Temperature to activate PID control |
| `TargetTemp` | 75°C | Target temperature for PID controller |
| `EmergencyTemp` | 90°C | Critical temperature for emergency power limit |
| `DefaultMinPower` | 150W | Minimum power limit (emergency) |
| `DefaultMaxPower` | 600W | Maximum power limit (full power) |
| `Kp` | 20.0 | Proportional gain |
| `Ki` | 0.5 | Integral gain |
| `Kd` | 2.5 | Derivative gain |
| `IdleSleepMs` | 1500ms | Sleep interval when idle |
| `ControllingSleepMs` | 250ms | Sleep interval when controlling |

### External Configuration File (`appsettings.json`)

GpuPowerControl supports an external JSON configuration file that allows you to customize all thermal control parameters without recompilation. Place `appsettings.json` in the same directory as `GpuPowerControl.exe`.

#### Setup

1. Copy the sample `appsettings.json` (included in the repository root) next to the executable
2. Edit the values to match your GPU model and thermal preferences
3. Restart the application to apply changes

#### Comment Support

The configuration loader strips both line comments (`//`) and block comments (`/* */`) before parsing the JSON. This allows you to annotate your configuration file with explanations for each setting:

```json
{
  // Thermal thresholds
  "thermal": {
    "triggerTemp": 80,
    "targetTemp": 75
  }
}
```

#### Configuration Sections

The `appsettings.json` file contains the following sections:

| Section | Description |
|---------|-------------|
| `thermal` | Temperature thresholds (trigger, target, emergency, hysteresis, predictive triggering) |
| `pid` | PID controller tuning (Kp, Ki, Kd, integral clamping) |
| `power` | Power limits and gating (min/max power, rate limits, adjustment intervals) |
| `timing` | Control loop timing (PID interval, sleep durations) |
| `faultTolerance` | Error handling (max consecutive read failures) |

#### CLI Override

Command-line arguments override the corresponding `appsettings.json` values. This allows you to quickly test different configurations without editing the file.

## Architecture

```
Program.cs (entry point)
├── ThermalController (orchestration)
│   ├── IGpuDevice (hardware abstraction)
│   │   ├── NvmlGpuDevice (NVML P/Invoke)
│   │   ├── SimulatedGpuDevice (physics-based thermal model)
│   │   └── MockGpuDevice (test mock)
│   ├── PidController (control algorithm)
│   ├── TriggerEvaluator (trigger logic)
│   └── Events: OnStateChange, OnStep
│
├── DashboardDataProvider (event consumer)
│   └── IDashboardDataProvider
│       ├── ConsoleDashboard (Spectre.Console UI)
│       ├── JsonPublisher (JSON file output)
│       ├── CsvExporter (CSV export)
│       └── ErrorConsole (error display)
│
└── ToastNotificationService (Windows notifications)
```

### Directory Structure

| Directory | Description |
|-----------|-------------|
| `Core/` | Thermal control logic (ThermalController, PidController, TriggerEvaluator, config) |
| `Dashboard/` | Console UI components (Spectre.Console rendering, CSV/JSON export, key handling) |
| `Devices/` | GPU device abstractions (NVIDIA NVML, simulated GPU, test mocks) |
| `Notifications/` | Windows toast notification service |
| `Nvml/` | NVIDIA NVML P/Invoke bindings |
| `Profiling/` | Performance profiling tools and scripts |
| `GpuPowerControl.Tests/` | xUnit test suite (unit + integration tests) |

## Testing

The project includes xUnit tests:

```bash
# Run all tests
dotnet test

# Run with minimal output
dotnet test --logger "console;verbosity=minimal"
```

Test structure:
- **Unit tests**: PID controller, trigger evaluator, dashboard components
- **Integration tests**: End-to-end thermal control loop
- **Mocks**: `MockGpuDevice` for testing without hardware

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Make your changes
4. Add tests for new functionality
5. Run `dotnet test` to ensure all tests pass
6. Commit and push your branch
7. Submit a pull request

## License

This project is licensed under the [MIT License](LICENSE).

## Acknowledgments

- Built with [Spectre.Console](https://spectreconsole.net/) for the TUI
- Uses [CommunityToolkit.MSIX](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk) for toast notifications
- NVIDIA [NVML](https://docs.nvidia.com/deploy/gpu-workload-management/nvidia-management-library/) for GPU hardware access
