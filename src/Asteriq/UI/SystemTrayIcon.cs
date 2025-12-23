using SkiaSharp;
using System.Drawing.Drawing2D;

namespace Asteriq.UI;

/// <summary>
/// Manages the system tray icon with color-changing based on forwarding state.
/// Draws a simple joystick icon that changes color based on forwarding state and current theme.
/// </summary>
public sealed class SystemTrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private bool _isActive;
    private Icon? _currentIcon;

    public SystemTrayIcon(string toolTipText = "Asteriq")
    {
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
    /// Generate icon with current theme colors.
    /// Draws a simple joystick shape directly using GDI+.
    /// </summary>
    private Icon GenerateIcon()
    {
        const int size = 16;
        using var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        // Get color based on state and current theme
        SKColor skColor = _isActive ? FUIColors.Active : FUIColors.TextDim;
        Color color = Color.FromArgb(skColor.Alpha, skColor.Red, skColor.Green, skColor.Blue);

        // Draw simple joystick icon (stick + base)
        using var pen = new Pen(color, 1.5f);
        using var brush = new SolidBrush(color);

        // Base (circle at bottom)
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
