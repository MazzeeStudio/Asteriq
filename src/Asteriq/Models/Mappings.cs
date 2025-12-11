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

    /// <summary>Key name for keyboard output (e.g., "Space", "A", "F1")</summary>
    public string? KeyName { get; set; }

    /// <summary>Modifier key names for keyboard output (e.g., "Ctrl", "Shift", "Alt")</summary>
    public List<string>? Modifiers { get; set; }

    public override string ToString() => Type switch
    {
        OutputType.VJoyAxis => $"vJoy {VJoyDevice} Axis {Index}",
        OutputType.VJoyButton => $"vJoy {VJoyDevice} Button {Index}",
        OutputType.VJoyPov => $"vJoy {VJoyDevice} POV {Index}",
        OutputType.Keyboard => FormatKeyboardOutput(),
        _ => "Unknown"
    };

    private string FormatKeyboardOutput()
    {
        var modStr = Modifiers is not null && Modifiers.Count > 0 ? string.Join("+", Modifiers) + "+" : "";
        return $"Key {modStr}{KeyName ?? Index.ToString()}";
    }
}

/// <summary>
/// Deadzone mode (following JoystickGremlinEx patterns)
/// </summary>
public enum DeadzoneMode
{
    /// <summary>Centered axis (e.g., joystick) - has center deadzone</summary>
    Centered,

    /// <summary>End-only axis (e.g., throttle/slider) - deadzone at min end only</summary>
    EndOnly
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

    /// <summary>Deadzone mode - centered (joystick) vs end-only (throttle)</summary>
    public DeadzoneMode DeadzoneMode { get; set; } = DeadzoneMode.Centered;

    /// <summary>
    /// Deadzone at low end (for EndOnly mode: 0.0 to 1.0)
    /// For Centered mode: this is the left/negative full extent (-1.0)
    /// Default -1.0 means full negative range is active
    /// </summary>
    public float DeadzoneLow { get; set; } = -1.0f;

    /// <summary>
    /// Deadzone center-left edge (Centered mode only, -1.0 to 0.0)
    /// Values between DeadzoneLow and DeadzoneCenterLow ramp from -1 to 0
    /// Default 0 means no center deadzone on negative side
    /// </summary>
    public float DeadzoneCenterLow { get; set; } = 0f;

    /// <summary>
    /// Deadzone center-right edge (Centered mode only, 0.0 to 1.0)
    /// Values between DeadzoneCenterRight and DeadzoneHigh ramp from 0 to 1
    /// Default 0 means no center deadzone on positive side
    /// </summary>
    public float DeadzoneCenterHigh { get; set; } = 0f;

    /// <summary>
    /// Deadzone at high end (for EndOnly mode: 0.0 to 1.0)
    /// For Centered mode: this is the right/positive full extent (+1.0)
    /// Default +1.0 means full positive range is active
    /// </summary>
    public float DeadzoneHigh { get; set; } = 1.0f;

    /// <summary>Simple deadzone property for backward compatibility (uses center deadzone)</summary>
    public float Deadzone
    {
        get => Math.Max(Math.Abs(DeadzoneCenterLow), Math.Abs(DeadzoneCenterHigh));
        set
        {
            DeadzoneCenterLow = -Math.Abs(value);
            DeadzoneCenterHigh = Math.Abs(value);
        }
    }

    /// <summary>Saturation point (0.0 to 1.0, where output reaches max)</summary>
    public float Saturation { get; set; } = 1f;

    /// <summary>Invert the axis output</summary>
    public bool Inverted { get; set; } = false;

    /// <summary>Custom curve control points (for CurveType.Custom)</summary>
    public List<(float input, float output)>? ControlPoints { get; set; }

    /// <summary>
    /// Apply the curve to an input value (-1 to 1 range)
    /// Processing order (matching JoystickGremlinEx):
    /// 1. Deadzone - filter small movements
    /// 2. Saturation - scale output range
    /// 3. Response curve - shape the response
    /// 4. Inversion - flip if needed
    /// </summary>
    public float Apply(float input)
    {
        // Step 1: Apply deadzone
        float deadzoned;
        if (DeadzoneMode == DeadzoneMode.Centered)
        {
            // Centered deadzone (4-parameter model like JoystickGremlinEx)
            deadzoned = ApplyCenteredDeadzone(input);
        }
        else
        {
            // End-only deadzone (for throttle/slider)
            float normalized = (input + 1f) / 2f; // -1..1 -> 0..1
            deadzoned = ApplyEndDeadzone(normalized);
            deadzoned = deadzoned * 2f - 1f; // 0..1 -> -1..1
        }

        // Step 2: Apply saturation (scales so Saturation input -> 1.0 output)
        float saturated = ApplySaturation(deadzoned);

        // Step 3: Apply response curve to magnitude
        float sign = Math.Sign(saturated);
        float magnitude = Math.Abs(saturated);

        float curved = Type switch
        {
            CurveType.Linear => magnitude,
            CurveType.SCurve => ApplySCurve(magnitude),
            CurveType.Exponential => ApplyExponential(magnitude),
            CurveType.Custom => ApplyCustom(magnitude),
            _ => magnitude
        };

        curved = Math.Clamp(curved, 0f, 1f);

        // Step 4: Apply inversion
        if (Inverted)
            curved = 1f - curved;

        return sign * curved;
    }

