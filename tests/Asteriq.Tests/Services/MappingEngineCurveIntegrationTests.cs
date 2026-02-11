using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using Asteriq.VJoy;
using Moq;

namespace Asteriq.Tests.Services;

/// <summary>
/// Integration tests to verify that curves and deadzones are properly applied
/// when processing input through the mapping engine
/// </summary>
public class MappingEngineCurveIntegrationTests
{
    [Fact]
    public void ProcessInput_WithDeadzone_AppliesCurve()
    {
        // Arrange
        var mockVJoy = new Mock<IVJoyService>();
        mockVJoy.Setup(v => v.AcquireDevice(It.IsAny<uint>())).Returns(true);

        float? capturedValue = null;
        mockVJoy.Setup(v => v.SetAxis(It.IsAny<uint>(), It.IsAny<HID_USAGES>(), It.IsAny<float>()))
            .Callback<uint, HID_USAGES, float>((_, _, value) => capturedValue = value);

        var engine = new MappingEngine(mockVJoy.Object);

        var profile = new MappingProfile
        {
            AxisMappings = new List<AxisMapping>
            {
                new()
                {
                    Name = "Test Axis",
                    Inputs = new List<InputSource>
                    {
                        new()
                        {
                            DeviceId = "00000000-0000-0000-0000-000000000001",
                            DeviceName = "TestDevice",
                            Type = InputType.Axis,
                            Index = 0
                        }
                    },
                    Output = new OutputTarget
                    {
                        Type = OutputType.VJoyAxis,
                        VJoyDevice = 1,
                        Index = 0
                    },
                    Curve = new AxisCurve
                    {
                        Type = CurveType.Linear,
                        Deadzone = 0.1f,  // 10% deadzone
                        Saturation = 1.0f
                    }
                }
            }
        };

        engine.LoadProfile(profile);
        engine.Start();

        // Act - send input within deadzone
        var state = new DeviceInputState
        {
            DeviceName = "TestDevice",
            InstanceGuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Axes = new[] { 0.05f },  // Within 10% deadzone
            Buttons = Array.Empty<bool>(),
            Hats = Array.Empty<int>()
        };

        engine.ProcessInput(state);

        // Assert - output should be 0 due to deadzone
        Assert.NotNull(capturedValue);
        Assert.Equal(0f, capturedValue.Value, 2);
    }

    [Fact]
    public void ProcessInput_WithCurve_AppliesCurvature()
    {
        // Arrange
        var mockVJoy = new Mock<IVJoyService>();
        mockVJoy.Setup(v => v.AcquireDevice(It.IsAny<uint>())).Returns(true);

        float? capturedValue = null;
        mockVJoy.Setup(v => v.SetAxis(It.IsAny<uint>(), It.IsAny<HID_USAGES>(), It.IsAny<float>()))
            .Callback<uint, HID_USAGES, float>((_, _, value) => capturedValue = value);

        var engine = new MappingEngine(mockVJoy.Object);

        var profile = new MappingProfile
        {
            AxisMappings = new List<AxisMapping>
            {
                new()
                {
                    Name = "Test Axis",
                    Inputs = new List<InputSource>
                    {
                        new()
                        {
                            DeviceId = "00000000-0000-0000-0000-000000000001",
                            DeviceName = "TestDevice",
                            Type = InputType.Axis,
                            Index = 0
                        }
                    },
                    Output = new OutputTarget
                    {
                        Type = OutputType.VJoyAxis,
                        VJoyDevice = 1,
                        Index = 0
                    },
                    Curve = new AxisCurve
                    {
                        Type = CurveType.Exponential,
                        Curvature = 0.5f,  // Positive curvature reduces sensitivity at center
                        Deadzone = 0f,
                        Saturation = 1.0f
                    }
                }
            }
        };

        engine.LoadProfile(profile);
        engine.Start();

        // Act - send 50% input
        var state = new DeviceInputState
        {
            DeviceName = "TestDevice",
            InstanceGuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Axes = new[] { 0.5f },
            Buttons = Array.Empty<bool>(),
            Hats = Array.Empty<int>()
        };

        engine.ProcessInput(state);

        // Assert - with exponential curve and positive curvature, output should be less than input
        Assert.NotNull(capturedValue);
        Assert.True(capturedValue.Value < 0.5f,
            $"Expected curved output < 0.5, got {capturedValue.Value}");
        Assert.True(capturedValue.Value > 0f,
            $"Expected curved output > 0, got {capturedValue.Value}");
    }

