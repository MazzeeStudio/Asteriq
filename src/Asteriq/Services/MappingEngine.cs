using Asteriq.Models;
using Asteriq.Services.Abstractions;
using Asteriq.VJoy;

namespace Asteriq.Services;

/// <summary>
/// Processes input mappings and outputs to vJoy/keyboard
/// </summary>
public class MappingEngine : IMappingEngine
{
    private readonly IVJoyService _vjoy;
    private readonly KeyboardService _keyboard;
    private readonly bool _ownsKeyboard;
    private readonly Dictionary<string, float[]> _deviceAxisValues = new();
    private readonly Dictionary<string, bool[]> _deviceButtonValues = new();
    private readonly Dictionary<string, int[]> _deviceHatValues = new();
    private readonly Dictionary<Guid, float> _mergeAxisCache = new();
    private readonly object _lock = new();

    private MappingProfile? _activeProfile;
    private bool _isRunning;

    public MappingEngine(IVJoyService vjoy) : this(vjoy, new KeyboardService())
    {
        _ownsKeyboard = true;
    }

    public MappingEngine(IVJoyService vjoy, KeyboardService keyboard)
    {
        _vjoy = vjoy;
        _keyboard = keyboard;
        _ownsKeyboard = false;
    }

    /// <summary>
    /// Currently active profile
    /// </summary>
    public MappingProfile? ActiveProfile => _activeProfile;

    /// <summary>
    /// Whether the engine is processing mappings
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Load and activate a mapping profile
    /// </summary>
    public void LoadProfile(MappingProfile profile)
    {
        lock (_lock)
        {
            _activeProfile = profile;
            _mergeAxisCache.Clear();

            // Initialize merge cache for axis mappings with multiple inputs
            foreach (var mapping in profile.AxisMappings.Where(m => m.Inputs.Count > 1))
            {
                _mergeAxisCache[mapping.Id] = 0f;
            }
        }
    }

    /// <summary>
    /// Start processing mappings
    /// </summary>
    /// <param name="initialStates">Optional initial device states for synchronization.
    /// If provided, vJoy outputs will be set to match current hardware positions.</param>
    public bool Start(IEnumerable<DeviceInputState>? initialStates = null)
    {
        if (_activeProfile is null)
            return false;

        // Acquire required vJoy devices
        var requiredDevices = GetRequiredVJoyDevices();
        foreach (var deviceId in requiredDevices)
        {
            if (!_vjoy.AcquireDevice(deviceId))
            {
                System.Diagnostics.Debug.WriteLine($"Failed to acquire vJoy device {deviceId}");
                return false;
            }
        }

        _isRunning = true;

        // Synchronize vJoy outputs with current hardware state
        if (initialStates is not null)
        {
            SynchronizeInitialState(initialStates);
        }

        return true;
    }

    /// <summary>
    /// Synchronize vJoy outputs with current hardware state.
    /// This prevents axes from jumping when forwarding starts.
    /// </summary>
    private void SynchronizeInitialState(IEnumerable<DeviceInputState> deviceStates)
    {
        if (_activeProfile is null)
            return;

        foreach (var state in deviceStates)
        {
            // Cache device values
            _deviceAxisValues[state.DeviceName] = state.Axes;
            _deviceButtonValues[state.DeviceName] = state.Buttons;
            _deviceHatValues[state.DeviceName] = state.Hats;

            // Process all axis mappings for this device
            foreach (var mapping in _activeProfile.AxisMappings)
            {
                foreach (var input in mapping.Inputs)
                {
                    if (input.DeviceName == state.DeviceName && input.Index < state.Axes.Length)
                    {
                        float value = state.Axes[input.Index];

                        // Apply curve if present
                        if (mapping.Curve is not null)
                        {
                            value = mapping.Curve.Apply(value);
                        }

                        // Output to vJoy
                        var axis = IndexToHidUsage(mapping.Output.Index);
                        _vjoy.SetAxis(mapping.Output.VJoyDevice, axis, value);
                    }
                }
            }

            // Process all button mappings for this device
            foreach (var mapping in _activeProfile.ButtonMappings)
            {
                foreach (var input in mapping.Inputs)
                {
                    if (input.DeviceName == state.DeviceName && input.Index < state.Buttons.Length)
                    {
                        bool pressed = state.Buttons[input.Index];

                        // Handle inversion
                        if (mapping.Invert)
                            pressed = !pressed;

                        // Output to vJoy (button indices are 1-based in vJoy)
                        _vjoy.SetButton(mapping.Output.VJoyDevice, mapping.Output.Index + 1, pressed);
                    }
                }
            }

            // Process all hat mappings for this device
            foreach (var mapping in _activeProfile.HatMappings)
            {
                foreach (var input in mapping.Inputs)
                {
                    if (input.DeviceName == state.DeviceName && input.Index < state.Hats.Length)
                    {
                        int angle = state.Hats[input.Index];

                        if (mapping.UseContinuous)
                        {
                            _vjoy.SetContinuousPov(mapping.Output.VJoyDevice, (uint)mapping.Output.Index, angle);
                        }
                        else
                        {
                            int discrete = HatAngleToDiscrete(angle);
                            _vjoy.SetDiscretePov(mapping.Output.VJoyDevice, (uint)mapping.Output.Index, discrete);
                        }
                    }
                }
            }
        }

        System.Diagnostics.Debug.WriteLine($"Synchronized initial state for {deviceStates.Count()} devices");
    }

