using Asteriq.Models;
using Asteriq.Services;

namespace Asteriq.UI.Controllers;

public partial class SCBindingsTabController
{
    private void ApplyDefaultBindingsToProfile()
    {
        if (_scInstall.Actions is null) return;

        int kbCount = 0, moCount = 0, jsCount = 0;

        foreach (var action in _scInstall.Actions)
        {
            foreach (var defaultBinding in action.DefaultBindings)
            {
                // Skip empty bindings (SC uses space for "no binding")
                if (string.IsNullOrWhiteSpace(defaultBinding.Input) || string.IsNullOrEmpty(defaultBinding.Input.Trim()))
                    continue;

                SCDeviceType deviceType;
                if (defaultBinding.DevicePrefix.StartsWith("kb", StringComparison.OrdinalIgnoreCase))
                {
                    deviceType = SCDeviceType.Keyboard;
                    kbCount++;
                }
                else if (defaultBinding.DevicePrefix.StartsWith("mo", StringComparison.OrdinalIgnoreCase))
                {
                    deviceType = SCDeviceType.Mouse;
                    moCount++;
                }
                else if (defaultBinding.DevicePrefix.StartsWith("js", StringComparison.OrdinalIgnoreCase))
                {
                    deviceType = SCDeviceType.Joystick;
                    jsCount++;
                }
                else
                {
                    continue; // Skip gamepad and unknown
                }

                var binding = new SCActionBinding
                {
                    ActionMap = action.ActionMap,
                    ActionName = action.ActionName,
                    DeviceType = deviceType,
                    InputName = defaultBinding.Input,
                    InputType = InferInputTypeFromName(defaultBinding.Input),
                    Inverted = defaultBinding.Inverted,
                    ActivationMode = defaultBinding.ActivationMode,
                    Modifiers = defaultBinding.Modifiers.ToList()
                };

                // For joystick bindings, set VJoyDevice based on the js instance in prefix
                if (deviceType == SCDeviceType.Joystick)
                {
                    // Extract instance from "js1", "js2", etc.
                    if (defaultBinding.DevicePrefix.Length > 2 &&
                        uint.TryParse(defaultBinding.DevicePrefix.AsSpan(2), out var jsInstance))
                    {
                        binding.VJoyDevice = jsInstance;
                    }
                    else
                    {
                        binding.VJoyDevice = 1; // Default to js1
                    }
                }

                _scExportProfile.SetBinding(action.ActionMap, action.ActionName, binding);
            }
        }

        _scExportProfileService?.SaveProfile(_scExportProfile);
        System.Diagnostics.Debug.WriteLine($"[MainForm] Applied SC defaults to profile: {kbCount} KB, {moCount} Mouse, {jsCount} JS bindings");
    }

    private void AssignKeyboardBinding(SCAction action, Keys key, List<string> modifiers)
    {
        // Store as SC-format keyboard binding
        string inputName = KeyToSCInput(key);
        System.Diagnostics.Debug.WriteLine($"[SCBindings] Assigning KB binding: {action.ActionName} = {string.Join("+", modifiers)}+{inputName}");

        // Store in the export profile (persisted and clearable)
        var binding = new SCActionBinding
        {
            ActionMap = action.ActionMap,
            ActionName = action.ActionName,
            DeviceType = SCDeviceType.Keyboard,
            InputName = inputName,
            InputType = SCInputType.Button,
            Modifiers = modifiers
        };

        _scExportProfile.SetBinding(action.ActionMap, action.ActionName, binding);
        _scExportProfileService?.SaveProfile(_scExportProfile);

        SetStatus($"Bound {action.ActionName} to kb1_{inputName}");
    }

    private void AssignMouseBinding(SCAction action, string inputName, List<string>? modifiers = null)
    {
        string modDisplay = modifiers is { Count: > 0 } ? string.Join("+", modifiers) + "+" : "";
        System.Diagnostics.Debug.WriteLine($"[SCBindings] Assigning Mouse binding: {action.ActionName} = {modDisplay}{inputName}");

        var binding = new SCActionBinding
        {
            ActionMap = action.ActionMap,
            ActionName = action.ActionName,
            DeviceType = SCDeviceType.Mouse,
            InputName = inputName,
            InputType = SCInputType.Button,
            Modifiers = modifiers ?? new List<string>()
        };

        _scExportProfile.SetBinding(action.ActionMap, action.ActionName, binding);
        _scExportProfileService?.SaveProfile(_scExportProfile);

        SetStatus($"Bound {action.ActionName} to mo1_{modDisplay}{inputName}");
    }

