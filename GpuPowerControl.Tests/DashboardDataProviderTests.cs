using GpuPowerControl.Tests.Mocks;
using GpuThermalController.Core;
using GpuThermalController.Dashboard;
using Xunit;

namespace GpuPowerControl.Tests;

public class DashboardDataProviderTests
{
    private MockGpuDevice CreateDevice() => new MockGpuDevice("Test GPU", 150, 600);

    private ThermalControllerConfig CreateConfig(
        uint targetTemp = 75,
        uint triggerTemp = 80,
        uint emergencyTemp = 95,
        double kp = 10.0,
        double ki = 0.5,
        double kd = 2.0,
        int idleSleepMs = 250,
        int controllingSleepMs = 100,
        uint exitHysteresis = 2)
    {
        return new ThermalControllerConfig
        {
            TargetTemp = targetTemp,
            TriggerTemp = triggerTemp,
            EmergencyTemp = emergencyTemp,
            Kp = kp,
            Ki = ki,
            Kd = kd,
            IdleSleepMs = idleSleepMs,
            ControllingSleepMs = controllingSleepMs,
            DefaultDt = 0.25,
            MinimumDt = 0.01,
            ExitHysteresis = exitHysteresis,
            MaxConsecutiveReadFailures = 5,
            LookaheadSeconds = 1.5,
            PredictiveFloor = 70,
            IntegralMax = 250,
            IntegralMin = -50,
            DefaultMaxPower = 600,
            DefaultMinPower = 150
        };
    }

    private (ThermalController Controller, DashboardDataProvider Provider) CreatePipeline(
        MockGpuDevice? device = null,
        ThermalControllerConfig? config = null,
        int historyCapacity = 2400,
        int eventCapacity = 500)
    {
        device ??= CreateDevice();
        config ??= CreateConfig();

        var pid = new PidController(config.Kp, config.Ki, config.Kd, config.TargetTemp, device.MaxPower, device.MinPower,
            config.IntegralMax, config.IntegralMin, config.MinimumDt);
        var trigger = new TriggerEvaluator(config.TriggerTemp, config.PredictiveFloor, config.LookaheadSeconds);
        var controller = new ThermalController(device, pid, trigger, config);
        var provider = new DashboardDataProvider(device, config, historyCapacity, eventCapacity);
        provider.Subscribe(controller);

        return (controller, provider);
    }

    // --- Config Tests ---

    [Fact]
    public void Config_ReflectsDeviceAndControllerValues()
    {
        var device = new MockGpuDevice("RTX 4090", 200, 450);
        var config = CreateConfig(targetTemp: 70, triggerTemp: 75, emergencyTemp: 90, kp: 5, ki: 1, kd: 3);
        var (_, provider) = CreatePipeline(device, config);

        Assert.Equal("RTX 4090", provider.Config.GpuName);
        Assert.Equal(200, provider.Config.MinPower);
        Assert.Equal(450, provider.Config.MaxPower);
        Assert.Equal(70, (int)provider.Config.TargetTemp);
        Assert.Equal(75, (int)provider.Config.TriggerTemp);
        Assert.Equal(90, (int)provider.Config.EmergencyTemp);
        Assert.Equal(5, provider.Config.Kp);
        Assert.Equal(1, provider.Config.Ki);
        Assert.Equal(3, provider.Config.Kd);
    }

    // --- Initial State Tests ---

    [Fact]
    public void InitialSnapshot_HasCorrectDefaults()
    {
        var (_, provider) = CreatePipeline();

        Assert.Equal(75, (int)provider.Current.Temperature);
        Assert.Equal(600, provider.Current.CurrentPowerLimit);
        Assert.False(provider.Current.IsControlling);
        Assert.Equal(0, provider.Current.PidCycles);
        Assert.Equal(0, provider.Current.StateTransitions);
    }

    // --- History Ring Buffer Tests ---

    [Fact]
    public void History_GrowsWithSteps()
    {
        var device = CreateDevice();
        device.SetConstantTemperature(60);
        var (controller, provider) = CreatePipeline(device);

        controller.Step(60);
        controller.Step(61);
        controller.Step(62);

        var history = provider.GetHistory(100);
        Assert.Equal(3, history.Count);
    }

    [Fact]
    public void History_RespectsCapacity()
    {
        var device = CreateDevice();
        device.SetConstantTemperature(60);
        var (controller, provider) = CreatePipeline(device, historyCapacity: 5);

        for (int i = 0; i < 10; i++)
            controller.Step(60 + i);

        var history = provider.GetHistory(100);
        Assert.Equal(5, history.Count);
    }

