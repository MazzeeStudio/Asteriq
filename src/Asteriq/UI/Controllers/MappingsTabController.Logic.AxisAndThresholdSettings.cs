using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
    private int AxisIndexForRow(int row) =>
        row >= 0 && row < _visibleAxisIndices.Count ? _visibleAxisIndices[row] : -1;

    /// <summary>
    /// Switches the Mappings tab to a specific vJoy device + axis slot. Used by the
    /// "GO TO MERGED AXIS" button on the merged-away panel. _visibleAxisIndices is
    /// recomputed each render, so we resolve the row against the target device's axis
    /// list directly rather than the currently-cached one.
    /// </summary>
    private void NavigateToAxisSlot(uint vjoyDeviceId, int axisIndex)
    {
        int devIndex = -1;
        for (int i = 0; i < _ctx.VJoyDevices.Count; i++)
        {
            if (_ctx.VJoyDevices[i].Id == vjoyDeviceId) { devIndex = i; break; }
        }
        if (devIndex < 0) return;

        _ctx.SelectedVJoyDeviceIndex = devIndex;
        _mappingCategory = 1;

        var axisIndices = GetVJoyAxisIndices(_ctx.VJoyDevices[devIndex]);
        int row = axisIndices.IndexOf(axisIndex);
        if (row < 0) return;

        _selectedMappingRow = row;
        _selectionIsExplicit = true;
    }

    /// <summary>
    /// Get a human-readable name for a vJoy axis index.
    /// </summary>
    private static string GetVJoyAxisName(int index)
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
    private void UpdateMergeOperationForSelected(MergeOperation op)
    {
        var axisMapping = GetCurrentAxisMapping();
        if (axisMapping is null) return;

        axisMapping.MergeOp = op;

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
        _curve.SelectedType = CurveType.Linear;
        _curve.ControlPoints = new List<SKPoint> { new(0, 0), new(1, 1) };
        _curve.Symmetrical = false;
        _deadzone.Min = -1.0f;
        _deadzone.CenterMin = 0.0f;
        _deadzone.CenterMax = 0.0f;
        _deadzone.Max = 1.0f;
        _deadzone.CenterEnabled = false;
        _deadzone.AxisInverted = false;
        _threshold.IsThresholdMode = false;
        _threshold.AboveEnabled = false;
        _threshold.BelowEnabled = false;
        _threshold.AboveThreshold = 0.5f;
        _threshold.AboveHysteresis = 0.05f;
        _threshold.AboveKeyName = "";
        _threshold.AboveModifiers = null;
        _threshold.AboveCapturing = false;
        _threshold.BelowThreshold = -0.5f;
        _threshold.BelowHysteresis = 0.05f;
        _threshold.BelowKeyName = "";
        _threshold.BelowModifiers = null;
        _threshold.BelowCapturing = false;

        // Only for axis category
        if (_mappingCategory != 1) return;
        if (_selectedMappingRow < 0) return;

        // Check for AxisToButtonMappings (threshold mode — can have above, below, or both)
        var a2bMappings = GetCurrentAxisToButtonMappings();
        if (a2bMappings.Count > 0)
        {
            _threshold.IsThresholdMode = true;
            foreach (var m in a2bMappings)
            {
                if (m.ActivateAbove)
                {
                    _threshold.AboveEnabled = true;
                    _threshold.AboveThreshold = m.Threshold;
                    _threshold.AboveHysteresis = m.Hysteresis;
                    _threshold.AboveKeyName = m.Output.KeyName ?? "";
                    _threshold.AboveModifiers = m.Output.Modifiers;
                }
                else
                {
                    _threshold.BelowEnabled = true;
                    _threshold.BelowThreshold = m.Threshold;
                    _threshold.BelowHysteresis = m.Hysteresis;
                    _threshold.BelowKeyName = m.Output.KeyName ?? "";
                    _threshold.BelowModifiers = m.Output.Modifiers;
                }
            }
            // If only one direction loaded, default the other to enabled for fresh setups
            if (!_threshold.AboveEnabled && !_threshold.BelowEnabled)
                _threshold.AboveEnabled = true;
            return;
        }

        var axisMapping = GetCurrentAxisMapping();
        if (axisMapping is null) return;

        // Load curve settings from mapping
        var curve = axisMapping.Curve;
        _curve.SelectedType = curve.Type;
        _curve.Symmetrical = curve.Symmetrical;
        _deadzone.AxisInverted = axisMapping.Invert;

        // Load deadzone settings
        _deadzone.Min = curve.DeadzoneLow;
        _deadzone.CenterMin = curve.DeadzoneCenterLow;
        _deadzone.CenterMax = curve.DeadzoneCenterHigh;
        _deadzone.Max = curve.DeadzoneHigh;
        _deadzone.CenterEnabled = curve.DeadzoneCenterLow > 0f || curve.DeadzoneCenterHigh > 0f;

        // Load control points for custom curve
        if (curve.Type == CurveType.Custom && curve.ControlPoints is not null && curve.ControlPoints.Count >= 2)
        {
            _curve.ControlPoints = curve.ControlPoints.Select(p => new SKPoint(p.Input, p.Output)).ToList();
        }
        else
        {
            // Generate default control points based on curve type
            _curve.ControlPoints = curve.Type switch
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
        axisMapping.Curve.Type = _curve.SelectedType;
        axisMapping.Curve.Symmetrical = _curve.Symmetrical;
        axisMapping.Invert = _deadzone.AxisInverted;

        // Save deadzone settings
        axisMapping.Curve.DeadzoneLow = _deadzone.Min;
        axisMapping.Curve.DeadzoneCenterLow = _deadzone.CenterEnabled ? _deadzone.CenterMin : 0f;
        axisMapping.Curve.DeadzoneCenterHigh = _deadzone.CenterEnabled ? _deadzone.CenterMax : 0f;
        axisMapping.Curve.DeadzoneHigh = _deadzone.Max;

        // Save control points for custom curve
        if (_curve.SelectedType == CurveType.Custom)
        {
            axisMapping.Curve.ControlPoints = _curve.ControlPoints
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

    private void SaveThresholdSettingsForRow()
    {
        if (_mappingCategory != 1 || _selectedMappingRow < 0) return;
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;
        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];

        // Get inputs from any existing mapping to preserve them
        var existingMappings = GetCurrentAxisToButtonMappings();
        var inputs = existingMappings.FirstOrDefault()?.Inputs ?? new List<InputSource>();

        // Remove all existing threshold mappings for this axis row
        int axisIdx = AxisIndexForRow(_selectedMappingRow);
        if (axisIdx < 0) return;

        profile.AxisToButtonMappings.RemoveAll(m =>
            m.SourceVJoyDevice == vjoyDevice.Id &&
            m.SourceAxisIndex == axisIdx);

        // Re-create enabled mappings
        if (_threshold.AboveEnabled)
        {
            profile.AxisToButtonMappings.Add(new AxisToButtonMapping
            {
                Inputs = inputs,
                Output = new OutputTarget { Type = OutputType.Keyboard, KeyName = _threshold.AboveKeyName, Modifiers = _threshold.AboveModifiers },
                Threshold = _threshold.AboveThreshold,
                ActivateAbove = true,
                Hysteresis = _threshold.AboveHysteresis,
                SourceVJoyDevice = vjoyDevice.Id,
                SourceAxisIndex = axisIdx,
            });
        }
        if (_threshold.BelowEnabled)
        {
            profile.AxisToButtonMappings.Add(new AxisToButtonMapping
            {
                Inputs = inputs,
                Output = new OutputTarget { Type = OutputType.Keyboard, KeyName = _threshold.BelowKeyName, Modifiers = _threshold.BelowModifiers },
                Threshold = _threshold.BelowThreshold,
                ActivateAbove = false,
                Hysteresis = _threshold.BelowHysteresis,
                SourceVJoyDevice = vjoyDevice.Id,
                SourceAxisIndex = axisIdx,
            });
        }

        profile.ModifiedAt = DateTime.UtcNow;
        _ctx.ProfileManager.SaveActiveProfile();
    }

    private void SwitchToThresholdMode()
    {
        if (_mappingCategory != 1 || _selectedMappingRow < 0) return;
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;
        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int axisIdx = AxisIndexForRow(_selectedMappingRow);
        if (axisIdx < 0) return;

        // Preserve input from existing AxisMapping
        var existingAxis = GetCurrentAxisMapping();
        var inputs = existingAxis?.Inputs ?? new List<InputSource>();

        // Remove existing AxisMapping
        if (existingAxis is not null)
            profile.AxisMappings.Remove(existingAxis);

        // Create AxisToButtonMapping (above enabled by default)
        profile.AxisToButtonMappings.Add(new AxisToButtonMapping
        {
            Inputs = inputs,
            Output = new OutputTarget { Type = OutputType.Keyboard },
            Threshold = 0.5f,
            ActivateAbove = true,
            Hysteresis = 0.05f,
            SourceVJoyDevice = vjoyDevice.Id,
            SourceAxisIndex = axisIdx,
        });

        profile.ModifiedAt = DateTime.UtcNow;
        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.OnMappingsChanged();
        LoadAxisSettingsForRow();
        _ctx.MarkDirty();
    }

    private void SwitchToAxisMode()
    {
        if (_mappingCategory != 1 || _selectedMappingRow < 0) return;
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;
        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int axisIdx = AxisIndexForRow(_selectedMappingRow);
        if (axisIdx < 0) return;

        // Preserve inputs from existing AxisToButtonMappings
        var existingA2Bs = GetCurrentAxisToButtonMappings();
        var inputs = existingA2Bs.FirstOrDefault()?.Inputs ?? new List<InputSource>();

        // Remove all existing AxisToButtonMappings for this axis row
        profile.AxisToButtonMappings.RemoveAll(m =>
            m.SourceVJoyDevice == vjoyDevice.Id &&
            m.SourceAxisIndex == axisIdx);

        // Create AxisMapping with default curve
        var mapping = new AxisMapping
        {
            Inputs = inputs,
            Output = new OutputTarget
            {
                Type = OutputType.VJoyAxis,
                VJoyDevice = vjoyDevice.Id,
                Index = axisIdx,
            },
            Curve = new AxisCurve(),
        };
        profile.AxisMappings.Add(mapping);

        profile.ModifiedAt = DateTime.UtcNow;
        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.OnMappingsChanged();
        LoadAxisSettingsForRow();
        _ctx.MarkDirty();
    }

}