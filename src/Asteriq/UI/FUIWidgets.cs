using Asteriq.Models;
using SkiaSharp;

namespace Asteriq.UI;

/// <summary>
/// Reusable FUI widget primitives shared across all tab renderers.
/// All methods are stateless — all inputs are passed as parameters.
/// Follows the same pattern as FUIRenderer for consistency.
/// </summary>
internal static class FUIWidgets
{
    // ─── Device List ──────────────────────────────────────────────────────────

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

    internal static void DrawSlider(SKCanvas canvas, SKRect bounds, float value)
    {
        using var trackPaint = FUIRenderer.CreateFillPaint(FUIColors.Background2);
        canvas.DrawRoundRect(bounds, 4, 4, trackPaint);

        float fillWidth = bounds.Width * Math.Clamp(value, 0, 1);
        var fillBounds = new SKRect(bounds.Left, bounds.Top, bounds.Left + fillWidth, bounds.Bottom);
        using var fillPaint = FUIRenderer.CreateFillPaint(FUIColors.Active);
        canvas.DrawRoundRect(fillBounds, 4, 4, fillPaint);

        float handleX = bounds.Left + fillWidth;
        float handleRadius = bounds.Height;
        using var handlePaint = FUIRenderer.CreateFillPaint(FUIColors.TextBright);
        canvas.DrawCircle(handleX, bounds.MidY, handleRadius, handlePaint);
    }

    /// <param name="mousePosition">Current mouse position for hover detection.</param>
    /// <param name="knobT">Animation position: 0 = fully off, 1 = fully on. Caller lerps this over time.</param>
    internal static void DrawToggleSwitch(SKCanvas canvas, SKRect bounds, float knobT, Point mousePosition)
    {
        knobT = Math.Clamp(knobT, 0f, 1f);

        var b = new SKRect(
            MathF.Round(bounds.Left), MathF.Round(bounds.Top),
            MathF.Round(bounds.Right), MathF.Round(bounds.Bottom));

        bool isHovered = bounds.Contains(mousePosition.X, mousePosition.Y);
        float r = b.Height / 2f;
        float knobRadius = r - 2f;
        float knobOffX = b.Left + r;
        float knobOnX = b.Right - r;
        float knobX = knobOffX + (knobOnX - knobOffX) * knobT;
        float knobY = b.MidY;

        // Soft drop shadow — lifts the toggle slightly off the background
        using var shadowPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = SKColors.Transparent,
            ImageFilter = SKImageFilter.CreateDropShadow(0, 2f, 3f, 3f, new SKColor(0, 0, 0, 100))
        };
        canvas.DrawRoundRect(b, r, r, shadowPaint);

        // --- Track: always dark — no active tint ---
        using var trackFill = FUIRenderer.CreateFillPaint(FUIColors.Background0);
        canvas.DrawRoundRect(b, r, r, trackFill);

        // Track border
        var trackBorder = isHovered ? FUIColors.Frame : FUIColors.FrameDim;
        using var borderPaint = FUIRenderer.CreateStrokePaint(trackBorder, 1f);
        canvas.DrawRoundRect(b, r, r, borderPaint);

        // --- "○" ring symbol on the left side — large white ring, fades in when ON ---
        float alphaOn = knobT;
        if (alphaOn > 0.02f)
        {
            float symX = b.Left + r;
            float symR = b.Height * 0.22f;
            using var ringPaint = FUIRenderer.CreateStrokePaint(
                FUIColors.Active.WithAlpha((byte)(alphaOn * 220)), 2f);
            canvas.DrawCircle(symX, knobY, symR, ringPaint);
        }

        // --- "–" pill on the right side — filled rounded rect, fades out when ON ---
        float alphaOff = 1f - knobT;
        if (alphaOff > 0.02f)
        {
            float symX = b.Right - r;
            float pillW = b.Height * 0.38f;
            float pillH = b.Height * 0.15f;
            var pillRect = new SKRect(symX - pillW / 2f, knobY - pillH / 2f,
                                      symX + pillW / 2f, knobY + pillH / 2f);
            using var pillPaint = FUIRenderer.CreateFillPaint(
                FUIColors.Primary.WithAlpha((byte)(alphaOff * 200)));
            canvas.DrawRoundRect(pillRect, pillH / 2f, pillH / 2f, pillPaint);
        }

        // --- Knob: near-black with radial top-left highlight for 3D raised look ---
        // Base: near-black fill
        using var knobBase = FUIRenderer.CreateFillPaint(FUIColors.Void);
        canvas.DrawCircle(knobX, knobY, knobRadius, knobBase);

