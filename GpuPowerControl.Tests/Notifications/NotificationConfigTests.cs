using GpuThermalController.Notifications;
using Xunit;

namespace GpuPowerControl.Tests;

/// <summary>
/// Tests for NotificationConfig default values and property behavior.
/// </summary>
public class NotificationConfigTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        // Act
        var config = new NotificationConfig();

        // Assert
        Assert.False(config.Enabled);
        Assert.Equal("com.gpupowercontrol.app", config.AppUserModelId);
        Assert.False(config.InfoSoundEnabled);
        Assert.Equal("", config.WarningSoundPath);
        Assert.Equal(10, config.MaxQueuedToasts);
    }

    [Fact]
    public void Enabled_CanBeSet()
    {
        // Arrange
        var config = new NotificationConfig();
        Assert.False(config.Enabled);

        // Act
        config.Enabled = true;

        // Assert
        Assert.True(config.Enabled);
    }

    [Fact]
    public void AppUserModelId_CanBeCustomized()
    {
        // Arrange
        var config = new NotificationConfig();

        // Act
        config.AppUserModelId = "my.custom.app";

        // Assert
        Assert.Equal("my.custom.app", config.AppUserModelId);
    }

    [Fact]
    public void MaxQueuedToasts_CanBeSet()
    {
        // Arrange
        var config = new NotificationConfig();

        // Act
        config.MaxQueuedToasts = 20;

        // Assert
        Assert.Equal(20, config.MaxQueuedToasts);
    }

    [Fact]
    public void InfoSoundEnabled_DefaultIsFalse()
    {
        // Act
        var config = new NotificationConfig();

        // Assert
        Assert.False(config.InfoSoundEnabled);
    }

    [Fact]
    public void WarningSoundPath_DefaultIsEmpty()
    {
        // Act
        var config = new NotificationConfig();

        // Assert
        Assert.Equal("", config.WarningSoundPath);
    }
}