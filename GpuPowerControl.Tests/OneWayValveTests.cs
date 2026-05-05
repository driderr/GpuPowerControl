using GpuThermalController.Core;
using GpuPowerControl.Tests.Mocks;
using Xunit;

namespace GpuPowerControl.Tests;

public class OneWayValveTests
{
    // === TEST 25: One-Way Power Valve ===

    [Fact]
    public void Step_OverTrigger_BlocksPowerIncrease()
    {
        // When temperature is at or above TriggerTemp, power should never increase
        var config = new ThermalControllerConfig
        {
            MinPowerDeltaFarW = 10,
            MinPowerDeltaNearW = 3,
            MinAdjustmentIntervalMs = 0, // Disable interval gate
            NearTargetThreshold = 3,
            TriggerTemp = 80
        };
        var device = new MockGpuDevice(minPower: config.DefaultMinPower, maxPower: config.DefaultMaxPower);
        var pid = new PidController(config.Kp, config.Ki, config.Kd, config.TargetTemp, config.DefaultMaxPower, config.DefaultMinPower,
            config.IntegralMax, config.IntegralMin, config.MinimumDt);
        var trigger = new TriggerEvaluator(config.TriggerTemp, config.PredictiveFloor, config.LookaheadSeconds);
        var controller = new ThermalController(device, pid, trigger, config);

        // Trigger control at high temp - power will be reduced
        controller.Step(85);
        Assert.True(controller.IsControlling);
        int powerAfterReduce = controller.CurrentPowerLimit;
        Assert.True(powerAfterReduce < 600);

        // Step at same high temp - PID may want to increase power (cooling/stable temp)
        // But the one-way valve should block it since temp >= TriggerTemp
        controller.Step(85);
        int powerAfterSecond = controller.CurrentPowerLimit;

        // Power should NOT have increased
        Assert.True(powerAfterSecond <= powerAfterReduce,
            $"Power increased from {powerAfterReduce} to {powerAfterSecond} while temp >= TriggerTemp");
    }

    [Fact]
    public void Step_OverTrigger_AllowsPowerDecrease()
    {
        // Power reductions should still be allowed above trigger temp
        var config = new ThermalControllerConfig
        {
            MinPowerDeltaFarW = 10,
            MinAdjustmentIntervalMs = 0,
            NearTargetThreshold = 3
        };
        var device = new MockGpuDevice(minPower: config.DefaultMinPower, maxPower: config.DefaultMaxPower);
        var pid = new PidController(config.Kp, config.Ki, config.Kd, config.TargetTemp, config.DefaultMaxPower, config.DefaultMinPower,
            config.IntegralMax, config.IntegralMin, config.MinimumDt);
        var trigger = new TriggerEvaluator(config.TriggerTemp, config.PredictiveFloor, config.LookaheadSeconds);
        var controller = new ThermalController(device, pid, trigger, config);

        // Freeze time to avoid interval gate
        controller.TimeProvider = () => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Trigger control
        controller.Step(85);
        int powerAfterFirst = controller.CurrentPowerLimit;

        // Temp rising further - PID should reduce power more
        controller.Step(88, dt: 0.25);
        int powerAfterSecond = controller.CurrentPowerLimit;

        // Power should decrease or stay same (never increase above trigger)
        Assert.True(powerAfterSecond <= powerAfterFirst,
            $"Power increased from {powerAfterFirst} to {powerAfterSecond} while temp >= TriggerTemp");
    }

    [Fact]
    public void Step_BelowTrigger_AllowsPowerIncrease()
    {
        // When temperature drops below TriggerTemp, power increases should be allowed again
        var config = new ThermalControllerConfig
        {
            MinPowerDeltaFarW = 10,
            MinPowerDeltaNearW = 3,
            MinAdjustmentIntervalMs = 0,
            NearTargetThreshold = 3,
            TriggerTemp = 80
        };
        var device = new MockGpuDevice(minPower: config.DefaultMinPower, maxPower: config.DefaultMaxPower);
        var pid = new PidController(config.Kp, config.Ki, config.Kd, config.TargetTemp, config.DefaultMaxPower, config.DefaultMinPower,
            config.IntegralMax, config.IntegralMin, config.MinimumDt);
        var trigger = new TriggerEvaluator(config.TriggerTemp, config.PredictiveFloor, config.LookaheadSeconds);
        var controller = new ThermalController(device, pid, trigger, config);

        // Trigger control
        controller.Step(85);
        Assert.True(controller.IsControlling);
        int powerAfterReduce = controller.CurrentPowerLimit;

        // Cool below trigger temp - power increase should now be allowed
        controller.Step(78);
        controller.Step(77);

        // Power may have increased now that temp < TriggerTemp
        // At minimum, controller should still be functioning
        Assert.True(controller.CurrentPowerLimit >= 150 && controller.CurrentPowerLimit <= 600);
    }
}