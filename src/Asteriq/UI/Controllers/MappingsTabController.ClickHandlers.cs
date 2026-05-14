using System.Runtime.InteropServices;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
    private void HandleRightClick(MouseEventArgs e)
    {
        if (_mappingCategory == 1 && _curve.SelectedType == CurveType.Custom)
        {
            var pt = new SKPoint(e.X, e.Y);
            if (_curve.Bounds.Contains(pt))
            {
                int pointIndex = FindCurvePointAt(pt, _curve.Bounds);
                if (pointIndex >= 0)
                    RemoveCurveControlPoint(pointIndex);
            }
        }
    }

    private bool HandleCategoryTabClick()
    {
        if (_hoveredMappingCategory < 0) return false;
        _mappingCategory = _hoveredMappingCategory;
        _selectedMappingRow = -1;
        _selectionIsExplicit = false;
        _listScroll.ScrollOffset = 0;
        CancelInputListening();
        SelectFirstRowIfUnselected();
        return true;
    }

    private bool HandleAutoScrollClick(MouseEventArgs e)
    {
        if (!_autoScroll.CheckboxBounds.Contains(e.X, e.Y)) return false;
        _autoScroll.Enabled = !_autoScroll.Enabled;
        _ctx.MarkDirty();
        return true;
    }

    private bool HandleRightPanelClick(MouseEventArgs e) =>
        TryClickPriorityManageButtons(e)
        || TryClickAddInputButton(e)
        || TryClickInputSourceRemoveButtons(e)
        || TryClickMergeDropdown(e)
        || TryClickButtonCategoryEditor(e);

    // Manage in Keybindings first so stale bounds from a previously-rendered normal
    // editor (e.g. _addInputButtonBounds, _clearAllButtonBounds) cannot intercept the
    // click. _sharedManageButtonBounds and _mergedAwayManageButtonBounds are SKRect.Empty
    // unless their respective panels are active.
    private bool TryClickPriorityManageButtons(MouseEventArgs e)
    {
        if (_sharedManageButtonBounds.Contains(e.X, e.Y) && !string.IsNullOrEmpty(_sharedManageSearchText))
        {
            _ctx.OpenSCBindingsWithSearch?.Invoke(_sharedManageVJoyDevice, _sharedManageSearchText);
            return true;
        }

        if (_mergedAwayManageButtonBounds.Contains(e.X, e.Y) && _mergedAwayManageTarget is not null)
        {
            NavigateToAxisSlot(_mergedAwayManageTarget.Value.vjoyDevice, _mergedAwayManageTarget.Value.axisIndex);
            return true;
        }

        return false;
    }

    private bool TryClickAddInputButton(MouseEventArgs e)
    {
        if (!_addInputButtonBounds.Contains(e.X, e.Y)) return false;
        if (_selectedMappingRow < 0 || !_selectionIsExplicit) return false;

        if (_inputDetection.IsListening) CancelInputListening();
        else StartInputListening(_selectedMappingRow);
        return true;
    }

    private bool TryClickInputSourceRemoveButtons(MouseEventArgs e)
    {
        if (!_selectionIsExplicit) return false;
        for (int i = 0; i < _inputSourceRemoveBounds.Count; i++)
        {
            if (_inputSourceRemoveBounds[i].Contains(e.X, e.Y))
            {
                RemoveInputSourceAtIndex(i);
                return true;
            }
        }
        return false;
    }

    // Merge operation dropdown (axis category, 2+ inputs). When the dropdown is open,
    // a click inside the list selects + closes; a click outside both the list and the
    // selector closes (and swallows the click); a click on the selector itself falls
    // through to the toggle below.
    private bool TryClickMergeDropdown(MouseEventArgs e)
    {
        if (_mappingCategory != 1 || _selectedMappingRow < 0 || !_selectionIsExplicit)
            return false;

        if (_merge.DropdownOpen)
        {
            if (!_merge.DropdownBounds.IsEmpty && _merge.DropdownBounds.Contains(e.X, e.Y))
            {
                const float itemHeight = 28f;
                int idx = (int)((e.Y - _merge.DropdownBounds.Top - 2f) / itemHeight);
                if (idx >= 0 && idx < s_mergeOps.Length)
                    UpdateMergeOperationForSelected(s_mergeOps[idx]);
                _merge.DropdownOpen = false;
                _merge.HoveredIndex = -1;
                return true;
            }

            if (!_merge.SelectorBounds.Contains(e.X, e.Y))
            {
                _merge.DropdownOpen = false;
                _merge.HoveredIndex = -1;
                return true;
            }
        }

        if (_merge.SelectorBounds.Contains(e.X, e.Y))
        {
            _merge.DropdownOpen = !_merge.DropdownOpen;
            if (!_merge.DropdownOpen) _merge.HoveredIndex = -1;
            return true;
        }

        return false;
    }

    private bool TryClickButtonCategoryEditor(MouseEventArgs e)
    {
        if (_mappingCategory != 0 || _selectedMappingRow < 0 || !_selectionIsExplicit)
            return false;

        return TryClickButtonModeButtons(e)
            || TryClickPulseSlider(e)
            || TryClickHoldSlider(e)
            || TryClickOutputTypeButtons(e)
            || TryClickKeyCapture(e)
            || TryClickClearMapping(e);
    }

    private bool TryClickButtonModeButtons(MouseEventArgs e)
    {
        // Blocked for modifier keys
        bool selectedIsModifier = _keyboardOutput.IsKeyboard && IsModifierKeyName(_keyboardOutput.SelectedKeyName);
        if (selectedIsModifier) return false;

        for (int i = 0; i < _buttonMode.ModeBounds.Length; i++)
        {
            if (_buttonMode.ModeBounds[i].Contains(e.X, e.Y))
            {
                _buttonMode.SelectedMode = (ButtonMode)i;
                UpdateButtonModeForSelected();
                return true;
            }
        }
        return false;
    }

    private bool TryClickPulseSlider(MouseEventArgs e)
    {
        if (_buttonMode.SelectedMode != ButtonMode.Pulse) return false;
        if (!_buttonMode.PulseSliderBounds.Contains(e.X, e.Y)) return false;
        _buttonMode.DraggingPulse = true;
        UpdatePulseDurationFromMouse(e.X);
        return true;
    }

    private bool TryClickHoldSlider(MouseEventArgs e)
    {
        if (_buttonMode.SelectedMode != ButtonMode.HoldToActivate) return false;
        if (!_buttonMode.HoldSliderBounds.Contains(e.X, e.Y)) return false;
        _buttonMode.DraggingHold = true;
        UpdateHoldDurationFromMouse(e.X);
        return true;
    }

    private bool TryClickOutputTypeButtons(MouseEventArgs e)
    {
        if (_keyboardOutput.BtnBounds.Contains(e.X, e.Y))
        {
            _keyboardOutput.IsKeyboard = false;
            _keyboardOutput.SelectedKeyName = "";
            UpdateOutputTypeForSelected();
            return true;
        }
        if (_keyboardOutput.KeyBounds.Contains(e.X, e.Y))
        {
            _keyboardOutput.IsKeyboard = true;
            UpdateOutputTypeForSelected();
            return true;
        }
        return false;
    }

    private bool TryClickKeyCapture(MouseEventArgs e)
    {
        if (!_keyboardOutput.IsKeyboard) return false;
        if (!_keyboardOutput.CaptureBounds.Contains(e.X, e.Y)) return false;
        _keyboardOutput.IsCapturing = true;
        _keyboardOutput.CaptureStartTicks = Environment.TickCount64;
        return true;
    }

    private bool TryClickClearMapping(MouseEventArgs e)
    {
        if (!_clearAllButtonBounds.Contains(e.X, e.Y)) return false;
        ClearSelectedButtonMapping();
        return true;
    }

    private bool HandleThresholdClick(MouseEventArgs e)
    {
        if (_mappingCategory != 1 || _selectedMappingRow < 0)
            return false;

        // Output mode toggle
        if (_threshold.AxisModeBounds.Contains(e.X, e.Y) && _threshold.IsThresholdMode)
        {
            SwitchToAxisMode();
            return true;
        }
        if (_threshold.ThresholdModeBounds.Contains(e.X, e.Y) && !_threshold.IsThresholdMode)
        {
            // Block switch to threshold when merge mode (2+ inputs)
            var inputs = GetInputsForSelectedOutput();
            if (inputs.Count >= 2) return true;
            SwitchToThresholdMode();
            return true;
        }

        if (!_threshold.IsThresholdMode) return false;

        // Direction checkboxes
        if (_threshold.AboveBounds.Contains(e.X, e.Y))
        {
            _threshold.AboveEnabled = !_threshold.AboveEnabled;
            if (!_threshold.AboveEnabled && !_threshold.BelowEnabled)
                _threshold.BelowEnabled = true; // At least one must be active
            SaveThresholdSettingsForRow();
            _ctx.MarkDirty();
            return true;
        }
        if (_threshold.BelowBounds.Contains(e.X, e.Y))
        {
            _threshold.BelowEnabled = !_threshold.BelowEnabled;
            if (!_threshold.AboveEnabled && !_threshold.BelowEnabled)
                _threshold.AboveEnabled = true; // At least one must be active
            SaveThresholdSettingsForRow();
            _ctx.MarkDirty();
            return true;
        }

        // Above threshold controls (clear before capture â€” clear bounds are inside capture bounds)
        if (_threshold.AboveEnabled)
        {
            if (_threshold.AboveSliderBounds.Contains(e.X, e.Y))
            {
                _threshold.DraggingAboveThreshold = true;
                UpdateThresholdFromMouse(e.X, _threshold.AboveSliderBounds, out _threshold.AboveThreshold);
                _ctx.MarkDirty();
                return true;
            }
            if (_threshold.AboveHystSliderBounds.Contains(e.X, e.Y))
            {
                _threshold.DraggingAboveHysteresis = true;
                UpdateHysteresisFromMouse(e.X, _threshold.AboveHystSliderBounds, out _threshold.AboveHysteresis);
                _ctx.MarkDirty();
                return true;
            }
            if (_threshold.AboveClearBounds.HitTest(e.X, e.Y) && !string.IsNullOrEmpty(_threshold.AboveKeyName))
            {
                _threshold.AboveKeyName = "";
                _threshold.AboveModifiers = null;
                SaveThresholdSettingsForRow();
                _ctx.MarkDirty();
                return true;
            }
            if (_threshold.AboveCaptureBounds.Contains(e.X, e.Y))
            {
                _threshold.AboveCapturing = true;
                _threshold.AboveCaptureStartTicks = Environment.TickCount64;
                _ctx.MarkDirty();
                return true;
            }
        }

        // Below threshold controls (clear before capture â€” clear bounds are inside capture bounds)
        if (_threshold.BelowEnabled)
        {
            if (_threshold.BelowSliderBounds.Contains(e.X, e.Y))
            {
                _threshold.DraggingBelowThreshold = true;
                UpdateThresholdFromMouse(e.X, _threshold.BelowSliderBounds, out _threshold.BelowThreshold);
                _ctx.MarkDirty();
                return true;
            }
            if (_threshold.BelowHystSliderBounds.Contains(e.X, e.Y))
            {
                _threshold.DraggingBelowHysteresis = true;
                UpdateHysteresisFromMouse(e.X, _threshold.BelowHystSliderBounds, out _threshold.BelowHysteresis);
                _ctx.MarkDirty();
                return true;
            }
            if (_threshold.BelowClearBounds.HitTest(e.X, e.Y) && !string.IsNullOrEmpty(_threshold.BelowKeyName))
            {
                _threshold.BelowKeyName = "";
                _threshold.BelowModifiers = null;
                SaveThresholdSettingsForRow();
                _ctx.MarkDirty();
                return true;
            }
            if (_threshold.BelowCaptureBounds.Contains(e.X, e.Y))
            {
                _threshold.BelowCapturing = true;
                _threshold.BelowCaptureStartTicks = Environment.TickCount64;
                _ctx.MarkDirty();
                return true;
            }
        }

        return false;
    }

    private static void UpdateThresholdFromMouse(float mouseX, SKRect bounds, out float threshold)
    {
        if (bounds.Width <= 0) { threshold = 0; return; }
        float norm = Math.Clamp((mouseX - bounds.Left) / bounds.Width, 0f, 1f);
        threshold = (norm * 2f) - 1f; // Map 0..1 to -1..1
    }

    private static void UpdateHysteresisFromMouse(float mouseX, SKRect bounds, out float hysteresis)
    {
        if (bounds.Width <= 0) { hysteresis = 0.05f; return; }
        float norm = Math.Clamp((mouseX - bounds.Left) / bounds.Width, 0f, 1f);
        hysteresis = norm * 0.25f; // Map 0..1 to 0..0.25
    }

    private bool HandleAxisSettingsClick(MouseEventArgs e)
    {
        if (_mappingCategory != 1 || _selectedMappingRow < 0 || !_selectionIsExplicit)
            return false;

        var pt = new SKPoint(e.X, e.Y);

        if (HandleCurvePresetClick(pt))
            return true;

        // Curve control point drag start
        if (_curve.SelectedType == CurveType.Custom && _curve.Bounds.Contains(pt))
        {
            int pointIndex = FindCurvePointAt(pt, _curve.Bounds);
            if (pointIndex >= 0)
            {
                _curve.DraggingPoint = pointIndex;
                return true;
            }

            var graphPt = CurveScreenToGraph(pt, _curve.Bounds);
            AddCurveControlPoint(graphPt);
            return true;
        }

        // Deadzone handle click
        int dzHandle = FindDeadzoneHandleAt(pt);
        if (dzHandle >= 0)
        {
            _deadzone.SelectedHandle = dzHandle;
            _deadzone.DraggingHandle = dzHandle;
            _ctx.MarkDirty();
            return true;
        }

        // Click on slider background â€” deselect
        if (_deadzone.SliderBounds.Contains(pt))
        {
            _deadzone.SelectedHandle = -1;
            _ctx.MarkDirty();
            return true;
        }

        return false;
    }

    private bool HandleNetSwitchClick(MouseEventArgs e)
    {
        if (_netSwitch.ActionBounds.HitTest(e.X, e.Y))
        { AssignSwitchButtonForSelectedRow(); return true; }

        if (_netSwitch.BadgeXBounds.HitTest(e.X, e.Y))
        { ClearNetworkSwitchButton(); return true; }

        return false;
    }

    private bool HandleVJoyNavigationClick(MouseEventArgs e)
    {
        if (_vjoyPrevButtonBounds.Contains(e.X, e.Y) && _ctx.SelectedVJoyDeviceIndex > 0)
        {
            _ctx.SelectedVJoyDeviceIndex--;
            ResetMappingSelectionState();
            return true;
        }

        if (_vjoyNextButtonBounds.Contains(e.X, e.Y) && _ctx.SelectedVJoyDeviceIndex < _ctx.VJoyDevices.Count - 1)
        {
            _ctx.SelectedVJoyDeviceIndex++;
            ResetMappingSelectionState();
            return true;
        }

        return false;
    }

    private void ResetMappingSelectionState()
    {
        _selectedMappingRow = -1;
        _selectionIsExplicit = false;
        _listScroll.ScrollOffset = 0;
        _highlight.ControlDef = null;
        CancelInputListening();
        _ctx.UpdateMappingsPrimaryDeviceMap();
        SelectFirstRowIfUnselected();
        _ctx.MarkDirty();
    }

    private void HandleMappingRowClick()
    {
        if (_hoveredMappingRow < 0) return;

        if (_hoveredMappingRow != _selectedMappingRow)
        {
            CancelInputListening();
            _selectedMappingRow = _hoveredMappingRow;
            LoadOutputTypeStateForRow();
            LoadAxisSettingsForRow();
        }
        else
        {
            _selectedMappingRow = _hoveredMappingRow;
        }

        _selectionIsExplicit = true;
        _highlight.ControlDef = GetControlForRow(_selectedMappingRow);
        _highlight.ControlHighlightTicks = Environment.TickCount64;
    }

}