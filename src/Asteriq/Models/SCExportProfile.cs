namespace Asteriq.Models;

/// <summary>
/// Represents a Star Citizen export profile configuration.
/// Maps Asteriq's vJoy outputs and physical devices to SC joystick instances for export.
/// </summary>
public class SCExportProfile
{
    /// <summary>
    /// User-friendly name for the exported profile
    /// </summary>
    public string ProfileName { get; set; } = "";

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
    /// Maps physical device HidDevicePaths to SC joystick instance numbers.
    /// Persisted so column assignments survive across sessions.
    /// </summary>
    public Dictionary<string, int> PhysicalDeviceToSCInstance { get; set; } = new();

    /// <summary>
    /// Persists DirectInput GUIDs for physical devices so XML export can emit correct Product attributes.
    /// Key: HidDevicePath, Value: DirectInput instance GUID.
    /// </summary>
    public Dictionary<string, Guid> PhysicalDeviceDirectInputGuids { get; set; } = new();

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
    /// Gets the SC instance number for a physical device
    /// </summary>
    public int GetSCInstanceForPhysical(string hidDevicePath)
    {
        return PhysicalDeviceToSCInstance.TryGetValue(hidDevicePath, out var instance)
            ? instance
            : 0; // 0 = not yet assigned
    }

