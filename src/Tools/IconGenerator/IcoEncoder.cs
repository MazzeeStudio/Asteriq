using System;
using System.Collections.Generic;
using System.IO;

namespace Asteriq.Tools.IconGenerator;

/// <summary>
/// Encodes multiple PNG images into a single Windows ICO file.
/// Based on ICO format specification.
/// </summary>
public static class IcoEncoder
{
    /// <summary>
    /// Encodes a list of PNG images into a Windows ICO file.
    /// </summary>
    /// <param name="images">List of (size, pngData) tuples for each icon size</param>
    /// <param name="outputPath">Path to write the .ico file</param>
    public static void EncodeToIco(List<(int size, byte[] pngData)> images, string outputPath)
    {
        if (images is null || images.Count == 0)
            throw new ArgumentException("At least one image is required", nameof(images));

        using var stream = File.Create(outputPath);
        using var writer = new BinaryWriter(stream);

        // ICO Header (6 bytes total)
        writer.Write((ushort)0);              // Reserved (must be 0)
        writer.Write((ushort)1);              // Type: 1 = ICO, 2 = CUR
        writer.Write((ushort)images.Count);   // Number of images in file

        // Calculate offset to first image data (after all directory entries)
        // Header = 6 bytes, each directory entry = 16 bytes
        int dataOffset = 6 + (16 * images.Count);

        // Write ICONDIRENTRY for each image (16 bytes each)
        foreach (var (size, pngData) in images)
        {
            // Width and height (0 = 256 per ICO spec)
            writer.Write((byte)(size == 256 ? 0 : size));   // Width
            writer.Write((byte)(size == 256 ? 0 : size));   // Height
            writer.Write((byte)0);                          // Color palette (0 = no palette)
            writer.Write((byte)0);                          // Reserved (must be 0)
            writer.Write((ushort)1);                        // Color planes (should be 1)
            writer.Write((ushort)32);                       // Bits per pixel (32 = RGBA)
            writer.Write((uint)pngData.Length);             // Size of image data in bytes
            writer.Write((uint)dataOffset);                 // Offset to image data

            dataOffset += pngData.Length;
        }

        // Write PNG data for each image
        foreach (var (_, pngData) in images)
        {
            writer.Write(pngData);
        }
    }
}