    /// <summary>
    /// Stop processing mappings
    /// </summary>
    public void Stop()
    {
        _isRunning = false;

        // Reset all vJoy devices
        var requiredDevices = GetRequiredVJoyDevices();
        foreach (var deviceId in requiredDevices)
        {
            _vjoy.ResetDevice(deviceId);
        }
    }

    /// <summary>
    /// Process input state and apply mappings
    /// </summary>
    public void ProcessInput(DeviceInputState state)
    {
        if (!_isRunning || _activeProfile is null)
            return;

        var deviceId = GetDeviceId(state);

        lock (_lock)
        {
            // Cache current values for this device
            _deviceAxisValues[deviceId] = state.Axes;
            _deviceButtonValues[deviceId] = state.Buttons;
            _deviceHatValues[deviceId] = state.Hats;

            // Update shift layer states
            UpdateShiftLayers(deviceId, state);

            // Process axis mappings (filtered by active layer)
            foreach (var mapping in _activeProfile.AxisMappings.Where(m => m.Enabled && IsMappingActive(m)))
            {
                ProcessAxisMapping(mapping, deviceId, state);
            }

            // Process button mappings (filtered by active layer)
            foreach (var mapping in _activeProfile.ButtonMappings.Where(m => m.Enabled && IsMappingActive(m)))
            {
                ProcessButtonMapping(mapping, deviceId, state);
            }

            // Process hat mappings (filtered by active layer)
            foreach (var mapping in _activeProfile.HatMappings.Where(m => m.Enabled && IsMappingActive(m)))
            {
                ProcessHatMapping(mapping, deviceId, state);
            }

            // Process axis-to-button mappings (filtered by active layer)
            foreach (var mapping in _activeProfile.AxisToButtonMappings.Where(m => m.Enabled && IsMappingActive(m)))
            {
                ProcessAxisToButtonMapping(mapping, deviceId, state);
            }

            // Process button-to-axis mappings (filtered by active layer)
            foreach (var mapping in _activeProfile.ButtonToAxisMappings.Where(m => m.Enabled && IsMappingActive(m)))
            {
                ProcessButtonToAxisMapping(mapping, deviceId, state);
            }
        }
    }

    /// <summary>
    /// Update shift layer active states based on button inputs
    /// </summary>
    private void UpdateShiftLayers(string deviceId, DeviceInputState state)
    {
        if (_activeProfile is null)
            return;

        foreach (var layer in _activeProfile.ShiftLayers)
        {
            if (layer.ActivatorButton is null)
                continue;

            // Check if activator button is on this device
            if (layer.ActivatorButton.DeviceId != deviceId)
                continue;

            // Get button state
            bool isPressed = false;
            if (layer.ActivatorButton.Type == InputType.Button &&
                layer.ActivatorButton.Index < state.Buttons.Length)
            {
                isPressed = state.Buttons[layer.ActivatorButton.Index];
            }

            layer.IsActive = isPressed;
        }
    }

    /// <summary>
    /// Check if a mapping should be processed based on its layer
    /// </summary>
    private bool IsMappingActive(Mapping mapping)
    {
        if (_activeProfile is null)
            return false;

        // No layer = base layer, always active when no shift is pressed
        if (mapping.LayerId is null)
        {
            // Base layer is active when NO shift layers are active
            return !_activeProfile.ShiftLayers.Any(l => l.IsActive);
        }

        // Check if this mapping's layer is active
        var layer = _activeProfile.ShiftLayers.FirstOrDefault(l => l.Id == mapping.LayerId);
        return layer?.IsActive ?? false;
    }

