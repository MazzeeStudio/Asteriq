using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
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
}