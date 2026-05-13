using Asteriq.Models;
using Asteriq.Services;
using Microsoft.Win32;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI;

public static partial class FUIRenderer
{
    public static void DrawGlowingLine(SKCanvas canvas, SKPoint start, SKPoint end,
        SKColor color, float lineWeight = LineWeight, float glowRadius = GlowRadius)
    {
        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color.WithAlpha(80),
            StrokeWidth = lineWeight + 3f,
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(glowRadius, glowRadius)
        };
        canvas.DrawLine(start, end, glowPaint);

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = lineWeight,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Square
        };
        canvas.DrawLine(start, end, paint);
    }

    public static void DrawGlowingDot(SKCanvas canvas, SKPoint center, SKColor color,
        float radius = 4f, float glowRadius = GlowRadius)
    {
        using var glowPaint = CreateGlowPaint(color.WithAlpha(100), glowRadius);
        canvas.DrawCircle(center, radius + glowRadius / 2, glowPaint);

        using var paint = CreateFillPaint(color);
        canvas.DrawCircle(center, radius, paint);
    }



    public static void DrawScanLine(SKCanvas canvas, SKRect bounds, float progress,
        SKColor color, float thickness = 2f)
    {
        float y = bounds.Top + bounds.Height * progress;

        using var paint = CreateStrokePaint(color.WithAlpha((byte)(color.Alpha * 0.4f)), thickness);
        canvas.DrawLine(bounds.Left, y, bounds.Right, y, paint);

        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thickness + 4f,
            Color = color.WithAlpha((byte)(color.Alpha * 0.15f)),
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(6f, 6f)
        };
        canvas.DrawLine(bounds.Left, y, bounds.Right, y, glowPaint);
    }

    public static void DrawScanLineOverlay(SKCanvas canvas, SKRect bounds,
        float lineSpacing = 3f, byte alpha = 8)
    {
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(0, 0, 0, alpha),
            StrokeWidth = 1f
        };

        for (float y = bounds.Top; y < bounds.Bottom; y += lineSpacing)
        {
            canvas.DrawLine(bounds.Left, y, bounds.Right, y, paint);
        }
    }



    public static void DrawDotGrid(SKCanvas canvas, SKRect bounds, float spacing = 20f, SKColor? color = null)
    {
        var gridColor = color ?? FUIColors.Grid;

        using var paint = CreateFillPaint(gridColor);

        for (float x = bounds.Left; x <= bounds.Right; x += spacing)
        {
            for (float y = bounds.Top; y <= bounds.Bottom; y += spacing)
            {
                canvas.DrawCircle(x, y, 1f, paint);
            }
        }
    }

    public static void DrawLineGrid(SKCanvas canvas, SKRect bounds, float spacing = 40f, SKColor? color = null)
    {
        var gridColor = color ?? FUIColors.Grid;

        using var paint = CreateStrokePaint(gridColor, 0.5f);

        for (float x = bounds.Left; x <= bounds.Right; x += spacing)
        {
            canvas.DrawLine(x, bounds.Top, x, bounds.Bottom, paint);
        }

        for (float y = bounds.Top; y <= bounds.Bottom; y += spacing)
        {
            canvas.DrawLine(bounds.Left, y, bounds.Right, y, paint);
        }
    }



    public static void DrawDataBar(SKCanvas canvas, SKRect bounds, float value,
        SKColor fillColor, SKColor frameColor, bool horizontal = true)
    {
        using var framePaint = CreateStrokePaint(frameColor);
        canvas.DrawRect(bounds, framePaint);

        value = Math.Clamp(value, 0f, 1f);
        SKRect fillRect;

        if (horizontal)
        {
            fillRect = new SKRect(bounds.Left + 1, bounds.Top + 1,
                                  bounds.Left + 1 + (bounds.Width - 2) * value, bounds.Bottom - 1);
        }
        else
        {
            float fillHeight = (bounds.Height - 2) * value;
            fillRect = new SKRect(bounds.Left + 1, bounds.Bottom - 1 - fillHeight,
                                  bounds.Right - 1, bounds.Bottom - 1);
        }

        if (fillRect.Width > 0 && fillRect.Height > 0)
        {
            using var glowPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = fillColor.WithAlpha(60),
                ImageFilter = SKImageFilter.CreateBlur(4f, 4f)
            };
            canvas.DrawRect(fillRect, glowPaint);

            using var fillPaint = CreateFillPaint(fillColor);
            canvas.DrawRect(fillRect, fillPaint);
        }
    }



}