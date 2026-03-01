using Asteriq.Models;
using Asteriq.Services;

namespace Asteriq.UI.Controllers;

public partial class SCBindingsTabController
{
    private List<SCGridColumn> GetSCGridColumns()
    {
        var columns = new List<SCGridColumn>
        {
            new SCGridColumn { Id = "kb", Header = "KB", DevicePrefix = "kb1", IsKeyboard = true },
            new SCGridColumn { Id = "mouse", Header = "Mouse", DevicePrefix = "mo1", IsMouse = true }
        };

        // Track all SC instances in use to avoid collisions between vJoy and physical columns
        var usedSCInstances = new HashSet<int>();

        // Add a column for each vJoy device that exists and is mapped in the export profile
        var existingVJoyIds = _ctx.VJoyDevices.Where(v => v.Exists).Select(v => v.Id).ToHashSet();
        foreach (var vjoy in _ctx.VJoyDevices.Where(v => v.Exists))
        {
            int scInstance = _scExportProfile.GetSCInstance(vjoy.Id);
            columns.Add(new SCGridColumn
            {
                Id = $"js{scInstance}",
                Header = $"JS{scInstance}",
                DevicePrefix = $"js{scInstance}",
                VJoyDeviceId = vjoy.Id,
                SCInstance = scInstance,
                IsJoystick = true
            });
            usedSCInstances.Add(scInstance);
        }

        // Add read-only columns for JS instances stored in the profile that have no backing vJoy device.
        // This lets users view bindings they previously configured even when vJoy isn't installed.
        foreach (var kv in _scExportProfile.VJoyToSCInstance
            .Where(kv => !existingVJoyIds.Contains(kv.Key))
            .OrderBy(kv => kv.Value))
        {
            columns.Add(new SCGridColumn
            {
                Id = $"js{kv.Value}",
                Header = $"JS{kv.Value}",
                DevicePrefix = $"js{kv.Value}",
                VJoyDeviceId = kv.Key,
                SCInstance = kv.Value,
                IsJoystick = true,
                IsReadOnly = true
            });
            usedSCInstances.Add(kv.Value);
        }

        // Add physical device columns only when no vJoy devices exist.
        // When vJoy is installed, physical devices are routed through vJoy —
        // showing them as separate columns would create confusion and duplicate bindings.
        if (existingVJoyIds.Count > 0)
            return columns;

        foreach (var device in _ctx.Devices)
        {
            if (device.IsVirtual || !device.IsConnected) continue;
            if (string.IsNullOrEmpty(device.HidDevicePath)) continue;

            // Check if this device already has a persisted SC instance
            int scInstance = _scExportProfile.GetSCInstanceForPhysical(device.HidDevicePath);
            if (scInstance == 0)
            {
                // Assign next available SC instance
                scInstance = usedSCInstances.Count > 0 ? usedSCInstances.Max() + 1 : 1;
                _scExportProfile.SetSCInstanceForPhysical(device.HidDevicePath, scInstance);
                // Persist the DirectInput GUID for XML export
                if (device.DirectInputGuid != Guid.Empty)
                {
                    _scExportProfile.PhysicalDeviceDirectInputGuids[device.HidDevicePath] = device.DirectInputGuid;
                }
                _scExportProfileService?.SaveProfile(_scExportProfile);
            }

            usedSCInstances.Add(scInstance);

            // Build a truncated header from the device name
            string shortName = TruncateDeviceName(device.Name);

            columns.Add(new SCGridColumn
            {
                Id = $"phys:{device.HidDevicePath}",
                Header = shortName,
                DevicePrefix = $"js{scInstance}",
                SCInstance = scInstance,
                IsJoystick = true,
                PhysicalDevice = device
            });
        }

        return columns;
    }

