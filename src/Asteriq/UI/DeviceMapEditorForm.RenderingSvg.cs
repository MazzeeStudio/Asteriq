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
    private void DrawSvgPanel(SKCanvas canvas)
    {
        // Background
        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background0);
        canvas.DrawRect(_svgPanelBounds, bgPaint);

        // Frame
        using var framePaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame);
        canvas.DrawRect(_svgPanelBounds, framePaint);

        // Draw SVG
        if (_currentSvg?.Picture is not null)
        {
            var svgBounds = new SKRect(_svgPanelBounds.Left + 10, _svgPanelBounds.Top + 10,
                _svgPanelBounds.Right - 10, _svgPanelBounds.Bottom - 10);
            var t = FUIRenderer.DrawSvgInBounds(canvas, _currentSvg, svgBounds, _deviceMap.Mirror);
            ApplySvgTransform(t);
        }
        else
        {
            FUIRenderer.DrawTextCentered(canvas, "No SVG loaded", _svgPanelBounds, FUIColors.TextDisabled, 17f);
        }

        // Draw control overlays
        DrawControlOverlays(canvas);
    }

    private void ApplySvgTransform(FUIRenderer.SvgTransform t)
    {
        _svgScale = t.Scale;
        _svgOffset = t.Offset;
        _svgScaledWidth = t.ScaledWidth;
    }

    private void DrawControlOverlays(SKCanvas canvas)
    {
        // CA2000: using var inside foreach is safe â€” analyzer false positive
#pragma warning disable CA2000
        foreach (var kvp in _deviceMap.Controls)
        {
            var key = kvp.Key;
            var control = kvp.Value;

            if (control.Anchor is null) continue;

            bool isSelected = key == _selectedControlKey;
            var anchorScreen = ViewBoxToScreen(control.Anchor.X, control.Anchor.Y);

            // Draw anchor point
            float radius = isSelected ? 10f : 6f;
            using var anchorPaint = FUIRenderer.CreateFillPaint(isSelected ? FUIColors.Active : FUIColors.Primary.WithAlpha(180));
            canvas.DrawCircle(anchorScreen, radius, anchorPaint);

            // Draw anchor outline
            using var outlinePaint = FUIRenderer.CreateStrokePaint(isSelected ? FUIColors.Active : FUIColors.Frame, 2f);
            canvas.DrawCircle(anchorScreen, radius, outlinePaint);

            // Calculate label position (offset from anchor in viewbox coords)
            float labelX = control.Anchor.X + (control.LabelOffset?.X ?? 50);
            float labelY = control.Anchor.Y + (control.LabelOffset?.Y ?? 0);
            var labelScreen = ViewBoxToScreen(labelX, labelY);

            // Measure label text
            var labelText = control.Label ?? key;
            var labelBounds = MeasureText(labelText, 14f);
            float padding = 6f;
            float shelfLength = 10f;

            // Determine if anchor is to the left or right of the label
            bool anchorOnLeft = anchorScreen.X < labelScreen.X;

            // Calculate label box - text position is top-left of text
            var labelRect = new SKRect(
                labelScreen.X - padding,
                labelScreen.Y - padding,
                labelScreen.X + labelBounds.Width + padding,
                labelScreen.Y + labelBounds.Height + padding);

            // Shelf extends horizontally from the appropriate side of the label box
            SKPoint shelfStart, shelfEnd;
            if (anchorOnLeft)
            {
                // Shelf extends left from left edge of label box
                shelfStart = new SKPoint(labelRect.Left, labelRect.MidY);
                shelfEnd = new SKPoint(labelRect.Left - shelfLength, labelRect.MidY);
            }
            else
            {
                // Shelf extends right from right edge of label box
                shelfStart = new SKPoint(labelRect.Right, labelRect.MidY);
                shelfEnd = new SKPoint(labelRect.Right + shelfLength, labelRect.MidY);
            }

            // Draw lead line (connects to end of shelf)
            DrawLeadLine(canvas, anchorScreen, shelfEnd, control.LeadLine, isSelected);

            // Draw the shelf connector line
            using var shelfPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = isSelected ? FUIColors.Active : FUIColors.Primary.WithAlpha(150),
                StrokeWidth = isSelected ? 2f : 1f,
                IsAntialias = true
            };
            canvas.DrawLine(shelfStart, shelfEnd, shelfPaint);

            // Draw label background
            FUIRenderer.DrawRoundedPanel(canvas, labelRect,
                isSelected ? FUIColors.SelectionBg : FUIColors.Background1.WithAlpha(200),
                isSelected ? FUIColors.Active : FUIColors.Frame);

            // Draw label text (baseline adjusted)
            using var textPaint = new SKPaint { TextSize = 14f, IsAntialias = true };
            var metrics = textPaint.FontMetrics;
            float textY = labelScreen.Y - metrics.Ascent; // Adjust for baseline
            FUIRenderer.DrawText(canvas, labelText, new SKPoint(labelScreen.X, textY),
                FUIColors.ContentColor(isSelected), 14f);
        }
