using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.Tools.IconGenerator;

/// <summary>
/// Renders application icons from SVG with text label.
/// </summary>
public static class IconRenderer
{
    // No background color - use transparent
    private static readonly SKColor ForegroundColor = SKColors.White;

    /// <summary>
    /// Generates all icon sizes from the specified SVG file.
    /// </summary>
    /// <param name="svgPath">Path to the SVG file</param>
    /// <returns>List of (size, pngData) tuples</returns>
    public static List<(int size, byte[] pngData)> GenerateAllSizes(string svgPath)
    {
        if (!File.Exists(svgPath))
            throw new FileNotFoundException($"SVG file not found: {svgPath}");

        int[] sizes = { 16, 24, 32, 48, 256 };
        var results = new List<(int, byte[])>();

        foreach (int size in sizes)
        {
            byte[] pngData = RenderIcon(svgPath, size);
            results.Add((size, pngData));
        }

        return results;
    }

    /// <summary>
    /// Renders a single icon at the specified size.
    /// </summary>
    private static byte[] RenderIcon(string svgPath, int size)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size));
        var canvas = surface.Canvas;

        // Clear to transparent background
        canvas.Clear(SKColors.Transparent);

        // Load SVG
        var svg = new SKSvg();
        using (var stream = File.OpenRead(svgPath))
        {
            svg.Load(stream);
        }

        if (svg.Picture is null)
            throw new InvalidOperationException($"Failed to load SVG: {svgPath}");

        // Calculate SVG scaling - use 90% of canvas for maximum size
        var bounds = svg.Picture.CullRect;
        float scale = Math.Min(size / bounds.Width, size / bounds.Height) * 0.90f; // 90% to add small padding
        float offsetX = (size - bounds.Width * scale) / 2;
        float offsetY = (size - bounds.Height * scale) / 2;

        // Render SVG with gradients preserved
        canvas.Save();
        canvas.Translate(offsetX, offsetY);
        canvas.Scale(scale);

        // Draw the SVG as-is to preserve gradient detail
        // The throttle.svg already has beautiful grayscale gradients
        canvas.DrawPicture(svg.Picture);
        canvas.Restore();

        // Add 8-pointed compass star in top-left corner
        DrawCompassStar(canvas, size);

        // Encode to PNG
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Draws an 8-pointed compass star in the top-left corner.
    /// </summary>
    private static void DrawCompassStar(SKCanvas canvas, int iconSize)
    {
        // Star size relative to icon size
        float starSize = iconSize switch
        {
            >= 256 => iconSize * 0.18f,  // 18% of icon size for large icons
            >= 48 => iconSize * 0.22f,   // 22% for medium (more visible)
            _ => iconSize * 0.25f        // 25% for small (maximum visibility)
        };

        // Position in top-left with padding
        float padding = iconSize * 0.08f; // 8% padding from edges
        float centerX = padding + starSize / 2;
        float centerY = padding + starSize / 2;

        // Draw 8-pointed star with filled triangular points
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ForegroundColor,
            IsAntialias = true
        };

        float outerRadius = starSize / 2;
        float innerRadius = outerRadius * 0.3f; // Inner circle is 30% of outer radius
        float pointWidth = outerRadius * 0.25f; // Width of each point at the base

        // Draw 8 points at 45° intervals
        for (int i = 0; i < 8; i++)
        {
            float angle = (float)(i * Math.PI / 4); // 45° in radians
            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);

            // Calculate perpendicular direction for point width
            float perpCos = (float)Math.Cos(angle + Math.PI / 2);
            float perpSin = (float)Math.Sin(angle + Math.PI / 2);

            // Create a triangular point
            using var path = new SKPath();

            // Start at the tip of the point
            path.MoveTo(
                centerX + outerRadius * cos,
                centerY + outerRadius * sin);

            // Left base corner
            path.LineTo(
                centerX + innerRadius * cos + pointWidth * perpCos,
                centerY + innerRadius * sin + pointWidth * perpSin);

            // Right base corner
            path.LineTo(
                centerX + innerRadius * cos - pointWidth * perpCos,
                centerY + innerRadius * sin - pointWidth * perpSin);

            path.Close();
            canvas.DrawPath(path, paint);
        }

        // For larger icons, add a subtle glow effect
        if (iconSize >= 128)
        {
            using var glowPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = ForegroundColor.WithAlpha(40),
                IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3f)
            };

            // Draw glow layer slightly larger
            for (int i = 0; i < 8; i++)
            {
                float angle = (float)(i * Math.PI / 4);
                float cos = (float)Math.Cos(angle);
                float sin = (float)Math.Sin(angle);
                float perpCos = (float)Math.Cos(angle + Math.PI / 2);
                float perpSin = (float)Math.Sin(angle + Math.PI / 2);

                using var path = new SKPath();
                path.MoveTo(
                    centerX + outerRadius * cos,
                    centerY + outerRadius * sin);
                path.LineTo(
                    centerX + innerRadius * cos + pointWidth * perpCos,
                    centerY + innerRadius * sin + pointWidth * perpSin);
                path.LineTo(
                    centerX + innerRadius * cos - pointWidth * perpCos,
                    centerY + innerRadius * sin - pointWidth * perpSin);
                path.Close();
                canvas.DrawPath(path, glowPaint);
            }
        }
    }
}
