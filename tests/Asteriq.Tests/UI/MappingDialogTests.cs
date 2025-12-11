using Asteriq.Models;
using Asteriq.Services;
using Asteriq.UI;

namespace Asteriq.Tests.UI;

public class MappingDialogTests
{
    #region MappingDialogResult Tests

    [Fact]
    public void Constructor_WithNoArguments_SetsDefaultValues()
    {
        var result = new MappingDialogResult();

        Assert.False(result.Success);
        Assert.Null(result.Input);
        Assert.Null(result.Output);
        Assert.Equal(ButtonMode.Normal, result.ButtonMode);
        Assert.Null(result.AxisCurve);
        Assert.Equal("", result.MappingName);
    }

    [Fact]
    public void Properties_WhenSet_ReturnCorrectValues()
    {
        var input = new DetectedInput
        {
            DeviceGuid = Guid.NewGuid(),
            DeviceName = "Test Device",
            Type = InputType.Button,
            Index = 5,
            Value = 1f
        };
        var output = new OutputTarget
        {
            Type = OutputType.VJoyButton,
            VJoyDevice = 1,
            Index = 3
        };

        var result = new MappingDialogResult
        {
            Success = true,
            Input = input,
            Output = output,
            ButtonMode = ButtonMode.Toggle,
            MappingName = "Test Mapping"
        };

        Assert.True(result.Success);
        Assert.Same(input, result.Input);
        Assert.Same(output, result.Output);
        Assert.Equal(ButtonMode.Toggle, result.ButtonMode);
        Assert.Equal("Test Mapping", result.MappingName);
    }

    [Fact]
    public void AxisCurve_WhenSet_ReturnsCorrectCurveProperties()
    {
        var curve = new AxisCurve
        {
            Type = CurveType.SCurve,
            Curvature = 0.5f,
            Deadzone = 0.05f,
            Saturation = 1.0f
        };

        var result = new MappingDialogResult
        {
            Success = true,
            AxisCurve = curve
        };

        Assert.NotNull(result.AxisCurve);
        Assert.Equal(CurveType.SCurve, result.AxisCurve.Type);
        Assert.Equal(0.5f, result.AxisCurve.Curvature);
        Assert.Equal(0.05f, result.AxisCurve.Deadzone);
    }

    #endregion

    #region MappingDialogState Tests

    [Fact]
    public void GetValues_WhenCalled_ContainsAllExpectedStates()
    {
        var values = Enum.GetValues<MappingDialogState>();

        Assert.Contains(MappingDialogState.WaitingForInput, values);
        Assert.Contains(MappingDialogState.SelectingOutput, values);
        Assert.Contains(MappingDialogState.ConfiguringOptions, values);
        Assert.Contains(MappingDialogState.Complete, values);
    }

    [Fact]
    public void GetValues_WhenCalled_ReturnsWaitingForInputAsFirstValue()
    {
        var values = Enum.GetValues<MappingDialogState>();

        Assert.Equal(MappingDialogState.WaitingForInput, values.Cast<MappingDialogState>().First());
    }

    #endregion

    #region Dialog Workflow Tests (without actually showing the dialog)

    [Fact]
    public void Output_WithButtonInput_HasVJoyButtonType()
    {
        var result = new MappingDialogResult
        {
            Success = true,
            Input = new DetectedInput
            {
                DeviceGuid = Guid.NewGuid(),
                DeviceName = "Joystick",
                Type = InputType.Button,
                Index = 0,
                Value = 1f
            },
            Output = new OutputTarget
            {
                Type = OutputType.VJoyButton,
                VJoyDevice = 1,
                Index = 5
            }
        };

        Assert.Equal(OutputType.VJoyButton, result.Output!.Type);
    }

    [Fact]
    public void Output_WithAxisInput_HasVJoyAxisType()
    {
        var result = new MappingDialogResult
        {
            Success = true,
            Input = new DetectedInput
            {
                DeviceGuid = Guid.NewGuid(),
                DeviceName = "Throttle",
                Type = InputType.Axis,
                Index = 0,
                Value = 0.5f
            },
            Output = new OutputTarget
            {
                Type = OutputType.VJoyAxis,
                VJoyDevice = 1,
                Index = 2
            }
        };

        Assert.Equal(OutputType.VJoyAxis, result.Output!.Type);
    }

    [Fact]
    public void ToButtonMapping_WithValidResult_CreatesCorrectMapping()
    {
        var result = new MappingDialogResult
        {
            Success = true,
            Input = new DetectedInput
            {
                DeviceGuid = Guid.NewGuid(),
                DeviceName = "Stick",
                Type = InputType.Button,
                Index = 3,
                Value = 1f
            },
            Output = new OutputTarget
            {
                Type = OutputType.VJoyButton,
                VJoyDevice = 1,
                Index = 10
            },
            ButtonMode = ButtonMode.Toggle,
            MappingName = "Fire Toggle"
        };

        var mapping = new ButtonMapping
        {
            Name = result.MappingName,
            Inputs = new List<InputSource> { result.Input!.ToInputSource() },
            Output = result.Output!,
            Mode = result.ButtonMode
        };

        Assert.Equal("Fire Toggle", mapping.Name);
        Assert.Single(mapping.Inputs);
        Assert.Equal(ButtonMode.Toggle, mapping.Mode);
        Assert.Equal(OutputType.VJoyButton, mapping.Output.Type);
    }

    [Fact]
    public void ToAxisMapping_WithValidResult_CreatesCorrectMapping()
    {
        var result = new MappingDialogResult
        {
            Success = true,
            Input = new DetectedInput
            {
                DeviceGuid = Guid.NewGuid(),
                DeviceName = "Throttle",
                Type = InputType.Axis,
                Index = 0,
                Value = 0.5f
            },
            Output = new OutputTarget
            {
                Type = OutputType.VJoyAxis,
                VJoyDevice = 1,
                Index = 5
            },
            AxisCurve = new AxisCurve(),
            MappingName = "Throttle"
        };

        var mapping = new AxisMapping
        {
            Name = result.MappingName,
            Inputs = new List<InputSource> { result.Input!.ToInputSource() },
            Output = result.Output!,
            Curve = result.AxisCurve ?? new AxisCurve()
        };

        Assert.Equal("Throttle", mapping.Name);
        Assert.Single(mapping.Inputs);
        Assert.Equal(OutputType.VJoyAxis, mapping.Output.Type);
        Assert.NotNull(mapping.Curve);
    }

    #endregion
}
