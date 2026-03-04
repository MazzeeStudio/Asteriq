using System.Drawing.Drawing2D;

namespace Asteriq.UI;

/// <summary>
/// Custom renderer for dark-themed context menus matching the FUI aesthetic.
/// </summary>
public class DarkContextMenuRenderer : ToolStripProfessionalRenderer
{
    private const int IconColumnWidth = 32;

    public DarkContextMenuRenderer() : base(new DarkContextMenuColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var rect = new Rectangle(Point.Empty, e.Item.Size);

        if (e.Item.Selected || e.Item.Pressed)
        {
            var skColor     = FUIColors.Active;
            var activeColor = Color.FromArgb(skColor.Red, skColor.Green, skColor.Blue);

            // Subtle tinted fill across the full row
            using var brush = new SolidBrush(Color.FromArgb(40, activeColor));
            e.Graphics.FillRectangle(brush, rect);

            // Left border accent (inside the icon column)
            using var accentBrush = new SolidBrush(activeColor);
            e.Graphics.FillRectangle(accentBrush, 0, 0, 2, rect.Height);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var skColor  = e.Item.Enabled ? FUIColors.TextPrimary : FUIColors.TextDisabled;
        e.TextColor  = Color.FromArgb(skColor.Red, skColor.Green, skColor.Blue);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var skColor        = FUIColors.FrameDim;
        // Half-alpha to make separator dimmer than other frame elements
        var separatorColor = Color.FromArgb(90, skColor.Red, skColor.Green, skColor.Blue);
        int y              = e.Item.Height / 2;

        using var pen = new Pen(separatorColor);
        e.Graphics.DrawLine(pen, 0, y, e.Item.Width, y);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // Intentionally empty — let DWM own the window edge entirely.
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        if (e.Item is null) return;
        var skColor   = e.Item.Enabled ? FUIColors.TextDim : FUIColors.TextDisabled;
        e.ArrowColor  = Color.FromArgb(skColor.Red, skColor.Green, skColor.Blue);
        base.OnRenderArrow(e);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // Draw the icon column background in the same dark colour as the rest of the menu.
        // This prevents the default light-grey gradient WinForms paints here.
        var skColor = FUIColors.Background1;
        using var brush = new SolidBrush(Color.FromArgb(skColor.Red, skColor.Green, skColor.Blue));
        e.Graphics.FillRectangle(brush,
            new Rectangle(0, 0, IconColumnWidth, e.AffectedBounds.Height));
    }

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Image is null) return;

        // Centre the icon inside the icon column
        int iconSize = e.Image.Width;
        int x        = (IconColumnWidth - iconSize) / 2;
        int y        = (e.Item.Height  - iconSize) / 2;

        e.Graphics.DrawImage(e.Image, x, y, iconSize, iconSize);
    }
}

/// <summary>
/// Colour table for dark-themed context menus.
/// </summary>
public class DarkContextMenuColorTable : ProfessionalColorTable
{
    private static Color Bg1()
    {
        var c = FUIColors.Background1;
        return Color.FromArgb(c.Red, c.Green, c.Blue);
    }

    // Make the border invisible by matching the background colour exactly.
    public override Color MenuBorder              => Bg1();
    public override Color MenuItemBorder          => Color.Transparent;

    public override Color MenuItemSelected
    {
        get
        {
            var c = FUIColors.Active;
            return Color.FromArgb(30, c.Red, c.Green, c.Blue);
        }
    }

    public override Color MenuItemSelectedGradientBegin => MenuItemSelected;
    public override Color MenuItemSelectedGradientEnd   => MenuItemSelected;
    public override Color MenuItemPressedGradientBegin  => MenuItemSelected;
    public override Color MenuItemPressedGradientEnd    => MenuItemSelected;

    public override Color ToolStripDropDownBackground   => Bg1();

    public override Color ImageMarginGradientBegin      => Bg1();
    public override Color ImageMarginGradientMiddle     => Bg1();
    public override Color ImageMarginGradientEnd        => Bg1();

    public override Color SeparatorDark
    {
        get
        {
            var c = FUIColors.FrameDim;
            return Color.FromArgb(c.Red, c.Green, c.Blue);
        }
    }

    public override Color SeparatorLight => SeparatorDark;
}
