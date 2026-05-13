using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
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
    private void CreateBindingForRow(int rowIndex, DetectedInput input)
    {
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        // Use current mapping category to determine axis vs button
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        // For axes, translate visual row to actual vJoy axis index
        int outputIndex = isAxis ? AxisIndexForRow(rowIndex) : rowIndex;
        if (outputIndex < 0) return;

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
        // For axes, translate visual row to actual vJoy axis index
        int outputIndex = isAxis ? AxisIndexForRow(rowIndex) : rowIndex;
        if (outputIndex < 0) return;

        if (isAxis)
        {
            var existing = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);
            if (existing is not null)
                profile.AxisMappings.Remove(existing);

            // Also remove any AxisToButtonMapping for this axis row
            var existingA2B = profile.AxisToButtonMappings.FirstOrDefault(m =>
                m.SourceVJoyDevice == vjoyDevice.Id &&
                m.SourceAxisIndex == outputIndex);
            if (existingA2B is not null)
                profile.AxisToButtonMappings.Remove(existingA2B);
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

    private static string? FindExistingMappingForInput(MappingProfile profile, InputSource inputToCheck)
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
    private static void RemoveExistingMappingsForInput(MappingProfile profile, InputSource inputToRemove)
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
    private void SaveMappingForRow(int rowIndex, DetectedInput input, bool isAxis)
    {
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        // For axes, translate visual row to actual vJoy axis index
        int outputIndex = isAxis ? AxisIndexForRow(rowIndex) : rowIndex;
        if (outputIndex < 0) return;
        var newInputSource = input.ToInputSource();

        if (isAxis)
        {
            if (_threshold.IsThresholdMode)
            {
                // Threshold mode: add input to AxisToButtonMapping
                var existingA2B = profile.AxisToButtonMappings.FirstOrDefault(m =>
                    m.SourceVJoyDevice == vjoyDevice.Id &&
                    m.SourceAxisIndex == outputIndex);

                if (existingA2B is not null)
                {
                    existingA2B.Inputs.Add(newInputSource);
                }
                else
                {
                    var mapping = new AxisToButtonMapping
                    {
                        Name = $"{input.DeviceName} Axis {input.Index} -> Threshold Key",
                        Inputs = new List<InputSource> { newInputSource },
                        Output = new OutputTarget { Type = OutputType.Keyboard },
                        SourceVJoyDevice = vjoyDevice.Id,
                        SourceAxisIndex = outputIndex,
                    };
                    profile.AxisToButtonMappings.Add(mapping);
                }
            }
            else
            {
                // Normal axis mode
                var existingMapping = profile.AxisMappings.FirstOrDefault(m =>
                    m.Output.Type == OutputType.VJoyAxis &&
                    m.Output.VJoyDevice == vjoyDevice.Id &&
                    m.Output.Index == outputIndex);

                if (existingMapping is not null)
                {
                    existingMapping.Inputs.Add(newInputSource);
                    existingMapping.Name = $"vJoy {vjoyDevice.Id} Axis {outputIndex} ({existingMapping.Inputs.Count} inputs)";
                }
                else
                {
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
                existingMapping.Output.Type = _keyboardOutput.IsKeyboard ? OutputType.Keyboard : OutputType.VJoyButton;
                if (_keyboardOutput.IsKeyboard)
                {
                    existingMapping.Output.KeyName = _keyboardOutput.SelectedKeyName;
                    existingMapping.Output.Modifiers = _keyboardOutput.SelectedModifiers?.ToList();
                }
                else
                {
                    existingMapping.Output.KeyName = null;
                    existingMapping.Output.Modifiers = null;
                }
                existingMapping.Mode = _buttonMode.SelectedMode;
                existingMapping.Name = $"vJoy {vjoyDevice.Id} Button {outputIndex + 1} ({existingMapping.Inputs.Count} inputs)";
            }
            else
            {
                // Create new mapping using current panel settings
                var outputType = _keyboardOutput.IsKeyboard ? OutputType.Keyboard : OutputType.VJoyButton;
                var outputTarget = new OutputTarget
                {
                    Type = outputType,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                };

                if (_keyboardOutput.IsKeyboard)
                {
                    outputTarget.KeyName = _keyboardOutput.SelectedKeyName;
                    outputTarget.Modifiers = _keyboardOutput.SelectedModifiers?.ToList();
                }

                string mappingName = _keyboardOutput.IsKeyboard && !string.IsNullOrEmpty(_keyboardOutput.SelectedKeyName)
                    ? $"{input.DeviceName} Button {input.Index + 1} -> {FormatKeyComboForDisplay(_keyboardOutput.SelectedKeyName, _keyboardOutput.SelectedModifiers)}"
                    : $"{input.DeviceName} Button {input.Index + 1} -> vJoy {vjoyDevice.Id} Button {outputIndex + 1}";

                var mapping = new ButtonMapping
                {
                    Name = mappingName,
                    Inputs = new List<InputSource> { newInputSource },
                    Output = outputTarget,
                    Mode = _buttonMode.SelectedMode
                };
                profile.ButtonMappings.Add(mapping);
            }
        }

        profile.ModifiedAt = DateTime.UtcNow;
        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.OnMappingsChanged();
        _inputDetection.PendingInput = null;
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
        int outputIndex = isAxis ? AxisIndexForRow(_selectedMappingRow) : _selectedMappingRow;
        if (outputIndex < 0) return;

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

}