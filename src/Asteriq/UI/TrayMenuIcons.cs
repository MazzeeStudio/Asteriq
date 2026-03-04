using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Asteriq.UI;

/// <summary>
/// Draws small GDI+ icon bitmaps for the system tray context menu.
/// All icons are drawn as clean line-art on transparent backgrounds.
/// </summary>
internal static class TrayMenuIcons
{
    /// <summary>Window outline — used for "Open" action.</summary>
    public static Bitmap Open(int size, Color color) => Create(size, g =>
    {
        float m  = size * 0.10f;
        float w  = size - m * 2;
        float h  = size - m * 2;
        float th = h * 0.26f; // title bar height
        using var pen = new Pen(color, 1.3f) { LineJoin = LineJoin.Round };
        g.DrawRectangle(pen, m, m, w, h);
        g.DrawLine(pen, m, m + th, m + w, m + th);
    });

    /// <summary>Right-pointing triangle — used for "Start Forwarding".</summary>
    public static Bitmap Play(int size, Color color) => Create(size, g =>
    {
        float cx = size * 0.54f;
        float cy = size * 0.50f;
        float r  = size * 0.33f;
        PointF[] pts =
        [
            new(cx - r * 0.55f, cy - r),
            new(cx + r,         cy),
            new(cx - r * 0.55f, cy + r),
        ];
        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, pts);
    });

    /// <summary>Filled square — used for "Stop Forwarding".</summary>
    public static Bitmap Stop(int size, Color color) => Create(size, g =>
    {
        float m = size * 0.27f;
        float s = size - m * 2;
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, m, m, s, s);
    });

    /// <summary>Three nodes connected by lines — used for network/connect actions.</summary>
    public static Bitmap Network(int size, Color color) => Create(size, g =>
    {
        float r   = size * 0.11f;
        float topX = size * 0.50f, topY = size * 0.16f;
        float blX  = size * 0.18f, blY  = size * 0.80f;
        float brX  = size * 0.82f, brY  = size * 0.80f;

        using var pen   = new Pen(color, 1.2f);
        using var brush = new SolidBrush(color);

        // Lines first (drawn beneath dots)
        g.DrawLine(pen, topX, topY + r, blX + r * 0.7f, blY - r * 0.7f);
        g.DrawLine(pen, topX, topY + r, brX - r * 0.7f, brY - r * 0.7f);
        g.DrawLine(pen, blX  + r, blY, brX - r, brY);

        // Dots
        g.FillEllipse(brush, topX - r, topY - r, r * 2, r * 2);
        g.FillEllipse(brush, blX  - r, blY  - r, r * 2, r * 2);
        g.FillEllipse(brush, brX  - r, brY  - r, r * 2, r * 2);
    });

    /// <summary>X mark — used for "Exit".</summary>
    public static Bitmap Exit(int size, Color color) => Create(size, g =>
    {
        float m = size * 0.21f;
        using var pen = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, m,          m,          size - m, size - m);
        g.DrawLine(pen, size - m,   m,          m,        size - m);
    });

    /// <summary>Monitor outline — used for individual peer items in the submenu.</summary>
    public static Bitmap Monitor(int size, Color color) => Create(size, g =>
    {
        float mx = size * 0.10f;
        float w  = size - mx * 2;
        float h  = (size - mx * 2) * 0.62f;
        float by = mx + h;

        using var pen = new Pen(color, 1.2f) { LineJoin = LineJoin.Round };
        g.DrawRectangle(pen, mx, mx, w, h);
        // Stand
        g.DrawLine(pen, size * 0.50f, by,              size * 0.50f, by + size * 0.18f);
        g.DrawLine(pen, size * 0.30f, by + size * 0.18f, size * 0.70f, by + size * 0.18f);
    });

    // -------------------------------------------------------------------------

    private static Bitmap Create(int size, Action<Graphics> draw)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        draw(g);
        return bmp;
    }
}
