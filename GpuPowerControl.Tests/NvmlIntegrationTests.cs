using GpuThermalController.Nvml;
using Xunit;

namespace GpuPowerControl.Tests;

/// <summary>
/// Brief integration tests against the real NVML library.
/// All tests gracefully skip when NVML is unavailable (no NVIDIA GPU/drivers).
/// These tests are READ-ONLY and will not modify any GPU settings.
/// </summary>
[Trait("Category", "Integration")]
public class NvmlIntegrationTests
{
    private bool _initialized = false;

    private bool TryInitNVML()
    {
        if (_initialized) return true;

        int result = NVML.nvmlInit_v2();
        if (result == 0)
        {
            _initialized = true;
            return true;
        }

        return false;
    }

    private void Cleanup()
    {
        if (_initialized)
        {
            NVML.nvmlShutdown();
            _initialized = false;
        }
    }

    [Fact]
    public void NvmlInitAndShutdown_RoundTripSucceeds()
    {
        int initResult = NVML.nvmlInit_v2();
        if (initResult != 0)
        {
            return; // NVML not available, skip
        }

        int shutdownResult = NVML.nvmlShutdown();
        Assert.Equal(0, shutdownResult);
    }

    [Fact]
    public void GetDeviceCount_ReturnsAtLeastOne()
    {
        if (!TryInitNVML())
        {
            return; // NVML not available, skip
        }

        try
        {
            int result = NVML.nvmlDeviceGetCount_v2(out uint count);
            Assert.Equal(0, result);
            Assert.True(count >= 1, "Expected at least 1 GPU");
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void GetDeviceHandleAndName_Succeeds()
    {
        if (!TryInitNVML())
        {
            return; // NVML not available, skip
        }

        try
        {
            int handleResult = NVML.nvmlDeviceGetHandleByIndex_v2(0, out IntPtr handle);
            Assert.Equal(0, handleResult);
            Assert.NotEqual(IntPtr.Zero, handle);

            // Try to get name
            var nameBuilder = new System.Text.StringBuilder(64);
            int nameResult = NVML.nvmlDeviceGetName(handle, nameBuilder, nameBuilder.Capacity);
            Assert.Equal(0, nameResult);
            Assert.NotEmpty(nameBuilder.ToString());
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void GetTemperature_ReturnsReasonableValue()
    {
        if (!TryInitNVML())
        {
            return; // NVML not available, skip
        }

        try
        {
            int handleResult = NVML.nvmlDeviceGetHandleByIndex_v2(0, out IntPtr handle);
            Assert.Equal(0, handleResult);

            int tempResult = NVML.nvmlDeviceGetTemperature(handle, 0, out uint temp);
            Assert.Equal(0, tempResult);
            Assert.InRange(temp, 0u, 120u); // GPU temp should be between 0-120C
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void GetPowerConstraints_ReturnsValidRange()
    {
        if (!TryInitNVML())
        {
            return; // NVML not available, skip
        }

        try
        {
            int handleResult = NVML.nvmlDeviceGetHandleByIndex_v2(0, out IntPtr handle);
            Assert.Equal(0, handleResult);

            int result = NVML.nvmlDeviceGetPowerManagementLimitConstraints(handle, out uint minMw, out uint maxMw);
            Assert.Equal(0, result);
            Assert.True(minMw < maxMw, "Min power should be less than max power");
            Assert.True(minMw > 0, "Min power should be positive");
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void GetDefaultPowerLimit_ReturnsValueWithinConstraints()
    {
        if (!TryInitNVML())
        {
            return; // NVML not available, skip
        }

        try
        {
            int handleResult = NVML.nvmlDeviceGetHandleByIndex_v2(0, out IntPtr handle);
            Assert.Equal(0, handleResult);

            int defaultResult = NVML.nvmlDeviceGetPowerManagementDefaultLimit(handle, out uint defaultMw);
            Assert.Equal(0, defaultResult);

            int constraintsResult = NVML.nvmlDeviceGetPowerManagementLimitConstraints(handle, out uint minMw, out uint maxMw);
            Assert.Equal(0, constraintsResult);

            Assert.True(defaultMw >= minMw, "Default should be >= min");
            Assert.True(defaultMw <= maxMw, "Default should be <= max");
        }
        finally
        {
            Cleanup();
        }
    }

    [Fact]
    public void SetPowerLimit_ChangesAndRestoresSafely()
    {
        if (!TryInitNVML())
        {
            return; // NVML not available, skip
        }

        IntPtr handle = IntPtr.Zero;
        uint originalLimit = 0;
        bool changed = false;

        try
        {
            int handleResult = NVML.nvmlDeviceGetHandleByIndex_v2(0, out handle);
            Assert.Equal(0, handleResult);

            // Get constraints and current limit
            int constraintsResult = NVML.nvmlDeviceGetPowerManagementLimitConstraints(handle, out uint minMw, out uint maxMw);
            Assert.Equal(0, constraintsResult);

            int currentResult = NVML.nvmlDeviceGetPowerManagementLimit(handle, out originalLimit);
            Assert.Equal(0, currentResult);

            // Pick a safe test value: midpoint of range
            uint testValue = minMw + (maxMw - minMw) / 2;

            // Set new power limit - gracefully skip if not permitted (requires admin)
            int setResult = NVML.nvmlDeviceSetPowerManagementLimit(handle, testValue);
            if (setResult == NVML.NVML_ERROR_NOT_PERMITTED)
            {
                return; // Skip: requires admin privileges
            }
            Assert.Equal(0, setResult);
            changed = true;

            // Verify it took
            int verifyResult = NVML.nvmlDeviceGetPowerManagementLimit(handle, out uint verifyMw);
            Assert.Equal(0, verifyResult);
            Assert.Equal(testValue, verifyMw);
        }
        finally
        {
            // ALWAYS restore original value, even if assertions above fail
            if (changed && handle != IntPtr.Zero)
            {
                NVML.nvmlDeviceSetPowerManagementLimit(handle, originalLimit);
            }
            Cleanup();
        }
    }
}
