using Asteriq.Models;
using Asteriq.Services;
using Microsoft.Win32;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI;

public static partial class FUIRenderer
{
    public static SKPath CreateFrame(SKRect bounds, float cornerSize = CornerRadius)
    {
        var path = new SKPath();

        if (CurrentCornerStyle == CornerStyle.Chamfered && cornerSize > 0)
        {
            float c = cornerSize;
            path.MoveTo(bounds.Left, bounds.Top);
            path.LineTo(bounds.Right, bounds.Top);
            path.LineTo(bounds.Right, bounds.Bottom - c);
            path.LineTo(bounds.Right - c, bounds.Bottom);
            path.LineTo(bounds.Left, bounds.Bottom);
            path.Close();
        }
        else if (CurrentCornerStyle == CornerStyle.Hard || cornerSize <= 0)
        {
            path.AddRect(bounds);
        }
        else
        {
            path.AddRoundRect(bounds, cornerSize, cornerSize);
        }

        return path;
    }

    public static void DrawLCornerFrame(SKCanvas canvas, SKRect bounds, SKColor color,
        float cornerLength = 30f, float chamfer = ChamferSize, float lineWeight = LineWeight, bool withGlow = false)
    {
        if (withGlow)
        {
            using var glowPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = color.WithAlpha(50),
                StrokeWidth = lineWeight + 4f,
                IsAntialias = true,
                ImageFilter = SKImageFilter.CreateBlur(GlowRadius, GlowRadius)
            };
            DrawLCornerPaths(canvas, bounds, cornerLength, chamfer, glowPaint);
        }

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = lineWeight,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Square
        };
        DrawLCornerPaths(canvas, bounds, cornerLength, chamfer, paint);
    }

    private static void DrawLCornerPaths(SKCanvas canvas, SKRect bounds, float cornerLength, float chamfer, SKPaint paint)
    {
        canvas.DrawLine(bounds.Left, bounds.Top + cornerLength, bounds.Left, bounds.Top, paint);
        canvas.DrawLine(bounds.Left, bounds.Top, bounds.Left + cornerLength, bounds.Top, paint);

        float c = chamfer;
        using var path = new SKPath();
        path.MoveTo(bounds.Right - cornerLength, bounds.Bottom);
        path.LineTo(bounds.Right - c, bounds.Bottom);
        path.LineTo(bounds.Right, bounds.Bottom - c);
        path.LineTo(bounds.Right, bounds.Bottom - cornerLength);
        canvas.DrawPath(path, paint);
    }

    public static void DrawFrame(SKCanvas canvas, SKRect bounds, SKColor color,
        float cornerRadius = CornerRadius, float lineWeight = LineWeight, bool withGlow = false)
    {
        using var path = CreateFrame(bounds, cornerRadius);

        if (withGlow)
        {
            using var glowPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = color.WithAlpha(60),
                StrokeWidth = lineWeight + 4f,
                IsAntialias = true,
                ImageFilter = SKImageFilter.CreateBlur(GlowRadius, GlowRadius)
            };
            canvas.DrawPath(path, glowPaint);
        }

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = lineWeight,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };
        canvas.DrawPath(path, paint);
    }

    public static void FillFrame(SKCanvas canvas, SKRect bounds, SKColor fillColor,
        float cornerRadius = CornerRadius)
    {
        using var path = CreateFrame(bounds, cornerRadius);

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(bounds.MidX, bounds.Top),
                new SKPoint(bounds.MidX, bounds.Bottom),
                new[] { fillColor.WithAlpha((byte)Math.Min(255, fillColor.Alpha * 1.2f)), fillColor },
                SKShaderTileMode.Clamp)
        };
        canvas.DrawPath(path, paint);
    }

    public static void DrawAngularFrame(SKCanvas canvas, SKRect bounds, SKColor color,
        float cornerRadius = CornerRadius, float lineWeight = LineWeight, bool withGlow = false)
        => DrawFrame(canvas, bounds, color, cornerRadius, lineWeight, withGlow);

    public static void FillAngularFrame(SKCanvas canvas, SKRect bounds, SKColor fillColor,
        float cornerRadius = CornerRadius)
        => FillFrame(canvas, bounds, fillColor, cornerRadius);



    public static SKPaint CreateGlowPaint(SKColor color, float radius = GlowRadius)
    {
        return new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = color,
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(radius, radius)
        };
    }

    /// <summary>Creates a standard antialiased fill paint. Caller is responsible for disposal.</summary>
    public static SKPaint CreateFillPaint(SKColor color)
        => new SKPaint { Style = SKPaintStyle.Fill, Color = color, IsAntialias = true };

    /// <summary>Creates a standard antialiased stroke paint. Caller is responsible for disposal.</summary>
    public static SKPaint CreateStrokePaint(SKColor color, float strokeWidth = 1f)
        => new SKPaint { Style = SKPaintStyle.Stroke, Color = color, StrokeWidth = strokeWidth, IsAntialias = true };

    /// <summary>
    /// Draws a filled rounded rectangle with a stroked border in a single call,
    /// replacing the common 6-line fill+stroke sequence used throughout the codebase.
    /// </summary>
    public static void DrawRoundedPanel(SKCanvas canvas, SKRect bounds, SKColor fillColor, SKColor borderColor,
        float radius = 3f, float strokeWidth = 1f)
    {
        using var bgPaint = CreateFillPaint(fillColor);
        canvas.DrawRoundRect(bounds, radius, radius, bgPaint);
        using var borderPaint = CreateStrokePaint(borderColor, strokeWidth);
        canvas.DrawRoundRect(bounds, radius, radius, borderPaint);
    }

}