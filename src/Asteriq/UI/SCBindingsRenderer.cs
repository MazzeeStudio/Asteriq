using Asteriq.Models;
using SkiaSharp;

namespace Asteriq.UI;

/// <summary>
/// Renders SC-bindings-specific primitives: type indicators, conflict badges,
/// binding badges, vJoy mapping rows, and profile dropdowns.
/// All methods are stateless — inputs are passed as parameters.
/// </summary>
internal static class SCBindingsRenderer
{
    // ─── Type Indicators ──────────────────────────────────────────────────────

    internal static void DrawInputTypeIndicator(SKCanvas canvas, float x, float centerY, SCInputType inputType, SKColor color)
    {
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color.WithAlpha(150),
            StrokeWidth = 1.2f,
            IsAntialias = true
        };

        switch (inputType)
        {
            case SCInputType.Axis:
                float arrowLen = 4f;
                canvas.DrawLine(x, centerY, x + arrowLen * 2, centerY, paint);
                canvas.DrawLine(x, centerY, x + 2, centerY - 2, paint);
                canvas.DrawLine(x, centerY, x + 2, centerY + 2, paint);
                canvas.DrawLine(x + arrowLen * 2, centerY, x + arrowLen * 2 - 2, centerY - 2, paint);
                canvas.DrawLine(x + arrowLen * 2, centerY, x + arrowLen * 2 - 2, centerY + 2, paint);
                break;

            case SCInputType.Button:
                paint.Style = SKPaintStyle.Fill;
                canvas.DrawCircle(x + 4, centerY, 3f, paint);
                break;

            case SCInputType.Hat:
                float hatSize = 3f;
                float cx = x + 4;
                canvas.DrawLine(cx, centerY - hatSize, cx, centerY + hatSize, paint);
                canvas.DrawLine(cx - hatSize, centerY, cx + hatSize, centerY, paint);
                break;
        }
    }

    internal static void DrawConflictIndicator(SKCanvas canvas, float x, float y)
    {
        float size = 8f;

        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Warning, IsAntialias = true };
        var path = new SKPath();
        path.MoveTo(x + size / 2, y);
        path.LineTo(x + size, y + size);
        path.LineTo(x, y + size);
        path.Close();
        canvas.DrawPath(path, fillPaint);

        using var textPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.White,
            IsAntialias = true,
            TextSize = 10f
        };
        canvas.DrawText("!", x + size / 2 - 1.5f, y + size - 1.5f, textPaint);
    }

    // ─── Binding Badges ───────────────────────────────────────────────────────

    internal static void DrawBindingBadge(SKCanvas canvas, float x, float y, float maxWidth,
        string text, SKColor color, bool isDefault, SCInputType? inputType = null)
    {
        float fontSize = 12f;
        float textWidth = FUIRenderer.MeasureText(text, fontSize);
        float indicatorWidth = inputType.HasValue ? 14f : 0f;
        float badgeWidth = Math.Min(maxWidth - 2, textWidth + indicatorWidth + 10);
        float badgeHeight = 16f;

        var badgeBounds = new SKRect(x, y, x + badgeWidth, y + badgeHeight);

        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = isDefault ? FUIColors.Background2.WithAlpha(180) : color.WithAlpha(40),
            IsAntialias = true
        };
        canvas.DrawRoundRect(badgeBounds, 3f, 3f, bgPaint);

        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color.WithAlpha(isDefault ? (byte)100 : (byte)180),
            StrokeWidth = 1f,
            IsAntialias = true
        };
        canvas.DrawRoundRect(badgeBounds, 3f, 3f, borderPaint);

        float textX = x + 5;
        if (inputType.HasValue)
        {
            DrawInputTypeIndicator(canvas, x + 4, y + badgeHeight / 2, inputType.Value, color);
            textX = x + indicatorWidth + 2;
        }

        string displayText = text;
        float availableTextWidth = badgeWidth - (textX - x) - 5;
        if (textWidth > availableTextWidth)
            displayText = FUIWidgets.TruncateTextToWidth(text, availableTextWidth - 4, fontSize);

        FUIRenderer.DrawText(canvas, displayText, new SKPoint(textX, y + badgeHeight / 2 + 3), color, fontSize);
    }

    internal static void DrawBindingBadgeCentered(SKCanvas canvas, SKRect cellBounds,
        string text, SKColor color, bool isDefault, SCInputType? inputType = null)
    {
        float fontSize = 14f;
        float textWidth = FUIRenderer.MeasureText(text, fontSize);
        float indicatorWidth = inputType.HasValue ? 14f : 0f;
        float padding = 8f;
        float badgeWidth = Math.Min(cellBounds.Width - 6, textWidth + indicatorWidth + padding * 2);
        float badgeHeight = 20f;

        float badgeX = cellBounds.Left + (cellBounds.Width - badgeWidth) / 2;
        float badgeY = cellBounds.Top + (cellBounds.Height - badgeHeight) / 2;
        var badgeBounds = new SKRect(badgeX, badgeY, badgeX + badgeWidth, badgeY + badgeHeight);

        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = isDefault ? FUIColors.Background2.WithAlpha(180) : color.WithAlpha(40),
            IsAntialias = true
        };
        canvas.DrawRoundRect(badgeBounds, 4f, 4f, bgPaint);

        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color.WithAlpha(isDefault ? (byte)100 : (byte)180),
            StrokeWidth = 1f,
            IsAntialias = true
        };
        canvas.DrawRoundRect(badgeBounds, 4f, 4f, borderPaint);

        float textX = badgeX + padding;
        if (inputType.HasValue)
        {
            DrawInputTypeIndicator(canvas, badgeX + 4, badgeY + badgeHeight / 2, inputType.Value, color);
            textX = badgeX + indicatorWidth + 4;
        }

        string displayText = text;
        float availableTextWidth = badgeWidth - (textX - badgeX) - padding;
        if (textWidth > availableTextWidth)
            displayText = FUIWidgets.TruncateTextToWidth(text, availableTextWidth - 4, fontSize);

        float textY = badgeY + badgeHeight / 2 + 4;
        FUIRenderer.DrawText(canvas, displayText, new SKPoint(textX, textY), color, fontSize);
    }

    // ─── Multi-Keycap ─────────────────────────────────────────────────────────

    internal static float MeasureMultiKeycapWidth(List<string> components, SCInputType? inputType)
    {
        float fontSize = 13f;
        float badgePadding = 8f;
        float gap = 3f;
        float totalWidth = 0f;
        for (int i = 0; i < components.Count; i++)
        {
            float textWidth = FUIRenderer.MeasureText(components[i], fontSize);
            float indicatorSpace = (i == components.Count - 1 && inputType.HasValue) ? 12f : 0f;
            totalWidth += textWidth + badgePadding * 2 + indicatorSpace;
            if (i > 0) totalWidth += gap;
        }
        return totalWidth;
    }

    internal static void DrawMultiKeycapBinding(SKCanvas canvas, SKRect cellBounds,
        List<string> components, SKColor color, SCInputType? inputType)
    {
        if (components.Count == 0) return;

        float fontSize = 13f;
        float badgeHeight = 18f;
        float badgePadding = 8f;
        float gap = 3f;

        float totalWidth = 0f;
        var badgeWidths = new float[components.Count];
        for (int i = 0; i < components.Count; i++)
        {
            float textWidth = FUIRenderer.MeasureText(components[i], fontSize);
            float indicatorSpace = (i == components.Count - 1 && inputType.HasValue) ? 12f : 0f;
            badgeWidths[i] = textWidth + badgePadding * 2 + indicatorSpace;
            totalWidth += badgeWidths[i];
            if (i > 0) totalWidth += gap;
        }

        float startX = cellBounds.Left + (cellBounds.Width - totalWidth) / 2;
        float badgeY = cellBounds.Top + (cellBounds.Height - badgeHeight) / 2;
        float currentX = startX;

        for (int i = 0; i < components.Count; i++)
        {
            var comp = components[i];
            float badgeWidth = badgeWidths[i];
            bool isMainKey = i == components.Count - 1;

            var badgeBounds = new SKRect(currentX, badgeY, currentX + badgeWidth, badgeY + badgeHeight);

            byte bgAlpha = isMainKey ? (byte)50 : (byte)35;
            using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = color.WithAlpha(bgAlpha), IsAntialias = true };
            canvas.DrawRoundRect(badgeBounds, 3f, 3f, bgPaint);

            byte borderAlpha = isMainKey ? (byte)180 : (byte)120;
            using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = color.WithAlpha(borderAlpha), StrokeWidth = 1f, IsAntialias = true };
            canvas.DrawRoundRect(badgeBounds, 3f, 3f, borderPaint);

            float textX = currentX + badgePadding;
            if (isMainKey && inputType.HasValue)
            {
                DrawInputTypeIndicator(canvas, currentX + 4, badgeY + badgeHeight / 2, inputType.Value, color);
                textX = currentX + 14f;
            }

            float textY = badgeY + badgeHeight / 2 + 3.5f;
            var textColor = isMainKey ? color : color.WithAlpha(200);
            FUIRenderer.DrawText(canvas, comp, new SKPoint(textX, textY), textColor, fontSize);

            currentX += badgeWidth + gap;
        }
    }

    // ─── Profile Dropdowns ────────────────────────────────────────────────────

    internal static void DrawSCProfileDropdown(SKCanvas canvas, SKRect bounds, string text, bool hovered, bool open)
    {
        var bgColor = open ? FUIColors.Active.WithAlpha(40) : (hovered ? FUIColors.Background2.WithAlpha(180) : FUIColors.Background2.WithAlpha(120));
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, bgPaint);

        var borderColor = open ? FUIColors.Active : (hovered ? FUIColors.FrameBright : FUIColors.Frame);
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = borderColor, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, borderPaint);

        string displayText = text.Length > 18 ? text.Substring(0, 15) + "..." : text;
        FUIRenderer.DrawText(canvas, displayText, new SKPoint(bounds.Left + 6, bounds.MidY + 4f), FUIColors.TextPrimary, 13f);

        float arrowX = bounds.Right - 14f;
        float arrowY = bounds.MidY;
        using var arrowPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.TextDim, IsAntialias = true };
        using var arrowPath = new SKPath();
        arrowPath.MoveTo(arrowX - 4, arrowY - 2);
        arrowPath.LineTo(arrowX + 4, arrowY - 2);
        arrowPath.LineTo(arrowX, arrowY + 3);
        arrowPath.Close();
        canvas.DrawPath(arrowPath, arrowPaint);
    }

    internal static void DrawSCProfileDropdownWide(SKCanvas canvas, SKRect bounds, string text, bool hovered, bool open)
    {
        var bgColor = open ? FUIColors.Active.WithAlpha(40) : (hovered ? FUIColors.Background2.WithAlpha(180) : FUIColors.Background2.WithAlpha(120));
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, bgPaint);

        var borderColor = open ? FUIColors.Active : (hovered ? FUIColors.FrameBright : FUIColors.Frame);
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = borderColor, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, borderPaint);

        float maxTextWidth = bounds.Width - 30f;
        string displayText = text;
        if (FUIRenderer.MeasureText(text, 13f) > maxTextWidth)
        {
            int len = text.Length;
            while (len > 0 && FUIRenderer.MeasureText(text.Substring(0, len) + "...", 13f) > maxTextWidth)
                len--;
            displayText = len > 0 ? text.Substring(0, len) + "..." : "...";
        }
        FUIRenderer.DrawText(canvas, displayText, new SKPoint(bounds.Left + 8, bounds.MidY + 4f), FUIColors.TextPrimary, 13f);

        float arrowX = bounds.Right - 14f;
        float arrowY = bounds.MidY;
        using var arrowPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.TextDim, IsAntialias = true };
        using var arrowPath = new SKPath();
        arrowPath.MoveTo(arrowX - 4, arrowY - 2);
        arrowPath.LineTo(arrowX + 4, arrowY - 2);
        arrowPath.LineTo(arrowX, arrowY + 3);
        arrowPath.Close();
        canvas.DrawPath(arrowPath, arrowPaint);
    }

    internal static void DrawSCProfileButton(SKCanvas canvas, SKRect bounds, string icon, bool hovered, string tooltip, bool disabled = false)
    {
        SKColor bgColor;
        if (disabled)
            bgColor = FUIColors.Background2.WithAlpha(60);
        else if (hovered)
            bgColor = FUIColors.Active.WithAlpha(80);
        else
            bgColor = FUIColors.Background2.WithAlpha(120);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, bgPaint);

        var borderColor = disabled ? FUIColors.Frame.WithAlpha(80) : (hovered ? FUIColors.Active : FUIColors.Frame);
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = borderColor, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, borderPaint);

        var iconColor = disabled ? FUIColors.TextDim.WithAlpha(100) : (hovered ? FUIColors.TextBright : FUIColors.TextPrimary);

        if (icon == "💾" || tooltip == "Save")
        {
            float cx = bounds.MidX;
            float cy = bounds.MidY;
            using var iconPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = iconColor, StrokeWidth = 1.5f, IsAntialias = true };
            canvas.DrawRect(cx - 5, cy - 5, 10, 10, iconPaint);
            canvas.DrawLine(cx - 2, cy - 5, cx - 2, cy - 2, iconPaint);
            canvas.DrawLine(cx + 2, cy - 5, cx + 2, cy - 2, iconPaint);
        }
        else if (icon == "+")
        {
            float cx = bounds.MidX;
            float cy = bounds.MidY;
            using var iconPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = iconColor, StrokeWidth = 2f, IsAntialias = true };
            canvas.DrawLine(cx - 5, cy, cx + 5, cy, iconPaint);
            canvas.DrawLine(cx, cy - 5, cx, cy + 5, iconPaint);
        }
        else if (icon == "×")
        {
            float cx = bounds.MidX;
            float cy = bounds.MidY;
            using var iconPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = iconColor, StrokeWidth = 2f, IsAntialias = true };
            canvas.DrawLine(cx - 4, cy - 4, cx + 4, cy + 4, iconPaint);
            canvas.DrawLine(cx + 4, cy - 4, cx - 4, cy + 4, iconPaint);
        }
        else
        {
            FUIRenderer.DrawTextCentered(canvas, icon, bounds, iconColor, 15f);
        }
    }

    // ─── vJoy Mapping Rows ────────────────────────────────────────────────────

    internal static void DrawVJoyMappingRow(SKCanvas canvas, SKRect bounds, uint vjoyId, int scInstance, bool isHovered)
    {
        var bgColor = isHovered ? FUIColors.Background2.WithAlpha(150) : FUIColors.Background1.WithAlpha(80);
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        canvas.DrawRect(bounds, bgPaint);

        if (isHovered)
        {
            using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active.WithAlpha(150), StrokeWidth = 1f, IsAntialias = true };
            canvas.DrawRect(bounds, borderPaint);
        }

        float textY = bounds.MidY + 4;
        FUIRenderer.DrawText(canvas, $"vJoy {vjoyId}", new SKPoint(bounds.Left + 10, textY), FUIColors.TextPrimary, 14f);
        FUIRenderer.DrawText(canvas, "→", new SKPoint(bounds.Left + 80, textY), FUIColors.TextDim, 14f);
        FUIRenderer.DrawText(canvas, $"js{scInstance}", new SKPoint(bounds.Left + 110, textY), FUIColors.Active, 14f, true);

        if (isHovered)
            FUIRenderer.DrawText(canvas, "click to change", new SKPoint(bounds.Right - 90, textY), FUIColors.TextDim, 12f);
    }

    internal static void DrawVJoyMappingRowCompact(SKCanvas canvas, SKRect bounds, uint vjoyId, int scInstance, bool isHovered)
    {
        if (isHovered)
        {
            using var hoverPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2.WithAlpha(100), IsAntialias = true };
            canvas.DrawRect(bounds, hoverPaint);
        }

        float textY = bounds.MidY + 4;
        FUIRenderer.DrawText(canvas, $"vJoy {vjoyId}", new SKPoint(bounds.Left + 5, textY), FUIColors.TextPrimary, 13f);
        FUIRenderer.DrawText(canvas, "→", new SKPoint(bounds.Left + 60, textY), FUIColors.TextDim, 13f);
        FUIRenderer.DrawText(canvas, $"js{scInstance}", new SKPoint(bounds.Left + 80, textY), FUIColors.Active, 13f, true);
    }
}