    private void AssignJoystickBinding(SCAction action, SCGridColumn col, string inputName, List<string>? modifiers = null)
    {
        string modifierPrefix = modifiers is { Count: > 0 } ? string.Join("+", modifiers) + "+" : "";
        System.Diagnostics.Debug.WriteLine($"[SCBindings] Assigning JS binding: {action.ActionName} = js{col.SCInstance}_{modifierPrefix}{inputName} (physical={col.IsPhysical})");

        // Check for conflicting bindings (same input + same modifier set used by another action).
        // rctrl+button13 and button13 are DIFFERENT bindings; only flag when both match.
        List<SCActionBinding> conflicts;
        if (col.IsPhysical)
        {
            conflicts = _scExportProfile.GetConflictingBindings(
                col.PhysicalDeviceKey,
                inputName,
                action.ActionMap,
                action.ActionName,
                modifiers);
        }
        else
        {
            conflicts = _scExportProfile.GetConflictingBindings(
                col.VJoyDeviceId,
                inputName,
                action.ActionMap,
                action.ActionName,
                modifiers);
        }

        if (conflicts.Count > 0)
        {
            // Show conflict dialog
            string actionDisplayName = SCCategoryMapper.FormatActionName(action.ActionName);
            string inputDisplayName = SCBindingsRenderer.FormatInputName(inputName);
            string deviceName = $"JS{col.SCInstance}";

            using var dialog = new BindingConflictDialog(conflicts, actionDisplayName, inputDisplayName, deviceName);
            dialog.ShowDialog(_ctx.OwnerForm);

            switch (dialog.Result)
            {
                case BindingConflictResult.Cancel:
                    System.Diagnostics.Debug.WriteLine($"[SCBindings] Binding cancelled for {action.ActionName}");
                    return;

                case BindingConflictResult.ReplaceAll:
                    foreach (var conflict in conflicts)
                    {
                        _scExportProfile.RemoveBinding(conflict);
                        System.Diagnostics.Debug.WriteLine($"[SCBindings] Removed conflicting binding: {conflict.ActionName}");
                    }
                    break;

                case BindingConflictResult.ApplyAnyway:
                    System.Diagnostics.Debug.WriteLine($"[SCBindings] Applying binding with {conflicts.Count} conflict(s)");
                    break;
            }
        }

        // Cross-device conflict check: same action already bound to a DIFFERENT vJoy device.
        // Only applies to vJoy bindings (not physical device columns).
        if (!col.IsPhysical)
        {
            var existingOnOtherDevice = _scExportProfile.Bindings
                .FirstOrDefault(b =>
                    b.ActionMap == action.ActionMap &&
                    b.ActionName == action.ActionName &&
                    b.DeviceType == SCDeviceType.Joystick &&
                    b.PhysicalDeviceId is null &&
                    b.VJoyDevice != col.VJoyDeviceId);

            if (existingOnOtherDevice is not null)
            {
                int primaryInstance = _scExportProfile.GetSCInstance(existingOnOtherDevice.VJoyDevice);
                string primaryInputDisplay = SCBindingsRenderer.FormatInputName(existingOnOtherDevice.InputName);
                string secondaryInputDisplay = SCBindingsRenderer.FormatInputName(inputName);

                // Gather ALL other actions on the same primary binding â€” same vJoy device,
                // same input, AND same modifier set. Bindings that reuse the button number
                // with a different modifier (e.g. button28 alone vs rctrl+button28) are
                // different actions that can and should have independent shares.
                var otherAffected = _scExportProfile.Bindings
                    .Where(b => b.DeviceType == SCDeviceType.Joystick &&
                        b.PhysicalDeviceId is null &&
                        b.VJoyDevice == existingOnOtherDevice.VJoyDevice &&
                        b.InputName == existingOnOtherDevice.InputName &&
                        b.Modifiers.SequenceEqual(existingOnOtherDevice.Modifiers) &&
                        !(b.ActionMap == action.ActionMap && b.ActionName == action.ActionName))
                    .ToList();
                var affectedNames = otherAffected
                    .Select(b => SCCategoryMapper.FormatActionName(b.ActionName))
                    .Distinct().ToList();

                using var sharedDialog = new SCSharedBindingDialog(
                    SCCategoryMapper.FormatActionName(action.ActionName),
                    $"JS{primaryInstance}",
                    primaryInputDisplay,
                    $"JS{col.SCInstance}",
                    secondaryInputDisplay,
                    affectedNames);
                sharedDialog.ShowDialog(_ctx.OwnerForm);

                switch (sharedDialog.Result)
                {
                    case SCSharedBindingResult.Cancel:
                        System.Diagnostics.Debug.WriteLine($"[SCBindings] Cross-device binding cancelled for {action.ActionName}");
                        return;

                    case SCSharedBindingResult.Replace:
                        // Remove the existing primary binding and fall through to assign normally
                        _scExportProfile.RemoveBinding(existingOnOtherDevice);
                        System.Diagnostics.Debug.WriteLine($"[SCBindings] Replaced JS{primaryInstance} binding for {action.ActionName}");
                        break;

                    case SCSharedBindingResult.Share:
                        // Reroute physical button + update SharedWith on ALL affected bindings
                        PerformShare(existingOnOtherDevice, col.VJoyDeviceId, inputName);
                        foreach (var affected in otherAffected)
                        {
                            if (!affected.SharedWith.Any(s => s.VJoySlot == col.VJoyDeviceId))
                            {
                                affected.SharedWith.Add(new SCSharedInput
                                {
                                    VJoySlot = col.VJoyDeviceId,
                                    InputName = inputName,
                                    ReroutedMappingIds = new List<Guid>() // Reroute already done by PerformShare
                                });
                            }
                        }
                        _scExportProfileService?.SaveProfile(_scExportProfile);
                        UpdateSharedCells();
                        UpdateConflictingBindings();
                        int totalShared = 1 + otherAffected.Count;
                        SetStatus($"Shared: JS{col.SCInstance} {secondaryInputDisplay} â†’ JS{primaryInstance} {primaryInputDisplay} ({totalShared} action(s))");
                        _ctx.MarkDirty();
                        return; // Don't create a new binding â€” the share is the assignment
                }
            }
        }

        // Cross-device conflict check for physical device columns.
        // SC only supports one joystick binding per action; no vJoy rerouting is available.
        if (col.IsPhysical)
        {
            var existingOnOtherPhysical = _scExportProfile.Bindings
                .FirstOrDefault(b =>
                    b.ActionMap == action.ActionMap &&
                    b.ActionName == action.ActionName &&
                    b.DeviceType == SCDeviceType.Joystick &&
                    b.PhysicalDeviceId is not null &&
                    b.PhysicalDeviceId != col.PhysicalDeviceKey);

            if (existingOnOtherPhysical is not null)
            {
                int existingInstance = _scExportProfile.GetSCInstanceForPhysical(existingOnOtherPhysical.PhysicalDeviceId!);
                string existingInputDisplay = SCBindingsRenderer.FormatInputName(existingOnOtherPhysical.InputName);
                string newInputDisplay = SCBindingsRenderer.FormatInputName(inputName);

                using var replaceDialog = new FUIConfirmDialog(
                    "Action Already Bound",
                    $"\"{SCCategoryMapper.FormatActionName(action.ActionName)}\" is already bound to JS{existingInstance} / {existingInputDisplay}.\n\n" +
                    $"Star Citizen only supports one joystick binding per action.\n\n" +
                    $"Replace it with JS{col.SCInstance} / {newInputDisplay}?",
                    "Replace", "Cancel");

                if (replaceDialog.ShowDialog(_ctx.OwnerForm) != DialogResult.Yes)
                {
                    System.Diagnostics.Debug.WriteLine($"[SCBindings] Physical cross-device binding cancelled for {action.ActionName}");
                    return;
                }

                _scExportProfile.RemoveBinding(existingOnOtherPhysical);
                System.Diagnostics.Debug.WriteLine($"[SCBindings] Replaced physical JS{existingInstance} binding for {action.ActionName}");
            }
        }

        // Determine input type from input name
        var inputType = InferInputTypeFromName(inputName);

        var modifierList = modifiers ?? new List<string>();

        if (col.IsPhysical)
        {
            // Physical device binding
            _scExportProfile.SetBinding(action.ActionMap, action.ActionName, new SCActionBinding
            {
                ActionMap = action.ActionMap,
                ActionName = action.ActionName,
                DeviceType = SCDeviceType.Joystick,
                PhysicalDeviceId = col.PhysicalDeviceKey,
                InputName = inputName,
                InputType = inputType,
                Modifiers = modifierList
            });
        }
        else
        {
            // vJoy binding
            _scExportProfile.SetBinding(action.ActionMap, action.ActionName, new SCActionBinding
            {
                ActionMap = action.ActionMap,
                ActionName = action.ActionName,
                DeviceType = SCDeviceType.Joystick,
                VJoyDevice = col.VJoyDeviceId,
                InputName = inputName,
                InputType = inputType,
                Modifiers = modifierList
            });

            // Ensure this vJoy device has an SC instance mapping (required for export)
            if (!_scExportProfile.VJoyToSCInstance.ContainsKey(col.VJoyDeviceId))
            {
                _scExportProfile.SetSCInstance(col.VJoyDeviceId, col.SCInstance);
                System.Diagnostics.Debug.WriteLine($"[SCBindings] Set vJoy{col.VJoyDeviceId} -> js{col.SCInstance} mapping");
            }
        }

        // Save the profile and update conflict detection
        _scExportProfileService?.SaveProfile(_scExportProfile);
        UpdateConflictingBindings();

        SetStatus($"Bound {action.ActionName} to js{col.SCInstance}_{modifierPrefix}{inputName}");
    }

    /// <summary>
    /// Performs the Share operation: adds a SharedWith entry to the primary binding, and
    /// for no-modifier primaries also reroutes physical button mappings to the primary vJoy
    /// button so existing XML profiles keep firing the action via the primary's rebind.
    /// When the primary has modifiers, skip the reroute â€” the secondary input must reach
    /// SC on its own vJoy slot so the per-share <rebind> in the XML export can fire the
    /// action without requiring the modifier held on the secondary device.
    /// </summary>
}