    private void ProcessAxisMapping(AxisMapping mapping, string deviceId, DeviceInputState state)
    {
        // Check if this input is relevant to this mapping
        var relevantInputs = mapping.Inputs.Where(i =>
            i.DeviceId == deviceId && i.Type == InputType.Axis).ToList();

        if (relevantInputs.Count == 0)
            return;

        float outputValue;

        if (mapping.Inputs.Count == 1)
        {
            // Single input - direct mapping
            var input = mapping.Inputs[0];
            if (input.Index >= state.Axes.Length)
                return;

            outputValue = state.Axes[input.Index];
        }
        else
        {
            // Multiple inputs - merge operation
            var values = new List<float>();

            foreach (var input in mapping.Inputs)
            {
                if (_deviceAxisValues.TryGetValue(input.DeviceId, out var axes) &&
                    input.Index < axes.Length)
                {
                    values.Add(axes[input.Index]);
                }
            }

            if (values.Count == 0)
                return;

            outputValue = ApplyMerge(values, mapping.MergeOp);
        }

        // Apply curve
        outputValue = mapping.Curve.Apply(outputValue);

        // Apply inversion
        if (mapping.Invert)
            outputValue = -outputValue;

        // Output to vJoy
        if (mapping.Output.Type == OutputType.VJoyAxis)
        {
            var axis = IndexToHidUsage(mapping.Output.Index);
            _vjoy.SetAxis(mapping.Output.VJoyDevice, axis, outputValue);
        }
    }

    private void ProcessButtonMapping(ButtonMapping mapping, string deviceId, DeviceInputState state)
    {
        // Check if this input is relevant
        var relevantInputs = mapping.Inputs.Where(i =>
            i.DeviceId == deviceId && i.Type == InputType.Button).ToList();

        if (relevantInputs.Count == 0)
            return;

        // Get combined input state
        bool inputPressed = false;

        if (mapping.Inputs.Count == 1)
        {
            var input = mapping.Inputs[0];
            if (input.Index < state.Buttons.Length)
                inputPressed = state.Buttons[input.Index];
        }
        else
        {
            // Multiple inputs - any pressed = pressed
            foreach (var input in mapping.Inputs)
            {
                if (_deviceButtonValues.TryGetValue(input.DeviceId, out var buttons) &&
                    input.Index < buttons.Length && buttons[input.Index])
                {
                    inputPressed = true;
                    break;
                }
            }
        }

        // Apply button mode
        bool outputPressed = ApplyButtonMode(mapping, inputPressed);

        // Apply inversion
        if (mapping.Invert)
            outputPressed = !outputPressed;

        // Output
        if (mapping.Output.Type == OutputType.VJoyButton)
        {
            _vjoy.SetButton(mapping.Output.VJoyDevice, mapping.Output.Index + 1, outputPressed); // vJoy buttons are 1-indexed
        }
        else if (mapping.Output.Type == OutputType.Keyboard)
        {
            _keyboard.SetKey(mapping.Output.KeyName, outputPressed, mapping.Output.Modifiers);
        }
    }

    private void ProcessHatMapping(HatMapping mapping, string deviceId, DeviceInputState state)
    {
        // Check if this input is relevant
        var relevantInputs = mapping.Inputs.Where(i =>
            i.DeviceId == deviceId && i.Type == InputType.Hat).ToList();

        if (relevantInputs.Count == 0)
            return;

        // Get hat value (SDL reports -1 for centered, or angle in degrees 0-360)
        int hatValue = -1;

        if (mapping.Inputs.Count == 1)
        {
            var input = mapping.Inputs[0];
            if (input.Index < state.Hats.Length)
                hatValue = state.Hats[input.Index];
        }
        else
        {
            // Multiple hat inputs - take first non-centered value
            foreach (var input in mapping.Inputs)
            {
                if (_deviceHatValues.TryGetValue(input.DeviceId, out var hats) &&
                    input.Index < hats.Length && hats[input.Index] >= 0)
                {
                    hatValue = hats[input.Index];
                    break;
                }
            }
        }

        // Output to vJoy POV
        if (mapping.Output.Type == OutputType.VJoyPov)
        {
            uint povIndex = (uint)(mapping.Output.Index + 1); // vJoy POV is 1-indexed

            if (mapping.UseContinuous)
            {
                // Continuous POV - pass angle directly
                _vjoy.SetContinuousPov(mapping.Output.VJoyDevice, povIndex, hatValue);
            }
            else
            {
                // Discrete POV - convert angle to direction (0-3) or -1 for neutral
                int direction = HatAngleToDiscrete(hatValue);
                _vjoy.SetDiscretePov(mapping.Output.VJoyDevice, povIndex, direction);
            }
        }
    }

