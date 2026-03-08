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
    /// Generate a taskbar/form icon themed for the current Windows light/dark setting.
    /// Dark mode → brand orange; light mode → near-black.
    /// </summary>
    public Icon CreateFormIcon()
    {
        SKColor color = IsSystemLightTheme()
            ? new SKColor(0x20, 0x20, 0x20)  // near-black — readable on light taskbar
            : FUIColors.TextBright;           // white — same as inactive tray on dark taskbar
        return RenderIconWithColor(Color.FromArgb(color.Red, color.Green, color.Blue));
    }

    /// <summary>
    /// Generate tray icon from SVG with current state and Windows theme colors.
    /// Renders SVG to bitmap then applies color tint.
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

                canvas.Translate((size - bounds.Width * scale) / 2f, (size - bounds.Height * scale) / 2f);
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
                using var tintedBitmap = ApplyColorTint(bitmap, targetColor);
                return Icon.FromHandle(tintedBitmap.GetHicon());
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            // SVG loading failed, fall back to simple shape
        }

        // Fallback: simple A shape if SVG fails
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
