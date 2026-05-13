using Asteriq.Models;
using SkiaSharp;

namespace Asteriq.UI;

internal static partial class FUIWidgets
{
    /// <param name="mousePosition">Current mouse position for hover detection.</param>
    internal static void DrawThemeButton(SKCanvas canvas, SKRect bounds, string name, SKColor previewColor, bool isActive, Point mousePosition)
    {
        bool isHovered = bounds.Contains(mousePosition.X, mousePosition.Y);

        var bgColor = isActive
            ? previewColor.WithAlpha(60)
            : (isHovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background2);
        var frameColor = isActive
            ? previewColor
            : (isHovered ? FUIColors.FrameBright : FUIColors.Frame);
        var textColor = FUIColors.SecondaryColor(isActive);

        using var themeBgPaint = FUIRenderer.CreateFillPaint(bgColor);
        canvas.DrawRect(bounds, themeBgPaint);

        using var themeFramePaint = FUIRenderer.CreateStrokePaint(frameColor, isActive ? 1.5f : 1f);
        canvas.DrawRect(bounds, themeFramePaint);

        FUIRenderer.DrawTextCentered(canvas, name, bounds, textColor, 12f);

        var indicatorBounds = new SKRect(bounds.Left + 2, bounds.Bottom - 2,
            bounds.Right - 2, bounds.Bottom - 1);
        using var indicatorPaint = FUIRenderer.CreateFillPaint(previewColor.WithAlpha((byte)(isActive ? 200 : 100)));
        canvas.DrawRect(indicatorBounds, indicatorPaint);
    }

    // ─── Text Helpers ─────────────────────────────────────────────────────────

    internal static void DrawSearchBox(SKCanvas canvas, SKRect bounds, string text, bool focused, Point mousePosition,
        string placeholder = "Search actions...", IReadOnlyList<string>? captureBadges = null,
        int cursorPos = -1, int selectionStart = -1, int selectionEnd = -1)
    {
        var bgColor = focused ? FUIColors.Background2.WithAlpha(180) : FUIColors.Background2.WithAlpha(100);
        var borderColor = focused ? FUIColors.Active : FUIColors.Frame;
        FUIRenderer.DrawRoundedPanel(canvas, bounds, bgColor, borderColor, 4f);

        float iconX = bounds.Left + 8f;
        float iconY = bounds.MidY;
        using var iconPaint = FUIRenderer.CreateStrokePaint(FUIColors.TextDim, 1.5f);
        canvas.DrawCircle(iconX + 5, iconY - 1, 5f, iconPaint);
        canvas.DrawLine(iconX + 9, iconY + 3, iconX + 13, iconY + 7, iconPaint);

        float contentX = bounds.Left + 24f;
        float textY = bounds.MidY + 4f;
        const float textFontSize = 13f;

        if (string.IsNullOrEmpty(text))
        {
            FUIRenderer.DrawText(canvas, placeholder, new SKPoint(contentX, textY), FUIColors.TextDim, textFontSize);
        }
        else if (captureBadges is not null && captureBadges.Count > 0)
        {
            // Draw keycap badges matching the table — same visual language
            const float badgeH = 18f;
            const float badgePadX = 6f;
            const float badgeGap = 3f;
            const float fontSize = 12f;
            var color = FUIColors.Active;
            float x = contentX;
            float badgeY = bounds.MidY - badgeH / 2;

            for (int i = 0; i < captureBadges.Count; i++)
            {
                string label = captureBadges[i];
                float textW = FUIRenderer.MeasureText(label, fontSize);
                float badgeW = textW + badgePadX * 2;
                bool isMain = i == captureBadges.Count - 1;

                var badgeRect = new SKRect(x, badgeY, x + badgeW, badgeY + badgeH);
                byte bgAlpha = isMain ? (byte)50 : (byte)35;
                byte borderAlpha = isMain ? (byte)180 : (byte)120;
                FUIRenderer.DrawRoundedPanel(canvas, badgeRect, color.WithAlpha(bgAlpha), color.WithAlpha(borderAlpha));
                FUIRenderer.DrawText(canvas, label, new SKPoint(x + badgePadX, bounds.MidY + 4f), color, fontSize);

                x += badgeW + badgeGap;
            }

            // × always visible at right edge (same hit zone as plain text clear)
            float clearX = bounds.Right - 18f;
            float clearY = bounds.MidY;
            using var clearPaint = FUIRenderer.CreateStrokePaint(FUIColors.TextDim, 1.5f);
            canvas.DrawLine(clearX - 4, clearY - 4, clearX + 4, clearY + 4, clearPaint);
            canvas.DrawLine(clearX + 4, clearY - 4, clearX - 4, clearY + 4, clearPaint);
        }
        else
        {
            // Draw selection highlight behind text
            if (focused && selectionStart >= 0 && selectionEnd >= 0 && selectionStart != selectionEnd)
            {
                int sS = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, text.Length);
                int sE = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, text.Length);
                float selStartX = contentX + (sS > 0 ? FUIRenderer.MeasureText(text[..sS], textFontSize) : 0);
                float selEndX = contentX + FUIRenderer.MeasureText(text[..sE], textFontSize);
                var selRect = new SKRect(selStartX, bounds.Top + 4, selEndX, bounds.Bottom - 4);
                using var selPaint = FUIRenderer.CreateFillPaint(FUIColors.SelectionBgStrong);
                canvas.DrawRect(selRect, selPaint);
            }

