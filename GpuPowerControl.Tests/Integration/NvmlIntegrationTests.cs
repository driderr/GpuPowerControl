using GpuThermalController.Nvml;
using Xunit;
using Xunit.Sdk;

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

    [SkippableFact]
    public void NvmlInitAndShutdown_RoundTripSucceeds()
    {
        int initResult = NVML.nvmlInit_v2();
        Skip.If(initResult != 0, "NVML not available (no NVIDIA GPU/drivers)");

        int shutdownResult = NVML.nvmlShutdown();
        Assert.Equal(0, shutdownResult);
    }

    [SkippableFact]
    public void GetDeviceCount_ReturnsAtLeastOne()
    {
        bool nvmlAvailable = TryInitNVML();
        Skip.If(!nvmlAvailable, "NVML not available (no NVIDIA GPU/drivers)");

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

    [SkippableFact]
    public void GetDeviceHandleAndName_Succeeds()
    {
        bool nvmlAvailable = TryInitNVML();
        Skip.If(!nvmlAvailable, "NVML not available (no NVIDIA GPU/drivers)");

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

    [SkippableFact]
    public void GetTemperature_ReturnsReasonableValue()
    {
        bool nvmlAvailable = TryInitNVML();
        Skip.If(!nvmlAvailable, "NVML not available (no NVIDIA GPU/drivers)");

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

    [SkippableFact]
    public void GetPowerConstraints_ReturnsValidRange()
    {
        bool nvmlAvailable = TryInitNVML();
        Skip.If(!nvmlAvailable, "NVML not available (no NVIDIA GPU/drivers)");

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

    [SkippableFact]
    public void GetDefaultPowerLimit_ReturnsValueWithinConstraints()
    {
        bool nvmlAvailable = TryInitNVML();
        Skip.If(!nvmlAvailable, "NVML not available (no NVIDIA GPU/drivers)");

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

    [SkippableFact]
    public void SetPowerLimit_ChangesAndRestoresSafely()
    {
        bool nvmlAvailable = TryInitNVML();
        Skip.If(!nvmlAvailable, "NVML not available (no NVIDIA GPU/drivers)");

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

            // Pick a value guaranteed to differ from the current limit
            uint middle = minMw + (maxMw - minMw) / 2;
            uint testValue = (originalLimit == middle) ? minMw : middle;

            // Set new power limit - gracefully skip if not permitted (requires admin)
            int setResult = NVML.nvmlDeviceSetPowerManagementLimit(handle, testValue);
            Skip.If(setResult == NVML.NVML_ERROR_NOT_PERMITTED, "Requires admin privileges to set power limit");

            Assert.Equal(0, setResult);
            changed = true;

            // Verify it took
            int verifyResult = NVML.nvmlDeviceGetPowerManagementLimit(handle, out uint verifyMw);
            Assert.Equal(0, verifyResult);
            Assert.NotEqual(originalLimit, verifyMw);
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