#pragma warning restore CA2000
    }

    private void DrawLeadLine(SKCanvas canvas, SKPoint anchor, SKPoint label, LeadLineDefinition? leadLine, bool selected)
    {
        // Only track handle positions for the selected control
        if (selected)
        {
            _segmentEndHandles.Clear();
            _shelfEndHandle = default;
        }

        using var linePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = selected ? FUIColors.Active : FUIColors.Primary.WithAlpha(150),
            StrokeWidth = selected ? 2f : 1f,
            IsAntialias = true
        };

        if (leadLine is null)
        {
            // Simple straight line
            canvas.DrawLine(anchor, label, linePaint);
            return;
        }

        // Build path from lead line definition
        using var path = new SKPath();
        path.MoveTo(anchor);

        // Shelf segment - when mirrored, screen direction is inverted
        bool goesRight = leadLine.ShelfSide == "right";
        // When mirrored, a "right" shelf in viewbox goes LEFT on screen
        bool screenGoesRight = _deviceMap.Mirror ? !goesRight : goesRight;
        float shelfEndX = anchor.X + (screenGoesRight ? 1 : -1) * leadLine.ShelfLength * _svgScale;
        var shelfEnd = new SKPoint(shelfEndX, anchor.Y);
        path.LineTo(shelfEnd);

        // Store shelf end handle position (only for selected)
        if (selected)
        {
            _shelfEndHandle = shelfEnd;
        }

        // Additional segments
        var currentPoint = shelfEnd;
        if (leadLine.Segments is not null)
        {
            foreach (var seg in leadLine.Segments)
            {
                float angleRad = seg.Angle * MathF.PI / 180f;
                float dx = MathF.Cos(angleRad) * seg.Length * _svgScale;
                float dy = -MathF.Sin(angleRad) * seg.Length * _svgScale; // Y is inverted

                if (!screenGoesRight) dx = -dx; // Mirror for left shelf (screen direction)

                currentPoint = new SKPoint(currentPoint.X + dx, currentPoint.Y + dy);
                path.LineTo(currentPoint);

                // Store segment end handle position (only for selected)
                if (selected)
                {
                    _segmentEndHandles.Add(currentPoint);
                }
            }
        }

        // Always draw final connector to label
        path.LineTo(label);

        canvas.DrawPath(path, linePaint);

        // Draw draggable handles only for selected control WITH segments
        // (without segments, the lead line is in "simple mode" - shelf + auto line to label)
        // The handles let users control segment angles/lengths; the final connector to label is automatic
        if (selected && leadLine.Segments is not null && leadLine.Segments.Count > 0)
        {
            DrawSegmentHandles(canvas, anchor, leadLine);
        }
    }

    private void DrawSegmentHandles(SKCanvas canvas, SKPoint anchor, LeadLineDefinition leadLine)
    {
        using var handleFill = FUIRenderer.CreateFillPaint(FUIColors.ActiveStrong);
        using var handleStroke = FUIRenderer.CreateStrokePaint(FUIColors.Active, 2f);
        using var handleHover = FUIRenderer.CreateFillPaint(FUIColors.Primary.WithAlpha(100));

        // Draw shelf end handle
        bool shelfHovered = SKPoint.Distance(_shelfEndHandle, _mousePos) < HandleHitRadius;
        if (shelfHovered)
        {
            canvas.DrawCircle(_shelfEndHandle, HandleRadius + 3, handleHover);
        }
        canvas.DrawCircle(_shelfEndHandle, HandleRadius, handleFill);
        canvas.DrawCircle(_shelfEndHandle, HandleRadius, handleStroke);

        // Draw segment end handles
        for (int i = 0; i < _segmentEndHandles.Count; i++)
        {
            var handlePos = _segmentEndHandles[i];
            bool segHovered = SKPoint.Distance(handlePos, _mousePos) < HandleHitRadius;
            if (segHovered)
            {
                canvas.DrawCircle(handlePos, HandleRadius + 3, handleHover);
            }
            canvas.DrawCircle(handlePos, HandleRadius, handleFill);
            canvas.DrawCircle(handlePos, HandleRadius, handleStroke);

            // Draw segment index number
            FUIRenderer.DrawTextCentered(canvas, (i + 1).ToString(),
                new SKRect(handlePos.X - 10, handlePos.Y - 10, handlePos.X + 10, handlePos.Y + 10),
                FUIColors.Background0, 12f);
        }
    }

}
#endif