        // Subtle top-left highlight simulates light source
        var highlightPt = new SKPoint(knobX - knobRadius * 0.25f, knobY - knobRadius * 0.32f);
        using var highlightPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Shader = SKShader.CreateRadialGradient(
                highlightPt, knobRadius * 0.75f,
                new SKColor[] { new SKColor(0xFF, 0xFF, 0xFF, 28), SKColors.Transparent },
                null, SKShaderTileMode.Clamp)
        };
        canvas.DrawCircle(knobX, knobY, knobRadius, highlightPaint);

        // Rim: slightly lighter edge reinforces the raised-button illusion
        using var rimPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(knobX, knobY), knobRadius,
                new SKColor[] { SKColors.Transparent, new SKColor(0x30, 0x36, 0x3C, 160) },
                new float[] { 0.78f, 1f },
                SKShaderTileMode.Clamp)
        };
        canvas.DrawCircle(knobX, knobY, knobRadius, rimPaint);

        // Knob border: always active colour
        var knobBorderColor = FUIColors.Active.WithAlpha(160);
        using var knobBorder = FUIRenderer.CreateStrokePaint(knobBorderColor, 1f);
        canvas.DrawCircle(knobX, knobY, knobRadius, knobBorder);
    }

    internal static void DrawSettingsSlider(SKCanvas canvas, SKRect bounds, int value, int maxValue)
    {
        float trackHeight = 4f;
        float trackY = bounds.MidY - trackHeight / 2;
        var trackRect = new SKRect(bounds.Left, trackY, bounds.Right, trackY + trackHeight);

        FUIRenderer.DrawRoundedPanel(canvas, trackRect, FUIColors.Background2, FUIColors.Frame, 2f);

        float fillWidth = (bounds.Width - 6) * (value / (float)maxValue);
        if (fillWidth > 0)
        {
            var fillRect = new SKRect(bounds.Left + 2, trackY + 1, bounds.Left + 2 + fillWidth, trackY + trackHeight - 1);
            using var fillPaint = FUIRenderer.CreateFillPaint(FUIColors.ActiveStrong);
            canvas.DrawRoundRect(fillRect, 1, 1, fillPaint);
        }

        float knobX = bounds.Left + 3 + (bounds.Width - 6) * (value / (float)maxValue);
        float knobRadius = 6f;
        using var knobPaint = FUIRenderer.CreateFillPaint(FUIColors.TextBright);
        canvas.DrawCircle(knobX, bounds.MidY, knobRadius, knobPaint);

        using var knobFramePaint = FUIRenderer.CreateStrokePaint(FUIColors.Active);
        canvas.DrawCircle(knobX, bounds.MidY, knobRadius, knobFramePaint);
    }

    internal static void DrawInteractiveSlider(SKCanvas canvas, SKRect bounds, float value, SKColor color, bool dragging)
    {
        FUIRenderer.DrawRoundedPanel(canvas, bounds, FUIColors.Background2, FUIColors.Frame, 4f);

        float fillWidth = bounds.Width * Math.Clamp(value, 0, 1);
        if (fillWidth > 2)
        {
            var fillBounds = new SKRect(bounds.Left + 1, bounds.Top + 1, bounds.Left + fillWidth - 1, bounds.Bottom - 1);
            using var fillPaint = FUIRenderer.CreateFillPaint(color.WithAlpha(100));
            canvas.DrawRoundRect(fillBounds, 3, 3, fillPaint);
        }

        float handleX = bounds.Left + fillWidth;
        float handleRadius = dragging ? 8f : 6f;
        using var handlePaint = FUIRenderer.CreateFillPaint(dragging ? color : FUIColors.TextPrimary);
        canvas.DrawCircle(handleX, bounds.MidY, handleRadius, handlePaint);

        using var handleStroke = FUIRenderer.CreateStrokePaint(color, 1.5f);
        canvas.DrawCircle(handleX, bounds.MidY, handleRadius, handleStroke);
    }

    internal static void DrawDurationSlider(SKCanvas canvas, SKRect bounds, float value, bool dragging)
    {
        value = Math.Clamp(value, 0f, 1f);

        FUIRenderer.DrawRoundedPanel(canvas, bounds, FUIColors.Background2, FUIColors.Frame, 4f);

        float fillWidth = bounds.Width * value;
        if (fillWidth > 2)
        {
            var fillBounds = new SKRect(bounds.Left + 1, bounds.Top + 1, bounds.Left + fillWidth - 1, bounds.Bottom - 1);
            using var fillPaint = FUIRenderer.CreateFillPaint(FUIColors.SelectionBgStrong);
            canvas.DrawRoundRect(fillBounds, 3, 3, fillPaint);
        }

        float handleX = bounds.Left + fillWidth;
        float handleRadius = dragging ? 8f : 6f;
        using var handlePaint = FUIRenderer.CreateFillPaint(FUIColors.ContentColor(dragging));
        canvas.DrawCircle(handleX, bounds.MidY, handleRadius, handlePaint);

        using var handleStroke = FUIRenderer.CreateStrokePaint(FUIColors.Active, 1.5f);
        canvas.DrawCircle(handleX, bounds.MidY, handleRadius, handleStroke);
    }

    /// <param name="mousePosition">Current mouse position for hover detection.</param>
    internal static void DrawCheckbox(SKCanvas canvas, SKRect bounds, bool isChecked, Point mousePosition)
    {
        bool isHovered = bounds.Contains(mousePosition.X, mousePosition.Y);

        var bgColor = isChecked
            ? FUIColors.Active.WithAlpha(FUIColors.AlphaGlow)
            : (isHovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background2);
        var frameColor = isChecked
            ? FUIColors.Active
            : (isHovered ? FUIColors.FrameBright : FUIColors.Frame);
        FUIRenderer.DrawRoundedPanel(canvas, bounds, bgColor, frameColor, 2f);

        if (isChecked)
        {
            using var checkPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.Active,
                StrokeWidth = 2f,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round
            };
            float cx = bounds.MidX;
            float cy = bounds.MidY;
            float s = bounds.Width * 0.3f;
            canvas.DrawLine(cx - s, cy, cx - s * 0.3f, cy + s * 0.7f, checkPaint);
            canvas.DrawLine(cx - s * 0.3f, cy + s * 0.7f, cx + s, cy - s * 0.5f, checkPaint);
        }
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

    /// <summary>Truncates text to fit within maxWidth at the given fontSize, appending "..." if needed.</summary>
    internal static string TruncateTextToWidth(string text, float maxWidth, float fontSize)
    {
        if (FUIRenderer.MeasureText(text, fontSize) <= maxWidth) return text;

        int low = 0;
        int high = text.Length;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (FUIRenderer.MeasureText(string.Concat(text.AsSpan(0, mid), "..."), fontSize) <= maxWidth)
                low = mid;
            else
                high = mid - 1;
        }
        return low > 0 ? string.Concat(text.AsSpan(0, low), "...") : "...";
    }

    // ─── SC Bindings Shared Widgets ───────────────────────────────────────────


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

    internal static void DrawSCCheckbox(SKCanvas canvas, SKRect bounds, bool isChecked, bool isHovered)
    {
        var bgColor = isChecked ? FUIColors.Active.WithAlpha(FUIColors.AlphaGlow) : FUIColors.Background2.WithAlpha(100);
        if (isHovered) bgColor = bgColor.WithAlpha((byte)Math.Min(255, bgColor.Alpha + 40));
        var borderColor = isChecked ? FUIColors.Active : (isHovered ? FUIColors.FrameBright : FUIColors.Frame);
        FUIRenderer.DrawRoundedPanel(canvas, bounds, bgColor, borderColor);

        if (isChecked)
        {
            using var checkPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active, StrokeWidth = 2f, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
            float cx = bounds.MidX;
            float cy = bounds.MidY;
            canvas.DrawLine(cx - 4, cy, cx - 1, cy + 3, checkPaint);
            canvas.DrawLine(cx - 1, cy + 3, cx + 4, cy - 3, checkPaint);
        }
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

    /// <summary>
    /// Draws a horizontal segmented control — a row of mutually exclusive segments
    /// where exactly one can be selected. Returns the bounds of each segment for
    /// hit-testing in the caller's input handler.
    /// </summary>
    /// <param name="selectedIndex">0-based index of the active segment, or -1 for none.</param>
    /// <param name="hoveredIndex">0-based index of the hovered segment, or -1 for none.</param>
    /// <param name="enabled">When false, all segments render dimmed and non-interactive.</param>
    internal static SKRect[] DrawSegmentedControl(
        SKCanvas canvas,
        SKRect bounds,
        string[] labels,
        int selectedIndex,
        int hoveredIndex,
        bool enabled = true)
    {
        int count = labels.Length;
        if (count == 0) return Array.Empty<SKRect>();

        float segWidth = bounds.Width / count;
        var segmentBounds = new SKRect[count];

        for (int i = 0; i < count; i++)
        {
            var seg = new SKRect(bounds.Left + i * segWidth, bounds.Top,
                bounds.Left + (i + 1) * segWidth, bounds.Bottom);
            segmentBounds[i] = seg;

            bool isSelected = i == selectedIndex;
            bool isHovered = enabled && i == hoveredIndex;

            // Background
            SKColor bgColor;
            if (!enabled)
                bgColor = FUIColors.Background1.WithAlpha(80);
            else if (isSelected)
                bgColor = FUIColors.Active.WithAlpha(FUIColors.AlphaGlow);
            else if (isHovered)
                bgColor = FUIColors.Background2.WithAlpha(200);
            else
                bgColor = FUIColors.Background1.WithAlpha(150);

            using var bgPaint = FUIRenderer.CreateFillPaint(bgColor);
            canvas.DrawRect(seg, bgPaint);

            // Frame
            SKColor frameColor;
            if (!enabled)
                frameColor = FUIColors.Frame.WithAlpha(60);
            else if (isSelected)
                frameColor = FUIColors.Active;
            else if (isHovered)
                frameColor = FUIColors.FrameBright;
            else
                frameColor = FUIColors.Frame;

            using var framePaint = FUIRenderer.CreateStrokePaint(frameColor, isSelected ? 1.5f : 1f);
            canvas.DrawRect(seg, framePaint);

            // Text
            SKColor textColor;
            if (!enabled)
                textColor = FUIColors.TextDim.WithAlpha(100);
            else if (isSelected)
                textColor = FUIColors.TextBright;
            else if (isHovered)
                textColor = FUIColors.TextPrimary;
            else
                textColor = FUIColors.TextDim;

            FUIRenderer.DrawTextCentered(canvas, labels[i], seg, textColor, 11f, true);
        }

        return segmentBounds;
    }

    // ─── Network Mode Indicator ───────────────────────────────────────────────

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

    /// <summary>
    /// Draws a scrollbar (vertical or horizontal) and returns the track and thumb bounds
    /// for hit-testing. When <paramref name="isHovered"/> is true the scrollbar uses
    /// brighter colours to indicate interactivity.
    /// </summary>
    /// <param name="canvas">Target canvas.</param>
    /// <param name="trackBounds">Full track rectangle (caller decides position and size).</param>
    /// <param name="scrollOffset">Current scroll position (0 = start).</param>
    /// <param name="contentSize">Total content size (height for vertical, width for horizontal).</param>
    /// <param name="viewSize">Visible viewport size along the scroll axis.</param>
    /// <param name="isHovered">Whether the scrollbar is hovered or being dragged.</param>
    /// <param name="thumbBounds">Receives the computed thumb rectangle.</param>
    /// <param name="isHorizontal">True for a horizontal scrollbar.</param>
    /// <param name="cornerRadius">Rounding radius for track and thumb.</param>
    /// <param name="drawTrack">When false, only the thumb is drawn (useful for minimal indicators).</param>
    internal static void DrawScrollbar(
        SKCanvas canvas,
        SKRect trackBounds,
        float scrollOffset,
        float contentSize,
        float viewSize,
        bool isHovered,
        out SKRect thumbBounds,
        bool isHorizontal = false,
        float cornerRadius = 4f,
        bool drawTrack = true)
    {
        float trackLen = isHorizontal ? trackBounds.Width : trackBounds.Height;
        float minThumb = 30f;
        float thumbLen = Math.Max(minThumb, trackLen * (viewSize / contentSize));
        float maxScroll = Math.Max(0, contentSize - viewSize);
        float ratio = maxScroll > 0 ? scrollOffset / maxScroll : 0;
        float thumbOffset = ratio * (trackLen - thumbLen);

        if (isHorizontal)
        {
            thumbBounds = new SKRect(
                trackBounds.Left + thumbOffset, trackBounds.Top,
                trackBounds.Left + thumbOffset + thumbLen, trackBounds.Bottom);
        }
        else
        {
            thumbBounds = new SKRect(
                trackBounds.Left, trackBounds.Top + thumbOffset,
                trackBounds.Right, trackBounds.Top + thumbOffset + thumbLen);
        }

        // Track
        if (drawTrack)
        {
            using var trackPaint = FUIRenderer.CreateFillPaint(
                FUIColors.Background2.WithAlpha(isHovered ? (byte)120 : (byte)80));
            canvas.DrawRoundRect(trackBounds, cornerRadius, cornerRadius, trackPaint);
        }

        // Thumb
        var thumbColour = isHovered
            ? FUIColors.Active
            : FUIColors.Frame.WithAlpha(180);
        using var thumbPaint = FUIRenderer.CreateFillPaint(thumbColour);
        canvas.DrawRoundRect(thumbBounds, cornerRadius, cornerRadius, thumbPaint);
    }

    /// <summary>
    /// Draws a minimal passive scroll indicator (no track, no hover state).
    /// Suitable for read-only lists that scroll but have no interactive scrollbar.
    /// </summary>
    internal static void DrawScrollIndicator(
        SKCanvas canvas,
        SKRect trackBounds,
        float scrollOffset,
        float contentSize,
        float viewSize,
        float cornerRadius = 1.5f)
    {
        float trackLen = trackBounds.Height;
        float thumbLen = Math.Max(20f, trackLen * (viewSize / contentSize));
        float maxScroll = Math.Max(0, contentSize - viewSize);
        float ratio = maxScroll > 0 ? scrollOffset / maxScroll : 0;
        float thumbOffset = ratio * (trackLen - thumbLen);

        // Subtle track
        using var trackPaint = FUIRenderer.CreateFillPaint(FUIColors.Frame.WithAlpha(40));
        canvas.DrawRoundRect(trackBounds, cornerRadius, cornerRadius, trackPaint);

        // Thumb
        var thumbRect = new SKRect(
            trackBounds.Left, trackBounds.Top + thumbOffset,
            trackBounds.Right, trackBounds.Top + thumbOffset + thumbLen);
        using var thumbPaint = FUIRenderer.CreateFillPaint(FUIColors.Primary.WithAlpha(200));
        canvas.DrawRoundRect(thumbRect, cornerRadius, cornerRadius, thumbPaint);
    }

    // ─── Panel Split Animator ─────────────────────────────────────────────────

    /// <summary>
    /// Drives animated expand/collapse for a two-panel split layout.
    /// T lerps from 0 (panel A collapsed, B expanded) to 1 (A expanded, B collapsed).
    /// Call <see cref="Update"/> each tick. Use <see cref="ComputeBounds"/> to get animated bounds.
    /// </summary>
    internal struct PanelSplitAnimator
    {
        /// <summary>Current animation position: 0 = panel A collapsed, 1 = panel A expanded.</summary>
        public float T;

        /// <summary>True when panel B exists in the layout.</summary>
        public bool HasPanelB;

        /// <summary>True when panel B existed last frame (used to animate disappearance).</summary>
        public bool HadPanelB;

        private const float LerpSpeed = 0.18f;

        /// <summary>
        /// Call each tick. Returns true if the animation is still in progress (caller should MarkDirty).
        /// </summary>
        public bool Update(bool panelAExpanded, bool hasPanelB)
        {
            HasPanelB = hasPanelB;
            float target = (!hasPanelB || panelAExpanded) ? 1f : 0f;
            if (MathF.Abs(T - target) > 0.001f)
            {
                T += (target - T) * LerpSpeed;
                if (MathF.Abs(T - target) < 0.001f) T = target;
                return true;
            }
            if (!hasPanelB && T >= 0.999f)
                HadPanelB = false;
            else if (hasPanelB)
                HadPanelB = true;
            return false;
        }

        /// <summary>Whether the two-panel animated layout should be used.</summary>
        public readonly bool UseAnimatedLayout => HasPanelB || (HadPanelB && T < 0.999f);

        /// <summary>Whether panel B is animating out.</summary>
        public readonly bool IsAnimatingOut => !HasPanelB && HadPanelB && T < 0.999f;

        /// <summary>
        /// Computes the two panel bounds. Panel A height proportional to T, panel B gets the rest.
        /// </summary>
        public readonly (SKRect boundsA, SKRect boundsB) ComputeBounds(
            SKRect area, float gap, float collapsedH)
        {
            float expandableH = area.Height - 2 * collapsedH - gap;
            float aH = collapsedH + expandableH * T;
            var boundsA = new SKRect(area.Left, area.Top, area.Right, area.Top + aH);
            var boundsB = new SKRect(area.Left, boundsA.Bottom + gap, area.Right, area.Bottom);
            return (boundsA, boundsB);
        }
    }
}
