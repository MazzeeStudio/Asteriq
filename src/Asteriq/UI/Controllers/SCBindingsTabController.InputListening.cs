using Asteriq.Models;
using Asteriq.Services;

namespace Asteriq.UI.Controllers;

public partial class SCBindingsTabController
{
    private void CheckSCBindingInput()
    {
        if (!_scListening.IsListening || _cell.ListeningColumn is null || _scFilteredActions is null)
            return;

        // Check for timeout
        if ((DateTime.Now - _scListening.StartTime).TotalMilliseconds > SCListeningTimeoutMs)
        {
            CancelSCInputListening();
            return;
        }

        // Check for Escape to cancel
        if (IsKeyHeld(0x1B)) // VK_ESCAPE
        {
            CancelSCInputListening();
            return;
        }

        var (actionIndex, colIndex) = _cell.SelectedCell;
        if (actionIndex < 0 || actionIndex >= _scFilteredActions.Count)
            return;

        var action = _scFilteredActions[actionIndex];
        var col = _cell.ListeningColumn;

        // Detect input based on column type
        if (col.IsKeyboard)
        {
            var detectedKey = DetectKeyboardInput();
            if (detectedKey is not null)
            {
                // Cancel BEFORE assigning — prevents timer from re-entering if assignment shows a blocking dialog
                var key = detectedKey.Value;
                CancelSCInputListening();
                AssignKeyboardBinding(action, key.key, key.modifiers);
            }
        }
        else if (col.IsMouse)
        {
            var detectedMouse = DetectMouseInput();
            if (detectedMouse is not null)
            {
                var mouse = detectedMouse;
                CancelSCInputListening();
                AssignMouseBinding(action, mouse);
            }
        }
        else if (col.IsJoystick)
        {
            // Modifier detection strategy: use the Mappings profile to know which physical
            // buttons are mapped to keyboard modifier keys (e.g. physical button 31 → RCTRL).
            // When one of those buttons is detected, skip it as a binding target and wait for
            // the REAL target button.  This avoids all timing races between SDL2 button state
            // and GetAsyncKeyState, which caused the old two-phase approach to be unreliable.

            var detectedJoystick = DetectJoystickInput(col);
            if (detectedJoystick is not null)
            {
                var (inputName, deviceGuid) = detectedJoystick.Value;
                var modKey = (deviceGuid, ParseButtonIndex(inputName));
                bool isModifier = _scListening.PendingModifiers is null
                    && modKey.Item2 >= 0
                    && _scModifierPhysicalButtons.Contains(modKey);

                if (isModifier)
                {
                    // This is the modifier button — record which modifier it produces and
                    // keep listening for the actual target button.
                    _scListening.PendingModifiers = _scModifierButtonToModifiers.TryGetValue(modKey, out var mods)
                        ? new List<string>(mods) : null;
                    _scListening.StartTime = DateTime.Now; // reset timeout so user has time to press target
                    _ctx.MarkDirty();
                }
                else
                {
                    // Regular button or target button — assign with any pending modifier.
                    // Cancel BEFORE assigning so a blocking conflict dialog cannot re-trigger detection.
                    var finalModifiers = _scListening.PendingModifiers;
                    CancelSCInputListening();
                    AssignJoystickBinding(action, col, inputName, finalModifiers);
                }
            }
        }
    }

    private void CancelSCInputListening()
    {
        _scListening.IsListening = false;
        _cell.ListeningColumn = null;
        _scListening.PendingModifiers = null;
        ResetJoystickDetectionState();
        System.Diagnostics.Debug.WriteLine("[SCBindings] Input listening cancelled");
    }

