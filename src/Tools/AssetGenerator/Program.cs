using SkiaSharp;
using Svg.Skia;

/// <summary>
/// Generates all required MSIX Store icon asset PNGs from an SVG source.
///
/// Usage: AssetGenerator &lt;svg-path&gt; &lt;output-dir&gt;
///
/// Produces:
///   Square44x44Logo.png    — 44×44   (taskbar, title bar)
///   Square150x150Logo.png  — 150×150 (Start menu tile)
///   Wide310x150Logo.png    — 310×150 (wide Start menu tile)
///   Square310x310Logo.png  — 310×310 (large Start menu tile)
///   StoreLogo.png          — 50×50   (Store listing badge)
///
/// All square assets render the logo centred on the app's dark background.
/// Wide assets place the logo left-centred with breathing room.
/// Replace with branded assets once the final logo is ready.
/// </summary>

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: AssetGenerator <svg-path> <output-dir>");
    return 1;
}

string svgPath   = args[0];
string outputDir = args[1];

if (!File.Exists(svgPath))
{
    Console.Error.WriteLine($"SVG not found: {svgPath}");
    return 1;
}

Directory.CreateDirectory(outputDir);

// App background colour — matches FUIColors.Background0 (#06080A)
var background = new SKColor(6, 8, 10);

// Load SVG once
var svg = new SKSvg();
using (var stream = File.OpenRead(svgPath))
    svg.Load(stream);

if (svg.Picture is null)
{
    Console.Error.WriteLine("Failed to parse SVG.");
    return 1;
}

var svgBounds = svg.Picture.CullRect;

// Square assets
var squareSizes = new[] { (44, "Square44x44Logo"), (150, "Square150x150Logo"), (310, "Square310x310Logo"), (50, "StoreLogo") };
foreach (var (size, name) in squareSizes)
{
    var path = Path.Combine(outputDir, $"{name}.png");
    GenerateSquare(svg.Picture, svgBounds, size, background, path);
    Console.WriteLine($"  {name}.png  ({size}×{size})");
}

// Wide asset — 310×150
{
    var path = Path.Combine(outputDir, "Wide310x150Logo.png");
    GenerateWide(svg.Picture, svgBounds, 310, 150, background, path);
    Console.WriteLine($"  Wide310x150Logo.png  (310×150)");
}

Console.WriteLine($"\nAll assets written to: {outputDir}");
return 0;

// ── Helpers ──────────────────────────────────────────────────────────────────

static void GenerateSquare(SKPicture picture, SKRect svgBounds, int size, SKColor bg, string outputPath)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size));
    var canvas = surface.Canvas;

    canvas.Clear(bg);

    // Fit logo to 80% of canvas, centred
    float padding = size * 0.10f;
    float available = size - padding * 2;
    float scale = Math.Min(available / svgBounds.Width, available / svgBounds.Height);
    float offsetX = (size - svgBounds.Width  * scale) / 2f;
    float offsetY = (size - svgBounds.Height * scale) / 2f;

    canvas.Save();
    canvas.Translate(offsetX, offsetY);
    canvas.Scale(scale);
    canvas.DrawPicture(picture);
    canvas.Restore();

    SavePng(surface, outputPath);
}

static void GenerateWide(SKPicture picture, SKRect svgBounds, int width, int height, SKColor bg, string outputPath)
{
    using var surface = SKSurface.Create(new SKImageInfo(width, height));
    var canvas = surface.Canvas;

    canvas.Clear(bg);

    // Logo scaled to fit height with padding, horizontally centred
    float paddingV = height * 0.12f;
    float available = height - paddingV * 2;
    float scale = Math.Min(available / svgBounds.Width, available / svgBounds.Height);
    float offsetX = (width  - svgBounds.Width  * scale) / 2f;
    float offsetY = (height - svgBounds.Height * scale) / 2f;

    canvas.Save();
    canvas.Translate(offsetX, offsetY);
    canvas.Scale(scale);
    canvas.DrawPicture(picture);
    canvas.Restore();

    SavePng(surface, outputPath);
}

static void SavePng(SKSurface surface, string path)
{
    using var image = surface.Snapshot();
    using var data  = image.Encode(SKEncodedImageFormat.Png, 100);
    using var fs    = File.OpenWrite(path);
    data.SaveTo(fs);
}
