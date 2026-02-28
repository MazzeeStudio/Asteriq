using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
    private void OpenMappingEditor(int rowIndex)
    {
        if (!_ctx.ProfileManager.HasActiveProfile)
        {
            _ctx.CreateNewProfilePrompt!();
            if (!_ctx.ProfileManager.HasActiveProfile) return;
        }
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;

        // Cancel any existing listening
        CancelInputListening();

        _mappingEditorOpen = true;
        _editingRowIndex = rowIndex;
        _selectedMappingRow = rowIndex;
        _isEditingAxis = rowIndex < 8;
        _pendingInput = null;
        _manualEntryMode = false;
        _selectedButtonMode = ButtonMode.Normal;
        _selectedSourceDevice = 0;
        _selectedSourceControl = 0;

        // Load existing binding if present
        LoadExistingBinding(rowIndex);
    }

    private void LoadExistingBinding(int rowIndex)
    {
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        bool isAxis = rowIndex < 8;
        int outputIndex = isAxis ? rowIndex : rowIndex - 8;

        if (isAxis)
        {
            var mapping = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (mapping is not null && mapping.Inputs.Count > 0)
            {
                var input = mapping.Inputs[0];
                _pendingInput = new DetectedInput
                {
                    DeviceGuid = Guid.TryParse(input.DeviceId, out var guid) ? guid : Guid.Empty,
                    DeviceName = input.DeviceName,
                    Type = input.Type,
                    Index = input.Index,
                    Value = 0
                };

                // Set selected device in dropdown
                for (int i = 0; i < _ctx.Devices.Count; i++)
                {
                    if (_ctx.Devices[i].InstanceGuid.ToString() == input.DeviceId)
                    {
                        _selectedSourceDevice = i;
                        break;
                    }
                }
                _selectedSourceControl = input.Index;
            }
        }
        else
        {
            var mapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyButton &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (mapping is not null && mapping.Inputs.Count > 0)
            {
                var input = mapping.Inputs[0];
                _pendingInput = new DetectedInput
                {
                    DeviceGuid = Guid.TryParse(input.DeviceId, out var guid) ? guid : Guid.Empty,
                    DeviceName = input.DeviceName,
                    Type = input.Type,
                    Index = input.Index,
                    Value = 0
                };
                _selectedButtonMode = mapping.Mode;

                // Set selected device in dropdown
                for (int i = 0; i < _ctx.Devices.Count; i++)
                {
                    if (_ctx.Devices[i].InstanceGuid.ToString() == input.DeviceId)
                    {
                        _selectedSourceDevice = i;
                        break;
                    }
                }
                _selectedSourceControl = input.Index;
            }
        }
    }

    private void CloseMappingEditor()
    {
        CancelInputListening();
        _mappingEditorOpen = false;
        _editingRowIndex = -1;
        _pendingInput = null;
        _deviceDropdownOpen = false;
        _controlDropdownOpen = false;
    }

    /// <summary>
    /// Starts listening for input. Fire-and-forget from UI.
    /// All exceptions are handled internally.
    /// </summary>
    private void StartListeningForInput()
    {
        // Fire-and-forget async operation with internal exception handling
        _ = StartListeningForInputAsync();
    }

    private async Task StartListeningForInputAsync()
    {
        if (_isListeningForInput) return;
        if (!_mappingEditorOpen) return;

        _isListeningForInput = true;
        _inputListeningStartTime = DateTime.Now;
        _pendingInput = null;

        // Determine input type based on what we're editing
        var filter = _isEditingAxis ? InputDetectionFilter.Axes : InputDetectionFilter.Buttons;

        _inputDetectionService ??= new InputDetectionService(_ctx.InputService);

        try
        {
            // Wait for actual input change - use a delay to skip initial state
            await Task.Delay(200); // Small delay to let user release any currently pressed buttons

            var detected = await _inputDetectionService.WaitForInputAsync(filter, 0.15f, 15000);

            if (detected is not null && _mappingEditorOpen)
            {
                _pendingInput = detected;

                // Update manual entry dropdowns to match detected input
                PhysicalDeviceInfo? sourceDevice = null;
                for (int i = 0; i < _ctx.Devices.Count; i++)
                {
                    if (_ctx.Devices[i].InstanceGuid == detected.DeviceGuid)
                    {
                        _selectedSourceDevice = i;
                        sourceDevice = _ctx.Devices[i];
                        break;
                    }
                }
                _selectedSourceControl = detected.Index;

                // Note: We intentionally do NOT auto-select vJoy row here.
                // When user explicitly clicks a row to edit, their choice is respected.
                // Type-aware mapping is only used in 1:1 auto-mapping feature.
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] Input listening cancelled or failed: {ex.Message}");
        }
        finally
        {
            _isListeningForInput = false;
        }
    }

    private void SaveMapping()
    {
        if (!_mappingEditorOpen || _pendingInput is null) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int outputIndex = _isEditingAxis ? _editingRowIndex : _editingRowIndex - 8;

        // Remove existing binding
        RemoveBindingAtRow(_editingRowIndex, save: false);

        if (_isEditingAxis)
        {
            var mapping = new AxisMapping
            {
                Name = $"{_pendingInput.DeviceName} Axis {_pendingInput.Index} -> vJoy {vjoyDevice.Id} Axis {outputIndex}",
                Inputs = new List<InputSource> { _pendingInput.ToInputSource() },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyAxis,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                },
                Curve = new AxisCurve()
            };
            profile.AxisMappings.Add(mapping);
        }
        else
        {
            var mapping = new ButtonMapping
            {
                Name = $"{_pendingInput.DeviceName} Button {_pendingInput.Index + 1} -> vJoy {vjoyDevice.Id} Button {outputIndex + 1}",
                Inputs = new List<InputSource> { _pendingInput.ToInputSource() },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyButton,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                },
                Mode = _selectedButtonMode
            };
            profile.ButtonMappings.Add(mapping);
        }

        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.OnMappingsChanged();
        CloseMappingEditor();
    }

    private void CreateBindingFromManualEntry()
    {
        if (!_manualEntryMode || _ctx.Devices.Count == 0 || _selectedSourceDevice >= _ctx.Devices.Count) return;

        var device = _ctx.Devices[_selectedSourceDevice];
        _pendingInput = new DetectedInput
        {
            DeviceGuid = device.InstanceGuid,
            DeviceName = device.Name,
            Type = _isEditingAxis ? InputType.Axis : InputType.Button,
            Index = _selectedSourceControl,
            Value = 0
        };
    }

    /// <summary>
    /// Create 1:1 mappings from the selected physical device to a user-selected vJoy device.
    /// Maps all axes, buttons, and hats directly without any curves or modifications.
    /// </summary>
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

        // Remove any existing mappings from this device to this vJoy device
        profile.AxisMappings.RemoveAll(m =>
            m.Inputs.Any(i => i.DeviceId == deviceId) &&
            m.Output.VJoyDevice == vjoyDevice.Id);
        profile.ButtonMappings.RemoveAll(m =>
            m.Inputs.Any(i => i.DeviceId == deviceId) &&
            m.Output.VJoyDevice == vjoyDevice.Id);
        profile.HatMappings.RemoveAll(m =>
            m.Inputs.Any(i => i.DeviceId == deviceId) &&
            m.Output.VJoyDevice == vjoyDevice.Id);

        // Create axis mappings using simple sequential mapping
        // Maps physical axis 0 -> vJoy axis 0, axis 1 -> vJoy axis 1, etc.
        // This is predictable and consistent with manual mapping behavior.
        var vjoyAxisIndices = GetVJoyAxisIndices(vjoyDevice);

        LogMapping($"=== 1:1 Mapping for {physicalDevice.Name} ===");
        LogMapping($"Device: {physicalDevice.Name}, AxisCount: {physicalDevice.AxisCount}");

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
                Curve = new AxisCurve { Type = CurveType.Linear }
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
    private int CountVJoyAxes(VJoyDeviceInfo vjoy)
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
    private List<int> GetVJoyAxisIndices(VJoyDeviceInfo vjoy)
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
    /// Get a human-readable name for a vJoy axis index.
    /// </summary>
    private string GetVJoyAxisName(int index)
    {
        return index switch
        {
            0 => "X",
            1 => "Y",
            2 => "Z",
            3 => "RX",
            4 => "RY",
            5 => "RZ",
            6 => "Slider1",
            7 => "Slider2",
            _ => $"Axis{index}"
        };
    }

    /// <summary>
    /// Find the best vJoy device that can accommodate all controls from the physical device
    /// </summary>
    private VJoyDeviceInfo? FindBestVJoyDevice(PhysicalDeviceInfo physical)
    {
        VJoyDeviceInfo? best = null;
        int bestScore = -1;

        foreach (var vjoy in _ctx.VJoyDevices)
        {
            int axes = CountVJoyAxes(vjoy);
            int buttons = vjoy.ButtonCount;
            int povs = vjoy.ContPovCount + vjoy.DiscPovCount;

            // Check if this vJoy can accommodate all controls
            if (axes >= physical.AxisCount &&
                buttons >= physical.ButtonCount &&
                povs >= physical.HatCount)
            {
                // Score based on how close the match is (lower excess = better)
                int excess = (axes - physical.AxisCount) +
                            (buttons - physical.ButtonCount) +
                            (povs - physical.HatCount);
                int score = 1000 - excess; // Higher score = better match

                if (score > bestScore)
                {
                    bestScore = score;
                    best = vjoy;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Programmatically create a new vJoy device using vJoyConfig.exe CLI,
    /// configured to match the physical device's capabilities.
    /// Returns the created device on success, or null if cancelled or failed.
    /// </summary>
    private VJoyDeviceInfo? CreateVJoyDeviceForPhysical(PhysicalDeviceInfo physical)
    {
        string? configPath = _ctx.DriverSetupManager.GetVJoyConfigPath();
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

        // Build axis list: up to 8 axes matching physical device count
        string[] axisNames = { "X", "Y", "Z", "RX", "RY", "RZ", "SL0", "SL1" };
        int axisCount = Math.Min(physical.AxisCount, axisNames.Length);
        int buttonCount = Math.Max(physical.ButtonCount, 1);
        int povCount = physical.HatCount;

        string args = $"{newId} -f";
        if (axisCount > 0)
            args += $" -a {string.Join(" ", axisNames.Take(axisCount))}";
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

        var result = FUIMessageBox.ShowQuestion(_ctx.OwnerForm, message, "vJoy Configuration Required");

        if (result)
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
                var result = FUIMessageBox.ShowQuestion(_ctx.OwnerForm,
                    $"vJoy #{selectedVJoy.Id} doesn't have enough capacity.\n\n" +
                    $"Physical device: {physicalDevice.AxisCount} axes, {physicalDevice.ButtonCount} buttons, {physicalDevice.HatCount} hats\n" +
                    $"vJoy #{selectedVJoy.Id}: {axes} axes, {buttons} buttons, {povs} POVs\n\n" +
                    "Some controls will not be mapped. Continue?",
                    "Partial Mapping");

                if (!result)
                    return null;
            }

            return selectedVJoy;
        }

        return null;
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private static void LogMapping(string message)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Asteriq", "axis_types.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] [Mapping] {message}\n");
    }

    /// <summary>
    /// Clear all mappings for the selected physical device.
    /// </summary>
    private void ClearDeviceMappings()
    {
        if (_ctx.SelectedDevice < 0 || _ctx.SelectedDevice >= _ctx.Devices.Count) return;

        var physicalDevice = _ctx.Devices[_ctx.SelectedDevice];
        if (physicalDevice.IsVirtual) return;

        var result = FUIMessageBox.ShowQuestion(_ctx.OwnerForm,
            $"Remove all mappings for {physicalDevice.Name}?\n\nThis will remove axis, button, and hat mappings from all vJoy devices.",
            "Clear Mappings");

        if (!result) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        string deviceId = physicalDevice.InstanceGuid.ToString();

        // Remove all mappings from this device
        int axisRemoved = profile.AxisMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));
        int buttonRemoved = profile.ButtonMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));
        int hatRemoved = profile.HatMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));

        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.OnMappingsChanged();

        FUIMessageBox.ShowInfo(_ctx.OwnerForm,
            $"Removed {axisRemoved} axis, {buttonRemoved} button, and {hatRemoved} hat mappings.",
            "Mappings Cleared");

        _ctx.InvalidateCanvas();
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

        var result = FUIMessageBox.ShowQuestion(_ctx.OwnerForm,
            $"Permanently remove {device.Name}?\n\n" +
            "This will:\n" +
            "ÔÇó Clear all axis, button, and hat mappings\n" +
            "ÔÇó Remove the device from the disconnected list\n\n" +
            "This cannot be undone.",
            "Remove Device");

        if (!result) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        string deviceId = device.InstanceGuid.ToString();

        // Remove all mappings from this device
        int axisRemoved = 0, buttonRemoved = 0, hatRemoved = 0;
        if (profile is not null)
        {
            axisRemoved = profile.AxisMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));
            buttonRemoved = profile.ButtonMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));
            hatRemoved = profile.HatMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));
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

    private void CreateBindingForRow(int rowIndex, DetectedInput input)
    {
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        // Use current mapping category to determine axis vs button
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        // rowIndex is already the correct index within the current category
        int outputIndex = rowIndex;

        // Remove existing binding for this output
        RemoveBindingAtRow(rowIndex, save: false);

        if (isAxis)
        {
            var mapping = new AxisMapping
            {
                Name = $"{input.DeviceName} Axis {input.Index} -> vJoy {vjoyDevice.Id} Axis {outputIndex}",
                Inputs = new List<InputSource> { input.ToInputSource() },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyAxis,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                },
                Curve = new AxisCurve()
            };
            profile.AxisMappings.Add(mapping);
        }
        else
        {
            var mapping = new ButtonMapping
            {
                Name = $"{input.DeviceName} Button {input.Index + 1} -> vJoy {vjoyDevice.Id} Button {outputIndex + 1}",
                Inputs = new List<InputSource> { input.ToInputSource() },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyButton,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                },
                Mode = ButtonMode.Normal
            };
            profile.ButtonMappings.Add(mapping);
        }

        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.OnMappingsChanged();
    }

    private void RemoveBindingAtRow(int rowIndex, bool save = true)
    {
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        // Use current mapping category to determine axis vs button
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        // rowIndex is already the correct index within the current category
        int outputIndex = rowIndex;

        if (isAxis)
        {
            var existing = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);
            if (existing is not null)
            {
                profile.AxisMappings.Remove(existing);
            }
        }
        else
        {
            var existing = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyButton &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);
            if (existing is not null)
            {
                profile.ButtonMappings.Remove(existing);
            }
        }

        if (save)
        {
            _ctx.ProfileManager.SaveActiveProfile();
            _ctx.OnMappingsChanged();
        }
    }

    private void CancelInputListening()
    {
        if (_isListeningForInput)
        {
            _inputDetectionService?.Cancel();
            _isListeningForInput = false;
        }
    }

    /// <summary>
    /// Check if a physical input is already mapped anywhere in the profile.
    /// Returns the mapping name if found, null otherwise.
    /// </summary>
    private string? FindExistingMappingForInput(MappingProfile profile, InputSource inputToCheck)
    {
        // Check axis mappings
        foreach (var mapping in profile.AxisMappings)
        {
            foreach (var input in mapping.Inputs)
            {
                if (input.DeviceId == inputToCheck.DeviceId &&
                    input.Type == inputToCheck.Type &&
                    input.Index == inputToCheck.Index)
                {
                    return mapping.Name;
                }
            }
        }

        // Check button mappings
        foreach (var mapping in profile.ButtonMappings)
        {
            foreach (var input in mapping.Inputs)
            {
                if (input.DeviceId == inputToCheck.DeviceId &&
                    input.Type == inputToCheck.Type &&
                    input.Index == inputToCheck.Index)
                {
                    return mapping.Name;
                }
            }
        }

        // Check hat mappings
        foreach (var mapping in profile.HatMappings)
        {
            foreach (var input in mapping.Inputs)
            {
                if (input.DeviceId == inputToCheck.DeviceId &&
                    input.Type == inputToCheck.Type &&
                    input.Index == inputToCheck.Index)
                {
                    return mapping.Name;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Show a confirmation dialog when a duplicate mapping is detected.
    /// Returns true if the user wants to proceed and replace the existing mapping.
    /// </summary>
    private bool ConfirmDuplicateMapping(string existingMappingName, string newMappingTarget)
    {
        using var dialog = new FUIConfirmDialog(
            "Duplicate Mapping",
            $"This input is already mapped to:\n\n{existingMappingName}\n\nRemove existing and create new mapping for {newMappingTarget}?",
            "Replace",
            "Cancel");
        return dialog.ShowDialog(_ctx.OwnerForm) == DialogResult.Yes;
    }

    /// <summary>
    /// Remove any existing mappings that use the specified input source.
    /// </summary>
    private void RemoveExistingMappingsForInput(MappingProfile profile, InputSource inputToRemove)
    {
        // Remove from axis mappings
        foreach (var mapping in profile.AxisMappings.ToList())
        {
            mapping.Inputs.RemoveAll(i =>
                i.DeviceId == inputToRemove.DeviceId &&
                i.Type == inputToRemove.Type &&
                i.Index == inputToRemove.Index);

            // If no inputs remain, remove the mapping entirely
            if (mapping.Inputs.Count == 0)
            {
                profile.AxisMappings.Remove(mapping);
            }
        }

        // Remove from button mappings
        foreach (var mapping in profile.ButtonMappings.ToList())
        {
            mapping.Inputs.RemoveAll(i =>
                i.DeviceId == inputToRemove.DeviceId &&
                i.Type == inputToRemove.Type &&
                i.Index == inputToRemove.Index);

            if (mapping.Inputs.Count == 0)
            {
                profile.ButtonMappings.Remove(mapping);
            }
        }

        // Remove from hat mappings
        foreach (var mapping in profile.HatMappings.ToList())
        {
            mapping.Inputs.RemoveAll(i =>
                i.DeviceId == inputToRemove.DeviceId &&
                i.Type == inputToRemove.Type &&
                i.Index == inputToRemove.Index);

            if (mapping.Inputs.Count == 0)
            {
                profile.HatMappings.Remove(mapping);
            }
        }
    }

    /// <summary>
    /// Starts listening for input on a specific row. Fire-and-forget from UI.
    /// All exceptions are handled internally.
    /// </summary>
    private void StartInputListening(int rowIndex)
    {
        // Fire-and-forget async operation with internal exception handling
        _ = StartInputListeningAsync(rowIndex);
    }

    private async Task StartInputListeningAsync(int rowIndex)
    {
        if (_isListeningForInput) return;
        if (rowIndex < 0) return;

        _isListeningForInput = true;
        _inputListeningStartTime = DateTime.Now;
        _pendingInput = null;

        // Determine input type based on current mapping category tab
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        var filter = isAxis ? InputDetectionFilter.Axes : InputDetectionFilter.Buttons;

        _inputDetectionService ??= new InputDetectionService(_ctx.InputService);

        try
        {
            // Small delay to let user release any currently pressed buttons
            await Task.Delay(200);

            var detected = await _inputDetectionService.WaitForInputAsync(filter, 0.15f, 15000);

            if (detected is not null && _selectedMappingRow == rowIndex)
            {
                _pendingInput = detected;
                var inputSource = detected.ToInputSource();

                // Note: We intentionally do NOT auto-select vJoy row here.
                // When user explicitly clicks a row to map, their choice is respected.
                // Type-aware mapping is only used in 1:1 auto-mapping feature.
                int targetRowIndex = rowIndex;

                // Check for duplicate mapping
                var profile = _ctx.ProfileManager.ActiveProfile;
                if (profile is not null)
                {
                    var existingMapping = FindExistingMappingForInput(profile, inputSource);
                    if (existingMapping is not null)
                    {
                        string newTarget = isAxis ? $"vJoy Axis {targetRowIndex}" : $"vJoy Button {targetRowIndex + 1}";
                        if (!ConfirmDuplicateMapping(existingMapping, newTarget))
                        {
                            // User cancelled, don't create the mapping
                            return;
                        }
                        // User confirmed, remove existing mapping first
                        RemoveExistingMappingsForInput(profile, inputSource);
                    }
                }

                // Save the mapping using current panel settings (output type, key combo, button mode)
                SaveMappingForRow(targetRowIndex, detected, isAxis);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] Input listening for row {rowIndex} cancelled or failed: {ex.Message}");
        }
        finally
        {
            _isListeningForInput = false;
        }
    }

    /// <summary>
    /// Start input listening when user has assigned a keyboard key to an empty button slot.
    /// When physical input is detected, creates a new mapping with the pending keyboard output.
    /// </summary>
    private async Task StartPendingKeyboardInputListeningAsync()
    {
        if (_isListeningForInput) return;
        if (_pendingKeyboardKey is null) return;

        _isListeningForInput = true;
        _inputListeningStartTime = DateTime.Now;
        _pendingInput = null;

        _inputDetectionService ??= new InputDetectionService(_ctx.InputService);

        try
        {
            // Small delay to let user release any currently pressed buttons
            await Task.Delay(200);

            var detected = await _inputDetectionService.WaitForInputAsync(InputDetectionFilter.Buttons, 0.15f, 15000);

            if (detected is not null && _pendingKeyboardKey is not null)
            {
                var profile = _ctx.ProfileManager.ActiveProfile;
                if (profile is null) return;

                var newInputSource = detected.ToInputSource();

                // Check for duplicate mapping
                var existingMapping = FindExistingMappingForInput(profile, newInputSource);
                if (existingMapping is not null)
                {
                    string newTarget = $"Keyboard: {FormatKeyComboForDisplay(_pendingKeyboardKey, _pendingKeyboardModifiers)}";
                    if (!ConfirmDuplicateMapping(existingMapping, newTarget))
                    {
                        // User cancelled, clear pending state
                        ClearPendingKeyboardState();
                        return;
                    }
                    // User confirmed, remove existing mapping first
                    RemoveExistingMappingsForInput(profile, newInputSource);
                }

                // Create new button mapping with keyboard output
                var mapping = new ButtonMapping
                {
                    Name = $"{detected.DeviceName} Button {detected.Index + 1} -> {FormatKeyComboForDisplay(_pendingKeyboardKey, _pendingKeyboardModifiers)}",
                    Inputs = new List<InputSource> { newInputSource },
                    Output = new OutputTarget
                    {
                        Type = OutputType.Keyboard,
                        VJoyDevice = _pendingKeyboardVJoyDevice,
                        Index = _pendingKeyboardOutputIndex,
                        KeyName = _pendingKeyboardKey,
                        Modifiers = _pendingKeyboardModifiers
                    },
                    Mode = _selectedButtonMode
                };
                profile.ButtonMappings.Add(mapping);
                profile.ModifiedAt = DateTime.UtcNow;
                _ctx.ProfileManager.SaveActiveProfile();
                _ctx.OnMappingsChanged();

                // Update the pending input so UI can show it
                _pendingInput = detected;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] Pending keyboard input listening cancelled or failed: {ex.Message}");
        }
        finally
        {
            _isListeningForInput = false;
            ClearPendingKeyboardState();
        }
    }

    private void ClearPendingKeyboardState()
    {
        _pendingKeyboardKey = null;
        _pendingKeyboardModifiers = null;
        _pendingKeyboardOutputIndex = -1;
        _pendingKeyboardVJoyDevice = 0;
    }

    private void SaveMappingForRow(int rowIndex, DetectedInput input, bool isAxis)
    {
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        // rowIndex is already the correct index within the current category (axes or buttons)
        int outputIndex = rowIndex;
        var newInputSource = input.ToInputSource();

        if (isAxis)
        {
            // Find existing mapping or create new one
            var existingMapping = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (existingMapping is not null)
            {
                // Add input to existing mapping (support multiple inputs)
                existingMapping.Inputs.Add(newInputSource);
                existingMapping.Name = $"vJoy {vjoyDevice.Id} Axis {outputIndex} ({existingMapping.Inputs.Count} inputs)";
            }
            else
            {
                // Create new mapping
                var mapping = new AxisMapping
                {
                    Name = $"{input.DeviceName} Axis {input.Index} -> vJoy {vjoyDevice.Id} Axis {outputIndex}",
                    Inputs = new List<InputSource> { newInputSource },
                    Output = new OutputTarget
                    {
                        Type = OutputType.VJoyAxis,
                        VJoyDevice = vjoyDevice.Id,
                        Index = outputIndex
                    },
                    Curve = new AxisCurve()
                };
                profile.AxisMappings.Add(mapping);
            }
        }
        else
        {
            // Find existing mapping for this button slot (regardless of output type)
            var existingMapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (existingMapping is not null)
            {
                // Add input to existing mapping (support multiple inputs)
                existingMapping.Inputs.Add(newInputSource);

                // Update with current panel settings
                existingMapping.Output.Type = _outputTypeIsKeyboard ? OutputType.Keyboard : OutputType.VJoyButton;
                if (_outputTypeIsKeyboard)
                {
                    existingMapping.Output.KeyName = _selectedKeyName;
                    existingMapping.Output.Modifiers = _selectedModifiers?.ToList();
                }
                else
                {
                    existingMapping.Output.KeyName = null;
                    existingMapping.Output.Modifiers = null;
                }
                existingMapping.Mode = _selectedButtonMode;
                existingMapping.Name = $"vJoy {vjoyDevice.Id} Button {outputIndex + 1} ({existingMapping.Inputs.Count} inputs)";
            }
            else
            {
                // Create new mapping using current panel settings
                var outputType = _outputTypeIsKeyboard ? OutputType.Keyboard : OutputType.VJoyButton;
                var outputTarget = new OutputTarget
                {
                    Type = outputType,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                };

                if (_outputTypeIsKeyboard)
                {
                    outputTarget.KeyName = _selectedKeyName;
                    outputTarget.Modifiers = _selectedModifiers?.ToList();
                }

                string mappingName = _outputTypeIsKeyboard && !string.IsNullOrEmpty(_selectedKeyName)
                    ? $"{input.DeviceName} Button {input.Index + 1} -> {FormatKeyComboForDisplay(_selectedKeyName, _selectedModifiers)}"
                    : $"{input.DeviceName} Button {input.Index + 1} -> vJoy {vjoyDevice.Id} Button {outputIndex + 1}";

                var mapping = new ButtonMapping
                {
                    Name = mappingName,
                    Inputs = new List<InputSource> { newInputSource },
                    Output = outputTarget,
                    Mode = _selectedButtonMode
                };
                profile.ButtonMappings.Add(mapping);
            }
        }

        profile.ModifiedAt = DateTime.UtcNow;
        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.OnMappingsChanged();
        _pendingInput = null;
    }

    private void RemoveInputSourceAtIndex(int inputIndex)
    {
        if (_selectedMappingRow < 0) return;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        int outputIndex = _selectedMappingRow;

        if (isAxis)
        {
            var mapping = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (mapping is not null && inputIndex >= 0 && inputIndex < mapping.Inputs.Count)
            {
                mapping.Inputs.RemoveAt(inputIndex);
                if (mapping.Inputs.Count == 0)
                {
                    // Remove the entire mapping if no inputs left
                    profile.AxisMappings.Remove(mapping);
                }
            }
        }
        else
        {
            var mapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyButton &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (mapping is not null && inputIndex >= 0 && inputIndex < mapping.Inputs.Count)
            {
                mapping.Inputs.RemoveAt(inputIndex);
                if (mapping.Inputs.Count == 0)
                {
                    // Remove the entire mapping if no inputs left
                    profile.ButtonMappings.Remove(mapping);
                }
            }
        }

        profile.ModifiedAt = DateTime.UtcNow;
        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.OnMappingsChanged();
    }

    private void UpdateMergeOperationForSelected(int mergeOpIndex)
    {
        var axisMapping = GetCurrentAxisMapping();
        if (axisMapping is null) return;

        MergeOperation[] ops = { MergeOperation.Average, MergeOperation.Maximum, MergeOperation.Minimum, MergeOperation.Sum };
        if (mergeOpIndex < 0 || mergeOpIndex >= ops.Length) return;

        axisMapping.MergeOp = ops[mergeOpIndex];

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is not null)
        {
            profile.ModifiedAt = DateTime.UtcNow;
            _ctx.ProfileManager.SaveActiveProfile();
        }
        _ctx.OnMappingsChanged();
    }

    private void LoadAxisSettingsForRow()
    {
        // Reset to defaults
        _selectedCurveType = CurveType.Linear;
        _curveControlPoints = new List<SKPoint> { new(0, 0), new(1, 1) };
        _curveSymmetrical = false;
        _deadzoneMin = -1.0f;
        _deadzoneCenterMin = 0.0f;
        _deadzoneCenterMax = 0.0f;
        _deadzoneMax = 1.0f;
        _deadzoneCenterEnabled = false;
        _axisInverted = false;

        // Only for axis category
        if (_mappingCategory != 1) return;
        if (_selectedMappingRow < 0) return;

        var axisMapping = GetCurrentAxisMapping();
        if (axisMapping is null) return;

        // Load curve settings from mapping
        var curve = axisMapping.Curve;
        _selectedCurveType = curve.Type;
        _curveSymmetrical = curve.Symmetrical;
        _axisInverted = axisMapping.Invert;

        // Load deadzone settings
        _deadzoneMin = curve.DeadzoneLow;
        _deadzoneCenterMin = curve.DeadzoneCenterLow;
        _deadzoneCenterMax = curve.DeadzoneCenterHigh;
        _deadzoneMax = curve.DeadzoneHigh;
        _deadzoneCenterEnabled = curve.DeadzoneCenterLow != 0 || curve.DeadzoneCenterHigh != 0;

        // Load control points for custom curve
        if (curve.Type == CurveType.Custom && curve.ControlPoints is not null && curve.ControlPoints.Count >= 2)
        {
            _curveControlPoints = curve.ControlPoints.Select(p => new SKPoint(p.Input, p.Output)).ToList();
        }
        else
        {
            // Generate default control points based on curve type
            _curveControlPoints = curve.Type switch
            {
                CurveType.SCurve => new List<SKPoint> { new(0, 0), new(0.25f, 0.1f), new(0.75f, 0.9f), new(1, 1) },
                CurveType.Exponential => new List<SKPoint> { new(0, 0), new(0.5f, 0.25f), new(1, 1) },
                _ => new List<SKPoint> { new(0, 0), new(1, 1) }
            };
        }
    }

    private void SaveAxisSettingsForRow()
    {
        // Only for axis category
        if (_mappingCategory != 1) return;
        if (_selectedMappingRow < 0) return;

        var axisMapping = GetCurrentAxisMapping();
        if (axisMapping is null) return;

        // Save curve settings to mapping
        axisMapping.Curve.Type = _selectedCurveType;
        axisMapping.Curve.Symmetrical = _curveSymmetrical;
        axisMapping.Invert = _axisInverted;

        // Save deadzone settings
        axisMapping.Curve.DeadzoneLow = _deadzoneMin;
        axisMapping.Curve.DeadzoneCenterLow = _deadzoneCenterEnabled ? _deadzoneCenterMin : 0f;
        axisMapping.Curve.DeadzoneCenterHigh = _deadzoneCenterEnabled ? _deadzoneCenterMax : 0f;
        axisMapping.Curve.DeadzoneHigh = _deadzoneMax;

        // Save control points for custom curve
        if (_selectedCurveType == CurveType.Custom)
        {
            axisMapping.Curve.ControlPoints = _curveControlPoints
                .Select(p => new CurvePoint(p.X, p.Y))
                .ToList();
        }

        // Persist
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is not null)
        {
            profile.ModifiedAt = DateTime.UtcNow;
            _ctx.ProfileManager.SaveActiveProfile();
        }
    }

    private void LoadOutputTypeStateForRow()
    {
        // Reset state
        _outputTypeIsKeyboard = false;
        _selectedKeyName = "";
        _selectedModifiers = null;
        _isCapturingKey = false;
        _selectedButtonMode = ButtonMode.Normal;
        _pulseDurationMs = 100;
        _holdDurationMs = 500;

        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping is not null)
        {
            _outputTypeIsKeyboard = mapping.Output.Type == OutputType.Keyboard;
            _selectedKeyName = mapping.Output.KeyName ?? "";
            _selectedModifiers = mapping.Output.Modifiers?.ToList();
            _selectedButtonMode = mapping.Mode;
            _pulseDurationMs = mapping.PulseDurationMs;
            _holdDurationMs = mapping.HoldDurationMs;
        }
    }

    private void UpdateButtonModeForSelected()
    {
        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        // Find mapping for this button slot (either VJoyButton or Keyboard output)
        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping is not null)
        {
            mapping.Mode = _selectedButtonMode;
            profile.ModifiedAt = DateTime.UtcNow;
            _ctx.ProfileManager.SaveActiveProfile();
        }
    }

    private void UpdateOutputTypeForSelected()
    {
        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        // Find mapping for this button slot
        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping is not null)
        {
            // Update output type and clear/set key name
            mapping.Output.Type = _outputTypeIsKeyboard ? OutputType.Keyboard : OutputType.VJoyButton;
            if (!_outputTypeIsKeyboard)
            {
                mapping.Output.KeyName = null;
                mapping.Output.Modifiers = null;
            }
            else if (!string.IsNullOrEmpty(_selectedKeyName))
            {
                mapping.Output.KeyName = _selectedKeyName;
            }
            profile.ModifiedAt = DateTime.UtcNow;
            _ctx.ProfileManager.SaveActiveProfile();
        }
    }

    private void UpdateKeyNameForSelected()
    {
        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping is null && !string.IsNullOrEmpty(_selectedKeyName))
        {
            // No existing mapping - need to capture a physical input first
            // Store the keyboard key and start listening for physical input
            _pendingKeyboardKey = _selectedKeyName;
            _pendingKeyboardModifiers = _selectedModifiers?.ToList();
            _pendingKeyboardOutputIndex = outputIndex;
            _pendingKeyboardVJoyDevice = vjoyDevice.Id;

            // Start async input detection for pending keyboard binding
            _ = StartPendingKeyboardInputListeningAsync();
            return;
        }

        if (mapping is not null)
        {
            // Update existing mapping
            if (!string.IsNullOrEmpty(_selectedKeyName))
            {
                mapping.Output.Type = OutputType.Keyboard;
            }
            mapping.Output.KeyName = _selectedKeyName;
            mapping.Output.Modifiers = _selectedModifiers?.ToList();
            profile.ModifiedAt = DateTime.UtcNow;
            _ctx.ProfileManager.SaveActiveProfile();
        }
    }

    /// <summary>
    /// Clear all bindings (keyboard and input sources) for the selected button mapping
    /// </summary>
    private void ClearSelectedButtonMapping()
    {
        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        // Find and remove the mapping
        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping is not null)
        {
            profile.ButtonMappings.Remove(mapping);
            profile.ModifiedAt = DateTime.UtcNow;
            _ctx.ProfileManager.SaveActiveProfile();
            _ctx.OnMappingsChanged();

            // Reset UI state
            _selectedKeyName = "";
            _selectedModifiers = null;
            _outputTypeIsKeyboard = false;
            _selectedButtonMode = ButtonMode.Normal;
        }
    }

    /// <summary>
    /// Clear just the keyboard binding for the selected button mapping (keeps physical inputs)
    /// </summary>
    private void ClearKeyboardBinding()
    {
        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping is not null)
        {
            // Clear keyboard binding but keep mapping
            mapping.Output.Type = OutputType.VJoyButton;
            mapping.Output.KeyName = null;
            mapping.Output.Modifiers = null;
            profile.ModifiedAt = DateTime.UtcNow;
            _ctx.ProfileManager.SaveActiveProfile();

            // Update UI state
            _selectedKeyName = "";
            _selectedModifiers = null;
            _outputTypeIsKeyboard = false;
        }
    }

    private void UpdateDurationForSelectedMapping()
    {
        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping is not null)
        {
            mapping.PulseDurationMs = _pulseDurationMs;
            mapping.HoldDurationMs = _holdDurationMs;
            profile.ModifiedAt = DateTime.UtcNow;
            _ctx.ProfileManager.SaveActiveProfile();
        }
    }

    /// <summary>
    /// Make the current curve points symmetrical around the center.
    /// </summary>
    private void MakeCurveSymmetrical()
    {
        if (_curveControlPoints.Count < 2) return;

        // Create a new symmetrical set of points
        var newPoints = new List<SKPoint>();

        // Always include start point
        newPoints.Add(new SKPoint(0, 0));

        // For each point in the left half (X < 0.5), create its mirror
        var leftHalf = _curveControlPoints
            .Where(p => p.X > 0 && p.X < 0.5f)
            .OrderBy(p => p.X)
            .ToList();

        foreach (var pt in leftHalf)
        {
            newPoints.Add(pt);
        }

        // Add center point if there's one
        var centerPoint = _curveControlPoints.FirstOrDefault(p => Math.Abs(p.X - 0.5f) < 0.02f);
        if (centerPoint.X > 0.4f && centerPoint.X < 0.6f)
        {
            newPoints.Add(new SKPoint(0.5f, 0.5f)); // Center is always (0.5, 0.5) for perfect symmetry
        }
        else if (leftHalf.Count > 0)
        {
            // Add a center point if we have left half points
            newPoints.Add(new SKPoint(0.5f, 0.5f));
        }

        // Add mirrored points from left half (in reverse order for right half)
        for (int i = leftHalf.Count - 1; i >= 0; i--)
        {
            var pt = leftHalf[i];
            newPoints.Add(new SKPoint(1f - pt.X, 1f - pt.Y));
        }

        // Always include end point
        newPoints.Add(new SKPoint(1, 1));

        _curveControlPoints = newPoints;
    }

    private void AddCurveControlPoint(SKPoint graphPt)
    {
        // Don't add points at exact endpoints
        if (graphPt.X <= 0.01f || graphPt.X >= 0.99f) return;

        // Find insertion position (maintain sorted order by X)
        int insertIndex = 0;
        for (int i = 0; i < _curveControlPoints.Count; i++)
        {
            if (_curveControlPoints[i].X < graphPt.X)
                insertIndex = i + 1;
        }

        _curveControlPoints.Insert(insertIndex, graphPt);

        // If symmetrical mode is enabled, also add the mirror point
        if (_curveSymmetrical)
        {
            float mirrorX = 1f - graphPt.X;
            float mirrorY = 1f - graphPt.Y;

            // Don't add if mirror point would be too close to existing point
            bool tooClose = _curveControlPoints.Any(p => Math.Abs(p.X - mirrorX) < 0.04f);
            if (!tooClose && mirrorX > 0.01f && mirrorX < 0.99f)
            {
                // Find insertion position for mirror point
                int mirrorInsertIndex = 0;
                for (int i = 0; i < _curveControlPoints.Count; i++)
                {
                    if (_curveControlPoints[i].X < mirrorX)
                        mirrorInsertIndex = i + 1;
                }

                _curveControlPoints.Insert(mirrorInsertIndex, new SKPoint(mirrorX, mirrorY));
            }
        }

        _selectedCurveType = CurveType.Custom;
        _ctx.InvalidateCanvas();
    }

    private void RemoveCurveControlPoint(int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= _curveControlPoints.Count)
            return;

        var pt = _curveControlPoints[pointIndex];

        // Don't remove endpoints (0,0) or (1,1)
        bool isEndpoint = pointIndex == 0 || pointIndex == _curveControlPoints.Count - 1;
        if (isEndpoint)
            return;

        // Don't remove center point (0.5, 0.5)
        bool isCenterPoint = Math.Abs(pt.X - 0.5f) < 0.01f && Math.Abs(pt.Y - 0.5f) < 0.01f;
        if (isCenterPoint)
            return;

        // Remove the point
        _curveControlPoints.RemoveAt(pointIndex);

        // If symmetrical mode is enabled, also remove the mirror point
        if (_curveSymmetrical)
        {
            float mirrorX = 1f - pt.X;

            // Find and remove the mirror point
            for (int i = _curveControlPoints.Count - 1; i >= 0; i--)
            {
                var mirrorPt = _curveControlPoints[i];
                // Skip endpoints and center
                if (i == 0 || i == _curveControlPoints.Count - 1)
                    continue;
                if (Math.Abs(mirrorPt.X - 0.5f) < 0.01f && Math.Abs(mirrorPt.Y - 0.5f) < 0.01f)
                    continue;

                if (Math.Abs(mirrorPt.X - mirrorX) < 0.02f)
                {
                    _curveControlPoints.RemoveAt(i);
                    break;
                }
            }
        }

        _ctx.InvalidateCanvas();
    }

    private void OpenAddMappingDialog()
    {
        // Ensure we have an active profile
        if (!_ctx.ProfileManager.HasActiveProfile)
        {
            _ctx.CreateNewProfilePrompt!();
            if (!_ctx.ProfileManager.HasActiveProfile) return;
        }

        using var dialog = new MappingDialog(_ctx.InputService, _ctx.VJoyService);
        if (dialog.ShowDialog(_ctx.OwnerForm) == DialogResult.OK && dialog.Result.Success)
        {
            var result = dialog.Result;

            // Create the mapping based on detected input type
            if (result.Input!.Type == InputType.Button)
            {
                var mapping = new ButtonMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    Mode = result.ButtonMode
                };
                _ctx.ProfileManager.ActiveProfile!.ButtonMappings.Add(mapping);
            }
            else if (result.Input.Type == InputType.Axis)
            {
                var mapping = new AxisMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    Curve = result.AxisCurve ?? new AxisCurve()
                };
                _ctx.ProfileManager.ActiveProfile!.AxisMappings.Add(mapping);
            }
            else if (result.Input.Type == InputType.Hat)
            {
                var mapping = new HatMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    UseContinuous = true // Default to continuous POV
                };
                _ctx.ProfileManager.ActiveProfile!.HatMappings.Add(mapping);
            }

            // Save the profile
            _ctx.ProfileManager.SaveActiveProfile();
            _ctx.OnMappingsChanged();
        }
    }

    private void OpenMappingDialogForControl(string controlId)
    {
        // Need device map, selected device, and control info
        if (_ctx.DeviceMap is null || _ctx.SelectedDevice < 0 || _ctx.SelectedDevice >= _ctx.Devices.Count)
            return;

        // Find the control definition in the device map
        if (!_ctx.DeviceMap.Controls.TryGetValue(controlId, out var control))
            return;

        // Get the binding from the control (e.g., "button0", "x", "hat0")
        if (control.Bindings is null || control.Bindings.Count == 0)
            return;

        var device = _ctx.Devices[_ctx.SelectedDevice];
        var binding = control.Bindings[0];

        // Parse the binding to determine input type and index
        var (inputType, inputIndex) = ParseBinding(binding, control.Type);
        if (inputType is null)
            return;

        // Ensure we have an active profile
        if (!_ctx.ProfileManager.HasActiveProfile)
        {
            _ctx.CreateNewProfilePrompt!();
            if (!_ctx.ProfileManager.HasActiveProfile) return;
        }

        // Create a pre-selected DetectedInput
        var preSelectedInput = new DetectedInput
        {
            DeviceGuid = device.InstanceGuid,
            DeviceName = device.Name,
            Type = inputType.Value,
            Index = inputIndex,
            Value = 0
        };

        // Open dialog with pre-selected input (skips "wait for input" phase)
        using var dialog = new MappingDialog(_ctx.InputService, _ctx.VJoyService, preSelectedInput);
        if (dialog.ShowDialog(_ctx.OwnerForm) == DialogResult.OK && dialog.Result.Success)
        {
            var result = dialog.Result;

            // Create the mapping based on detected input type
            if (result.Input!.Type == InputType.Button)
            {
                var mapping = new ButtonMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    Mode = result.ButtonMode
                };
                _ctx.ProfileManager.ActiveProfile!.ButtonMappings.Add(mapping);
            }
            else if (result.Input.Type == InputType.Axis)
            {
                var mapping = new AxisMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    Curve = result.AxisCurve ?? new AxisCurve()
                };
                _ctx.ProfileManager.ActiveProfile!.AxisMappings.Add(mapping);
            }
            else if (result.Input.Type == InputType.Hat)
            {
                var mapping = new HatMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    UseContinuous = true
                };
                _ctx.ProfileManager.ActiveProfile!.HatMappings.Add(mapping);
            }

            // Save the profile
            _ctx.ProfileManager.SaveActiveProfile();
            _ctx.OnMappingsChanged();
        }
    }

    private (InputType? type, int index) ParseBinding(string binding, string controlType)
    {
        // Handle button bindings: "button0", "button1", etc.
        if (binding.StartsWith("button", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(binding.Substring(6), out int buttonIndex))
                return (InputType.Button, buttonIndex);
        }

        // Handle axis bindings: "x", "y", "z", "rx", "ry", "rz", "slider0", "slider1"
        var axisMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "x", 0 }, { "y", 1 }, { "z", 2 },
            { "rx", 3 }, { "ry", 4 }, { "rz", 5 },
            { "slider0", 6 }, { "slider1", 7 }
        };
        if (axisMap.TryGetValue(binding, out int axisIndex))
            return (InputType.Axis, axisIndex);

        // Handle hat bindings: "hat0", "hat1", etc.
        if (binding.StartsWith("hat", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(binding.Substring(3), out int hatIndex))
                return (InputType.Hat, hatIndex);
        }

        // Fall back to control type if binding doesn't parse
        return controlType.ToUpperInvariant() switch
        {
            "BUTTON" => (InputType.Button, 0),
            "AXIS" => (InputType.Axis, 0),
            "HAT" or "POV" => (InputType.Hat, 0),
            _ => (null, 0)
        };
    }

}
