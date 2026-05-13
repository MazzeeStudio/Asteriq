using Asteriq.Models;
using SkiaSharp;

namespace Asteriq.UI;

internal static partial class FUIWidgets
{
    internal static void DrawDeviceListItem(SKCanvas canvas, float x, float y, float width,
        string name, string status, bool isSelected, bool isHovered, string vjoyAssignment = "")
    {
        var itemBounds = new SKRect(x, y, x + width, y + 60);
        bool isDisconnected = status == "DISCONNECTED";

        // Selection/hover background
        if (isSelected || isHovered)
        {
            var bgColor = isSelected
                ? (isDisconnected ? FUIColors.Danger.WithAlpha(20) : FUIColors.ActiveLight)
                : FUIColors.Primary.WithAlpha(15);
            FUIRenderer.FillFrame(canvas, itemBounds, bgColor, 6f);
        }

        // Item frame
        var frameColor = isSelected
            ? (isDisconnected ? FUIColors.Danger : FUIColors.Active)
            : (isHovered ? FUIColors.FrameBright : FUIColors.FrameDim);
        FUIRenderer.DrawFrame(canvas, itemBounds, frameColor, 6f, isSelected ? 1.5f : 1f, isSelected);

        // Status indicator dot
        var statusColor = isDisconnected ? FUIColors.Danger : FUIColors.Active;
        FUIRenderer.DrawGlowingDot(canvas, new SKPoint(x + 18, y + 22), statusColor, 4f,
            isDisconnected ? 4f : 8f);

        // Device name (truncate if needed) - dim for disconnected
        string displayName = name.Length > 28 ? string.Concat(name.AsSpan(0, 25), "...") : name;
        var nameColor = isDisconnected
            ? FUIColors.TextDim
            : (isSelected ? FUIColors.TextBright : FUIColors.TextPrimary);
        FUIRenderer.DrawText(canvas, displayName, new SKPoint(x + 36, y + 24), nameColor, 16f, isSelected && !isDisconnected);

        // Status text
        var statusTextColor = isDisconnected ? FUIColors.Danger : FUIColors.Active;
        FUIRenderer.DrawText(canvas, status, new SKPoint(x + 36, y + 44), statusTextColor, 14f);

        // vJoy assignment indicator
        if (!isDisconnected && !string.IsNullOrEmpty(vjoyAssignment))
        {
            FUIRenderer.DrawText(canvas, vjoyAssignment, new SKPoint(x + width - 85, y + 45),
                FUIColors.TextDim, 14f);
        }

        // Selection bar — 6px wide pill on the right edge
        if (isSelected)
        {
            var barColor = isDisconnected ? FUIColors.Danger : FUIColors.Active;
            var barRect = new SKRect(x + width - 12, y + 6, x + width - 6, y + 50);
            using var barPaint = FUIRenderer.CreateFillPaint(barColor);
            canvas.DrawRoundRect(barRect, 3f, 3f, barPaint);
        }
    }

    // ─── Forwarding / Status ──────────────────────────────────────────────────


    internal static void DrawStatusItem(SKCanvas canvas, float x, float y, float width, string label, string value, SKColor valueColor, float fontSize = 14f)
    {
        float textOffsetY = fontSize <= 12f ? 9f : 12f;
        FUIRenderer.DrawText(canvas, label, new SKPoint(x, y + textOffsetY), FUIColors.TextDim, fontSize);

        var dotColor = valueColor == FUIColors.Active ? valueColor : FUIColors.Primary.WithAlpha(100);
        float dotX = x + width - 110;
        FUIRenderer.DrawGlowingDot(canvas, new SKPoint(dotX, y + (fontSize <= 12f ? 6f : 8f)), dotColor, 2f, 4f);

        float textStartX = dotX + 10;
        float rightEdge = x + width;
        float maxValueWidth = rightEdge - textStartX - 8;

        using var measurePaint = FUIRenderer.CreateTextPaint(valueColor, fontSize);
        string displayValue = value;
        float textWidth = measurePaint.MeasureText(displayValue);

        if (textWidth > maxValueWidth)
        {
            while (displayValue.Length > 1 && measurePaint.MeasureText(displayValue + "…") > maxValueWidth)
            {
                displayValue = displayValue.Substring(0, displayValue.Length - 1);
            }
            displayValue += "…";
        }

        FUIRenderer.DrawText(canvas, displayValue, new SKPoint(textStartX, y + textOffsetY), valueColor, fontSize);
    }

