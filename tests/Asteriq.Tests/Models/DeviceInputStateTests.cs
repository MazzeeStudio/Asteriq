using Asteriq.Models;

namespace Asteriq.Tests.Models;

public class DeviceInputStateTests
{
    [Fact]
    public void DeviceInputState_DefaultValues_AreEmpty()
    {
        var state = new DeviceInputState();

        Assert.Empty(state.Axes);
        Assert.Empty(state.Buttons);
        Assert.Empty(state.Hats);
        Assert.Equal(string.Empty, state.DeviceName);
        Assert.Equal(0, state.DeviceIndex);
        Assert.Equal(Guid.Empty, state.InstanceGuid);
    }

    [Fact]
    public void DeviceInputState_WithValues_StoresCorrectly()
    {
        var guid = Guid.NewGuid();
        var timestamp = DateTime.UtcNow;

        var state = new DeviceInputState
        {
            DeviceIndex = 1,
            DeviceName = "Test Joystick",
            InstanceGuid = guid,
            Timestamp = timestamp,
            Axes = new float[] { 0.5f, -0.5f, 1.0f },
            Buttons = new bool[] { true, false, true },
            Hats = new int[] { 0, 90, -1 }
        };

        Assert.Equal(1, state.DeviceIndex);
        Assert.Equal("Test Joystick", state.DeviceName);
        Assert.Equal(guid, state.InstanceGuid);
        Assert.Equal(timestamp, state.Timestamp);
        Assert.Equal(3, state.Axes.Length);
        Assert.Equal(0.5f, state.Axes[0]);
        Assert.Equal(3, state.Buttons.Length);
        Assert.True(state.Buttons[0]);
        Assert.Equal(3, state.Hats.Length);
        Assert.Equal(90, state.Hats[1]);
    }

    [Fact]
    public void PhysicalDeviceInfo_DefaultValues_AreEmpty()
    {
        var info = new PhysicalDeviceInfo();

        Assert.Equal(string.Empty, info.Name);
        Assert.Equal(0, info.DeviceIndex);
        Assert.Equal(0, info.AxisCount);
        Assert.Equal(0, info.ButtonCount);
        Assert.Equal(0, info.HatCount);
        Assert.Equal(Guid.Empty, info.InstanceGuid);
        Assert.True(info.IsConnected); // Default should be connected
        Assert.Equal(string.Empty, info.HidDevicePath);
    }

    [Fact]
    public void PhysicalDeviceInfo_IsConnected_DefaultsToTrue()
    {
        var info = new PhysicalDeviceInfo();

        Assert.True(info.IsConnected);
    }

    [Fact]
    public void PhysicalDeviceInfo_IsConnected_CanBeSetToFalse()
    {
        var info = new PhysicalDeviceInfo { IsConnected = false };

        Assert.False(info.IsConnected);
    }

    [Fact]
    public void PhysicalDeviceInfo_DeviceIndex_CanBeModified()
    {
        var info = new PhysicalDeviceInfo { DeviceIndex = 5 };

        info.DeviceIndex = 10;

        Assert.Equal(10, info.DeviceIndex);
    }

    [Fact]
    public void PhysicalDeviceInfo_HidDevicePath_CanBeSet()
    {
        var info = new PhysicalDeviceInfo
        {
            HidDevicePath = @"\\?\HID#VID_3344&PID_0001"
        };

        Assert.Equal(@"\\?\HID#VID_3344&PID_0001", info.HidDevicePath);
    }

    [Fact]
    public void PhysicalDeviceInfo_ToString_ReturnsExpectedFormat()
    {
        var info = new PhysicalDeviceInfo
        {
            DeviceIndex = 0,
            Name = "VKB Gladiator",
            AxisCount = 6,
            ButtonCount = 32,
            HatCount = 1
        };

        var result = info.ToString();

        Assert.Contains("VKB Gladiator", result);
        Assert.Contains("6", result);
        Assert.Contains("32", result);
    }

    #region AxisInfo Tests

    [Theory]
    [InlineData(AxisType.X, 0)]
    [InlineData(AxisType.Y, 1)]
    [InlineData(AxisType.Z, 2)]
    [InlineData(AxisType.RX, 3)]
    [InlineData(AxisType.RY, 4)]
    [InlineData(AxisType.RZ, 5)]
    [InlineData(AxisType.Slider, 6)]
    [InlineData(AxisType.Unknown, -1)]
    public void AxisInfo_ToVJoyAxisIndex_ReturnsCorrectIndex(AxisType type, int expectedIndex)
    {
        var axisInfo = new AxisInfo { Index = 0, Type = type, Name = "Test" };

        var result = axisInfo.ToVJoyAxisIndex();

        Assert.Equal(expectedIndex, result);
    }

