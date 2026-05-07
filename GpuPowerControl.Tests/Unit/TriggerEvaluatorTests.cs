using GpuThermalController.Core;
using Xunit;

namespace GpuPowerControl.Tests;

public class TriggerEvaluatorTests
{
    private TriggerEvaluator CreateEvaluator(
        uint? triggerTemp = default,
        double? predictiveFloor = default,
        double? lookaheadSeconds = default)
    {
        var config = new ThermalControllerConfig();
        return new TriggerEvaluator(
            triggerTemp ?? config.TriggerTemp,
            predictiveFloor ?? config.PredictiveFloor,
            lookaheadSeconds ?? config.LookaheadSeconds);
    }

    // --- Safety Trigger Tests ---

    [Fact]
    public void Evaluate_SafetyTriggerAtThreshold()
    {
        var evaluator = CreateEvaluator();
        var result = evaluator.Evaluate(80, 0, false);

        Assert.Equal(TriggerResult.Safety, result);
    }

    [Fact]
    public void Evaluate_SafetyTriggerAboveThreshold()
    {
        var evaluator = CreateEvaluator();
        var result = evaluator.Evaluate(90, 0, false);

        Assert.Equal(TriggerResult.Safety, result);
    }

    [Fact]
    public void Evaluate_SafetyTriggerBelowThreshold()
    {
        var evaluator = CreateEvaluator();
        var result = evaluator.Evaluate(79, 0, false);

        Assert.Equal(TriggerResult.None, result);
    }

    [Fact]
    public void Evaluate_SafetyTriggerWithRisingTemp()
    {
        var evaluator = CreateEvaluator();
        var result = evaluator.Evaluate(85, 10.0, false);

        Assert.Equal(TriggerResult.Safety, result);
    }

    // --- Predictive Trigger Tests ---

    [Fact]
    public void Evaluate_PredictiveTriggerWithFastRise()
    {
        var evaluator = CreateEvaluator();
        // temp=75, derivative=5 C/s, lookahead=1.5s -> predicted = 75 + 7.5 = 82.5 >= 80
        var result = evaluator.Evaluate(75, 5.0, false);

        Assert.Equal(TriggerResult.Predictive, result);
    }

    [Fact]
    public void Evaluate_PredictiveTriggerWithSlowRise()
    {
        var evaluator = CreateEvaluator();
        // temp=75, derivative=1 C/s, lookahead=1.5s -> predicted = 75 + 1.5 = 76.5 < 80
        var result = evaluator.Evaluate(75, 1.0, false);

        Assert.Equal(TriggerResult.None, result);
    }

    [Fact]
    public void Evaluate_PredictiveTriggerWithExactPrediction()
    {
        var evaluator = CreateEvaluator();
        // temp=70, derivative=3.333 C/s, lookahead=1.5s -> predicted = 70 + 5 = 75 < 80
        var result = evaluator.Evaluate(70, 3.333, false);

        Assert.Equal(TriggerResult.None, result);
    }

    [Fact]
    public void Evaluate_PredictiveTriggerBelowFloorDisabled()
    {
        var evaluator = CreateEvaluator();
        // temp=65 (below 70 floor), fast rise - should NOT trigger predictively
        var result = evaluator.Evaluate(65, 20.0, false);

        Assert.Equal(TriggerResult.None, result);
    }

    [Fact]
    public void Evaluate_PredictiveTriggerAtFloor()
    {
        var evaluator = CreateEvaluator();
        // temp=70 (at floor), derivative=10 C/s, lookahead=1.5s -> predicted = 70 + 15 = 85 >= 80
        var result = evaluator.Evaluate(70, 10.0, false);

        Assert.Equal(TriggerResult.Predictive, result);
    }

    [Fact]
    public void Evaluate_SafetyTriggerTakesPriorityOverPredictive()
    {
        var evaluator = CreateEvaluator();
        // temp=85 (above trigger), fast rise - safety should fire, not predictive
        var result = evaluator.Evaluate(85, 10.0, false);

        Assert.Equal(TriggerResult.Safety, result);
    }

    // --- Already Controlling Tests ---

    [Fact]
    public void Evaluate_NoTriggerWhenAlreadyControlling()
    {
        var evaluator = CreateEvaluator();
        var result = evaluator.Evaluate(90, 10.0, true);

        Assert.Equal(TriggerResult.None, result);
    }

    [Fact]
    public void Evaluate_NoPredictiveTriggerWhenAlreadyControlling()
    {
        var evaluator = CreateEvaluator();
        var result = evaluator.Evaluate(75, 10.0, true);

        Assert.Equal(TriggerResult.None, result);
    }

    // --- Edge Cases ---

    [Fact]
    public void Evaluate_ZeroDerivativeNoPredictiveTrigger()
    {
        var evaluator = CreateEvaluator();
        // temp=75, derivative=0 -> predicted = 75 < 80
        var result = evaluator.Evaluate(75, 0, false);

        Assert.Equal(TriggerResult.None, result);
    }

    [Fact]
    public void Evaluate_NegativeDerivativeNoPredictiveTrigger()
    {
        var evaluator = CreateEvaluator();
        // temp=75, derivative=-5 (cooling) -> predicted = 75 - 7.5 = 67.5 < 80
        var result = evaluator.Evaluate(75, -5.0, false);

        Assert.Equal(TriggerResult.None, result);
    }

    [Fact]
    public void Evaluate_VeryHighTemperatureTriggersSafety()
    {
        var evaluator = CreateEvaluator();
        var result = evaluator.Evaluate(150, 0, false);

        Assert.Equal(TriggerResult.Safety, result);
    }

    [Fact]
    public void Evaluate_ColdTemperatureNoTrigger()
    {
        var evaluator = CreateEvaluator();
        var result = evaluator.Evaluate(30, 0, false);

        Assert.Equal(TriggerResult.None, result);
    }

    [Fact]
    public void Evaluate_CustomTriggerThreshold()
    {
        var evaluator = CreateEvaluator(triggerTemp: 70);
        var result = evaluator.Evaluate(70, 0, false);

        Assert.Equal(TriggerResult.Safety, result);
    }
}