    internal static void DrawLayerIndicator(SKCanvas canvas, float x, float y, float width, string name, bool isActive)
    {
        float height = 22f;
        var bounds = new SKRect(x, y, x + width, y + height);
        var frameColor = isActive ? FUIColors.Active : FUIColors.FrameDim;
        var fillColor = isActive ? FUIColors.SelectionBg : SKColors.Transparent;

        FUIRenderer.FillFrame(canvas, bounds, fillColor, 4f);
        FUIRenderer.DrawFrame(canvas, bounds, frameColor, 4f, 1f, isActive);

        var textColor = FUIColors.SecondaryColor(isActive);
        FUIRenderer.DrawTextCentered(canvas, name, bounds, textColor, 13f, isActive);
    }

    internal static void DrawJoystickOutlineFallback(SKCanvas canvas, SKRect bounds)
    {
        using var outlinePaint = FUIRenderer.CreateStrokePaint(FUIColors.Primary.WithAlpha(60), 1.5f);

        float centerX = bounds.MidX;
        float stickWidth = 36f;
        float baseWidth = 70f;

        canvas.DrawLine(centerX, bounds.Top + 36, centerX, bounds.Bottom - 56, outlinePaint);

        var gripRect = new SKRect(centerX - stickWidth / 2, bounds.Top + 24,
                                   centerX + stickWidth / 2, bounds.Top + 84);
        canvas.DrawRoundRect(gripRect, 8, 8, outlinePaint);

        var baseRect = new SKRect(centerX - baseWidth / 2, bounds.Bottom - 65,
                                   centerX + baseWidth / 2, bounds.Bottom - 30);
        canvas.DrawRoundRect(baseRect, 4, 4, outlinePaint);

        canvas.DrawCircle(centerX, bounds.Top + 45, 7, outlinePaint);

        var triggerRect = new SKRect(centerX + stickWidth / 2 - 4, bounds.Top + 65,
                                      centerX + stickWidth / 2 + 12, bounds.Top + 82);
        canvas.DrawRect(triggerRect, outlinePaint);

        canvas.DrawCircle(centerX - stickWidth / 2 - 8, bounds.Top + 55, 5, outlinePaint);
        canvas.DrawCircle(centerX - stickWidth / 2 - 8, bounds.Top + 70, 5, outlinePaint);
    }

    // ─── Settings Widgets ─────────────────────────────────────────────────────