    /// <summary>
    /// Apply centered deadzone (4-parameter model matching JoystickGremlinEx)
    /// Params: DeadzoneLow, DeadzoneCenterLow, DeadzoneCenterHigh, DeadzoneHigh
    /// Constraint: -1 <= low < low_center <= 0 <= high_center < high <= 1
    /// </summary>
    private float ApplyCenteredDeadzone(float value)
    {
        // JoystickGremlinEx formula from vjoy.py:1014-1031
        if (value >= 0)
        {
            // Positive region: scale from high_center to high → 0 to 1
            float range = Math.Abs(DeadzoneHigh - DeadzoneCenterHigh);
            if (range <= 0.0001f) return value > DeadzoneCenterHigh ? 1f : 0f;
            return Math.Min(1f, Math.Max(0f, (value - DeadzoneCenterHigh) / range));
        }
        else
        {
            // Negative region: scale from low to low_center → -1 to 0
            float range = Math.Abs(DeadzoneLow - DeadzoneCenterLow);
            if (range <= 0.0001f) return value < DeadzoneCenterLow ? -1f : 0f;
            return Math.Max(-1f, Math.Min(0f, (value - DeadzoneCenterLow) / range));
        }
    }

    /// <summary>
    /// Apply end-only deadzone (for throttle/slider)
    /// Input is 0..1 range, maps to 0..1 output
    /// </summary>
    private float ApplyEndDeadzone(float value)
    {
        float dzLow = Math.Max(0f, (DeadzoneLow + 1f) / 2f); // Convert -1..1 to 0..1
        float dzHigh = Math.Min(1f, (DeadzoneHigh + 1f) / 2f);

        if (value <= dzLow)
            return 0f;
        if (value >= dzHigh)
            return 1.0f;

        float range = dzHigh - dzLow;
        if (range <= 0.0001f) return value;

        return (value - dzLow) / range;
    }

    /// <summary>
    /// Apply saturation - scales output so that input=Saturation produces output=1.0
    /// </summary>
    private float ApplySaturation(float value)
    {
        if (Saturation >= 1.0f) return value;

        float absValue = Math.Abs(value);
        float sign = Math.Sign(value);

        if (absValue >= Saturation)
            return sign * 1.0f;

        return sign * (absValue / Saturation);
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
        if (ControlPoints is null || ControlPoints.Count < 2)
            return x;

        // Catmull-Rom spline interpolation for smooth curves
        for (int i = 0; i < ControlPoints.Count - 1; i++)
        {
            var (x1, y1) = ControlPoints[i];
            var (x2, y2) = ControlPoints[i + 1];

            if (x >= x1 && x <= x2)
            {
                if (Math.Abs(x2 - x1) < 0.001f) return y1;
                float t = (x - x1) / (x2 - x1);

                // Get surrounding points for Catmull-Rom spline
                // p0 = point before p1, p3 = point after p2
                float y0 = i > 0 ? ControlPoints[i - 1].output : y1 - (y2 - y1);
                float y3 = i < ControlPoints.Count - 2 ? ControlPoints[i + 2].output : y2 + (y2 - y1);

                return CatmullRomInterpolate(y0, y1, y2, y3, t);
            }
        }

        return x;
    }

