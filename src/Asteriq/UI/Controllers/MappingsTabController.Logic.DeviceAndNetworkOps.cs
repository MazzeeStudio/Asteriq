using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
    private void ClearDeviceMappings()
    {
        if (_ctx.SelectedDevice < 0 || _ctx.SelectedDevice >= _ctx.Devices.Count) return;

        var physicalDevice = _ctx.Devices[_ctx.SelectedDevice];
        if (physicalDevice.IsVirtual) return;

        int clearResult = FUIMessageBox.Show(_ctx.OwnerForm,
            $"Remove all mappings for {physicalDevice.Name}?\n\nThis will remove axis, button, and hat mappings from all vJoy devices.",
            "Clear Mappings", FUIMessageBox.MessageBoxType.Question, "Clear", "Cancel");

        if (clearResult != 0) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        string deviceId = physicalDevice.InstanceGuid.ToString();

        // Remove mappings from this device. For merge mappings (multiple inputs),
        // only strip this device's input — don't destroy the whole mapping.
        int axisRemoved = RemoveMappingsForDevice(profile.AxisMappings, deviceId);
        int buttonRemoved = RemoveMappingsForDevice(profile.ButtonMappings, deviceId);
        int hatRemoved = RemoveMappingsForDevice(profile.HatMappings, deviceId);

        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.OnMappingsChanged();

        FUIMessageBox.ShowInfo(_ctx.OwnerForm,
            $"Removed {axisRemoved} axis, {buttonRemoved} button, and {hatRemoved} hat mappings.",
            "Mappings Cleared");

        _ctx.InvalidateCanvas();
    }

    /// <summary>
    /// Removes a device from a mapping list safely. For sole-input mappings, removes
    /// the entire mapping. For merge mappings (multiple inputs), strips only this
    /// device's input and keeps the mapping intact. Returns the count of fully removed mappings.
    /// </summary>
    private static int RemoveMappingsForDevice<T>(List<T> mappings, string deviceId) where T : Mapping
    {
        // First, strip device from merge mappings (multiple inputs)
        foreach (var m in mappings.Where(m => m.Inputs.Count > 1))
            m.Inputs.RemoveAll(i => i.DeviceId == deviceId);

        // Then remove mappings where this was the sole input (now empty after strip, or was always sole)
        return mappings.RemoveAll(m =>
            m.Inputs.Count == 0 ||
            (m.Inputs.Count == 1 && m.Inputs[0].DeviceId == deviceId));
    }

    /// <summary>
    /// Remove a disconnected device completely from the app's data.
    /// This clears all mappings and removes it from the disconnected devices list.
    /// </summary>
    private void RemoveDisconnectedDevice()
    {
        if (_ctx.SelectedDevice < 0 || _ctx.SelectedDevice >= _ctx.Devices.Count) return;

        var device = _ctx.Devices[_ctx.SelectedDevice];
        if (device.IsConnected || device.IsVirtual) return; // Only works for disconnected physical devices

        int removeResult = FUIMessageBox.Show(_ctx.OwnerForm,
            $"Permanently remove {device.Name}?\n\n" +
            "This will:\n" +
            "• Clear all axis, button, and hat mappings\n" +
            "• Remove the device from the disconnected list\n\n" +
            "This cannot be undone.",
            "Remove Device", FUIMessageBox.MessageBoxType.Question, "Remove", "Cancel");

        if (removeResult != 0) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        string deviceId = device.InstanceGuid.ToString();

        // Remove mappings from this device (preserving merge mappings for other devices)
        int axisRemoved = 0, buttonRemoved = 0, hatRemoved = 0;
        if (profile is not null)
        {
            axisRemoved = RemoveMappingsForDevice(profile.AxisMappings, deviceId);
            buttonRemoved = RemoveMappingsForDevice(profile.ButtonMappings, deviceId);
            hatRemoved = RemoveMappingsForDevice(profile.HatMappings, deviceId);
            _ctx.ProfileManager.SaveActiveProfile();
            _ctx.OnMappingsChanged();
        }

        // Remove from disconnected devices list
        _ctx.DisconnectedDevices.RemoveAll(d => d.InstanceGuid == device.InstanceGuid);
        _ctx.SaveDisconnectedDevices!();

        // Refresh and update selection
        _ctx.RefreshDevices();
        if (_ctx.SelectedDevice >= _ctx.Devices.Count)
        {
            _ctx.SelectedDevice = Math.Max(0, _ctx.Devices.Count - 1);
        }

        FUIMessageBox.ShowInfo(_ctx.OwnerForm,
            $"Device removed.\n\nCleared {axisRemoved} axis, {buttonRemoved} button, and {hatRemoved} hat mappings.",
            "Device Removed");

        _ctx.InvalidateCanvas();
    }

    private void AssignSwitchButtonForSelectedRow()
    {
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null || _selectedMappingRow < 0 || _mappingCategory != 0) return;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;
        if (!_ctx.AppSettings.NetworkEnabled) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];

        // Find the button mapping for the selected output slot
        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.Type == OutputType.VJoyButton &&
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == _selectedMappingRow);

        // Find the first physical button input source
        var inp = mapping?.Inputs.FirstOrDefault(i => i.Type == InputType.Button);

        if (inp is null)
        {
            _highlight.FlashText = "Assign a physical button first";
            _highlight.FlashTicks = Environment.TickCount64;
            _ctx.MarkDirty();
            return;
        }

        // Resolve SDL2 DeviceIndex from the device list (best-effort; stable ID is DeviceId)
        int devIndex = _ctx.Devices
            .FirstOrDefault(d => d.InstanceGuid.ToString()
                .Equals(inp.DeviceId, StringComparison.OrdinalIgnoreCase))
            ?.DeviceIndex ?? 0;

        profile.NetworkSwitchButton = new NetworkSwitchConfig
        {
            DeviceIndex = devIndex,
            ButtonIndex = inp.Index,
            DeviceId = inp.DeviceId,
            DisplayName = $"{inp.DeviceName} Button {inp.Index + 1}"
        };

        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.CheckNetworkSwitchConflicts?.Invoke();
        _ctx.MarkDirty();
    }

    /// <summary>
    /// Removes the network switch button assignment from the active profile.
    /// </summary>
    private void ClearNetworkSwitchButton()
    {
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null || profile.NetworkSwitchButton is null) return;

        profile.NetworkSwitchButton = null;
        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.CheckNetworkSwitchConflicts?.Invoke();
        _ctx.MarkDirty();
    }

    /// <summary>
    /// Describes the SC share that has rerouted a vJoy slot to fire a primary action's button.
    /// When non-null, the Mappings tab should render the slot as read-only with a deep-link
    /// to the SC Bindings tab instead of the normal mapping editor.
    /// </summary>
    private sealed class SharedSlotInfo
    {
        public required string ActionName;
        public required string ActionMap;
        public required string ActionDisplayName;
        public required uint PrimaryVJoyDevice;
        public required int PrimaryButtonIndex;
    }

    /// <summary>
    /// Returns share info for every SC action that has rerouted this vJoy slot. Returns an
    /// empty list if the slot is standalone. Mirrors the reroute gate in PerformShare: only
    /// JS primaries with no modifiers are considered shared-away.
    /// </summary>
    private List<SharedSlotInfo> GetSharedSlotInfos(uint vjoyDevice, int buttonIndex)
    {
        var result = new List<SharedSlotInfo>();
        var profile = _ctx.GetActiveSCExportProfile?.Invoke();
        if (profile is null) return result;

        foreach (var binding in profile.Bindings)
        {
            if (binding.DeviceType != SCDeviceType.Joystick) continue;
            if (binding.Modifiers.Count > 0) continue;

            int primaryBtn = ParseSCButtonIndex(binding.InputName);
            if (primaryBtn < 0) continue;

            foreach (var shared in binding.SharedWith)
            {
                if (shared.VJoySlot != vjoyDevice) continue;
                int sharedBtn = ParseSCButtonIndex(shared.InputName);
                if (sharedBtn != buttonIndex) continue;

                result.Add(new SharedSlotInfo
                {
                    ActionName = binding.ActionName,
                    ActionMap = binding.ActionMap,
                    ActionDisplayName = SCCategoryMapper.FormatActionName(binding.ActionName),
                    PrimaryVJoyDevice = binding.VJoyDevice,
                    PrimaryButtonIndex = primaryBtn,
                });
                break; // a single binding can only target one slot once; move on
            }
        }
        return result;
    }

    /// <summary>
    /// Parses an SC button input name like "button21" into a 0-based index. Returns -1 for
    /// non-button inputs.
    /// </summary>
    private static int ParseSCButtonIndex(string inputName)
    {
        if (inputName.StartsWith("button", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(inputName.AsSpan(6), out var n) && n >= 1)
            return n - 1;
        return -1;
    }

}