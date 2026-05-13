using Asteriq.Models;
using Asteriq.Services;

namespace Asteriq.UI.Controllers;

public partial class SCBindingsTabController
{
    private static int ParseButtonIndex(string inputName)
    {
        if (inputName.StartsWith("button", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(inputName[6..], out var n) && n >= 1)
            return n - 1; // 0-based
        return -1;
    }

    private void UpdateDuplicateActionBindings()
    {
        _conflicts.DuplicateActionBindings = _scExportProfile.GetDuplicateJoystickActionKeys();
    }

    /// <summary>
    /// Checks whether any SC binding uses the same physical vJoy button as the NET SWITCH button.
    /// Updates <c>_conflicts.NetworkConflictKeys</c> and <c>_exportBlockedByNetworkConflict</c>.
    /// Called by <c>UpdateConflictingBindings</c> and by MappingsTabController via TabContext callback.
    /// </summary>
    internal void CheckNetworkSwitchConflicts()
    {
        _conflicts.NetworkConflictKeys.Clear();

        var switchCfg = _ctx.ProfileManager.ActiveProfile?.NetworkSwitchButton;
        if (switchCfg is null) return;

        // Find the ButtonMapping that has this physical input as a source
        var profile = _ctx.ProfileManager.ActiveProfile!;
        var physMapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.Type == OutputType.VJoyButton &&
            m.Inputs.Any(inp =>
                inp.Type == InputType.Button &&
                inp.Index == switchCfg.ButtonIndex &&
                inp.DeviceId.Equals(switchCfg.DeviceId, StringComparison.OrdinalIgnoreCase)));

        if (physMapping is null) return;

        uint switchVJoyDevice = physMapping.Output.VJoyDevice;
        // vJoy button output is 0-based Index; SC binding uses "button{index+1}"
        string switchInputName = $"button{physMapping.Output.Index + 1}";

        // Find all SC bindings that use this vJoy button
        foreach (var binding in _scExportProfile.Bindings)
        {
            if (binding.DeviceType != SCDeviceType.Joystick) continue;
            if (binding.PhysicalDeviceId is not null) continue;
            if (binding.VJoyDevice != switchVJoyDevice) continue;
            if (!binding.InputName.Equals(switchInputName, StringComparison.OrdinalIgnoreCase)) continue;

            _conflicts.NetworkConflictKeys.Add(binding.Key);
        }

        _ctx.MarkDirty();
    }

    /// <summary>Public entry point called via TabContext.CheckNetworkSwitchConflicts.</summary>
    public void CheckNetworkSwitchConflictsPublic() => CheckNetworkSwitchConflicts();