    private static (Keys key, List<string> modifiers)? DetectKeyboardInput()
    {
        // Collect held modifiers
        var modifiers = new List<string>();
        if (IsKeyHeld(0xA0) || IsKeyHeld(0xA1)) // VK_LSHIFT, VK_RSHIFT
        {
            modifiers.Add(IsKeyHeld(0xA1) ? "rshift" : "lshift");
        }
        if (IsKeyHeld(0xA2) || IsKeyHeld(0xA3)) // VK_LCONTROL, VK_RCONTROL
        {
            modifiers.Add(IsKeyHeld(0xA3) ? "rctrl" : "lctrl");
        }
        if (IsKeyHeld(0xA4) || IsKeyHeld(0xA5)) // VK_LMENU, VK_RMENU (Alt)
        {
            modifiers.Add(IsKeyHeld(0xA5) ? "ralt" : "lalt");
        }

        // Check for regular keys (A-Z)
        for (int vk = 0x41; vk <= 0x5A; vk++) // A-Z
        {
            if (IsKeyPressed(vk))
            {
                return ((Keys)vk, modifiers);
            }
        }

        // Check number keys (0-9)
        for (int vk = 0x30; vk <= 0x39; vk++)
        {
            if (IsKeyPressed(vk))
            {
                return ((Keys)vk, modifiers);
            }
        }

        // Check function keys (F1-F12)
        for (int vk = 0x70; vk <= 0x7B; vk++) // VK_F1 - VK_F12
        {
            if (IsKeyPressed(vk))
            {
                return ((Keys)vk, modifiers);
            }
        }

        // Check common keys
        int[] commonKeys = { 0x20, 0x0D, 0x08, 0x09, 0x2E, 0x2D, 0x24, 0x23, 0x21, 0x22, // Space, Enter, Backspace, Tab, Delete, Insert, Home, End, PgUp, PgDn
                            0x25, 0x26, 0x27, 0x28, // Arrow keys
                            0xC0, 0xBD, 0xBB, 0xDB, 0xDD, 0xDC, 0xBA, 0xDE, 0xBC, 0xBE, 0xBF }; // Symbol keys

        foreach (var vk in commonKeys)
        {
            if (IsKeyPressed(vk))
            {
                return ((Keys)vk, modifiers);
            }
        }

        return null;
    }

    private static string? DetectMouseInput()
    {
        if (IsKeyPressed(0x01)) return "mouse1"; // VK_LBUTTON
        if (IsKeyPressed(0x02)) return "mouse2"; // VK_RBUTTON
        if (IsKeyPressed(0x04)) return "mouse3"; // VK_MBUTTON
        if (IsKeyPressed(0x05)) return "mouse4"; // VK_XBUTTON1
        if (IsKeyPressed(0x06)) return "mouse5"; // VK_XBUTTON2

        // Mouse wheel detection would need WM_MOUSEWHEEL messages which we don't have here
        // For now, mouse wheel bindings need to be entered differently

        return null;
    }