    [Fact]
    public void History_KeepsMostRecentItems()
    {
        var device = CreateDevice();
        device.SetConstantTemperature(60);
        var (controller, provider) = CreatePipeline(device, historyCapacity: 3);

        for (int i = 0; i < 6; i++)
            controller.Step(60 + i);

        var history = provider.GetHistory(100);
        Assert.Equal(3, history.Count);
        Assert.Equal(63, (int)history[0].Temperature);
        Assert.Equal(64, (int)history[1].Temperature);
        Assert.Equal(65, (int)history[2].Temperature);
    }

    [Fact]
    public void GetHistory_ReturnsUpToRequestedCount()
    {
        var device = CreateDevice();
        device.SetConstantTemperature(60);
        var (controller, provider) = CreatePipeline(device);

        for (int i = 0; i < 10; i++)
            controller.Step(60 + i);

        var history = provider.GetHistory(3);
        Assert.Equal(3, history.Count);
        Assert.Equal(67, (int)history[0].Temperature);
        Assert.Equal(68, (int)history[1].Temperature);
        Assert.Equal(69, (int)history[2].Temperature);
    }

    [Fact]
    public void GetHistory_ReturnsLessWhenNotEnoughItems()
    {
        var device = CreateDevice();
        device.SetConstantTemperature(60);
        var (controller, provider) = CreatePipeline(device);

        controller.Step(60);
        controller.Step(61);

        var history = provider.GetHistory(100);
        Assert.Equal(2, history.Count);
    }

    // --- Events Ring Buffer Tests ---

    [Fact]
    public void Events_CollectedOnStateChange()
    {
        var device = CreateDevice();
        device.SetTemperatureSequence(new uint[] { 60, 60, 80, 80 });
        var (controller, provider) = CreatePipeline(device);

        controller.Step();
        controller.Step();
        controller.Step();

        var events = provider.GetEvents(100);
        Assert.NotEmpty(events);
    }

    [Fact]
    public void Events_RespectsCapacity()
    {
        var device = CreateDevice();
        device.SetTemperatureSequence(Enumerable.Repeat(80u, 20));
        var (controller, provider) = CreatePipeline(device, eventCapacity: 5);

        for (int i = 0; i < 10; i++)
            controller.Step(80);

        var events = provider.GetEvents(100);
        Assert.True(events.Count <= 5, $"Expected <= 5 events, got {events.Count}");
    }

    [Fact]
    public void GetEvents_ReturnsUpToRequestedCount()
    {
        var device = CreateDevice();
        device.SetTemperatureSequence(Enumerable.Repeat(80u, 20));
        var (controller, provider) = CreatePipeline(device);

        for (int i = 0; i < 5; i++)
            controller.Step(80);

        var events = provider.GetEvents(2);
        Assert.True(events.Count <= 2, $"Expected <= 2 events, got {events.Count}");
    }

    // --- MetricsSnapshot Accuracy Tests ---

    [Fact]
    public void Snapshot_IsControlling_TrueAfterTrigger()
    {
        var device = CreateDevice();
        device.SetTemperatureSequence(new uint[] { 60, 80 });
        var (controller, provider) = CreatePipeline(device);

        controller.Step(60);
        Assert.False(provider.Current.IsControlling);

        controller.Step(80);
        Assert.True(provider.Current.IsControlling);
    }

    [Fact]
    public void Snapshot_PidCycles_Increments()
    {
        var device = CreateDevice();
        device.SetTemperatureSequence(Enumerable.Repeat(80u, 5));
        var (controller, provider) = CreatePipeline(device);

        controller.Step(80);
        var afterFirst = provider.Current.PidCycles;

        controller.Step(80);
        Assert.True(provider.Current.PidCycles > afterFirst);
    }

    [Fact]
    public void Snapshot_StateTransitions_Increments()
    {
        var device = CreateDevice();
        var config = CreateConfig(targetTemp: 75, triggerTemp: 80, exitHysteresis: 2);
        device.SetTemperatureSequence(new uint[] { 60, 80, 80, 70 });
        var (controller, provider) = CreatePipeline(device, config);

        controller.Step(60);
        Assert.Equal(0, provider.Current.StateTransitions);

        controller.Step(80);
        Assert.True(provider.Current.StateTransitions >= 1);
    }

    [Fact]
    public void Snapshot_Derivative_Calculated()
    {
        var device = CreateDevice();
        var (controller, provider) = CreatePipeline(device);

        controller.Step(60);
        controller.Step(70);

        Assert.NotEqual(0, provider.Current.Derivative);
    }

