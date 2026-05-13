using Asteriq.Models;
using SkiaSharp;

namespace Asteriq.UI;

/// <summary>
/// Reusable FUI widget primitives shared across all tab renderers.
/// All methods are stateless — all inputs are passed as parameters.
/// Follows the same pattern as FUIRenderer for consistency.
/// </summary>
internal static partial class FUIWidgets
{
    internal static void DrawProfileStat(SKCanvas canvas, float x, float y, string label, string value, float valueOffset = 130f)
    {
        FUIRenderer.DrawText(canvas, label, new SKPoint(x, y), FUIColors.TextDim, 13f);
        FUIRenderer.DrawText(canvas, value, new SKPoint(x + valueOffset, y), FUIColors.TextPrimary, 13f);
    }


    internal static void DrawSettingsValueField(SKCanvas canvas, SKRect bounds, string value)
    {
        FUIRenderer.DrawRoundedPanel(canvas, bounds, FUIColors.Background2, FUIColors.Frame);
        FUIRenderer.DrawTextCentered(canvas, value, bounds, FUIColors.TextPrimary, 14f);
    }

    // ─── Mapping Editor Widgets ───────────────────────────────────────────────

    /// <summary>
    /// Draws a section label with enforced spacing: 16px top margin, label text, 8px bottom margin.
    /// Returns updated y position (ready for the next element below).
    /// </summary>
    internal static float DrawSectionLabel(SKCanvas canvas, string text, float leftMargin, ref float y, float fontSize = 13f)
    {
        y += 16;
        FUIRenderer.DrawText(canvas, text, new SKPoint(leftMargin, y + fontSize), FUIColors.TextDim, fontSize);
        y += fontSize + 8;
        return y;
    }

