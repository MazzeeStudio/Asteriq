using Asteriq.Models;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
    private int FindCurvePointAt(SKPoint screenPt, SKRect bounds)
    {
        const float HitRadius = 12f;

        for (int i = 0; i < _curveControlPoints.Count; i++)
        {
            var pt = _curveControlPoints[i];

            // Skip center point - it's not selectable
            bool isCenterPoint = Math.Abs(pt.X - 0.5f) < 0.01f && Math.Abs(pt.Y - 0.5f) < 0.01f;
            if (isCenterPoint)
                continue;

            float x = bounds.Left + pt.X * bounds.Width;

            // Apply inversion to display Y position to match the visual
            float displayY = _axisInverted ? (1f - pt.Y) : pt.Y;
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
        var bounds = _deadzoneSliderBounds;
        if (bounds.Width <= 0) return -1;

        // Convert deadzone values to 0..1 range
        float minPos = (_deadzoneMin + 1f) / 2f;
        float centerMinPos = (_deadzoneCenterMin + 1f) / 2f;
        float centerMaxPos = (_deadzoneCenterMax + 1f) / 2f;
        float maxPos = (_deadzoneMax + 1f) / 2f;

        if (_deadzoneCenterEnabled)
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
        if (_draggingDeadzoneHandle < 0) return;
        var bounds = _deadzoneSliderBounds;
        if (bounds.Width <= 0) return;

        float value;

        if (_deadzoneCenterEnabled)
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

            switch (_draggingDeadzoneHandle)
            {
                case 0: // Min handle on left track
                    float normLeft0 = Math.Clamp((screenPt.X - leftTrackLeft) / leftTrackWidth, 0f, 1f);
                    value = normLeft0 - 1f; // Maps 0..1 to -1..0
                    value = Math.Clamp(value, -1f, _deadzoneCenterMin - 0.02f);
                    _deadzoneMin = value;
                    break;
                case 1: // CenterMin handle on left track (right edge)
                    float normLeft1 = Math.Clamp((screenPt.X - leftTrackLeft) / leftTrackWidth, 0f, 1f);
                    value = normLeft1 - 1f; // Maps 0..1 to -1..0
                    value = Math.Clamp(value, _deadzoneMin + 0.02f, 0f);
                    _deadzoneCenterMin = value;
                    break;
                case 2: // CenterMax handle on right track (left edge)
                    float normRight2 = Math.Clamp((screenPt.X - rightTrackLeft) / rightTrackWidth, 0f, 1f);
                    value = normRight2; // Maps 0..1 to 0..1
                    value = Math.Clamp(value, 0f, _deadzoneMax - 0.02f);
                    _deadzoneCenterMax = value;
                    break;
                case 3: // Max handle on right track
                    float normRight3 = Math.Clamp((screenPt.X - rightTrackLeft) / rightTrackWidth, 0f, 1f);
                    value = normRight3; // Maps 0..1 to 0..1
                    value = Math.Clamp(value, _deadzoneCenterMax + 0.02f, 1f);
                    _deadzoneMax = value;
                    break;
            }
        }
        else
        {
            // Single track layout - convert screen X to -1..1 range
            float normalized = (screenPt.X - bounds.Left) / bounds.Width;
            value = normalized * 2f - 1f;

            switch (_draggingDeadzoneHandle)
            {
                case 0: // Min handle
                    value = Math.Clamp(value, -1f, _deadzoneMax - 0.1f);
                    _deadzoneMin = value;
                    break;
                case 3: // Max handle
                    value = Math.Clamp(value, _deadzoneMin + 0.1f, 1f);
                    _deadzoneMax = value;
                    break;
            }
        }
    }

    private void UpdateDraggedCurvePoint(SKPoint screenPt)
    {
        if (_draggingCurvePoint < 0 || _draggingCurvePoint >= _curveControlPoints.Count)
            return;

        var graphPt = CurveScreenToGraph(screenPt, _curveEditorBounds);

        // Constrain endpoints to X edges
        if (_draggingCurvePoint == 0)
            graphPt.X = 0;
        else if (_draggingCurvePoint == _curveControlPoints.Count - 1)
            graphPt.X = 1;
        else
        {
            // Interior points: constrain X between neighbors
            float minX = _curveControlPoints[_draggingCurvePoint - 1].X + 0.02f;
            float maxX = _curveControlPoints[_draggingCurvePoint + 1].X - 0.02f;
            // Ensure minX <= maxX (neighbors might be very close)
            if (minX > maxX)
            {
                float midX = (_curveControlPoints[_draggingCurvePoint - 1].X + _curveControlPoints[_draggingCurvePoint + 1].X) / 2f;
                graphPt.X = midX;
            }
            else
            {
                graphPt.X = Math.Clamp(graphPt.X, minX, maxX);
            }
        }

        _curveControlPoints[_draggingCurvePoint] = graphPt;

        // If symmetrical mode is enabled, mirror the change
        if (_curveSymmetrical)
        {
            UpdateSymmetricalPoint(_draggingCurvePoint, graphPt);
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
            if (mirrorIndex > 0 && mirrorIndex < _curveControlPoints.Count - 1)
            {
                // Interior point - constrain X between neighbors
                float minX = _curveControlPoints[mirrorIndex - 1].X + 0.02f;
                float maxX = _curveControlPoints[mirrorIndex + 1].X - 0.02f;
                mirrorPt = new SKPoint(Math.Clamp(mirrorPt.X, minX, maxX), mirrorPt.Y);
            }
            else if (mirrorIndex == 0)
            {
                mirrorPt = new SKPoint(0, mirrorPt.Y);
            }
            else if (mirrorIndex == _curveControlPoints.Count - 1)
            {
                mirrorPt = new SKPoint(1, mirrorPt.Y);
            }

            _curveControlPoints[mirrorIndex] = mirrorPt;
        }
    }

    /// <summary>
    /// Find the index of the mirror point for symmetry.
    /// Returns -1 if no suitable mirror point exists.
    /// </summary>
    private int FindMirrorPointIndex(int sourceIndex, float targetX)
    {
        // Special cases for endpoints
        if (sourceIndex == 0) return _curveControlPoints.Count - 1;
        if (sourceIndex == _curveControlPoints.Count - 1) return 0;

        // For interior points, find the one closest to the mirror X position
        int bestIndex = -1;
        float bestDist = float.MaxValue;

        for (int i = 0; i < _curveControlPoints.Count; i++)
        {
            if (i == sourceIndex) continue;

            float dist = Math.Abs(_curveControlPoints[i].X - targetX);
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
        for (int i = 0; i < _curvePresetBounds.Length; i++)
        {
            if (_curvePresetBounds[i].Contains(pt))
            {
                _selectedCurveType = i switch
                {
                    0 => CurveType.Linear,
                    1 => CurveType.SCurve,
                    2 => CurveType.Exponential,
                    _ => CurveType.Custom
                };

                // Reset control points when switching to custom
                if (_selectedCurveType == CurveType.Custom && _curveControlPoints.Count == 2)
                {
                    // Add a middle point for custom curve
                    _curveControlPoints = new List<SKPoint>
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
        if (_invertToggleBounds.Contains(pt))
        {
            _axisInverted = !_axisInverted;
            SaveAxisSettingsForRow();  // Persist invert change
            _ctx.InvalidateCanvas();
            return true;
        }

        // Check symmetrical checkbox (only for Custom curve)
        if (!_curveSymmetricalCheckboxBounds.IsEmpty && _curveSymmetricalCheckboxBounds.Contains(pt))
        {
            _curveSymmetrical = !_curveSymmetrical;
            if (_curveSymmetrical)
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
        if (_deadzoneCenterCheckboxBounds.Contains(pt))
        {
            _deadzoneCenterEnabled = !_deadzoneCenterEnabled;
            // When disabling center, reset center values and clear selection if center handle was selected
            if (!_deadzoneCenterEnabled)
            {
                _deadzoneCenterMin = 0.0f;
                _deadzoneCenterMax = 0.0f;
                if (_selectedDeadzoneHandle == 1 || _selectedDeadzoneHandle == 2)
                    _selectedDeadzoneHandle = -1;
            }
            SaveAxisSettingsForRow();  // Persist center deadzone change
            _ctx.InvalidateCanvas();
            return true;
        }

        // Preset buttons - apply to selected handle
        if (_selectedDeadzoneHandle >= 0)
        {
            // Preset values: 0%, 2%, 5%, 10%
            float[] presetValues = { 0.0f, 0.02f, 0.05f, 0.10f };

            for (int i = 0; i < _deadzonePresetBounds.Length; i++)
            {
                if (!_deadzonePresetBounds[i].IsEmpty && _deadzonePresetBounds[i].Contains(pt))
                {
                    float presetVal = presetValues[i];

                    switch (_selectedDeadzoneHandle)
                    {
                        case 0: // Min (Start) - set distance from -1
                            _deadzoneMin = -1.0f + presetVal;
                            break;
                        case 1: // CenterMin - set negative offset from 0
                            _deadzoneCenterMin = -presetVal;
                            break;
                        case 2: // CenterMax - set positive offset from 0
                            _deadzoneCenterMax = presetVal;
                            break;
                        case 3: // Max (End) - set distance from 1
                            _deadzoneMax = 1.0f - presetVal;
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
        if (_pulseDurationSliderBounds.Width <= 0) return;

        float normalized = (mouseX - _pulseDurationSliderBounds.Left) / _pulseDurationSliderBounds.Width;
        normalized = Math.Clamp(normalized, 0f, 1f);

        // Map 0-1 to 100-1000ms
        _pulseDurationMs = (int)(100f + normalized * 900f);
    }

    private void UpdateHoldDurationFromMouse(float mouseX)
    {
        if (_holdDurationSliderBounds.Width <= 0) return;

        float normalized = (mouseX - _holdDurationSliderBounds.Left) / _holdDurationSliderBounds.Width;
        normalized = Math.Clamp(normalized, 0f, 1f);

        // Map 0-1 to 200-2000ms
        _holdDurationMs = (int)(200f + normalized * 1800f);
    }

}
