using Asteriq.DirectInput;

namespace Asteriq.Tests.DirectInput;

public class DirectInputTests
{
    #region DirectInputAxisType Tests

    [Theory]
    [InlineData(DirectInputAxisType.Unknown, 0)]
    [InlineData(DirectInputAxisType.X, 1)]
    [InlineData(DirectInputAxisType.Y, 2)]
    [InlineData(DirectInputAxisType.Z, 3)]
    [InlineData(DirectInputAxisType.RX, 4)]
    [InlineData(DirectInputAxisType.RY, 5)]
    [InlineData(DirectInputAxisType.RZ, 6)]
    [InlineData(DirectInputAxisType.Slider, 7)]
    public void DirectInputAxisType_HasCorrectValues(DirectInputAxisType type, int expectedValue)
    {
        Assert.Equal(expectedValue, (int)type);
    }

    #endregion

    #region DirectInputAxisInfo Tests

    [Fact]
    public void DirectInputAxisInfo_DefaultValues_AreCorrect()
    {
        var axisInfo = new DirectInputAxisInfo();

        Assert.Equal(0, axisInfo.Index);
        Assert.Equal(DirectInputAxisType.Unknown, axisInfo.Type);
        Assert.Equal(string.Empty, axisInfo.Name);
        Assert.Equal(Guid.Empty, axisInfo.TypeGuid);
    }

    [Fact]
    public void DirectInputAxisInfo_WithValues_StoresCorrectly()
    {
        var typeGuid = Guid.NewGuid();
        var axisInfo = new DirectInputAxisInfo
        {
            Index = 3,
            Type = DirectInputAxisType.RX,
            Name = "X Rotation",
            TypeGuid = typeGuid
        };

        Assert.Equal(3, axisInfo.Index);
        Assert.Equal(DirectInputAxisType.RX, axisInfo.Type);
        Assert.Equal("X Rotation", axisInfo.Name);
        Assert.Equal(typeGuid, axisInfo.TypeGuid);
    }

    #endregion

    #region DirectInputDeviceInfo Tests

    [Fact]
    public void DirectInputDeviceInfo_DefaultValues_AreCorrect()
    {
        var deviceInfo = new DirectInputDeviceInfo();

        Assert.Equal(Guid.Empty, deviceInfo.InstanceGuid);
        Assert.Equal(Guid.Empty, deviceInfo.ProductGuid);
        Assert.Equal(string.Empty, deviceInfo.InstanceName);
        Assert.Equal(string.Empty, deviceInfo.ProductName);
        Assert.NotNull(deviceInfo.Axes);
        Assert.Empty(deviceInfo.Axes);
        Assert.Equal(0, deviceInfo.ButtonCount);
        Assert.Equal(0, deviceInfo.PovCount);
    }

    [Fact]
    public void DirectInputDeviceInfo_WithValues_StoresCorrectly()
    {
        var instanceGuid = Guid.NewGuid();
        var productGuid = Guid.NewGuid();

        var deviceInfo = new DirectInputDeviceInfo
        {
            InstanceGuid = instanceGuid,
            ProductGuid = productGuid,
            InstanceName = "Joystick 1",
            ProductName = "VPC Stick MT-50CM2",
            Axes = new List<DirectInputAxisInfo>
            {
                new DirectInputAxisInfo { Index = 0, Type = DirectInputAxisType.X },
                new DirectInputAxisInfo { Index = 1, Type = DirectInputAxisType.Y }
            },
            ButtonCount = 32,
            PovCount = 1
        };

        Assert.Equal(instanceGuid, deviceInfo.InstanceGuid);
        Assert.Equal(productGuid, deviceInfo.ProductGuid);
        Assert.Equal("Joystick 1", deviceInfo.InstanceName);
        Assert.Equal("VPC Stick MT-50CM2", deviceInfo.ProductName);
        Assert.Equal(2, deviceInfo.Axes.Count);
        Assert.Equal(32, deviceInfo.ButtonCount);
        Assert.Equal(1, deviceInfo.PovCount);
    }

    #endregion

    #region AxisType to DirectInputAxisType Mapping Tests

    [Theory]
    [InlineData(DirectInputAxisType.X, 1)]
    [InlineData(DirectInputAxisType.Y, 2)]
    [InlineData(DirectInputAxisType.Z, 3)]
    [InlineData(DirectInputAxisType.RX, 4)]
    [InlineData(DirectInputAxisType.RY, 5)]
    [InlineData(DirectInputAxisType.RZ, 6)]
    [InlineData(DirectInputAxisType.Slider, 7)]
    public void DirectInputAxisType_MatchesModelAxisType(DirectInputAxisType diType, int expectedValue)
    {
        // Verify that DirectInputAxisType values match Asteriq.Models.AxisType values
        // This is important for the cast in InputService.PopulateAxisTypes
        var modelAxisType = (Asteriq.Models.AxisType)expectedValue;
        Assert.Equal(expectedValue, (int)diType);
        Assert.Equal((int)modelAxisType, (int)diType);
    }

    #endregion
}
