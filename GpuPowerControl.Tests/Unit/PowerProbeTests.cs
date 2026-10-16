using GpuPowerControl.Devices;
using GpuPowerControl.Tests.Mocks;
using Xunit;

namespace GpuPowerControl.Tests
{
    public class PowerProbePlannerTests
    {
        [Theory]
        [InlineData(600, 150, 600, 5, 595)] // normal case: slightly below the restore target
        [InlineData(155, 150, 600, 5, 150)] // below, clamped to the hardware floor
        [InlineData(150, 150, 600, 5, 155)] // already at the floor: probe slightly above
        [InlineData(2, 1, 95, 5, 1)]        // low-power GPU near its floor: probe at the floor
        [InlineData(95, 95, 95, 5, 95)]     // zero-width range: only one value possible
        public void ChooseProbeTarget_PicksARealChangeWithinTheHardwareRange(
            int restoreW, int minW, int maxW, int stepW, int expected)
        {
            Assert.Equal(expected, PowerProbePlanner.ChooseProbeTarget(restoreW, minW, maxW, stepW));
        }

        [Theory]
        [InlineData(595, 595, 1, true)]   // exact match
        [InlineData(596, 595, 1, true)]   // within tolerance
        [InlineData(594, 595, 1, true)]   // within tolerance
        [InlineData(597, 595, 1, false)]  // outside tolerance: firmware override
        public void ReadbackMatches_RespectsTolerance(int readW, int requestedW, int toleranceW, bool expected)
        {
            Assert.Equal(expected, PowerProbePlanner.ReadbackMatches(readW, requestedW, toleranceW));
        }
    }

    public class GpuDeviceProbeTests
    {
        [Fact]
        public void MockProbePowerControl_SucceedsByDefault()
        {
            var gpu = new MockGpuDevice();
            Assert.True(gpu.ProbePowerControl().CanControlPower);
        }

        [Fact]
        public void MockProbePowerControl_ReportsConfiguredFailure()
        {
            var gpu = new MockGpuDevice();
            gpu.ProbeResult = PowerProbeResult.Failure("NVML_ERROR_NOT_SUPPORTED");
            var result = gpu.ProbePowerControl();
            Assert.False(result.CanControlPower);
            Assert.Contains("NOT_SUPPORTED", result.Detail);
        }

        [Fact]
        public void SimulatedProbePowerControl_Succeeds()
        {
            var gpu = new SimulatedGpuDevice();
            var result = gpu.ProbePowerControl();
            Assert.True(result.CanControlPower);
            Assert.Contains("confirmed", result.Detail);
        }
    }
}