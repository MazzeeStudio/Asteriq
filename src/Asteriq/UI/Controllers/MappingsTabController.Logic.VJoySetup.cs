using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
    private void CreateOneToOneMappings()
    {
        // Validate selection
        if (_ctx.SelectedDevice < 0 || _ctx.SelectedDevice >= _ctx.Devices.Count) return;

        var physicalDevice = _ctx.Devices[_ctx.SelectedDevice];
        if (physicalDevice.IsVirtual) return; // Only map physical devices

        // Ensure vJoy devices are loaded
        if (_ctx.VJoyDevices.Count == 0)
        {
            _ctx.VJoyDevices = _ctx.VJoyService.EnumerateDevices();
        }

        // Check if we have any vJoy devices
        if (_ctx.VJoyDevices.Count == 0)
        {
            ShowVJoyConfigurationHelp(physicalDevice, noDevices: true);
            return;
        }

        // Show vJoy device selection dialog
        var vjoyDevice = ShowVJoyDeviceSelectionDialog(physicalDevice);
        if (vjoyDevice is null) return; // User cancelled

        // Update selected vJoy device index
        _ctx.SelectedVJoyDeviceIndex = _ctx.VJoyDevices.IndexOf(vjoyDevice);

        // Ensure we have an active profile
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null)
        {
            profile = _ctx.ProfileManager.CreateAndActivateProfile($"1:1 - {physicalDevice.Name}");
        }

        // Build device ID for InputSource (using GUID)
        string deviceId = physicalDevice.InstanceGuid.ToString();

        // Remove ALL existing mappings from this device (any vJoy target).
        // For merge mappings (multiple inputs), strip this device's input but keep the mapping.
        RemoveMappingsForDevice(profile.AxisMappings, deviceId);
        RemoveMappingsForDevice(profile.ButtonMappings, deviceId);
        RemoveMappingsForDevice(profile.HatMappings, deviceId);
        RemoveMappingsForDevice(profile.AxisToButtonMappings, deviceId);
        RemoveMappingsForDevice(profile.ButtonToAxisMappings, deviceId);

        // Create axis mappings using sequential index mapping.
        // SDL2 and DirectInput can enumerate axes in different orders, so we map
        // by position: SDL2 axis 0 → vJoy's first enabled axis, axis 1 → second, etc.
        // The vJoy device is configured with the correct axis TYPES via DI detection
        // (done during device creation / Match Physical), so the types are already correct.
        var vjoyAxisIndices = GetVJoyAxisIndices(vjoyDevice);

        // Throttles and pedals use end-only deadzone (0-100%), joysticks use centered (-100% to +100%)
        string deviceType = MainForm.DetectDeviceType(physicalDevice.Name);
        var deadzoneMode = deviceType is "Throttle" or "Pedals"
            ? DeadzoneMode.EndOnly
            : DeadzoneMode.Centered;

        LogMapping($"=== 1:1 Mapping for {physicalDevice.Name} ===");
        LogMapping($"Device: {physicalDevice.Name}, AxisCount: {physicalDevice.AxisCount}, Type: {deviceType}");

        int axesToMap = Math.Min(physicalDevice.AxisCount, vjoyAxisIndices.Count);
        for (int i = 0; i < axesToMap; i++)
        {
            int vjoyAxisIndex = vjoyAxisIndices[i];
            string vjoyAxisName = GetVJoyAxisName(vjoyAxisIndex);
            string physicalAxisName = $"Axis {i}";

            LogMapping($"Mapping axis {i} -> vJoy axis {vjoyAxisIndex} ({vjoyAxisName})");

            var mapping = new AxisMapping
            {
                Name = $"{physicalDevice.Name} {physicalAxisName} -> vJoy {vjoyDevice.Id} {vjoyAxisName}",
                Inputs = new List<InputSource>
                {
                    new InputSource
                    {
                        DeviceId = deviceId,
                        DeviceName = physicalDevice.Name,
                        Type = InputType.Axis,
                        Index = i
                    }
                },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyAxis,
                    VJoyDevice = vjoyDevice.Id,
                    Index = vjoyAxisIndex
                },
                Curve = new AxisCurve { Type = CurveType.Linear, DeadzoneMode = deadzoneMode }
            };
            profile.AxisMappings.Add(mapping);
        }

        // Create button mappings
        int buttonsToMap = Math.Min(physicalDevice.ButtonCount, vjoyDevice.ButtonCount);

        for (int i = 0; i < buttonsToMap; i++)
        {
            var mapping = new ButtonMapping
            {
                Name = $"{physicalDevice.Name} Btn {i + 1} -> vJoy {vjoyDevice.Id} Btn {i + 1}",
                Inputs = new List<InputSource>
                {
                    new InputSource
                    {
                        DeviceId = deviceId,
                        DeviceName = physicalDevice.Name,
                        Type = InputType.Button,
                        Index = i
                    }
                },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyButton,
                    VJoyDevice = vjoyDevice.Id,
                    Index = i
                },
                Mode = ButtonMode.Normal
            };
            profile.ButtonMappings.Add(mapping);
        }

        // Create hat/POV mappings
        int hatsToMap = Math.Min(physicalDevice.HatCount, vjoyDevice.ContPovCount + vjoyDevice.DiscPovCount);

        for (int i = 0; i < hatsToMap; i++)
        {
            var mapping = new HatMapping
            {
                Name = $"{physicalDevice.Name} Hat {i} -> vJoy {vjoyDevice.Id} POV {i}",
                Inputs = new List<InputSource>
                {
                    new InputSource
                    {
                        DeviceId = deviceId,
                        DeviceName = physicalDevice.Name,
                        Type = InputType.Hat,
                        Index = i
                    }
                },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyPov,
                    VJoyDevice = vjoyDevice.Id,
                    Index = i
                },
                UseContinuous = vjoyDevice.ContPovCount > i
            };
            profile.HatMappings.Add(mapping);
        }

        // Create/update device assignment so SaveDeviceOrder can track this device
        profile.DeviceAssignments.RemoveAll(a =>
            a.PhysicalDevice.Guid.Equals(deviceId, StringComparison.OrdinalIgnoreCase));

        var (vid, pid) = DeviceMatchingService.ExtractVidPidFromSdlGuid(physicalDevice.InstanceGuid);

        profile.DeviceAssignments.Add(new DeviceAssignment
        {
            PhysicalDevice = new PhysicalDeviceRef
            {
                Name = physicalDevice.Name,
                Guid = deviceId,
                VidPid = vid > 0 ? $"{vid:X4}:{pid:X4}" : ""
            },
            VJoyDevice = vjoyDevice.Id
        });

        // Save the profile
        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.OnMappingsChanged();

        // Refresh profiles list
        _ctx.Profiles = _ctx.ProfileRepository.ListProfiles();

        // Note: Tab switch to Mappings is handled by the caller (DevicesTabController)
        _ctx.InvalidateCanvas();
    }

    /// <summary>
    /// Count the number of axes configured on a vJoy device
    /// </summary>
    private static int CountVJoyAxes(VJoyDeviceInfo vjoy)
    {
        int count = 0;
        if (vjoy.HasAxisX) count++;
        if (vjoy.HasAxisY) count++;
        if (vjoy.HasAxisZ) count++;
        if (vjoy.HasAxisRX) count++;
        if (vjoy.HasAxisRY) count++;
        if (vjoy.HasAxisRZ) count++;
        if (vjoy.HasSlider0) count++;
        if (vjoy.HasSlider1) count++;
        return count;
    }

    /// <summary>
    /// Get the list of available vJoy axis indices in standard order.
    /// Returns indices 0-7 corresponding to X, Y, Z, RX, RY, RZ, Slider0, Slider1.
    /// </summary>
    private static List<int> GetVJoyAxisIndices(VJoyDeviceInfo vjoy)
    {
        var indices = new List<int>();
        if (vjoy.HasAxisX) indices.Add(0);   // X
        if (vjoy.HasAxisY) indices.Add(1);   // Y
        if (vjoy.HasAxisZ) indices.Add(2);   // Z
        if (vjoy.HasAxisRX) indices.Add(3);  // RX
        if (vjoy.HasAxisRY) indices.Add(4);  // RY
        if (vjoy.HasAxisRZ) indices.Add(5);  // RZ
        if (vjoy.HasSlider0) indices.Add(6); // Slider0
        if (vjoy.HasSlider1) indices.Add(7); // Slider1
        return indices;
    }

    /// <summary>
    /// Programmatically create a new vJoy device using vJoyConfig.exe CLI,
    /// configured to match the physical device's capabilities.
    /// Returns the created device on success, or null if cancelled or failed.
    /// </summary>
    private VJoyDeviceInfo? CreateVJoyDeviceForPhysical(PhysicalDeviceInfo physical)
    {
        string? configPath = DriverSetupManager.GetVJoyConfigPath();
        if (configPath is null)
        {
            FUIMessageBox.ShowWarning(_ctx.OwnerForm,
                "vJoyConfig.exe was not found in your vJoy installation.\n\n" +
                "Please ensure vJoy is installed correctly.",
                "vJoy Not Found");
            return null;
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
            return null;
        }

        // Show configuration dialog pre-populated from the physical device's detected axes
        var detectedFlags = VJoyDirectInputOrderService.GetVJoyAxisFlagsForDevice(physical);
        int detectedButtons = Math.Max(physical.ButtonCount, 1);
        int detectedPovs = physical.HatCount;

        var config = VJoyConfigDialog.ShowConfig(_ctx.OwnerForm, physical.Name,
            detectedFlags, detectedButtons, detectedPovs);
        if (config is null) return null;

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
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User cancelled the UAC elevation prompt
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            FUIMessageBox.ShowError(_ctx.OwnerForm,
                $"Failed to create vJoy device:\n{ex.Message}",
                "vJoy Creation Failed");
            return null;
        }

        // Re-enumerate vJoy list via MainForm callback so _vjoyDevices stays in sync with SyncTabContext()
        _ctx.RefreshVJoyDevices?.Invoke();
        // Also rescan SDL2 so the new virtual joystick appears in the Devices tab
        _ctx.RefreshDevices?.Invoke();
        return _ctx.VJoyDevices.FirstOrDefault(v => v.Id == newId);
    }

    /// <summary>
    /// Show help dialog for vJoy configuration with recommended settings
    /// </summary>
    private void ShowVJoyConfigurationHelp(PhysicalDeviceInfo physical, bool noDevices)
    {
        string message;
        if (noDevices)
        {
            message = "No vJoy devices are configured.\n\n";
        }
        else
        {
            message = "No vJoy device has enough capacity for this physical device.\n\n";
        }

        message += $"To create a 1:1 mapping for {physical.Name}, configure a vJoy device with:\n\n" +
                   $"  Axes: {physical.AxisCount} (X, Y, Z, Rx, Ry, Rz, Slider, Dial)\n" +
                   $"  Buttons: {physical.ButtonCount}\n" +
                   $"  POV Hats: {physical.HatCount} (Continuous recommended)\n\n" +
                   "Would you like to open the vJoy Configuration utility?";

        int configResult = FUIMessageBox.Show(_ctx.OwnerForm, message, "vJoy Configuration Required",
            FUIMessageBox.MessageBoxType.Question, "Open", "Cancel");

        if (configResult == 0)
        {
            LaunchVJoyConfigurator();
        }
    }

    /// <summary>
    /// Attempt to launch the vJoy configuration utility
    /// </summary>
    private void LaunchVJoyConfigurator()
    {
        // Common vJoy installation paths
        string[] possiblePaths = new[]
        {
            @"C:\Program Files\vJoy\x64\vJoyConf.exe",
            @"C:\Program Files\vJoy\x86\vJoyConf.exe",
            @"C:\Program Files (x86)\vJoy\x64\vJoyConf.exe",
            @"C:\Program Files (x86)\vJoy\x86\vJoyConf.exe"
        };

        string? vjoyConfPath = possiblePaths.FirstOrDefault(File.Exists);

        if (vjoyConfPath is not null)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = vjoyConfPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
            {
                FUIMessageBox.ShowError(_ctx.OwnerForm,
                    $"Failed to launch vJoy Configurator:\n{ex.Message}",
                    "Launch Failed");
            }
        }
        else
        {
            FUIMessageBox.ShowWarning(_ctx.OwnerForm,
                "vJoy Configuration utility (vJoyConf.exe) was not found.\n\n" +
                "Please install vJoy from:\nhttps://github.com/jshafer817/vJoy/releases\n\n" +
                "Or manually run vJoyConf.exe from your vJoy installation folder.",
                "vJoy Not Found");
        }
    }

    /// <summary>
    /// Show a dialog to select a vJoy device for 1:1 mapping.
    /// Returns the selected device or null if cancelled.
    /// </summary>
    private VJoyDeviceInfo? ShowVJoyDeviceSelectionDialog(PhysicalDeviceInfo physicalDevice)
    {
        var items = new List<FUISelectionDialog.SelectionItem>();

        // Add vJoy devices to list
        foreach (var vjoy in _ctx.VJoyDevices)
        {
            int axes = CountVJoyAxes(vjoy);
            int buttons = vjoy.ButtonCount;
            int povs = vjoy.ContPovCount + vjoy.DiscPovCount;

            string status;
            if (axes >= physicalDevice.AxisCount &&
                buttons >= physicalDevice.ButtonCount &&
                povs >= physicalDevice.HatCount)
            {
                status = "[OK]";
            }
            else
            {
                status = "[partial]";
            }

            items.Add(new FUISelectionDialog.SelectionItem
            {
                Text = $"vJoy #{vjoy.Id}: {axes} axes, {buttons} buttons, {povs} POVs",
                Status = status,
                Tag = vjoy
            });
        }

        // Add option to configure new vJoy device
        items.Add(new FUISelectionDialog.SelectionItem
        {
            Text = "+ Configure new vJoy device...",
            IsAction = true
        });

        string description = $"Select a vJoy device to map {physicalDevice.Name}:\n" +
                           $"({physicalDevice.AxisCount} axes, {physicalDevice.ButtonCount} buttons, {physicalDevice.HatCount} hats)";

        int selectedIndex = FUISelectionDialog.Show(_ctx.OwnerForm, "Select vJoy Device", description, items, "Map 1:1", "Cancel");

        if (selectedIndex < 0)
            return null;

        // Check if user selected "Configure new vJoy device"
        if (selectedIndex == _ctx.VJoyDevices.Count)
        {
            return CreateVJoyDeviceForPhysical(physicalDevice);
        }

        if (selectedIndex >= 0 && selectedIndex < _ctx.VJoyDevices.Count)
        {
            var selectedVJoy = _ctx.VJoyDevices[selectedIndex];

            // Warn about partial mappings if necessary
            int axes = CountVJoyAxes(selectedVJoy);
            int buttons = selectedVJoy.ButtonCount;
            int povs = selectedVJoy.ContPovCount + selectedVJoy.DiscPovCount;

            if (axes < physicalDevice.AxisCount ||
                buttons < physicalDevice.ButtonCount ||
                povs < physicalDevice.HatCount)
            {
                int partialResult = FUIMessageBox.Show(_ctx.OwnerForm,
                    $"vJoy #{selectedVJoy.Id} doesn't have enough capacity.\n\n" +
                    $"Physical device: {physicalDevice.AxisCount} axes, {physicalDevice.ButtonCount} buttons, {physicalDevice.HatCount} hats\n" +
                    $"vJoy #{selectedVJoy.Id}: {axes} axes, {buttons} buttons, {povs} POVs\n\n" +
                    "Some controls will not be mapped. Continue?",
                    "Partial Mapping", FUIMessageBox.MessageBoxType.Question, "Continue", "Cancel");

                if (partialResult != 0)
                    return null;
            }

            return selectedVJoy;
        }

        return null;
    }

}