#if DEBUG
using System.Text.Json;
using System.Text.Json.Serialization;
using Asteriq.Models;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Svg.Skia;

namespace Asteriq.UI;

public partial class DeviceMapEditorForm
{
    private SKPoint ViewBoxToScreen(float viewBoxX, float viewBoxY)
    {
        float screenX, screenY;

        if (_deviceMap.Mirror)
        {
            // When mirrored, X is inverted: viewBox 0 -> right edge, viewBox max -> left edge
            screenX = _svgOffset.X + _svgScaledWidth - viewBoxX * _svgScale;
        }
        else
        {
            screenX = _svgOffset.X + viewBoxX * _svgScale;
        }

        screenY = _svgOffset.Y + viewBoxY * _svgScale;
        return new SKPoint(screenX, screenY);
    }

    private SKPoint ScreenToViewBox(float screenX, float screenY)
    {
        float viewBoxX, viewBoxY;

        if (_deviceMap.Mirror)
        {
            // When mirrored, invert X conversion
            viewBoxX = (_svgOffset.X + _svgScaledWidth - screenX) / _svgScale;
        }
        else
        {
            viewBoxX = (screenX - _svgOffset.X) / _svgScale;
        }

        viewBoxY = (screenY - _svgOffset.Y) / _svgScale;
        return new SKPoint(viewBoxX, viewBoxY);
    }



    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        float s = FUIRenderer.CanvasScaleFactor;
        float mx = e.X / s, my = e.Y / s;
        _mousePos = new SKPoint(mx, my);
        _mouseViewBox = ScreenToViewBox(mx, my);

        // Window dragging (use raw coords for PointToScreen)
        if (_isDragging)
        {
            var newLocation = PointToScreen(new Point(e.X - _dragStart.X, e.Y - _dragStart.Y));
            Location = newLocation;
            return;
        }

        // Dragging anchor, label, or segment handles
        if (_dragMode != DragMode.None && _selectedControlKey is not null)
        {
            if (_deviceMap.Controls.TryGetValue(_selectedControlKey, out var control))
            {
                if (_dragMode == DragMode.Anchor)
                {
                    control.Anchor ??= new Point2D();
                    control.Anchor.X = _mouseViewBox.X;
                    control.Anchor.Y = _mouseViewBox.Y;
                    _hasUnsavedChanges = true;
                }
                else if (_dragMode == DragMode.Label && control.Anchor is not null)
                {
                    control.LabelOffset ??= new Point2D();
                    control.LabelOffset.X = _mouseViewBox.X - control.Anchor.X;
                    control.LabelOffset.Y = _mouseViewBox.Y - control.Anchor.Y;
                    _hasUnsavedChanges = true;
                }
                else if (_dragMode == DragMode.ShelfEnd && control.Anchor is not null && control.LeadLine is not null)
                {
                    // Calculate new shelf length from mouse position relative to anchor
                    var anchorScreen = ViewBoxToScreen(control.Anchor.X, control.Anchor.Y);
                    float dx = _mousePos.X - anchorScreen.X;

                    // Shelf is horizontal, so we only care about X distance
                    // When mirrored, screen direction is inverted from viewbox direction
                    bool goesRight = control.LeadLine.ShelfSide == "right";
                    bool screenGoesRight = _deviceMap.Mirror ? !goesRight : goesRight;
                    float newLength = (screenGoesRight ? dx : -dx) / _svgScale;
                    control.LeadLine.ShelfLength = Math.Max(10, newLength);
                    _hasUnsavedChanges = true;
                }
                else if (_dragMode == DragMode.Segment && control.Anchor is not null &&
                         control.LeadLine?.Segments is not null && _dragSegmentIndex >= 0 &&
                         _dragSegmentIndex < control.LeadLine.Segments.Count)
                {
                    // Calculate the start point of this segment
                    var anchorScreen = ViewBoxToScreen(control.Anchor.X, control.Anchor.Y);
                    bool goesRight = control.LeadLine.ShelfSide == "right";
                    bool screenGoesRight = _deviceMap.Mirror ? !goesRight : goesRight;

                    // Start from shelf end
                    float shelfEndX = anchorScreen.X + (screenGoesRight ? 1 : -1) * control.LeadLine.ShelfLength * _svgScale;
                    var segmentStart = new SKPoint(shelfEndX, anchorScreen.Y);

                    // Walk through previous segments to find the start of the dragged segment
                    for (int i = 0; i < _dragSegmentIndex; i++)
                    {
                        var prevSeg = control.LeadLine.Segments[i];
                        float angleRad = prevSeg.Angle * MathF.PI / 180f;
                        float dx = MathF.Cos(angleRad) * prevSeg.Length * _svgScale;
                        float dy = -MathF.Sin(angleRad) * prevSeg.Length * _svgScale;
                        if (!screenGoesRight) dx = -dx;
                        segmentStart = new SKPoint(segmentStart.X + dx, segmentStart.Y + dy);
                    }

                    // Calculate new angle and length from segment start to mouse position
                    float deltaX = _mousePos.X - segmentStart.X;
                    float deltaY = _mousePos.Y - segmentStart.Y;

                    // Mirror X for left-side shelf (screen direction)
                    if (!screenGoesRight) deltaX = -deltaX;

                    // Calculate length (in screen units, then convert to viewbox)
                    float screenLength = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
                    float newLength = screenLength / _svgScale;

                    // Calculate angle (remember Y is inverted in screen coords)
                    // atan2 gives angle from positive X axis, counterclockwise
                    float newAngle = MathF.Atan2(-deltaY, deltaX) * 180f / MathF.PI;

                    var segment = control.LeadLine.Segments[_dragSegmentIndex];
                    segment.Length = Math.Max(10, newLength);
                    segment.Angle = MathF.Round(newAngle / 5) * 5; // Snap to 5-degree increments
                    _hasUnsavedChanges = true;
                }
            }
            return;
        }

