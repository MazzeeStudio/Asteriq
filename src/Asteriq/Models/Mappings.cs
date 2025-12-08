namespace Asteriq.Models;

/// <summary>
/// Types of input sources
/// </summary>
public enum InputType
{
    Axis,
    Button,
    Hat
}

/// <summary>
/// Types of output targets
/// </summary>
public enum OutputType
{
    VJoyAxis,
    VJoyButton,
    VJoyPov,
    Keyboard
}

/// <summary>
/// Button activation modes
/// </summary>
public enum ButtonMode
{
    /// <summary>Output mirrors input state</summary>
    Normal,

    /// <summary>Output toggles on each press</summary>
    Toggle,

    /// <summary>Output pulses briefly on press</summary>
    Pulse,

    /// <summary>Output activates after held for duration</summary>
    HoldToActivate
}

/// <summary>
/// Axis response curve types
/// </summary>
public enum CurveType
{
    /// <summary>Linear 1:1 response</summary>
    Linear,

    /// <summary>S-curve for fine center control</summary>
    SCurve,

    /// <summary>Exponential for progressive response</summary>
    Exponential,

    /// <summary>Custom curve defined by control points</summary>
    Custom
}

/// <summary>
/// Merge operations for combining multiple inputs
/// </summary>
public enum MergeOperation
{
    /// <summary>Average of inputs</summary>
    Average,

    /// <summary>Minimum value</summary>
    Minimum,

    /// <summary>Maximum value</summary>
    Maximum,

    /// <summary>Sum (clamped to -1..1)</summary>
    Sum
}

/// <summary>
/// Identifies an input source on a physical device
/// </summary>
public class InputSource
{
    /// <summary>Device VID:PID for stable identification</summary>
    public string DeviceId { get; set; } = "";

    /// <summary>Human-readable device name</summary>
    public string DeviceName { get; set; } = "";

    /// <summary>Type of input (axis, button, hat)</summary>
    public InputType Type { get; set; }

    /// <summary>Index of the input on the device (0-based)</summary>
    public int Index { get; set; }

    public override string ToString() => $"{DeviceName} {Type} {Index}";
}

/// <summary>
/// Identifies an output target
/// </summary>
public class OutputTarget
{
    /// <summary>Type of output</summary>
    public OutputType Type { get; set; }

    /// <summary>vJoy device ID (1-16) or 0 for keyboard</summary>
    public uint VJoyDevice { get; set; }

    /// <summary>Axis/button index for vJoy, or virtual key code for keyboard</summary>
    public int Index { get; set; }

    /// <summary>Modifier keys for keyboard output</summary>
    public int[]? Modifiers { get; set; }

    public override string ToString() => Type switch
    {
        OutputType.VJoyAxis => $"vJoy {VJoyDevice} Axis {Index}",
        OutputType.VJoyButton => $"vJoy {VJoyDevice} Button {Index}",
        OutputType.VJoyPov => $"vJoy {VJoyDevice} POV {Index}",
        OutputType.Keyboard => $"Key {Index}",
        _ => "Unknown"
    };
}

/// <summary>
/// Configuration for axis response curve
/// </summary>
public class AxisCurve
{
    /// <summary>Type of curve</summary>
    public CurveType Type { get; set; } = CurveType.Linear;

    /// <summary>Curvature amount (-1.0 to 1.0, 0 = linear)</summary>
    public float Curvature { get; set; } = 0f;

    /// <summary>Deadzone at center (0.0 to 1.0)</summary>
    public float Deadzone { get; set; } = 0f;

    /// <summary>Saturation point (0.0 to 1.0, where output reaches max)</summary>
    public float Saturation { get; set; } = 1f;

    /// <summary>Custom curve control points (for CurveType.Custom)</summary>
    public List<(float input, float output)>? ControlPoints { get; set; }

    /// <summary>
    /// Apply the curve to an input value
    /// </summary>
    public float Apply(float input)
    {
        // Apply deadzone
        float absInput = Math.Abs(input);
        if (absInput < Deadzone)
            return 0f;

        // Scale input from deadzone to saturation
        float sign = Math.Sign(input);
        float scaled = (absInput - Deadzone) / (Saturation - Deadzone);
        scaled = Math.Clamp(scaled, 0f, 1f);

        // Apply curve
        float output = Type switch
        {
            CurveType.Linear => scaled,
            CurveType.SCurve => ApplySCurve(scaled),
            CurveType.Exponential => ApplyExponential(scaled),
            CurveType.Custom => ApplyCustom(scaled),
            _ => scaled
        };

        return sign * Math.Clamp(output, 0f, 1f);
    }

    private float ApplySCurve(float x)
    {
        // S-curve using smoothstep with curvature adjustment
        float t = x;
        float curve = Math.Clamp(Curvature, -1f, 1f);

        if (curve >= 0)
        {
            // More curve = flatter center, steeper edges
            float power = 1f + curve * 2f;
            t = (float)Math.Pow(t, power);
        }
        else
        {
            // Negative = steeper center, flatter edges
            float power = 1f / (1f - curve * 2f);
            t = (float)Math.Pow(t, power);
        }

        return t;
    }

    private float ApplyExponential(float x)
    {
        float curve = Math.Clamp(Curvature, -1f, 1f);
        float power = 1f + curve * 2f;
        return (float)Math.Pow(x, power);
    }