    /// <summary>
    /// Catmull-Rom spline interpolation for smooth curves through control points.
    /// t ranges from 0 to 1, output is between p1 and p2.
    /// </summary>
    private static float CatmullRomInterpolate(float p0, float p1, float p2, float p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
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
/// Assignment of a physical device to a vJoy device slot
/// </summary>
public class DeviceAssignment
{
    /// <summary>Physical device identification</summary>
    public PhysicalDeviceRef PhysicalDevice { get; set; } = new();

    /// <summary>vJoy device ID (1-16)</summary>
    public uint VJoyDevice { get; set; }

    /// <summary>Optional device map override (null = auto-detect)</summary>
    public string? DeviceMapOverride { get; set; }
}

/// <summary>
/// Physical device identification info for matching across sessions
/// </summary>
public class PhysicalDeviceRef
{
    /// <summary>Human-readable device name</summary>
    public string Name { get; set; } = "";

    /// <summary>Device instance GUID (primary identifier)</summary>
    public string Guid { get; set; } = "";

    /// <summary>Vendor:Product ID for fallback matching (e.g., "3344:0194")</summary>
    public string VidPid { get; set; } = "";
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

    /// <summary>Device assignments (physical device → vJoy slot)</summary>
    public List<DeviceAssignment> DeviceAssignments { get; set; } = new();

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

    /// <summary>Get the vJoy device assigned to a physical device by GUID or VID:PID</summary>
    public uint? GetVJoyDeviceForPhysical(string deviceGuid, string? vidPid = null)
    {
        // First try exact GUID match
        var assignment = DeviceAssignments.FirstOrDefault(a =>
            !string.IsNullOrEmpty(a.PhysicalDevice.Guid) &&
            a.PhysicalDevice.Guid.Equals(deviceGuid, StringComparison.OrdinalIgnoreCase));

        // Fallback to VID:PID match
        if (assignment is null && !string.IsNullOrEmpty(vidPid))
        {
            assignment = DeviceAssignments.FirstOrDefault(a =>
                !string.IsNullOrEmpty(a.PhysicalDevice.VidPid) &&
                a.PhysicalDevice.VidPid.Equals(vidPid, StringComparison.OrdinalIgnoreCase));
        }

        return assignment?.VJoyDevice;
    }

    /// <summary>Get the physical device assigned to a vJoy device</summary>
    public DeviceAssignment? GetAssignmentForVJoy(uint vJoyDevice)
    {
        return DeviceAssignments.FirstOrDefault(a => a.VJoyDevice == vJoyDevice);
    }

    /// <summary>
    /// Find the vJoy output for a given physical input.
    /// Used by SC Bindings to determine what vJoy binding to create when user presses a physical input.
    /// </summary>
    /// <param name="deviceId">Physical device ID (GUID or VID:PID)</param>
    /// <param name="inputType">Type of input (Axis, Button, Hat)</param>
    /// <param name="inputIndex">Index of the input on the device</param>
    /// <returns>The vJoy output target if a mapping exists, null otherwise</returns>
    public OutputTarget? GetVJoyOutputForPhysicalInput(string deviceId, InputType inputType, int inputIndex)
    {
        // Search through all mapping types for a match
        IEnumerable<Mapping> allMappings = inputType switch
        {
            InputType.Axis => AxisMappings.Cast<Mapping>().Concat(ButtonToAxisMappings),
            InputType.Button => ButtonMappings.Cast<Mapping>().Concat(AxisToButtonMappings),
            InputType.Hat => HatMappings,
            _ => Enumerable.Empty<Mapping>()
        };

        foreach (var mapping in allMappings)
        {
            if (!mapping.Enabled) continue;

            foreach (var input in mapping.Inputs)
            {
                if (input.Type == inputType &&
                    input.Index == inputIndex &&
                    (input.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase) ||
                     (!string.IsNullOrEmpty(input.DeviceId) && deviceId.Contains(input.DeviceId))))
                {
                    // Found a mapping - return the output if it's a vJoy output
                    if (mapping.Output.Type == OutputType.VJoyAxis ||
                        mapping.Output.Type == OutputType.VJoyButton ||
                        mapping.Output.Type == OutputType.VJoyPov)
                    {
                        return mapping.Output;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Format a vJoy output as an SC binding string (e.g., "js1_button5", "js2_x")
    /// </summary>
    /// <param name="output">The vJoy output target</param>
    /// <param name="scInstanceId">The SC joystick instance (1-8) that this vJoy device maps to</param>
    /// <returns>SC-formatted input string</returns>
    public static string FormatAsSCBinding(OutputTarget output, int scInstanceId)
    {
        var prefix = $"js{scInstanceId}";

        return output.Type switch
        {
            OutputType.VJoyButton => $"{prefix}_button{output.Index + 1}", // SC uses 1-based
            OutputType.VJoyAxis => $"{prefix}_{GetSCAxisName(output.Index)}",
            OutputType.VJoyPov => $"{prefix}_hat{output.Index + 1}_up", // Simplified - would need direction
            _ => ""
        };
    }

    /// <summary>
    /// Convert vJoy axis index to SC axis name
    /// </summary>
    private static string GetSCAxisName(int axisIndex)
    {
        // vJoy axis indices: 0=X, 1=Y, 2=Z, 3=RX, 4=RY, 5=RZ, 6=Slider0, 7=Slider1
        return axisIndex switch
        {
            0 => "x",
            1 => "y",
            2 => "z",
            3 => "rotx",
            4 => "roty",
            5 => "rotz",
            6 => "slider1",
            7 => "slider2",
            _ => $"axis{axisIndex}"
        };
    }
}
