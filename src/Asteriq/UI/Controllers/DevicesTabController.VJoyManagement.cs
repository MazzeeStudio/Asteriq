using System.Text.RegularExpressions;
using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class DevicesTabController
{
    private void RemoveVJoyDevice(uint vjoyId)
    {
        // Prevent removal of the last vJoy device — vJoy requires at least one device
        int existingCount = _ctx.VJoyDevices.Count(v => v.Exists);
        if (existingCount <= 1)
        {
            FUIMessageBox.ShowError(_ctx.OwnerForm,
                "Cannot remove the last vJoy device.\nvJoy requires at least one device to be configured.",
                "Remove Blocked");
            return;
        }

        // Build per-device mapping breakdown for the warning dialog
        var profile = _ctx.ProfileManager.ActiveProfile;
        int mappingCount = 0;
        string[]? detailLines = null;

        if (profile is not null)
        {
            // Count total affected mappings
            mappingCount =
                profile.AxisMappings.Count(m => m.Output.VJoyDevice == vjoyId) +
                profile.ButtonMappings.Count(m => m.Output.VJoyDevice == vjoyId) +
                profile.HatMappings.Count(m => m.Output.VJoyDevice == vjoyId) +
                profile.AxisToButtonMappings.Count(m => m.Output.VJoyDevice == vjoyId) +
                profile.ButtonToAxisMappings.Count(m => m.Output.VJoyDevice == vjoyId);

            if (mappingCount > 0)
            {
                // Tally how many affected mappings reference each physical device
                var deviceCounts = new Dictionary<string, int>();
                void Tally(IEnumerable<string> deviceIds)
                {
                    foreach (var id in deviceIds)
                    {
                        deviceCounts.TryGetValue(id, out int c);
                        deviceCounts[id] = c + 1;
                    }
                }

                foreach (var m in profile.AxisMappings.Where(m => m.Output.VJoyDevice == vjoyId))
                    Tally(m.Inputs.Select(i => i.DeviceId).Distinct());
                foreach (var m in profile.ButtonMappings.Where(m => m.Output.VJoyDevice == vjoyId))
                    Tally(m.Inputs.Select(i => i.DeviceId).Distinct());
                foreach (var m in profile.HatMappings.Where(m => m.Output.VJoyDevice == vjoyId))
                    Tally(m.Inputs.Select(i => i.DeviceId).Distinct());
                foreach (var m in profile.AxisToButtonMappings.Where(m => m.Output.VJoyDevice == vjoyId))
                    Tally(m.Inputs.Select(i => i.DeviceId).Distinct());
                foreach (var m in profile.ButtonToAxisMappings.Where(m => m.Output.VJoyDevice == vjoyId))
                    Tally(m.Inputs.Select(i => i.DeviceId).Distinct());

                var deviceLookup = _ctx.Devices.Concat(_ctx.DisconnectedDevices)
                    .DistinctBy(d => d.InstanceGuid)
                    .ToDictionary(d => d.InstanceGuid.ToString(), d => d.Name);

                detailLines = deviceCounts
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv =>
                    {
                        string name = deviceLookup.TryGetValue(kv.Key, out var n) ? n : "Unknown device";
                        int c = kv.Value;
                        return $"{name}  \u2014  {c} mapping{(c == 1 ? "" : "s")}";
                    })
                    .ToArray();
            }
        }

        string message = mappingCount > 0
            ? $"Remove vJoy Slot {vjoyId}?\n\nThis will delete the virtual device from the driver.\n" +
              $"{mappingCount} mapping{(mappingCount == 1 ? "" : "s")} targeting this device will also be removed.\n\n" +
              "An admin prompt will appear."
            : $"Remove vJoy Slot {vjoyId}?\n\nThis will delete the virtual device from the driver.\n\nAn admin prompt will appear.";

        bool confirmed = FUIMessageBox.ShowDestructiveConfirm(_ctx.OwnerForm, message, "Remove vJoy Device", "Remove", detailLines);
        if (!confirmed) return;

        string? configPath = DriverSetupManager.GetVJoyConfigPath();
        if (configPath is null)
        {
            FUIMessageBox.ShowWarning(_ctx.OwnerForm,
                "vJoyConfig.exe was not found in your vJoy installation.",
                "vJoy Not Found");
            return;
        }

        // Clean up mappings before deleting the device so stale references never persist
        if (profile is not null && mappingCount > 0)
        {
            profile.AxisMappings.RemoveAll(m => m.Output.VJoyDevice == vjoyId);
            profile.ButtonMappings.RemoveAll(m => m.Output.VJoyDevice == vjoyId);
            profile.HatMappings.RemoveAll(m => m.Output.VJoyDevice == vjoyId);
            profile.AxisToButtonMappings.RemoveAll(m => m.Output.VJoyDevice == vjoyId);
            profile.ButtonToAxisMappings.RemoveAll(m => m.Output.VJoyDevice == vjoyId);
            _ctx.ProfileManager.SaveActiveProfile();
            _ctx.OnMappingsChanged();
        }

        // Release the device if our process has it acquired (prevents driver refusing deletion)
        _ctx.VJoyService.ReleaseDevice(vjoyId);

        int exitCode;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = configPath,
                Arguments = $"-d {vjoyId}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit(10000);
            exitCode = process?.ExitCode ?? -1;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return; // User cancelled UAC — mappings already cleaned, that's fine
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            FUIMessageBox.ShowError(_ctx.OwnerForm,
                $"Failed to remove vJoy device:\n{ex.Message}",
                "Removal Failed");
            return;
        }

        if (exitCode != 0)
        {
            FUIMessageBox.ShowError(_ctx.OwnerForm,
                $"vJoyConfig.exe reported failure (exit code {exitCode}).\n\n" +
                "The device may still exist. Try using the vJoy Configuration tool directly.",
                "Removal Failed");
            return;
        }

        // Update vJoy list — do NOT call RefreshDevices() here because SDL2 still reports
        // the device until the OS sends a device-removed notification
        _ctx.RefreshVJoyDevices?.Invoke();
        _ctx.SelectedDevice = -1;
        _ctx.MarkDirty();
    }

    /// <summary>
    /// Reconfigure a vJoy device to match its paired physical device's capabilities.
    /// Deletes and recreates the vJoy slot via vJoyConfig.exe (requires UAC).
    /// Preserves all existing mappings by keeping the same slot ID.
    /// </summary>
    private void ConfigureVJoyDevice(uint vjoyId)
    {
        var vjoyDevice = _ctx.VJoyDevices.FirstOrDefault(v => v.Id == vjoyId);
        if (vjoyDevice is null) return;

        string? configPath = DriverSetupManager.GetVJoyConfigPath();
        if (configPath is null)
        {
            FUIMessageBox.ShowWarning(_ctx.OwnerForm,
                "vJoyConfig.exe was not found in your vJoy installation.\n\n" +
                "Please ensure vJoy is installed correctly.",
                "vJoy Not Found");
            return;
        }

        // Pre-populate from current vJoy device config
        var currentFlags = new List<string>();
        if (vjoyDevice.HasAxisX) currentFlags.Add("X");
        if (vjoyDevice.HasAxisY) currentFlags.Add("Y");
        if (vjoyDevice.HasAxisZ) currentFlags.Add("Z");
        if (vjoyDevice.HasAxisRX) currentFlags.Add("RX");
        if (vjoyDevice.HasAxisRY) currentFlags.Add("RY");
        if (vjoyDevice.HasAxisRZ) currentFlags.Add("RZ");
        if (vjoyDevice.HasSlider0) currentFlags.Add("SL0");
        if (vjoyDevice.HasSlider1) currentFlags.Add("SL1");

        int currentPovs = vjoyDevice.ContPovCount + vjoyDevice.DiscPovCount;
        bool currentContinuous = vjoyDevice.ContPovCount > 0;

        var config = VJoyConfigDialog.ShowConfig(_ctx.OwnerForm,
            $"vJoy Device {vjoyId}", currentFlags, vjoyDevice.ButtonCount, currentPovs, currentContinuous);
        if (config is null) return;

        _ctx.VJoyService.ReleaseDevice(vjoyId);

        string args = $"{vjoyId} -f";
        if (config.AxisFlags.Count > 0)
            args += $" -a {string.Join(" ", config.AxisFlags)}";
        if (config.ButtonCount > 0)
            args += $" -b {config.ButtonCount}";
        if (config.PovCount > 0)
            args += $" -p {config.PovCount}";

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = configPath,
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };

            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit(15000);

            int exitCode = process?.ExitCode ?? -1;
            if (exitCode != 0)
            {
                FUIMessageBox.ShowError(_ctx.OwnerForm,
                    $"vJoyConfig.exe reported failure (exit code {exitCode}).\n\n" +
                    "The device configuration may not have changed.",
                    "Configuration Failed");
                return;
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return; // User cancelled UAC
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            FUIMessageBox.ShowError(_ctx.OwnerForm,
                $"Failed to reconfigure vJoy device:\n{ex.Message}",
                "Configuration Failed");
            return;
        }

        _ctx.RefreshVJoyDevices?.Invoke();
        _ctx.RefreshDevices?.Invoke();
        _ctx.InvalidateCanvas();
    }

    private void AddVJoyDevice()
    {
        string? configPath = DriverSetupManager.GetVJoyConfigPath();
        if (configPath is null)
        {
            FUIMessageBox.ShowWarning(_ctx.OwnerForm,
                "vJoyConfig.exe was not found in your vJoy installation.\n\n" +
                "Please ensure vJoy is installed correctly.",
                "vJoy Not Found");
            return;
        }

        // Find the next available vJoy slot (1-16)
        var existingIds = _ctx.VJoyDevices.Select(v => v.Id).ToHashSet();
        uint newId = 0;
        for (uint id = 1; id <= 16; id++)
        {
            if (!existingIds.Contains(id))
            {
                newId = id;
                break;
            }
        }

        if (newId == 0)
        {
            FUIMessageBox.ShowWarning(_ctx.OwnerForm,
                "All 16 vJoy device slots are in use.",
                "No Slots Available");
            return;
        }

        var config = VJoyConfigDialog.ShowConfig(_ctx.OwnerForm, $"vJoy Device {newId}");
        if (config is null) return;

        string args = $"{newId} -f";
        if (config.AxisFlags.Count > 0)
            args += $" -a {string.Join(" ", config.AxisFlags)}";
        if (config.ButtonCount > 0)
            args += $" -b {config.ButtonCount}";
        if (config.PovCount > 0)
            args += $" -p {config.PovCount}";

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = configPath,
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };

            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit(15000);

            int exitCode = process?.ExitCode ?? -1;
            if (exitCode != 0)
            {
                FUIMessageBox.ShowError(_ctx.OwnerForm,
                    $"vJoyConfig.exe reported failure (exit code {exitCode}).\n\n" +
                    "The device may not have been created.",
                    "Creation Failed");
                return;
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return; // User cancelled UAC
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            FUIMessageBox.ShowError(_ctx.OwnerForm,
                $"Failed to create vJoy device:\n{ex.Message}",
                "Creation Failed");
            return;
        }

        _ctx.RefreshVJoyDevices?.Invoke();
        _ctx.RefreshDevices?.Invoke();
        _ctx.InvalidateCanvas();
    }

    private void SyncVJoyToPhysical(uint vjoyId)
    {
        var physical = GetPrimaryPhysicalDevice(vjoyId);
        if (physical is null) return;

        string? configPath = DriverSetupManager.GetVJoyConfigPath();
        if (configPath is null)
        {
            FUIMessageBox.ShowWarning(_ctx.OwnerForm,
                "vJoyConfig.exe was not found in your vJoy installation.\n\n" +
                "Please ensure vJoy is installed correctly.",
                "vJoy Not Found");
            return;
        }

        int buttonCount = Math.Max(physical.ButtonCount, 1);
        int povCount = physical.HatCount;
        var axisFlags = VJoyDirectInputOrderService.GetVJoyAxisFlagsForDevice(physical);

        string message = $"Match vJoy Slot {vjoyId} to {physical.Name}?\n\n" +
                         $"Axes: {string.Join(", ", axisFlags)}\n" +
                         $"Buttons: {buttonCount}\n" +
                         $"Hats: {povCount}\n\n" +
                         "Existing mappings will be preserved.\nAn admin prompt will appear.";

        int result = FUIMessageBox.Show(_ctx.OwnerForm, message, "Match Physical Device",
            FUIMessageBox.MessageBoxType.Question, "Match", "Cancel");
        if (result != 0) return;

        // Release the device before reconfiguring
        _ctx.VJoyService.ReleaseDevice(vjoyId);

        // Build vJoyConfig args
        string args = $"{vjoyId} -f";
        if (axisFlags.Count > 0)
            args += $" -a {string.Join(" ", axisFlags)}";
        if (buttonCount > 0)
            args += $" -b {buttonCount}";
        if (povCount > 0)
            args += $" -p {povCount}";

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = configPath,
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };

            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit(15000);

            int exitCode = process?.ExitCode ?? -1;
            if (exitCode != 0)
            {
                FUIMessageBox.ShowError(_ctx.OwnerForm,
                    $"vJoyConfig.exe reported failure (exit code {exitCode}).\n\n" +
                    "The device configuration may not have changed.",
                    "Sync Failed");
                return;
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return; // User cancelled UAC
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            FUIMessageBox.ShowError(_ctx.OwnerForm,
                $"Failed to reconfigure vJoy device:\n{ex.Message}",
                "Sync Failed");
            return;
        }

        // Re-enumerate and refresh
        _ctx.RefreshVJoyDevices?.Invoke();
        _ctx.RefreshDevices?.Invoke();
        _ctx.MarkDirty();
    }



    /// <summary>
    /// Gets the vJoy slot ID for the currently selected virtual device.
    /// Uses VJoyDevices list by position first (most reliable); falls back to name regex.
    /// </summary>
    private uint GetSelectedVJoyId()
    {
        if (_devCat.Active != 1 || _ctx.SelectedDevice < 0 || _ctx.SelectedDevice >= _ctx.Devices.Count)
            return 0;
        var device = _ctx.Devices[_ctx.SelectedDevice];
        if (!device.IsVirtual) return 0;

        // Primary: match by index within virtual devices to VJoyDevices list
        var virtualDevices = _ctx.Devices.Where(d => d.IsVirtual).ToList();
        int virtualIndex = virtualDevices.IndexOf(device);
        if (virtualIndex >= 0 && virtualIndex < _ctx.VJoyDevices.Count)
            return _ctx.VJoyDevices[virtualIndex].Id;

        // Fallback: parse slot number from device name (e.g. "vJoy Device 1" → 1)
        var match = s_vjoySlotNumberRegex.Match(device.Name);
        return match.Success && uint.TryParse(match.Value, out uint id) ? id : 0;
    }

    /// <summary>
    /// Gets the primary physical device mapped to the currently selected vJoy slot.
    /// Returns null if nothing is mapped.
    /// </summary>
    private PhysicalDeviceInfo? GetPrimaryPhysicalDevice(uint vjoyId)
    {
        if (vjoyId == 0) return null;
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return null;
        var primaryGuid = profile.GetPrimaryDeviceForVJoy(vjoyId);
        if (string.IsNullOrEmpty(primaryGuid)) return null;
        return _ctx.Devices.FirstOrDefault(d =>
            !d.IsVirtual && d.InstanceGuid.ToString().Equals(primaryGuid, StringComparison.OrdinalIgnoreCase));
    }

}