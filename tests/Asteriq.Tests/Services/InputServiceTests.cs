using Asteriq.Services;
using Asteriq.Models;

namespace Asteriq.Tests.Services;

public class InputServiceTests
{
    [Fact]
    public void HasStateChanged_IdenticalStates_ReturnsFalse()
    {
        var state1 = CreateState(new float[] { 0.0f, 0.5f }, new bool[] { false, true });
        var state2 = CreateState(new float[] { 0.0f, 0.5f }, new bool[] { false, true });

        bool changed = HasStateChanged(state1, state2);

        Assert.False(changed);
    }

    [Fact]
    public void HasStateChanged_ButtonChanged_ReturnsTrue()
    {
        var state1 = CreateState(new float[] { 0.0f }, new bool[] { false, false });
        var state2 = CreateState(new float[] { 0.0f }, new bool[] { false, true });

        bool changed = HasStateChanged(state1, state2);

        Assert.True(changed);
    }

    [Fact]
    public void HasStateChanged_AxisChangedAboveThreshold_ReturnsTrue()
    {
        var state1 = CreateState(new float[] { 0.0f }, new bool[] { });
        var state2 = CreateState(new float[] { 0.05f }, new bool[] { }); // Above 0.01 threshold

        bool changed = HasStateChanged(state1, state2);

        Assert.True(changed);
    }

    [Fact]
    public void HasStateChanged_AxisChangedBelowThreshold_ReturnsFalse()
    {
        var state1 = CreateState(new float[] { 0.0f }, new bool[] { });
        var state2 = CreateState(new float[] { 0.005f }, new bool[] { }); // Below 0.01 threshold

        bool changed = HasStateChanged(state1, state2);

        Assert.False(changed);
    }

    [Fact]
    public void HasStateChanged_HatChanged_ReturnsTrue()
    {
        var state1 = CreateState(new float[] { }, new bool[] { }, new int[] { -1 });
        var state2 = CreateState(new float[] { }, new bool[] { }, new int[] { 90 });

        bool changed = HasStateChanged(state1, state2);

        Assert.True(changed);
    }

    [Theory]
    [InlineData(0.0f, 0.009f, false)]  // Below threshold
    [InlineData(0.0f, 0.011f, true)]   // Above threshold
    [InlineData(-1.0f, -0.98f, true)]  // Significant change
    [InlineData(0.5f, 0.5f, false)]    // No change
    public void HasStateChanged_AxisThreshold_WorksCorrectly(float val1, float val2, bool expectedChanged)
    {
        var state1 = CreateState(new float[] { val1 }, new bool[] { });
        var state2 = CreateState(new float[] { val2 }, new bool[] { });

        bool changed = HasStateChanged(state1, state2);

        Assert.Equal(expectedChanged, changed);
    }

    // Helper to create state
    private static DeviceInputState CreateState(float[] axes, bool[] buttons, int[]? hats = null)
    {
        return new DeviceInputState
        {
            DeviceIndex = 0,
            DeviceName = "Test",
            Axes = axes,
            Buttons = buttons,
            Hats = hats ?? Array.Empty<int>()
        };
    }

    // Mirror the HasStateChanged logic from InputService
    private static bool HasStateChanged(DeviceInputState last, DeviceInputState current, float axisThreshold = 0.01f)
    {
        // Check buttons
        for (int i = 0; i < Math.Min(last.Buttons.Length, current.Buttons.Length); i++)
        {
            if (last.Buttons[i] != current.Buttons[i])
                return true;
        }

        // Check axes
        for (int i = 0; i < Math.Min(last.Axes.Length, current.Axes.Length); i++)
        {
            if (Math.Abs(last.Axes[i] - current.Axes[i]) > axisThreshold)
                return true;
        }

        // Check hats
        for (int i = 0; i < Math.Min(last.Hats.Length, current.Hats.Length); i++)
        {
            if (last.Hats[i] != current.Hats[i])
                return true;
        }

        return false;
    }
}

public class HatConversionTests
{
    [Theory]
    [InlineData(0x01, 0)]     // SDL_HAT_UP
    [InlineData(0x02, 90)]    // SDL_HAT_RIGHT
    [InlineData(0x04, 180)]   // SDL_HAT_DOWN
    [InlineData(0x08, 270)]   // SDL_HAT_LEFT
    [InlineData(0x03, 45)]    // SDL_HAT_RIGHTUP
    [InlineData(0x06, 135)]   // SDL_HAT_RIGHTDOWN
    [InlineData(0x0C, 225)]   // SDL_HAT_LEFTDOWN
    [InlineData(0x09, 315)]   // SDL_HAT_LEFTUP
    [InlineData(0x00, -1)]    // SDL_HAT_CENTERED
    public void HatToAngle_ReturnsCorrectAngle(byte hatState, int expectedAngle)
    {
        int result = HatToAngle(hatState);

        Assert.Equal(expectedAngle, result);
    }

    // Mirror the HatToAngle logic from InputService
    private static int HatToAngle(byte hatState)
    {
        const byte SDL_HAT_UP = 0x01;
        const byte SDL_HAT_RIGHT = 0x02;
        const byte SDL_HAT_DOWN = 0x04;
        const byte SDL_HAT_LEFT = 0x08;
        const byte SDL_HAT_RIGHTUP = SDL_HAT_RIGHT | SDL_HAT_UP;
        const byte SDL_HAT_RIGHTDOWN = SDL_HAT_RIGHT | SDL_HAT_DOWN;
        const byte SDL_HAT_LEFTUP = SDL_HAT_LEFT | SDL_HAT_UP;
        const byte SDL_HAT_LEFTDOWN = SDL_HAT_LEFT | SDL_HAT_DOWN;

        return hatState switch
        {
            SDL_HAT_UP => 0,
            SDL_HAT_RIGHTUP => 45,
            SDL_HAT_RIGHT => 90,
            SDL_HAT_RIGHTDOWN => 135,
            SDL_HAT_DOWN => 180,
            SDL_HAT_LEFTDOWN => 225,
            SDL_HAT_LEFT => 270,
            SDL_HAT_LEFTUP => 315,
            _ => -1
        };
    }
}
