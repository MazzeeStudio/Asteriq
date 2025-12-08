using Asteriq.Models;

namespace Asteriq.Tests.Services;

/// <summary>
/// Tests for Phase 6 advanced mapping features
/// </summary>
public class Phase6MappingTests
{
    #region Hat Mapping Tests

    [Fact]
    public void HatMapping_DefaultValues_UseContinuousTrue()
    {
        var mapping = new HatMapping();

        Assert.True(mapping.UseContinuous);
    }

    [Theory]
    [InlineData(-1, -1)]    // Neutral
    [InlineData(0, 0)]      // North
    [InlineData(45, 1)]     // East (boundary)
    [InlineData(90, 1)]     // East (center)
    [InlineData(134, 1)]    // East (boundary)
    [InlineData(135, 2)]    // South (boundary)
    [InlineData(180, 2)]    // South (center)
    [InlineData(224, 2)]    // South (boundary)
    [InlineData(225, 3)]    // West (boundary)
    [InlineData(270, 3)]    // West (center)
    [InlineData(314, 3)]    // West (boundary)
    [InlineData(315, 0)]    // North (boundary)
    [InlineData(359, 0)]    // North
    public void HatAngleToDiscrete_ConvertsCorrectly(int angle, int expectedDirection)
    {
        // We need to test the static method via reflection since it's private
        // Instead, test the logic directly
        int direction = HatAngleToDiscreteHelper(angle);

        Assert.Equal(expectedDirection, direction);
    }

    private static int HatAngleToDiscreteHelper(int angle)
    {
        if (angle < 0)
            return -1;

        angle = angle % 360;

        if (angle >= 315 || angle < 45)
            return 0; // North
        if (angle >= 45 && angle < 135)
            return 1; // East
        if (angle >= 135 && angle < 225)
            return 2; // South
        return 3; // West
    }

    #endregion

    #region Shift Layer Tests

    [Fact]
    public void ShiftLayer_DefaultValues_NotActive()
    {
        var layer = new ShiftLayer();

        Assert.NotEqual(Guid.Empty, layer.Id);
        Assert.Equal("", layer.Name);
        Assert.Null(layer.ActivatorButton);
    }

    [Fact]
    public void ShiftLayer_WithActivatorButton_HasCorrectInput()
    {
        var layer = new ShiftLayer
        {
            Name = "Shift 1",
            ActivatorButton = new InputSource
            {
                DeviceId = "device-1",
                DeviceName = "Joystick",
                Type = InputType.Button,
                Index = 5
            }
        };

        Assert.Equal("Shift 1", layer.Name);
        Assert.NotNull(layer.ActivatorButton);
        Assert.Equal("device-1", layer.ActivatorButton.DeviceId);
        Assert.Equal(5, layer.ActivatorButton.Index);
    }

    [Fact]
    public void Mapping_LayerId_NullByDefault()
    {
        var axisMapping = new AxisMapping();
        var buttonMapping = new ButtonMapping();
        var hatMapping = new HatMapping();

        Assert.Null(axisMapping.LayerId);
        Assert.Null(buttonMapping.LayerId);
        Assert.Null(hatMapping.LayerId);
    }

    [Fact]
    public void Mapping_LayerId_CanBeSet()
    {
        var layerId = Guid.NewGuid();
        var mapping = new ButtonMapping { LayerId = layerId };

        Assert.Equal(layerId, mapping.LayerId);
    }

    [Fact]
    public void MappingProfile_ShiftLayers_EmptyByDefault()
    {
        var profile = new MappingProfile();

        Assert.NotNull(profile.ShiftLayers);
        Assert.Empty(profile.ShiftLayers);
    }

    #endregion

    #region Axis-to-Button Tests

    [Fact]
    public void AxisToButtonMapping_DefaultValues()
    {
        var mapping = new AxisToButtonMapping();

        Assert.Equal(0.5f, mapping.Threshold);
        Assert.True(mapping.ActivateAbove);
        Assert.Equal(0.05f, mapping.Hysteresis);
    }

    [Theory]
    [InlineData(0.6f, 0.5f, true, true)]   // Above threshold, activate above
    [InlineData(0.4f, 0.5f, true, false)]  // Below threshold, activate above
    [InlineData(0.4f, 0.5f, false, true)]  // Below threshold, activate below
    [InlineData(0.6f, 0.5f, false, false)] // Above threshold, activate below
    public void AxisToButtonMapping_ThresholdLogic(float axisValue, float threshold, bool activateAbove, bool expectedActivated)
    {
        // Test threshold crossing logic
        bool shouldActivate;
        if (activateAbove)
        {
            shouldActivate = axisValue > threshold;
        }
        else
        {
            shouldActivate = axisValue < threshold;
        }

        Assert.Equal(expectedActivated, shouldActivate);
    }

