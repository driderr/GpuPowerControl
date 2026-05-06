using GpuThermalController.Core;
using Xunit;

namespace GpuThermalController.Tests;

public class PidControllerTests
{
    private PidController CreateController(
        double? kp = null,
        double? ki = null,
        double? kd = null,
        uint? targetTemp = null,
        int? maxPower = null,
        int? minPower = null)
    {
        var config = new ThermalControllerConfig();
        return new PidController(
            kp ?? config.Kp,
            ki ?? config.Ki,
            kd ?? config.Kd,
            targetTemp ?? config.TargetTemp,
            maxPower ?? config.DefaultMaxPower,
            minPower ?? config.DefaultMinPower,
            config.IntegralMax,
            config.IntegralMin,
            config.IntegralBand,
            config.MinimumDt);
    }

    // --- Proportional Term Tests ---

    [Fact]
    public void CalculatePowerLimit_ZeroErrorProducesNoReduction()
    {
        var controller = CreateController();
        int result = controller.CalculatePowerLimit(75, 75, 0.25);

        // At target temp with no change, power should be max (600W)
        Assert.Equal(600, result);
    }

    [Fact]
    public void CalculatePowerLimit_PositiveErrorProducesProportionalReduction()
    {
        var controller = CreateController(kp: 10.0, ki: 0, kd: 0); // Only P term
        int result = controller.CalculatePowerLimit(80, 80, 0.25);

        // Error = 80 - 75 = 5, P = 10 * 5 = 50, power = 600 - 50 = 550
        Assert.Equal(550, result);
    }

    [Fact]
    public void CalculatePowerLimit_NegativeErrorIncreasesPowerToMax()
    {
        var controller = CreateController(kp: 10.0, ki: 0, kd: 0); // Only P term
        int result = controller.CalculatePowerLimit(70, 70, 0.25);

        // Error = 70 - 75 = -5, P = 10 * -5 = -50, power = 600 - (-50) = 650, clamped to 600
        Assert.Equal(600, result);
    }

    // --- Integral Term Tests ---

    [Fact]
    public void CalculatePowerLimit_IntegralAccumulatesOverTime()
    {
        var controller = CreateController(kp: 0, ki: 1.0, kd: 0); // Only I term
        // First call: error = 5, integral = 0 + (5 * 0.25) = 1.25
        int result1 = controller.CalculatePowerLimit(80, 80, 0.25);
        // Second call: error = 5, integral = 1.25 + (5 * 0.25) = 2.5
        int result2 = controller.CalculatePowerLimit(80, 80, 0.25);

        // result1: I = 1.0 * 1.25 = 1.25, power = 600 - 1 = 598 (floored)
        // result2: I = 1.0 * 2.5 = 2.5, power = 600 - 2 = 597 (floored)
        Assert.True(result2 < result1, "Integral should cause more reduction over time");
        Assert.True(controller.Integral > 0);
    }

    [Fact]
    public void CalculatePowerLimit_AntiWindupClampsIntegralAtMaximum()
    {
        var controller = CreateController(kp: 0, ki: 100.0, kd: 0);
        // Drive integral very high by calling many times with large error
        for (int i = 0; i < 10; i++)
        {
            controller.CalculatePowerLimit(100, 100, 1.0); // error = 25, dt = 1.0
        }

        // Integral should be clamped at 250
        Assert.True(controller.Integral <= 250);
    }

    [Fact]
    public void CalculatePowerLimit_AntiWindupClampsIntegralAtMinimum()
    {
        var controller = CreateController(kp: 0, ki: 100.0, kd: 0);
        // Drive integral very negative by calling with negative error
        for (int i = 0; i < 10; i++)
        {
            controller.CalculatePowerLimit(50, 50, 1.0); // error = -25, dt = 1.0
        }

        // Integral should be clamped at -50
        Assert.True(controller.Integral >= -50);
    }

    [Fact]
    public void CalculatePowerLimit_IntegralExposedForTesting()
    {
        var controller = CreateController(kp: 0, ki: 1.0, kd: 0);
        Assert.Equal(0, controller.Integral);

        controller.CalculatePowerLimit(80, 80, 0.25); // error = 5
        Assert.True(controller.Integral > 0);
    }

    // --- Derivative Term Tests ---

    [Fact]
    public void CalculatePowerLimit_DerivativeAppliesWhenRising()
    {
        var controller = CreateController(kp: 0, ki: 0, kd: 10.0); // Only D term
        // Temp rising from 70 to 80 in 0.25s = 40 C/s
        int result = controller.CalculatePowerLimit(80, 70, 0.25);

        // derivative = (80-70)/0.25 = 40, D = 10 * 40 = 400, power = 600 - 400 = 200
        Assert.Equal(200, result);
    }

