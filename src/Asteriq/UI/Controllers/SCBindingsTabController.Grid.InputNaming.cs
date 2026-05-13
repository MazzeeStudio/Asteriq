using Asteriq.Models;
using Asteriq.Services;

namespace Asteriq.UI.Controllers;

public partial class SCBindingsTabController
{
    private string GetVJoyAxisNameFromMapping(PhysicalDeviceInfo device, int physicalAxisIndex, SCGridColumn col)
    {
        // Look up the mapping in the active profile
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is not null)
        {
            // Try to find a mapping for this physical input using GUID
            var deviceId = device.InstanceGuid.ToString();
            var output = profile.GetVJoyOutputForPhysicalInput(deviceId, InputType.Axis, physicalAxisIndex);

            if (output is not null && output.Type == OutputType.VJoyAxis)
            {
                // Found a mapping - return the vJoy output axis name
                string axisName = VJoyAxisIndexToSCName(output.Index);
                System.Diagnostics.Debug.WriteLine($"[SCBindings] Found mapping: {device.Name} axis {physicalAxisIndex} -> vJoy axis {output.Index} ({axisName})");
                return axisName;
            }
        }

        // No mapping found - fall back to sequential index-based naming
        // This assumes physical axis N maps to vJoy axis N
        System.Diagnostics.Debug.WriteLine($"[SCBindings] No mapping found for {device.Name} axis {physicalAxisIndex}, using index-based fallback");
        return VJoyAxisIndexToSCName(physicalAxisIndex);
    }

    private static string VJoyAxisIndexToSCName(int vjoyAxisIndex)
    {
        return vjoyAxisIndex switch
        {
            0 => "x",
            1 => "y",
            2 => "z",
            3 => "rx",
            4 => "ry",
            5 => "rz",
            6 => "slider1",
            7 => "slider2",
            _ => $"axis{vjoyAxisIndex}"
        };
    }

    private static string GetSCAxisNameFromDevice(int axisIndex, PhysicalDeviceInfo? device)
    {
        // Use HID axis type info if available (this properly detects sliders)
        if (device is not null && device.AxisInfos.Count > 0)
        {
            var axisInfo = device.AxisInfos.FirstOrDefault(a => a.Index == axisIndex);
            if (axisInfo is not null)
            {
                return axisInfo.Type switch
                {
                    AxisType.X => "x",
                    AxisType.Y => "y",
                    AxisType.Z => "z",
                    AxisType.RX => "rx",
                    AxisType.RY => "ry",
                    AxisType.RZ => "rz",
                    AxisType.Slider => GetSliderName(axisIndex, device),
                    _ => $"axis{axisIndex}"
                };
            }
        }

        // Fallback to index-based mapping if no HID info
        return axisIndex switch
        {
            0 => "x",
            1 => "y",
            2 => "z",
            3 => "rx",
            4 => "ry",
            5 => "rz",
            6 => "slider1",
            7 => "slider2",
            _ => $"axis{axisIndex}"
        };
    }

    private static string GetSliderName(int axisIndex, PhysicalDeviceInfo device)
    {
        // Count how many sliders come before this one
        int sliderNumber = 1;
        foreach (var axis in device.AxisInfos.OrderBy(a => a.Index))
        {
            if (axis.Index == axisIndex)
                break;
            if (axis.Type == AxisType.Slider)
                sliderNumber++;
        }
        return $"slider{sliderNumber}";
    }

    private static string GetHatDirection(int dir)
    {
        return dir switch
        {
            0 => "up",
            1 => "right",
            2 => "down",
            3 => "left",
            _ => "up"
        };
    }

    private static int HatAngleToDiscrete(int angle)
    {
        if (angle < 0) return -1; // Centered
        // Normalize to 0-359
        angle = ((angle % 360) + 360) % 360;
        // 315-44 = up, 45-134 = right, 135-224 = down, 225-314 = left
        if (angle >= 315 || angle < 45) return 0;   // Up
        if (angle >= 45 && angle < 135) return 1;   // Right
        if (angle >= 135 && angle < 225) return 2;  // Down
        return 3; // Left
    }

    private static int GetAxisIndexFromBinding(string binding)
    {
        return binding.ToLowerInvariant() switch
        {
            "x" => 0,
            "y" => 1,
            "z" => 2,
            "rx" => 3,
            "ry" => 4,
            "rz" => 5,
            "slider1" => 6,
            "slider2" => 7,
            _ when binding.StartsWith("axis") && int.TryParse(binding.AsSpan(4), out int idx) => idx,
            _ => -1
        };
    }

    private static SCInputType InferInputTypeFromName(string inputName)
    {
        if (string.IsNullOrEmpty(inputName))
            return SCInputType.Button;

        var lower = inputName.ToLowerInvariant();

        // Button inputs (check first since it's most common)
        if (lower.StartsWith("button"))
            return SCInputType.Button;

        // Hat/POV inputs
        if (lower.StartsWith("hat"))
            return SCInputType.Hat;

        // Known axis names (matches SCVirtStick exactly)
        if (lower is "x" or "y" or "z" or "rx" or "ry" or "rz" or
            "slider1" or "slider2" or "throttle" or "rotz" or "rotx" or "roty")
            return SCInputType.Axis;

        // Slider axes (any slider*)
        if (lower.StartsWith("slider"))
            return SCInputType.Axis;

        // Fallback axis names (axis0, axis1, etc.)
        if (lower.StartsWith("axis"))
            return SCInputType.Axis;

        // Default to button (same as SCVirtStick)
        return SCInputType.Button;
    }

    private static string KeyToSCInput(Keys key)
    {
        return key switch
        {
            >= Keys.A and <= Keys.Z => key.ToString().ToLower(),
            >= Keys.D0 and <= Keys.D9 => ((int)key - (int)Keys.D0).ToString(),
            >= Keys.NumPad0 and <= Keys.NumPad9 => $"np_{(int)key - (int)Keys.NumPad0}",
            >= Keys.F1 and <= Keys.F12 => key.ToString().ToLower(),
            Keys.Space => "space",
            Keys.Enter => "enter",
            Keys.Escape => "escape",
            Keys.Back => "backspace",
            Keys.Tab => "tab",
            Keys.Delete => "delete",
            Keys.Insert => "insert",
            Keys.Home => "home",
            Keys.End => "end",
            Keys.PageUp => "pgup",
            Keys.PageDown => "pgdn",
            Keys.Left => "left",
            Keys.Right => "right",
            Keys.Up => "up",
            Keys.Down => "down",
            Keys.OemMinus => "minus",
            Keys.Oemplus => "equals",
            Keys.OemOpenBrackets => "lbracket",
            Keys.OemCloseBrackets => "rbracket",
            Keys.OemBackslash or Keys.Oem5 => "backslash",
            Keys.OemSemicolon or Keys.Oem1 => "semicolon",
            Keys.OemQuotes or Keys.Oem7 => "apostrophe",
            Keys.Oemcomma => "comma",
            Keys.OemPeriod => "period",
            Keys.OemQuestion or Keys.Oem2 => "slash",
            Keys.Oemtilde or Keys.Oem3 => "grave",
            Keys.LShiftKey => "lshift",
            Keys.RShiftKey => "rshift",
            Keys.LControlKey => "lctrl",
            Keys.RControlKey => "rctrl",
            Keys.LMenu => "lalt",
            Keys.RMenu => "ralt",
            _ => key.ToString().ToLower()
        };
    }

    // Maps SC modifier key names (as stored in OutputTarget.KeyName) to Windows VK codes.
    private static readonly Dictionary<string, int> s_modifierNameToVK = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rctrl"]  = VK_RCONTROL,
        ["lctrl"]  = VK_LCONTROL,
        ["rshift"] = VK_RSHIFT,
        ["lshift"] = VK_LSHIFT,
        ["ralt"]   = VK_RMENU,
        ["lalt"]   = VK_LMENU,
    };

    /// <summary>
    /// Scans the active Mappings profile for keyboard-output buttons whose key name is a modifier
    /// (rctrl, lctrl, rshift, lshift, ralt, lalt) and populates <see cref="_scModifierKeys"/> so
    /// that joystick listen mode can detect compound modifier+button inputs.
    /// </summary>
    private void UpdateModifierKeys()
    {
        _scModifierKeys.Clear();
        _scModifierPhysicalButtons.Clear();
        _scModifierButtonToModifiers.Clear();

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        foreach (var mapping in profile.ButtonMappings)
        {
            if (mapping.Output.Type != OutputType.Keyboard) continue;
            var keyName = mapping.Output.KeyName;
            if (keyName is null) continue;
            if (!s_modifierNameToVK.TryGetValue(keyName, out int vkCode)) continue;

            string modName = keyName.ToLowerInvariant();
            _scModifierKeys.TryAdd(vkCode, modName);

            // Record which (device, button) pairs trigger this modifier key so that
            // button31 on the LEFT stick is treated as a modifier only for that device â€”
            // the same button index on a DIFFERENT device is not affected.
            foreach (var input in mapping.Inputs)
            {
                if (input.Type != InputType.Button) continue;
                if (!Guid.TryParse(input.DeviceId, out var deviceGuid)) continue;
                var key = (deviceGuid, input.Index); // 0-based index
                _scModifierPhysicalButtons.Add(key);
                if (!_scModifierButtonToModifiers.TryGetValue(key, out var mods))
                {
                    mods = new List<string>();
                    _scModifierButtonToModifiers[key] = mods;
                }
                if (!mods.Contains(modName))
                    mods.Add(modName);
            }
        }

        System.Diagnostics.Debug.WriteLine(
            $"[SCBindings] UpdateModifierKeys: {_scModifierKeys.Count} modifier(s), " +
            $"{_scModifierPhysicalButtons.Count} modifier (device,button) pair(s)");
    }

    /// <summary>
    /// Rebuilds the conflict-links list for the currently selected cell.
    /// Called whenever the selected cell changes.
    /// </summary>
}