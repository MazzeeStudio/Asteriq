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
    private static readonly SKColor BackgroundColor = new SKColor(0x0A, 0x0E, 0x14); // FUIColors.Background1
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

        // Clear to dark background
        canvas.Clear(BackgroundColor);

        // Load SVG
        var svg = new SKSvg();
        using (var stream = File.OpenRead(svgPath))
        {
            svg.Load(stream);
        }

        if (svg.Picture is null)
            throw new InvalidOperationException($"Failed to load SVG: {svgPath}");

        // Calculate layout - reserve space for text at bottom
        float textSpace = size >= 48 ? size * 0.25f : 0;
        float throttleSpace = size - textSpace;

        // Calculate SVG scaling and positioning to fit in available space
        var bounds = svg.Picture.CullRect;
        float scale = Math.Min(throttleSpace / bounds.Width, throttleSpace / bounds.Height) * 0.85f; // 85% to add some padding
        float offsetX = (size - bounds.Width * scale) / 2;
        float offsetY = (throttleSpace - bounds.Height * scale) / 2;

        // Render SVG with gradients preserved
        canvas.Save();
        canvas.Translate(offsetX, offsetY);
        canvas.Scale(scale);

        // Draw the SVG as-is to preserve gradient detail
        // The throttle.svg already has beautiful grayscale gradients
        canvas.DrawPicture(svg.Picture);
        canvas.Restore();

        // Add text/symbol at bottom (only for larger sizes)
        if (size >= 48)
        {
            // Use star symbol for smaller sizes, full text for 256x256
            string label = size >= 128 ? "ASTERIQ" : "★";
            DrawLabel(canvas, label, size, throttleSpace);
        }

        // Encode to PNG
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Draws the label (text or symbol) at the bottom of the icon.
    /// </summary>
    private static void DrawLabel(SKCanvas canvas, string label, int iconSize, float yOffset)
    {
        // Calculate font size based on icon size
        float fontSize = label == "★"
            ? iconSize * 0.15f    // Star symbol: 15% of icon size
            : iconSize switch
            {
                256 => 22f,
                128 => 12f,
                _ => 7f
            };

        // Try Carbon font first, fallback to Consolas
        var typeface = SKTypeface.FromFamilyName("Carbon",
            SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright)
            ?? SKTypeface.FromFamilyName("Consolas");

        using var paint = new SKPaint
        {
            Color = ForegroundColor,
            TextSize = fontSize,
            IsAntialias = true,
            Typeface = typeface,
            TextAlign = SKTextAlign.Center
        };

        // Position text at bottom with padding
        float x = iconSize / 2f; // Center horizontally
        float y = yOffset + (iconSize - yOffset) / 2 + fontSize / 3; // Center in text area

        canvas.DrawText(label, x, y, paint);
    }
}