            FUIRenderer.DrawText(canvas, text, new SKPoint(contentX, textY), FUIColors.TextPrimary, textFontSize);

            // × clear button — always visible when text is present
            float clearX = bounds.Right - 18f;
            float clearY = bounds.MidY;
            using var clearPaint = FUIRenderer.CreateStrokePaint(FUIColors.TextDim, 1.5f);
            canvas.DrawLine(clearX - 4, clearY - 4, clearX + 4, clearY + 4, clearPaint);
            canvas.DrawLine(clearX + 4, clearY - 4, clearX - 4, clearY + 4, clearPaint);
        }

        if (focused)
        {
            int cPos = cursorPos >= 0 ? Math.Clamp(cursorPos, 0, text?.Length ?? 0) : (text?.Length ?? 0);
            float cursorX = contentX + (string.IsNullOrEmpty(text) || cPos == 0 ? 0 : FUIRenderer.MeasureText(text[..cPos], textFontSize));
            if ((DateTime.Now.Millisecond / 500) % 2 == 0)
            {
                using var cursorPaint = FUIRenderer.CreateStrokePaint(FUIColors.Active);
                canvas.DrawLine(cursorX, bounds.Top + 5, cursorX, bounds.Bottom - 5, cursorPaint);
            }
        }
    }

    internal static void DrawVerticalSideTab(SKCanvas canvas, SKRect bounds, string label, bool isSelected, bool isHovered)
    {
        if (isSelected)
        {
            using var accentPaint = FUIRenderer.CreateStrokePaint(FUIColors.Active, 3f);
            canvas.DrawLine(bounds.Right - 1, bounds.Top + 5, bounds.Right - 1, bounds.Bottom - 5, accentPaint);

            using var glowPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.Active.WithAlpha(FUIColors.AlphaGlow),
                StrokeWidth = 8f,
                IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f)
            };
            canvas.DrawLine(bounds.Right - 1, bounds.Top + 5, bounds.Right - 1, bounds.Bottom - 5, glowPaint);
        }

        canvas.Save();
        canvas.Translate(bounds.MidX - 2, bounds.MidY);
        canvas.RotateDegrees(-90);

        var textColor = isSelected ? FUIColors.Active : (isHovered ? FUIColors.TextBright : FUIColors.TextDim.WithAlpha(150));
        using var textPaint = new SKPaint
        {
            Color = textColor,
            TextSize = 13f,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
            TextAlign = SKTextAlign.Center
        };
        canvas.DrawText(label, 0, 4f, textPaint);
        canvas.Restore();
    }

    /// <summary>
    /// Draws the network mode indicator in the status bar.
    /// Does nothing when <paramref name="networkEnabled"/> is false.
    /// Local = dim gray circle; Remote = filled Active circle with outer ring.
    /// </summary>
    internal static void DrawNetworkModeIndicator(
        SKCanvas canvas, float centerX, float midY,
        Services.Abstractions.NetworkInputMode mode, bool networkEnabled)
    {
        if (!networkEnabled) return;

        const float r = 3.5f;

        if (mode == Services.Abstractions.NetworkInputMode.Remote)
        {
            // Master sending — filled green circle + outer ring
            using var fill = FUIRenderer.CreateFillPaint(FUIColors.Active);
            canvas.DrawCircle(centerX, midY, r, fill);
            using var ring = FUIRenderer.CreateStrokePaint(FUIColors.Active.WithAlpha(FUIColors.AlphaBorderSoft), 1f);
            canvas.DrawCircle(centerX, midY, r + 2.5f, ring);
        }
        else if (mode == Services.Abstractions.NetworkInputMode.Receiving)
        {
            // Client receiving — green ring only (no fill)
            using var ring = FUIRenderer.CreateStrokePaint(FUIColors.Active.WithAlpha(FUIColors.AlphaBorderSoft), 1.5f);
            canvas.DrawCircle(centerX, midY, r, ring);
        }
        else
        {
            // Local / not connected — dim grey dot
            using var fill = FUIRenderer.CreateFillPaint(FUIColors.FrameDim.WithAlpha(FUIColors.AlphaBorderSoft));
            canvas.DrawCircle(centerX, midY, r, fill);
        }
    }

    // ─── FUI Folder Icon ──────────────────────────────────────────────────────

    /// <summary>
    /// Draws a futuristic folder icon centred in the given bounds.
    /// Chamfered body, tab notch with circle nodes, diagonal hatching, inner parallel line.
    /// </summary>
    internal static void DrawFUIFolderIcon(SKCanvas canvas, SKRect bounds, SKColor strokeColor, SKColor accentColor)
    {
        float w = bounds.Width;
        float h = bounds.Height;
        float x = bounds.Left;
        float y = bounds.Top;

        // Key proportions (relative to bounds)
        float chamfer = Math.Min(w, h) * 0.10f;   // corner chamfer size
        float tabW    = w * 0.38f;                  // tab width
        float tabH    = h * 0.16f;                  // tab height above body
        float nodeR   = Math.Min(w, h) * 0.045f;   // circle node radius

        // Body top-left Y (below tab)
        float bodyTop = y + tabH;

        // ── Outer body path (chamfered rectangle with tab notch) ──
        using var bodyPath = new SKPath();

        // Start at top-left of tab (with small chamfer)
        float tabChamfer = chamfer * 0.6f;
        bodyPath.MoveTo(x + tabChamfer, y);

        // Tab top edge → tab right corner with step-down
        bodyPath.LineTo(x + tabW - tabChamfer, y);
        bodyPath.LineTo(x + tabW, y + tabChamfer);

        // Tab step-down to body level (notch)
        float notchW = w * 0.06f;
        bodyPath.LineTo(x + tabW + notchW, bodyTop);

        // Body top edge → top-right chamfer
        bodyPath.LineTo(x + w - chamfer, bodyTop);
        bodyPath.LineTo(x + w, bodyTop + chamfer);

        // Right edge → bottom-right chamfer
        bodyPath.LineTo(x + w, y + h - chamfer);
        bodyPath.LineTo(x + w - chamfer, y + h);

        // Bottom edge → bottom-left chamfer
        bodyPath.LineTo(x + chamfer, y + h);
        bodyPath.LineTo(x, y + h - chamfer);

        // Left edge back up → top-left tab chamfer
        bodyPath.LineTo(x, y + tabChamfer);
        bodyPath.Close();

        using var strokePaint = FUIRenderer.CreateStrokePaint(strokeColor, 1.2f);
        canvas.DrawPath(bodyPath, strokePaint);

        // ── Circle nodes at tab junction ──
        float node1X = x + tabW;
        float node1Y = y + tabChamfer;
        float node2X = x + tabW + notchW * 0.5f;
        float node2Y = bodyTop - (bodyTop - y - tabChamfer) * 0.4f;

        using var nodePaint = FUIRenderer.CreateStrokePaint(accentColor, 1.0f);
        canvas.DrawCircle(node1X, node1Y, nodeR, nodePaint);
        canvas.DrawCircle(node2X, node2Y, nodeR, nodePaint);

        // ── Inner parallel line (left + bottom edge, inset) ──
        float inset = Math.Max(2.5f, Math.Min(w, h) * 0.06f);
        using var innerPath = new SKPath();
        float innerChamfer = chamfer * 0.7f;

        // Left edge inner line (from partway down to bottom-left chamfer, then along bottom)
        float innerStartY = bodyTop + h * 0.15f;
        innerPath.MoveTo(x + inset, innerStartY);
        innerPath.LineTo(x + inset, y + h - innerChamfer - inset);
        innerPath.LineTo(x + inset + innerChamfer, y + h - inset);
        innerPath.LineTo(x + w * 0.45f, y + h - inset);

        using var innerPaint = FUIRenderer.CreateStrokePaint(strokeColor.WithAlpha(120), 1.0f);
        canvas.DrawPath(innerPath, innerPaint);

        // ── Diagonal hatching strip (left side of body) ──
        float hatchX = x + inset + 1f;
        float hatchW = w * 0.08f;
        float hatchTop = bodyTop + h * 0.22f;
        float hatchBot = y + h - inset - innerChamfer - 2f;
        float hatchStep = Math.Max(3f, h * 0.06f);

        using var hatchPaint = FUIRenderer.CreateStrokePaint(accentColor.WithAlpha(140), 0.9f);
        for (float hy = hatchTop; hy < hatchBot; hy += hatchStep)
        {
            float hy2 = Math.Min(hy + hatchStep * 0.6f, hatchBot);
            canvas.DrawLine(hatchX, hy2, hatchX + hatchW, hy, hatchPaint);
        }
    }

    // ─── Collapsible Panel ────────────────────────────────────────────────────

    /// <summary>
    /// Draws a collapsible panel header with title and expand/collapse indicator.
    /// Sets <paramref name="headerBounds"/> for click hit-testing by the caller.
    /// Returns the panel chrome metrics so the caller can draw content below.
    /// </summary>
    /// <param name="canvas">Target canvas.</param>
    /// <param name="bounds">Full panel bounds (collapsed or expanded).</param>
    /// <param name="title">Panel title text.</param>
    /// <param name="isExpanded">Whether the panel content is visible.</param>
    /// <param name="isHovered">Whether the header is currently hovered (collapsed state only).</param>
    /// <param name="headerBounds">Output: the clickable header area for hit-testing.</param>
    /// <returns>Panel chrome metrics (LeftMargin, RightMargin, Y after title).</returns>
    internal static FUIRenderer.PanelMetrics DrawCollapsiblePanelHeader(
        SKCanvas canvas,
        SKRect bounds,
        string title,
        bool isExpanded,
        bool isHovered,
        out SKRect headerBounds)
    {
        float cornerLen = isExpanded ? 30f : Math.Min(16f, bounds.Height * 0.35f);
        var m = FUIRenderer.DrawPanelChrome(canvas, bounds, cornerLength: cornerLen);
        float y = m.Y;

        headerBounds = new SKRect(bounds.Left, bounds.Top, bounds.Right,
            bounds.Top + FUIRenderer.PanelHeaderHeight);

        DrawPanelTitle(canvas, m.LeftMargin, m.RightMargin, ref y, title);

        // Expand/collapse indicator
        string indicator = isExpanded ? "-" : "+";
        float indW = FUIRenderer.MeasureText(indicator, 13f);
        var indColour = isHovered && !isExpanded
            ? FUIColors.TextBright
            : FUIColors.Active.WithAlpha(isExpanded ? (byte)100 : (byte)180);
        FUIRenderer.DrawText(canvas, indicator, new SKPoint(m.RightMargin - indW, y - 18f),
            indColour, 13f, true);

        return new FUIRenderer.PanelMetrics
        {
            LeftMargin = m.LeftMargin,
            RightMargin = m.RightMargin,
            ContentWidth = m.ContentWidth,
            RowHeight = m.RowHeight,
            SectionSpacing = m.SectionSpacing,
            Y = y
        };
    }

    // ─── Scrollbar ────────────────────────────────────────────────────────────

}