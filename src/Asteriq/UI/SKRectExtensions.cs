using SkiaSharp;

namespace Asteriq.UI;

public static class SKRectExtensions
{
    public static SKRect Inset(this SKRect rect, float dx, float dy)
    {
        return new SKRect(rect.Left + dx, rect.Top + dy, rect.Right - dx, rect.Bottom - dy);
    }

    /// <summary>
    /// Safe hit-test: returns false for empty/unset bounds, true if point is inside.
    /// Replaces the repeated <c>!bounds.IsEmpty &amp;&amp; bounds.Contains(x, y)</c> pattern.
    /// </summary>
    public static bool HitTest(this SKRect bounds, float x, float y)
        => !bounds.IsEmpty && bounds.Contains(x, y);

    /// <summary>
    /// Safe hit-test using an SKPoint.
    /// </summary>
    public static bool HitTest(this SKRect bounds, SKPoint pt)
        => !bounds.IsEmpty && bounds.Contains(pt);
}
