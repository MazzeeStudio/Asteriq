using SkiaSharp;

namespace Asteriq.UI;

/// <summary>
/// Renders a futuristic background with grid, ambient glows,
/// noise texture, scanlines, and vignette effects.
/// Inspired by GalaxiaWeb's FUI background system.
/// </summary>
public class FUIBackground : IDisposable
{
    // Cached bitmaps for static elements
    private SKBitmap? _gridBitmap;
    private SKBitmap? _vignetteBitmap;
    private SKBitmap? _noiseBitmap;
    private SKSize _cachedSize;

    private readonly Random _random = new();

    // Settings - intensity values 0-100 for UI, converted to 0-1 internally
    public bool EnableVignette { get; set; } = true;

    // Intensity controls (0-100 scale for UI)
    public int GridStrength { get; set; } = 50;        // 0 = off, 100 = max
    public int GlowIntensity { get; set; } = 40;       // 0 = off, 100 = max
    public int NoiseIntensity { get; set; } = 8;       // 0 = off, 100 = max (subtle by default)
    public int ScanlineIntensity { get; set; } = 0;    // 0 = off, 100 = max
    public int VignetteStrength { get; set; } = 50;    // 0 = off, 100 = max

    // Derived properties for rendering
    private float GridOpacity => GridStrength / 100f;
    private float GlowOpacity => GlowIntensity / 100f;
    private float NoiseOpacity => NoiseIntensity / 100f;
    private float ScanlineOpacity => ScanlineIntensity / 100f;
    private float VignetteOpacity => VignetteStrength / 100f;

    public FUIBackground()
    {
    }

    /// <summary>
    /// Update animation state (currently no-op, kept for API compatibility)
    /// </summary>
    public void Update(float deltaTime)
    {
        // No animation needed currently
    }

