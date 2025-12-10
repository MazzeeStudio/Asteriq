namespace Asteriq.Models;

/// <summary>
/// Represents an action definition from Star Citizen's defaultProfile.xml
/// </summary>
public class SCAction
{
    /// <summary>
    /// The action map this action belongs to (e.g., "spaceship_movement")
    /// </summary>
    public string ActionMap { get; set; } = string.Empty;

    /// <summary>
    /// The action name (e.g., "v_strafe_forward")
    /// </summary>
    public string ActionName { get; set; } = string.Empty;

    /// <summary>
    /// Category for UI grouping (e.g., "Flight - Movement")
    /// Derived from actionmap name
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// The type of input this action expects
    /// </summary>
    public SCInputType InputType { get; set; } = SCInputType.Button;

    /// <summary>
    /// Default bindings from SC's defaultProfile.xml
    /// </summary>
    public List<SCDefaultBinding> DefaultBindings { get; set; } = new();

    /// <summary>
    /// Unique key for this action (ActionMap + ActionName)
    /// </summary>
    public string Key => $"{ActionMap}.{ActionName}";

    public override string ToString() => $"{ActionMap}/{ActionName}";
}

/// <summary>
/// Input type for SC actions
/// </summary>
public enum SCInputType
{
    Button,
    Axis,
    Hat
}

/// <summary>
/// A default binding from SC's defaultProfile.xml
/// </summary>
public class SCDefaultBinding
{
    /// <summary>
    /// Device type prefix (kb1, mo1, js1, js2, etc.)
    /// </summary>
    public string DevicePrefix { get; set; } = string.Empty;

    /// <summary>
    /// The input name (e.g., "w", "button1", "x", "hat1_up")
    /// </summary>
    public string Input { get; set; } = string.Empty;

    /// <summary>
    /// Full input string (e.g., "js1_button1", "kb1_w")
    /// </summary>
    public string FullInput => $"{DevicePrefix}_{Input}";

    /// <summary>
    /// Whether this input is inverted (for axes)
    /// </summary>
    public bool Inverted { get; set; }

    /// <summary>
    /// Activation mode for this binding
    /// </summary>
    public SCActivationMode ActivationMode { get; set; } = SCActivationMode.Press;

    /// <summary>
    /// Modifier keys required (e.g., "lshift", "lctrl")
    /// </summary>
    public List<string> Modifiers { get; set; } = new();

    public override string ToString() => FullInput;
}

/// <summary>
/// Activation modes supported by Star Citizen
/// </summary>
public enum SCActivationMode
{
    /// <summary>
    /// Standard press/release
    /// </summary>
    Press,

    /// <summary>
    /// Hold for a duration
    /// </summary>
    Hold,

    /// <summary>
    /// Double-tap the input
    /// </summary>
    DoubleTap,

    /// <summary>
    /// Triple-tap the input
    /// </summary>
    TripleTap,

    /// <summary>
    /// Delayed/long press
    /// </summary>
    DelayedPress
}

/// <summary>
/// Extension methods for SCActivationMode
/// </summary>
public static class SCActivationModeExtensions
{
    /// <summary>
    /// Converts activation mode to SC XML attribute value
    /// Returns null for Press (default, no attribute needed)
    /// </summary>
    public static string? ToXmlString(this SCActivationMode mode)
    {
        return mode switch
        {
            SCActivationMode.DoubleTap => "double_tap",
            SCActivationMode.TripleTap => "triple_tap",
            SCActivationMode.Hold => "hold",
            SCActivationMode.DelayedPress => "delayed_press",
            _ => null
        };
    }

    /// <summary>
    /// Parses SC XML activationMode attribute to enum
    /// </summary>
    public static SCActivationMode ParseFromXml(string? xmlValue)
    {
        if (string.IsNullOrEmpty(xmlValue))
            return SCActivationMode.Press;

        var lower = xmlValue.ToLower();

        if (lower == "double_tap" || lower == "doubletap")
            return SCActivationMode.DoubleTap;

        if (lower == "triple_tap" || lower == "tripletap")
            return SCActivationMode.TripleTap;

        if (lower == "hold" || lower == "delayed_hold" || lower.Contains("hold"))
            return SCActivationMode.Hold;

        if (lower.StartsWith("delayed_press") || lower == "long_press" || lower == "longpress")
            return SCActivationMode.DelayedPress;

        return SCActivationMode.Press;
    }
}