    /// <summary>
    /// Convert hat angle to discrete direction (0=N, 1=E, 2=S, 3=W, -1=neutral)
    /// </summary>
    private static int HatAngleToDiscrete(int angle)
    {
        if (angle < 0)
            return -1; // Neutral

        // Normalize to 0-359
        angle = angle % 360;

        // Map to 4 directions with 90-degree zones
        // N: 315-360, 0-44 (centered on 0)
        // E: 45-134 (centered on 90)
        // S: 135-224 (centered on 180)
        // W: 225-314 (centered on 270)
        if (angle >= 315 || angle < 45)
            return 0; // North
        if (angle >= 45 && angle < 135)
            return 1; // East
        if (angle >= 135 && angle < 225)
            return 2; // South
        return 3; // West
    }

    private void ProcessAxisToButtonMapping(AxisToButtonMapping mapping, string deviceId, DeviceInputState state)
    {
        // Check if this input is relevant
        var relevantInputs = mapping.Inputs.Where(i =>
            i.DeviceId == deviceId && i.Type == InputType.Axis).ToList();

        if (relevantInputs.Count == 0)
            return;

        // Get axis value
        float axisValue = 0f;

        if (mapping.Inputs.Count == 1)
        {
            var input = mapping.Inputs[0];
            if (input.Index < state.Axes.Length)
                axisValue = state.Axes[input.Index];
        }
        else
        {
            // Multiple inputs - use first available
            foreach (var input in mapping.Inputs)
            {
                if (_deviceAxisValues.TryGetValue(input.DeviceId, out var axes) &&
                    input.Index < axes.Length)
                {
                    axisValue = axes[input.Index];
                    break;
                }
            }
        }

        // Apply hysteresis to prevent flickering
        bool shouldActivate;
        if (mapping.ActivateAbove)
        {
            // Activate when above threshold
            float activateThreshold = mapping.IsActivated ? mapping.Threshold - mapping.Hysteresis : mapping.Threshold;
            shouldActivate = axisValue > activateThreshold;
        }
        else
        {
            // Activate when below threshold
            float activateThreshold = mapping.IsActivated ? mapping.Threshold + mapping.Hysteresis : mapping.Threshold;
            shouldActivate = axisValue < activateThreshold;
        }

        mapping.IsActivated = shouldActivate;

        // Apply inversion
        bool outputPressed = mapping.Invert ? !shouldActivate : shouldActivate;

        // Output
        if (mapping.Output.Type == OutputType.VJoyButton)
        {
            _vjoy.SetButton(mapping.Output.VJoyDevice, mapping.Output.Index + 1, outputPressed); // vJoy buttons are 1-indexed
        }
        else if (mapping.Output.Type == OutputType.Keyboard)
        {
            _keyboard.SetKey(mapping.Output.KeyName, outputPressed, mapping.Output.Modifiers);
        }
    }

    private void ProcessButtonToAxisMapping(ButtonToAxisMapping mapping, string deviceId, DeviceInputState state)
    {
        // Check if this input is relevant
        var relevantInputs = mapping.Inputs.Where(i =>
            i.DeviceId == deviceId && i.Type == InputType.Button).ToList();

        if (relevantInputs.Count == 0)
            return;

        // Get button state
        bool isPressed = false;

        if (mapping.Inputs.Count == 1)
        {
            var input = mapping.Inputs[0];
            if (input.Index < state.Buttons.Length)
                isPressed = state.Buttons[input.Index];
        }
        else
        {
            // Multiple inputs - any pressed = pressed
            foreach (var input in mapping.Inputs)
            {
                if (_deviceButtonValues.TryGetValue(input.DeviceId, out var buttons) &&
                    input.Index < buttons.Length && buttons[input.Index])
                {
                    isPressed = true;
                    break;
                }
            }
        }

        // Determine target value
        float targetValue = isPressed ? mapping.PressedValue : mapping.ReleasedValue;

        // Apply smoothing if configured
        float outputValue;
        if (mapping.SmoothingMs > 0 && mapping.LastUpdate.HasValue)
        {
            var elapsed = (DateTime.UtcNow - mapping.LastUpdate.Value).TotalMilliseconds;
            var rate = elapsed / mapping.SmoothingMs;
            var delta = targetValue - mapping.CurrentValue;
            outputValue = mapping.CurrentValue + (float)Math.Min(rate, 1.0) * delta;
        }
        else
        {
            outputValue = targetValue;
        }

        mapping.CurrentValue = outputValue;
        mapping.LastUpdate = DateTime.UtcNow;

        // Apply inversion
        if (mapping.Invert)
            outputValue = -outputValue;

        // Output to vJoy
        if (mapping.Output.Type == OutputType.VJoyAxis)
        {
            var axis = IndexToHidUsage(mapping.Output.Index);
            _vjoy.SetAxis(mapping.Output.VJoyDevice, axis, outputValue);
        }
    }

