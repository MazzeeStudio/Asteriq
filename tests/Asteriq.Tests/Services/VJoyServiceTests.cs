using Asteriq.Services;
using Asteriq.VJoy;

namespace Asteriq.Tests.Services;

public class VJoyServiceTests
{
    [Fact]
    public void AxisConversion_NegativeOne_ReturnsMin()
    {
        // Test the axis conversion formula: -1.0 should map to 0
        float input = -1.0f;
        int expected = VJoyService.AxisMin; // 0

        int result = ConvertAxis(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void AxisConversion_Zero_ReturnsCenter()
    {
        // Test the axis conversion formula: 0.0 should map to center (16384)
        float input = 0.0f;
        int expected = VJoyService.AxisCenter; // 16384

        int result = ConvertAxis(input);

        // Allow small rounding error
        Assert.InRange(result, expected - 1, expected + 1);
    }

    [Fact]
    public void AxisConversion_PositiveOne_ReturnsMax()
    {
        // Test the axis conversion formula: 1.0 should map to max (32767)
        float input = 1.0f;
        int expected = VJoyService.AxisMax; // 32767

        int result = ConvertAxis(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void AxisConversion_OutOfRange_ClampedToMin()
    {
        // Values below -1.0 should clamp to min
        float input = -2.0f;

        int result = ConvertAxis(input);

        Assert.Equal(VJoyService.AxisMin, result);
    }

    [Fact]
    public void AxisConversion_OutOfRange_ClampedToMax()
    {
        // Values above 1.0 should clamp to max
        float input = 2.0f;

        int result = ConvertAxis(input);

        Assert.Equal(VJoyService.AxisMax, result);
    }

    [Fact]
    public void AxisConversion_MidPoint_ReturnsQuarter()
    {
        // -0.5 should map to ~8192 (quarter range)
        float input = -0.5f;
        int expected = VJoyService.AxisMax / 4; // ~8192

        int result = ConvertAxis(input);

        Assert.InRange(result, expected - 100, expected + 100);
    }

    [Theory]
    [InlineData(-1.0f, 0)]
    [InlineData(-0.5f, 8192)]
    [InlineData(0.0f, 16384)]
    [InlineData(0.5f, 24576)]
    [InlineData(1.0f, 32767)]
    public void AxisConversion_VariousValues_MapsCorrectly(float input, int expectedApprox)
    {
        int result = ConvertAxis(input);

        // Allow small rounding variance
        Assert.InRange(result, expectedApprox - 100, expectedApprox + 100);
    }

    // Helper method that mirrors VJoyService.SetAxis conversion logic
    private static int ConvertAxis(float value)
    {
        int rawValue = (int)((value + 1.0f) * 0.5f * VJoyService.AxisMax);
        return Math.Clamp(rawValue, VJoyService.AxisMin, VJoyService.AxisMax);
    }
}

public class VJoyDeviceInfoTests
{
    [Fact]
    public void VJoyDeviceInfo_DefaultValues()
    {
        var info = new VJoyDeviceInfo
        {
            Id = 1,
            Exists = true,
            Status = VjdStat.Free,
            ButtonCount = 32,
            HasAxisX = true,
            HasAxisY = true
        };

        Assert.Equal(1u, info.Id);
        Assert.True(info.Exists);
        Assert.Equal(VjdStat.Free, info.Status);
        Assert.Equal(32, info.ButtonCount);
        Assert.True(info.HasAxisX);
        Assert.True(info.HasAxisY);
        Assert.False(info.HasAxisZ); // Default false
    }
}
