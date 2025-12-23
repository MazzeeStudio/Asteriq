using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI;

public partial class MainForm
{
    #region Devices Tab
    private void DrawDeviceListPanel(SKCanvas canvas, SKRect bounds)
    {
        float pad = FUIRenderer.PanelPadding;
        float itemGap = FUIRenderer.ItemSpacing;
        float frameInset = 5f;

        // Vertical side tabs width
        float sideTabWidth = 28f;

        // Panel shadow
        FUIRenderer.DrawPanelShadow(canvas, bounds, 3f, 3f, 10f);

        // Panel background (shifted right to make room for side tabs)
        var contentBounds = new SKRect(bounds.Left + frameInset + sideTabWidth, bounds.Top + frameInset,
                                        bounds.Right - frameInset, bounds.Bottom - frameInset);
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(140),
            IsAntialias = true
        };
        canvas.DrawRect(contentBounds, bgPaint);

        // Draw vertical side tabs (D1 Physical, D2 Virtual)
        DrawDeviceCategorySideTabs(canvas, bounds.Left + frameInset, bounds.Top + frameInset,
            sideTabWidth, bounds.Height - frameInset * 2);

        // L-corner frame (adjusted for side tabs)
        bool panelHovered = _hoveredDevice >= 0;
        var frameBounds = new SKRect(bounds.Left + sideTabWidth, bounds.Top, bounds.Right, bounds.Bottom);
        FUIRenderer.DrawLCornerFrame(canvas, frameBounds,
            panelHovered ? FUIColors.FrameBright : FUIColors.Frame, 40f, 10f, 1.5f, panelHovered);

        // Header - show D1 or D2 based on selected category
        float titleBarHeight = 32f;
        var titleBounds = new SKRect(contentBounds.Left, contentBounds.Top, contentBounds.Right, contentBounds.Top + titleBarHeight);
        string categoryCode = _deviceCategory == 0 ? "D1" : "D2";
        //string categoryName = _deviceCategory == 0 ? "PHYSICAL DEVICES" : "VIRTUAL DEVICES";
        string categoryName = _deviceCategory == 0 ? "DEVICES" : "DEVICES";
        FUIRenderer.DrawPanelTitle(canvas, titleBounds, categoryCode, categoryName);

        // Filter devices by category
        var filteredDevices = _deviceCategory == 0
            ? _devices.Where(d => !d.IsVirtual).ToList()
            : _devices.Where(d => d.IsVirtual).ToList();

        // Device list
        float itemY = contentBounds.Top + titleBarHeight + pad;
        float itemHeight = 60f;

        if (filteredDevices.Count == 0)
        {
            string emptyMsg = _deviceCategory == 0
                ? "No physical devices detected"
                : "No virtual devices detected";
            string helpMsg = _deviceCategory == 0
                ? "Connect a joystick or gamepad"
                : "Install vJoy or start a virtual device";
            FUIRenderer.DrawText(canvas, emptyMsg,
                new SKPoint(contentBounds.Left + pad, itemY + 20), FUIColors.TextDim, 12f);
            FUIRenderer.DrawText(canvas, helpMsg,
                new SKPoint(contentBounds.Left + pad, itemY + 38), FUIColors.TextDisabled, 10f);
        }
        else
        {
            // Clear and rebuild device item bounds for hit testing
            _deviceItemBounds.Clear();

            for (int i = 0; i < filteredDevices.Count && itemY + itemHeight < contentBounds.Bottom - 40; i++)
            {
                // Find the actual device index in _devices
                int actualIndex = _devices.IndexOf(filteredDevices[i]);

                // Track bounds for drag-drop hit testing
                var itemBounds = new SKRect(contentBounds.Left + pad - 10, itemY,
                    contentBounds.Left + pad - 10 + contentBounds.Width - pad, itemY + itemHeight);
                _deviceItemBounds.Add(itemBounds);

                // Skip drawing the item being dragged (we'll draw it at cursor position)
                if (_isDraggingDevice && actualIndex == _dragDeviceIndex)
                {
                    // Draw drop indicator line where this item would go
                    itemY += itemHeight + itemGap;
                    continue;
                }

                // Draw drop indicator if we're dragging and this is the drop target
                if (_isDraggingDevice && i == _dragDropTargetIndex)
                {
                    using var dropPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Stroke,
                        Color = FUIColors.Active,
                        StrokeWidth = 2f,
                        IsAntialias = true
                    };
                    canvas.DrawLine(itemBounds.Left, itemY - 2, itemBounds.Right, itemY - 2, dropPaint);
                }

                string status = filteredDevices[i].IsConnected ? "ONLINE" : "DISCONNECTED";
                // For physical devices show vJoy assignment, for virtual devices show primary device
                string assignment = filteredDevices[i].IsVirtual
                    ? GetPrimaryDeviceForVJoyDevice(filteredDevices[i])
                    : GetVJoyAssignmentForDevice(filteredDevices[i]);
                DrawDeviceListItem(canvas, contentBounds.Left + pad - 10, itemY, contentBounds.Width - pad,
                    filteredDevices[i].Name, status, actualIndex == _selectedDevice, actualIndex == _hoveredDevice, assignment);
                itemY += itemHeight + itemGap;
            }