    [Fact]
    public void Snapshot_PidComponents_PopulatedWhenControlling()
    {
        var device = CreateDevice();
        device.SetTemperatureSequence(Enumerable.Repeat(80u, 5));
        var (controller, provider) = CreatePipeline(device);

        controller.Step(80);

        var sum = provider.Current.PidP + provider.Current.PidI + provider.Current.PidD;
        Assert.NotEqual(0, sum);
    }

    [Fact]
    public void Snapshot_PollingIntervalMs_ChangesWithMode()
    {
        var device = CreateDevice();
        device.SetTemperatureSequence(new uint[] { 60, 80 });
        var config = CreateConfig(idleSleepMs: 250, controllingSleepMs: 100);
        var (controller, provider) = CreatePipeline(device, config);

        controller.Step(60);
        Assert.Equal(250, provider.Current.PollingIntervalMs);

        controller.Step(80);
        Assert.Equal(100, provider.Current.PollingIntervalMs);
    }

    // --- Event Data Tests ---

    [Fact]
    public void Events_HaveCorrectType()
    {
        var device = CreateDevice();
        device.SetTemperatureSequence(new uint[] { 60, 80 });
        var (controller, provider) = CreatePipeline(device);

        controller.Step(60);
        controller.Step(80);

        var events = provider.GetEvents(100);
        var triggerEvent = events.FirstOrDefault(e => e.EventType == ControllerEventType.Trigger);
        Assert.NotNull(triggerEvent);
    }

    [Fact]
    public void Events_HaveTemperatureAndPower()
    {
        var device = CreateDevice();
        device.SetTemperatureSequence(new uint[] { 60, 80 });
        var (controller, provider) = CreatePipeline(device);

        controller.Step(60);
        controller.Step(80);

        var events = provider.GetEvents(100);
        foreach (var e in events)
        {
            Assert.True(e.Temperature > 0);
            Assert.True(e.PowerLimit > 0);
        }
    }

    [Fact]
    public void Events_EmptyWhenNoStateChanges()
    {
        var device = CreateDevice();
        device.SetConstantTemperature(60);
        var (controller, provider) = CreatePipeline(device);

        controller.Step(60);
        controller.Step(60);

        var events = provider.GetEvents(100);
        Assert.Empty(events);
    }

    // --- MetricsUpdated Event Tests ---

    [Fact]
    public void MetricsUpdated_EventFiresOnStep()
    {
        var device = CreateDevice();
        device.SetConstantTemperature(60);
        var config = CreateConfig();
        var pid = new PidController(config.Kp, config.Ki, config.Kd, config.TargetTemp, device.MaxPower, device.MinPower,
            config.IntegralMax, config.IntegralMin, config.MinimumDt);
        var trigger = new TriggerEvaluator(config.TriggerTemp, config.PredictiveFloor, config.LookaheadSeconds);
        var controller = new ThermalController(device, pid, trigger, config);
        var provider = new DashboardDataProvider(device, config);
        provider.Subscribe(controller);

        bool fired = false;
        MetricsSnapshot? captured = null;

        provider.MetricsUpdated += (s, e) =>
        {
            fired = true;
            captured = e;
        };

        controller.Step(60);

        Assert.True(fired);
        Assert.NotNull(captured);
    }

    // --- EventLogged Event Tests ---

    [Fact]
    public void EventLogged_EventFiresOnStateChange()
    {
        var device = CreateDevice();
        device.SetTemperatureSequence(new uint[] { 60, 80 });
        var (controller, provider) = CreatePipeline(device);

        bool fired = false;
        DashboardEvent? captured = null;

        provider.EventLogged += (s, e) =>
        {
            fired = true;
            captured = e;
        };

        controller.Step(60);
        Assert.False(fired);

        controller.Step(80);
        Assert.True(fired);
        Assert.NotNull(captured);
    }

    // --- ReadFailures Tracking ---

    [Fact]
    public void Snapshot_ReadFailures_Tracked()
    {
        var device = CreateDevice();
        device.SetConstantTemperature(60);
        var (controller, provider) = CreatePipeline(device);

        controller.Step(60);
        Assert.Equal(0, provider.Current.ReadFailures);

        device.GetTemperatureShouldFail = true;
        for (int i = 0; i < 3; i++)
            controller.Step();

        Assert.True(provider.Current.ReadFailures > 0);
    }

    // --- Uptime Test ---

    [Fact]
    public void Snapshot_Uptime_NonNegative()
    {
        var device = CreateDevice();
        device.SetConstantTemperature(60);
        var (controller, provider) = CreatePipeline(device);
        controller.Step(60);

        Assert.True(provider.Current.Uptime.TotalMilliseconds >= 0);
    }
}