    private void UpdateConflictingBindings()
    {
        _conflicts.ConflictingBindings.Clear();

        // Track which inputs are used and by which actions
        // Key: "js1_button5" or "phys:path_button5" etc., Value: list of binding keys that use it
        var inputUsage = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // Check user bindings from export profile (joystick, keyboard, and mouse)
        foreach (var binding in _scExportProfile.Bindings)
        {
            // Include modifiers in the key so rctrl+button13 and button13 are separate slots
            string modPrefix = binding.Modifiers is { Count: > 0 }
                ? string.Join("+", binding.Modifiers.OrderBy(m => m, StringComparer.OrdinalIgnoreCase)) + "+"
                : "";

            string inputKey;
            if (binding.DeviceType == SCDeviceType.Keyboard)
            {
                inputKey = $"kb_{modPrefix}{binding.InputName}";
            }
            else if (binding.DeviceType == SCDeviceType.Mouse)
            {
                inputKey = $"mouse_{modPrefix}{binding.InputName}";
            }
            else if (binding.PhysicalDeviceId is not null)
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
                    _conflicts.ConflictingBindings.Add(actionKey);
                }
            }
        }

        UpdateDuplicateActionBindings();
        CheckNetworkSwitchConflicts();
    }

    private void UpdateConflictLinks()
    {
        _conflicts.ConflictLinks.Clear();
        _conflicts.ConflictLinkBounds.Clear();
        _conflicts.ConflictLinkHovered = -1;

        if (_scSelectedActionIndex < 0 || _scFilteredActions is null
            || _cell.SelectedCell.colIndex < 0 || _grid.Columns is null
            || _cell.SelectedCell.colIndex >= _grid.Columns.Count)
            return;

        var selectedAction = _scFilteredActions[_scSelectedActionIndex];
        var col = _grid.Columns[_cell.SelectedCell.colIndex];

        // Find the binding for the selected cell.
        // For shared/rerouted cells there is no direct binding on the secondary column â€”
        // fall back to the primary binding so conflict links reflect what the cell routes to.
        // Determine the device type for lookup
        SCDeviceType? cellDeviceType = col.IsKeyboard ? SCDeviceType.Keyboard
            : col.IsMouse ? SCDeviceType.Mouse
            : col.IsJoystick ? SCDeviceType.Joystick
            : null;

        SCActionBinding? selectedBinding = col.IsPhysical
            ? _scExportProfile.Bindings.FirstOrDefault(b =>
                b.ActionMap == selectedAction.ActionMap && b.ActionName == selectedAction.ActionName &&
                b.DeviceType == SCDeviceType.Joystick && b.PhysicalDeviceId == col.PhysicalDeviceKey)
            : col.IsJoystick
                ? _scExportProfile.Bindings.FirstOrDefault(b =>
                    b.ActionMap == selectedAction.ActionMap && b.ActionName == selectedAction.ActionName &&
                    b.DeviceType == SCDeviceType.Joystick && b.PhysicalDeviceId is null &&
                    b.VJoyDevice == col.VJoyDeviceId)
                : cellDeviceType.HasValue
                    ? _scExportProfile.Bindings.FirstOrDefault(b =>
                        b.ActionMap == selectedAction.ActionMap && b.ActionName == selectedAction.ActionName &&
                        b.DeviceType == cellDeviceType.Value)
                    : null;

        // If no direct binding, check whether this is a shared/rerouted cell and use the primary binding
        if (selectedBinding is null && col.IsJoystick && !col.IsPhysical)
        {
            string sharedKey = $"{selectedAction.Key}|{col.VJoyDeviceId}";
            if (_conflicts.SharedCells.TryGetValue(sharedKey, out var sharedInfo))
            {
                selectedBinding = _scExportProfile.Bindings.FirstOrDefault(b =>
                    b.ActionMap == selectedAction.ActionMap && b.ActionName == selectedAction.ActionName &&
                    b.DeviceType == SCDeviceType.Joystick && b.PhysicalDeviceId is null &&
                    b.VJoyDevice == sharedInfo.PrimaryVJoyDevice);
            }
        }

        if (selectedBinding is null || !_conflicts.ConflictingBindings.Contains(selectedBinding.Key))
            return;

        // Find all other bindings with the same device + inputName + modifier set.
        // Use the binding's own VJoyDevice (may be the primary device for rerouted cells).
        List<SCActionBinding> conflicts;
        if (col.IsPhysical)
        {
            conflicts = _scExportProfile.GetConflictingBindings(col.PhysicalDeviceKey,
                selectedBinding.InputName, selectedAction.ActionMap, selectedAction.ActionName, selectedBinding.Modifiers);
        }
        else if (col.IsKeyboard || col.IsMouse)
        {
            conflicts = _scExportProfile.GetConflictingBindings(selectedBinding.DeviceType,
                selectedBinding.InputName, selectedAction.ActionMap, selectedAction.ActionName, selectedBinding.Modifiers);
        }
        else
        {
            conflicts = _scExportProfile.GetConflictingBindings(selectedBinding.VJoyDevice,
                selectedBinding.InputName, selectedAction.ActionMap, selectedAction.ActionName, selectedBinding.Modifiers);
        }

        foreach (var conflict in conflicts)
            _conflicts.ConflictLinks.Add((conflict.ActionMap, conflict.ActionName));
    }

    /// <summary>
    /// Scrolls the binding list so the given action index is centred in view,
    /// then starts the amber highlight animation on that row.
    /// </summary>
}