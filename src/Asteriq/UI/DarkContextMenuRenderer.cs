using System.Drawing.Drawing2D;

namespace Asteriq.UI;

/// <summary>
/// Custom renderer for dark-themed context menus matching the FUI aesthetic.
/// </summary>
public class DarkContextMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkContextMenuRenderer() : base(new DarkContextMenuColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var rect = new Rectangle(Point.Empty, e.Item.Size);

        if (e.Item.Selected || e.Item.Pressed)
        {
            // Highlighted item - use active color
            var skColor = FUIColors.Active;
            var activeColor = Color.FromArgb(skColor.Red, skColor.Green, skColor.Blue);
            using var brush = new SolidBrush(Color.FromArgb(40, activeColor));
            e.Graphics.FillRectangle(brush, rect);

            // Left border accent
            using var borderBrush = new SolidBrush(activeColor);
            e.Graphics.FillRectangle(borderBrush, 0, 0, 2, rect.Height);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // Use FUI text colors
        var skColor = e.Item.Enabled ? FUIColors.TextPrimary : FUIColors.TextDisabled;
        e.TextColor = Color.FromArgb(skColor.Red, skColor.Green, skColor.Blue);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var skColor = FUIColors.FrameDim;
        var separatorColor = Color.FromArgb(skColor.Red, skColor.Green, skColor.Blue);

        var rect = new Rectangle(e.Item.ContentRectangle.Left + 30,
            e.Item.ContentRectangle.Height / 2,
            e.Item.ContentRectangle.Width - 30,
            1);

        using var pen = new Pen(separatorColor);
        e.Graphics.DrawLine(pen, rect.Left, rect.Top, rect.Right, rect.Top);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        var skColor = e.Item.Enabled ? FUIColors.TextPrimary : FUIColors.TextDisabled;
        e.ArrowColor = Color.FromArgb(skColor.Red, skColor.Green, skColor.Blue);
        base.OnRenderArrow(e);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // Don't render image margin background - keep it consistent
    }
}

/// <summary>
/// Color table for dark-themed context menus.
/// </summary>
public class DarkContextMenuColorTable : ProfessionalColorTable
{
    public override Color MenuBorder
    {
        get
        {
            var skColor = FUIColors.Frame;
            return Color.FromArgb(skColor.Red, skColor.Green, skColor.Blue);
        }
    }

    public override Color MenuItemBorder
    {
        get
        {
            var skColor = FUIColors.Active;
            return Color.FromArgb(skColor.Red, skColor.Green, skColor.Blue);
        }
    }

    public override Color MenuItemSelected
    {
        get
        {
            var skColor = FUIColors.Active;
            return Color.FromArgb(30, skColor.Red, skColor.Green, skColor.Blue);
        }
    }

    public override Color MenuItemSelectedGradientBegin => MenuItemSelected;
    public override Color MenuItemSelectedGradientEnd => MenuItemSelected;

    public override Color MenuItemPressedGradientBegin => MenuItemSelected;
    public override Color MenuItemPressedGradientEnd => MenuItemSelected;

    public override Color ToolStripDropDownBackground
    {
        get
        {
            var skColor = FUIColors.Background1;
            return Color.FromArgb(skColor.Red, skColor.Green, skColor.Blue);
        }
    }

    public override Color ImageMarginGradientBegin => ToolStripDropDownBackground;
    public override Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
    public override Color ImageMarginGradientEnd => ToolStripDropDownBackground;

    public override Color SeparatorDark
    {
        get
        {
            var skColor = FUIColors.FrameDim;
            return Color.FromArgb(skColor.Red, skColor.Green, skColor.Blue);
        }
    }

    public override Color SeparatorLight => SeparatorDark;
}
