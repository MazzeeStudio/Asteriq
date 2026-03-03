using Asteriq.Models;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
    private int FindCurvePointAt(SKPoint screenPt, SKRect bounds)
    {
        const float HitRadius = 12f;

        for (int i = 0; i < _curve.ControlPoints.Count; i++)
        {
            var pt = _curve.ControlPoints[i];

            // Skip center point - it's not selectable
            bool isCenterPoint = Math.Abs(pt.X - 0.5f) < 0.01f && Math.Abs(pt.Y - 0.5f) < 0.01f;
            if (isCenterPoint)
                continue;

            float x = bounds.Left + pt.X * bounds.Width;

            // Apply inversion to display Y position to match the visual
            float displayY = _deadzone.AxisInverted ? (1f - pt.Y) : pt.Y;
            float y = bounds.Bottom - displayY * bounds.Height;

            float dist = MathF.Sqrt(MathF.Pow(screenPt.X - x, 2) + MathF.Pow(screenPt.Y - y, 2));
            if (dist <= HitRadius)
                return i;
        }
        return -1;
    }

    private int FindDeadzoneHandleAt(SKPoint screenPt)
    {
        const float HitRadius = 12f;
        var bounds = _deadzone.SliderBounds;
        if (bounds.Width <= 0) return -1;

        // Convert deadzone values to 0..1 range
        float minPos = (_deadzone.Min + 1f) / 2f;
        float centerMinPos = (_deadzone.CenterMin + 1f) / 2f;
        float centerMaxPos = (_deadzone.CenterMax + 1f) / 2f;
        float maxPos = (_deadzone.Max + 1f) / 2f;

        if (_deadzone.CenterEnabled)
        {
            // Two separate tracks - must calculate handle positions on each track
            // Gap must match DrawDualDeadzoneSlider
            float gap = 24f;
            float centerX = bounds.MidX;

            // Left track: from bounds.Left to centerX - gap/2
            float leftTrackLeft = bounds.Left;
            float leftTrackRight = centerX - gap / 2;
            float leftTrackWidth = leftTrackRight - leftTrackLeft;

            // Right track: from centerX + gap/2 to bounds.Right
            float rightTrackLeft = centerX + gap / 2;
            float rightTrackRight = bounds.Right;
            float rightTrackWidth = rightTrackRight - rightTrackLeft;

            // Map positions to track coordinates
            float minPosInLeft = Math.Clamp((minPos - 0f) / 0.5f, 0f, 1f);
            float ctrMinPosInLeft = Math.Clamp((centerMinPos - 0f) / 0.5f, 0f, 1f);
            float ctrMaxPosInRight = Math.Clamp((centerMaxPos - 0.5f) / 0.5f, 0f, 1f);
            float maxPosInRight = Math.Clamp((maxPos - 0.5f) / 0.5f, 0f, 1f);

            // Calculate screen positions for each handle
            float minHandleX = leftTrackLeft + minPosInLeft * leftTrackWidth;
            float ctrMinHandleX = leftTrackLeft + ctrMinPosInLeft * leftTrackWidth;
            float ctrMaxHandleX = rightTrackLeft + ctrMaxPosInRight * rightTrackWidth;
            float maxHandleX = rightTrackLeft + maxPosInRight * rightTrackWidth;

            // Check each handle (check all 4)
            float[] handleXs = { minHandleX, ctrMinHandleX, ctrMaxHandleX, maxHandleX };
            for (int i = 0; i < 4; i++)
            {
                float dist = MathF.Sqrt(MathF.Pow(screenPt.X - handleXs[i], 2) + MathF.Pow(screenPt.Y - bounds.MidY, 2));
                if (dist <= HitRadius)
                    return i;
            }
        }
        else
        {
            // Single track - only min (0) and max (3) handles
            float minHandleX = bounds.Left + minPos * bounds.Width;
            float maxHandleX = bounds.Left + maxPos * bounds.Width;

            float distMin = MathF.Sqrt(MathF.Pow(screenPt.X - minHandleX, 2) + MathF.Pow(screenPt.Y - bounds.MidY, 2));
            if (distMin <= HitRadius) return 0;

            float distMax = MathF.Sqrt(MathF.Pow(screenPt.X - maxHandleX, 2) + MathF.Pow(screenPt.Y - bounds.MidY, 2));
            if (distMax <= HitRadius) return 3;
        }

        return -1;
    }

    private void UpdateDraggedDeadzoneHandle(SKPoint screenPt)
    {
        if (_deadzone.DraggingHandle < 0) return;
        var bounds = _deadzone.SliderBounds;
        if (bounds.Width <= 0) return;

        float value;

        if (_deadzone.CenterEnabled)
        {
            // Two-track layout - convert screen position to value based on which track
            // Gap must match DrawDualDeadzoneSlider
            float gap = 24f;
            float centerX = bounds.MidX;

            // Left track: maps to -1..0 (handles 0 and 1)
            float leftTrackLeft = bounds.Left;
            float leftTrackRight = centerX - gap / 2;
            float leftTrackWidth = leftTrackRight - leftTrackLeft;

            // Right track: maps to 0..1 (handles 2 and 3)
            float rightTrackLeft = centerX + gap / 2;
            float rightTrackRight = bounds.Right;
            float rightTrackWidth = rightTrackRight - rightTrackLeft;

            switch (_deadzone.DraggingHandle)
            {
                case 0: // Min handle on left track
                    float normLeft0 = Math.Clamp((screenPt.X - leftTrackLeft) / leftTrackWidth, 0f, 1f);
                    value = normLeft0 - 1f; // Maps 0..1 to -1..0
                    value = Math.Clamp(value, -1f, _deadzone.CenterMin - 0.02f);
                    _deadzone.Min = value;
                    break;
                case 1: // CenterMin handle on left track (right edge)
                    float normLeft1 = Math.Clamp((screenPt.X - leftTrackLeft) / leftTrackWidth, 0f, 1f);
                    value = normLeft1 - 1f; // Maps 0..1 to -1..0
                    value = Math.Clamp(value, _deadzone.Min + 0.02f, 0f);
                    _deadzone.CenterMin = value;
                    break;
                case 2: // CenterMax handle on right track (left edge)
                    float normRight2 = Math.Clamp((screenPt.X - rightTrackLeft) / rightTrackWidth, 0f, 1f);
                    value = normRight2; // Maps 0..1 to 0..1
                    value = Math.Clamp(value, 0f, _deadzone.Max - 0.02f);
                    _deadzone.CenterMax = value;
                    break;
                case 3: // Max handle on right track
                    float normRight3 = Math.Clamp((screenPt.X - rightTrackLeft) / rightTrackWidth, 0f, 1f);
                    value = normRight3; // Maps 0..1 to 0..1
                    value = Math.Clamp(value, _deadzone.CenterMax + 0.02f, 1f);
                    _deadzone.Max = value;
                    break;
            }
        }
        else
        {
            // Single track layout - convert screen X to -1..1 range
            float normalized = (screenPt.X - bounds.Left) / bounds.Width;
            value = normalized * 2f - 1f;

            switch (_deadzone.DraggingHandle)
            {
                case 0: // Min handle
                    value = Math.Clamp(value, -1f, _deadzone.Max - 0.1f);
                    _deadzone.Min = value;
                    break;
                case 3: // Max handle
                    value = Math.Clamp(value, _deadzone.Min + 0.1f, 1f);
                    _deadzone.Max = value;
                    break;
            }
        }
    }

    private void UpdateDraggedCurvePoint(SKPoint screenPt)
    {
        if (_curve.DraggingPoint < 0 || _curve.DraggingPoint >= _curve.ControlPoints.Count)
            return;

        var graphPt = CurveScreenToGraph(screenPt, _curve.Bounds);

        // Constrain endpoints to X edges
        if (_curve.DraggingPoint == 0)
            graphPt.X = 0;
        else if (_curve.DraggingPoint == _curve.ControlPoints.Count - 1)
            graphPt.X = 1;
        else
        {
            // Interior points: constrain X between neighbors
            float minX = _curve.ControlPoints[_curve.DraggingPoint - 1].X + 0.02f;
            float maxX = _curve.ControlPoints[_curve.DraggingPoint + 1].X - 0.02f;
            // Ensure minX <= maxX (neighbors might be very close)
            if (minX > maxX)
            {
                float midX = (_curve.ControlPoints[_curve.DraggingPoint - 1].X + _curve.ControlPoints[_curve.DraggingPoint + 1].X) / 2f;
                graphPt.X = midX;
            }
            else
            {
                graphPt.X = Math.Clamp(graphPt.X, minX, maxX);
            }
        }

        _curve.ControlPoints[_curve.DraggingPoint] = graphPt;

        // If symmetrical mode is enabled, mirror the change
        if (_curve.Symmetrical)
        {
            UpdateSymmetricalPoint(_curve.DraggingPoint, graphPt);
        }
    }

    /// <summary>
    /// Update the symmetrical counterpart of a curve point.
    /// Points are mirrored around the center (0.5, 0.5).
    /// </summary>
    private void UpdateSymmetricalPoint(int pointIndex, SKPoint graphPt)
    {
        // Mirror point: (x, y) -> (1-x, 1-y)
        float mirrorX = 1f - graphPt.X;
        float mirrorY = 1f - graphPt.Y;
        var mirrorPt = new SKPoint(mirrorX, mirrorY);

        // Find the corresponding mirror point in the list
        // Points are stored sorted by X, so we need to find the one with matching mirror X
        int mirrorIndex = FindMirrorPointIndex(pointIndex, mirrorX);

        if (mirrorIndex >= 0 && mirrorIndex != pointIndex)
        {
            // Update mirror point, but constrain to valid range
            if (mirrorIndex > 0 && mirrorIndex < _curve.ControlPoints.Count - 1)
            {
                // Interior point - constrain X between neighbors
                float minX = _curve.ControlPoints[mirrorIndex - 1].X + 0.02f;
                float maxX = _curve.ControlPoints[mirrorIndex + 1].X - 0.02f;
                mirrorPt = new SKPoint(Math.Clamp(mirrorPt.X, minX, maxX), mirrorPt.Y);
            }
            else if (mirrorIndex == 0)
            {
                mirrorPt = new SKPoint(0, mirrorPt.Y);
            }
            else if (mirrorIndex == _curve.ControlPoints.Count - 1)
            {
                mirrorPt = new SKPoint(1, mirrorPt.Y);
            }

            _curve.ControlPoints[mirrorIndex] = mirrorPt;
        }
    }

    /// <summary>
    /// Find the index of the mirror point for symmetry.
    /// Returns -1 if no suitable mirror point exists.
    /// </summary>
    private int FindMirrorPointIndex(int sourceIndex, float targetX)
    {
        // Special cases for endpoints
        if (sourceIndex == 0) return _curve.ControlPoints.Count - 1;
        if (sourceIndex == _curve.ControlPoints.Count - 1) return 0;

        // For interior points, find the one closest to the mirror X position
        int bestIndex = -1;
        float bestDist = float.MaxValue;

        for (int i = 0; i < _curve.ControlPoints.Count; i++)
        {
            if (i == sourceIndex) continue;

            float dist = Math.Abs(_curve.ControlPoints[i].X - targetX);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private bool HandleCurvePresetClick(SKPoint pt)
    {
        // Check each stored preset button bound
        for (int i = 0; i < _curve.PresetBounds.Length; i++)
        {
            if (_curve.PresetBounds[i].Contains(pt))
            {
                _curve.SelectedType = i switch
                {
                    0 => CurveType.Linear,
                    1 => CurveType.SCurve,
                    2 => CurveType.Exponential,
                    _ => CurveType.Custom
                };

                // Reset control points when switching to custom
                if (_curve.SelectedType == CurveType.Custom && _curve.ControlPoints.Count == 2)
                {
                    // Add a middle point for custom curve
                    _curve.ControlPoints = new List<SKPoint>
                    {
                        new(0, 0),
                        new(0.5f, 0.5f),
                        new(1, 1)
                    };
                }

                SaveAxisSettingsForRow();  // Persist curve type change
                _ctx.InvalidateCanvas();
                return true;
            }
        }

        // Check invert checkbox
        if (_deadzone.InvertToggleBounds.Contains(pt))
        {
            _deadzone.AxisInverted = !_deadzone.AxisInverted;
            SaveAxisSettingsForRow();  // Persist invert change
            _ctx.InvalidateCanvas();
            return true;
        }

        // Check symmetrical checkbox (only for Custom curve)
        if (!_curve.CheckboxBounds.IsEmpty && _curve.CheckboxBounds.Contains(pt))
        {
            _curve.Symmetrical = !_curve.Symmetrical;
            if (_curve.Symmetrical)
            {
                // When enabling symmetry, mirror existing points around center
                MakeCurveSymmetrical();
            }
            SaveAxisSettingsForRow();  // Persist symmetry change
            _ctx.InvalidateCanvas();
            return true;
        }

        // Check centre checkbox and deadzone presets
        if (HandleDeadzonePresetClick(pt))
            return true;

        return false;
    }

    private bool HandleDeadzonePresetClick(SKPoint pt)
    {
        // Centre checkbox click
        if (_deadzone.CenterCheckboxBounds.Contains(pt))
        {
            _deadzone.CenterEnabled = !_deadzone.CenterEnabled;
            // When disabling center, reset center values and clear selection if center handle was selected
            if (!_deadzone.CenterEnabled)
            {
                _deadzone.CenterMin = 0.0f;
                _deadzone.CenterMax = 0.0f;
                if (_deadzone.SelectedHandle == 1 || _deadzone.SelectedHandle == 2)
                    _deadzone.SelectedHandle = -1;
            }
            SaveAxisSettingsForRow();  // Persist center deadzone change
            _ctx.InvalidateCanvas();
            return true;
        }

        // Preset buttons - apply to selected handle
        if (_deadzone.SelectedHandle >= 0)
        {
            // Preset values: 0%, 2%, 5%, 10%
            float[] presetValues = { 0.0f, 0.02f, 0.05f, 0.10f };

            for (int i = 0; i < _deadzone.PresetBounds.Length; i++)
            {
                if (!_deadzone.PresetBounds[i].IsEmpty && _deadzone.PresetBounds[i].Contains(pt))
                {
                    float presetVal = presetValues[i];

                    switch (_deadzone.SelectedHandle)
                    {
                        case 0: // Min (Start) - set distance from -1
                            _deadzone.Min = -1.0f + presetVal;
                            break;
                        case 1: // CenterMin - set negative offset from 0
                            _deadzone.CenterMin = -presetVal;
                            break;
                        case 2: // CenterMax - set positive offset from 0
                            _deadzone.CenterMax = presetVal;
                            break;
                        case 3: // Max (End) - set distance from 1
                            _deadzone.Max = 1.0f - presetVal;
                            break;
                    }
                    SaveAxisSettingsForRow();  // Persist deadzone preset change
                    _ctx.InvalidateCanvas();
                    return true;
                }
            }
        }
        return false;
    }

    private void UpdatePulseDurationFromMouse(float mouseX)
    {
        if (_buttonMode.PulseSliderBounds.Width <= 0) return;

        float normalized = (mouseX - _buttonMode.PulseSliderBounds.Left) / _buttonMode.PulseSliderBounds.Width;
        normalized = Math.Clamp(normalized, 0f, 1f);

        // Map 0-1 to 100-1000ms
        _buttonMode.PulseDurationMs = (int)(100f + normalized * 900f);
    }

    private void UpdateHoldDurationFromMouse(float mouseX)
    {
        if (_buttonMode.HoldSliderBounds.Width <= 0) return;

        float normalized = (mouseX - _buttonMode.HoldSliderBounds.Left) / _buttonMode.HoldSliderBounds.Width;
        normalized = Math.Clamp(normalized, 0f, 1f);

        // Map 0-1 to 200-2000ms
        _buttonMode.HoldDurationMs = (int)(200f + normalized * 1800f);
    }

}