    [Fact]
    public void CalculatePowerLimit_DerivativeZeroWhenFalling()
    {
        var controller = CreateController(kp: 0, ki: 0, kd: 10.0); // Only D term
        // Temp falling from 80 to 70 in 0.25s
        int result = controller.CalculatePowerLimit(70, 80, 0.25);

        // derivative negative, so D = 0, power = 600
        Assert.Equal(600, result);
    }

    [Fact]
    public void CalculatePowerLimit_DerivativeZeroWhenStable()
    {
        var controller = CreateController(kp: 0, ki: 0, kd: 10.0); // Only D term
        int result = controller.CalculatePowerLimit(75, 75, 0.25);

        // No change, derivative = 0, power = 600
        Assert.Equal(600, result);
    }

    // --- Combined PID Tests ---

    [Fact]
    public void CalculatePowerLimit_FullPidCalculationWithAllTerms()
    {
        var controller = CreateController(kp: 8.0, ki: 0.5, kd: 2.5);
        // Rising from 70 to 82 in 0.25s, error = 7
        int result = controller.CalculatePowerLimit(82, 70, 0.25);

        // P = 8 * 7 = 56
        // I = 0.5 * (7 * 0.25) = 0.5 * 1.75 = 0.875
        // D = 2.5 * (12/0.25) = 2.5 * 48 = 120
        // Total reduction = 56 + 0.875 + 120 = 176.875
        // Power = 600 - 176 = 423 (floored)
        Assert.True(result < 600, "Power should be reduced");
        Assert.True(result >= 150, "Power should be above minimum");
    }

    [Fact]
    public void CalculatePowerLimit_ClampsToMinimumPower()
    {
        var controller = CreateController(kp: 100.0, ki: 0, kd: 0);
        // Huge error: 150 - 75 = 75, P = 100 * 75 = 7500
        int result = controller.CalculatePowerLimit(150, 150, 0.25);

        Assert.Equal(150, result); // Clamped to minPower
    }

    [Fact]
    public void CalculatePowerLimit_ClampsToMaximumPower()
    {
        var controller = CreateController(kp: 10.0, ki: 0, kd: 0);
        // Negative error: 50 - 75 = -25, P = 10 * -25 = -250
        // Power = 600 - (-250) = 850, clamped to 600
        int result = controller.CalculatePowerLimit(50, 50, 0.25);

        Assert.Equal(600, result); // Clamped to maxPower
    }

    // --- Reset Tests ---

    [Fact]
    public void Reset_ClearsIntegral()
    {
        var controller = CreateController(kp: 0, ki: 1.0, kd: 0);
        controller.CalculatePowerLimit(80, 80, 0.25);
        Assert.True(controller.Integral > 0);

        controller.Reset();
        Assert.Equal(0, controller.Integral);
    }

    [Fact]
    public void Reset_AllowsFreshStart()
    {
        var controller = CreateController(kp: 0, ki: 1.0, kd: 0);

        // Accumulate integral
        for (int i = 0; i < 5; i++)
            controller.CalculatePowerLimit(80, 80, 0.25);

        int beforeReset = controller.CalculatePowerLimit(80, 80, 0.25);

        controller.Reset();

        int afterReset = controller.CalculatePowerLimit(80, 80, 0.25);

        // After reset, integral starts fresh, so reduction should be much smaller
        Assert.True(afterReset > beforeReset, "After reset, power should be higher (less reduction)");
    }

    // --- Edge Cases ---

    [Fact]
    public void CalculatePowerLimit_SmallDtDoesNotCauseInstability()
    {
        var controller = CreateController(kp: 8.0, ki: 0.5, kd: 2.5);
        // Very small dt, small temp change
        int result = controller.CalculatePowerLimit(75, 75, 0.001);

        Assert.Equal(600, result); // Should be stable at max power
    }

    [Fact]
    public void CalculatePowerLimit_ZeroDtHandledGracefully()
    {
        var controller = CreateController(kp: 8.0, ki: 0.5, kd: 2.5);
        // dt = 0 should be treated as 0.001
        int result = controller.CalculatePowerLimit(75, 75, 0);

        Assert.Equal(600, result);
    }

    [Fact]
    public void CalculatePowerLimit_NegativeDtHandledGracefully()
    {
        var controller = CreateController(kp: 8.0, ki: 0.5, kd: 2.5);
        int result = controller.CalculatePowerLimit(75, 75, -1.0);

        Assert.Equal(600, result);
    }

    [Fact]
    public void CalculatePowerLimit_LargeTemperatureJumpClamped()
    {
        var controller = CreateController(kp: 8.0, ki: 0.5, kd: 2.5);
        // Massive jump: 0 to 200 in 0.25s
        int result = controller.CalculatePowerLimit(200, 0, 0.25);

        Assert.Equal(150, result); // Should be clamped to minimum
    }
}