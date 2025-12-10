using Asteriq.Models;

namespace Asteriq.Tests.Services;

public class MergeOperationTests
{
    [Fact]
    public void Average_MultipleValues_ReturnsAverage()
    {
        var values = new List<float> { 0.2f, 0.4f, 0.6f };

        float result = ApplyMerge(values, MergeOperation.Average);

        Assert.Equal(0.4f, result, 3);
    }

    [Fact]
    public void Average_SingleValue_ReturnsThatValue()
    {
        var values = new List<float> { 0.5f };

        float result = ApplyMerge(values, MergeOperation.Average);

        Assert.Equal(0.5f, result, 3);
    }

    [Fact]
    public void Minimum_MultipleValues_ReturnsSmallest()
    {
        var values = new List<float> { 0.3f, -0.5f, 0.8f };

        float result = ApplyMerge(values, MergeOperation.Minimum);

        Assert.Equal(-0.5f, result, 3);
    }

    [Fact]
    public void Maximum_MultipleValues_ReturnsLargest()
    {
        var values = new List<float> { 0.3f, -0.5f, 0.8f };

        float result = ApplyMerge(values, MergeOperation.Maximum);

        Assert.Equal(0.8f, result, 3);
    }

    [Fact]
    public void Sum_WithinRange_ReturnsSummed()
    {
        var values = new List<float> { 0.2f, 0.3f };

        float result = ApplyMerge(values, MergeOperation.Sum);

        Assert.Equal(0.5f, result, 3);
    }

    [Fact]
    public void Sum_ExceedsRange_ClampedToOne()
    {
        var values = new List<float> { 0.8f, 0.5f };

        float result = ApplyMerge(values, MergeOperation.Sum);

        Assert.Equal(1.0f, result, 3);
    }

    [Fact]
    public void Sum_NegativeExceedsRange_ClampedToNegativeOne()
    {
        var values = new List<float> { -0.8f, -0.5f };

        float result = ApplyMerge(values, MergeOperation.Sum);

        Assert.Equal(-1.0f, result, 3);
    }

    [Fact]
    public void Merge_EmptyList_ReturnsZero()
    {
        var values = new List<float>();

        float result = ApplyMerge(values, MergeOperation.Average);

        Assert.Equal(0f, result);
    }

    // Mirror the ApplyMerge logic from MappingEngine
    private static float ApplyMerge(List<float> values, MergeOperation op)
    {
        if (values.Count == 0)
            return 0f;

        return op switch
        {
            MergeOperation.Average => values.Average(),
            MergeOperation.Minimum => values.Min(),
            MergeOperation.Maximum => values.Max(),
            MergeOperation.Sum => Math.Clamp(values.Sum(), -1f, 1f),
            _ => values[0]
        };
    }
}

/// <summary>
/// Tests button mode behavior by testing the logic without accessing internal state.
/// Uses a separate test helper class that mirrors MappingEngine logic.
/// </summary>
public class ButtonModeTests
{
    [Fact]
    public void Normal_InputPressed_OutputPressed()
    {
        var helper = new ButtonModeHelper(ButtonMode.Normal);

        bool result = helper.Process(true);

        Assert.True(result);
    }

    [Fact]
    public void Normal_InputReleased_OutputReleased()
    {
        var helper = new ButtonModeHelper(ButtonMode.Normal);

        bool result = helper.Process(false);

        Assert.False(result);
    }

    [Fact]
    public void Toggle_FirstPress_TogglesOn()
    {
        var helper = new ButtonModeHelper(ButtonMode.Toggle);

        // First press - rising edge
        bool result = helper.Process(true);

        Assert.True(result);
    }

    [Fact]
    public void Toggle_HoldingButton_StaysToggled()
    {
        var helper = new ButtonModeHelper(ButtonMode.Toggle);

        // First press - toggles on
        bool first = helper.Process(true);
        Assert.True(first);

        // Continue holding - should stay on (not re-toggle)
        bool hold1 = helper.Process(true);
        Assert.True(hold1);

        bool hold2 = helper.Process(true);
        Assert.True(hold2);
    }

    [Fact]
    public void Toggle_ReleaseAndPress_TogglesOff()
    {
        var helper = new ButtonModeHelper(ButtonMode.Toggle);

        // First press - toggles on
        helper.Process(true);

        // Release
        helper.Process(false);

        // Press again - toggles off
        bool result = helper.Process(true);
        Assert.False(result);
    }

