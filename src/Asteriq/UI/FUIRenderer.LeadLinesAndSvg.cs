using Asteriq.Models;
using Asteriq.Services;
using Microsoft.Win32;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI;

public static partial class FUIRenderer
{
    /// <summary>
    /// Draws a lead line (callout line) from a label to a target point.
    /// FUI standard pattern: horizontal shelf â†’ 90Â° vertical â†’ horizontal to target
    ///
    /// LABEL â”€â”€â”€â”€â”€â”€â”
    ///             â”‚
    ///             â””â”€â”€â”€â”€â”€â”€â— target
    ///
    /// Never diagonal. Always orthogonal segments.
    /// </summary>
    public static void DrawLeadLine(SKCanvas canvas, SKPoint labelEnd, SKPoint target,
        SKColor color, float progress = 1f, bool showEndpoint = true, float lineWeight = LineWeightThin)
    {
        progress = Math.Clamp(progress, 0f, 1f);
        if (progress <= 0) return;

        // Calculate the path segments
        // Segment 1: Horizontal from label end to above/below target X
        // Segment 2: Vertical down/up to target Y
        // Segment 3: Horizontal to target

        float horizontalExtend = 20f; // How far horizontal before turning down
        float verticalX = target.X - horizontalExtend; // X position for vertical segment

        var point1 = labelEnd;
        var point2 = new SKPoint(verticalX, labelEnd.Y);  // End of first horizontal
        var point3 = new SKPoint(verticalX, target.Y);     // End of vertical (corner)
        var point4 = target;

        // Calculate total path length for animation
        float seg1Len = Math.Abs(point2.X - point1.X);
        float seg2Len = Math.Abs(point3.Y - point2.Y);
        float seg3Len = Math.Abs(point4.X - point3.X);
        float totalLen = seg1Len + seg2Len + seg3Len;

        if (totalLen < 1f) return;

        float seg1Ratio = seg1Len / totalLen;
        float seg2Ratio = seg2Len / totalLen;
        // seg3Ratio = 1 - seg1Ratio - seg2Ratio

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = lineWeight,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Square
        };

        using var path = new SKPath();
        path.MoveTo(point1);

        SKPoint currentEnd = point1;

        if (progress <= seg1Ratio)
        {
            // Drawing segment 1
            float segProgress = progress / seg1Ratio;
            currentEnd = Lerp(point1, point2, segProgress);
            path.LineTo(currentEnd);
        }
        else if (progress <= seg1Ratio + seg2Ratio)
        {
            // Drawing segment 2
            path.LineTo(point2);
            float segProgress = (progress - seg1Ratio) / seg2Ratio;
            currentEnd = Lerp(point2, point3, segProgress);
            path.LineTo(currentEnd);
        }
        else
        {
            // Drawing segment 3
            path.LineTo(point2);
            path.LineTo(point3);
            float segProgress = (progress - seg1Ratio - seg2Ratio) / (1f - seg1Ratio - seg2Ratio);
            currentEnd = Lerp(point3, point4, segProgress);
            path.LineTo(currentEnd);
        }

        canvas.DrawPath(path, paint);

