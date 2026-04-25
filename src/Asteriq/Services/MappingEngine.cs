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

    // Per-mapping state for "Last" merge modes (LastSnap / LastTakeover).
    // Tracks which input currently owns the output and the last-seen value per
    // input so crossings can be detected on the next tick.
    private sealed class LastMergeState
    {
        public int OwningInputIndex;
        public float OwnershipValue;
        public float[]? PreviousValues;
    }
    private readonly Dictionary<Guid, LastMergeState> _lastMergeStates = new();
    private const float LastMergeMoveDeadband = 0.005f;

    // Tracks which mappings currently assert "pressed" for each vJoy button.
    // When multiple mappings target the same button (e.g. shared bindings),
    // the output is true if ANY mapping asserts pressed — even across separate
    // device polling cycles. This prevents two mappings from fighting.
    private readonly Dictionary<(uint vjoyDevice, int buttonIndex), HashSet<Guid>> _buttonPressedBy = new();
    private readonly HashSet<(uint vjoyDevice, int buttonIndex)> _dirtyButtonOutputs = new();
    private readonly Dictionary<(uint vjoyDevice, int buttonIndex), bool> _lastButtonState = new();

    // Same OR-dedup as _buttonPressedBy but for keyboard output. Without it, two
    // mappings sharing one keyboard key (e.g. two physical buttons both outputting
    // LCtrl as a modifier) would race: when one releases, it would send key-up
    // even though the other is still held.
    private readonly Dictionary<int, HashSet<Guid>> _keyPressedBy = new();
    private readonly Dictionary<int, bool> _lastKeyOutputState = new();
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
            _lastMergeStates.Clear();
            _buttonPressedBy.Clear();
            _dirtyButtonOutputs.Clear();
            _lastButtonState.Clear();
            _keyPressedBy.Clear();
            _lastKeyOutputState.Clear();

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

        var stateList = deviceStates.ToList();
        foreach (var state in stateList)
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
                        if (mapping.Invert)
                            pressed = !pressed;
                        CollectButtonOutput(mapping.Output.VJoyDevice, mapping.Output.Index, pressed, mapping.Id);
                    }
                }
            }
            FlushButtonOutputs();

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

        System.Diagnostics.Debug.WriteLine($"Synchronized initial state for {stateList.Count} devices");
    }

    /// <summary>
    /// Stop processing mappings
    /// </summary>
    public void Stop()
    {
        _isRunning = false;

        // Release any keyboard keys that were held during forwarding
        _keyboard.ReleaseAll();

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

            // Process button mappings — outputs are tracked per-mapping persistently.
            // When multiple mappings target the same vJoy button (e.g. shared bindings),
            // the output is true if ANY mapping asserts pressed, even across device polls.
            foreach (var mapping in _activeProfile.ButtonMappings.Where(m => m.Enabled && IsMappingActive(m)))
            {
                ProcessButtonMapping(mapping, deviceId, state);
            }
            foreach (var mapping in _activeProfile.AxisToButtonMappings.Where(m => m.Enabled && IsMappingActive(m)))
            {
                ProcessAxisToButtonMapping(mapping, deviceId, state);
            }
            FlushButtonOutputs();

            // Process hat mappings (filtered by active layer)
            foreach (var mapping in _activeProfile.HatMappings.Where(m => m.Enabled && IsMappingActive(m)))
            {
                ProcessHatMapping(mapping, deviceId, state);
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

            outputValue = mapping.MergeOp is MergeOperation.LastSnap or MergeOperation.LastTakeover
                ? ApplyLastMerge(mapping, values)
                : ApplyMerge(values, mapping.MergeOp);
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

        // Output — vJoy buttons are collected and flushed with OR dedup
        if (mapping.Output.Type == OutputType.VJoyButton)
        {
            CollectButtonOutput(mapping.Output.VJoyDevice, mapping.Output.Index, outputPressed, mapping.Id);
        }
        else if (mapping.Output.Type == OutputType.Keyboard)
        {
            CollectKeyboardOutput(mapping.Output.KeyName, mapping.Output.Modifiers, outputPressed, mapping.Id);
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

        // Output — vJoy buttons are collected and flushed with OR dedup
        if (mapping.Output.Type == OutputType.VJoyButton)
        {
            CollectButtonOutput(mapping.Output.VJoyDevice, mapping.Output.Index, outputPressed, mapping.Id);
        }
        else if (mapping.Output.Type == OutputType.Keyboard)
        {
            CollectKeyboardOutput(mapping.Output.KeyName, mapping.Output.Modifiers, outputPressed, mapping.Id);
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

    private static bool ApplyButtonMode(ButtonMapping mapping, bool inputPressed)
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

    private void CollectButtonOutput(uint vjoyDevice, int buttonIndex, bool pressed, Guid mappingId)
    {
        var key = (vjoyDevice, buttonIndex);
        if (!_buttonPressedBy.TryGetValue(key, out var sources))
        {
            sources = new HashSet<Guid>();
            _buttonPressedBy[key] = sources;
        }

        if (pressed)
            sources.Add(mappingId);
        else
            sources.Remove(mappingId);

        _dirtyButtonOutputs.Add(key);
    }

    /// <summary>
    /// Routes keyboard output through per-VK OR-dedup so multiple mappings can drive
    /// the same key without racing — when one mapping releases the key, the OS only
    /// sees key-up if no other mapping is still asserting it. Preserves the existing
    /// modifier ordering: on press, modifiers down before main key; on release,
    /// main key up before modifiers (in reverse).
    /// </summary>
    private void CollectKeyboardOutput(string? keyName, List<string>? modifierNames, bool pressed, Guid mappingId)
    {
        if (string.IsNullOrEmpty(keyName)) return;
        var mainCode = KeyboardService.GetKeyCode(keyName);
        if (!mainCode.HasValue) return;

        int[]? modCodes = null;
        if (modifierNames is not null && modifierNames.Count > 0)
        {
            modCodes = modifierNames
                .Select(KeyboardService.GetKeyCode)
                .Where(c => c.HasValue)
                .Select(c => c!.Value)
                .ToArray();
        }

        if (pressed)
        {
            if (modCodes is not null)
                foreach (var mod in modCodes) ApplyKeyOR(mod, mappingId, want: true);
            ApplyKeyOR(mainCode.Value, mappingId, want: true);
        }
        else
        {
            ApplyKeyOR(mainCode.Value, mappingId, want: false);
            if (modCodes is not null)
                for (int i = modCodes.Length - 1; i >= 0; i--)
                    ApplyKeyOR(modCodes[i], mappingId, want: false);
        }
    }

    private void ApplyKeyOR(int vk, Guid mappingId, bool want)
    {
        if (!_keyPressedBy.TryGetValue(vk, out var sources))
        {
            sources = new HashSet<Guid>();
            _keyPressedBy[vk] = sources;
        }
        if (want) sources.Add(mappingId);
        else sources.Remove(mappingId);

        bool nowPressed = sources.Count > 0;
        if (!_lastKeyOutputState.TryGetValue(vk, out var lastState) || lastState != nowPressed)
        {
            _keyboard.SetKey(vk, nowPressed);
            _lastKeyOutputState[vk] = nowPressed;
        }
    }

    private void FlushButtonOutputs()
    {
        foreach (var key in _dirtyButtonOutputs)
        {
            bool anyPressed = _buttonPressedBy.TryGetValue(key, out var sources) && sources.Count > 0;

            // Only write to vJoy when the state actually changes — avoids sending
            // redundant HID reports that can reset hold-detection timers in games.
            if (!_lastButtonState.TryGetValue(key, out var lastState) || lastState != anyPressed)
            {
                _vjoy.SetButton(key.vjoyDevice, key.buttonIndex + 1, anyPressed); // vJoy buttons are 1-indexed
                _lastButtonState[key] = anyPressed;
            }
        }
        _dirtyButtonOutputs.Clear();
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

    /// <summary>
    /// "Last touched" merge. Transfers ownership to whichever input moved most
    /// recently. For LastTakeover, the new input must first cross the current
    /// output value (soft pickup) so the hand-off is jump-free. For LastSnap,
    /// any movement beyond the deadband claims ownership immediately.
    /// </summary>
    private float ApplyLastMerge(AxisMapping mapping, List<float> values)
    {
        if (!_lastMergeStates.TryGetValue(mapping.Id, out var st))
        {
            st = new LastMergeState();
            _lastMergeStates[mapping.Id] = st;
        }

        // Reset if input count changed (user added/removed an input)
        if (st.PreviousValues is null || st.PreviousValues.Length != values.Count)
        {
            st.PreviousValues = values.ToArray();
            st.OwningInputIndex = 0;
            st.OwnershipValue = values[0];
            return values[0];
        }

        bool isTakeover = mapping.MergeOp == MergeOperation.LastTakeover;

        for (int i = 0; i < values.Count; i++)
        {
            if (i == st.OwningInputIndex) continue;

            float prev = st.PreviousValues[i];
            float curr = values[i];
            float delta = curr - prev;

            if (Math.Abs(delta) <= LastMergeMoveDeadband)
                continue;

            if (isTakeover)
            {
                // Soft takeover: transfer only once the physical input crosses
                // (or touches) the current ownership value.
                bool crossed = (prev - st.OwnershipValue) * (curr - st.OwnershipValue) <= 0f;
                if (!crossed) continue;
            }

            st.OwningInputIndex = i;
            break;
        }

        float output = values[st.OwningInputIndex];
        st.OwnershipValue = output;

        for (int i = 0; i < values.Count; i++)
            st.PreviousValues[i] = values[i];

        return output;
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

    private static HID_USAGES IndexToHidUsage(int index) => VJoyAxisHelper.IndexToHidUsage(index);

    public void Dispose()
    {
        Stop();
        if (_ownsKeyboard)
            _keyboard.Dispose();
        GC.SuppressFinalize(this);
    }
}