        // Update hover states
        UpdateHoverStates(mx, my);
        _canvas.Invalidate();
    }

    private void UpdateHoverStates(float x, float y)
    {
        var pt = new SKPoint(x, y);

        // Toolbar buttons
        _saveButtonHovered = _saveButtonBounds.Contains(pt);
        _newButtonHovered = _newButtonBounds.Contains(pt);
        _loadButtonHovered = _loadButtonBounds.Contains(pt);
        _mirrorCheckboxHovered = _mirrorCheckboxBounds.Contains(pt);

        // Controls list
        _addControlHovered = _addControlButtonBounds.Contains(pt);
        _deleteControlHovered = _deleteControlButtonBounds.Contains(pt);

        _hoveredControlListItem = -1;
        for (int i = 0; i < _controlListItemBounds.Count; i++)
        {
            if (_controlListItemBounds[i].Contains(pt))
            {
                _hoveredControlListItem = i;
                break;
            }
        }

        // SVG dropdown items
        _hoveredSvgDropdownItem = -1;
        if (_svgDropdownOpen)
        {
            for (int i = 0; i < _svgDropdownItemBounds.Count; i++)
            {
                if (_svgDropdownItemBounds[i].Contains(pt))
                {
                    _hoveredSvgDropdownItem = i;
                    break;
                }
            }
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        _isDragging = false;
        _dragMode = DragMode.None;
        _dragSegmentIndex = -1;
    }

    private void OnMouseWheel(object? sender, MouseEventArgs e)
    {
        var pt = new SKPoint(e.X, e.Y);

        // Scroll controls list when mouse is over it
        if (_controlsListBounds.Contains(pt))
        {
            float scrollAmount = -e.Delta / 4f; // Adjust scroll speed
            _controlsListScroll += scrollAmount;
            _canvas.Invalidate();
        }
    }


}
#endif