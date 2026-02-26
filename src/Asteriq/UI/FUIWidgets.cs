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
                ? (isDisconnected ? FUIColors.Danger.WithAlpha(20) : FUIColors.Active.WithAlpha(30))
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
        string displayName = name.Length > 28 ? name.Substring(0, 25) + "..." : name;
        var nameColor = isDisconnected
            ? FUIColors.TextDim
            : (isSelected ? FUIColors.TextBright : FUIColors.TextPrimary);
        FUIRenderer.DrawText(canvas, displayName, new SKPoint(x + 36, y + 24), nameColor, 13f, isSelected && !isDisconnected);

        // Status text
        var statusTextColor = isDisconnected ? FUIColors.Danger : FUIColors.Active;
        FUIRenderer.DrawText(canvas, status, new SKPoint(x + 36, y + 44), statusTextColor, 11f);

        // vJoy assignment indicator
        if (!isDisconnected && !string.IsNullOrEmpty(vjoyAssignment))
        {
            FUIRenderer.DrawText(canvas, vjoyAssignment, new SKPoint(x + width - 85, y + 45),
                FUIColors.TextDim, 11f);
        }

        // Selection chevron
        if (isSelected)
        {
            using var chevronPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = isDisconnected ? FUIColors.Danger : FUIColors.Active,
                StrokeWidth = 2f,
                IsAntialias = true
            };
            canvas.DrawLine(x + width - 18, y + 24, x + width - 10, y + 28, chevronPaint);
            canvas.DrawLine(x + width - 10, y + 28, x + width - 18, y + 32, chevronPaint);
        }
    }

    // ─── Forwarding / Status ──────────────────────────────────────────────────

    internal static void DrawForwardingButton(SKCanvas canvas, SKRect bounds, string text, bool isHovered, bool isStop)
    {
        var accentColor = isStop ? FUIColors.Danger : FUIColors.Active;

        var bgColor = isHovered
            ? accentColor.WithAlpha(50)
            : (isStop ? FUIColors.Danger.WithAlpha(20) : FUIColors.Active.WithAlpha(20));
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = bgColor,
            IsAntialias = true
        };
        canvas.DrawRect(bounds, bgPaint);

        var borderColor = isHovered ? accentColor : accentColor.WithAlpha(150);
        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = borderColor,
            StrokeWidth = isHovered ? 2f : 1f,
            IsAntialias = true
        };
        canvas.DrawRect(bounds, borderPaint);

        var textColor = isHovered ? FUIColors.TextBright : accentColor;
        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 11f, isHovered);
    }

    internal static void DrawStatusItem(SKCanvas canvas, float x, float y, float width, string label, string value, SKColor valueColor)
    {
        FUIRenderer.DrawText(canvas, label, new SKPoint(x, y + 12), FUIColors.TextDim, 11f);

        var dotColor = valueColor == FUIColors.Active ? valueColor : FUIColors.Primary.WithAlpha(100);
        float dotX = x + width - 110;
        FUIRenderer.DrawGlowingDot(canvas, new SKPoint(dotX, y + 8), dotColor, 2f, 4f);

        float textStartX = dotX + 10;
        float rightEdge = x + width;
        float maxValueWidth = rightEdge - textStartX - 8;

        using var measurePaint = FUIRenderer.CreateTextPaint(valueColor, 11f);
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

        FUIRenderer.DrawText(canvas, displayValue, new SKPoint(textStartX, y + 12), valueColor, 11f);
    }

    internal static void DrawLayerIndicator(SKCanvas canvas, float x, float y, float width, string name, bool isActive)
    {
        float height = 22f;
        var bounds = new SKRect(x, y, x + width, y + height);
        var frameColor = isActive ? FUIColors.Active : FUIColors.FrameDim;
        var fillColor = isActive ? FUIColors.Active.WithAlpha(40) : SKColors.Transparent;

        FUIRenderer.FillFrame(canvas, bounds, fillColor, 4f);
        FUIRenderer.DrawFrame(canvas, bounds, frameColor, 4f, 1f, isActive);

        var textColor = isActive ? FUIColors.TextBright : FUIColors.TextDim;
        FUIRenderer.DrawTextCentered(canvas, name, bounds, textColor, 10f, isActive);
    }

    internal static void DrawJoystickOutlineFallback(SKCanvas canvas, SKRect bounds)
    {
        using var outlinePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Primary.WithAlpha(60),
            StrokeWidth = 1.5f,
            IsAntialias = true
        };

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

        FUIRenderer.DrawText(canvas, "SHIFT LAYERS", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += lineHeight;

        FUIRenderer.DrawText(canvas, "[Coming soon] Hold a button to activate alternative mappings", new SKPoint(leftMargin, y), FUIColors.TextDim, 9f);
        y += lineHeight + 4;

        float layerRowHeight = FUIRenderer.TouchTargetStandard;
        foreach (var layer in profile.ShiftLayers)
        {
            if (y + layerRowHeight > bottom - 50) break;

            var rowBounds = new SKRect(leftMargin, y, rightMargin, y + layerRowHeight - 4);
            using var rowBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
            canvas.DrawRoundRect(rowBounds, 4, 4, rowBgPaint);

            using var rowFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
            canvas.DrawRoundRect(rowBounds, 4, 4, rowFramePaint);

            FUIRenderer.DrawText(canvas, layer.Name, new SKPoint(leftMargin + 10, y + 11), FUIColors.TextPrimary, 11f);

            string activatorText = layer.ActivatorButton is not null
                ? $"Button {layer.ActivatorButton.Index + 1} on {layer.ActivatorButton.DeviceName}"
                : "Not assigned";
            FUIRenderer.DrawText(canvas, activatorText, new SKPoint(leftMargin + 100, y + 11),
                layer.ActivatorButton is not null ? FUIColors.TextDim : FUIColors.Warning.WithAlpha(150), 9f);

            float delSize = 20f;
            var delBounds = new SKRect(rightMargin - delSize - 8, y + (layerRowHeight - delSize) / 2 - 2,
                rightMargin - 8, y + (layerRowHeight + delSize) / 2 - 2);

            using var delPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Danger.WithAlpha(60) };
            canvas.DrawRoundRect(delBounds, 2, 2, delPaint);
            FUIRenderer.DrawTextCentered(canvas, "x", delBounds, FUIColors.Danger, 12f);

            y += layerRowHeight;
        }

        if (y + 36 < bottom)
        {
            var addBounds = new SKRect(leftMargin, y, rightMargin, y + 30);
            using var addBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Success.WithAlpha(20) };
            canvas.DrawRoundRect(addBounds, 4, 4, addBgPaint);

            using var addFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Success.WithAlpha(100), StrokeWidth = 1f };
            canvas.DrawRoundRect(addBounds, 4, 4, addFramePaint);

            FUIRenderer.DrawTextCentered(canvas, "+ Add Shift Layer", addBounds, FUIColors.Success, 11f);
        }

    }

    internal static void DrawProfileStat(SKCanvas canvas, float x, float y, string label, string value, float valueOffset = 130f)
    {
        FUIRenderer.DrawText(canvas, label, new SKPoint(x, y), FUIColors.TextDim, 10f);
        FUIRenderer.DrawText(canvas, value, new SKPoint(x + valueOffset, y), FUIColors.TextPrimary, 10f);
    }

    internal static void DrawSettingsButton(SKCanvas canvas, SKRect bounds, string text, bool disabled)
    {
        var bgColor = disabled ? FUIColors.Background2.WithAlpha(100) : FUIColors.Background2;
        var frameColor = disabled ? FUIColors.Frame.WithAlpha(80) : FUIColors.Frame;
        var textColor = disabled ? FUIColors.TextDim.WithAlpha(100) : FUIColors.TextPrimary;

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRoundRect(bounds, 4, 4, bgPaint);

        using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = frameColor, StrokeWidth = 1f };
        canvas.DrawRoundRect(bounds, 4, 4, framePaint);

        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 11f);
    }

    internal static void DrawSettingsValueField(SKCanvas canvas, SKRect bounds, string value)
    {
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        canvas.DrawRoundRect(bounds, 3, 3, bgPaint);

        using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
        canvas.DrawRoundRect(bounds, 3, 3, framePaint);

        FUIRenderer.DrawTextCentered(canvas, value, bounds, FUIColors.TextPrimary, 11f);
    }

    // ─── Mapping Editor Widgets ───────────────────────────────────────────────

    internal static void DrawSlider(SKCanvas canvas, SKRect bounds, float value)
    {
        using var trackPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        canvas.DrawRoundRect(bounds, 4, 4, trackPaint);

        float fillWidth = bounds.Width * Math.Clamp(value, 0, 1);
        var fillBounds = new SKRect(bounds.Left, bounds.Top, bounds.Left + fillWidth, bounds.Bottom);
        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active };
        canvas.DrawRoundRect(fillBounds, 4, 4, fillPaint);

        float handleX = bounds.Left + fillWidth;
        float handleRadius = bounds.Height;
        using var handlePaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.TextBright };
        canvas.DrawCircle(handleX, bounds.MidY, handleRadius, handlePaint);
    }

    /// <param name="mousePosition">Current mouse position for hover detection.</param>
    internal static void DrawToggleSwitch(SKCanvas canvas, SKRect bounds, bool on, Point mousePosition)
    {
        bool isHovered = bounds.Contains(mousePosition.X, mousePosition.Y);

        SKColor trackColor = on
            ? FUIColors.Active.WithAlpha(150)
            : (isHovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background2);
        using var trackPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = trackColor };
        canvas.DrawRoundRect(bounds, bounds.Height / 2, bounds.Height / 2, trackPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = on ? FUIColors.Active : (isHovered ? FUIColors.FrameBright : FUIColors.Frame),
            StrokeWidth = 1f
        };
        canvas.DrawRoundRect(bounds, bounds.Height / 2, bounds.Height / 2, framePaint);

        float knobRadius = bounds.Height / 2 - 3;
        float knobX = on ? bounds.Right - knobRadius - 3 : bounds.Left + knobRadius + 3;
        using var knobPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.TextBright };
        canvas.DrawCircle(knobX, bounds.MidY, knobRadius, knobPaint);
    }

    internal static void DrawSettingsSlider(SKCanvas canvas, SKRect bounds, int value, int maxValue)
    {
        float trackHeight = 4f;
        float trackY = bounds.MidY - trackHeight / 2;
        var trackRect = new SKRect(bounds.Left, trackY, bounds.Right, trackY + trackHeight);

        using var trackBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        canvas.DrawRoundRect(trackRect, 2, 2, trackBgPaint);

        using var trackFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
        canvas.DrawRoundRect(trackRect, 2, 2, trackFramePaint);

        float fillWidth = (bounds.Width - 6) * (value / (float)maxValue);
        if (fillWidth > 0)
        {
            var fillRect = new SKRect(bounds.Left + 2, trackY + 1, bounds.Left + 2 + fillWidth, trackY + trackHeight - 1);
            using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(180) };
            canvas.DrawRoundRect(fillRect, 1, 1, fillPaint);
        }

        float knobX = bounds.Left + 3 + (bounds.Width - 6) * (value / (float)maxValue);
        float knobRadius = 6f;
        using var knobPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.TextBright, IsAntialias = true };
        canvas.DrawCircle(knobX, bounds.MidY, knobRadius, knobPaint);

        using var knobFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawCircle(knobX, bounds.MidY, knobRadius, knobFramePaint);
    }

    internal static void DrawInteractiveSlider(SKCanvas canvas, SKRect bounds, float value, SKColor color, bool dragging)
    {
        using var trackPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        canvas.DrawRoundRect(bounds, 4, 4, trackPaint);

        using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
        canvas.DrawRoundRect(bounds, 4, 4, framePaint);

        float fillWidth = bounds.Width * Math.Clamp(value, 0, 1);
        if (fillWidth > 2)
        {
            var fillBounds = new SKRect(bounds.Left + 1, bounds.Top + 1, bounds.Left + fillWidth - 1, bounds.Bottom - 1);
            using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = color.WithAlpha(100) };
            canvas.DrawRoundRect(fillBounds, 3, 3, fillPaint);
        }

        float handleX = bounds.Left + fillWidth;
        float handleRadius = dragging ? 8f : 6f;
        using var handlePaint = new SKPaint { Style = SKPaintStyle.Fill, Color = dragging ? color : FUIColors.TextPrimary, IsAntialias = true };
        canvas.DrawCircle(handleX, bounds.MidY, handleRadius, handlePaint);

        using var handleStroke = new SKPaint { Style = SKPaintStyle.Stroke, Color = color, StrokeWidth = 1.5f, IsAntialias = true };
        canvas.DrawCircle(handleX, bounds.MidY, handleRadius, handleStroke);
    }

    internal static void DrawDurationSlider(SKCanvas canvas, SKRect bounds, float value, bool dragging)
    {
        value = Math.Clamp(value, 0f, 1f);

        using var trackPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        canvas.DrawRoundRect(bounds, 4, 4, trackPaint);

        using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
        canvas.DrawRoundRect(bounds, 4, 4, framePaint);

        float fillWidth = bounds.Width * value;
        if (fillWidth > 2)
        {
            var fillBounds = new SKRect(bounds.Left + 1, bounds.Top + 1, bounds.Left + fillWidth - 1, bounds.Bottom - 1);
            using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(80) };
            canvas.DrawRoundRect(fillBounds, 3, 3, fillPaint);
        }

        float handleX = bounds.Left + fillWidth;
        float handleRadius = dragging ? 8f : 6f;
        using var handlePaint = new SKPaint { Style = SKPaintStyle.Fill, Color = dragging ? FUIColors.Active : FUIColors.TextPrimary, IsAntialias = true };
        canvas.DrawCircle(handleX, bounds.MidY, handleRadius, handlePaint);

        using var handleStroke = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active, StrokeWidth = 1.5f, IsAntialias = true };
        canvas.DrawCircle(handleX, bounds.MidY, handleRadius, handleStroke);
    }

    /// <param name="mousePosition">Current mouse position for hover detection.</param>
    internal static void DrawCheckbox(SKCanvas canvas, SKRect bounds, bool isChecked, Point mousePosition)
    {
        bool isHovered = bounds.Contains(mousePosition.X, mousePosition.Y);

        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = isChecked
                ? FUIColors.Active.WithAlpha(60)
                : (isHovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background2)
        };
        canvas.DrawRoundRect(bounds, 2, 2, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = isChecked
                ? FUIColors.Active
                : (isHovered ? FUIColors.FrameBright : FUIColors.Frame),
            StrokeWidth = 1f
        };
        canvas.DrawRoundRect(bounds, 2, 2, framePaint);

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

    internal static void DrawToggleButton(SKCanvas canvas, SKRect bounds, string text, bool active, bool hovered)
    {
        var bgColor = active
            ? FUIColors.Active.WithAlpha(60)
            : (hovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);
        var textColor = active ? FUIColors.Active : (hovered ? FUIColors.TextPrimary : FUIColors.TextDim);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = active ? FUIColors.Active : FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 11f);
    }

    internal static void DrawDropdown(SKCanvas canvas, SKRect bounds, string text, bool open)
    {
        var bgColor = open ? FUIColors.Primary.WithAlpha(40) : FUIColors.Background2;
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = open ? FUIColors.Primary : FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        FUIRenderer.DrawText(canvas, text, new SKPoint(bounds.Left + 8, bounds.MidY + 4),
            FUIColors.TextPrimary, 11f);

        string arrow = open ? "▲" : "▼";
        FUIRenderer.DrawText(canvas, arrow, new SKPoint(bounds.Right - 18, bounds.MidY + 4),
            FUIColors.TextDim, 10f);
    }

    internal static void DrawSmallIconButton(SKCanvas canvas, SKRect bounds, string icon, bool hovered, bool isDanger = false)
    {
        var bgColor = hovered
            ? (isDanger ? FUIColors.Warning.WithAlpha(60) : FUIColors.Active.WithAlpha(60))
            : FUIColors.Background2.WithAlpha(100);
        var textColor = hovered
            ? (isDanger ? FUIColors.Warning : FUIColors.Active)
            : FUIColors.TextDim;

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = hovered ? (isDanger ? FUIColors.Warning : FUIColors.Active) : FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        FUIRenderer.DrawTextCentered(canvas, icon, bounds, textColor, 14f);
    }

    internal static void DrawActionButton(SKCanvas canvas, SKRect bounds, string text, bool hovered, bool isPrimary)
    {
        var bgColor = isPrimary
            ? (hovered ? FUIColors.Active : FUIColors.Active.WithAlpha(180))
            : (hovered ? FUIColors.Primary.WithAlpha(60) : FUIColors.Background2);
        var textColor = isPrimary
            ? FUIColors.Background1
            : (hovered ? FUIColors.TextBright : FUIColors.TextPrimary);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = isPrimary ? FUIColors.Active : FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 12f);
    }

    internal static void DrawArrowButton(SKCanvas canvas, SKRect bounds, string arrow, bool hovered, bool enabled)
    {
        var bgColor = enabled
            ? (hovered ? FUIColors.Primary.WithAlpha(80) : FUIColors.Background2)
            : FUIColors.Background1;
        var arrowColor = enabled
            ? (hovered ? FUIColors.TextBright : FUIColors.TextPrimary)
            : FUIColors.TextDisabled;

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = enabled ? FUIColors.Frame : FUIColors.FrameDim,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        float centerX = bounds.MidX;
        float centerY = bounds.MidY;
        float arrowSize = 8f;

        using var arrowPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = arrowColor, IsAntialias = true };
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
        float fontSize = 10f;
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

            using var keycapBgPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = FUIColors.TextPrimary.WithAlpha(25),
                IsAntialias = true
            };
            canvas.DrawRoundRect(keycapBounds, 3, 3, keycapBgPaint);

            using var keycapFramePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.TextPrimary.WithAlpha(150),
                StrokeWidth = 1f,
                IsAntialias = true
            };
            canvas.DrawRoundRect(keycapBounds, 3, 3, keycapFramePaint);

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
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = enabled ? FUIColors.Background2.WithAlpha(100) : FUIColors.Background1.WithAlpha(80)
        };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = enabled ? FUIColors.Frame : FUIColors.FrameDim,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        var typeColor = type == "BUTTON" ? FUIColors.Active : FUIColors.Primary;
        FUIRenderer.DrawText(canvas, type, new SKPoint(bounds.Left + 10, bounds.Top + 18),
            enabled ? typeColor : typeColor.WithAlpha(100), 10f);

        FUIRenderer.DrawText(canvas, source, new SKPoint(bounds.Left + 80, bounds.Top + 18),
            enabled ? FUIColors.TextPrimary : FUIColors.TextDim, 12f);

        FUIRenderer.DrawText(canvas, "->", new SKPoint(bounds.Left + 80, bounds.Top + 36),
            FUIColors.TextDim, 11f);

        FUIRenderer.DrawText(canvas, target, new SKPoint(bounds.Left + 110, bounds.Top + 36),
            enabled ? FUIColors.TextPrimary : FUIColors.TextDim, 12f);

        var statusColor = enabled ? FUIColors.Success : FUIColors.TextDisabled;
        FUIRenderer.DrawGlowingDot(canvas, new SKPoint(bounds.Right - 20, bounds.MidY),
            statusColor, 4f, enabled ? 6f : 2f);
    }

    internal static void DrawAddMappingButton(SKCanvas canvas, SKRect bounds, bool hovered)
    {
        var bgColor = hovered ? FUIColors.Active.WithAlpha(60) : FUIColors.Primary.WithAlpha(30);
        var frameColor = hovered ? FUIColors.Active : FUIColors.Primary;

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = frameColor,
            StrokeWidth = hovered ? 2f : 1f,
            IsAntialias = true
        };
        canvas.DrawRect(bounds, framePaint);

        float iconX = bounds.Left + 16;
        float iconY = bounds.MidY;
        using var iconPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = hovered ? FUIColors.TextBright : FUIColors.TextPrimary,
            StrokeWidth = 2f,
            IsAntialias = true
        };
        canvas.DrawLine(iconX - 6, iconY, iconX + 6, iconY, iconPaint);
        canvas.DrawLine(iconX, iconY - 6, iconX, iconY + 6, iconPaint);

        FUIRenderer.DrawText(canvas, "ADD MAPPING",
            new SKPoint(bounds.Left + 30, bounds.MidY + 5),
            hovered ? FUIColors.TextBright : FUIColors.TextPrimary, 12f);
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
        var textColor = isActive ? FUIColors.TextBright : FUIColors.TextDim;

        using var themeBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, themeBgPaint);

        using var themeFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = frameColor, StrokeWidth = isActive ? 1.5f : 1f };
        canvas.DrawRect(bounds, themeFramePaint);

        FUIRenderer.DrawTextCentered(canvas, name, bounds, textColor, 8f);

        var indicatorBounds = new SKRect(bounds.Left + 2, bounds.Bottom - 2,
            bounds.Right - 2, bounds.Bottom - 1);
        using var indicatorPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = previewColor.WithAlpha((byte)(isActive ? 200 : 100)) };
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
            if (FUIRenderer.MeasureText(text.Substring(0, mid) + "...", fontSize) <= maxWidth)
                low = mid;
            else
                high = mid - 1;
        }
        return low > 0 ? text.Substring(0, low) + "..." : "...";
    }

    // ─── SC Bindings Shared Widgets ───────────────────────────────────────────

    internal static void DrawExportButton(SKCanvas canvas, SKRect bounds, string text, bool isHovered, bool isEnabled)
    {
        var bgColor = isEnabled
            ? (isHovered ? FUIColors.Active.WithAlpha(180) : FUIColors.Active.WithAlpha(120))
            : FUIColors.Background2.WithAlpha(100);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        using var path = FUIRenderer.CreateFrame(bounds, 4f);
        canvas.DrawPath(path, bgPaint);

        var borderColor = isEnabled ? FUIColors.Active : FUIColors.Frame.WithAlpha(100);
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = borderColor, StrokeWidth = 1.5f, IsAntialias = true };
        canvas.DrawPath(path, borderPaint);

        var textColor = isEnabled ? FUIColors.TextBright : FUIColors.TextDim;
        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 12f);
    }

    internal static void DrawImportButton(SKCanvas canvas, SKRect bounds, string text, bool isHovered, bool isEnabled)
    {
        var bgColor = isEnabled
            ? (isHovered ? FUIColors.Primary.WithAlpha(150) : FUIColors.Primary.WithAlpha(80))
            : FUIColors.Background2.WithAlpha(100);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        using var path = FUIRenderer.CreateFrame(bounds, 4f);
        canvas.DrawPath(path, bgPaint);

        var borderColor = isEnabled ? FUIColors.Primary : FUIColors.Frame.WithAlpha(100);
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = borderColor, StrokeWidth = 1.5f, IsAntialias = true };
        canvas.DrawPath(path, borderPaint);

        var textColor = isEnabled ? FUIColors.TextPrimary : FUIColors.TextDim;
        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 11f);

        if (isEnabled)
        {
            float arrowX = bounds.Right - 16;
            float arrowY = bounds.MidY;
            using var arrowPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = textColor, StrokeWidth = 1.5f, IsAntialias = true };
            canvas.DrawLine(arrowX - 4, arrowY - 2, arrowX, arrowY + 2, arrowPaint);
            canvas.DrawLine(arrowX, arrowY + 2, arrowX + 4, arrowY - 2, arrowPaint);
        }
    }

    internal static void DrawSearchBox(SKCanvas canvas, SKRect bounds, string text, bool focused, Point mousePosition)
    {
        var bgColor = focused ? FUIColors.Background2.WithAlpha(180) : FUIColors.Background2.WithAlpha(100);
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 4f, 4f, bgPaint);

        var borderColor = focused ? FUIColors.Active : FUIColors.Frame;
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = borderColor, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 4f, 4f, borderPaint);

        float iconX = bounds.Left + 8f;
        float iconY = bounds.MidY;
        using var iconPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.TextDim, StrokeWidth = 1.5f, IsAntialias = true };
        canvas.DrawCircle(iconX + 5, iconY - 1, 5f, iconPaint);
        canvas.DrawLine(iconX + 9, iconY + 3, iconX + 13, iconY + 7, iconPaint);

        float textX = bounds.Left + 24f;
        float textY = bounds.MidY + 4f;
        if (string.IsNullOrEmpty(text))
        {
            FUIRenderer.DrawText(canvas, "Search actions...", new SKPoint(textX, textY), FUIColors.TextDim, 10f);
        }
        else
        {
            FUIRenderer.DrawText(canvas, text, new SKPoint(textX, textY), FUIColors.TextPrimary, 10f);

            if (bounds.Contains(mousePosition.X, mousePosition.Y))
            {
                float clearX = bounds.Right - 18f;
                float clearY = bounds.MidY;
                using var clearPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.TextDim, StrokeWidth = 1.5f, IsAntialias = true };
                canvas.DrawLine(clearX - 4, clearY - 4, clearX + 4, clearY + 4, clearPaint);
                canvas.DrawLine(clearX + 4, clearY - 4, clearX - 4, clearY + 4, clearPaint);
            }
        }

        if (focused)
        {
            float cursorX = textX + (string.IsNullOrEmpty(text) ? 0 : FUIRenderer.MeasureText(text, 10f));
            if ((DateTime.Now.Millisecond / 500) % 2 == 0)
            {
                using var cursorPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active, StrokeWidth = 1f };
                canvas.DrawLine(cursorX, bounds.Top + 5, cursorX, bounds.Bottom - 5, cursorPaint);
            }
        }
    }

    internal static void DrawCollapseIndicator(SKCanvas canvas, float x, float y, bool isCollapsed, bool isHovered)
    {
        var color = isHovered ? FUIColors.TextBright : FUIColors.Primary;
        using var paint = new SKPaint { Style = SKPaintStyle.Fill, Color = color, IsAntialias = true };

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
        var bgColor = isChecked ? FUIColors.Active.WithAlpha(60) : FUIColors.Background2.WithAlpha(100);
        if (isHovered) bgColor = bgColor.WithAlpha((byte)Math.Min(255, bgColor.Alpha + 40));
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, bgPaint);

        var borderColor = isChecked ? FUIColors.Active : (isHovered ? FUIColors.FrameBright : FUIColors.Frame);
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = borderColor, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, borderPaint);

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
        var bgColor = hovered ? FUIColors.Active.WithAlpha(80) : FUIColors.Background2.WithAlpha(120);
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, bgPaint);

        var borderColor = hovered ? FUIColors.Active : FUIColors.Frame;
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = borderColor, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, borderPaint);

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

    internal static void DrawTextButton(SKCanvas canvas, SKRect bounds, string text, bool hovered, bool disabled = false)
    {
        SKColor bgColor;
        if (disabled)
            bgColor = FUIColors.Background2.WithAlpha(60);
        else if (hovered)
            bgColor = FUIColors.Active.WithAlpha(80);
        else
            bgColor = FUIColors.Background2.WithAlpha(100);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, bgPaint);

        var borderColor = disabled ? FUIColors.Frame.WithAlpha(80) : (hovered ? FUIColors.Active : FUIColors.Frame);
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = borderColor, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, borderPaint);

        var textColor = disabled ? FUIColors.TextDim.WithAlpha(100) : (hovered ? FUIColors.TextBright : FUIColors.TextPrimary);
        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 9f);
    }

    // ─── General Navigation Widgets ───────────────────────────────────────────

    internal static void DrawDropdownItem(SKCanvas canvas, float x, float itemY, float width, float itemHeight,
        string text, bool isHovered, bool isActive, bool isEnabled)
    {
        var itemBounds = new SKRect(x + 4, itemY, x + width - 4, itemY + itemHeight);

        if (isHovered && isEnabled)
        {
            using var hoverPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(40), IsAntialias = true };
            canvas.DrawRect(itemBounds, hoverPaint);

            using var accentPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active, IsAntialias = true };
            canvas.DrawRect(new SKRect(x + 4, itemY + 2, x + 6, itemY + itemHeight - 2), accentPaint);
        }

        var color = !isEnabled ? FUIColors.TextDisabled
            : isHovered ? FUIColors.TextBright
            : FUIColors.TextDim;
        FUIRenderer.DrawText(canvas, text, new SKPoint(x + 12, itemY + 17), color, 11f);
    }

    internal static void DrawTextFieldReadOnly(SKCanvas canvas, SKRect bounds, string text, bool isHovered)
    {
        var bgColor = isHovered ? FUIColors.Background2.WithAlpha(180) : FUIColors.Background1.WithAlpha(140);
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        canvas.DrawRect(bounds, bgPaint);

        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRect(bounds, borderPaint);

        FUIRenderer.DrawText(canvas, text, new SKPoint(bounds.Left + 10, bounds.MidY + 4), FUIColors.TextPrimary, 11f);
    }

    internal static void DrawVerticalSideTab(SKCanvas canvas, SKRect bounds, string label, bool isSelected, bool isHovered)
    {
        if (isSelected)
        {
            using var accentPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active, StrokeWidth = 3f, IsAntialias = true };
            canvas.DrawLine(bounds.Right - 1, bounds.Top + 5, bounds.Right - 1, bounds.Bottom - 5, accentPaint);

            using var glowPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.Active.WithAlpha(60),
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
            TextSize = 10f,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
            TextAlign = SKTextAlign.Center
        };
        canvas.DrawText(label, 0, 4f, textPaint);
        canvas.Restore();
    }

    internal static void DrawSelector(SKCanvas canvas, SKRect bounds, string text, bool isHovered, bool isEnabled)
    {
        var bgColor = isEnabled
            ? (isHovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background1.WithAlpha(150))
            : FUIColors.Background1.WithAlpha(100);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        canvas.DrawRect(bounds, bgPaint);

        var borderColor = isEnabled
            ? (isHovered ? FUIColors.FrameBright : FUIColors.Frame)
            : FUIColors.Frame.WithAlpha(100);
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = borderColor, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRect(bounds, borderPaint);

        float textPadding = 8f;
        float arrowSpaceRight = 20f;
        float maxTextWidth = bounds.Width - textPadding - arrowSpaceRight;
        string truncatedText = TruncateTextToWidth(text, maxTextWidth, 11f);

        var textColor = isEnabled ? FUIColors.TextPrimary : FUIColors.TextDim;
        FUIRenderer.DrawText(canvas, truncatedText, new SKPoint(bounds.Left + textPadding, bounds.MidY + 4), textColor, 11f);

        if (isEnabled)
        {
            float arrowX = bounds.Right - 12f;
            float arrowY = bounds.MidY;
            using var arrowPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.TextDim, IsAntialias = true };
            using var arrowPath = new SKPath();
            arrowPath.MoveTo(arrowX - 4, arrowY - 2);
            arrowPath.LineTo(arrowX + 4, arrowY - 2);
            arrowPath.LineTo(arrowX, arrowY + 3);
            arrowPath.Close();
            canvas.DrawPath(arrowPath, arrowPaint);
        }
    }
}
