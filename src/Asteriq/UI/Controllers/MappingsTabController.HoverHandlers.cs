using System.Runtime.InteropServices;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
    private void ResetMoveHoverStates()
    {
        _vjoyPrevHovered = false;
        _vjoyNextHovered = false;
        _hoveredMappingRow = -1;
        _hoveredAddButton = -1;
        _hoveredRemoveButton = -1;
        _buttonMode.HoveredMode = -1;
        _keyboardOutput.HoveredOutputType = -1;
        _keyboardOutput.CaptureHovered = false;
        _keyboardOutput.ClearHovered = false;
        _addInputButtonHovered = false;
        _clearAllButtonHovered = false;
        _hoveredInputSourceRemove = -1;
        _merge.HoveredIndex = -1;
        _merge.SelectorHovered = false;
        _hoveredCurvePreset = -1;
        _hoveredDeadzonePreset = -1;
        _threshold.HoveredOutputMode = -1;
        _threshold.HoveredDirection = -1;
        _threshold.AboveCaptureHovered = false;
        _threshold.AboveClearHovered = false;
        _threshold.BelowCaptureHovered = false;
        _threshold.BelowClearHovered = false;
        _hoveredMappingCategory = -1;
        _autoScroll.CheckboxHovered = false;
    }

    private bool UpdateMappingDragState(MouseEventArgs e)
    {
        if (_buttonMode.DraggingPulse)
        { UpdatePulseDurationFromMouse(e.X); _ctx.MarkDirty(); return true; }

        if (_buttonMode.DraggingHold)
        { UpdateHoldDurationFromMouse(e.X); _ctx.MarkDirty(); return true; }

        if (_threshold.DraggingAboveThreshold)
        { UpdateThresholdFromMouse(e.X, _threshold.AboveSliderBounds, out _threshold.AboveThreshold); _ctx.MarkDirty(); return true; }
        if (_threshold.DraggingBelowThreshold)
        { UpdateThresholdFromMouse(e.X, _threshold.BelowSliderBounds, out _threshold.BelowThreshold); _ctx.MarkDirty(); return true; }
        if (_threshold.DraggingAboveHysteresis)
        { UpdateHysteresisFromMouse(e.X, _threshold.AboveHystSliderBounds, out _threshold.AboveHysteresis); _ctx.MarkDirty(); return true; }
        if (_threshold.DraggingBelowHysteresis)
        { UpdateHysteresisFromMouse(e.X, _threshold.BelowHystSliderBounds, out _threshold.BelowHysteresis); _ctx.MarkDirty(); return true; }

        return false;
    }

    private bool UpdateRightPanelHover(MouseEventArgs e)
    {
        if (_autoScroll.CheckboxBounds.Contains(e.X, e.Y))
        { _autoScroll.CheckboxHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }

        if (_addInputButtonBounds.Contains(e.X, e.Y))
        { _addInputButtonHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }

        for (int i = 0; i < _inputSourceRemoveBounds.Count; i++)
        {
            if (_inputSourceRemoveBounds[i].Contains(e.X, e.Y))
            { _hoveredInputSourceRemove = i; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
        }

        // Merge operation dropdown (axis category)
        if (_mappingCategory == 1 && _selectedMappingRow >= 0)
        {
            if (_merge.DropdownOpen && !_merge.DropdownBounds.IsEmpty
                && _merge.DropdownBounds.Contains(e.X, e.Y))
            {
                const float itemHeight = 28f;
                int idx = (int)((e.Y - _merge.DropdownBounds.Top - 2f) / itemHeight);
                _merge.HoveredIndex = (idx >= 0 && idx < s_mergeOps.Length) ? idx : -1;
                _ctx.OwnerForm.Cursor = Cursors.Hand;
                return true;
            }

            if (_merge.SelectorBounds.Contains(e.X, e.Y))
            {
                _merge.SelectorHovered = true;
                _merge.HoveredIndex = -1;
                _ctx.OwnerForm.Cursor = Cursors.Hand;
                return true;
            }
        }

        // Axis output mode toggle and threshold controls (axis category)
        if (_mappingCategory == 1 && _selectedMappingRow >= 0)
        {
            if (_threshold.AxisModeBounds.Contains(e.X, e.Y))
            { _threshold.HoveredOutputMode = 0; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
            if (_threshold.ThresholdModeBounds.Contains(e.X, e.Y))
            {
                // Don't show hand cursor when disabled (merge mode)
                bool mergeActive = !_threshold.IsThresholdMode && GetInputsForSelectedOutput().Count >= 2;
                if (!mergeActive) _ctx.OwnerForm.Cursor = Cursors.Hand;
                _threshold.HoveredOutputMode = 1;
                return true;
            }

            if (_threshold.IsThresholdMode)
            {
                if (_threshold.AboveBounds.Contains(e.X, e.Y))
                { _threshold.HoveredDirection = 0; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
                if (_threshold.BelowBounds.Contains(e.X, e.Y))
                { _threshold.HoveredDirection = 1; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }

                if (_threshold.AboveEnabled)
                {
                    if (_threshold.AboveClearBounds.HitTest(e.X, e.Y))
                    { _threshold.AboveClearHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
                    if (_threshold.AboveCaptureBounds.Contains(e.X, e.Y))
                    { _threshold.AboveCaptureHovered = true; _ctx.OwnerForm.Cursor = Cursors.IBeam; return true; }
                }
                if (_threshold.BelowEnabled)
                {
                    if (_threshold.BelowClearBounds.HitTest(e.X, e.Y))
                    { _threshold.BelowClearHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
                    if (_threshold.BelowCaptureBounds.Contains(e.X, e.Y))
                    { _threshold.BelowCaptureHovered = true; _ctx.OwnerForm.Cursor = Cursors.IBeam; return true; }
                }
            }
        }

        // Button mode controls (button category)
        if (_mappingCategory == 0 && _selectedMappingRow >= 0)
        {
            for (int i = 0; i < _buttonMode.ModeBounds.Length; i++)
            {
                if (_buttonMode.ModeBounds[i].Contains(e.X, e.Y))
                { _buttonMode.HoveredMode = i; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
            }

            if (_keyboardOutput.BtnBounds.Contains(e.X, e.Y))
            { _keyboardOutput.HoveredOutputType = 0; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
            if (_keyboardOutput.KeyBounds.Contains(e.X, e.Y))
            { _keyboardOutput.HoveredOutputType = 1; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }

            if (_keyboardOutput.IsKeyboard && _keyboardOutput.ClearBounds.HitTest(e.X, e.Y))
            { _keyboardOutput.ClearHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
            if (_keyboardOutput.IsKeyboard && _keyboardOutput.CaptureBounds.Contains(e.X, e.Y))
            { _keyboardOutput.CaptureHovered = true; _ctx.OwnerForm.Cursor = Cursors.IBeam; return true; }

            if (_clearAllButtonBounds.Contains(e.X, e.Y))
            { _clearAllButtonHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
        }

        // Manage button only exists when the row is shared-away; bounds are SKRect.Empty otherwise.
        if (_sharedManageButtonBounds.Contains(e.X, e.Y))
        { _sharedManageButtonHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }

        // Same pattern for the merged-away "go to merged axis" button.
        if (_mergedAwayManageButtonBounds.Contains(e.X, e.Y))
        { _mergedAwayManageButtonHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }

        return false;
    }

    private bool UpdateAxisEditorHover(MouseEventArgs e)
    {
        if (_mappingCategory != 1 || _selectedMappingRow < 0) return false;

        var pt = new SKPoint(e.X, e.Y);

        if (_curve.DraggingPoint >= 0)
        { UpdateDraggedCurvePoint(pt); _ctx.MarkDirty(); return true; }

        if (_deadzone.DraggingHandle >= 0)
        { UpdateDraggedDeadzoneHandle(pt); _ctx.MarkDirty(); return true; }

        // Curve preset buttons (LINEAR, S-CURVE, EXPO, CUSTOM)
        for (int i = 0; i < _curve.PresetBounds.Length; i++)
        {
            if (_curve.PresetBounds[i].Contains(pt))
            { _hoveredCurvePreset = i; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
        }

        // Deadzone preset buttons (0%, 2%, 5%, 10%)
        if (_deadzone.SelectedHandle >= 0)
        {
            for (int i = 0; i < _deadzone.PresetBounds.Length; i++)
            {
                if (_deadzone.PresetBounds[i].Contains(pt))
                { _hoveredDeadzonePreset = i; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
            }
        }

        if (_curve.SelectedType == CurveType.Custom && _curve.Bounds.Contains(pt))
        {
            int newHovered = FindCurvePointAt(pt, _curve.Bounds);
            if (newHovered != _curve.HoveredPoint)
            {
                _curve.HoveredPoint = newHovered;
                _ctx.OwnerForm.Cursor = newHovered >= 0 ? Cursors.Hand : Cursors.Cross;
                _ctx.MarkDirty();
            }
            return true;
        }

        if (_curve.HoveredPoint >= 0)
        {
            _curve.HoveredPoint = -1;
            _ctx.MarkDirty();
        }

        return false;
    }

    private bool UpdateNetSwitchHover(MouseEventArgs e)
    {
        if (_netSwitch.ActionBounds.HitTest(e.X, e.Y))
        { _netSwitch.ActionHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }

        if (_netSwitch.BadgeXBounds.HitTest(e.X, e.Y))
        { _netSwitch.BadgeXHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }

        return false;
    }

    private bool UpdateVJoyNavigationHover(MouseEventArgs e)
    {
        if (_vjoyPrevButtonBounds.Contains(e.X, e.Y) && _ctx.SelectedVJoyDeviceIndex > 0)
        { _vjoyPrevHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }

        if (_vjoyNextButtonBounds.Contains(e.X, e.Y) && _ctx.SelectedVJoyDeviceIndex < _ctx.VJoyDevices.Count - 1)
        { _vjoyNextHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }

        return false;
    }

    private bool UpdateMappingRowHover(MouseEventArgs e)
    {
        for (int i = 0; i < _mappingRowBounds.Count; i++)
        {
            if (_mappingRowBounds[i].Contains(e.X, e.Y))
            { _hoveredMappingRow = i; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
        }
        return false;
    }

    private void UpdateCategoryTabHover(MouseEventArgs e)
    {
        if (_mappingCategoryButtonsBounds.Contains(e.X, e.Y))
        { _hoveredMappingCategory = 0; _ctx.OwnerForm.Cursor = Cursors.Hand; }
        else if (_mappingCategoryAxesBounds.Contains(e.X, e.Y))
        { _hoveredMappingCategory = 1; _ctx.OwnerForm.Cursor = Cursors.Hand; }
    }

}