    /// <summary>
    /// Sets the SC instance for a physical device
    /// </summary>
    public void SetSCInstanceForPhysical(string hidDevicePath, int scInstance)
    {
        PhysicalDeviceToSCInstance[hidDevicePath] = scInstance;
        Modified = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the number of joystick instances to export.
    /// Includes both vJoy and physical device instances.
    /// </summary>
    public int JoystickCount
    {
        get
        {
            int maxVJoy = VJoyToSCInstance.Count > 0 ? VJoyToSCInstance.Values.Max() : 0;
            int maxPhysical = PhysicalDeviceToSCInstance.Count > 0 ? PhysicalDeviceToSCInstance.Values.Max() : 0;
            return Math.Max(maxVJoy, maxPhysical);
        }
    }

    /// <summary>
    /// Adds or updates a binding for a specific device type
    /// </summary>
    public void SetBinding(string actionMap, string actionName, SCActionBinding binding)
    {
        // Remove existing binding for this action AND device type
        // For joystick bindings, also consider the device to allow multiple joystick bindings
        // (e.g., js1_button5 and js2_button3 for the same action)
        if (binding.DeviceType == SCDeviceType.Joystick)
        {
            if (binding.PhysicalDeviceId is not null)
            {
                // Physical device binding: deduplicate by PhysicalDeviceId
                Bindings.RemoveAll(b => b.ActionMap == actionMap && b.ActionName == actionName &&
                    b.DeviceType == SCDeviceType.Joystick && b.PhysicalDeviceId == binding.PhysicalDeviceId);
            }
            else
            {
                // vJoy binding: deduplicate by VJoyDevice
                Bindings.RemoveAll(b => b.ActionMap == actionMap && b.ActionName == actionName &&
                    b.DeviceType == SCDeviceType.Joystick && b.PhysicalDeviceId is null && b.VJoyDevice == binding.VJoyDevice);
            }
        }
        else
        {
            Bindings.RemoveAll(b => b.ActionMap == actionMap && b.ActionName == actionName && b.DeviceType == binding.DeviceType);
        }
        binding.ActionMap = actionMap;
        binding.ActionName = actionName;
        Bindings.Add(binding);
        Modified = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets a joystick binding for an action (backwards compatible)
    /// </summary>
    public SCActionBinding? GetBinding(string actionMap, string actionName)
    {
        return Bindings.FirstOrDefault(b =>
            b.ActionMap == actionMap && b.ActionName == actionName && b.DeviceType == SCDeviceType.Joystick);
    }

    /// <summary>
    /// Gets a binding for an action with specific device type
    /// </summary>
    public SCActionBinding? GetBinding(string actionMap, string actionName, SCDeviceType deviceType)
    {
        return Bindings.FirstOrDefault(b =>
            b.ActionMap == actionMap && b.ActionName == actionName && b.DeviceType == deviceType);
    }

    /// <summary>
    /// Removes all bindings for an action (backwards compatible - removes all device types)
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
    /// Removes a binding for an action with specific device type
    /// </summary>
    public bool RemoveBinding(string actionMap, string actionName, SCDeviceType deviceType)
    {
        var removed = Bindings.RemoveAll(b =>
            b.ActionMap == actionMap && b.ActionName == actionName && b.DeviceType == deviceType);
        if (removed > 0)
            Modified = DateTime.UtcNow;
        return removed > 0;
    }

    /// <summary>
    /// Removes a specific binding object (for multi-column right-click clear)
    /// </summary>
    public bool RemoveBinding(SCActionBinding binding)
    {
        var removed = Bindings.Remove(binding);
        if (removed)
            Modified = DateTime.UtcNow;
        return removed;
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
    /// Gets all joystick bindings that would conflict with the given input on a vJoy device.
    /// Excludes the specified action to avoid reporting self-conflicts.
    /// </summary>
    public List<SCActionBinding> GetConflictingBindings(
        uint vjoyDevice,
        string inputName,
        string? excludeActionMap = null,
        string? excludeActionName = null)
    {
        return Bindings.Where(b =>
            b.DeviceType == SCDeviceType.Joystick &&
            b.PhysicalDeviceId is null &&
            b.VJoyDevice == vjoyDevice &&
            b.InputName.Equals(inputName, StringComparison.OrdinalIgnoreCase) &&
            !(b.ActionMap == excludeActionMap && b.ActionName == excludeActionName))
            .ToList();
    }

    /// <summary>
    /// Gets all joystick bindings that would conflict with the given input on a physical device.
    /// Excludes the specified action to avoid reporting self-conflicts.
    /// </summary>
    public List<SCActionBinding> GetConflictingBindings(
        string physicalDeviceId,
        string inputName,
        string? excludeActionMap = null,
        string? excludeActionName = null)
    {
        return Bindings.Where(b =>
            b.DeviceType == SCDeviceType.Joystick &&
            b.PhysicalDeviceId == physicalDeviceId &&
            b.InputName.Equals(inputName, StringComparison.OrdinalIgnoreCase) &&
            !(b.ActionMap == excludeActionMap && b.ActionName == excludeActionName))
            .ToList();
    }

    /// <summary>
    /// Generates the filename for export
    /// </summary>
    public string GetExportFileName()
    {
        var name = string.IsNullOrEmpty(ProfileName) ? "unnamed" : ProfileName;
        var sanitized = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        return $"layout_{sanitized}_exported.xml";
    }
}

/// <summary>
/// Device type for a binding
/// </summary>
public enum SCDeviceType
{
    Joystick,
    Keyboard,
    Mouse
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
    /// The device type (Joystick, Keyboard, Mouse)
    /// </summary>
    public SCDeviceType DeviceType { get; set; } = SCDeviceType.Joystick;

    /// <summary>
    /// The vJoy device ID (1-16) - only used for vJoy Joystick bindings (PhysicalDeviceId is null)
    /// </summary>
    public uint VJoyDevice { get; set; }

    /// <summary>
    /// HidDevicePath of the physical device this binding belongs to.
    /// Null for vJoy bindings, set for physical device bindings.
    /// </summary>
    public string? PhysicalDeviceId { get; set; }

    /// <summary>
    /// The input name (e.g., "button1", "x", "hat1_up" for joystick, "w", "space" for keyboard)
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
    /// Modifier keys (e.g., "lshift", "lctrl")
    /// </summary>
    public List<string> Modifiers { get; set; } = new();

    /// <summary>
    /// Gets the full SC input string (e.g., "js1_button5", "kb1_w", "mo1_mouse1")
    /// </summary>
    public string GetSCInputString(int scInstance = 1)
    {
        return DeviceType switch
        {
            SCDeviceType.Keyboard => $"kb1_{InputName}",
            SCDeviceType.Mouse => $"mo1_{InputName}",
            _ => $"js{scInstance}_{InputName}"
        };
    }

    /// <summary>
    /// Unique key for this binding. Includes device discriminator so bindings on different
    /// physical devices or vJoy slots don't collide.
    /// </summary>
    public string Key => PhysicalDeviceId is not null
        ? $"{ActionMap}.{ActionName}.{DeviceType}.phys:{PhysicalDeviceId}"
        : $"{ActionMap}.{ActionName}.{DeviceType}.vjoy:{VJoyDevice}";

    /// <summary>
    /// Simple key without device type (for backwards compatibility)
    /// </summary>
    public string ActionKey => $"{ActionMap}.{ActionName}";

    public override string ToString() => DeviceType switch
    {
        SCDeviceType.Keyboard => $"{ActionMap}/{ActionName} -> kb1_{InputName}",
        SCDeviceType.Mouse => $"{ActionMap}/{ActionName} -> mo1_{InputName}",
        _ when PhysicalDeviceId is not null => $"{ActionMap}/{ActionName} -> phys:{InputName}",
        _ => $"{ActionMap}/{ActionName} -> vJoy{VJoyDevice}_{InputName}"
    };
}