    [Theory]
    [InlineData(AxisType.X, "X")]
    [InlineData(AxisType.Y, "Y")]
    [InlineData(AxisType.Z, "Z")]
    [InlineData(AxisType.RX, "RX")]
    [InlineData(AxisType.RY, "RY")]
    [InlineData(AxisType.RZ, "RZ")]
    [InlineData(AxisType.Slider, "Slider")]
    [InlineData(AxisType.Unknown, "Unknown")]
    public void AxisInfo_TypeName_ReturnsCorrectName(AxisType type, string expectedName)
    {
        var axisInfo = new AxisInfo { Index = 0, Type = type, Name = "Test" };

        var result = axisInfo.TypeName;

        Assert.Equal(expectedName, result);
    }

    [Fact]
    public void AxisInfo_DefaultValues_AreCorrect()
    {
        var axisInfo = new AxisInfo();

        Assert.Equal(0, axisInfo.Index);
        Assert.Equal(AxisType.Unknown, axisInfo.Type);
        Assert.Equal(string.Empty, axisInfo.Name);
    }

    #endregion

    #region PhysicalDeviceInfo AxisInfos Tests

    [Fact]
    public void PhysicalDeviceInfo_AxisInfos_DefaultsToEmptyList()
    {
        var info = new PhysicalDeviceInfo();

        Assert.NotNull(info.AxisInfos);
        Assert.Empty(info.AxisInfos);
    }

    [Fact]
    public void PhysicalDeviceInfo_GetAxisType_ReturnsCorrectType()
    {
        var info = new PhysicalDeviceInfo
        {
            AxisInfos = new List<AxisInfo>
            {
                new AxisInfo { Index = 0, Type = AxisType.X },
                new AxisInfo { Index = 1, Type = AxisType.Y },
                new AxisInfo { Index = 2, Type = AxisType.Slider }
            }
        };

        Assert.Equal(AxisType.X, info.GetAxisType(0));
        Assert.Equal(AxisType.Y, info.GetAxisType(1));
        Assert.Equal(AxisType.Slider, info.GetAxisType(2));
    }

    [Fact]
    public void PhysicalDeviceInfo_GetAxisType_ReturnsUnknownForMissingIndex()
    {
        var info = new PhysicalDeviceInfo
        {
            AxisInfos = new List<AxisInfo>
            {
                new AxisInfo { Index = 0, Type = AxisType.X }
            }
        };

        Assert.Equal(AxisType.Unknown, info.GetAxisType(5));
    }

    [Fact]
    public void PhysicalDeviceInfo_GetAxisType_ReturnsUnknownWhenEmpty()
    {
        var info = new PhysicalDeviceInfo();

        Assert.Equal(AxisType.Unknown, info.GetAxisType(0));
    }

    [Fact]
    public void PhysicalDeviceInfo_WithTypicalVirpilLayout_MapsCorrectly()
    {
        // Simulates a Virpil Alpha Prime with X, Y, Z, RX, RY, and a Slider at index 5
        var info = new PhysicalDeviceInfo
        {
            Name = "VPC Stick MT-50CM2",
            AxisCount = 6,
            AxisInfos = new List<AxisInfo>
            {
                new AxisInfo { Index = 0, Type = AxisType.X, Name = "X Axis" },
                new AxisInfo { Index = 1, Type = AxisType.Y, Name = "Y Axis" },
                new AxisInfo { Index = 2, Type = AxisType.Z, Name = "Z Axis" },
                new AxisInfo { Index = 3, Type = AxisType.RX, Name = "X Rotation" },
                new AxisInfo { Index = 4, Type = AxisType.RY, Name = "Y Rotation" },
                new AxisInfo { Index = 5, Type = AxisType.Slider, Name = "Slider" }
            }
        };

        // Verify the slider at index 5 maps to vJoy Slider0 (index 6), not RZ (index 5)
        var sliderAxis = info.AxisInfos.First(a => a.Index == 5);
        Assert.Equal(AxisType.Slider, sliderAxis.Type);
        Assert.Equal(6, sliderAxis.ToVJoyAxisIndex()); // Slider0 = 6
    }

    #endregion
}
