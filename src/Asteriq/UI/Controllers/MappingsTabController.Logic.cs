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
        _inputDetection.PendingInput = null;
        _inputDetection.ManualEntryMode = false;
        _buttonMode.SelectedMode = ButtonMode.Normal;
        _inputDetection.SelectedSourceDevice = 0;
        _inputDetection.SelectedSourceControl = 0;

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
                _inputDetection.PendingInput = new DetectedInput
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
                        _inputDetection.SelectedSourceDevice = i;
                        break;
                    }
                }
                _inputDetection.SelectedSourceControl = input.Index;
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
                _inputDetection.PendingInput = new DetectedInput
                {
                    DeviceGuid = Guid.TryParse(input.DeviceId, out var guid) ? guid : Guid.Empty,
                    DeviceName = input.DeviceName,
                    Type = input.Type,
                    Index = input.Index,
                    Value = 0
                };
                _buttonMode.SelectedMode = mapping.Mode;

                // Set selected device in dropdown
                for (int i = 0; i < _ctx.Devices.Count; i++)
                {
                    if (_ctx.Devices[i].InstanceGuid.ToString() == input.DeviceId)
                    {
                        _inputDetection.SelectedSourceDevice = i;
                        break;
                    }
                }
                _inputDetection.SelectedSourceControl = input.Index;
            }
        }
    }

    private void CloseMappingEditor()
    {
        CancelInputListening();
        _mappingEditorOpen = false;
        _editingRowIndex = -1;
        _inputDetection.PendingInput = null;
        _inputDetection.DeviceDropdownOpen = false;
        _inputDetection.ControlDropdownOpen = false;
    }

    /// <summary>
    /// Starts listening for input. Fire-and-forget from UI.
    /// All exceptions are handled internally.
    /// </summary>
    private void SaveMapping()
    {
        if (!_mappingEditorOpen || _inputDetection.PendingInput is null) return;

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
                Name = $"{_inputDetection.PendingInput.DeviceName} Axis {_inputDetection.PendingInput.Index} -> vJoy {vjoyDevice.Id} Axis {outputIndex}",
                Inputs = new List<InputSource> { _inputDetection.PendingInput.ToInputSource() },
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
                Name = $"{_inputDetection.PendingInput.DeviceName} Button {_inputDetection.PendingInput.Index + 1} -> vJoy {vjoyDevice.Id} Button {outputIndex + 1}",
                Inputs = new List<InputSource> { _inputDetection.PendingInput.ToInputSource() },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyButton,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                },
                Mode = _buttonMode.SelectedMode
            };
            profile.ButtonMappings.Add(mapping);
        }

        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.OnMappingsChanged();
        CloseMappingEditor();
    }

    private void CreateBindingFromManualEntry()
    {
        if (!_inputDetection.ManualEntryMode || _ctx.Devices.Count == 0 || _inputDetection.SelectedSourceDevice >= _ctx.Devices.Count) return;

        var device = _ctx.Devices[_inputDetection.SelectedSourceDevice];
        _inputDetection.PendingInput = new DetectedInput
        {
            DeviceGuid = device.InstanceGuid,
            DeviceName = device.Name,
            Type = _isEditingAxis ? InputType.Axis : InputType.Button,
            Index = _inputDetection.SelectedSourceControl,
            Value = 0
        };
    }

    /// <summary>
    /// Create 1:1 mappings from the selected physical device to a user-selected vJoy device.
    /// Maps all axes, buttons, and hats directly without any curves or modifications.
    /// </summary>
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

    private static (InputType? type, int index) ParseBinding(string binding, string controlType)
    {
        // Handle button bindings: "button0", "button1", etc.
        if (binding.StartsWith("button", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(binding.AsSpan(6), out int buttonIndex))
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
            if (int.TryParse(binding.AsSpan(3), out int hatIndex))
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

    /// <summary>
    /// Assigns the currently selected button row as the network switch button.
    /// Finds the first physical button input for that row and saves it to the profile.
    /// </summary>
}