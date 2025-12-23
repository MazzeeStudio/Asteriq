using SkiaSharp;
using Svg.Skia;
using System.Runtime.InteropServices;

namespace Asteriq.UI;

/// <summary>
/// Manages the system tray icon with color-changing based on forwarding state.
/// Loads joystick.svg and renders it in normal (dim) or active (glowing) state.
/// </summary>
public sealed class SystemTrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly string _svgPath;
    private Icon? _normalIcon;
    private Icon? _activeIcon;
    private bool _isActive;

    public SystemTrayIcon(string svgPath, string toolTipText = "Asteriq")
    {
        _svgPath = svgPath;
        _notifyIcon = new NotifyIcon
        {
            Text = toolTipText,
            Visible = true
        };

        GenerateIcons();
        SetActive(false); // Start in normal state
    }

    /// <summary>
    /// Generate both normal and active state icons from the SVG.
    /// </summary>
    private void GenerateIcons()
    {
        if (!File.Exists(_svgPath))
        {
            throw new FileNotFoundException($"SVG file not found: {_svgPath}");
        }

        // Load SVG
        var svg = new SKSvg();
        svg.Load(_svgPath);

        if (svg.Picture is null)
        {
            throw new InvalidOperationException($"Failed to load SVG from {_svgPath}");
        }

        // Icon sizes for Windows (16x16 is standard tray icon size)
        const int iconSize = 16;

        // Generate normal icon (dim grayscale)
        _normalIcon = GenerateIcon(svg.Picture, iconSize, FUIColors.TextDim);

        // Generate active icon (theme active color with glow)
        _activeIcon = GenerateIcon(svg.Picture, iconSize, FUIColors.Active);
    }

    /// <summary>
    /// Generate an icon from the SVG picture with the specified color.
    /// </summary>
    private static Icon GenerateIcon(SKPicture picture, int size, SKColor color)
    {
        // Create a bitmap to render the SVG
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        // Calculate scale to fit SVG in icon size
        var bounds = picture.CullRect;
        var scale = Math.Min(size / bounds.Width, size / bounds.Height);
        var offsetX = (size - bounds.Width * scale) / 2;
        var offsetY = (size - bounds.Height * scale) / 2;

        canvas.Translate(offsetX, offsetY);
        canvas.Scale(scale);

        // Apply color filter to colorize the SVG
        using var paint = new SKPaint();
        using var colorFilter = SKColorFilter.CreateBlendMode(color, SKBlendMode.SrcIn);
        paint.ColorFilter = colorFilter;

        canvas.DrawPicture(picture, paint);
        canvas.Flush();

        // Convert to Windows Icon
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var memoryStream = new MemoryStream();
        data.SaveTo(memoryStream);
        memoryStream.Position = 0;

        // Convert PNG to Icon
        using var bitmap = new Bitmap(memoryStream);
        return Icon.FromHandle(bitmap.GetHicon());
    }

    /// <summary>
    /// Set the icon to active (glowing) or normal (dim) state.
    /// </summary>
    public void SetActive(bool active)
    {
        if (_isActive == active) return;

        _isActive = active;
        _notifyIcon.Icon = active ? _activeIcon : _normalIcon;
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

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _normalIcon?.Dispose();
        _activeIcon?.Dispose();
    }
}
