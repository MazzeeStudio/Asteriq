using SkiaSharp;

namespace Asteriq.UI;

/// <summary>
/// Renders a futuristic background with grid, ambient glows, floating panels,
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

    // Floating panels state
    private readonly List<FloatingPanel> _panels = new();
    private readonly Random _random = new();

    // Animation time
    private float _time;

    // Settings
    public bool EnableGrid { get; set; } = true;
    public bool EnableAmbientGlow { get; set; } = true;
    public bool EnablePanels { get; set; } = true;
    public bool EnableNoise { get; set; } = true;
    public bool EnableScanlines { get; set; } = false;
    public bool EnableVignette { get; set; } = true;

    public int PanelCount { get; set; } = 8;
    public float GridOpacity { get; set; } = 0.5f;
    public float NoiseOpacity { get; set; } = 0.08f;
    public float VignetteStrength { get; set; } = 0.5f;

    /// <summary>
    /// Represents a floating panel in the background
    /// </summary>
    private class FloatingPanel
    {
        public float X, Y, Width, Height;
        public float DriftSpeedX, DriftSpeedY;
        public float DriftAmplitudeX, DriftAmplitudeY;
        public float Phase;
        public float BaseX, BaseY;
    }

    public FUIBackground()
    {
    }

    /// <summary>
    /// Update animation state
    /// </summary>
    public void Update(float deltaTime)
    {
        _time += deltaTime;
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
            RegeneratePanels(bounds);
            _cachedSize = size;
        }

        // Layer 1: Grid
        if (EnableGrid && _gridBitmap != null)
        {
            using var gridPaint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * GridOpacity)) };
            canvas.DrawBitmap(_gridBitmap, bounds.Left, bounds.Top, gridPaint);
        }

        // Layer 2: Ambient glows
        if (EnableAmbientGlow)
        {
            DrawAmbientGlows(canvas, bounds);
        }

        // Layer 3: Floating panels
        if (EnablePanels)
        {
            DrawFloatingPanels(canvas, bounds);
        }

        // Layer 4: Noise/grain
        if (EnableNoise && _noiseBitmap != null)
        {
            using var noisePaint = new SKPaint
            {
                Color = SKColors.White.WithAlpha((byte)(255 * NoiseOpacity)),
                BlendMode = SKBlendMode.Overlay
            };
            canvas.DrawBitmap(_noiseBitmap, bounds.Left, bounds.Top, noisePaint);
        }

        // Layer 5: Scanlines
        if (EnableScanlines)
        {
            DrawScanlines(canvas, bounds);
        }

        // Layer 6: Vignette
        if (EnableVignette && _vignetteBitmap != null)
        {
            using var vignettePaint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * VignetteStrength)) };
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

        var bitmap = new SKBitmap(noiseWidth, noiseHeight);

        // Generate noise using pixel manipulation
        var pixels = new uint[noiseWidth * noiseHeight];
        for (int i = 0; i < pixels.Length; i++)
        {
            byte noise = (byte)_random.Next(0, 256);
            // Grayscale noise with alpha
            pixels[i] = (uint)((noise << 24) | (noise << 16) | (noise << 8) | noise);
        }

        unsafe
        {
            fixed (uint* ptr = pixels)
            {
                bitmap.SetPixels((IntPtr)ptr);
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

    private void RegeneratePanels(SKRect bounds)
    {
        _panels.Clear();

        for (int i = 0; i < PanelCount; i++)
        {
            float maxW = 400;
            float maxH = 250;
            float w = 140 + (float)_random.NextDouble() * (maxW - 140);
            float h = 80 + (float)_random.NextDouble() * (maxH - 80);
            float x = bounds.Left + (float)_random.NextDouble() * (bounds.Width - w);
            float y = bounds.Top + (float)_random.NextDouble() * (bounds.Height - h);

            _panels.Add(new FloatingPanel
            {
                X = x,
                Y = y,
                BaseX = x,
                BaseY = y,
                Width = w,
                Height = h,
                DriftSpeedX = 0.1f + (float)_random.NextDouble() * 0.2f,
                DriftSpeedY = 0.08f + (float)_random.NextDouble() * 0.15f,
                DriftAmplitudeX = 15 + (float)_random.NextDouble() * 25,
                DriftAmplitudeY = 10 + (float)_random.NextDouble() * 20,
                Phase = (float)_random.NextDouble() * MathF.PI * 2
            });
        }
    }

    private void DrawAmbientGlows(SKCanvas canvas, SKRect bounds)
    {
        var glowColor = FUIColors.Glow;

        // Create several ambient glow spots
        var glowSpots = new[]
        {
            (x: bounds.Left + bounds.Width * 0.33f, y: bounds.Top + bounds.Height * 0.8f, r: 80f, opacity: 0.33f),
            (x: bounds.Left + bounds.Width * 0.65f, y: bounds.Top + bounds.Height * 0.2f, r: 50f, opacity: 0.35f),
            (x: bounds.Left + bounds.Width * 0.5f, y: bounds.Top + bounds.Height * 0.5f, r: 250f, opacity: 0.25f),
        };

        foreach (var spot in glowSpots)
        {
            using var shader = SKShader.CreateRadialGradient(
                new SKPoint(spot.x, spot.y),
                spot.r,
                new[] { glowColor.WithAlpha((byte)(100 * spot.opacity)), SKColors.Transparent },
                new[] { 0f, 1f },
                SKShaderTileMode.Clamp
            );

            using var paint = new SKPaint { Shader = shader };
            canvas.DrawCircle(spot.x, spot.y, spot.r, paint);
        }
    }

    private void DrawFloatingPanels(SKCanvas canvas, SKRect bounds)
    {
        var panelFill = FUIColors.Primary.WithAlpha(8);
        var panelStroke = FUIColors.Frame.WithAlpha(40);
        var glowColor = FUIColors.Glow;

        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = panelFill,
            IsAntialias = true
        };

        using var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = panelStroke,
            StrokeWidth = 1f,
            IsAntialias = true
        };

        foreach (var panel in _panels)
        {
            // Calculate drifted position
            float driftX = MathF.Sin(_time * panel.DriftSpeedX + panel.Phase) * panel.DriftAmplitudeX;
            float driftY = MathF.Cos(_time * panel.DriftSpeedY + panel.Phase * 1.3f) * panel.DriftAmplitudeY;

            float x = panel.BaseX + driftX;
            float y = panel.BaseY + driftY;

            var rect = new SKRoundRect(new SKRect(x, y, x + panel.Width, y + panel.Height), 5);

            // Draw glow
            using var glowPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = glowColor.WithAlpha(15),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8),
                IsAntialias = true
            };
            canvas.DrawRoundRect(rect, glowPaint);

            // Draw panel
            canvas.DrawRoundRect(rect, fillPaint);
            canvas.DrawRoundRect(rect, strokePaint);
        }
    }

    private void DrawScanlines(SKCanvas canvas, SKRect bounds)
    {
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = SKColors.White.WithAlpha(8),
            StrokeWidth = 1f,
            BlendMode = SKBlendMode.Overlay
        };

        // Horizontal scanlines every 3 pixels
        for (float y = bounds.Top; y < bounds.Bottom; y += 3)
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
