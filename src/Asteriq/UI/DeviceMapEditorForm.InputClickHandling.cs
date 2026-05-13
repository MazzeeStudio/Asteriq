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
    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        float s = FUIRenderer.CanvasScaleFactor;
        float mx = e.X / s, my = e.Y / s;
        var pt = new SKPoint(mx, my);

        // Title bar dragging (use raw coords for drag start, but canvas-space for hit-test)
        if (my < TitleBarHeight && mx < Width / s - 50)
        {
            _isDragging = true;
            _dragStart = e.Location;
            return;
        }

        // Close button
        if (my < TitleBarHeight && mx >= Width / s - 50)
        {
            Close();
            return;
        }

        // Right-click on segment handles to remove them
        if (e.Button == MouseButtons.Right && _svgPanelBounds.Contains(pt) &&
            _selectedControlKey is not null &&
            _deviceMap.Controls.TryGetValue(_selectedControlKey, out var control) &&
            control.LeadLine?.Segments is not null)
        {
            for (int i = 0; i < _segmentEndHandles.Count; i++)
            {
                float segDist = SKPoint.Distance(_segmentEndHandles[i], _mousePos);
                if (segDist < HandleHitRadius)
                {
                    // Remove this segment
                    control.LeadLine.Segments.RemoveAt(i);
                    if (control.LeadLine.Segments.Count == 0)
                        control.LeadLine.Segments = null;
                    _hasUnsavedChanges = true;
                    return;
                }
            }
        }

        // SVG dropdown
        if (_svgDropdownBounds.Contains(pt))
        {
            _svgDropdownOpen = !_svgDropdownOpen;
            return;
        }

        // SVG dropdown item selection
        if (_svgDropdownOpen)
        {
            for (int i = 0; i < _svgDropdownItemBounds.Count; i++)
            {
                if (_svgDropdownItemBounds[i].Contains(pt))
                {
                    _deviceMap.SvgFile = _availableSvgFiles[i];
                    LoadSvgFile(_availableSvgFiles[i]);
                    _hasUnsavedChanges = true;
                    _svgDropdownOpen = false;
                    return;
                }
            }
            _svgDropdownOpen = false;
            return;
        }

        // Toolbar buttons
        if (_saveButtonBounds.Contains(pt))
        {
            SaveJsonFile();
            return;
        }

        if (_newButtonBounds.Contains(pt))
        {
            _deviceMap = new DeviceMap { SvgFile = _deviceMap.SvgFile };
            _selectedControlKey = null;
            _hasUnsavedChanges = false;
            _jsonFileName = "new_device.json";
            return;
        }

        if (_loadButtonBounds.Contains(pt))
        {
            // Try to open from source directory, not bin output
            var sourceDir = FindSourceMapsDirectory();
            using var ofd = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                InitialDirectory = sourceDir ?? Path.Combine(_imagesDir, "Maps")
            };
            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                LoadJsonFile(ofd.FileName);
            }
            return;
        }

        if (_mirrorCheckboxBounds.Contains(pt))
        {
            _deviceMap.Mirror = !_deviceMap.Mirror;
            _hasUnsavedChanges = true;
            return;
        }

        // Controls list
        if (_addControlButtonBounds.Contains(pt))
        {
            AddNewControl();
            return;
        }

        if (_deleteControlButtonBounds.Contains(pt) && _selectedControlKey is not null)
        {
            _deviceMap.Controls.Remove(_selectedControlKey);
            _selectedControlKey = null;
            _hasUnsavedChanges = true;
            return;
        }

        for (int i = 0; i < _controlListItemBounds.Count; i++)
        {
            if (_controlListItemBounds[i].Contains(pt))
            {
                _selectedControlKey = _deviceMap.Controls.Keys.ElementAt(i);
                return;
            }
        }

        // Lead line editing buttons (only when a control is selected)
        if (_selectedControlKey is not null && _deviceMap.Controls.TryGetValue(_selectedControlKey, out var selectedControl))
        {
            if (HandleLeadLineButtonClick(pt, selectedControl))
                return;
        }

        // Click on SVG panel - set anchor or start drag
        if (_svgPanelBounds.Contains(pt))
        {
            HandleSvgPanelClick(e);
        }
    }

    private void HandleSvgPanelClick(MouseEventArgs e)
    {
        var viewBoxPos = _mouseViewBox;
        bool shiftHeld = (Control.ModifierKeys & Keys.Shift) != 0;

        // Shift+Click: Reposition anchor of selected control to click position
        if (shiftHeld && _selectedControlKey is not null)
        {
            if (_deviceMap.Controls.TryGetValue(_selectedControlKey, out var control))
            {
                control.Anchor ??= new Point2D();
                control.Anchor.X = viewBoxPos.X;
                control.Anchor.Y = viewBoxPos.Y;
                _hasUnsavedChanges = true;
            }
            return;
        }

        // First, check if clicking on lead line segment handles (only for selected control)
        if (_selectedControlKey is not null &&
            _deviceMap.Controls.TryGetValue(_selectedControlKey, out var selectedControl) &&
            selectedControl.LeadLine is not null)
        {
            // Check shelf end handle
            if (_shelfEndHandle != default)
            {
                float shelfDist = SKPoint.Distance(_shelfEndHandle, _mousePos);
                if (shelfDist < HandleHitRadius)
                {
                    _dragMode = DragMode.ShelfEnd;
                    _dragSegmentIndex = -1;
                    return;
                }
            }

            // Check segment end handles
            for (int i = 0; i < _segmentEndHandles.Count; i++)
            {
                float segDist = SKPoint.Distance(_segmentEndHandles[i], _mousePos);
                if (segDist < HandleHitRadius)
                {
                    _dragMode = DragMode.Segment;
                    _dragSegmentIndex = i;
                    return;
                }
            }
        }

        // Check if clicking on existing control anchor or label
        foreach (var kvp in _deviceMap.Controls)
        {
            var control = kvp.Value;
            if (control.Anchor is null) continue;

            var anchorScreen = ViewBoxToScreen(control.Anchor.X, control.Anchor.Y);
            float anchorDist = SKPoint.Distance(anchorScreen, _mousePos);

            if (anchorDist < 15)
            {
                _selectedControlKey = kvp.Key;
                _dragMode = DragMode.Anchor;
                return;
            }

            // Check label area
            float labelX = control.Anchor.X + (control.LabelOffset?.X ?? 50);
            float labelY = control.Anchor.Y + (control.LabelOffset?.Y ?? 0);
            var labelScreen = ViewBoxToScreen(labelX, labelY);
            float labelDist = SKPoint.Distance(labelScreen, _mousePos);

            if (labelDist < 30)
            {
                _selectedControlKey = kvp.Key;
                _dragMode = DragMode.Label;
                return;
            }
        }

        // If we have a selected control without anchor, set anchor
        if (_selectedControlKey is not null)
        {
            if (_deviceMap.Controls.TryGetValue(_selectedControlKey, out var control))
            {
                if (control.Anchor is null)
                {
                    control.Anchor = new Point2D();
                    control.Anchor.X = viewBoxPos.X;
                    control.Anchor.Y = viewBoxPos.Y;
                    _hasUnsavedChanges = true;
                }
            }
        }
        else
        {
            // Create new control at click position
            AddNewControl(viewBoxPos);
        }
    }

    private bool HandleLeadLineButtonClick(SKPoint pt, ControlDefinition control)
    {
        // Shelf side toggle
        if (_shelfSideButtonBounds.Contains(pt))
        {
            control.LeadLine ??= new LeadLineDefinition();
            control.LeadLine.ShelfSide = control.LeadLine.ShelfSide == "left" ? "right" : "left";
            _hasUnsavedChanges = true;
            return true;
        }

        // Shelf length -/+
        if (_shelfLengthMinusBounds.Contains(pt))
        {
            control.LeadLine ??= new LeadLineDefinition();
            control.LeadLine.ShelfLength = Math.Max(10, control.LeadLine.ShelfLength - 10);
            _hasUnsavedChanges = true;
            return true;
        }

        if (_shelfLengthPlusBounds.Contains(pt))
        {
            control.LeadLine ??= new LeadLineDefinition();
            control.LeadLine.ShelfLength += 10;
            _hasUnsavedChanges = true;
            return true;
        }

        // Add segment
        if (_addSegmentButtonBounds.Contains(pt))
        {
            control.LeadLine ??= new LeadLineDefinition();
            control.LeadLine.Segments ??= new List<LeadLineSegment>();
            control.LeadLine.Segments.Add(new LeadLineSegment { Angle = -45, Length = 80 });
            _hasUnsavedChanges = true;
            return true;
        }

        // Remove segment
        if (_removeSegmentButtonBounds.Contains(pt) && control.LeadLine?.Segments?.Count > 0)
        {
            control.LeadLine.Segments.RemoveAt(control.LeadLine.Segments.Count - 1);
            if (control.LeadLine.Segments.Count == 0)
                control.LeadLine.Segments = null;
            _hasUnsavedChanges = true;
            return true;
        }

        // Segment angle/length buttons
        for (int i = 0; i < _segmentButtonBounds.Count; i++)
        {
            var (angMinus, angPlus, lenMinus, lenPlus) = _segmentButtonBounds[i];
            var seg = control.LeadLine?.Segments?[i];
            if (seg is null) continue;

            if (angMinus.Contains(pt))
            {
                seg.Angle -= 15;
                _hasUnsavedChanges = true;
                return true;
            }
            if (angPlus.Contains(pt))
            {
                seg.Angle += 15;
                _hasUnsavedChanges = true;
                return true;
            }
            if (lenMinus.Contains(pt))
            {
                seg.Length = Math.Max(10, seg.Length - 10);
                _hasUnsavedChanges = true;
                return true;
            }
            if (lenPlus.Contains(pt))
            {
                seg.Length += 10;
                _hasUnsavedChanges = true;
                return true;
            }
        }

        return false;
    }

    private void AddNewControl(SKPoint? position = null)
    {
        int index = _deviceMap.Controls.Count + 1;
        string key = $"control_{index}";
        while (_deviceMap.Controls.ContainsKey(key))
        {
            index++;
            key = $"control_{index}";
        }

        var control = new ControlDefinition
        {
            Id = key,
            Type = "Button",
            Label = $"Control {index}",
            Bindings = new List<string> { $"button{index}" }
        };

        if (position.HasValue)
        {
            control.Anchor = new Point2D { X = position.Value.X, Y = position.Value.Y };
            control.LabelOffset = new Point2D { X = 50, Y = 0 };
        }

        _deviceMap.Controls[key] = control;
        _selectedControlKey = key;
        _hasUnsavedChanges = true;
    }

}
#endif