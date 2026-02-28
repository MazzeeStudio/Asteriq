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
        var bgColor = isDefault ? FUIColors.PanelBgHover : color.WithAlpha(FUIColors.AlphaHoverBg);
        var borderColor = color.WithAlpha(isDefault ? (byte)100 : (byte)180);
        FUIRenderer.DrawRoundedPanel(canvas, badgeBounds, bgColor, borderColor, radius: 3f);

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

        var bgColor = isDefault ? FUIColors.PanelBgHover : color.WithAlpha(FUIColors.AlphaHoverBg);
        var borderColor = color.WithAlpha(isDefault ? (byte)100 : (byte)180);
        FUIRenderer.DrawRoundedPanel(canvas, badgeBounds, bgColor, borderColor, radius: 4f);

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
        List<string> components, SKColor color, SCInputType? inputType,
        bool conflict = false, bool duplicate = false, bool rerouted = false)
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
            // Reroute icon: ~10px icon + 6px right padding + 2px left breathing = 18px (only on last badge)
            float rerouteSpace = (i == components.Count - 1 && rerouted) ? 18f : 0f;
            badgeWidths[i] = textWidth + badgePadding * 2 + indicatorSpace + rerouteSpace;
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
            byte borderAlpha = isMainKey ? (byte)180 : (byte)120;
            FUIRenderer.DrawRoundedPanel(canvas, badgeBounds, color.WithAlpha(bgAlpha), color.WithAlpha(borderAlpha));

            // Conflict stripe on the last badge's right interior (amber, clipped to badge shape)
            if (isMainKey && conflict)
            {
                const float stripeW = 4f;
                const float glowW   = 8f;

                canvas.Save();
                canvas.ClipRoundRect(new SKRoundRect(badgeBounds, 3f, 3f), SKClipOperation.Intersect, antialias: true);

                // Soft bloom
                using var glowPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = FUIColors.Warning.WithAlpha(60),
                    IsAntialias = true,
                    MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3f)
                };
                canvas.DrawRect(new SKRect(badgeBounds.Right - glowW, badgeBounds.Top, badgeBounds.Right, badgeBounds.Bottom), glowPaint);

                // Solid core stripe
                using var sp = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Warning.WithAlpha(230), IsAntialias = true };
                canvas.DrawRect(new SKRect(badgeBounds.Right - stripeW, badgeBounds.Top, badgeBounds.Right, badgeBounds.Bottom), sp);

                canvas.Restore();
            }

            float textX = currentX + badgePadding;
            if (isMainKey && inputType.HasValue)
            {
                float dotX = currentX + 4;
                float dotY = badgeY + badgeHeight / 2;
                SKColor dotColor = isMainKey && duplicate ? FUIColors.Danger : color;

                // Danger dot: draw bloom glow behind the indicator
                if (isMainKey && duplicate)
                {
                    using var bloom = new SKPaint
                    {
                        Style = SKPaintStyle.Fill,
                        Color = FUIColors.Danger.WithAlpha(90),
                        IsAntialias = true,
                        MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3.5f)
                    };
                    canvas.DrawCircle(dotX, dotY, 5f, bloom);
                }

                DrawInputTypeIndicator(canvas, dotX, dotY, inputType.Value, dotColor);
                textX = currentX + 14f;
            }

            // Reroute icon: circle ring + connected ">" chevron as a single path
            // 2px top/bottom padding within badge, 6px from right badge edge
            if (isMainKey && rerouted)
            {
                float iM     = badgeY + badgeHeight / 2f;
                float iR     = badgeBounds.Right - 6f;  // 6px from right edge
                const float halfH  = 4f;                // chevron half-height (8px total)
                const float chevW  = 5f;                // chevron width
                const float circR  = 1.5f;              // circle radius
                const float circGap = 2f;               // gap between circle and chevron opening

                float openX  = iR - chevW;
                float circCX = openX - circGap - circR;

                using var iconPaint = new SKPaint
                {
                    Style       = SKPaintStyle.Stroke,
                    Color       = color.WithAlpha(210),
                    StrokeWidth = 1.3f,
                    IsAntialias = true,
                    StrokeCap   = SKStrokeCap.Round,
                    StrokeJoin  = SKStrokeJoin.Round
                };

                // Chevron as a single connected path: top-arm → tip → bottom-arm
                using var chevPath = new SKPath();
                chevPath.MoveTo(openX, iM - halfH);
                chevPath.LineTo(iR, iM);
                chevPath.LineTo(openX, iM + halfH);
                canvas.DrawPath(chevPath, iconPaint);

                // Circle ring at the left (inside the chevron opening)
                canvas.DrawCircle(circCX, iM, circR, iconPaint);
            }

            float textY = badgeY + badgeHeight / 2 + 3.5f;
            var textColor = isMainKey ? color : color.WithAlpha(200);
            FUIRenderer.DrawText(canvas, comp, new SKPoint(textX, textY), textColor, fontSize);

            currentX += badgeWidth + gap;
        }
    }

    // ─── Profile Dropdowns ────────────────────────────────────────────────────

    /// <summary>
    /// Draws a profile selection dropdown. Text is truncated by pixel measurement to fit
    /// within the available width (bounds.Width - 30px for arrow clearance).
    /// </summary>
    internal static void DrawSCProfileDropdown(SKCanvas canvas, SKRect bounds, string text, bool hovered, bool open)
    {
        var bgColor = open ? FUIColors.PanelBgActive : (hovered ? FUIColors.PanelBgHover : FUIColors.PanelBgDefault);
        var borderColor = open ? FUIColors.Active : (hovered ? FUIColors.FrameBright : FUIColors.Frame);
        FUIRenderer.DrawRoundedPanel(canvas, bounds, bgColor, borderColor);

        float maxTextWidth = bounds.Width - 30f;
        string displayText = FUIWidgets.TruncateTextToWidth(text, maxTextWidth, 13f);
        FUIRenderer.DrawText(canvas, displayText, new SKPoint(bounds.Left + 8, bounds.MidY + 4f), FUIColors.TextPrimary, 13f);

        float arrowX = bounds.Right - 14f;
        float arrowY = bounds.MidY;
        using var arrowPaint = FUIRenderer.CreateFillPaint(FUIColors.TextDim);
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

        var borderColor = disabled ? FUIColors.Frame.WithAlpha(80) : (hovered ? FUIColors.Active : FUIColors.Frame);
        FUIRenderer.DrawRoundedPanel(canvas, bounds, bgColor, borderColor);

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

    // ── Text formatting helpers ──────────────────────────────────────────────

    internal static List<string> GetBindingComponents(string input, List<string>? modifiers)
    {
        var components = new List<string>();
        if (modifiers is not null)
        {
            foreach (var mod in modifiers)
            {
                var formatted = FormatModifierName(mod);
                if (!string.IsNullOrEmpty(formatted))
                    components.Add(formatted);
            }
        }
        components.Add(FormatInputName(input));
        return components;
    }

    internal static string FormatModifierName(string modifier)
    {
        if (string.IsNullOrEmpty(modifier))
            return "";

        var lower = modifier.ToLowerInvariant();
        if (lower.Contains("shift")) return "SHFT";
        if (lower.Contains("ctrl") || lower.Contains("control")) return "CTRL";
        if (lower.Contains("alt")) return "ALT";

        var cleaned = lower.TrimStart('l', 'r').ToUpperInvariant();
        if (cleaned.Length > 4)
            cleaned = cleaned.Substring(0, 4);
        return cleaned;
    }

    internal static string FormatInputName(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        if (input.StartsWith("button", StringComparison.OrdinalIgnoreCase))
        {
            var num = input.Substring(6);
            return $"Btn{num}";
        }

        if (input.StartsWith("mwheel_", StringComparison.OrdinalIgnoreCase))
        {
            var dir = input.Substring(7);
            return dir.ToLower() switch
            {
                "up" => "WhlUp",
                "down" => "WhlDn",
                _ => $"Whl{char.ToUpper(dir[0])}"
            };
        }

        if (input.StartsWith("maxis_", StringComparison.OrdinalIgnoreCase))
            return $"M{input.Substring(6).ToUpper()}";

        if (input.StartsWith("mouse", StringComparison.OrdinalIgnoreCase))
            return $"M{input.Substring(5)}";

        if (input.Length == 1)
            return input.ToUpper();

        if (input.StartsWith("hat", StringComparison.OrdinalIgnoreCase))
            return input.ToUpper().Replace("HAT", "H").Replace("_", "");

        if (input.Length == 2 && input[0] == 'r' && char.IsLetter(input[1]))
            return input.ToUpper();

        if (input.StartsWith("slider", StringComparison.OrdinalIgnoreCase))
            return $"Sl{input.Substring(6)}";

        var result = char.ToUpper(input[0]) + (input.Length > 1 ? input.Substring(1) : "");
        if (result.Length > 8)
            result = result.Substring(0, 8);
        return result;
    }
}
