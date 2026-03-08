using System.Security;
using Microsoft.Win32;
using SkiaSharp;
using Svg.Skia;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
namespace Asteriq.UI;

/// <summary>
/// Manages the system tray icon with color-changing based on forwarding state.
/// Colorizes the Asteriq logo based on forwarding state and current theme.
/// </summary>
public sealed class SystemTrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private string _svgPath = string.Empty;
    private bool _isActive;
    private Icon? _currentIcon;

    public SystemTrayIcon(string toolTipText = "Asteriq")
    {
        UpdateSvgPath();
        _notifyIcon = new NotifyIcon
        {
            Text = toolTipText,
            Visible = false // Start hidden, will show after first icon generation
        };

        // Refresh icon if the user switches Windows light/dark mode live
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        // Generate initial icon
        UpdateIcon();
        _notifyIcon.Visible = true;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
            UpdateIcon();
    }

    /// <summary>
    /// Returns true when Windows is set to a light system theme (light taskbar/tray area).
    /// </summary>
    private static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { return false; }
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
    /// Generate the form/taskbar icon. Uses the Active brand colour so the icon is
    /// visible on any taskbar background without requiring theme detection.
    /// (Windows does not allow installed apps to change their taskbar icon per theme.)
    /// </summary>
    public Icon CreateFormIcon()
    {
        SKColor c = FUIColors.Active;
        return RenderIconWithColor(Color.FromArgb(c.Red, c.Green, c.Blue));
    }

    /// <summary>
    /// Generate tray icon from SVG with current state and Windows theme colors.
    /// </summary>
    private Icon GenerateIcon()
    {
        // Get color based on forwarding state and Windows taskbar theme.
        // In light mode the tray background is near-white, so use a dark colour for
        // the inactive state to keep the icon visible.
        SKColor skColor;
        if (_isActive)
            skColor = FUIColors.Active;
        else if (IsSystemLightTheme())
            skColor = new SKColor(40, 40, 40); // dark grey — readable on light taskbar
        else
            skColor = FUIColors.TextBright;
        return RenderIconWithColor(Color.FromArgb(skColor.Red, skColor.Green, skColor.Blue));
    }

    private Icon RenderIconWithColor(Color targetColor)
    {
        const int size = 64;

        try
        {
            // Load and render SVG using SkiaSharp
            using var svg = new SKSvg();
            if (File.Exists(_svgPath))
            {
                using var stream = File.OpenRead(_svgPath);
                svg.Load(stream);
            }

            if (svg.Picture is not null)
            {
                var bounds = svg.Picture.CullRect;
                var scale = Math.Min(size / bounds.Width, size / bounds.Height) * 0.90f;

                using var surface = SKSurface.Create(new SKImageInfo(size, size));
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);

                // Apply color tint via SkiaSharp color filter — no GDI+ involved.
                // The SVG paths are near-white, so multiplying each channel by the
                // target colour fraction maps them to the desired colour.
                float r = targetColor.R / 255f;
                float g = targetColor.G / 255f;
                float b = targetColor.B / 255f;

                using var colorFilter = SKColorFilter.CreateColorMatrix(new float[]
                {
                    r, 0, 0, 0, 0,
                    0, g, 0, 0, 0,
                    0, 0, b, 0, 0,
                    0, 0, 0, 1, 0
                });
                // SaveLayer guarantees the ColorFilter is applied when compositing.
                // DrawPicture(picture, paint) does not reliably apply paint filters.
                using var tintPaint = new SKPaint { ColorFilter = colorFilter };
                canvas.SaveLayer(tintPaint);
                canvas.Save();
                canvas.Translate((size - bounds.Width * scale) / 2f, (size - bounds.Height * scale) / 2f);
                canvas.Scale(scale);
                canvas.DrawPicture(svg.Picture);
                canvas.Restore();
                canvas.Restore(); // composites layer through tintPaint

                // Encode entirely in SkiaSharp — no GDI+ Bitmap needed.
                using var image = surface.Snapshot();
                using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
                return BuildIconFromPng(pngData.ToArray(), size);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            // SVG loading failed, fall back to simple shape
        }

        // Fallback: simple joystick shape if SVG fails
        return GenerateFallbackIcon(targetColor, size);
    }

    /// <summary>
    /// Writes a raw PNG byte array into a minimal ICO container and returns an Icon.
    /// No GDI+ involved — the PNG is embedded directly as the ICO image data.
    /// </summary>
    private static Icon BuildIconFromPng(byte[] png, int size)
    {
        using var icoStream = new MemoryStream();
        // ICONDIR: Reserved=0, Type=1 (ICO), Count=1
        icoStream.Write(new byte[] { 0, 0, 1, 0, 1, 0 });
        // ICONDIRENTRY: Width, Height, ColorCount, Reserved, Planes, BitCount, BytesInRes, ImageOffset(22)
        int w = size >= 256 ? 0 : size;
        int h = size >= 256 ? 0 : size;
        icoStream.WriteByte((byte)w);
        icoStream.WriteByte((byte)h);
        icoStream.WriteByte(0);  // ColorCount
        icoStream.WriteByte(0);  // Reserved
        icoStream.Write(BitConverter.GetBytes((short)1));   // Planes
        icoStream.Write(BitConverter.GetBytes((short)32));  // BitCount
        icoStream.Write(BitConverter.GetBytes(png.Length));
        icoStream.Write(BitConverter.GetBytes(22));  // ImageOffset = 6 + 16
        icoStream.Write(png);
        icoStream.Position = 0;
        return new Icon(icoStream);
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

        return BitmapToIcon(bitmap);
    }

    /// <summary>
    /// Encodes a Bitmap as a single-image ICO in a MemoryStream and returns a fully
    /// managed Icon. Unlike Icon.FromHandle(GetHicon()), this Icon owns its data and
    /// renders correctly as Form.Icon (taskbar button) on all Windows versions.
    /// </summary>
    private static Icon BitmapToIcon(Bitmap bitmap)
    {
        using var pngStream = new MemoryStream();
        bitmap.Save(pngStream, System.Drawing.Imaging.ImageFormat.Png);
        return BuildIconFromPng(pngStream.ToArray(), bitmap.Width);
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

    private void UpdateSvgPath()
    {
        _svgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "AsteriqLogo.svg");
    }

    /// <summary>
    /// Show a balloon tip notification.
    /// </summary>
    public void ShowBalloonTip(string title, string text, ToolTipIcon icon = ToolTipIcon.None, int timeout = 3000)
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
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
    }
}