    /// <summary>
    /// Render the background to the canvas
    /// </summary>
    public void Render(SKCanvas canvas, SKRect bounds)
    {
        // Check if we need to regenerate cached elements
        var size = new SKSize(bounds.Width, bounds.Height);
        if (_cachedSize != size)
        {
            RegenerateCaches(size);
            _cachedSize = size;
        }

        // Layer 1: Grid (intensity-based)
        if (GridStrength > 0 && _gridBitmap != null)
        {
            using var gridPaint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * GridOpacity)) };
            canvas.DrawBitmap(_gridBitmap, bounds.Left, bounds.Top, gridPaint);
        }

        // Layer 2: Ambient glows (intensity-based)
        if (GlowIntensity > 0)
        {
            DrawAmbientGlows(canvas, bounds);
        }

        // Layer 3: Noise/grain (intensity-based)
        if (NoiseIntensity > 0 && _noiseBitmap != null)
        {
            // Scale noise opacity - max around 15% for subtle effect
            byte alpha = (byte)(255 * NoiseOpacity * 0.15f);
            using var noisePaint = new SKPaint
            {
                Color = SKColors.White.WithAlpha(alpha),
                BlendMode = SKBlendMode.Overlay
            };
            canvas.DrawBitmap(_noiseBitmap, bounds.Left, bounds.Top, noisePaint);
        }

        // Layer 4: Scanlines (intensity-based)
        if (ScanlineIntensity > 0)
        {
            DrawScanlines(canvas, bounds);
        }

        // Layer 5: Vignette
        if (EnableVignette && VignetteStrength > 0 && _vignetteBitmap != null)
        {
            using var vignettePaint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * VignetteOpacity)) };
            canvas.DrawBitmap(_vignetteBitmap, bounds.Left, bounds.Top, vignettePaint);
        }
    }

    private void RegenerateCaches(SKSize size)
    {
        int width = (int)size.Width;
        int height = (int)size.Height;

        if (width <= 0 || height <= 0) return;

        // Regenerate grid
        _gridBitmap?.Dispose();
        _gridBitmap = GenerateGridBitmap(width, height);

        // Regenerate vignette
        _vignetteBitmap?.Dispose();
        _vignetteBitmap = GenerateVignetteBitmap(width, height);

        // Regenerate noise
        _noiseBitmap?.Dispose();
        _noiseBitmap = GenerateNoiseBitmap(width, height);
    }

    private SKBitmap GenerateGridBitmap(int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        // Determine cell sizes based on aspect ratio
        float aspect = (float)width / height;
        int majorCell = aspect > 2.3f ? 140 : (aspect < 1.5f ? 100 : 120);
        int minorCell = majorCell / 4;

        // Get theme-aware grid colors
        var minorColor = FUIColors.Grid.WithAlpha(40);
        var majorColor = FUIColors.Grid.WithAlpha(80);

        using var minorPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = minorColor,
            StrokeWidth = 1f,
            IsAntialias = false
        };

        using var majorPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = majorColor,
            StrokeWidth = 1f,
            IsAntialias = false
        };

        // Draw minor grid lines
        for (int x = 0; x <= width; x += minorCell)
        {
            if (x % majorCell != 0)
            {
                canvas.DrawLine(x, 0, x, height, minorPaint);
            }
        }
        for (int y = 0; y <= height; y += minorCell)
        {
            if (y % majorCell != 0)
            {
                canvas.DrawLine(0, y, width, y, minorPaint);
            }
        }

        // Draw major grid lines
        for (int x = 0; x <= width; x += majorCell)
        {
            canvas.DrawLine(x, 0, x, height, majorPaint);
        }
        for (int y = 0; y <= height; y += majorCell)
        {
            canvas.DrawLine(0, y, width, y, majorPaint);
        }

        return bitmap;
    }

    private SKBitmap GenerateVignetteBitmap(int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        float centerX = width / 2f;
        float centerY = height / 2f;
        float radius = MathF.Max(width, height) * 0.72f;

        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(centerX, centerY),
            radius,
            new[] { SKColors.Transparent, SKColors.Black.WithAlpha(140) },
            new[] { 0.55f, 1.0f },
            SKShaderTileMode.Clamp
        );

        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(0, 0, width, height, paint);

        return bitmap;
    }

    private SKBitmap GenerateNoiseBitmap(int width, int height)
    {
        // Generate at quarter resolution for performance
        int noiseWidth = width / 4;
        int noiseHeight = height / 4;

        var bitmap = new SKBitmap(noiseWidth, noiseHeight, SKColorType.Rgba8888, SKAlphaType.Premul);

        // Generate noise using pixel manipulation
        for (int y = 0; y < noiseHeight; y++)
        {
            for (int x = 0; x < noiseWidth; x++)
            {
                byte noise = (byte)_random.Next(0, 256);
                bitmap.SetPixel(x, y, new SKColor(noise, noise, noise, noise));
            }
        }

        // Scale up for use
        var scaledBitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(scaledBitmap);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.Low };
        canvas.DrawBitmap(bitmap, new SKRect(0, 0, width, height), paint);
        bitmap.Dispose();

        return scaledBitmap;
    }

    private void DrawAmbientGlows(SKCanvas canvas, SKRect bounds)
    {
        var glowColor = FUIColors.Glow;
        float intensityScale = GlowOpacity;  // 0-1 based on GlowIntensity setting

        // Create several ambient glow spots
        // Center glow is largest and brightest
        float centerRadius = MathF.Max(bounds.Width, bounds.Height) * 0.5f;
        var glowSpots = new[]
        {
            (x: bounds.Left + bounds.Width * 0.33f, y: bounds.Top + bounds.Height * 0.8f, r: 120f, opacity: 0.35f),
            (x: bounds.Left + bounds.Width * 0.70f, y: bounds.Top + bounds.Height * 0.15f, r: 80f, opacity: 0.30f),
            (x: bounds.Left + bounds.Width * 0.5f, y: bounds.Top + bounds.Height * 0.5f, r: centerRadius, opacity: 0.45f),  // Center - large and bright
        };

        foreach (var spot in glowSpots)
        {
            // Apply intensity scale to opacity
            byte alpha = (byte)(100 * spot.opacity * intensityScale);
            using var shader = SKShader.CreateRadialGradient(
                new SKPoint(spot.x, spot.y),
                spot.r,
                new[] { glowColor.WithAlpha(alpha), SKColors.Transparent },
                new[] { 0f, 1f },
                SKShaderTileMode.Clamp
            );

            using var paint = new SKPaint { Shader = shader };
            canvas.DrawCircle(spot.x, spot.y, spot.r, paint);
        }
    }

    private void DrawScanlines(SKCanvas canvas, SKRect bounds)
    {
        // Scale alpha based on intensity (max ~20 for subtle effect)
        byte alpha = (byte)(20 * ScanlineOpacity);
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = SKColors.White.WithAlpha(alpha),
            StrokeWidth = 1f,
            BlendMode = SKBlendMode.Overlay
        };

        // Horizontal scanlines every 2-3 pixels based on intensity
        int spacing = ScanlineIntensity > 50 ? 2 : 3;
        for (float y = bounds.Top; y < bounds.Bottom; y += spacing)
        {
            canvas.DrawLine(bounds.Left, y, bounds.Right, y, paint);
        }
    }

    public void Dispose()
    {
        _gridBitmap?.Dispose();
        _vignetteBitmap?.Dispose();
        _noiseBitmap?.Dispose();
    }
}
