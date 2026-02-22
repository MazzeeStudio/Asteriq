using Asteriq.Models;
using SkiaSharp;

namespace Asteriq.UI;

/// <summary>
/// Renders animated lead-lines from joystick control anchors to floating labels.
/// All methods are stateless — SVG transform info is passed as parameters.
/// </summary>
internal static class DeviceLeadLineRenderer
{
    internal static float Distance(SKPoint a, SKPoint b)
    {
        float dx = b.X - a.X;
        float dy = b.Y - a.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <param name="svgMirrored">Whether the SVG silhouette is being rendered mirrored.</param>
    /// <param name="svgScale">Current SVG render scale factor.</param>
    internal static void DrawInputLeadLine(SKCanvas canvas, SKPoint anchor, SKPoint labelPos, bool goesRight,
        float opacity, ActiveInputState input, bool svgMirrored, float svgScale)
    {
        byte alpha = (byte)(255 * opacity * input.AppearProgress);
        var lineColor = FUIColors.Active.WithAlpha(alpha);

        float progress = Math.Min(1f, input.AppearProgress * 1.5f);

        var pathPoints = BuildLeadLinePath(anchor, labelPos, input.Control?.LeadLine, goesRight, svgMirrored, svgScale);

        using var linePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = lineColor,
            StrokeWidth = 1.5f,
            IsAntialias = true
        };

        float totalLength = 0f;
        for (int i = 1; i < pathPoints.Count; i++)
        {
            totalLength += Distance(pathPoints[i - 1], pathPoints[i]);
        }

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
                path.LineTo(pathPoints[i]);
                drawnLength += segmentLength;
            }
            else
            {
                float t = remainingLength / segmentLength;
                var partialEnd = new SKPoint(
                    pathPoints[i - 1].X + (pathPoints[i].X - pathPoints[i - 1].X) * t,
                    pathPoints[i - 1].Y + (pathPoints[i].Y - pathPoints[i - 1].Y) * t);
                path.LineTo(partialEnd);
                drawnLength += remainingLength;
            }
        }
        canvas.DrawPath(path, linePaint);

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

        if (progress > 0.95f)
        {
            DrawInputLabel(canvas, labelPos, goesRight, input, alpha);
        }
    }

    /// <summary>
    /// Build the lead-line path points from anchor to label position.
    /// Uses LeadLine definition for intermediate segments, always ends at labelPos.
    /// </summary>
    /// <param name="svgMirrored">Whether the SVG silhouette is being rendered mirrored.</param>
    /// <param name="svgScale">Current SVG render scale factor.</param>
    internal static List<SKPoint> BuildLeadLinePath(SKPoint anchor, SKPoint labelPos, LeadLineDefinition? leadLine,
        bool defaultGoesRight, bool svgMirrored, float svgScale)
    {
        var points = new List<SKPoint> { anchor };

        if (leadLine is null)
        {
            float midX = defaultGoesRight ? anchor.X + 40 : anchor.X - 40;
            points.Add(new SKPoint(midX, anchor.Y));
            points.Add(labelPos);
            return points;
        }

        bool shelfGoesRight = leadLine.ShelfSide.Equals("right", StringComparison.OrdinalIgnoreCase);
        bool screenGoesRight = svgMirrored ? !shelfGoesRight : shelfGoesRight;
        float scaledShelfLength = leadLine.ShelfLength * svgScale;

        float shelfEndX = screenGoesRight ? anchor.X + scaledShelfLength : anchor.X - scaledShelfLength;
        var shelfEndPoint = new SKPoint(shelfEndX, anchor.Y);
        points.Add(shelfEndPoint);

        if (leadLine.Segments is not null && leadLine.Segments.Count > 0)
        {
            var currentPoint = shelfEndPoint;
            int shelfDirection = screenGoesRight ? 1 : -1;

            for (int i = 0; i < leadLine.Segments.Count - 1; i++)
            {
                var segment = leadLine.Segments[i];
                float scaledLength = segment.Length * svgScale;
                float angleRad = segment.Angle * MathF.PI / 180f;

                float dx = MathF.Cos(angleRad) * scaledLength * shelfDirection;
                float dy = -MathF.Sin(angleRad) * scaledLength;

                var segmentEnd = new SKPoint(currentPoint.X + dx, currentPoint.Y + dy);
                points.Add(segmentEnd);
                currentPoint = segmentEnd;
            }
        }

        points.Add(labelPos);
        return points;
    }

    internal static void DrawInputLabel(SKCanvas canvas, SKPoint pos, bool goesRight, ActiveInputState input, byte alpha)
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

        FUIRenderer.DrawText(canvas, label, new SKPoint(x + 5, y + 14), textColor, 11f);

        if (input.IsAxis)
        {
            float barWidth = labelWidth - 10;
            float barHeight = 10f;
            float value = (input.Value + 1f) / 2f;
            var barBounds = new SKRect(x + 5, y + 18, x + 5 + barWidth, y + 18 + barHeight);
            FUIRenderer.DrawDataBar(canvas, barBounds, value, activeColor, FUIColors.Frame.WithAlpha(alpha));
        }
        else
        {
            string valueText = input.Value > 0.5f ? "PRESSED" : input.Binding.ToUpper();
            var valueColor = input.Value > 0.5f ? activeColor : dimColor;
            FUIRenderer.DrawText(canvas, valueText, new SKPoint(x + labelWidth - 60, y + 14), valueColor, 9f);
        }
    }
}