    private (string inputName, Guid deviceGuid)? DetectJoystickInput(SCGridColumn col)
    {
        const float AxisThreshold = 0.15f; // 15% threshold like SCVirtStick/Gremlin

        // Initialize on first call - capture baseline from SDL2
        if (_scListening.AxisBaseline is null)
        {
            _scListening.AxisBaseline = new Dictionary<Guid, float[]>();
            _scListening.ButtonBaseline = new Dictionary<Guid, bool[]>();
            _scListening.HatBaseline = new Dictionary<Guid, int[]>();
            _scListening.BaselineFrames = 0;

            // Capture baseline from current SDL2 state
            for (int idx = 0; idx < _ctx.Devices.Count; idx++)
            {
                var device = _ctx.Devices[idx];
                if (device.IsVirtual || !device.IsConnected) continue;

                // For physical columns, only baseline the matching device
                if (col.IsPhysical && device.HidDevicePath != col.PhysicalDevice!.HidDevicePath) continue;

                var state = _ctx.InputService.GetDeviceState(idx);
                if (state is not null)
                {
                    _scListening.AxisBaseline[device.InstanceGuid] = (float[])state.Axes.Clone();
                    _scListening.ButtonBaseline[device.InstanceGuid] = (bool[])state.Buttons.Clone();
                    _scListening.HatBaseline[device.InstanceGuid] = (int[])state.Hats.Clone();
                }
            }

            System.Diagnostics.Debug.WriteLine($"[SCBindings] Initialized SDL2 input detection for {_scListening.AxisBaseline.Count} devices (physical={col.IsPhysical})");
            return null; // First frame - just capture baseline
        }

        _scListening.BaselineFrames++;

        // Skip first few frames to let baseline stabilize
        if (_scListening.BaselineFrames < 3)
            return null;

        // Check each physical device for input changes
        for (int idx = 0; idx < _ctx.Devices.Count; idx++)
        {
            var device = _ctx.Devices[idx];
            if (device.IsVirtual || !device.IsConnected) continue;

            // For physical columns, only listen to the matching device
            if (col.IsPhysical && device.HidDevicePath != col.PhysicalDevice!.HidDevicePath) continue;

            var state = _ctx.InputService.GetDeviceState(idx);
            if (state is null) continue;

            _scListening.AxisBaseline.TryGetValue(device.InstanceGuid, out var baselineAxes);
            _scListening.ButtonBaseline!.TryGetValue(device.InstanceGuid, out var baselineButtons);
            _scListening.HatBaseline!.TryGetValue(device.InstanceGuid, out var baselineHats);

            // Check for button presses - immediately return on first press
            for (int i = 0; i < state.Buttons.Length; i++)
            {
                bool wasPressed = baselineButtons is not null && i < baselineButtons.Length && baselineButtons[i];
                bool isPressed = state.Buttons[i];

                if (isPressed && !wasPressed)
                {
                    System.Diagnostics.Debug.WriteLine($"[SCBindings] SDL2 detected button {i + 1} on {device.Name}");
                    ResetJoystickDetectionState();
                    return ($"button{i + 1}", device.InstanceGuid);
                }
            }

            // Check for axis movement
            for (int i = 0; i < state.Axes.Length; i++)
            {
                float baselineValue = baselineAxes is not null && i < baselineAxes.Length ? baselineAxes[i] : 0f;
                float currValue = state.Axes[i];
                float deflection = Math.Abs(currValue - baselineValue);

                if (deflection > AxisThreshold)
                {
                    // For physical columns, use HID axis type info directly
                    // For vJoy columns, look up the vJoy output axis from the mapping profile
                    string axisName = col.IsPhysical
                        ? GetSCAxisNameFromDevice(i, device)
                        : GetVJoyAxisNameFromMapping(device, i, col);
                    System.Diagnostics.Debug.WriteLine($"[SCBindings] SDL2 detected axis {i} -> {axisName} on {device.Name}, deflection: {deflection:F2}");
                    ResetJoystickDetectionState();
                    return (axisName, device.InstanceGuid);
                }
            }

            // Check for hat movement
            for (int i = 0; i < state.Hats.Length; i++)
            {
                int baselineHat = baselineHats is not null && i < baselineHats.Length ? baselineHats[i] : -1;
                int currHat = state.Hats[i];

                // Hat changed from centered to a direction
                if (currHat >= 0 && baselineHat < 0)
                {
                    string hatDir = GetHatDirection(HatAngleToDiscrete(currHat));
                    System.Diagnostics.Debug.WriteLine($"[SCBindings] SDL2 detected hat {i + 1} {hatDir} on {device.Name}");
                    ResetJoystickDetectionState();
                    return ($"hat{i + 1}_{hatDir}", device.InstanceGuid);
                }
            }
        }

        return null;
    }

    private void ResetJoystickDetectionState()
    {
        _scListening.AxisBaseline = null;
        _scListening.ButtonBaseline = null;
        _scListening.HatBaseline = null;
        _scListening.BaselineFrames = 0;
    }

