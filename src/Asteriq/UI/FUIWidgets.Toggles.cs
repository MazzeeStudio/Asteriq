using Asteriq.Models;
using SkiaSharp;

namespace Asteriq.UI;

internal static partial class FUIWidgets
{
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

    /// <summary>
    /// Draws a checkbox with label. This is the single-source-of-truth for all checkbox+label combos.
    /// Label is positioned baseline-aligned with the checkbox bottom, 7px right of the checkbox.
    /// Label color: TextBright on hover, TextDim otherwise (via SecondaryColor).
    /// Returns the total width consumed (checkbox + gap + label).
    /// </summary>
    internal static float DrawCheckboxWithLabel(SKCanvas canvas, SKRect checkboxBounds, bool isChecked, bool isHovered,
        string label, float fontSize = 13f)
    {
        // Draw the checkbox
        var bgColor = isChecked
            ? FUIColors.Active.WithAlpha(FUIColors.AlphaGlow)
            : (isHovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background2);
        var frameColor = isChecked
            ? FUIColors.Active
            : (isHovered ? FUIColors.FrameBright : FUIColors.Frame);
        FUIRenderer.DrawRoundedPanel(canvas, checkboxBounds, bgColor, frameColor, 2f);

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
            float cx = checkboxBounds.MidX;
            float cy = checkboxBounds.MidY;
            float s = checkboxBounds.Width * 0.3f;
            canvas.DrawLine(cx - s, cy, cx - s * 0.3f, cy + s * 0.7f, checkPaint);
            canvas.DrawLine(cx - s * 0.3f, cy + s * 0.7f, cx + s, cy - s * 0.5f, checkPaint);
        }

        // Draw the label (right of checkbox)
        float labelX = checkboxBounds.Right + 7;
        float labelY = checkboxBounds.Bottom - 1;
        var labelColor = FUIColors.SecondaryColor(isHovered);
        FUIRenderer.DrawText(canvas, label, new SKPoint(labelX, labelY), labelColor, fontSize);

        return checkboxBounds.Width + 7 + FUIRenderer.MeasureText(label, fontSize);
    }

    /// <summary>
    /// Draws a checkbox with label positioned to the LEFT of the checkbox.
    /// Same visual style as DrawCheckboxWithLabel but reversed layout.
    /// </summary>
    internal static void DrawCheckboxWithLabelLeft(SKCanvas canvas, SKRect checkboxBounds, bool isChecked, bool isHovered,
        string label, float fontSize = 13f)
    {
        // Draw the checkbox (same as DrawCheckboxWithLabel)
        var bgColor = isChecked
            ? FUIColors.Active.WithAlpha(FUIColors.AlphaGlow)
            : (isHovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background2);
        var frameColor = isChecked
            ? FUIColors.Active
            : (isHovered ? FUIColors.FrameBright : FUIColors.Frame);
        FUIRenderer.DrawRoundedPanel(canvas, checkboxBounds, bgColor, frameColor, 2f);

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
            float cx = checkboxBounds.MidX;
            float cy = checkboxBounds.MidY;
            float s = checkboxBounds.Width * 0.3f;
            canvas.DrawLine(cx - s, cy, cx - s * 0.3f, cy + s * 0.7f, checkPaint);
            canvas.DrawLine(cx - s * 0.3f, cy + s * 0.7f, cx + s, cy - s * 0.5f, checkPaint);
        }

        // Draw the label (left of checkbox)
        float labelWidth = FUIRenderer.MeasureText(label, fontSize);
        float labelX = checkboxBounds.Left - 7 - labelWidth;
        float labelY = checkboxBounds.Bottom - 1;
        var labelColor = FUIColors.SecondaryColor(isHovered);
        FUIRenderer.DrawText(canvas, label, new SKPoint(labelX, labelY), labelColor, fontSize);
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

}