    internal static void DrawToggleButton(SKCanvas canvas, SKRect bounds, string text, bool active, bool hovered, float fontSize = 14f, bool scaleFont = true)
    {
        var bgColor = active
            ? FUIColors.Active.WithAlpha(FUIColors.AlphaGlow)
            : (hovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background2);
        var frameColor = active ? FUIColors.Active : (hovered ? FUIColors.FrameBright : FUIColors.Frame);
        var textColor = FUIColors.SecondaryColor(active);

        using var bgPaint = FUIRenderer.CreateFillPaint(bgColor);
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = FUIRenderer.CreateStrokePaint(frameColor, active ? 1.5f : 1f);
        canvas.DrawRect(bounds, framePaint);

        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, fontSize, scaleFont);
    }

    internal static void DrawDropdown(SKCanvas canvas, SKRect bounds, string text, bool open)
    {
        var bgColor = open ? FUIColors.Primary.WithAlpha(40) : FUIColors.Background2;
        using var bgPaint = FUIRenderer.CreateFillPaint(bgColor);
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = FUIRenderer.CreateStrokePaint(open ? FUIColors.Primary : FUIColors.Frame);
        canvas.DrawRect(bounds, framePaint);

        FUIRenderer.DrawText(canvas, text, new SKPoint(bounds.Left + 8, bounds.MidY + 4),
            FUIColors.TextPrimary, 14f);

        string arrow = open ? "▲" : "▼";
        FUIRenderer.DrawText(canvas, arrow, new SKPoint(bounds.Right - 18, bounds.MidY + 4),
            FUIColors.TextDim, 13f);
    }

    /// <summary>
    /// Draws a small FUI-style pill/badge containing all-caps text. The pill has
    /// square corners with a chamfer on the lower-right to echo the L-corner
    /// frame language used elsewhere. Returns the pill bounds so callers can
    /// position content that follows. Pass <paramref name="anchorY"/> as the
    /// vertical centre of the pill.
    /// </summary>
    internal static SKRect DrawPill(SKCanvas canvas, float x, float anchorY, string text,
        SKColor bgColor, SKColor textColor, float fontSize = 10f)
    {
        float textWidth = FUIRenderer.MeasureText(text, fontSize);
        const float paddingX = 6f;
        float height = fontSize + 8f;
        var bounds = new SKRect(x, anchorY - height / 2f, x + textWidth + paddingX * 2, anchorY + height / 2f);

        float chamfer = Math.Min(4f, height / 3f);
        using var path = new SKPath();
        path.MoveTo(bounds.Left, bounds.Top);
        path.LineTo(bounds.Right, bounds.Top);
        path.LineTo(bounds.Right, bounds.Bottom - chamfer);
        path.LineTo(bounds.Right - chamfer, bounds.Bottom);
        path.LineTo(bounds.Left, bounds.Bottom);
        path.Close();

        using var bgPaint = FUIRenderer.CreateFillPaint(bgColor);
        canvas.DrawPath(path, bgPaint);

        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, fontSize);
        return bounds;
    }

    internal static void DrawSmallIconButton(SKCanvas canvas, SKRect bounds, string icon, bool hovered, bool isDanger = false)
    {
        var bgColor = hovered
            ? (isDanger ? FUIColors.Warning.WithAlpha(60) : FUIColors.Active.WithAlpha(FUIColors.AlphaGlow))
            : FUIColors.Background2.WithAlpha(100);
        var textColor = hovered
            ? (isDanger ? FUIColors.Warning : FUIColors.Active)
            : FUIColors.TextDim;

        using var bgPaint = FUIRenderer.CreateFillPaint(bgColor);
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = FUIRenderer.CreateStrokePaint(hovered ? (isDanger ? FUIColors.Warning : FUIColors.Active) : FUIColors.Frame);
        canvas.DrawRect(bounds, framePaint);

        FUIRenderer.DrawTextCentered(canvas, icon, bounds, textColor, 17f);
    }

    internal static void DrawActionButton(SKCanvas canvas, SKRect bounds, string text, bool hovered, bool isPrimary)
    {
        var bgColor = isPrimary
            ? (hovered ? FUIColors.Active : FUIColors.ActiveStrong)
            : (hovered ? FUIColors.Primary.WithAlpha(60) : FUIColors.Background2);
        var textColor = isPrimary
            ? FUIColors.Background1
            : (hovered ? FUIColors.TextBright : FUIColors.TextPrimary);

        using var bgPaint = FUIRenderer.CreateFillPaint(bgColor);
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = FUIRenderer.CreateStrokePaint(isPrimary ? FUIColors.Active : FUIColors.Frame);
        canvas.DrawRect(bounds, framePaint);

        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 15f);
    }

    internal static void DrawArrowButton(SKCanvas canvas, SKRect bounds, string arrow, bool hovered, bool enabled)
    {
        var bgColor = enabled
            ? (hovered ? FUIColors.Primary.WithAlpha(80) : FUIColors.Background2)
            : FUIColors.Background1;
        var arrowColor = enabled
            ? (hovered ? FUIColors.TextBright : FUIColors.TextPrimary)
            : FUIColors.TextDisabled;

        using var bgPaint = FUIRenderer.CreateFillPaint(bgColor);
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = FUIRenderer.CreateStrokePaint(enabled ? FUIColors.Frame : FUIColors.FrameDim);
        canvas.DrawRect(bounds, framePaint);

        float centerX = bounds.MidX;
        float centerY = bounds.MidY;
        float arrowSize = 8f;

        using var arrowPaint = FUIRenderer.CreateFillPaint(arrowColor);
        using var path = new SKPath();
        if (arrow == "<")
        {
            path.MoveTo(centerX + arrowSize / 2, centerY - arrowSize);
            path.LineTo(centerX - arrowSize / 2, centerY);
            path.LineTo(centerX + arrowSize / 2, centerY + arrowSize);
            path.Close();
        }
        else
        {
            path.MoveTo(centerX - arrowSize / 2, centerY - arrowSize);
            path.LineTo(centerX + arrowSize / 2, centerY);
            path.LineTo(centerX - arrowSize / 2, centerY + arrowSize);
            path.Close();
        }
        canvas.DrawPath(path, arrowPaint);
    }

    internal static void DrawAddMappingButton(SKCanvas canvas, SKRect bounds, bool hovered)
    {
        var bgColor = hovered ? FUIColors.Active.WithAlpha(FUIColors.AlphaGlow) : FUIColors.Primary.WithAlpha(30);
        var frameColor = hovered ? FUIColors.Active : FUIColors.Primary;

        using var bgPaint = FUIRenderer.CreateFillPaint(bgColor);
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = FUIRenderer.CreateStrokePaint(frameColor, hovered ? 2f : 1f);
        canvas.DrawRect(bounds, framePaint);

        float iconX = bounds.Left + 16;
        float iconY = bounds.MidY;
        using var iconPaint = FUIRenderer.CreateStrokePaint(hovered ? FUIColors.TextBright : FUIColors.TextPrimary, 2f);
        canvas.DrawLine(iconX - 6, iconY, iconX + 6, iconY, iconPaint);
        canvas.DrawLine(iconX, iconY - 6, iconX, iconY + 6, iconPaint);

        FUIRenderer.DrawText(canvas, "ADD MAPPING",
            new SKPoint(bounds.Left + 30, bounds.MidY + 5),
            hovered ? FUIColors.TextBright : FUIColors.TextPrimary, 15f);
    }

    // ─── Theme Buttons ────────────────────────────────────────────────────────

    internal static void DrawCollapseIndicator(SKCanvas canvas, float x, float y, bool isCollapsed, bool isHovered)
    {
        var color = isHovered ? FUIColors.TextBright : FUIColors.Primary;
        using var paint = FUIRenderer.CreateFillPaint(color);

        var path = new SKPath();
        if (isCollapsed)
        {
            path.MoveTo(x, y - 4);
            path.LineTo(x + 6, y);
            path.LineTo(x, y + 4);
        }
        else
        {
            path.MoveTo(x - 2, y - 3);
            path.LineTo(x + 6, y - 3);
            path.LineTo(x + 2, y + 3);
        }
        path.Close();
        canvas.DrawPath(path, paint);
    }

    internal static void DrawProfileRefreshButton(SKCanvas canvas, SKRect bounds, bool hovered)
    {
        var bgColor = hovered ? FUIColors.SelectionBgStrong : FUIColors.PanelBgDefault;
        var borderColor = hovered ? FUIColors.Active : FUIColors.Frame;
        FUIRenderer.DrawRoundedPanel(canvas, bounds, bgColor, borderColor);

        float cx = bounds.MidX;
        float cy = bounds.MidY;
        float r = 5f;
        var iconColor = hovered ? FUIColors.TextBright : FUIColors.TextPrimary;
        using var iconPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = iconColor, StrokeWidth = 1.5f, IsAntialias = true, StrokeCap = SKStrokeCap.Round };

        using var arcPath = new SKPath();
        arcPath.AddArc(new SKRect(cx - r, cy - r, cx + r, cy + r), -45, 270);
        canvas.DrawPath(arcPath, iconPaint);

        using var arrowPath = new SKPath();
        arrowPath.MoveTo(cx + r - 1, cy - r + 2);
        arrowPath.LineTo(cx + r + 2, cy - r - 1);
        arrowPath.LineTo(cx + r + 1, cy - r + 3);
        canvas.DrawPath(arrowPath, iconPaint);
    }


    // ─── General Navigation Widgets ───────────────────────────────────────────

    internal static void DrawDropdownItem(SKCanvas canvas, float x, float itemY, float width, float itemHeight,
        string text, bool isHovered, bool isActive, bool isEnabled)
    {
        var itemBounds = new SKRect(x + 4, itemY, x + width - 4, itemY + itemHeight);

        if (isHovered && isEnabled)
        {
            using var hoverPaint = FUIRenderer.CreateFillPaint(FUIColors.SelectionBg);
            canvas.DrawRect(itemBounds, hoverPaint);

            using var accentPaint = FUIRenderer.CreateFillPaint(FUIColors.Active);
            canvas.DrawRect(new SKRect(x + 4, itemY + 2, x + 6, itemY + itemHeight - 2), accentPaint);
        }

        var color = !isEnabled ? FUIColors.TextDisabled
            : isHovered ? FUIColors.TextBright
            : FUIColors.TextDim;
        FUIRenderer.DrawText(canvas, text, new SKPoint(x + 12, itemY + 17), color, 14f);
    }

    internal static void DrawTextFieldReadOnly(SKCanvas canvas, SKRect bounds, string text, bool isHovered)
    {
        var bgColor = isHovered ? FUIColors.Background2.WithAlpha(180) : FUIColors.Background1.WithAlpha(140);
        using var bgPaint = FUIRenderer.CreateFillPaint(bgColor);
        canvas.DrawRect(bounds, bgPaint);

        using var borderPaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame);
        canvas.DrawRect(bounds, borderPaint);

        FUIRenderer.DrawText(canvas, text, new SKPoint(bounds.Left + 10, bounds.MidY + 4), FUIColors.TextPrimary, 14f);
    }

    /// <summary>
    /// Draws a standard FUI panel title with consistent spacing.
    /// Advances <paramref name="y"/> past the title (and optional divider) so the
    /// caller can start drawing content immediately.
    /// </summary>
    /// <param name="withDivider">When true, draws a horizontal rule below the title with extra breathing room.</param>
    internal static void DrawPanelTitle(
        SKCanvas canvas,
        float leftMargin,
        float rightMargin,
        ref float y,
        string title,
        bool withDivider = false)
    {
        FUIRenderer.DrawText(canvas, title, new SKPoint(leftMargin, y), FUIColors.TextBright, 14f, true);
        y += 18f;

        if (withDivider)
        {
            using var sep = FUIRenderer.CreateStrokePaint(FUIColors.Frame);
            canvas.DrawLine(leftMargin, y, rightMargin, y, sep);
            y += 14f;
        }
    }

    internal static void DrawSelector(SKCanvas canvas, SKRect bounds, string text, bool isHovered, bool isEnabled)
    {
        var bgColor = isEnabled
            ? (isHovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background1.WithAlpha(150))
            : FUIColors.Background1.WithAlpha(100);

        using var bgPaint = FUIRenderer.CreateFillPaint(bgColor);
        canvas.DrawRect(bounds, bgPaint);

        var borderColor = isEnabled
            ? (isHovered ? FUIColors.FrameBright : FUIColors.Frame)
            : FUIColors.Frame.WithAlpha(100);
        using var borderPaint = FUIRenderer.CreateStrokePaint(borderColor);
        canvas.DrawRect(bounds, borderPaint);

        float textPadding = 8f;
        float arrowSpaceRight = 20f;
        float maxTextWidth = bounds.Width - textPadding - arrowSpaceRight;
        string truncatedText = TruncateTextToWidth(text, maxTextWidth, 11f);

        var textColor = isEnabled ? FUIColors.TextPrimary : FUIColors.TextDim;
        FUIRenderer.DrawText(canvas, truncatedText, new SKPoint(bounds.Left + textPadding, bounds.MidY + 4), textColor, 14f);

        if (isEnabled)
        {
            float arrowX = bounds.Right - 12f;
            float arrowY = bounds.MidY;
            using var arrowPaint = FUIRenderer.CreateFillPaint(FUIColors.TextDim);
            using var arrowPath = new SKPath();
            arrowPath.MoveTo(arrowX - 4, arrowY - 2);
            arrowPath.LineTo(arrowX + 4, arrowY - 2);
            arrowPath.LineTo(arrowX, arrowY + 3);
            arrowPath.Close();
            canvas.DrawPath(arrowPath, arrowPaint);
        }
    }

    /// <summary>
    /// Draws a FUI-styled open dropdown panel: shadow, glow, background, L-corner frame, and
    /// a uniform list of string items with hover / selection highlighting.
    /// The caller is responsible for positioning <paramref name="bounds"/> and, when scrolling
    /// is needed, for drawing the scrollbar track on top after this call.
    /// </summary>
    /// <param name="selectedIndex">0-based index of the selected item, or -1 for none.</param>
    /// <param name="hoveredIndex">0-based index of the hovered item, or -1 for none.</param>
    /// <param name="itemHeight">Height of each row in pixels (default 28).</param>
    /// <param name="scrollOffset">Vertical pixel offset applied to the item list (default 0).</param>
    /// <param name="scrollbarWidth">Width reserved on the right edge for the caller's scrollbar (default 0).</param>
    internal static void DrawDropdownPanel(
        SKCanvas canvas,
        SKRect bounds,
        IReadOnlyList<string> items,
        int selectedIndex,
        int hoveredIndex,
        float itemHeight = 28f,
        float scrollOffset = 0f,
        float scrollbarWidth = 0f)
    {
        // Shadow + outer glow
        FUIRenderer.DrawPanelShadow(canvas, bounds, 4f, 4f, 15f);
        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.ActiveLight,
            StrokeWidth = 3f,
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(4f, 4f)
        };
        canvas.DrawRect(bounds, glowPaint);

        // Opaque backgrounds
        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Void);
        canvas.DrawRect(bounds, bgPaint);
        using var innerPaint = FUIRenderer.CreateFillPaint(FUIColors.Background0);
        canvas.DrawRect(bounds.Inset(2, 2), innerPaint);

        // L-corner frame
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.ActiveStrong, 20f, 6f, 1.5f, true);

        // Items (clipped for scroll)
        canvas.Save();
        canvas.ClipRect(bounds);
        float y = bounds.Top + 2f - scrollOffset;
        for (int i = 0; i < items.Count; i++)
        {
            var itemBounds = new SKRect(bounds.Left + 2, y, bounds.Right - 2 - scrollbarWidth, y + itemHeight);
            if (itemBounds.Bottom > bounds.Top && itemBounds.Top < bounds.Bottom)
            {
                bool isHovered = i == hoveredIndex;
                bool isSelected = i == selectedIndex;

                if (isHovered)
                {
                    using var hoverBg = FUIRenderer.CreateFillPaint(FUIColors.SelectionBg);
                    canvas.DrawRect(itemBounds, hoverBg);
                    using var accentBar = FUIRenderer.CreateFillPaint(FUIColors.Active);
                    canvas.DrawRect(new SKRect(itemBounds.Left, itemBounds.Top + 2, itemBounds.Left + 2, itemBounds.Bottom - 2), accentBar);
                }
                else if (isSelected)
                {
                    using var selAccent = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(FUIColors.AlphaGlow));
                    canvas.DrawRect(new SKRect(itemBounds.Left, itemBounds.Top + 2, itemBounds.Left + 2, itemBounds.Bottom - 2), selAccent);
                }

                var textColor = isSelected ? FUIColors.Active : (isHovered ? FUIColors.TextBright : FUIColors.TextPrimary);
                FUIRenderer.DrawText(canvas, items[i], new SKPoint(itemBounds.Left + 10, itemBounds.MidY + 4), textColor, 13f);
            }
            y += itemHeight;
        }
        canvas.Restore();
    }

    // ─── Segmented Control ──────────────────────────────────────────────────────

}