    internal static void DrawShiftLayersSection(SKCanvas canvas, float leftMargin, float rightMargin, float y, float bottom, MappingProfile profile)
    {
        float lineHeight = 16f;

        FUIRenderer.DrawText(canvas, "SHIFT LAYERS", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        y += lineHeight;

        FUIRenderer.DrawText(canvas, "[Coming soon] Hold a button to activate alternative mappings", new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
        y += lineHeight + 4;

        float layerRowHeight = FUIRenderer.TouchTargetStandard;
        // CA2000: using var inside foreach is safe — analyzer false positive
#pragma warning disable CA2000
        foreach (var layer in profile.ShiftLayers)
        {
            if (y + layerRowHeight > bottom - 50) break;

            var rowBounds = new SKRect(leftMargin, y, rightMargin, y + layerRowHeight - 4);
            FUIRenderer.DrawRoundedPanel(canvas, rowBounds, FUIColors.Background2, FUIColors.Frame, 4f);

            FUIRenderer.DrawText(canvas, layer.Name, new SKPoint(leftMargin + 10, y + 11), FUIColors.TextPrimary, 14f);

            string activatorText = layer.ActivatorButton is not null
                ? $"Button {layer.ActivatorButton.Index + 1} on {layer.ActivatorButton.DeviceName}"
                : "Not assigned";
            FUIRenderer.DrawText(canvas, activatorText, new SKPoint(leftMargin + 100, y + 11),
                layer.ActivatorButton is not null ? FUIColors.TextDim : FUIColors.Warning.WithAlpha(150), 12f);

            float delSize = 20f;
            var delBounds = new SKRect(rightMargin - delSize - 8, y + (layerRowHeight - delSize) / 2 - 2,
                rightMargin - 8, y + (layerRowHeight + delSize) / 2 - 2);

            using var delPaint = FUIRenderer.CreateFillPaint(FUIColors.Danger.WithAlpha(60));
            canvas.DrawRoundRect(delBounds, 2, 2, delPaint);
            FUIRenderer.DrawTextCentered(canvas, "x", delBounds, FUIColors.Danger, 15f);

            y += layerRowHeight;
        }
#pragma warning restore CA2000

        if (y + 36 < bottom)
        {
            var addBounds = new SKRect(leftMargin, y, rightMargin, y + 30);
            FUIRenderer.DrawRoundedPanel(canvas, addBounds, FUIColors.Success.WithAlpha(20), FUIColors.Success.WithAlpha(100), 4f);

            FUIRenderer.DrawTextCentered(canvas, "+ Add Shift Layer", addBounds, FUIColors.Success, 14f);
        }

    }

    internal static void DrawKeycapsInBounds(SKCanvas canvas, SKRect bounds, string keyName, List<string>? modifiers)
    {
        var parts = new List<string>();
        if (modifiers is not null && modifiers.Count > 0)
            parts.AddRange(modifiers);
        parts.Add(keyName);

        float keycapHeight = 20f;
        float keycapGap = 4f;
        float keycapPadding = 8f;
        float fontSize = 13f;
        float scaledFontSize = fontSize;

        using var measurePaint = new SKPaint
        {
            TextSize = scaledFontSize,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal)
        };

        float totalWidth = 0;
        var keycapWidths = new List<float>();
        foreach (var part in parts)
        {
            float textWidth = measurePaint.MeasureText(part.ToUpperInvariant());
            float keycapWidth = textWidth + keycapPadding * 2;
            keycapWidths.Add(keycapWidth);
            totalWidth += keycapWidth;
        }
        totalWidth += (parts.Count - 1) * keycapGap;

        float startX = bounds.MidX - totalWidth / 2;
        float keycapTop = bounds.MidY - keycapHeight / 2;

        for (int i = 0; i < parts.Count; i++)
        {
            string keyText = parts[i].ToUpperInvariant();
            float keycapWidth = keycapWidths[i];
            var keycapBounds = new SKRect(startX, keycapTop, startX + keycapWidth, keycapTop + keycapHeight);

            FUIRenderer.DrawRoundedPanel(canvas, keycapBounds, FUIColors.TextPrimary.WithAlpha(25), FUIColors.TextPrimary.WithAlpha(150));

            float textX = startX + keycapPadding;
            float textY = keycapBounds.MidY + scaledFontSize / 3;
            using var textPaint = new SKPaint
            {
                Color = FUIColors.TextPrimary,
                TextSize = scaledFontSize,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal)
            };
            canvas.DrawText(keyText, textX, textY, textPaint);

            startX += keycapWidth + keycapGap;
        }
    }

    internal static void DrawMappingItem(SKCanvas canvas, SKRect bounds, string source, string target, string type, bool enabled)
    {
        using var bgPaint = FUIRenderer.CreateFillPaint(enabled ? FUIColors.Background2.WithAlpha(100) : FUIColors.Background1.WithAlpha(80));
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = FUIRenderer.CreateStrokePaint(enabled ? FUIColors.Frame : FUIColors.FrameDim);
        canvas.DrawRect(bounds, framePaint);

        var typeColor = type == "BUTTON" ? FUIColors.Active : FUIColors.Primary;
        FUIRenderer.DrawText(canvas, type, new SKPoint(bounds.Left + 10, bounds.Top + 18),
            enabled ? typeColor : typeColor.WithAlpha(100), 13f);

        FUIRenderer.DrawText(canvas, source, new SKPoint(bounds.Left + 80, bounds.Top + 18),
            enabled ? FUIColors.TextPrimary : FUIColors.TextDim, 15f);

        FUIRenderer.DrawText(canvas, "->", new SKPoint(bounds.Left + 80, bounds.Top + 36),
            FUIColors.TextDim, 14f);

        FUIRenderer.DrawText(canvas, target, new SKPoint(bounds.Left + 110, bounds.Top + 36),
            enabled ? FUIColors.TextPrimary : FUIColors.TextDim, 15f);

        var statusColor = enabled ? FUIColors.Success : FUIColors.TextDisabled;
        FUIRenderer.DrawGlowingDot(canvas, new SKPoint(bounds.Right - 20, bounds.MidY),
            statusColor, 4f, enabled ? 6f : 2f);
    }

}