    [Fact]
    public void Toggle_MultipleToggleCycles_AlternatesState()
    {
        var helper = new ButtonModeHelper(ButtonMode.Toggle);

        // Cycle 1: Press -> On
        helper.Process(true);
        bool state1 = helper.Process(true);
        Assert.True(state1);

        // Release
        helper.Process(false);

        // Cycle 2: Press -> Off
        bool state2 = helper.Process(true);
        Assert.False(state2);

        // Release
        helper.Process(false);

        // Cycle 3: Press -> On
        bool state3 = helper.Process(true);
        Assert.True(state3);
    }

    [Fact]
    public void Pulse_OnPress_OutputsTrue()
    {
        var helper = new ButtonModeHelper(ButtonMode.Pulse, pulseDurationMs: 100);

        // Press - starts pulse
        bool result = helper.Process(true);

        Assert.True(result);
    }

    [Fact]
    public void Pulse_AfterDuration_OutputsFalse()
    {
        var helper = new ButtonModeHelper(ButtonMode.Pulse, pulseDurationMs: 10);

        // Press - starts pulse
        helper.Process(true);

        // Wait for pulse to expire
        Thread.Sleep(50);

        // Still pressing, but pulse expired
        bool result = helper.Process(true);

        Assert.False(result);
    }

    [Fact]
    public void HoldToActivate_ShortPress_DoesNotActivate()
    {
        var helper = new ButtonModeHelper(ButtonMode.HoldToActivate, holdDurationMs: 500);

        // Press briefly - not enough time
        bool result = helper.Process(true);

        Assert.False(result);
    }

    [Fact]
    public void HoldToActivate_LongPress_Activates()
    {
        var helper = new ButtonModeHelper(ButtonMode.HoldToActivate, holdDurationMs: 50);

        // Start pressing
        helper.Process(true);

        // Wait long enough
        Thread.Sleep(100);

        // Check again
        bool result = helper.Process(true);

        Assert.True(result);
    }

    [Fact]
    public void HoldToActivate_Release_Deactivates()
    {
        var helper = new ButtonModeHelper(ButtonMode.HoldToActivate, holdDurationMs: 10);

        // Activate
        helper.Process(true);
        Thread.Sleep(50);
        bool activated = helper.Process(true);
        Assert.True(activated);

        // Release
        bool released = helper.Process(false);
        Assert.False(released);
    }

    /// <summary>
    /// Helper class that mirrors MappingEngine button mode logic for testing
    /// </summary>
    private class ButtonModeHelper
    {
        private readonly ButtonMode _mode;
        private readonly int _pulseDurationMs;
        private readonly int _holdDurationMs;
        private bool _toggleState;
        private DateTime? _holdStartTime;

        public ButtonModeHelper(ButtonMode mode, int pulseDurationMs = 100, int holdDurationMs = 500)
        {
            _mode = mode;
            _pulseDurationMs = pulseDurationMs;
            _holdDurationMs = holdDurationMs;
        }

        public bool Process(bool inputPressed)
        {
            switch (_mode)
            {
                case ButtonMode.Normal:
                    return inputPressed;

                case ButtonMode.Toggle:
                    if (inputPressed && _holdStartTime == null)
                    {
                        _toggleState = !_toggleState;
                        _holdStartTime = DateTime.UtcNow;
                    }
                    else if (!inputPressed)
                    {
                        _holdStartTime = null;
                    }
                    return _toggleState;

                case ButtonMode.Pulse:
                    if (inputPressed && _holdStartTime == null)
                    {
                        _holdStartTime = DateTime.UtcNow;
                    }

                    if (_holdStartTime != null)
                    {
                        var elapsed = (DateTime.UtcNow - _holdStartTime.Value).TotalMilliseconds;
                        if (elapsed < _pulseDurationMs)
                            return true;

                        if (!inputPressed)
                            _holdStartTime = null;
                    }
                    return false;

                case ButtonMode.HoldToActivate:
                    if (inputPressed)
                    {
                        if (_holdStartTime == null)
                            _holdStartTime = DateTime.UtcNow;

                        var elapsed = (DateTime.UtcNow - _holdStartTime.Value).TotalMilliseconds;
                        return elapsed >= _holdDurationMs;
                    }
                    else
                    {
                        _holdStartTime = null;
                        return false;
                    }

                default:
                    return inputPressed;
            }
        }
    }
}

public class MappingModelTests
{
    [Fact]
    public void AxisMapping_DefaultCurve_IsLinear()
    {
        var mapping = new AxisMapping();

        Assert.Equal(CurveType.Linear, mapping.Curve.Type);
        Assert.Equal(0f, mapping.Curve.Deadzone);
        Assert.Equal(1f, mapping.Curve.Saturation);
    }

