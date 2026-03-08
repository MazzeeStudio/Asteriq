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
    /// Renders a single icon at the specified size with a thin dark outline.
    /// The outline makes the near-white logo visible on both light and dark backgrounds
    /// (File Explorer, pinned taskbar shortcuts, Start Menu).
    /// </summary>
    private static byte[] RenderIcon(string svgPath, int size)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size));
        var canvas = surface.Canvas;

        // Clear to transparent background
        canvas.Clear(SKColors.Transparent);

        // Load SVG
        using var svg = new SKSvg();
        using (var stream = File.OpenRead(svgPath))
        {
            svg.Load(stream);
        }

        if (svg.Picture is null)
            throw new InvalidOperationException($"Failed to load SVG: {svgPath}");

        // Scale to 87% to leave a 1-pixel margin for the outline without clipping.
        var bounds = svg.Picture.CullRect;
        float scale = Math.Min(size / bounds.Width, size / bounds.Height) * 0.87f;
        float offsetX = (size - bounds.Width * scale) / 2;
        float offsetY = (size - bounds.Height * scale) / 2;

        DrawOutlinedLogo(canvas, svg.Picture, offsetX, offsetY, scale);

        // Encode to PNG
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Draws the SVG with a 1-pixel dark outline then the original fill on top.
    /// Visible on both light and dark backgrounds without theme detection.
    /// </summary>
    internal static void DrawOutlinedLogo(SKCanvas canvas, SKPicture picture,
        float offsetX, float offsetY, float scale)
    {
        // Pass 1: dark dilated silhouette (the outline)
        // ColorMatrix zeroes every input channel and adds a fixed near-black offset,
        // preserving the source alpha so only the shape area is affected.
        using var outlineColorFilter = SKColorFilter.CreateColorMatrix(new float[]
        {
            0, 0, 0, 0, 0.10f,   // R = 0.10 (~#1A)
            0, 0, 0, 0, 0.10f,   // G = 0.10
            0, 0, 0, 0, 0.10f,   // B = 0.10
            0, 0, 0, 1, 0        // A = A_in
        });
        using var dilateFilter = SKImageFilter.CreateDilate(1, 1);
        using var outlinePaint = new SKPaint
        {
            ColorFilter = outlineColorFilter,
            ImageFilter  = dilateFilter
        };

        canvas.Save();
        canvas.Translate(offsetX, offsetY);
        canvas.Scale(scale);
        canvas.DrawPicture(picture, outlinePaint);
        canvas.Restore();

        // Pass 2: original near-white fill on top
        canvas.Save();
        canvas.Translate(offsetX, offsetY);
        canvas.Scale(scale);
        canvas.DrawPicture(picture);
        canvas.Restore();
    }
}