            // Draw the dragged item at cursor position (ghost effect)
            if (_isDraggingDevice && _dragDeviceIndex >= 0 && _dragDeviceIndex < _devices.Count)
            {
                var draggedDevice = _devices[_dragDeviceIndex];
                string status = draggedDevice.IsConnected ? "ONLINE" : "DISCONNECTED";
                string assignment = draggedDevice.IsVirtual
                    ? GetPrimaryDeviceForVJoyDevice(draggedDevice)
                    : GetVJoyAssignmentForDevice(draggedDevice);

                // Draw with transparency at cursor position
                canvas.Save();
                canvas.Translate(_dragCurrentPoint.X - _dragStartPoint.X, _dragCurrentPoint.Y - _dragStartPoint.Y);
                // Use a semi-transparent overlay effect
                using var ghostPaint = new SKPaint { Color = SKColors.White.WithAlpha(180) };
                DrawDeviceListItem(canvas, contentBounds.Left + pad - 10,
                    _deviceItemBounds.Count > 0 ? _deviceItemBounds[0].Top + (_dragDeviceIndex * (itemHeight + itemGap)) : contentBounds.Top + 50,
                    contentBounds.Width - pad, draggedDevice.Name, status, true, false, assignment);
                canvas.Restore();
            }
        }

        // "Scan for devices" prompt
        float promptY = bounds.Bottom - pad - 20;
        FUIRenderer.DrawText(canvas, "+ SCAN FOR DEVICES",
            new SKPoint(contentBounds.Left + pad, promptY), FUIColors.TextDim, 12f);

        using var bracketPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.FrameDim,
            StrokeWidth = 1f
        };
        canvas.DrawLine(contentBounds.Left + pad - 20, promptY - 10, contentBounds.Left + pad - 20, promptY + 5, bracketPaint);
        canvas.DrawLine(contentBounds.Left + pad - 20, promptY - 10, contentBounds.Left + pad - 8, promptY - 10, bracketPaint);
    }

    private void DrawDeviceCategorySideTabs(SKCanvas canvas, float x, float y, float width, float height)
    {
        // Style based on reference: narrow vertical tabs with text reading bottom-to-top
        // Accent bar on right side for selected tab, 4px space between tabs
        float tabHeight = 80f;
        float tabGap = 4f; // 4px space between tabs

        // Calculate total tabs height and start from bottom of available space
        float totalTabsHeight = tabHeight * 2 + tabGap;
        float startY = y + height - totalTabsHeight - 10f; // Align tabs near bottom

        // D1 Physical Devices tab (bottom)
        var d1Bounds = new SKRect(x, startY + tabHeight + tabGap, x + width, startY + tabHeight * 2 + tabGap);
        _deviceCategoryD1Bounds = d1Bounds;
        DrawVerticalSideTab(canvas, d1Bounds, "DEVICES_01", _deviceCategory == 0, _hoveredDeviceCategory == 0);

        // D2 Virtual Devices tab (above D1)
        var d2Bounds = new SKRect(x, startY, x + width, startY + tabHeight);
        _deviceCategoryD2Bounds = d2Bounds;
        DrawVerticalSideTab(canvas, d2Bounds, "DEVICES_02", _deviceCategory == 1, _hoveredDeviceCategory == 1);
    }


    private void DrawDeviceListItem(SKCanvas canvas, float x, float y, float width,
        string name, string status, bool isSelected, bool isHovered, string vjoyAssignment = "")
    {
        var itemBounds = new SKRect(x, y, x + width, y + 60);
        bool isDisconnected = status == "DISCONNECTED";

        // Selection/hover background
        if (isSelected || isHovered)
        {
            var bgColor = isSelected
                ? (isDisconnected ? FUIColors.Danger.WithAlpha(20) : FUIColors.Active.WithAlpha(30))
                : FUIColors.Primary.WithAlpha(15);
            FUIRenderer.FillFrame(canvas, itemBounds, bgColor, 6f);
        }

        // Item frame
        var frameColor = isSelected
            ? (isDisconnected ? FUIColors.Danger : FUIColors.Active)
            : (isHovered ? FUIColors.FrameBright : FUIColors.FrameDim);
        FUIRenderer.DrawFrame(canvas, itemBounds, frameColor, 6f, isSelected ? 1.5f : 1f, isSelected);

        // Status indicator dot
        var statusColor = isDisconnected ? FUIColors.Danger : FUIColors.Active;
        FUIRenderer.DrawGlowingDot(canvas, new SKPoint(x + 18, y + 22), statusColor, 4f,
            isDisconnected ? 4f : 8f);

        // Device name (truncate if needed) - dim for disconnected
        string displayName = name.Length > 28 ? name.Substring(0, 25) + "..." : name;
        var nameColor = isDisconnected
            ? FUIColors.TextDim
            : (isSelected ? FUIColors.TextBright : FUIColors.TextPrimary);
        FUIRenderer.DrawText(canvas, displayName, new SKPoint(x + 36, y + 24), nameColor, 13f, isSelected && !isDisconnected);

        // Status text
        var statusTextColor = isDisconnected ? FUIColors.Danger : FUIColors.Active;
        FUIRenderer.DrawText(canvas, status, new SKPoint(x + 36, y + 44), statusTextColor, 11f);

        // vJoy assignment indicator (positioned to avoid chevron overlap)
        if (!isDisconnected && !string.IsNullOrEmpty(vjoyAssignment))
        {
            FUIRenderer.DrawText(canvas, vjoyAssignment, new SKPoint(x + width - 85, y + 45),
                FUIColors.TextDim, 11f);
        }

        // Selection chevron
        if (isSelected)
        {
            using var chevronPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = isDisconnected ? FUIColors.Danger : FUIColors.Active,
                StrokeWidth = 2f,
                IsAntialias = true
            };
            canvas.DrawLine(x + width - 18, y + 24, x + width - 10, y + 28, chevronPaint);
            canvas.DrawLine(x + width - 10, y + 28, x + width - 18, y + 32, chevronPaint);
        }
    }

    private void DrawDeviceDetailsPanel(SKCanvas canvas, SKRect bounds)
    {
        float pad = FUIRenderer.PanelPadding;
        float frameInset = 5f;

        // Panel background (matching mappings view)
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(100),
            IsAntialias = true
        };
        canvas.DrawRect(new SKRect(bounds.Left + frameInset, bounds.Top + frameInset,
            bounds.Right - frameInset, bounds.Bottom - frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame.WithAlpha(150), 30f, 8f);

        if (_devices.Count == 0 || _selectedDevice < 0 || _selectedDevice >= _devices.Count)
        {
            FUIRenderer.DrawText(canvas, "Select a device to view details",
                new SKPoint(bounds.Left + pad, bounds.Top + 50), FUIColors.TextDim, 14f);
            return;
        }

        var device = _devices[_selectedDevice];

        // Draw the device silhouette centered in panel (matching mappings view style)
        float centerX = bounds.MidX;
        float centerY = bounds.MidY;

        if (_joystickSvg?.Picture is not null)
        {
            // Limit size to 900px max and apply same rendering as mappings tab
            float maxSize = 900f;
            float maxWidth = Math.Min(bounds.Width - 40, maxSize);
            float maxHeight = Math.Min(bounds.Height - 40, maxSize);

            // Create constrained bounds centered in the panel
            float constrainedWidth = Math.Min(maxWidth, maxHeight); // Keep square-ish
            float constrainedHeight = constrainedWidth;
            _silhouetteBounds = new SKRect(
                centerX - constrainedWidth / 2,
                centerY - constrainedHeight / 2,
                centerX + constrainedWidth / 2,
                centerY + constrainedHeight / 2
            );

            // Draw the silhouette using shared method
            DrawDeviceSilhouette(canvas, _silhouetteBounds);
        }
        else
        {
            // Placeholder when no SVG
            _silhouetteBounds = SKRect.Empty;
            FUIRenderer.DrawTextCentered(canvas, "Device Preview",
                new SKRect(bounds.Left, centerY - 20, bounds.Right, centerY + 20),
                FUIColors.TextDim, 14f);
        }

        // Draw dynamic lead-lines for active inputs
        DrawActiveInputLeadLines(canvas, bounds);
    }

    private void DrawActiveInputLeadLines(SKCanvas canvas, SKRect panelBounds)
    {
        if (_deviceMap is null || _joystickSvg?.Picture is null) return;

        var visibleInputs = _activeInputTracker.GetVisibleInputs();
        int inputIndex = 0;

        foreach (var input in visibleInputs)
        {
            var control = input.Control;
            if (control?.Anchor is null) continue; // Must have JSON anchor

            float opacity = input.GetOpacity(_activeInputTracker.FadeDelay, _activeInputTracker.FadeDuration);
            if (opacity < 0.01f) continue;

            // Use the JSON anchor point (in viewBox coordinates 0-2048)
            // Convert to screen coordinates using the stored SVG transform
            SKPoint anchorScreen = ViewBoxToScreen(control.Anchor.X, control.Anchor.Y);

            // Label position: use JSON labelOffset if specified, otherwise auto-stack
            float labelX, labelY;
            bool goesRight = true;

            if (control.LabelOffset is not null)
            {
                // labelOffset is relative to anchor, in viewBox units
                float labelVbX = control.Anchor.X + control.LabelOffset.X;
                float labelVbY = control.Anchor.Y + control.LabelOffset.Y;
                var labelScreen = ViewBoxToScreen(labelVbX, labelVbY);
                labelX = labelScreen.X;
                labelY = labelScreen.Y;
                // When mirrored, positive X offset appears on left side of screen
                bool offsetGoesRight = control.LabelOffset.X >= 0;
                goesRight = _svgMirrored ? !offsetGoesRight : offsetGoesRight;
            }
            else
            {
                // Fallback: auto-stack labels to the right of silhouette
                labelY = panelBounds.Top + 80 + (inputIndex * 55);
                if (labelY > panelBounds.Bottom - 60)
                {
                    labelY = panelBounds.Top + 80 + ((inputIndex % 8) * 55);
                }
                labelX = _silhouetteBounds.Right + 20;
            }

            // Draw the lead-line with fade - from anchor on joystick to label
            DrawInputLeadLine(canvas, anchorScreen, new SKPoint(labelX, labelY), goesRight, opacity, input);

            inputIndex++;
        }
    }

    /// <summary>
    /// Convert viewBox coordinates (0-2048) to screen coordinates.
    /// The SVG viewBox is 0 0 2048 2048, and we render it scaled/translated.
    /// Handles mirroring: when SVG is mirrored, viewBox X is inverted.
    /// </summary>
    private SKPoint ViewBoxToScreen(float viewBoxX, float viewBoxY)
    {
        if (_joystickSvg?.Picture is null)
            return new SKPoint(viewBoxX, viewBoxY);

        float screenX, screenY;

        if (_svgMirrored)
        {
            // When mirrored, the SVG is drawn with Scale(-scale, scale) after
            // Translate(scaledWidth, 0). This means viewBox X is inverted:
            // viewBox 0 -> screen offsetX + scaledWidth
            // viewBox 2048 -> screen offsetX
            var svgBounds = _joystickSvg.Picture.CullRect;
            float scaledWidth = svgBounds.Width * _svgScale;
            screenX = _svgOffset.X + scaledWidth - viewBoxX * _svgScale;
        }
        else
        {
            screenX = _svgOffset.X + viewBoxX * _svgScale;
        }

        screenY = _svgOffset.Y + viewBoxY * _svgScale;
        return new SKPoint(screenX, screenY);
    }

    private void DrawInputLeadLine(SKCanvas canvas, SKPoint anchor, SKPoint labelPos, bool goesRight,
        float opacity, ActiveInputState input)
    {
        byte alpha = (byte)(255 * opacity * input.AppearProgress);
        var lineColor = FUIColors.Active.WithAlpha(alpha);

        // Animation: line grows from anchor to label
        float progress = Math.Min(1f, input.AppearProgress * 1.5f);

        // Build the path points based on LeadLine definition or use default
        // Pass labelPos so the line ends at the label
        var pathPoints = BuildLeadLinePath(anchor, labelPos, input.Control?.LeadLine, goesRight);

        // Draw line with animation
        using var linePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = lineColor,
            StrokeWidth = 1.5f,
            IsAntialias = true
        };

        // Calculate total path length for animation
        float totalLength = 0f;
        for (int i = 1; i < pathPoints.Count; i++)
        {
            totalLength += Distance(pathPoints[i - 1], pathPoints[i]);
        }

        // Draw path up to current animation progress
        float targetLength = totalLength * progress;
        var path = new SKPath();
        path.MoveTo(pathPoints[0]);

        float drawnLength = 0f;
        for (int i = 1; i < pathPoints.Count && drawnLength < targetLength; i++)
        {
            float segmentLength = Distance(pathPoints[i - 1], pathPoints[i]);
            float remainingLength = targetLength - drawnLength;

            if (remainingLength >= segmentLength)
            {
                // Draw full segment
                path.LineTo(pathPoints[i]);
                drawnLength += segmentLength;
            }
            else
            {
                // Draw partial segment
                float t = remainingLength / segmentLength;
                var partialEnd = new SKPoint(
                    pathPoints[i - 1].X + (pathPoints[i].X - pathPoints[i - 1].X) * t,
                    pathPoints[i - 1].Y + (pathPoints[i].Y - pathPoints[i - 1].Y) * t);
                path.LineTo(partialEnd);
                drawnLength += remainingLength;
            }
        }
        canvas.DrawPath(path, linePaint);

        // Draw endpoint dot at anchor
        if (progress > 0.8f)
        {
            using var dotPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = lineColor,
                IsAntialias = true
            };
            canvas.DrawCircle(anchor, 4f, dotPaint);
        }

        // Draw label at the specified label position when line is complete
        if (progress > 0.95f)
        {
            DrawInputLabel(canvas, labelPos, goesRight, input, alpha);
        }
    }

    /// <summary>
    /// Build the lead-line path points from anchor to label position.
    /// Uses LeadLine definition for intermediate segments, always ends at labelPos.
    /// Returns a list of points: anchor -> shelf end -> segment ends... -> label position
    /// </summary>
    private List<SKPoint> BuildLeadLinePath(SKPoint anchor, SKPoint labelPos, LeadLineDefinition? leadLine, bool defaultGoesRight)
    {
        var points = new List<SKPoint> { anchor };

        if (leadLine is null)
        {
            // Default: simple path from anchor to label
            // Add a midpoint to create a nice angled line
            bool goesRight = defaultGoesRight;
            float midX = goesRight ? anchor.X + 40 : anchor.X - 40;
            points.Add(new SKPoint(midX, anchor.Y)); // Horizontal shelf
            points.Add(labelPos); // End at label
            return points;
        }

        // Use JSON-defined lead-line shape
        bool shelfGoesRight = leadLine.ShelfSide.Equals("right", StringComparison.OrdinalIgnoreCase);
        // When mirrored, screen direction is inverted from viewbox direction
        bool screenGoesRight = _svgMirrored ? !shelfGoesRight : shelfGoesRight;
        float scaledShelfLength = leadLine.ShelfLength * _svgScale;

        // Shelf (horizontal)
        float shelfEndX = screenGoesRight ? anchor.X + scaledShelfLength : anchor.X - scaledShelfLength;
        var shelfEndPoint = new SKPoint(shelfEndX, anchor.Y);
        points.Add(shelfEndPoint);

        // Process intermediate segments (if any)
        if (leadLine.Segments is not null && leadLine.Segments.Count > 0)
        {
            var currentPoint = shelfEndPoint;
            int shelfDirection = screenGoesRight ? 1 : -1;

            // Process all but the last segment normally
            for (int i = 0; i < leadLine.Segments.Count - 1; i++)
            {
                var segment = leadLine.Segments[i];
                float scaledLength = segment.Length * _svgScale;
                float angleRad = segment.Angle * MathF.PI / 180f;

                float dx = MathF.Cos(angleRad) * scaledLength * shelfDirection;
                float dy = -MathF.Sin(angleRad) * scaledLength;

                var segmentEnd = new SKPoint(currentPoint.X + dx, currentPoint.Y + dy);
                points.Add(segmentEnd);
                currentPoint = segmentEnd;
            }
        }

        // Always end at the label position
        points.Add(labelPos);

        return points;
    }

    private static float Distance(SKPoint a, SKPoint b)
    {
        float dx = b.X - a.X;
        float dy = b.Y - a.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private void DrawInputLabel(SKCanvas canvas, SKPoint pos, bool goesRight, ActiveInputState input, byte alpha)
    {
        var control = input.Control;
        string label = control?.Label ?? input.Binding.ToUpper();

        var textColor = FUIColors.TextBright.WithAlpha(alpha);
        var dimColor = FUIColors.TextDim.WithAlpha(alpha);
        var activeColor = FUIColors.Active.WithAlpha(alpha);

        float labelWidth = 140f;
        float labelHeight = input.IsAxis ? 32f : 22f;
        float x = goesRight ? pos.X : pos.X - labelWidth;
        float y = pos.Y - labelHeight / 2;

        // Background frame
        var frameBounds = new SKRect(x, y, x + labelWidth, y + labelHeight);
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha((byte)(160 * alpha / 255)),
            IsAntialias = true
        };
        canvas.DrawRect(frameBounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = activeColor,
            StrokeWidth = 1f,
            IsAntialias = true
        };
        canvas.DrawRect(frameBounds, framePaint);

        // Label text
        FUIRenderer.DrawText(canvas, label, new SKPoint(x + 5, y + 14), textColor, 11f);

        // Value indicator
        if (input.IsAxis)
        {
            // Data bar for axes
            float barWidth = labelWidth - 10;
            float barHeight = 10f;
            float value = (input.Value + 1f) / 2f; // Normalize -1..1 to 0..1
            var barBounds = new SKRect(x + 5, y + 18, x + 5 + barWidth, y + 18 + barHeight);
            FUIRenderer.DrawDataBar(canvas, barBounds, value, activeColor, FUIColors.Frame.WithAlpha(alpha));
        }
        else
        {
            // Button indicator: show PRESSED or binding
            string valueText = input.Value > 0.5f ? "PRESSED" : input.Binding.ToUpper();
            var valueColor = input.Value > 0.5f ? activeColor : dimColor;
            FUIRenderer.DrawText(canvas, valueText, new SKPoint(x + labelWidth - 60, y + 14), valueColor, 9f);
        }
    }

    private SKPoint SvgToScreen(float svgX, float svgY, SKRect svgBounds)
    {
        // Convert SVG coordinates to screen coordinates
        // Account for the fact that the SVG Picture.CullRect may not start at (0,0)
        // The svgBounds is the CullRect (actual content bounds), not viewBox bounds
        // SVG coordinates are in viewBox space, but rendering uses CullRect
        float relativeX = svgX - svgBounds.Left;
        float relativeY = svgY - svgBounds.Top;
        float screenX = _svgOffset.X + relativeX * _svgScale;
        float screenY = _svgOffset.Y + relativeY * _svgScale;
        return new SKPoint(screenX, screenY);
    }

    private void DrawDeviceSilhouette(SKCanvas canvas, SKRect bounds)
    {
        // Draw the actual SVG if loaded, otherwise fallback to simple outline
        var activeSvg = GetActiveSvg();
        if (activeSvg?.Picture is not null)
        {
            bool mirror = _deviceMap?.Mirror ?? false;
            DrawSvgInBounds(canvas, activeSvg, bounds, mirror);
        }
        else
        {
            DrawJoystickOutlineFallback(canvas, bounds);
        }
    }

    private void DrawSvgInBounds(SKCanvas canvas, SKSvg svg, SKRect bounds, bool mirror = false)
    {
        if (svg.Picture is null) return;

        var svgBounds = svg.Picture.CullRect;
        if (svgBounds.Width <= 0 || svgBounds.Height <= 0) return;

        // Calculate scale to fit SVG within bounds while maintaining aspect ratio
        float scaleX = bounds.Width / svgBounds.Width;
        float scaleY = bounds.Height / svgBounds.Height;
        float scale = Math.Min(scaleX, scaleY) * 0.95f; // 95% - make it larger!

        float scaledWidth = svgBounds.Width * scale;
        float scaledHeight = svgBounds.Height * scale;

        // Center the SVG within bounds
        // Account for CullRect origin - we need to translate so content starts at the center position
        float offsetX = bounds.Left + (bounds.Width - scaledWidth) / 2 - svgBounds.Left * scale;
        float offsetY = bounds.Top + (bounds.Height - scaledHeight) / 2 - svgBounds.Top * scale;

        // Store transform info for hit testing and coordinate conversion
        _svgScale = scale;
        _svgOffset = new SKPoint(offsetX, offsetY);
        _svgMirrored = mirror;

        canvas.Save();
        canvas.Translate(offsetX, offsetY);

        if (mirror)
        {
            // Flip horizontally: translate to right edge, then scale X by -1
            canvas.Translate(scaledWidth, 0);
            canvas.Scale(-scale, scale);
        }
        else
        {
            canvas.Scale(scale);
        }

        canvas.DrawPicture(svg.Picture);
        canvas.Restore();
    }

    private void DrawJoystickOutlineFallback(SKCanvas canvas, SKRect bounds)
    {
        // Fallback simple outline when SVG not loaded
        using var outlinePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Primary.WithAlpha(60),
            StrokeWidth = 1.5f,
            IsAntialias = true
        };

        float centerX = bounds.MidX;
        float stickWidth = 36f;
        float baseWidth = 70f;

        // Stick shaft
        canvas.DrawLine(centerX, bounds.Top + 36, centerX, bounds.Bottom - 56, outlinePaint);

        // Stick top (grip area)
        var gripRect = new SKRect(centerX - stickWidth / 2, bounds.Top + 24,
                                   centerX + stickWidth / 2, bounds.Top + 84);
        canvas.DrawRoundRect(gripRect, 8, 8, outlinePaint);

        // Base
        var baseRect = new SKRect(centerX - baseWidth / 2, bounds.Bottom - 65,
                                   centerX + baseWidth / 2, bounds.Bottom - 30);
        canvas.DrawRoundRect(baseRect, 4, 4, outlinePaint);

        // Hat indicator (small circle on top)
        canvas.DrawCircle(centerX, bounds.Top + 45, 7, outlinePaint);

        // Trigger area (small rect on front)
        var triggerRect = new SKRect(centerX + stickWidth / 2 - 4, bounds.Top + 65,
                                      centerX + stickWidth / 2 + 12, bounds.Top + 82);
        canvas.DrawRect(triggerRect, outlinePaint);

        // Button cluster on side
        canvas.DrawCircle(centerX - stickWidth / 2 - 8, bounds.Top + 55, 5, outlinePaint);
        canvas.DrawCircle(centerX - stickWidth / 2 - 8, bounds.Top + 70, 5, outlinePaint);
    }

    private void DrawDeviceActionsPanel(SKCanvas canvas, SKRect bounds)
    {
        float pad = FUIRenderer.PanelPadding;
        float frameInset = 5f;

        // Panel shadow
        FUIRenderer.DrawPanelShadow(canvas, bounds, 3f, 3f, 10f);

        // Panel background
        var contentBounds = new SKRect(bounds.Left + frameInset, bounds.Top + frameInset,
                                        bounds.Right - frameInset, bounds.Bottom - frameInset);
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(140),
            IsAntialias = true
        };
        canvas.DrawRect(contentBounds, bgPaint);

        // L-corner frame
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame, 35f, 10f);

        // Header
        float titleBarHeight = 32f;
        var titleBounds = new SKRect(contentBounds.Left, contentBounds.Top, contentBounds.Right, contentBounds.Top + titleBarHeight);
        FUIRenderer.DrawPanelTitle(canvas, titleBounds, "D3", "DEVICE ACTIONS");

        float y = contentBounds.Top + titleBarHeight + pad;
        float buttonHeight = 32f;
        float buttonGap = 8f;

        // Check if we have a physical device selected
        bool hasPhysicalDevice = _deviceCategory == 0 && _selectedDevice >= 0 &&
                                  _selectedDevice < _devices.Count && !_devices[_selectedDevice].IsVirtual;

        if (hasPhysicalDevice)
        {
            var device = _devices[_selectedDevice];
            bool isDisconnected = !device.IsConnected;

            // Device info
            FUIRenderer.DrawText(canvas, isDisconnected ? "DISCONNECTED DEVICE" : "SELECTED DEVICE",
                new SKPoint(contentBounds.Left + pad, y), isDisconnected ? FUIColors.Danger : FUIColors.TextDim, 9f);
            y += 16f;

            string shortName = device.Name.Length > 22 ? device.Name.Substring(0, 20) + "..." : device.Name;
            FUIRenderer.DrawText(canvas, shortName, new SKPoint(contentBounds.Left + pad, y),
                isDisconnected ? FUIColors.TextDim : FUIColors.TextPrimary, 11f);
            y += 20f;

            // Device stats
            FUIRenderer.DrawText(canvas, $"{device.AxisCount} axes  {device.ButtonCount} btns  {device.HatCount} hats",
                new SKPoint(contentBounds.Left + pad, y), FUIColors.TextDim, 9f);
            y += 24f;

            float buttonWidth = contentBounds.Width - pad * 2;

            if (isDisconnected)
            {
                // Disconnected device: Show Clear Mappings and Remove Device buttons
                _map1to1ButtonBounds = SKRect.Empty;

                // Clear mappings button - use FUIRenderer.DrawButton for consistent styling
                _clearMappingsButtonBounds = new SKRect(contentBounds.Left + pad, y, contentBounds.Left + pad + buttonWidth, y + buttonHeight);
                var clearState = _clearMappingsButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
                FUIRenderer.DrawButton(canvas, _clearMappingsButtonBounds, "CLEAR MAPPINGS", clearState, FUIColors.Danger);
                y += buttonHeight + buttonGap;

                // Remove device button - use FUIRenderer.DrawButton for consistent styling
                _removeDeviceButtonBounds = new SKRect(contentBounds.Left + pad, y, contentBounds.Left + pad + buttonWidth, y + buttonHeight);
                var removeState = _removeDeviceButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
                FUIRenderer.DrawButton(canvas, _removeDeviceButtonBounds, "REMOVE DEVICE", removeState, FUIColors.Danger);
            }
            else
            {
                // Connected device: Show Map 1:1 and Clear Mappings buttons
                _removeDeviceButtonBounds = SKRect.Empty;

                // Map 1:1 to vJoy button - use FUIRenderer.DrawButton for consistent styling
                _map1to1ButtonBounds = new SKRect(contentBounds.Left + pad, y, contentBounds.Left + pad + buttonWidth, y + buttonHeight);
                var mapState = _map1to1ButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
                FUIRenderer.DrawButton(canvas, _map1to1ButtonBounds, "MAP 1:1 TO VJOY", mapState);
                y += buttonHeight + buttonGap;

                // Clear mappings button - use FUIRenderer.DrawButton for consistent styling
                _clearMappingsButtonBounds = new SKRect(contentBounds.Left + pad, y, contentBounds.Left + pad + buttonWidth, y + buttonHeight);
                var clearState2 = _clearMappingsButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
                FUIRenderer.DrawButton(canvas, _clearMappingsButtonBounds, "CLEAR MAPPINGS", clearState2, FUIColors.Danger);
            }
        }
        else
        {
            // No device selected
            _map1to1ButtonBounds = SKRect.Empty;
            _clearMappingsButtonBounds = SKRect.Empty;
            _removeDeviceButtonBounds = SKRect.Empty;

            FUIRenderer.DrawText(canvas, "Select a physical device",
                new SKPoint(contentBounds.Left + pad, y + 20), FUIColors.TextDim, 10f);
            FUIRenderer.DrawText(canvas, "to see available actions",
                new SKPoint(contentBounds.Left + pad, y + 36), FUIColors.TextDim, 10f);
        }
    }

    private void DrawDeviceActionButton(SKCanvas canvas, SKRect bounds, string text, bool isHovered,
        bool isDestructive = false, bool isDangerous = false)
    {
        // isDangerous uses Danger (red/orange) color, isDestructive uses Warning (yellow)
        var accentColor = isDangerous ? FUIColors.Danger : (isDestructive ? FUIColors.Warning : FUIColors.Active);

        // Button background
        var bgColor = isHovered
            ? accentColor.WithAlpha(40)
            : (isDangerous ? FUIColors.Danger.WithAlpha(15) : FUIColors.Background2.WithAlpha(80));
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = bgColor,
            IsAntialias = true
        };
        canvas.DrawRect(bounds, bgPaint);

        // Button border
        var borderColor = isHovered ? accentColor : (isDangerous ? FUIColors.Danger.WithAlpha(100) : FUIColors.Frame);
        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = borderColor,
            StrokeWidth = 1f,
            IsAntialias = true
        };
        canvas.DrawRect(bounds, borderPaint);

        // Button text
        var textColor = isHovered ? accentColor : (isDangerous ? FUIColors.Danger : FUIColors.TextPrimary);
        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 10f);
    }

    private void DrawStatusPanel(SKCanvas canvas, SKRect bounds)
    {
        float pad = FUIRenderer.PanelPadding;
        float itemGap = FUIRenderer.SpaceSM;
        float frameInset = 5f;

        // Panel shadow
        FUIRenderer.DrawPanelShadow(canvas, bounds, 3f, 3f, 10f);

        // Panel background
        var contentBounds = new SKRect(bounds.Left + frameInset, bounds.Top + frameInset,
                                        bounds.Right - frameInset, bounds.Bottom - frameInset);
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(140),
            IsAntialias = true
        };
        canvas.DrawRect(contentBounds, bgPaint);

        // L-corner frame
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame, 35f, 10f);

        // Header
        float titleBarHeight = 32f;
        var titleBounds = new SKRect(contentBounds.Left, contentBounds.Top, contentBounds.Right, contentBounds.Top + titleBarHeight);
        FUIRenderer.DrawPanelTitle(canvas, titleBounds, "S1", "STATUS");

        // Status items
        float statusItemHeight = 32f;
        float itemY = contentBounds.Top + titleBarHeight + pad;

        // vJoy driver status
        bool vjoyActive = _vjoyService.IsInitialized;
        DrawStatusItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2, "VJOY DRIVER",
            vjoyActive ? "ACTIVE" : "NOT FOUND", vjoyActive ? FUIColors.Active : FUIColors.Danger);
        itemY += statusItemHeight + itemGap;

        // Forwarding status - this is the key item
        DrawStatusItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2, "FORWARDING",
            _isForwarding ? "RUNNING" : "STOPPED", _isForwarding ? FUIColors.Active : FUIColors.TextDim);
        itemY += statusItemHeight + itemGap;

        DrawStatusItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2, "INPUT RATE", "100 HZ", FUIColors.TextPrimary);
        itemY += statusItemHeight + itemGap;

        // Profile status
        string profileName = _mappingEngine.ActiveProfile?.Name ?? "NONE";
        DrawStatusItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2, "PROFILE", profileName.ToUpper(), FUIColors.TextPrimary);

        // Start/Stop Forwarding button at bottom
        float buttonHeight = 36f;
        float buttonWidth = contentBounds.Width - pad * 2;
        float buttonY = contentBounds.Bottom - buttonHeight - pad;

        if (_isForwarding)
        {
            // Show STOP button
            _stopForwardingButtonBounds = new SKRect(contentBounds.Left + pad, buttonY,
                contentBounds.Left + pad + buttonWidth, buttonY + buttonHeight);
            _startForwardingButtonBounds = SKRect.Empty;
            DrawForwardingButton(canvas, _stopForwardingButtonBounds, "STOP FORWARDING",
                _stopForwardingButtonHovered, isStop: true);
        }
        else
        {
            // Show START button
            _startForwardingButtonBounds = new SKRect(contentBounds.Left + pad, buttonY,
                contentBounds.Left + pad + buttonWidth, buttonY + buttonHeight);
            _stopForwardingButtonBounds = SKRect.Empty;
            DrawForwardingButton(canvas, _startForwardingButtonBounds, "START FORWARDING",
                _startForwardingButtonHovered, isStop: false);
        }
    }

    private void DrawForwardingButton(SKCanvas canvas, SKRect bounds, string text, bool isHovered, bool isStop)
    {
        // Button colors - green for start, red/orange for stop
        var accentColor = isStop ? FUIColors.Danger : FUIColors.Active;

        // Button background
        var bgColor = isHovered
            ? accentColor.WithAlpha(50)
            : (isStop ? FUIColors.Danger.WithAlpha(20) : FUIColors.Active.WithAlpha(20));
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = bgColor,
            IsAntialias = true
        };
        canvas.DrawRect(bounds, bgPaint);

        // Button border
        var borderColor = isHovered ? accentColor : accentColor.WithAlpha(150);
        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = borderColor,
            StrokeWidth = isHovered ? 2f : 1f,
            IsAntialias = true
        };
        canvas.DrawRect(bounds, borderPaint);

        // Button text
        var textColor = isHovered ? FUIColors.TextBright : accentColor;
        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 11f, isHovered);
    }

    private void DrawStatusItem(SKCanvas canvas, float x, float y, float width, string label, string value, SKColor valueColor)
    {
        FUIRenderer.DrawText(canvas, label, new SKPoint(x, y + 12), FUIColors.TextDim, 11f);

        var dotColor = valueColor == FUIColors.Active ? valueColor : FUIColors.Primary.WithAlpha(100);
        float dotX = x + width - 70;
        FUIRenderer.DrawGlowingDot(canvas, new SKPoint(dotX, y + 8), dotColor, 2f, 4f);

        FUIRenderer.DrawText(canvas, value, new SKPoint(dotX + 10, y + 12), valueColor, 11f);
    }

    private void DrawLayerIndicator(SKCanvas canvas, float x, float y, float width, string name, bool isActive)
    {
        float height = 22f;
        var bounds = new SKRect(x, y, x + width, y + height);
        var frameColor = isActive ? FUIColors.Active : FUIColors.FrameDim;
        var fillColor = isActive ? FUIColors.Active.WithAlpha(40) : SKColors.Transparent;

        FUIRenderer.FillFrame(canvas, bounds, fillColor, 4f);
        FUIRenderer.DrawFrame(canvas, bounds, frameColor, 4f, 1f, isActive);

        var textColor = isActive ? FUIColors.TextBright : FUIColors.TextDim;
        FUIRenderer.DrawTextCentered(canvas, name, bounds, textColor, 10f, isActive);
    }

    /// <summary>
    /// Start forwarding physical device input to vJoy virtual devices.
    /// Uses the active profile's mappings to determine what gets forwarded.
    /// </summary>
    private void StartForwarding()
    {
        if (_isForwarding) return;

        // Load the active profile into the mapping engine
        var profile = _profileService.ActiveProfile;
        if (profile is null)
        {
            // No profile - show error to user
            MessageBox.Show(
                "No active profile found.\n\nTo create mappings:\n1. Select a physical device\n2. Click 'MAP 1:1 TO VJOY'",
                "Cannot Start Forwarding",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        // Check if profile has any mappings
        if (profile.AxisMappings.Count == 0 && profile.ButtonMappings.Count == 0 && profile.HatMappings.Count == 0)
        {
            MessageBox.Show(
                $"Profile '{profile.Name}' has no mappings.\n\nTo create mappings:\n1. Select a physical device\n2. Click 'MAP 1:1 TO VJOY'",
                "Cannot Start Forwarding",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _mappingEngine.LoadProfile(profile);

        // Check vJoy is initialized
        if (!_vjoyService.IsInitialized)
        {
            MessageBox.Show(
                "vJoy driver is not initialized.\n\nPlease ensure vJoy is installed correctly.",
                "Cannot Start Forwarding",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        // Get the required vJoy devices from the profile
        var requiredDevices = profile.AxisMappings
            .Select(m => m.Output.VJoyDevice)
            .Concat(profile.ButtonMappings.Select(m => m.Output.VJoyDevice))
            .Concat(profile.HatMappings.Select(m => m.Output.VJoyDevice))
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        // Check each required device
        foreach (var deviceId in requiredDevices)
        {
            var info = _vjoyService.GetDeviceInfo(deviceId);
            if (!info.Exists)
            {
                MessageBox.Show(
                    $"vJoy device {deviceId} does not exist.\n\nPlease configure vJoy device {deviceId} using 'Configure vJoy'.",
                    "Cannot Start Forwarding",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
        }

        if (!_mappingEngine.Start())
        {
            // Get more detailed error info including owner PID
            int ourPid = Environment.ProcessId;
            var statusMessages = requiredDevices
                .Select(id => {
                    var info = _vjoyService.GetDeviceInfo(id);
                    int ownerPid = VJoy.VJoyInterop.GetOwnerPid(id);
                    return $"vJoy {id}: {info.Status} (Owner PID: {ownerPid}, Our PID: {ourPid})";
                })
                .ToList();

            MessageBox.Show(
                $"Failed to acquire vJoy device(s).\n\nDevice status:\n{string.Join("\n", statusMessages)}\n\nIf Owner PID matches Our PID, try restarting the app.\nIf different, another app owns the device.",
                "Cannot Start Forwarding",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        _isForwarding = true;
        System.Diagnostics.Debug.WriteLine($"Started forwarding with profile: {profile.Name}");
    }

    /// <summary>
    /// Stop forwarding physical device input to vJoy virtual devices.
    /// </summary>
    private void StopForwarding()
    {
        if (!_isForwarding) return;

        _mappingEngine.Stop();
        _isForwarding = false;
        System.Diagnostics.Debug.WriteLine("Stopped forwarding");
    }

    /// <summary>
    /// Get the vJoy device assignment string for a physical device.
    /// Looks up which vJoy device(s) this physical device is mapped to in the active profile.
    /// </summary>
    private string GetVJoyAssignmentForDevice(PhysicalDeviceInfo device)
    {
        var profile = _profileService.ActiveProfile;
        if (profile is null) return "";

        string deviceId = device.InstanceGuid.ToString();

        // Find all vJoy devices this physical device maps to
        var vjoyIds = new HashSet<uint>();

        foreach (var mapping in profile.AxisMappings)
        {
            if (mapping.Inputs.Any(i => i.DeviceId == deviceId) && mapping.Output.VJoyDevice > 0)
                vjoyIds.Add(mapping.Output.VJoyDevice);
        }
        foreach (var mapping in profile.ButtonMappings)
        {
            if (mapping.Inputs.Any(i => i.DeviceId == deviceId) && mapping.Output.VJoyDevice > 0)
                vjoyIds.Add(mapping.Output.VJoyDevice);
        }
        foreach (var mapping in profile.HatMappings)
        {
            if (mapping.Inputs.Any(i => i.DeviceId == deviceId) && mapping.Output.VJoyDevice > 0)
                vjoyIds.Add(mapping.Output.VJoyDevice);
        }

        if (vjoyIds.Count == 0) return "";
        if (vjoyIds.Count == 1) return $"VJOY:{vjoyIds.First()}";
        return $"VJOY:{string.Join(",", vjoyIds.OrderBy(x => x))}";
    }

    /// <summary>
    /// Get the primary physical device name for a virtual (vJoy) device
    /// </summary>
    private string GetPrimaryDeviceForVJoyDevice(PhysicalDeviceInfo vjoyDevice)
    {
        if (!vjoyDevice.IsVirtual) return "";

        var profile = _profileService.ActiveProfile;
        if (profile is null) return "";

        // Extract vJoy ID from device name (e.g., "vJoy Device 1" -> 1)
        uint vjoyId = 0;
        var match = System.Text.RegularExpressions.Regex.Match(vjoyDevice.Name, @"\d+");
        if (match.Success && uint.TryParse(match.Value, out uint parsedId))
        {
            vjoyId = parsedId;
        }

        if (vjoyId == 0) return "";

        // Get the primary physical device GUID for this vJoy slot
        var primaryGuid = profile.GetPrimaryDeviceForVJoy(vjoyId);
        if (string.IsNullOrEmpty(primaryGuid)) return "";

        // Find the physical device by GUID
        var primaryDevice = _devices.FirstOrDefault(d =>
            !d.IsVirtual && d.InstanceGuid.ToString().Equals(primaryGuid, StringComparison.OrdinalIgnoreCase));

        if (primaryDevice is null) return "";

        // Return a shortened name for display
        string name = primaryDevice.Name;
        if (name.Length > 20)
            name = name.Substring(0, 17) + "...";

        return $"→ {name}";
    }

    #endregion
}
