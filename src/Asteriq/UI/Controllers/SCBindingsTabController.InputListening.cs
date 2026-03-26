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
                var (inputName, modifiers) = detectedMouse.Value;
                CancelSCInputListening();
                AssignMouseBinding(action, inputName, modifiers);
            }
        }
        else if (col.IsJoystick)
        {
            // Modifier detection strategy: use the Mappings profile to know which physical
            // buttons are mapped to keyboard modifier keys (e.g. physical button 31 → RCTRL).
            // When one of those buttons is detected, skip it as a binding target and wait for
            // the REAL target button.  This avoids all timing races between SDL2 button state
            // and GetAsyncKeyState, which caused the old two-phase approach to be unreliable.

            var detectedJoystick = DetectJoystickInput(col, action.InputType);
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
        bool rAltHeld = IsKeyHeld(0xA5); // VK_RMENU (R-Alt / AltGr)

        if (IsKeyHeld(0xA0) || IsKeyHeld(0xA1)) // VK_LSHIFT, VK_RSHIFT
        {
            modifiers.Add(IsKeyHeld(0xA1) ? "rshift" : "lshift");
        }
        if (IsKeyHeld(0xA2) || IsKeyHeld(0xA3)) // VK_LCONTROL, VK_RCONTROL
        {
            // AltGr sends a phantom L-Ctrl — suppress it when R-Alt is held
            if (IsKeyHeld(0xA3))
                modifiers.Add("rctrl");
            else if (!rAltHeld)
                modifiers.Add("lctrl");
        }
        if (IsKeyHeld(0xA4) || rAltHeld) // VK_LMENU, VK_RMENU (Alt)
        {
            modifiers.Add(rAltHeld ? "ralt" : "lalt");
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

        // Standalone modifier keys — if a modifier is newly pressed with no other key,
        // return it as the key itself (e.g., LShift bound as "lshift" in SC)
        if (IsKeyPressed(0xA0)) return (Keys.LShiftKey, new List<string>());
        if (IsKeyPressed(0xA1)) return (Keys.RShiftKey, new List<string>());
        if (IsKeyPressed(0xA2)) return (Keys.LControlKey, new List<string>());
        if (IsKeyPressed(0xA3)) return (Keys.RControlKey, new List<string>());
        if (IsKeyPressed(0xA4)) return (Keys.LMenu, new List<string>());
        if (IsKeyPressed(0xA5)) return (Keys.RMenu, new List<string>());

        return null;
    }

    private (string inputName, List<string> modifiers)? DetectMouseInput()
    {
        string? mouse = null;
        if (IsKeyPressed(0x01)) mouse = "mouse1"; // VK_LBUTTON
        else if (IsKeyPressed(0x02)) mouse = "mouse2"; // VK_RBUTTON
        else if (IsKeyPressed(0x04)) mouse = "mouse3"; // VK_MBUTTON
        else if (IsKeyPressed(0x05)) mouse = "mouse4"; // VK_XBUTTON1
        else if (IsKeyPressed(0x06)) mouse = "mouse5"; // VK_XBUTTON2

        // Mouse wheel — captured by OnMouseWheel and stored as pending
        if (mouse is null)
        {
            var wheel = _scListening.PendingMouseWheel;
            if (wheel is not null)
            {
                _scListening.PendingMouseWheel = null;
                mouse = wheel;
            }
        }

        if (mouse is null)
            return null;

        // Collect held keyboard modifiers (same logic as DetectKeyboardInput)
        var modifiers = new List<string>();
        bool rAltHeld = IsKeyHeld(0xA5); // VK_RMENU
        if (IsKeyHeld(0xA0) || IsKeyHeld(0xA1))
            modifiers.Add(IsKeyHeld(0xA1) ? "rshift" : "lshift");
        if (IsKeyHeld(0xA2) || IsKeyHeld(0xA3))
        {
            if (IsKeyHeld(0xA3))
                modifiers.Add("rctrl");
            else if (!rAltHeld)
                modifiers.Add("lctrl");
        }
        if (IsKeyHeld(0xA4) || rAltHeld)
            modifiers.Add(rAltHeld ? "ralt" : "lalt");

        return (mouse, modifiers);
    }

    private (string inputName, Guid deviceGuid)? DetectJoystickInput(SCGridColumn col, SCInputType expectedType = SCInputType.Button)
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
                if (col.IsPhysical && GetPhysicalDeviceKey(device) != col.PhysicalDeviceKey) continue;

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
            if (col.IsPhysical && GetPhysicalDeviceKey(device) != col.PhysicalDeviceKey) continue;

            var state = _ctx.InputService.GetDeviceState(idx);
            if (state is null) continue;

            _scListening.AxisBaseline.TryGetValue(device.InstanceGuid, out var baselineAxes);
            _scListening.ButtonBaseline!.TryGetValue(device.InstanceGuid, out var baselineButtons);
            _scListening.HatBaseline!.TryGetValue(device.InstanceGuid, out var baselineHats);

            // Only detect the input type the action expects.
            // Axis actions only listen for axes; button actions listen for buttons/hats.
            if (expectedType == SCInputType.Axis)
            {
                for (int i = 0; i < state.Axes.Length; i++)
                {
                    float baselineValue = baselineAxes is not null && i < baselineAxes.Length ? baselineAxes[i] : 0f;
                    float currValue = state.Axes[i];
                    float deflection = Math.Abs(currValue - baselineValue);

                    if (deflection > AxisThreshold)
                    {
                        string axisName = col.IsPhysical
                            ? GetSCAxisNameFromDevice(i, device)
                            : GetVJoyAxisNameFromMapping(device, i, col);
                        System.Diagnostics.Debug.WriteLine($"[SCBindings] SDL2 detected axis {i} -> {axisName} on {device.Name}, deflection: {deflection:F2}");
                        ResetJoystickDetectionState();
                        return (axisName, device.InstanceGuid);
                    }
                }
            }
            else
            {
                // Button/Hat actions — only detect buttons and hats, ignore axis movement
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

                // Check for hat movement (also button-type input)
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

    // How long after the last detected button press to wait before committing the candidate.
    // This covers multi-stage triggers (e.g. VIRPIL button4→button5): if a higher button
    // follows within this window the candidate is updated to it before committing.
    private const int CaptureDebounceMs = 80;

    /// <summary>
    /// Activates button capture mode: subscribes to the 500 Hz InputReceived event so that
    /// the detection loop runs at full SDL2 polling rate rather than the 60 Hz UI timer.
    /// Builds a snapshot of physical device GUIDs on the UI thread (safe to read _ctx.Devices),
    /// then arms the background handler. SuppressForwarding is set immediately so no
    /// inputs reach the game while capture is active.
    /// </summary>
    private void StartButtonCapture()
    {
        // Build GUID → device key map on UI thread (safe here; background handler is read-only).
        _captureGuidToHidPath = new Dictionary<Guid, string>();
        for (int i = 0; i < _ctx.Devices.Count; i++)
        {
            var dev = _ctx.Devices[i];
            if (!dev.IsVirtual && dev.IsConnected)
                _captureGuidToHidPath[dev.InstanceGuid] = GetPhysicalDeviceKey(dev);
        }

        // Reset all detection state before subscribing.
        _capturePrevButtons = null;
        _capturePrevHats = null;
        _capturePendingModifierBg = null;
        _captureCandidateInput = null;
        _captureCandidatePath = null;
        _captureCandidateDebounceUntil = default;
        _captureWarmupUntil = DateTime.UtcNow + TimeSpan.FromMilliseconds(83); // ~5 frames @ 60 Hz

        // Subscribe — arm the flag BEFORE adding the handler so the first event is never missed.
        Interlocked.Exchange(ref _captureHandlerActive, 1);
        _captureEventHandler = OnCaptureInputReceived;
        _ctx.InputService.InputReceived += _captureEventHandler;

        _ctx.SuppressForwarding = true;
    }

    /// <summary>
    /// Deactivates button capture mode: signals the background handler to stop,
    /// unsubscribes from InputReceived, clears all detection state, and re-enables forwarding.
    /// Safe to call from the UI thread at any time (e.g. toggle-off click or Escape key).
    /// </summary>
    private void StopButtonCapture()
    {
        // Signal handler to stop before unsubscribing to close the race window.
        Interlocked.Exchange(ref _captureHandlerActive, 0);
        if (_captureEventHandler is not null)
        {
            _ctx.InputService.InputReceived -= _captureEventHandler;
            _captureEventHandler = null;
        }

        // Clear detection state — safe now that the handler is detached.
        _captureGuidToHidPath = null;
        _capturePrevButtons = null;
        _capturePrevHats = null;
        _captureMouseWheelPending = null;
        _capturePendingModifierBg = null;
        _captureCandidateInput = null;
        _captureCandidatePath = null;

        // Clear search filter state.
        _searchFilter.ButtonCaptureActive = false;
        _searchFilter.CaptureWaitingForRelease = false;
        _searchFilter.CaptureReleasePendingInput = null;
        _ctx.SuppressForwarding = false;
    }

    /// <summary>
    /// Button capture event handler — runs on the SDL2 background thread at ~500 Hz.
    ///
    /// Phase 1 — Warmup (~83 ms): both the ignore-baseline and previous-frame snapshot are
    ///   refreshed on every event, absorbing any pass-through buttons pressed in this window.
    /// Phase 2 — Detection with debounce: on each new-press transition the candidate is
    ///   updated and the debounce window is extended by <see cref="CaptureDebounceMs"/>.
    ///   Once the debounce window expires with no new presses, the final candidate is
    ///   committed via BeginInvoke. This correctly handles multi-stage triggers where button N
    ///   fires transiently on the way to button M — the last button pressed wins.
    ///
    /// Thread-safety: all detection dicts are written/read exclusively on this thread.
    /// The only cross-thread operation is the Interlocked flag + BeginInvoke for the result.
    /// </summary>
    private void OnCaptureInputReceived(object? sender, DeviceInputState state)
    {
        // Quick gate (volatile read) — bail out if capture was cancelled.
        if (_captureHandlerActive == 0) return;

        // Only process physical devices we snapshotted at subscription time.
        var guidMap = _captureGuidToHidPath;
        if (guidMap is null || !guidMap.TryGetValue(state.InstanceGuid, out string? hidPath)) return;

        var guid = state.InstanceGuid;
        var now = DateTime.UtcNow;
        bool inWarmup = now < _captureWarmupUntil;

        // Lazily initialise dicts on the first event we receive.
        _capturePrevButtons ??= new Dictionary<Guid, bool[]>();
        _capturePrevHats ??= new Dictionary<Guid, int[]>();

        if (inWarmup)
        {
            // During warmup, only update previous-frame snapshots for edge detection.
            // No baseline — buttons held during warmup are NOT permanently locked out.
            // They simply won't trigger until released and re-pressed (edge detection).
            _capturePrevButtons[guid] = (bool[])state.Buttons.Clone();
            _capturePrevHats[guid] = (int[])state.Hats.Clone();
            // Clear "was pressed" state for KB/mouse so the click that started capture
            // doesn't immediately trigger detection once warmup ends.
            GetAsyncKeyState(0x01); // VK_LBUTTON
            GetAsyncKeyState(0x02); // VK_RBUTTON
            GetAsyncKeyState(0x04); // VK_MBUTTON
            GetAsyncKeyState(0x05); // VK_XBUTTON1
            GetAsyncKeyState(0x06); // VK_XBUTTON2
            ClearStaleKeyPresses();
            return;
        }

        // Debounce check: if a candidate was recorded and the window has elapsed, commit it.
        if (_captureCandidateInput is not null && now >= _captureCandidateDebounceUntil)
        {
            string finalInput = _captureCandidateInput;
            string? finalPath = _captureCandidatePath;
            if (Interlocked.Exchange(ref _captureHandlerActive, 0) == 1)
            {
                _ctx.InputService.InputReceived -= _captureEventHandler;
                _captureEventHandler = null;
                _ctx.OwnerForm.BeginInvoke(() => ApplyButtonCaptureResult(finalInput, finalPath));
            }
            return;
        }

        // Detection phase — edge detection only (current vs previous frame).
        _capturePrevButtons.TryGetValue(guid, out var prevButtons);
        _capturePrevHats.TryGetValue(guid, out var prevHats);

        bool detectedThisEvent = false;

        // Buttons — scan all buttons; last new-press in this event wins the candidate.
        for (int i = 0; i < state.Buttons.Length; i++)
        {
            bool wasPrev = prevButtons is not null && i < prevButtons.Length && prevButtons[i];
            if (!state.Buttons[i] || wasPrev) continue;

            // New button press transition detected.
            string buttonName = $"button{i + 1}";
            var modKey = (guid, i);

            if (_scModifierPhysicalButtons.Contains(modKey) && _capturePendingModifierBg is null)
            {
                // Modifier button — record which modifier and re-arm with a fresh warmup
                // so the modifier hold is absorbed before the target button is detected.
                _capturePendingModifierBg = _scModifierButtonToModifiers.TryGetValue(modKey, out var mods) && mods.Count > 0
                    ? mods[0] : null;
                _capturePrevButtons = null;
                _capturePrevHats = null;
                _captureCandidateInput = null;
                _captureCandidatePath = null;
                _captureWarmupUntil = now + TimeSpan.FromMilliseconds(83);
                return;
            }

            string modPrefix = _capturePendingModifierBg is not null
                ? _capturePendingModifierBg + "+"
                : GetHeldModifierPrefix();

            // Update candidate and extend debounce. Continue iterating — if another button
            // becomes newly active in the same event it also updates the candidate.
            _captureCandidateInput = modPrefix + buttonName;
            _captureCandidatePath = hidPath;
            _captureCandidateDebounceUntil = now.AddMilliseconds(CaptureDebounceMs);
            detectedThisEvent = true;
        }

        // Hats — only check if no button was already detected this event.
        if (!detectedThisEvent)
        {
            for (int i = 0; i < state.Hats.Length; i++)
            {
                int prevHat = prevHats is not null && i < prevHats.Length ? prevHats[i] : -1;
                int currHat = state.Hats[i];

                if (currHat >= 0 && prevHat < 0)
                {
                    string dir = GetHatDirection(HatAngleToDiscrete(currHat));
                    string modPrefix = _capturePendingModifierBg is not null
                        ? _capturePendingModifierBg + "+"
                        : GetHeldModifierPrefix();
                    _captureCandidateInput = modPrefix + $"hat{i + 1}_{dir}";
                    _captureCandidatePath = hidPath;
                    _captureCandidateDebounceUntil = now.AddMilliseconds(CaptureDebounceMs);
                    break; // one hat direction per event
                }
            }
        }

        // Mouse button detection — check BEFORE keyboard so that modifier+mouse combos
        // (e.g. L-ALT+Mouse1) are captured as mouse actions, not as standalone modifier keys.
        if (!detectedThisEvent)
        {
            string? mouseResult = null;
            if (IsKeyPressed(0x01)) mouseResult = "mouse1"; // VK_LBUTTON
            else if (IsKeyPressed(0x02)) mouseResult = "mouse2"; // VK_RBUTTON
            else if (IsKeyPressed(0x04)) mouseResult = "mouse3"; // VK_MBUTTON
            else if (IsKeyPressed(0x05)) mouseResult = "mouse4"; // VK_XBUTTON1
            else if (IsKeyPressed(0x06)) mouseResult = "mouse5"; // VK_XBUTTON2

            // Check mouse wheel (set by OnMouseWheel on UI thread)
            var wheelPending = _captureMouseWheelPending;
            if (wheelPending is not null)
            {
                _captureMouseWheelPending = null;
                mouseResult = wheelPending;
            }

            if (mouseResult is not null)
            {
                string modPrefix = GetHeldModifierPrefix();
                _captureCandidateInput = $"mouse:{modPrefix}{mouseResult}";
                _captureCandidatePath = null;
                _captureCandidateDebounceUntil = now.AddMilliseconds(CaptureDebounceMs);
                detectedThisEvent = true;
            }
        }

        // Keyboard detection — poll for newly pressed keys
        if (!detectedThisEvent)
        {
            var kbResult = DetectKeyboardInput();
            if (kbResult is not null)
            {
                var (key, mods) = kbResult.Value;
                string scInput = KeyToSCInput(key);
                string modPrefix = mods.Count > 0 ? string.Join("+", mods) + "+" : "";
                _captureCandidateInput = $"kb:{modPrefix}{scInput}";
                _captureCandidatePath = null;
                _captureCandidateDebounceUntil = now.AddMilliseconds(CaptureDebounceMs);
                detectedThisEvent = true;
            }
        }

        // Always update previous-frame snapshot for this device.
        _capturePrevButtons![guid] = (bool[])state.Buttons.Clone();
        _capturePrevHats![guid] = (int[])state.Hats.Clone();
    }

    /// <summary>
    /// Returns the currently held keyboard modifier prefix (e.g. "lshift+", "ralt+lctrl+")
    /// for composing a capture search string. Returns empty string if none are held.
    /// Handles AltGr: suppresses phantom L-CTRL that Windows injects when R-ALT is held.
    /// </summary>
    private static string GetHeldModifierPrefix()
    {
        bool rAltHeld = IsKeyHeld(0xA5); // VK_RMENU (R-Alt / AltGr)
        var parts = new List<string>(3);

        if (IsKeyHeld(0xA0)) parts.Add("lshift");
        if (IsKeyHeld(0xA1)) parts.Add("rshift");
        // AltGr sends phantom L-Ctrl — suppress it when R-Alt is held
        if (IsKeyHeld(0xA3)) parts.Add("rctrl");
        else if (IsKeyHeld(0xA2) && !rAltHeld) parts.Add("lctrl");
        if (rAltHeld) parts.Add("ralt");
        else if (IsKeyHeld(0xA4)) parts.Add("lalt");

        return parts.Count > 0 ? string.Join("+", parts) + "+" : "";
    }

    private void ApplyButtonCaptureResult(string inputName, string? hidPath = null)
    {
        _searchFilter.ButtonCaptureActive = false;
        // Clear controller-level detection state (handler already unsubscribed before posting this call).
        _captureGuidToHidPath = null;
        _capturePrevButtons = null;
        _capturePrevHats = null;
        _capturePendingModifierBg = null;
        _captureCandidateInput = null;
        _captureCandidatePath = null;

        // Detect device type prefix from capture (kb: or mouse: for non-joystick captures)
        string displayName = inputName;
        string? captureDevicePrefix = null;
        if (inputName.StartsWith("kb:"))
        {
            captureDevicePrefix = "kb";
            displayName = inputName[3..];
        }
        else if (inputName.StartsWith("mouse:"))
        {
            captureDevicePrefix = "mouse";
            displayName = inputName[6..];
        }

        // Keep SuppressForwarding=true until the captured button is physically released,
        // then clear snapshots and resume. This prevents the held button from being
        // forwarded to the remote machine the instant forwarding resumes.
        string rawInput = displayName.Contains('+') ? displayName[(displayName.LastIndexOf('+') + 1)..] : displayName;
        _searchFilter.CaptureReleasePendingInput = rawInput;
        _searchFilter.CaptureReleaseWaitTicks = 0;
        _searchFilter.CaptureWaitingForRelease = captureDevicePrefix is null; // only wait for JS release
        _searchFilter.CaptureDeviceHidPath = hidPath;
        _searchFilter.SearchText = displayName;
        _searchFilter.CursorPos = displayName.Length;
        _searchFilter.SelectionStart = -1;
        _searchFilter.SelectionEnd = -1;
        _searchFilter.ButtonCaptureTextActive = true;
        if (captureDevicePrefix is null)
        {
            // SuppressForwarding remains true — CheckCaptureRelease() clears it on release
        }
        else
        {
            // KB/Mouse: release forwarding immediately (no physical release to wait for)
            _ctx.SuppressForwarding = false;
        }

        // Highlight the column corresponding to the detected device
        if (_grid.Columns is not null)
        {
            int foundCol = -1;

            if (captureDevicePrefix == "kb")
            {
                // Find the KB column
                for (int c = 0; c < _grid.Columns.Count; c++)
                {
                    if (_grid.Columns[c].IsKeyboard) { foundCol = c; break; }
                }
            }
            else if (captureDevicePrefix == "mouse")
            {
                // Find the Mouse column
                for (int c = 0; c < _grid.Columns.Count; c++)
                {
                    if (_grid.Columns[c].IsMouse) { foundCol = c; break; }
                }
            }
            else if (hidPath is not null)
            {
                // Joystick: find by device key
                // Case 1: physical column (no-vJoy setup)
                for (int c = 0; c < _grid.Columns.Count; c++)
                {
                    var col = _grid.Columns[c];
                    if (col.IsPhysical && col.PhysicalDeviceKey == hidPath)
                    {
                        foundCol = c;
                        break;
                    }
                }

                // Case 2: vJoy column — find via VJoyPrimaryDevices in the active Mappings profile
                if (foundCol < 0)
                {
                    var physDevice = _ctx.Devices.FirstOrDefault(d => !d.IsVirtual && GetPhysicalDeviceKey(d) == hidPath);
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

    // After capture, wait up to ~2 seconds (at ~60 Hz tick) for the button to be released
    private const int CaptureReleaseTimeoutTicks = 120;
    // Minimum ticks before polling for release — prevents same-frame or fast-tap false completion
    private const int CaptureReleaseMinTicks = 3;

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

        // Minimum wait before polling — prevents false completion on a fast tap or same-frame evaluation
        if (_searchFilter.CaptureReleaseWaitTicks < CaptureReleaseMinTicks)
            return;

        string? hidPath = _searchFilter.CaptureDeviceHidPath;
        string? inputName = _searchFilter.CaptureReleasePendingInput;

        // No device/input info — can't poll, keep waiting until timeout
        if (hidPath is null || inputName is null)
            return;

        // Find device by device key
        int devIdx = -1;
        for (int i = 0; i < _ctx.Devices.Count; i++)
        {
            if (!_ctx.Devices[i].IsVirtual && GetPhysicalDeviceKey(_ctx.Devices[i]) == hidPath)
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
