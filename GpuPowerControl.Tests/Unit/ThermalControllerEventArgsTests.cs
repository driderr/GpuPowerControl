using GpuThermalController.Core;
using Xunit;

namespace GpuPowerControl.Tests;

/// <summary>
/// Tests for ThermalControllerEventArgs constructor and property assignments.
/// </summary>
public class ThermalControllerEventArgsTests
{
    // --- Constructor Tests ---

    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var args = new ThermalControllerEventArgs(75.5, 400, true, ControllerEventType.Warning, "Temperature approaching threshold");

        Assert.Equal(75.5, args.Temperature);
        Assert.Equal(400, args.PowerLimit);
        Assert.True(args.IsControlling);
        Assert.Equal(ControllerEventType.Warning, args.EventType);
        Assert.Equal("Temperature approaching threshold", args.Message);
    }

    [Fact]
    public void Constructor_NullMessage_IsAllowed()
    {
        var args = new ThermalControllerEventArgs(60.0, 600, false, ControllerEventType.Info, null);

        Assert.Equal(60.0, args.Temperature);
        Assert.Equal(600, args.PowerLimit);
        Assert.False(args.IsControlling);
        Assert.Equal(ControllerEventType.Info, args.EventType);
        Assert.Null(args.Message);
    }

    [Fact]
    public void Constructor_EmptyMessage_IsAllowed()
    {
        var args = new ThermalControllerEventArgs(80.0, 350, true, ControllerEventType.Trigger, "");

        Assert.Equal("", args.Message);
    }

    // --- EventType Coverage ---

    [Fact]
    public void Constructor_InfoEventType()
    {
        var args = new ThermalControllerEventArgs(60, 600, false, ControllerEventType.Info);
        Assert.Equal(ControllerEventType.Info, args.EventType);
    }

    [Fact]
    public void Constructor_WarningEventType()
    {
        var args = new ThermalControllerEventArgs(75, 500, false, ControllerEventType.Warning);
        Assert.Equal(ControllerEventType.Warning, args.EventType);
    }

    [Fact]
    public void Constructor_TriggerEventType()
    {
        var args = new ThermalControllerEventArgs(80, 350, true, ControllerEventType.Trigger);
        Assert.Equal(ControllerEventType.Trigger, args.EventType);
    }

    [Fact]
    public void Constructor_EmergencyEventType()
    {
        var args = new ThermalControllerEventArgs(95, 150, true, ControllerEventType.Emergency);
        Assert.Equal(ControllerEventType.Emergency, args.EventType);
    }

    [Fact]
    public void Constructor_StableEventType()
    {
        var args = new ThermalControllerEventArgs(70, 600, false, ControllerEventType.Stable);
        Assert.Equal(ControllerEventType.Stable, args.EventType);
    }

    // --- Edge Cases ---

    [Fact]
    public void Constructor_NegativeTemperature_IsAllowed()
    {
        var args = new ThermalControllerEventArgs(-10.0, 600, false, ControllerEventType.Info);
        Assert.Equal(-10.0, args.Temperature);
    }

    [Fact]
    public void Constructor_ZeroPowerLimit_IsAllowed()
    {
        var args = new ThermalControllerEventArgs(95.0, 0, true, ControllerEventType.Emergency);
        Assert.Equal(0, args.PowerLimit);
    }

    [Fact]
    public void Constructor_HighTemperature()
    {
        var args = new ThermalControllerEventArgs(120.0, 150, true, ControllerEventType.Emergency, "Critical temperature");

        Assert.Equal(120.0, args.Temperature);
        Assert.Equal(150, args.PowerLimit);
        Assert.True(args.IsControlling);
        Assert.Equal(ControllerEventType.Emergency, args.EventType);
        Assert.Equal("Critical temperature", args.Message);
    }

    [Fact]
    public void Properties_AreReadOnly()
    {
        var args = new ThermalControllerEventArgs(75.0, 400, true, ControllerEventType.Warning, "Test");

        Assert.Equal(75.0, args.Temperature);
        Assert.Equal(400, args.PowerLimit);
        Assert.True(args.IsControlling);
        Assert.Equal(ControllerEventType.Warning, args.EventType);
        Assert.Equal("Test", args.Message);
    }

    [Fact]
    public void EventArgs_InheritsFromEventArgs()
    {
        var args = new ThermalControllerEventArgs(75.0, 400, true, ControllerEventType.Warning);
        Assert.IsAssignableFrom<EventArgs>(args);
    }
}