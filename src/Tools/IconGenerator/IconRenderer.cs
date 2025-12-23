using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.Tools.IconGenerator;

/// <summary>
/// Renders application icons from SVG.
/// </summary>
public static class IconRenderer
{
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

        // Encode to PNG
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
