namespace Asteriq.Models;

/// <summary>
/// Represents a Star Citizen export profile configuration.
/// Maps Asteriq's vJoy outputs to SC joystick instances for export.
/// </summary>
public class SCExportProfile
{
    /// <summary>
    /// User-friendly name for the exported profile
    /// </summary>
    public string ProfileName { get; set; } = "asteriq";

    /// <summary>
    /// Target SC environment (LIVE, PTU, etc.)
    /// </summary>
    public string TargetEnvironment { get; set; } = "LIVE";

    /// <summary>
    /// BuildId of SC at time of profile creation
    /// Used for schema change detection
    /// </summary>
    public string? TargetBuildId { get; set; }

    /// <summary>
    /// When this profile was created
    /// </summary>
    public DateTime Created { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this profile was last modified
    /// </summary>
    public DateTime Modified { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Maps vJoy device IDs to SC joystick instance numbers.
    /// Key: vJoy device ID (1-16), Value: SC js instance (1, 2, 3...)
    /// e.g., { 1: 1, 2: 2 } means vJoy1=js1, vJoy2=js2
    /// </summary>
    public Dictionary<uint, int> VJoyToSCInstance { get; set; } = new();

    /// <summary>
    /// Custom bindings to export (action -> vJoy input)
    /// </summary>
    public List<SCActionBinding> Bindings { get; set; } = new();

    /// <summary>
    /// Whether to include default keyboard bindings in export
    /// </summary>
    public bool IncludeKeyboardDefaults { get; set; } = false;

    /// <summary>
    /// Whether to include default mouse bindings in export
    /// </summary>
    public bool IncludeMouseDefaults { get; set; } = false;

    /// <summary>
    /// Gets the SC instance number for a vJoy device
    /// </summary>
    public int GetSCInstance(uint vjoyDevice)
    {
        return VJoyToSCInstance.TryGetValue(vjoyDevice, out var instance)
            ? instance
            : (int)vjoyDevice; // Default: vJoy ID = SC instance
    }

    /// <summary>
    /// Sets the SC instance for a vJoy device
    /// </summary>
    public void SetSCInstance(uint vjoyDevice, int scInstance)
    {
        VJoyToSCInstance[vjoyDevice] = scInstance;
        Modified = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the number of joystick instances to export
    /// </summary>
    public int JoystickCount => VJoyToSCInstance.Count > 0
        ? VJoyToSCInstance.Values.Max()
        : 0;

    /// <summary>
    /// Adds or updates a binding
    /// </summary>
    public void SetBinding(string actionMap, string actionName, SCActionBinding binding)
    {
        // Remove existing binding for this action
        Bindings.RemoveAll(b => b.ActionMap == actionMap && b.ActionName == actionName);
        binding.ActionMap = actionMap;
        binding.ActionName = actionName;
        Bindings.Add(binding);
        Modified = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets a binding for an action
    /// </summary>
    public SCActionBinding? GetBinding(string actionMap, string actionName)
    {
        return Bindings.FirstOrDefault(b =>
            b.ActionMap == actionMap && b.ActionName == actionName);
    }

    /// <summary>
    /// Removes a binding
    /// </summary>
    public bool RemoveBinding(string actionMap, string actionName)
    {
        var removed = Bindings.RemoveAll(b =>
            b.ActionMap == actionMap && b.ActionName == actionName);
        if (removed > 0)
            Modified = DateTime.UtcNow;
        return removed > 0;
    }

    /// <summary>
    /// Clears all bindings
    /// </summary>
    public void ClearBindings()
    {
        Bindings.Clear();
        Modified = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets all bindings that would conflict with the given input (same device + input).
    /// Excludes the specified action to avoid reporting self-conflicts.
    /// </summary>
    public List<SCActionBinding> GetConflictingBindings(
        uint vjoyDevice,
        string inputName,
        string? excludeActionMap = null,
        string? excludeActionName = null)
    {
        return Bindings.Where(b =>
            b.VJoyDevice == vjoyDevice &&
            b.InputName.Equals(inputName, StringComparison.OrdinalIgnoreCase) &&
            !(b.ActionMap == excludeActionMap && b.ActionName == excludeActionName))
            .ToList();
    }

    /// <summary>
    /// Generates the filename for export
    /// </summary>
    public string GetExportFileName()
    {
        var sanitized = string.Join("_", ProfileName.Split(Path.GetInvalidFileNameChars()));
        return $"layout_{sanitized}_exported.xml";
    }
}

/// <summary>
/// A single action binding for export to SC
/// </summary>
public class SCActionBinding
{
    /// <summary>
    /// The action map (e.g., "spaceship_movement")
    /// </summary>
    public string ActionMap { get; set; } = string.Empty;

    /// <summary>
    /// The action name (e.g., "v_strafe_forward")
    /// </summary>
    public string ActionName { get; set; } = string.Empty;

    /// <summary>
    /// The vJoy device ID (1-16)
    /// </summary>
    public uint VJoyDevice { get; set; }

    /// <summary>
    /// The input name on the vJoy device (e.g., "button1", "x", "hat1_up")
    /// </summary>
    public string InputName { get; set; } = string.Empty;

    /// <summary>
    /// Input type (axis, button, hat)
    /// </summary>
    public SCInputType InputType { get; set; } = SCInputType.Button;

    /// <summary>
    /// Whether the input is inverted (for axes)
    /// </summary>
    public bool Inverted { get; set; }

    /// <summary>
    /// Activation mode for this binding
    /// </summary>
    public SCActivationMode ActivationMode { get; set; } = SCActivationMode.Press;

    /// <summary>
    /// Modifier keys (currently unused for vJoy, but kept for future)
    /// </summary>
    public List<string> Modifiers { get; set; } = new();

    /// <summary>
    /// Gets the full SC input string (e.g., "js1_button5", "js2_x")
    /// </summary>
    public string GetSCInputString(int scInstance)
    {
        return $"js{scInstance}_{InputName}";
    }

    /// <summary>
    /// Unique key for this binding
    /// </summary>
    public string Key => $"{ActionMap}.{ActionName}";

    public override string ToString() => $"{ActionMap}/{ActionName} -> vJoy{VJoyDevice}_{InputName}";
}