        // Draw endpoint dot at current end position
        if (showEndpoint && progress > 0.05f)
        {
            DrawLeadLineEndpoint(canvas, currentEnd, color);
        }
    }

    /// <summary>
    /// Draws the small circle/dot at the end of a lead line
    /// Simple clean circle with subtle glow
    /// </summary>
    public static void DrawLeadLineEndpoint(SKCanvas canvas, SKPoint position, SKColor color, float radius = 3f)
    {
        // Glow first (behind)
        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = color.WithAlpha(50),
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(3f, 3f)
        };
        canvas.DrawCircle(position.X, position.Y, radius + 2f, glowPaint);

        // Outer ring
        using var ringPaint = CreateStrokePaint(color, 1.5f);
        canvas.DrawCircle(position.X, position.Y, radius, ringPaint);

        // Inner filled dot
        using var dotPaint = CreateFillPaint(color);
        canvas.DrawCircle(position.X, position.Y, radius * 0.35f, dotPaint);
    }

    /// <summary>
    /// Draws a complete callout with label on horizontal shelf and lead line to target.
    /// Label sits horizontally, line goes: horizontal â†’ vertical â†’ horizontal to target
    /// </summary>
    public static void DrawCallout(SKCanvas canvas, string label, SKPoint labelPos, SKPoint target,
        SKColor color, float progress = 1f, float fontSize = 13f)
    {
        // Draw label
        DrawText(canvas, label, labelPos, color, fontSize);

        // Calculate label end position (after text)
        using var textPaint = CreateTextPaint(color, fontSize);
        float textWidth = textPaint.MeasureText(label);
        var labelEnd = new SKPoint(labelPos.X + textWidth + 8, labelPos.Y - fontSize / 3);

        // Draw lead line (always uses standard FUI pattern now)
        DrawLeadLine(canvas, labelEnd, target, color, progress);
    }

    /// <summary>
    /// Draws a callout from the right side (label after target)
    /// Line goes: target â†’ horizontal â†’ vertical â†’ horizontal to label
    /// </summary>
    public static void DrawCalloutFromRight(SKCanvas canvas, string label, SKPoint labelPos, SKPoint target,
        SKColor color, float progress = 1f, float fontSize = 13f)
    {
        // For right-side callouts, we draw the line first, then the label
        // The geometry is mirrored

        float horizontalExtend = 20f;
        float verticalX = target.X + horizontalExtend;

        var point1 = target;
        var point2 = new SKPoint(verticalX, target.Y);
        var point3 = new SKPoint(verticalX, labelPos.Y - fontSize / 3);
        var point4 = new SKPoint(labelPos.X - 8, labelPos.Y - fontSize / 3);

        float seg1Len = Math.Abs(point2.X - point1.X);
        float seg2Len = Math.Abs(point3.Y - point2.Y);
        float seg3Len = Math.Abs(point4.X - point3.X);
        float totalLen = seg1Len + seg2Len + seg3Len;

        if (totalLen < 1f) return;

        progress = Math.Clamp(progress, 0f, 1f);

        float seg1Ratio = seg1Len / totalLen;
        float seg2Ratio = seg2Len / totalLen;

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = LineWeightThin,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Square
        };

        using var path = new SKPath();
        path.MoveTo(point1);

        SKPoint currentEnd = point1;

        if (progress <= seg1Ratio)
        {
            float segProgress = progress / seg1Ratio;
            currentEnd = Lerp(point1, point2, segProgress);
            path.LineTo(currentEnd);
        }
        else if (progress <= seg1Ratio + seg2Ratio)
        {
            path.LineTo(point2);
            float segProgress = (progress - seg1Ratio) / seg2Ratio;
            currentEnd = Lerp(point2, point3, segProgress);
            path.LineTo(currentEnd);
        }
        else
        {
            path.LineTo(point2);
            path.LineTo(point3);
            float segProgress = (progress - seg1Ratio - seg2Ratio) / (1f - seg1Ratio - seg2Ratio);
            currentEnd = Lerp(point3, point4, segProgress);
            path.LineTo(currentEnd);
        }

        canvas.DrawPath(path, paint);

        // Draw endpoint at target (start of line)
        if (progress > 0.05f)
        {
            DrawLeadLineEndpoint(canvas, point1, color);
        }

        // Draw label
        DrawText(canvas, label, labelPos, color, fontSize);
    }

    private static SKPoint Lerp(SKPoint a, SKPoint b, float t)
    {
        return new SKPoint(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t
        );
    }


    // â”€â”€â”€ SVG / Bitmap Drawing Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Transform state produced by <see cref="DrawSvgInBounds"/> and <see cref="DrawBitmapInBounds"/>.
    /// Callers can store these values for coordinate conversion (ViewBoxToScreen, etc.).
    /// </summary>
    public readonly struct SvgTransform
    {
        public float Scale { get; init; }
        public SKPoint Offset { get; init; }
        /// <summary>Source image width in viewBox units â€” used for mirror coordinate math.</summary>
        public float SourceWidth { get; init; }
        /// <summary>Scaled width in screen pixels â€” used by DeviceMapEditor for mirror math.</summary>
        public float ScaledWidth { get; init; }
    }

    /// <summary>
    /// Draws an SVG centred within <paramref name="bounds"/>, maintaining aspect ratio (95% fill).
    /// Optionally mirrors horizontally. Returns the computed transform for coordinate conversion.
    /// </summary>
    public static SvgTransform DrawSvgInBounds(SKCanvas canvas, SKSvg svg, SKRect bounds, bool mirror = false)
    {
        if (svg.Picture is null) return default;

        var svgBounds = svg.Picture.CullRect;
        if (svgBounds.Width <= 0 || svgBounds.Height <= 0) return default;

        float scaleX = bounds.Width / svgBounds.Width;
        float scaleY = bounds.Height / svgBounds.Height;
        float scale = Math.Min(scaleX, scaleY) * 0.95f;

        float scaledWidth = svgBounds.Width * scale;
        float scaledHeight = svgBounds.Height * scale;

        float offsetX = bounds.Left + (bounds.Width - scaledWidth) / 2 - svgBounds.Left * scale;
        float offsetY = bounds.Top + (bounds.Height - scaledHeight) / 2 - svgBounds.Top * scale;

        canvas.Save();
        canvas.Translate(offsetX, offsetY);

        if (mirror)
        {
            canvas.Translate(scaledWidth, 0);
            canvas.Scale(-scale, scale);
        }
        else
        {
            canvas.Scale(scale);
        }

        canvas.DrawPicture(svg.Picture);
        canvas.Restore();

        return new SvgTransform
        {
            Scale = scale,
            Offset = new SKPoint(offsetX, offsetY),
            SourceWidth = svgBounds.Width,
            ScaledWidth = scaledWidth
        };
    }

    /// <summary>
    /// Draws a bitmap centred within <paramref name="bounds"/>, maintaining aspect ratio (95% fill).
    /// Uses explicit viewBox dimensions for scaling (may differ from bitmap pixel size).
    /// Optionally mirrors horizontally. Returns the computed transform for coordinate conversion.
    /// </summary>
    public static SvgTransform DrawBitmapInBounds(SKCanvas canvas, SKBitmap bitmap, float viewBoxWidth, float viewBoxHeight, SKRect bounds, bool mirror = false)
    {
        if (viewBoxWidth <= 0 || viewBoxHeight <= 0) return default;

        float scaleX = bounds.Width / viewBoxWidth;
        float scaleY = bounds.Height / viewBoxHeight;
        float scale = Math.Min(scaleX, scaleY) * 0.95f;

        float scaledWidth = viewBoxWidth * scale;
        float scaledHeight = viewBoxHeight * scale;

        float offsetX = bounds.Left + (bounds.Width - scaledWidth) / 2;
        float offsetY = bounds.Top + (bounds.Height - scaledHeight) / 2;

        var destRect = new SKRect(offsetX, offsetY, offsetX + scaledWidth, offsetY + scaledHeight);

        canvas.Save();
        if (mirror)
            canvas.Scale(-1, 1, offsetX + scaledWidth / 2, offsetY + scaledHeight / 2);
        canvas.DrawBitmap(bitmap, destRect);
        canvas.Restore();

        return new SvgTransform
        {
            Scale = scale,
            Offset = new SKPoint(offsetX, offsetY),
            SourceWidth = viewBoxWidth,
            ScaledWidth = scaledWidth
        };
    }
}