    [Fact]
    public void ProcessInput_WithInversion_InvertsAfterCurve()
    {
        // Arrange
        var mockVJoy = new Mock<IVJoyService>();
        mockVJoy.Setup(v => v.AcquireDevice(It.IsAny<uint>())).Returns(true);

        float? capturedValue = null;
        mockVJoy.Setup(v => v.SetAxis(It.IsAny<uint>(), It.IsAny<HID_USAGES>(), It.IsAny<float>()))
            .Callback<uint, HID_USAGES, float>((_, _, value) => capturedValue = value);

        var engine = new MappingEngine(mockVJoy.Object);

        var profile = new MappingProfile
        {
            AxisMappings = new List<AxisMapping>
            {
                new()
                {
                    Name = "Test Axis",
                    Inputs = new List<InputSource>
                    {
                        new()
                        {
                            DeviceId = "00000000-0000-0000-0000-000000000001",
                            DeviceName = "TestDevice",
                            Type = InputType.Axis,
                            Index = 0
                        }
                    },
                    Output = new OutputTarget
                    {
                        Type = OutputType.VJoyAxis,
                        VJoyDevice = 1,
                        Index = 0
                    },
                    Invert = true,  // Invert the output
                    Curve = new AxisCurve
                    {
                        Type = CurveType.Linear,
                        Deadzone = 0f,
                        Saturation = 1.0f
                    }
                }
            }
        };

        engine.LoadProfile(profile);
        engine.Start();

        // Act - send positive input
        var state = new DeviceInputState
        {
            DeviceName = "TestDevice",
            InstanceGuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Axes = new[] { 0.5f },
            Buttons = Array.Empty<bool>(),
            Hats = Array.Empty<int>()
        };

        engine.ProcessInput(state);

        // Assert - output should be inverted
        Assert.NotNull(capturedValue);
        Assert.True(capturedValue.Value < 0f,
            $"Expected inverted output < 0, got {capturedValue.Value}");
    }

    [Fact]
    public void ProcessInput_WithSaturation_ClampsToPlusOne()
    {
        // Arrange
        var mockVJoy = new Mock<IVJoyService>();
        mockVJoy.Setup(v => v.AcquireDevice(It.IsAny<uint>())).Returns(true);

        float? capturedValue = null;
        mockVJoy.Setup(v => v.SetAxis(It.IsAny<uint>(), It.IsAny<HID_USAGES>(), It.IsAny<float>()))
            .Callback<uint, HID_USAGES, float>((_, _, value) => capturedValue = value);

        var engine = new MappingEngine(mockVJoy.Object);

        var profile = new MappingProfile
        {
            AxisMappings = new List<AxisMapping>
            {
                new()
                {
                    Name = "Test Axis",
                    Inputs = new List<InputSource>
                    {
                        new()
                        {
                            DeviceId = "00000000-0000-0000-0000-000000000001",
                            DeviceName = "TestDevice",
                            Type = InputType.Axis,
                            Index = 0
                        }
                    },
                    Output = new OutputTarget
                    {
                        Type = OutputType.VJoyAxis,
                        VJoyDevice = 1,
                        Index = 0
                    },
                    Curve = new AxisCurve
                    {
                        Type = CurveType.Linear,
                        Deadzone = 0f,
                        Saturation = 0.8f  // Saturate at 80%
                    }
                }
            }
        };

        engine.LoadProfile(profile);
        engine.Start();

        // Act - send input at saturation point
        var state = new DeviceInputState
        {
            DeviceName = "TestDevice",
            InstanceGuid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Axes = new[] { 0.8f },  // At saturation point
            Buttons = Array.Empty<bool>(),
            Hats = Array.Empty<int>()
        };

        engine.ProcessInput(state);

        // Assert - output should be 1.0 (saturated)
        Assert.NotNull(capturedValue);
        Assert.Equal(1.0f, capturedValue.Value, 2);
    }
}