    /// <summary>
    /// Polls all physical devices for button/hat presses while button capture mode is active.
    /// On first detected press, fills the search box and exits capture mode.
    /// </summary>
    private void CheckButtonCaptureInput()
    {
        // First call: snapshot baseline across all physical devices
        if (_searchFilter.CaptureButtonBaseline is null)
        {
            _searchFilter.CaptureButtonBaseline = new Dictionary<Guid, bool[]>();
            _searchFilter.CaptureHatBaseline = new Dictionary<Guid, int[]>();
            _searchFilter.CaptureBaselineFrames = 0;

            for (int idx = 0; idx < _ctx.Devices.Count; idx++)
            {
                var device = _ctx.Devices[idx];
                if (device.IsVirtual || !device.IsConnected) continue;
                var state = _ctx.InputService.GetDeviceState(idx);
                if (state is null) continue;
                _searchFilter.CaptureButtonBaseline[device.InstanceGuid] = (bool[])state.Buttons.Clone();
                _searchFilter.CaptureHatBaseline[device.InstanceGuid] = (int[])state.Hats.Clone();
            }
            return; // first frame — just capture baseline
        }

        _searchFilter.CaptureBaselineFrames++;
        if (_searchFilter.CaptureBaselineFrames < 3)
            return; // let baseline stabilise

        for (int idx = 0; idx < _ctx.Devices.Count; idx++)
        {
            var device = _ctx.Devices[idx];
            if (device.IsVirtual || !device.IsConnected) continue;
            var state = _ctx.InputService.GetDeviceState(idx);
            if (state is null) continue;

            _searchFilter.CaptureButtonBaseline.TryGetValue(device.InstanceGuid, out var baseButtons);
            _searchFilter.CaptureHatBaseline!.TryGetValue(device.InstanceGuid, out var baseHats);

            // Buttons
            for (int i = 0; i < state.Buttons.Length; i++)
            {
                bool wasPressed = baseButtons is not null && i < baseButtons.Length && baseButtons[i];
                if (!state.Buttons[i] || wasPressed) continue;

                string buttonName = $"button{i + 1}";
                var modKey = (device.InstanceGuid, i); // device-aware modifier lookup

                // If this specific (device, button) pair is mapped to a keyboard modifier,
                // treat it as the modifier and wait for the actual target button.
                // Using device.InstanceGuid means button31 on the LEFT stick ≠ button31 on the RIGHT stick.
                if (_scModifierPhysicalButtons.Contains(modKey) && _searchFilter.CapturePendingModifier is null)
                {
                    _searchFilter.CapturePendingModifier = _scModifierButtonToModifiers.TryGetValue(modKey, out var mods) && mods.Count > 0
                        ? mods[0] : null;
                    // Re-baseline so modifier is included in next baseline (won't fire again)
                    _searchFilter.CaptureButtonBaseline = null;
                    _searchFilter.CaptureHatBaseline = null;
                    _searchFilter.CaptureBaselineFrames = 0;
                    _ctx.MarkDirty();
                    return;
                }

                string modPrefix = _searchFilter.CapturePendingModifier is not null
                    ? _searchFilter.CapturePendingModifier + "+"
                    : GetHeldModifierPrefix();
                ApplyButtonCaptureResult(modPrefix + buttonName, device.HidDevicePath);
                return;
            }

            // Hats
            for (int i = 0; i < state.Hats.Length; i++)
            {
                int baseHat = baseHats is not null && i < baseHats.Length ? baseHats[i] : -1;
                int currHat = state.Hats[i];
                if (currHat >= 0 && baseHat < 0)
                {
                    string dir = GetHatDirection(HatAngleToDiscrete(currHat));
                    string modPrefix = _searchFilter.CapturePendingModifier is not null
                        ? _searchFilter.CapturePendingModifier + "+"
                        : GetHeldModifierPrefix();
                    ApplyButtonCaptureResult(modPrefix + $"hat{i + 1}_{dir}", device.HidDevicePath);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Returns the currently held keyboard modifier prefix (e.g. "lctrl+", "rshift+")
    /// for composing a capture search string. Returns empty string if none are held.
    /// Only one modifier is returned — the first one found in priority order.
    /// </summary>
    private static string GetHeldModifierPrefix()
    {
        if (IsKeyHeld(0xA2)) return "lctrl+";   // VK_LCONTROL
        if (IsKeyHeld(0xA3)) return "rctrl+";   // VK_RCONTROL
        if (IsKeyHeld(0xA0)) return "lshift+";  // VK_LSHIFT
        if (IsKeyHeld(0xA1)) return "rshift+";  // VK_RSHIFT
        if (IsKeyHeld(0xA4)) return "lalt+";    // VK_LMENU
        if (IsKeyHeld(0xA5)) return "ralt+";    // VK_RMENU
        return "";
    }

    private void ApplyButtonCaptureResult(string inputName, string? hidPath = null)
    {
        _searchFilter.ButtonCaptureActive = false;
        _searchFilter.CaptureButtonBaseline = null;
        _searchFilter.CaptureHatBaseline = null;
        _searchFilter.CapturePendingModifier = null;

        // Keep SuppressForwarding=true until the captured button is physically released,
        // then clear snapshots and resume. This prevents the held button from being
        // forwarded to the remote machine the instant forwarding resumes.
        string rawInput = inputName.Contains('+') ? inputName[(inputName.LastIndexOf('+') + 1)..] : inputName;
        _searchFilter.CaptureReleasePendingInput = rawInput;
        _searchFilter.CaptureReleaseWaitTicks = 0;
        _searchFilter.CaptureWaitingForRelease = true;
        // SuppressForwarding remains true — CheckCaptureRelease() clears it on release
        _searchFilter.CaptureDeviceHidPath = hidPath;
        _searchFilter.SearchText = inputName;
        _searchFilter.ButtonCaptureTextActive = true;

        // Highlight the column corresponding to the detected device (same as clicking the column header).
        // Two cases:
        //   - No vJoy setup: physical columns exist directly → match by HID path.
        //   - vJoy setup (typical): physical devices route through vJoy. Find the vJoy column whose
        //     primary physical device (from the active Mappings profile) matches the captured device.
        if (hidPath is not null && _grid.Columns is not null)
        {
            int foundCol = -1;

            // Case 1: physical column (no-vJoy setup)
            for (int c = 0; c < _grid.Columns.Count; c++)
            {
                var col = _grid.Columns[c];
                if (col.IsPhysical && col.PhysicalDevice!.HidDevicePath == hidPath)
                {
                    foundCol = c;
                    break;
                }
            }

            // Case 2: vJoy column — find via VJoyPrimaryDevices in the active Mappings profile
            if (foundCol < 0)
            {
                var physDevice = _ctx.Devices.FirstOrDefault(d => !d.IsVirtual && d.HidDevicePath == hidPath);
                var activeProfile = _ctx.ProfileManager.ActiveProfile;
                if (physDevice is not null && activeProfile is not null)
                {
                    string physGuid = physDevice.InstanceGuid.ToString();
                    foreach (var kv in activeProfile.VJoyPrimaryDevices)
                    {
                        if (!kv.Value.Equals(physGuid, StringComparison.OrdinalIgnoreCase)) continue;
                        uint vjoyId = kv.Key;
                        for (int c = 0; c < _grid.Columns.Count; c++)
                        {
                            var col = _grid.Columns[c];
                            if (col.IsJoystick && !col.IsPhysical && col.VJoyDeviceId == vjoyId)
                            {
                                foundCol = c;
                                break;
                            }
                        }
                        break;
                    }
                }
            }

            if (foundCol >= 0)
            {
                _colImport.HighlightedColumn = foundCol;
                _colImport.ProfileIndex = -1;
                _colImport.ColumnIndex = -1;
                _colImport.LoadedProfile = null;
                _colImport.SourceColumns.Clear();
            }
        }

        RefreshFilteredActions();
        _ctx.MarkDirty();
    }

    // After capture, wait up to ~2 seconds (at ~20 Hz tick) for the button to be released
    private const int CaptureReleaseTimeoutTicks = 40;

    /// <summary>
    /// Polls SDL2 after a button capture to detect when the captured button is physically released.
    /// Once released, clears forwarding snapshots and re-enables forwarding so the button press
    /// that was used for search is never sent to the remote machine.
    /// </summary>
    private void CheckCaptureRelease()
    {
        _searchFilter.CaptureReleaseWaitTicks++;

        // Safety timeout — force-finish if device disconnects or state is unavailable
        if (_searchFilter.CaptureReleaseWaitTicks > CaptureReleaseTimeoutTicks)
        {
            FinishCaptureRelease();
            return;
        }

        string? hidPath = _searchFilter.CaptureDeviceHidPath;
        string? inputName = _searchFilter.CaptureReleasePendingInput;

        // No device/input info — can't poll, keep waiting until timeout
        if (hidPath is null || inputName is null)
            return;

        // Find device by HID path
        int devIdx = -1;
        for (int i = 0; i < _ctx.Devices.Count; i++)
        {
            if (!_ctx.Devices[i].IsVirtual && _ctx.Devices[i].HidDevicePath == hidPath)
            {
                devIdx = i;
                break;
            }
        }

        // Device not found — keep waiting until timeout
        if (devIdx < 0)
            return;

        var state = _ctx.InputService.GetDeviceState(devIdx);

        // State unavailable — keep waiting until timeout
        if (state is null)
            return;

        // Check button still held
        if (inputName.StartsWith("button", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(inputName["button".Length..], out int btnNum))
        {
            int idx = btnNum - 1;
            if (idx >= 0 && idx < state.Buttons.Length && state.Buttons[idx])
                return; // still held — keep waiting
        }
        // Check hat still active
        else if (inputName.StartsWith("hat", StringComparison.OrdinalIgnoreCase))
        {
            int underscoreIdx = inputName.IndexOf('_');
            if (underscoreIdx > 3 && int.TryParse(inputName[3..underscoreIdx], out int hatNum))
            {
                int idx = hatNum - 1;
                if (idx >= 0 && idx < state.Hats.Length && state.Hats[idx] >= 0)
                    return; // still active — keep waiting
            }
        }

        FinishCaptureRelease();
    }

    private void FinishCaptureRelease()
    {
        _searchFilter.CaptureWaitingForRelease = false;
        _searchFilter.CaptureReleasePendingInput = null;
        _searchFilter.CaptureReleaseWaitTicks = 0;
        _ctx.ClearForwardingSnapshots?.Invoke();
        _ctx.SuppressForwarding = false;
    }
}