    private bool ApplyButtonMode(ButtonMapping mapping, bool inputPressed)
    {
        switch (mapping.Mode)
        {
            case ButtonMode.Normal:
                return inputPressed;

            case ButtonMode.Toggle:
                if (inputPressed && mapping.HoldStartTime is null)
                {
                    // Rising edge - toggle
                    mapping.ToggleState = !mapping.ToggleState;
                    mapping.HoldStartTime = DateTime.UtcNow; // Prevent re-toggle
                }
                else if (!inputPressed)
                {
                    mapping.HoldStartTime = null; // Reset on release
                }
                return mapping.ToggleState;

            case ButtonMode.Pulse:
                if (inputPressed && mapping.HoldStartTime is null)
                {
                    // Rising edge - start pulse
                    mapping.HoldStartTime = DateTime.UtcNow;
                }

                if (mapping.HoldStartTime is not null)
                {
                    var elapsed = (DateTime.UtcNow - mapping.HoldStartTime.Value).TotalMilliseconds;
                    if (elapsed < mapping.PulseDurationMs)
                        return true;

                    if (!inputPressed)
                        mapping.HoldStartTime = null;
                }
                return false;

            case ButtonMode.HoldToActivate:
                if (inputPressed)
                {
                    if (mapping.HoldStartTime is null)
                        mapping.HoldStartTime = DateTime.UtcNow;

                    var elapsed = (DateTime.UtcNow - mapping.HoldStartTime.Value).TotalMilliseconds;
                    return elapsed >= mapping.HoldDurationMs;
                }
                else
                {
                    mapping.HoldStartTime = null;
                    return false;
                }

            default:
                return inputPressed;
        }
    }

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

    private static string GetDeviceId(DeviceInputState state)
    {
        // Use GUID for now, could extract VID:PID
        return state.InstanceGuid.ToString();
    }

    private HashSet<uint> GetRequiredVJoyDevices()
    {
        var devices = new HashSet<uint>();

        if (_activeProfile is null)
            return devices;

        foreach (var mapping in _activeProfile.AxisMappings)
        {
            if (mapping.Output.Type == OutputType.VJoyAxis ||
                mapping.Output.Type == OutputType.VJoyButton ||
                mapping.Output.Type == OutputType.VJoyPov)
            {
                devices.Add(mapping.Output.VJoyDevice);
            }
        }

        foreach (var mapping in _activeProfile.ButtonMappings)
        {
            if (mapping.Output.Type == OutputType.VJoyAxis ||
                mapping.Output.Type == OutputType.VJoyButton ||
                mapping.Output.Type == OutputType.VJoyPov)
            {
                devices.Add(mapping.Output.VJoyDevice);
            }
        }

        foreach (var mapping in _activeProfile.HatMappings)
        {
            if (mapping.Output.Type == OutputType.VJoyPov)
            {
                devices.Add(mapping.Output.VJoyDevice);
            }
        }

        foreach (var mapping in _activeProfile.AxisToButtonMappings)
        {
            if (mapping.Output.Type == OutputType.VJoyButton)
            {
                devices.Add(mapping.Output.VJoyDevice);
            }
        }

        foreach (var mapping in _activeProfile.ButtonToAxisMappings)
        {
            if (mapping.Output.Type == OutputType.VJoyAxis)
            {
                devices.Add(mapping.Output.VJoyDevice);
            }
        }

        return devices;
    }

    private static HID_USAGES IndexToHidUsage(int index)
    {
        return index switch
        {
            0 => HID_USAGES.X,
            1 => HID_USAGES.Y,
            2 => HID_USAGES.Z,
            3 => HID_USAGES.RX,
            4 => HID_USAGES.RY,
            5 => HID_USAGES.RZ,
            6 => HID_USAGES.SL0,
            7 => HID_USAGES.SL1,
            _ => HID_USAGES.X
        };
    }

    public void Dispose()
    {
        Stop();
        if (_ownsKeyboard)
            _keyboard.Dispose();
    }
}