    [Fact]
    public void ButtonMapping_DefaultMode_IsNormal()
    {
        var mapping = new ButtonMapping();

        Assert.Equal(ButtonMode.Normal, mapping.Mode);
        Assert.Equal(100, mapping.PulseDurationMs);
        Assert.Equal(500, mapping.HoldDurationMs);
    }

    [Fact]
    public void ButtonMapping_PulseDuration_CanBeSet()
    {
        var mapping = new ButtonMapping
        {
            Mode = ButtonMode.Pulse,
            PulseDurationMs = 250
        };

        Assert.Equal(ButtonMode.Pulse, mapping.Mode);
        Assert.Equal(250, mapping.PulseDurationMs);
    }

    [Fact]
    public void ButtonMapping_HoldDuration_CanBeSet()
    {
        var mapping = new ButtonMapping
        {
            Mode = ButtonMode.HoldToActivate,
            HoldDurationMs = 1000
        };

        Assert.Equal(ButtonMode.HoldToActivate, mapping.Mode);
        Assert.Equal(1000, mapping.HoldDurationMs);
    }

    [Theory]
    [InlineData(100)]   // Minimum typical value
    [InlineData(500)]   // Mid-range
    [InlineData(1000)]  // Maximum typical value
    public void ButtonMapping_PulseDuration_AcceptsValidRange(int duration)
    {
        var mapping = new ButtonMapping { PulseDurationMs = duration };
        Assert.Equal(duration, mapping.PulseDurationMs);
    }

    [Theory]
    [InlineData(200)]   // Minimum typical value
    [InlineData(1000)]  // Mid-range
    [InlineData(2000)]  // Maximum typical value
    public void ButtonMapping_HoldDuration_AcceptsValidRange(int duration)
    {
        var mapping = new ButtonMapping { HoldDurationMs = duration };
        Assert.Equal(duration, mapping.HoldDurationMs);
    }

    [Fact]
    public void ButtonMapping_DurationValues_IndependentOfMode()
    {
        // Duration values should be stored regardless of mode
        var mapping = new ButtonMapping
        {
            Mode = ButtonMode.Normal,
            PulseDurationMs = 300,
            HoldDurationMs = 800
        };

        // Values should persist even though mode is Normal
        Assert.Equal(300, mapping.PulseDurationMs);
        Assert.Equal(800, mapping.HoldDurationMs);

        // Changing mode shouldn't affect stored duration values
        mapping.Mode = ButtonMode.Pulse;
        Assert.Equal(300, mapping.PulseDurationMs);

        mapping.Mode = ButtonMode.HoldToActivate;
        Assert.Equal(800, mapping.HoldDurationMs);
    }

    [Fact]
    public void MappingProfile_NewProfile_HasUniqueId()
    {
        var profile1 = new MappingProfile();
        var profile2 = new MappingProfile();

        Assert.NotEqual(profile1.Id, profile2.Id);
        Assert.NotEqual(Guid.Empty, profile1.Id);
    }

    [Fact]
    public void InputSource_ToString_ContainsDeviceInfo()
    {
        var source = new InputSource
        {
            DeviceName = "Test Joystick",
            Type = InputType.Axis,
            Index = 0
        };

        var str = source.ToString();

        Assert.Contains("Test Joystick", str);
        Assert.Contains("Axis", str);
    }

    [Fact]
    public void OutputTarget_ToString_VJoyAxis_FormatsCorrectly()
    {
        var target = new OutputTarget
        {
            Type = OutputType.VJoyAxis,
            VJoyDevice = 1,
            Index = 0
        };

        var str = target.ToString();

        Assert.Contains("vJoy", str);
        Assert.Contains("Axis", str);
    }

    [Fact]
    public void OutputTarget_ToString_VJoyButton_FormatsCorrectly()
    {
        var target = new OutputTarget
        {
            Type = OutputType.VJoyButton,
            VJoyDevice = 2,
            Index = 5
        };

        var str = target.ToString();

        Assert.Contains("vJoy", str);
        Assert.Contains("Button", str);
    }

    [Fact]
    public void Mapping_DefaultMergeOp_IsAverage()
    {
        var mapping = new AxisMapping();

        Assert.Equal(MergeOperation.Average, mapping.MergeOp);
    }

    [Fact]
    public void Mapping_InvertDefault_IsFalse()
    {
        var mapping = new AxisMapping();

        Assert.False(mapping.Invert);
    }

    [Fact]
    public void Mapping_EnabledDefault_IsTrue()
    {
        var mapping = new AxisMapping();

        Assert.True(mapping.Enabled);
    }
}