    private static string TruncateDeviceName(string name)
    {
        // Strip common generic suffixes
        string[] stripSuffixes = { " USB Joystick", " USB", " HID", " Device" };
        foreach (var suffix in stripSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && name.Length > suffix.Length)
                name = name[..^suffix.Length];
        }

        // Truncate to max 16 chars
        if (name.Length > 16)
            name = name[..14] + "..";

        return name;
    }

    private void CalculateDeviceColumnWidths()
    {
        _scGridDeviceColWidths.Clear();

        if (_scActions is null) return;

        var columns = GetSCGridColumns();
        float padding = 12f; // Cell padding

        foreach (var col in columns)
        {
            float maxWidth = _scGridDeviceColMinWidth;

            // Determine device type for this column
            SCDeviceType? deviceType = col.IsKeyboard ? SCDeviceType.Keyboard :
                                        col.IsMouse ? SCDeviceType.Mouse :
                                        col.IsJoystick ? SCDeviceType.Joystick : null;

            foreach (var action in _scActions)
            {
                // Check profile bindings for this column
                if (deviceType.HasValue)
                {
                    SCActionBinding? binding = null;
                    if (col.IsJoystick)
                    {
                        binding = _scExportProfile.GetBinding(action.ActionMap, action.ActionName, SCDeviceType.Joystick);
                        // Check if this binding matches the current column's device
                        if (binding is not null && _scExportProfile.GetSCInstance(binding.VJoyDevice) != col.SCInstance)
                            binding = null;
                    }
                    else
                    {
                        binding = _scExportProfile.GetBinding(action.ActionMap, action.ActionName, deviceType.Value);
                    }

                    if (binding is not null && !string.IsNullOrEmpty(binding.InputName))
                    {
                        var components = SCBindingsRenderer.GetBindingComponents(binding.InputName, binding.Modifiers);
                        float badgesWidth = SCBindingsRenderer.MeasureMultiKeycapWidth(components, binding.InputType) + padding;
                        maxWidth = Math.Max(maxWidth, badgesWidth);
                    }
                }

                // Also check default bindings
                var defaultBinding = action.DefaultBindings
                    .FirstOrDefault(b => b.DevicePrefix.Equals(col.DevicePrefix, StringComparison.OrdinalIgnoreCase));
                if (defaultBinding is not null && !string.IsNullOrEmpty(defaultBinding.Input))
                {
                    var modifiers = defaultBinding.Modifiers?.Where(m => !string.IsNullOrEmpty(m)).ToList();
                    var components = SCBindingsRenderer.GetBindingComponents(defaultBinding.Input, modifiers);
                    // Default bindings don't have input type info
                    float badgesWidth = SCBindingsRenderer.MeasureMultiKeycapWidth(components, null) + padding;
                    maxWidth = Math.Max(maxWidth, badgesWidth);
                }
            }

            _scGridDeviceColWidths[col.Id] = maxWidth;
        }
    }

    private void RefreshFilteredActions()
    {
        if (_scActions is null || _scSchemaService is null)
        {
            _scFilteredActions = null;
            return;
        }

        // Start with joystick-relevant actions
        var actions = _scSchemaService.FilterJoystickActions(_scActions);

        // Apply action map filter if set (use category name for filtering)
        // Use GetCategoryNameForAction to respect action-level overrides (e.g., Emergency)
        if (!string.IsNullOrEmpty(_scActionMapFilter))
        {
            actions = actions.Where(a =>
                SCCategoryMapper.GetCategoryNameForAction(a.ActionMap, a.ActionName) == _scActionMapFilter).ToList();
        }

        // Apply search filter if set - search multiple fields like SCVirtStick
        if (!string.IsNullOrEmpty(_scSearchText))
        {
            var searchLower = _scSearchText.ToLowerInvariant();
            actions = actions.Where(a => ActionMatchesSearch(a, searchLower)).ToList();
        }

        // Apply "show bound only" filter if enabled
        if (_scShowBoundOnly)
        {
            actions = actions.Where(a =>
                _scExportProfile.GetBinding(a.ActionMap, a.ActionName) is not null
            ).ToList();
        }

        // Sort by category order (like SCVirtStick), then by action name
        // IMPORTANT: Use GetSortOrderForAction to respect action-level overrides (e.g., Emergency)
        _scFilteredActions = actions
            .OrderBy(a => SCCategoryMapper.GetSortOrderForAction(a.ActionMap, a.ActionName))
            .ThenBy(a => SCCategoryMapper.GetCategoryNameForAction(a.ActionMap, a.ActionName))
            .ThenBy(a => a.ActionName)
            .ToList();

        _scBindingsScrollOffset = 0;  // Reset scroll when filter changes
        _scSelectedActionIndex = -1;  // Clear selection
    }

    private bool ActionMatchesSearch(SCAction action, string searchLower)
    {
        // Check action name (raw)
        if (action.ActionName.ToLowerInvariant().Contains(searchLower))
            return true;

        // Check formatted action name (display name)
        if (SCCategoryMapper.FormatActionName(action.ActionName).ToLowerInvariant().Contains(searchLower))
            return true;

        // Check category name
        if (SCCategoryMapper.GetCategoryName(action.ActionMap).ToLowerInvariant().Contains(searchLower))
            return true;

        // Check actionmap name (raw)
        if (action.ActionMap.ToLowerInvariant().Contains(searchLower))
            return true;

        // Check user binding input names
        var userBinding = _scExportProfile.GetBinding(action.ActionMap, action.ActionName);
        if (userBinding is not null)
        {
            if (userBinding.InputName.ToLowerInvariant().Contains(searchLower))
                return true;

            // Check modifiers
            foreach (var modifier in userBinding.Modifiers)
            {
                if (modifier.ToLowerInvariant().Contains(searchLower))
                    return true;
            }
        }

        // Check default binding input names
        foreach (var binding in action.DefaultBindings)
        {
            if (binding.Input.ToLowerInvariant().Contains(searchLower))
                return true;

            if (binding.FullInput.ToLowerInvariant().Contains(searchLower))
                return true;

            foreach (var modifier in binding.Modifiers)
            {
                if (modifier.ToLowerInvariant().Contains(searchLower))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Rebuilds the shared-cell lookup from the current export profile.
    /// Called after any binding change that may affect SharedWith lists.
    /// </summary>
    private void UpdateSharedCells()
    {
        _scSharedCells.Clear();
        foreach (var binding in _scExportProfile.Bindings)
        {
            foreach (var shared in binding.SharedWith)
            {
                string key = $"{binding.ActionKey}|{shared.VJoySlot}";
                _scSharedCells[key] = (binding.VJoyDevice, binding.InputName, shared.InputName);
            }
        }
    }

    /// <summary>
    /// Parses a vJoy button index (0-based) from an SC input name like "button33".
    /// Returns -1 if the input is not a button or cannot be parsed.
    /// </summary>
    private static int ParseButtonIndex(string inputName)
    {
        if (inputName.StartsWith("button", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(inputName[6..], out var n) && n >= 1)
            return n - 1; // 0-based
        return -1;
    }

    private void UpdateDuplicateActionBindings()
    {
        _scDuplicateActionBindings = _scExportProfile.GetDuplicateJoystickActionKeys();
    }

    private void UpdateConflictingBindings()
    {
        _scConflictingBindings.Clear();

        // Track which inputs are used and by which actions
        // Key: "js1_button5" or "phys:path_button5" etc., Value: list of binding keys that use it
        var inputUsage = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // Check user bindings from export profile
        foreach (var binding in _scExportProfile.Bindings)
        {
            if (binding.DeviceType != SCDeviceType.Joystick) continue;

            // Include modifiers in the key so rctrl+button13 and button13 are separate slots
            string modPrefix = binding.Modifiers is { Count: > 0 }
                ? string.Join("+", binding.Modifiers.OrderBy(m => m, StringComparer.OrdinalIgnoreCase)) + "+"
                : "";

            string inputKey;
            if (binding.PhysicalDeviceId is not null)
            {
                int scInstance = _scExportProfile.GetSCInstanceForPhysical(binding.PhysicalDeviceId);
                inputKey = $"js{scInstance}_{modPrefix}{binding.InputName}";
            }
            else
            {
                int scInstance = _scExportProfile.GetSCInstance(binding.VJoyDevice);
                inputKey = $"js{scInstance}_{modPrefix}{binding.InputName}";
            }

            string actionKey = binding.Key;

            if (!inputUsage.TryGetValue(inputKey, out var actions))
            {
                actions = new List<string>();
                inputUsage[inputKey] = actions;
            }
            actions.Add(actionKey);
        }

        // Find inputs that are used by multiple actions
        foreach (var kvp in inputUsage)
        {
            if (kvp.Value.Count > 1)
            {
                // Mark all actions using this input as conflicting
                foreach (var actionKey in kvp.Value)
                {
                    _scConflictingBindings.Add(actionKey);
                }
            }
        }

        UpdateDuplicateActionBindings();
    }

    private void ApplyDefaultBindingsToProfile()
    {
        if (_scActions is null) return;

        int kbCount = 0, moCount = 0, jsCount = 0;

        foreach (var action in _scActions)
        {
            foreach (var defaultBinding in action.DefaultBindings)
            {
                // Skip empty bindings (SC uses space for "no binding")
                if (string.IsNullOrWhiteSpace(defaultBinding.Input) || defaultBinding.Input.Trim() == "")
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
                        uint.TryParse(defaultBinding.DevicePrefix.Substring(2), out var jsInstance))
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

    private void AssignMouseBinding(SCAction action, string inputName)
    {
        System.Diagnostics.Debug.WriteLine($"[SCBindings] Assigning Mouse binding: {action.ActionName} = {inputName}");

        // Store in the export profile (persisted and clearable)
        var binding = new SCActionBinding
        {
            ActionMap = action.ActionMap,
            ActionName = action.ActionName,
            DeviceType = SCDeviceType.Mouse,
            InputName = inputName,
            InputType = SCInputType.Button
        };

        _scExportProfile.SetBinding(action.ActionMap, action.ActionName, binding);
        _scExportProfileService?.SaveProfile(_scExportProfile);

        SetStatus($"Bound {action.ActionName} to mo1_{inputName}");
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
                col.PhysicalDevice!.HidDevicePath,
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

            var dialog = new BindingConflictDialog(conflicts, actionDisplayName, inputDisplayName, deviceName);
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

                using var sharedDialog = new SCSharedBindingDialog(
                    SCCategoryMapper.FormatActionName(action.ActionName),
                    $"JS{primaryInstance}",
                    primaryInputDisplay,
                    $"JS{col.SCInstance}",
                    secondaryInputDisplay);
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
                        // Reroute physical button + update SharedWith — do not create a new SC binding
                        PerformShare(existingOnOtherDevice, col.VJoyDeviceId, inputName);
                        _scExportProfileService?.SaveProfile(_scExportProfile);
                        UpdateSharedCells();
                        UpdateConflictingBindings();
                        SetStatus($"Shared: JS{col.SCInstance} {secondaryInputDisplay} → JS{primaryInstance} {primaryInputDisplay}");
                        _ctx.MarkDirty();
                        return; // Don't create a new binding — the share is the assignment
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
                    b.PhysicalDeviceId != col.PhysicalDevice!.HidDevicePath);

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
                PhysicalDeviceId = col.PhysicalDevice!.HidDevicePath,
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
    /// Performs the Share operation: reroutes physical button mappings from secondary to primary
    /// and adds a SharedWith entry to the primary binding.
    /// </summary>
    private void PerformShare(SCActionBinding primaryBinding, uint secondaryVJoySlot, string secondaryInputName)
    {
        int secondaryButtonIndex = ParseButtonIndex(secondaryInputName);
        int primaryButtonIndex = ParseButtonIndex(primaryBinding.InputName);

        var reroutedIds = new List<Guid>();

        // Reroute in the active mapping profile if possible
        var mappingProfile = _ctx.ProfileManager.ActiveProfile;
        if (mappingProfile is not null && secondaryButtonIndex >= 0 && primaryButtonIndex >= 0)
        {
            foreach (var bm in mappingProfile.ButtonMappings)
            {
                if (bm.Output.Type == OutputType.VJoyButton &&
                    bm.Output.VJoyDevice == secondaryVJoySlot &&
                    bm.Output.Index == secondaryButtonIndex)
                {
                    bm.Output.VJoyDevice = primaryBinding.VJoyDevice;
                    bm.Output.Index = primaryButtonIndex;
                    reroutedIds.Add(bm.Id);
                }
            }

            if (reroutedIds.Count > 0)
            {
                _ctx.OnMappingsChanged();
                System.Diagnostics.Debug.WriteLine(
                    $"[SCBindings] Rerouted {reroutedIds.Count} mapping(s) from vJoy{secondaryVJoySlot}/{secondaryInputName} → vJoy{primaryBinding.VJoyDevice}/{primaryBinding.InputName}");
            }
        }

        primaryBinding.SharedWith.Add(new SCSharedInput
        {
            VJoySlot = secondaryVJoySlot,
            InputName = secondaryInputName,
            ReroutedMappingIds = reroutedIds
        });
    }

    /// <summary>
    /// Handles a click on a shared cell — shows the Unshare dialog and performs the operation if confirmed.
    /// </summary>
    private void HandleSharedCellClick(SCAction action, SCGridColumn col)
    {
        string sharedKey = $"{action.Key}|{col.VJoyDeviceId}";
        if (!_scSharedCells.TryGetValue(sharedKey, out var linkInfo))
            return;

        var primaryBinding = _scExportProfile.Bindings.FirstOrDefault(b =>
            b.ActionMap == action.ActionMap &&
            b.ActionName == action.ActionName &&
            b.DeviceType == SCDeviceType.Joystick &&
            b.PhysicalDeviceId is null &&
            b.VJoyDevice == linkInfo.PrimaryVJoyDevice);

        if (primaryBinding is null) return;

        var sharedEntry = primaryBinding.SharedWith.FirstOrDefault(s => s.VJoySlot == col.VJoyDeviceId);
        if (sharedEntry is null) return;

        int primaryInstance = _scExportProfile.GetSCInstance(primaryBinding.VJoyDevice);
        string primaryInputDisplay = SCBindingsRenderer.FormatInputName(primaryBinding.InputName);
        string secondaryInputDisplay = SCBindingsRenderer.FormatInputName(sharedEntry.InputName);

        using var dialog = new FUIConfirmDialog(
            "Shared Binding",
            $"{secondaryInputDisplay} on JS{col.SCInstance} is shared with JS{primaryInstance} / {primaryInputDisplay}.\n\nUnshare this binding?",
            "Unshare", "Cancel");

        if (dialog.ShowDialog(_ctx.OwnerForm) != DialogResult.Yes)
            return;

        PerformUnshare(primaryBinding, sharedEntry, col.SCInstance, primaryInstance, primaryInputDisplay, secondaryInputDisplay);
    }

    /// <summary>
    /// Performs the Unshare operation: restores rerouted mappings and removes the SharedWith entry.
    /// </summary>
    private void PerformUnshare(
        SCActionBinding primaryBinding,
        Models.SCSharedInput sharedEntry,
        int secondarySCInstance,
        int primarySCInstance,
        string primaryInputDisplay,
        string secondaryInputDisplay)
    {
        int secondaryButtonIndex = ParseButtonIndex(sharedEntry.InputName);

        // Restore rerouted mappings
        var mappingProfile = _ctx.ProfileManager.ActiveProfile;
        if (mappingProfile is not null && secondaryButtonIndex >= 0 && sharedEntry.ReroutedMappingIds.Count > 0)
        {
            foreach (var mappingId in sharedEntry.ReroutedMappingIds)
            {
                var mapping = mappingProfile.ButtonMappings.FirstOrDefault(m => m.Id == mappingId);
                if (mapping is not null)
                {
                    mapping.Output.VJoyDevice = sharedEntry.VJoySlot;
                    mapping.Output.Index = secondaryButtonIndex;
                }
            }
            _ctx.OnMappingsChanged();
            System.Diagnostics.Debug.WriteLine(
                $"[SCBindings] Restored {sharedEntry.ReroutedMappingIds.Count} mapping(s) back to vJoy{sharedEntry.VJoySlot}/{sharedEntry.InputName}");
        }

        primaryBinding.SharedWith.Remove(sharedEntry);

        _scExportProfileService?.SaveProfile(_scExportProfile);
        UpdateSharedCells();
        UpdateConflictingBindings();
        _ctx.MarkDirty();

        SetStatus($"Unshared JS{secondarySCInstance} {secondaryInputDisplay} from JS{primarySCInstance} {primaryInputDisplay}");
    }

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
            _ when binding.StartsWith("axis") && int.TryParse(binding.Substring(4), out int idx) => idx,
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
        _scModifierPhysicalButtonNames.Clear();
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

            // Record which physical buttons trigger this modifier key so they can be
            // identified during joystick listen mode — pressing one skips it as the
            // binding target and waits for the actual target button instead.
            foreach (var input in mapping.Inputs)
            {
                if (input.Type != InputType.Button) continue;
                string btnName = $"button{input.Index + 1}";
                _scModifierPhysicalButtonNames.Add(btnName);
                if (!_scModifierButtonToModifiers.TryGetValue(btnName, out var mods))
                {
                    mods = new List<string>();
                    _scModifierButtonToModifiers[btnName] = mods;
                }
                if (!mods.Contains(modName))
                    mods.Add(modName);
            }
        }

        System.Diagnostics.Debug.WriteLine(
            $"[SCBindings] UpdateModifierKeys: {_scModifierKeys.Count} modifier(s), " +
            $"{_scModifierPhysicalButtonNames.Count} modifier button(s): {string.Join(", ", _scModifierPhysicalButtonNames)}");
    }

    /// <summary>
    /// Rebuilds the conflict-links list for the currently selected cell.
    /// Called whenever the selected cell changes.
    /// </summary>
    private void UpdateConflictLinks()
    {
        _scConflictLinks.Clear();
        _scConflictLinkBounds.Clear();
        _scConflictLinkHovered = -1;

        if (_scSelectedActionIndex < 0 || _scFilteredActions is null
            || _scSelectedCell.colIndex < 0 || _scGridColumns is null
            || _scSelectedCell.colIndex >= _scGridColumns.Count)
            return;

        var selectedAction = _scFilteredActions[_scSelectedActionIndex];
        var col = _scGridColumns[_scSelectedCell.colIndex];

        // Find the binding for the selected cell.
        // For shared/rerouted cells there is no direct binding on the secondary column —
        // fall back to the primary binding so conflict links reflect what the cell routes to.
        SCActionBinding? selectedBinding = col.IsPhysical
            ? _scExportProfile.Bindings.FirstOrDefault(b =>
                b.ActionMap == selectedAction.ActionMap && b.ActionName == selectedAction.ActionName &&
                b.DeviceType == SCDeviceType.Joystick && b.PhysicalDeviceId == col.PhysicalDevice!.HidDevicePath)
            : col.IsJoystick
                ? _scExportProfile.Bindings.FirstOrDefault(b =>
                    b.ActionMap == selectedAction.ActionMap && b.ActionName == selectedAction.ActionName &&
                    b.DeviceType == SCDeviceType.Joystick && b.PhysicalDeviceId is null &&
                    b.VJoyDevice == col.VJoyDeviceId)
                : null;

        // If no direct binding, check whether this is a shared/rerouted cell and use the primary binding
        if (selectedBinding is null && col.IsJoystick && !col.IsPhysical)
        {
            string sharedKey = $"{selectedAction.Key}|{col.VJoyDeviceId}";
            if (_scSharedCells.TryGetValue(sharedKey, out var sharedInfo))
            {
                selectedBinding = _scExportProfile.Bindings.FirstOrDefault(b =>
                    b.ActionMap == selectedAction.ActionMap && b.ActionName == selectedAction.ActionName &&
                    b.DeviceType == SCDeviceType.Joystick && b.PhysicalDeviceId is null &&
                    b.VJoyDevice == sharedInfo.PrimaryVJoyDevice);
            }
        }

        if (selectedBinding is null || !_scConflictingBindings.Contains(selectedBinding.Key))
            return;

        // Find all other bindings with the same device + inputName + modifier set.
        // Use the binding's own VJoyDevice (may be the primary device for rerouted cells).
        var conflicts = col.IsPhysical
            ? _scExportProfile.GetConflictingBindings(col.PhysicalDevice!.HidDevicePath,
                selectedBinding.InputName, selectedAction.ActionMap, selectedAction.ActionName, selectedBinding.Modifiers)
            : _scExportProfile.GetConflictingBindings(selectedBinding.VJoyDevice,
                selectedBinding.InputName, selectedAction.ActionMap, selectedAction.ActionName, selectedBinding.Modifiers);

        foreach (var conflict in conflicts)
            _scConflictLinks.Add((conflict.ActionMap, conflict.ActionName));
    }

    /// <summary>
    /// Scrolls the binding list so the given action index is centred in view,
    /// then starts the amber highlight animation on that row.
    /// </summary>
    private void ScrollToAction(int actionIndex)
    {
        if (_scFilteredActions is null || actionIndex < 0 || actionIndex >= _scFilteredActions.Count)
            return;

        // Expand the category if it is collapsed
        var target = _scFilteredActions[actionIndex];
        string categoryName = SCCategoryMapper.GetCategoryNameForAction(target.ActionMap, target.ActionName);
        _scCollapsedCategories.Remove(categoryName);

        // Compute the Y offset of this action row within the content area
        float rowHeight = 28f, rowGap = 2f, categoryHeaderHeight = 28f;
        string? lastCategory = null;
        float contentY = 0;
        for (int i = 0; i <= actionIndex; i++)
        {
            var action = _scFilteredActions[i];
            string cat = SCCategoryMapper.GetCategoryNameForAction(action.ActionMap, action.ActionName);
            if (cat != lastCategory)
            {
                lastCategory = cat;
                contentY += categoryHeaderHeight;
            }
            if (i == actionIndex) break;
            contentY += rowHeight + rowGap;
        }

        // Centre the row vertically in the visible list area
        float rowMid = contentY + rowHeight / 2f;
        float viewHalf = _scBindingsListBounds.Height / 2f;
        float maxScroll = Math.Max(0, _scBindingsContentHeight - _scBindingsListBounds.Height);
        _scBindingsScrollOffset = Math.Clamp(rowMid - viewHalf, 0, maxScroll);

        // Select the row and start the highlight pulse
        _scSelectedActionIndex = actionIndex;
        _scSelectedCell = (actionIndex, _scSelectedCell.colIndex);
        _scConflictHighlightActionIndex = actionIndex;
        _scConflictHighlightStartTime = DateTime.Now;
        UpdateConflictLinks();
        _ctx.MarkDirty();
    }
}
