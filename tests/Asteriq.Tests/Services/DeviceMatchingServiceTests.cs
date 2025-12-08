using Asteriq.Services;

namespace Asteriq.Tests.Services;

public class DeviceMatchingServiceTests
{
    [Fact]
    public void ExtractVidPidFromSdlGuid_ValidGuid_ExtractsCorrectly()
    {
        // SDL GUID format: 00000003-VVVV-0000-PPPP-000000000000
        // VID in parts[1], PID in parts[3] (byte-swapped)
        // Example: 00000003-3344-0000-d540-000000000000
        // VID = 0x3344, PID = 0x40D5 (d540 byte-swapped)
        var guid = Guid.Parse("00000003-3344-0000-d540-000000000000");

        var (vid, pid) = DeviceMatchingService.ExtractVidPidFromSdlGuid(guid);

        Assert.Equal(0x3344, vid);
        Assert.Equal(0x40D5, pid);
    }

    [Fact]
    public void ExtractVidPidFromSdlGuid_VirpilThrottle_ExtractsCorrectly()
    {
        // Another Virpil device: VID 3344, PID 80D4
        // PID 80D4 byte-swapped = D480
        var guid = Guid.Parse("00000003-3344-0000-d480-000000000000");

        var (vid, pid) = DeviceMatchingService.ExtractVidPidFromSdlGuid(guid);

        Assert.Equal(0x3344, vid);
        Assert.Equal(0x80D4, pid);
    }

    [Fact]
    public void ExtractVidPidFromSdlGuid_VirpilStick_ExtractsCorrectly()
    {
        // Virpil stick: VID 3344, PID 0194
        // PID 0194 byte-swapped = 9401
        var guid = Guid.Parse("00000003-3344-0000-9401-000000000000");

        var (vid, pid) = DeviceMatchingService.ExtractVidPidFromSdlGuid(guid);

        Assert.Equal(0x3344, vid);
        Assert.Equal(0x0194, pid);
    }

    [Theory]
    [InlineData("00000003-3344-0000-d540-000000000000", 0x3344, 0x40D5)]
    [InlineData("00000003-3344-0000-d480-000000000000", 0x3344, 0x80D4)]
    [InlineData("00000003-3344-0000-9401-000000000000", 0x3344, 0x0194)]
    [InlineData("00000003-045e-0000-0d02-000000000000", 0x045E, 0x020D)] // Xbox controller example
    public void ExtractVidPidFromSdlGuid_VariousDevices_ExtractsCorrectly(string guidStr, int expectedVid, int expectedPid)
    {
        var guid = Guid.Parse(guidStr);

        var (vid, pid) = DeviceMatchingService.ExtractVidPidFromSdlGuid(guid);

        Assert.Equal((ushort)expectedVid, vid);
        Assert.Equal((ushort)expectedPid, pid);
    }

    [Fact]
    public void ExtractVidPidFromSdlGuid_EmptyGuid_ReturnsZeros()
    {
        var (vid, pid) = DeviceMatchingService.ExtractVidPidFromSdlGuid(Guid.Empty);

        Assert.Equal(0, vid);
        Assert.Equal(0, pid);
    }

    [Fact]
    public void ExtractVidPidFromSdlGuid_ByteSwapping_WorksCorrectly()
    {
        // Test that byte swapping is correct
        // Input in GUID parts[3]: "d540" = 0xD540
        // After swap: ((0xD540 & 0xFF) << 8) | ((0xD540 >> 8) & 0xFF)
        //           = (0x40 << 8) | 0xD5
        //           = 0x4000 | 0xD5
        //           = 0x40D5

        ushort pidRaw = 0xD540;
        ushort expected = 0x40D5;
        ushort swapped = (ushort)(((pidRaw & 0xFF) << 8) | ((pidRaw >> 8) & 0xFF));

        Assert.Equal(expected, swapped);
    }
}