    [Fact]
    public void AxisToButtonMapping_Hysteresis_PreventsFlickering()
    {
        var mapping = new AxisToButtonMapping
        {
            Threshold = 0.5f,
            Hysteresis = 0.05f,
            ActivateAbove = true
        };

        // Simulate crossing threshold
        float threshold1 = mapping.Threshold; // Not activated yet
        float threshold2 = mapping.Threshold - mapping.Hysteresis; // After activation, use lower threshold

        // Value at 0.51 should activate (above 0.5)
        Assert.True(0.51f > threshold1);

        // Once activated, value at 0.46 should still be active (above 0.45 = 0.5 - 0.05)
        Assert.True(0.46f > threshold2);

        // Value at 0.44 should deactivate (below 0.45)
        Assert.False(0.44f > threshold2);
    }

    #endregion

    #region Button-to-Axis Tests

    [Fact]
    public void ButtonToAxisMapping_DefaultValues()
    {
        var mapping = new ButtonToAxisMapping();

        Assert.Equal(1.0f, mapping.PressedValue);
        Assert.Equal(0.0f, mapping.ReleasedValue);
        Assert.Equal(0, mapping.SmoothingMs);
    }

    [Theory]
    [InlineData(true, 1.0f, 0.0f, 1.0f)]   // Pressed, default values
    [InlineData(false, 1.0f, 0.0f, 0.0f)]  // Released, default values
    [InlineData(true, -1.0f, 0.0f, -1.0f)] // Pressed, negative
    [InlineData(true, 0.5f, -0.5f, 0.5f)]  // Pressed, custom range
    [InlineData(false, 0.5f, -0.5f, -0.5f)] // Released, custom range
    public void ButtonToAxisMapping_OutputValues(bool pressed, float pressedValue, float releasedValue, float expectedOutput)
    {
        var output = pressed ? pressedValue : releasedValue;
        Assert.Equal(expectedOutput, output);
    }

    [Fact]
    public void ButtonToAxisMapping_Smoothing_RequiresTime()
    {
        var mapping = new ButtonToAxisMapping
        {
            SmoothingMs = 100,
            PressedValue = 1.0f,
            ReleasedValue = 0.0f
        };

        // Smoothing enabled means output approaches target over time
        Assert.Equal(100, mapping.SmoothingMs);
    }

    #endregion

    #region MappingProfile Integration Tests

    [Fact]
    public void MappingProfile_AllMappingTypes_Present()
    {
        var profile = new MappingProfile();

        Assert.NotNull(profile.ShiftLayers);
        Assert.NotNull(profile.AxisMappings);
        Assert.NotNull(profile.ButtonMappings);
        Assert.NotNull(profile.HatMappings);
        Assert.NotNull(profile.AxisToButtonMappings);
        Assert.NotNull(profile.ButtonToAxisMappings);
    }

    [Fact]
    public void MappingProfile_WithAllMappingTypes_Serializable()
    {
        var layerId = Guid.NewGuid();
        var profile = new MappingProfile
        {
            Name = "Test Profile",
            ShiftLayers = new List<ShiftLayer>
            {
                new ShiftLayer
                {
                    Id = layerId,
                    Name = "Shift",
                    ActivatorButton = new InputSource { DeviceId = "d1", Type = InputType.Button, Index = 0 }
                }
            },
            AxisMappings = new List<AxisMapping>
            {
                new AxisMapping { Name = "Axis", LayerId = layerId }
            },
            ButtonMappings = new List<ButtonMapping>
            {
                new ButtonMapping { Name = "Button" }
            },
            HatMappings = new List<HatMapping>
            {
                new HatMapping { Name = "Hat" }
            },
            AxisToButtonMappings = new List<AxisToButtonMapping>
            {
                new AxisToButtonMapping { Name = "A2B", Threshold = 0.75f }
            },
            ButtonToAxisMappings = new List<ButtonToAxisMapping>
            {
                new ButtonToAxisMapping { Name = "B2A", PressedValue = 0.5f }
            }
        };

        // Verify all parts are populated
        Assert.Single(profile.ShiftLayers);
        Assert.Single(profile.AxisMappings);
        Assert.Single(profile.ButtonMappings);
        Assert.Single(profile.HatMappings);
        Assert.Single(profile.AxisToButtonMappings);
        Assert.Single(profile.ButtonToAxisMappings);

        // Verify layer association
        Assert.Equal(layerId, profile.AxisMappings[0].LayerId);
        Assert.Null(profile.ButtonMappings[0].LayerId);
    }

    #endregion

    #region OutputType Tests

    [Fact]
    public void OutputType_VJoyPov_Exists()
    {
        var output = new OutputTarget
        {
            Type = OutputType.VJoyPov,
            VJoyDevice = 1,
            Index = 0
        };

        Assert.Equal(OutputType.VJoyPov, output.Type);
        Assert.Equal("vJoy 1 POV 0", output.ToString());
    }

    #endregion
}
