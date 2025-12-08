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
}
