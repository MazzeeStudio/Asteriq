using System;
using System.IO;
using System.Linq;

namespace Asteriq.Tools.IconGenerator;

/// <summary>
/// Command-line tool to generate Windows .ico files from SVG graphics.
/// </summary>
public class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: IconGenerator <output.ico> [svg-path]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Arguments:");
            Console.Error.WriteLine("  output.ico  Path to the output ICO file");
            Console.Error.WriteLine("  svg-path    (Optional) Path to the SVG source file");
            Console.Error.WriteLine();
            Console.Error.WriteLine("If svg-path is not provided, defaults to throttle.svg in Images/Devices/");
            return 1;
        }

        string outputPath = args[0];
        string svgPath;

        if (args.Length > 1)
        {
            svgPath = args[1];
        }
        else
        {
            // Default to throttle.svg relative to current directory
            svgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Devices", "throttle.svg");
        }

        try
        {
            Console.WriteLine($"Asteriq Icon Generator");
            Console.WriteLine($"=====================");
            Console.WriteLine($"SVG source: {svgPath}");
            Console.WriteLine($"Output:     {outputPath}");
            Console.WriteLine();

            if (!File.Exists(svgPath))
            {
                Console.Error.WriteLine($"ERROR: SVG file not found: {svgPath}");
                return 1;
            }

            Console.WriteLine("Generating icon sizes...");
            var images = IconRenderer.GenerateAllSizes(svgPath);
            Console.WriteLine($"  Generated {images.Count} sizes: " +
                string.Join(", ", images.Select(i => $"{i.size}×{i.size}")));

            Console.WriteLine("Encoding ICO file...");
            IcoEncoder.EncodeToIco(images, outputPath);

            var fileInfo = new FileInfo(outputPath);
            Console.WriteLine($"  Wrote {fileInfo.Length:N0} bytes");
            Console.WriteLine();
            Console.WriteLine($"✓ Successfully generated {outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            if (ex.InnerException is not null)
            {
                Console.Error.WriteLine($"  {ex.InnerException.Message}");
            }
            return 1;
        }
    }
}