    private float ApplyCustom(float x)
    {
        if (ControlPoints == null || ControlPoints.Count < 2)
            return x;

        // Linear interpolation between control points
        for (int i = 0; i < ControlPoints.Count - 1; i++)
        {
            var (x1, y1) = ControlPoints[i];
            var (x2, y2) = ControlPoints[i + 1];

            if (x >= x1 && x <= x2)
            {
                float t = (x - x1) / (x2 - x1);
                return y1 + t * (y2 - y1);
            }
        }

        return x;
    }
}

/// <summary>
/// A shift layer that can be activated by holding a button
/// </summary>
public class ShiftLayer
{
    /// <summary>Unique identifier for this layer</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Layer name (e.g., "Shift 1", "Mode A")</summary>
    public string Name { get; set; } = "";

    /// <summary>Button input that activates this layer</summary>
    public InputSource? ActivatorButton { get; set; }

    /// <summary>Whether this layer is currently active (runtime state)</summary>
    internal bool IsActive { get; set; } = false;
}

/// <summary>
/// A mapping from one or more inputs to an output
/// </summary>
public class Mapping
{
    /// <summary>Unique identifier for this mapping</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>User-friendly name</summary>
    public string Name { get; set; } = "";

    /// <summary>Whether this mapping is active</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Input source(s)</summary>
    public List<InputSource> Inputs { get; set; } = new();

    /// <summary>Output target</summary>
    public OutputTarget Output { get; set; } = new();

    /// <summary>For multiple inputs, how to merge them</summary>
    public MergeOperation MergeOp { get; set; } = MergeOperation.Average;

    /// <summary>Invert the output</summary>
    public bool Invert { get; set; } = false;

    /// <summary>Layer this mapping belongs to (null = base layer, always active)</summary>
    public Guid? LayerId { get; set; }
}

/// <summary>
/// Axis-specific mapping with curve support
/// </summary>
public class AxisMapping : Mapping
{
    /// <summary>Response curve configuration</summary>
    public AxisCurve Curve { get; set; } = new();
}

/// <summary>
/// Button-specific mapping with mode support
/// </summary>
public class ButtonMapping : Mapping
{
    /// <summary>Button activation mode</summary>
    public ButtonMode Mode { get; set; } = ButtonMode.Normal;

    /// <summary>Pulse duration in milliseconds (for Pulse mode)</summary>
    public int PulseDurationMs { get; set; } = 100;

    /// <summary>Hold duration in milliseconds (for HoldToActivate mode)</summary>
    public int HoldDurationMs { get; set; } = 500;

    /// <summary>Internal toggle state</summary>
    internal bool ToggleState { get; set; } = false;

    /// <summary>Internal timestamp for hold detection</summary>
    internal DateTime? HoldStartTime { get; set; }
}

/// <summary>
/// Hat/POV-specific mapping
/// </summary>
public class HatMapping : Mapping
{
    /// <summary>Use continuous POV (angle-based) vs discrete (4-direction)</summary>
    public bool UseContinuous { get; set; } = true;
}

/// <summary>
/// Axis-to-button mapping - triggers button when axis crosses threshold
/// </summary>
public class AxisToButtonMapping : Mapping
{
    /// <summary>Threshold value where button activates (-1.0 to 1.0)</summary>
    public float Threshold { get; set; } = 0.5f;

    /// <summary>Activate when above threshold (true) or below (false)</summary>
    public bool ActivateAbove { get; set; } = true;

    /// <summary>Hysteresis to prevent flickering (0.0 to 0.5)</summary>
    public float Hysteresis { get; set; } = 0.05f;

    /// <summary>Internal state tracking</summary>
    internal bool IsActivated { get; set; } = false;
}

/// <summary>
/// Button-to-axis mapping - outputs axis value when button is pressed
/// </summary>
public class ButtonToAxisMapping : Mapping
{
    /// <summary>Axis value when button is pressed (-1.0 to 1.0)</summary>
    public float PressedValue { get; set; } = 1.0f;

    /// <summary>Axis value when button is released (-1.0 to 1.0)</summary>
    public float ReleasedValue { get; set; } = 0.0f;

    /// <summary>Smoothing time in milliseconds (0 = instant)</summary>
    public int SmoothingMs { get; set; } = 0;

    /// <summary>Internal current value for smoothing</summary>
    internal float CurrentValue { get; set; } = 0f;

    /// <summary>Internal timestamp for smoothing</summary>
    internal DateTime? LastUpdate { get; set; }
}

/// <summary>
/// A profile containing all mappings for a configuration
/// </summary>
public class MappingProfile
{
    /// <summary>Unique identifier</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Profile name</summary>
    public string Name { get; set; } = "Default";

    /// <summary>Description</summary>
    public string Description { get; set; } = "";

    /// <summary>Shift layers for mode switching</summary>
    public List<ShiftLayer> ShiftLayers { get; set; } = new();

    /// <summary>Axis mappings</summary>
    public List<AxisMapping> AxisMappings { get; set; } = new();

    /// <summary>Button mappings</summary>
    public List<ButtonMapping> ButtonMappings { get; set; } = new();

    /// <summary>Hat/POV mappings</summary>
    public List<HatMapping> HatMappings { get; set; } = new();

    /// <summary>Axis-to-button mappings</summary>
    public List<AxisToButtonMapping> AxisToButtonMappings { get; set; } = new();

    /// <summary>Button-to-axis mappings</summary>
    public List<ButtonToAxisMapping> ButtonToAxisMappings { get; set; } = new();

    /// <summary>When created</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last modified</summary>
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}
