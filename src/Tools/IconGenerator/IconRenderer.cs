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
    // Drake Active colour (#FF8020) — matches FUIColors.Active for the Drake theme.
    // Visible on both light and dark backgrounds; consistent with the runtime Form.Icon.
    private static readonly SKColor s_iconColor = new(0xFF, 0x80, 0x20);

    /// <summary>
    /// Generates all icon sizes from the specified SVG file.
    /// </summary>
    public static List<(int size, byte[] pngData)> GenerateAllSizes(string svgPath)
    {
        if (!File.Exists(svgPath))
            throw new FileNotFoundException($"SVG file not found: {svgPath}");

        int[] sizes = { 16, 24, 32, 48, 256 };
        var results = new List<(int, byte[])>();

        foreach (int size in sizes)
            results.Add((size, RenderIcon(svgPath, size)));

        return results;
    }

    private static byte[] RenderIcon(string svgPath, int size)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        using var svg = new SKSvg();
        using (var stream = File.OpenRead(svgPath))
            svg.Load(stream);

        if (svg.Picture is null)
            throw new InvalidOperationException($"Failed to load SVG: {svgPath}");

        var bounds = svg.Picture.CullRect;
        float scale = Math.Min(size / bounds.Width, size / bounds.Height) * 0.90f;
        float offsetX = (size - bounds.Width * scale) / 2;
        float offsetY = (size - bounds.Height * scale) / 2;

        // Tint the near-white SVG to the brand colour via SaveLayer.
        // DrawPicture(picture, paint) does not reliably apply paint filters in SkiaSharp.
        float r = s_iconColor.Red   / 255f;
        float g = s_iconColor.Green / 255f;
        float b = s_iconColor.Blue  / 255f;

        using var colorFilter = SKColorFilter.CreateColorMatrix(new float[]
        {
            r, 0, 0, 0, 0,
            0, g, 0, 0, 0,
            0, 0, b, 0, 0,
            0, 0, 0, 1, 0
        });
        using var tintPaint = new SKPaint { ColorFilter = colorFilter };

        canvas.SaveLayer(tintPaint);
        canvas.Save();
        canvas.Translate(offsetX, offsetY);
        canvas.Scale(scale);
        canvas.DrawPicture(svg.Picture);
        canvas.Restore();
        canvas.Restore();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
