using SkiaSharp;
using Svg.Skia;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Asteriq.Models;

namespace Asteriq.UI;

/// <summary>
/// Manages the system tray icon with color-changing based on forwarding state.
/// Loads joystick.svg and colorizes it based on forwarding state and current theme.
/// </summary>
public sealed class SystemTrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private string _svgPath;
    private bool _isActive;
    private Icon? _currentIcon;
    private TrayIconType _iconType;

    public SystemTrayIcon(string toolTipText = "Asteriq", TrayIconType iconType = TrayIconType.Joystick)
    {
        _iconType = iconType;
        UpdateSvgPath();
        _notifyIcon = new NotifyIcon
        {
            Text = toolTipText,
            Visible = false // Start hidden, will show after first icon generation
        };

        // Generate initial icon
        UpdateIcon();
        _notifyIcon.Visible = true;
    }

    /// <summary>
    /// Set the context menu for the tray icon.
    /// </summary>
    public ContextMenuStrip? ContextMenuStrip
    {
        get => _notifyIcon.ContextMenuStrip;
        set => _notifyIcon.ContextMenuStrip = value;
    }

    /// <summary>
    /// Generate icon from SVG with current theme colors.
    /// Renders SVG to bitmap then applies color tint.
    /// </summary>
    private Icon GenerateIcon()
    {
        const int size = 32;  // Use 32x32 for better visibility in system tray

        // Get color based on state and current theme
        SKColor skColor = _isActive ? FUIColors.Active : FUIColors.TextDim;
        Color targetColor = Color.FromArgb(skColor.Red, skColor.Green, skColor.Blue);

        try
        {
            // Load and render SVG using SkiaSharp
            var svg = new SKSvg();
            if (File.Exists(_svgPath))
            {
                using var stream = File.OpenRead(_svgPath);
                svg.Load(stream);
            }

            if (svg.Picture is not null)
            {
                // Render SVG to SKBitmap
                var bounds = svg.Picture.CullRect;

                // Scale to fill more of the canvas - multiply by 1.5 to make icon bigger without clipping
                var baseScale = Math.Min(size / bounds.Width, size / bounds.Height);
                var scale = baseScale * 1.5f;

                using var surface = SKSurface.Create(new SKImageInfo(size, size));
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);

                // Center the scaled icon
                canvas.Translate((size - bounds.Width * scale) / 2, (size - bounds.Height * scale) / 2);
                canvas.Scale(scale);

                // Draw SVG in white (we'll colorize it later)
                canvas.DrawPicture(svg.Picture);

                // Convert to GDI+ bitmap
                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var ms = new MemoryStream();
                data.SaveTo(ms);
                ms.Position = 0;

                using var bitmap = new Bitmap(ms);

                // Apply color tint to the bitmap
                var tintedBitmap = ApplyColorTint(bitmap, targetColor);
                return Icon.FromHandle(tintedBitmap.GetHicon());
            }
        }
        catch
        {
            // SVG loading failed, fall back to simple shape
        }

        // Fallback: draw simple joystick icon if SVG fails
        return GenerateFallbackIcon(targetColor, size);
    }

    /// <summary>
    /// Apply a color tint to a bitmap while preserving transparency.
    /// </summary>
    private static Bitmap ApplyColorTint(Bitmap source, Color tintColor)
    {
        var result = new Bitmap(source.Width, source.Height);

        using (var g = Graphics.FromImage(result))
        {
            // Create a color matrix to tint the image
            float r = tintColor.R / 255f;
            float gr = tintColor.G / 255f;
            float b = tintColor.B / 255f;

            var colorMatrix = new ColorMatrix(new float[][]
            {
                new float[] {r, 0, 0, 0, 0},
                new float[] {0, gr, 0, 0, 0},
                new float[] {0, 0, b, 0, 0},
                new float[] {0, 0, 0, 1, 0},
                new float[] {0, 0, 0, 0, 1}
            });

            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(colorMatrix);

            g.DrawImage(source,
                new Rectangle(0, 0, source.Width, source.Height),
                0, 0, source.Width, source.Height,
                GraphicsUnit.Pixel,
                attributes);
        }

        return result;
    }

    /// <summary>
    /// Generate a simple fallback icon if SVG loading fails.
    /// </summary>
    private Icon GenerateFallbackIcon(Color color, int size)
    {
        using var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var pen = new Pen(color, 1.5f);
        using var brush = new SolidBrush(color);

        // Draw simple joystick icon (stick + base)
        float baseY = size * 0.75f;
        float baseRadius = size * 0.35f;
        g.FillEllipse(brush, size / 2f - baseRadius, baseY - baseRadius, baseRadius * 2, baseRadius * 2);

        // Stick (vertical line)
        float stickTop = size * 0.15f;
        float stickBottom = baseY;
        g.DrawLine(pen, size / 2f, stickTop, size / 2f, stickBottom);

        // Stick top (small circle)
        float topRadius = size * 0.2f;
        g.FillEllipse(brush, size / 2f - topRadius, stickTop - topRadius, topRadius * 2, topRadius * 2);

        // Add glow effect when active
        if (_isActive)
        {
            using var glowBrush = new SolidBrush(Color.FromArgb(60, color));
            g.FillEllipse(glowBrush, size / 2f - baseRadius * 1.3f, baseY - baseRadius * 1.3f,
                baseRadius * 2.6f, baseRadius * 2.6f);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    /// <summary>
    /// Update the icon with current state and theme colors.
    /// </summary>
    private void UpdateIcon()
    {
        _currentIcon?.Dispose();
        _currentIcon = GenerateIcon();
        _notifyIcon.Icon = _currentIcon;
    }

    /// <summary>
    /// Set the icon to active (glowing) or normal (dim) state.
    /// Regenerates icon with current theme colors.
    /// </summary>
    public void SetActive(bool active)
    {
        if (_isActive == active) return;

        _isActive = active;
        UpdateIcon();
    }

    /// <summary>
    /// Refresh the icon with current theme colors (call when theme changes).
    /// </summary>
    public void RefreshThemeColors()
    {
        UpdateIcon();
    }

    /// <summary>
    /// Change the icon type (joystick or throttle) and regenerate.
    /// </summary>
    public void SetIconType(TrayIconType iconType)
    {
        if (_iconType == iconType) return;

        _iconType = iconType;
        UpdateSvgPath();
        UpdateIcon();
    }

    /// <summary>
    /// Update the SVG path based on current icon type.
    /// </summary>
    private void UpdateSvgPath()
    {
        var svgFileName = _iconType == TrayIconType.Joystick ? "joystick.svg" : "throttle.svg";
        _svgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Devices", svgFileName);
    }

    /// <summary>
    /// Show a balloon tip notification.
    /// </summary>
    public void ShowBalloonTip(string title, string text, ToolTipIcon icon = ToolTipIcon.Info, int timeout = 3000)
    {
        _notifyIcon.ShowBalloonTip(timeout, title, text, icon);
    }

    /// <summary>
    /// Set the tooltip text.
    /// </summary>
    public void SetToolTip(string text)
    {
        _notifyIcon.Text = text;
    }

    /// <summary>
    /// Subscribe to tray icon click events.
    /// </summary>
    public event EventHandler? Click
    {
        add => _notifyIcon.Click += value;
        remove => _notifyIcon.Click -= value;
    }

    /// <summary>
    /// Subscribe to tray icon double-click events.
    /// </summary>
    public event EventHandler? DoubleClick
    {
        add => _notifyIcon.DoubleClick += value;
        remove => _notifyIcon.DoubleClick -= value;
    }

    /// <summary>
    /// Subscribe to tray icon mouse click events (includes button info).
    /// </summary>
    public event MouseEventHandler? MouseClick
    {
        add => _notifyIcon.MouseClick += value;
        remove => _notifyIcon.MouseClick -= value;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
    }
}
