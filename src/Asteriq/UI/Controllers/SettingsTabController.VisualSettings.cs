using System.Reflection;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using Serilog;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public sealed partial class SettingsTabController
{
    private void DrawVisualSettingsSubPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        bool headerHovered = new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Top + RightPanelCollapsedH)
            .Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        var m = FUIWidgets.DrawCollapsiblePanelHeader(canvas, bounds, "VISUAL", true, headerHovered, out var hdrBounds);
        _visualPanelHeaderBounds = hdrBounds;
        float y = m.Y + 14f; // extra spacing before theme grid
        float leftMargin = m.LeftMargin;
        float rightMargin = m.RightMargin;
        float contentWidth = m.ContentWidth;
        float sectionSpacing = 16f;

        // Theme section
        float themeLabelWidth = 36f;
        float themeAreaWidth = contentWidth - themeLabelWidth;
        float themeBtnGap = 4f;
        float themeBtnWidth = Math.Min(40f, (themeAreaWidth - themeBtnGap * 3) / 4);
        float themeBtnHeight = FUIRenderer.TouchTargetMinHeight;
        float themeBtnsStartX = leftMargin + themeLabelWidth;

        float themeLabelY = themeBtnHeight / 2 + 3;
        FUIRenderer.DrawTextTruncated(canvas, "Core", new SKPoint(leftMargin, y + themeLabelY), themeLabelWidth - 5, FUIColors.TextDim, 12f);

        FUITheme[] coreThemes = { FUITheme.Midnight, FUITheme.Matrix, FUITheme.Amber, FUITheme.Ice };
        string[] coreNames = { "MID", "MTX", "AMB", "ICE" };
        SKColor[] coreColors = {
            new SKColor(0x40, 0xA0, 0xFF), new SKColor(0x40, 0xFF, 0x40),
            new SKColor(0xFF, 0xA0, 0x40), new SKColor(0x40, 0xE0, 0xFF)
        };

        for (int i = 0; i < coreThemes.Length; i++)
        {
            var themeBounds = new SKRect(
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap), y,
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap) + themeBtnWidth, y + themeBtnHeight);
            StoreThemeButtonBounds(i, themeBounds);
            FUIWidgets.DrawThemeButton(canvas, themeBounds, coreNames[i], coreColors[i], FUIColors.CurrentTheme == coreThemes[i], _ctx.MousePosition);
        }
        y += themeBtnHeight + 8;

        FUIRenderer.DrawTextTruncated(canvas, "Mfr", new SKPoint(leftMargin, y + themeLabelY), themeLabelWidth - 5, FUIColors.TextDim, 12f);

        FUITheme[] mfrThemes1 = { FUITheme.Drake, FUITheme.Aegis, FUITheme.Anvil, FUITheme.Argo };
        string[] mfrNames1 = { "DRK", "AEG", "ANV", "ARG" };
        SKColor[] mfrColors1 = {
            new SKColor(0xFF, 0x80, 0x20), new SKColor(0x40, 0x90, 0xE0),
            new SKColor(0x90, 0xC0, 0x40), new SKColor(0xFF, 0xC0, 0x00)
        };

        for (int i = 0; i < mfrThemes1.Length; i++)
        {
            var themeBounds = new SKRect(
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap), y,
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap) + themeBtnWidth, y + themeBtnHeight);
            StoreThemeButtonBounds(4 + i, themeBounds);
            FUIWidgets.DrawThemeButton(canvas, themeBounds, mfrNames1[i], mfrColors1[i], FUIColors.CurrentTheme == mfrThemes1[i], _ctx.MousePosition);
        }
        y += themeBtnHeight + 4;

        FUITheme[] mfrThemes2 = { FUITheme.Crusader, FUITheme.Origin, FUITheme.MISC, FUITheme.RSI };
        string[] mfrNames2 = { "CRU", "ORI", "MSC", "RSI" };
        SKColor[] mfrColors2 = {
            new SKColor(0x40, 0x90, 0xE0), new SKColor(0xD4, 0xAF, 0x37),
            new SKColor(0x40, 0xC0, 0x90), new SKColor(0x50, 0xA0, 0xF0)
        };

        for (int i = 0; i < mfrThemes2.Length; i++)
        {
            var themeBounds = new SKRect(
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap), y,
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap) + themeBtnWidth, y + themeBtnHeight);
            StoreThemeButtonBounds(8 + i, themeBounds);
            FUIWidgets.DrawThemeButton(canvas, themeBounds, mfrNames2[i], mfrColors2[i], FUIColors.CurrentTheme == mfrThemes2[i], _ctx.MousePosition);
        }
        y += themeBtnHeight + 4;

        // ── Colour Palette preview ─────────────────────────────────────────
        FUIWidgets.DrawSectionLabel(canvas, "PALETTE", leftMargin, ref y);

        float swatchGap = 5f;
        float swatchW   = (contentWidth - swatchGap * 3f) / 4f;
        float swatchH   = 28f;

        // Row 1 — accent / state colours rendered as "mini button" style (tinted bg + border + text)
        // This matches how these colours actually appear in UI elements (e.g. the Danger Delete button).
        (SKColor color, string label)[] row1 =
        [
            (FUIColors.Primary, "PRIMARY"),
            (FUIColors.Active,  "ACTIVE"),
            (FUIColors.Warning, "WARNING"),
            (FUIColors.Danger,  "DANGER"),
        ];
        for (int i = 0; i < row1.Length; i++)
        {
            float sx = leftMargin + i * (swatchW + swatchGap);
            var rect = new SKRect(sx, y, sx + swatchW, y + swatchH);
            // Dark background tinted with the colour — mirrors Delete / Share button style
            using var tintFill = FUIRenderer.CreateFillPaint(row1[i].color.WithAlpha(35));
            canvas.DrawRect(rect, tintFill);
            using var borderPaint = FUIRenderer.CreateStrokePaint(row1[i].color.WithAlpha(180));
            canvas.DrawRect(rect, borderPaint);
            float lblW = FUIRenderer.MeasureText(row1[i].label, 9f);
            FUIRenderer.DrawText(canvas, row1[i].label,
                new SKPoint(sx + swatchW / 2f - lblW / 2f, y + swatchH / 2f + 4f),
                row1[i].color, 9f);
        }
        y += swatchH + 5f;

        // Row 2 — text hierarchy + frame: solid fills showing the literal colour values
        (SKColor color, string label)[] row2 =
        [
            (FUIColors.TextBright,  "BRIGHT"),
            (FUIColors.TextPrimary, "TEXT"),
            (FUIColors.TextDim,     "DIM"),
            (FUIColors.Frame,       "FRAME"),
        ];
        for (int i = 0; i < row2.Length; i++)
        {
            float sx = leftMargin + i * (swatchW + swatchGap);
            var rect = new SKRect(sx, y, sx + swatchW, y + swatchH);
            using var fill = FUIRenderer.CreateFillPaint(row2[i].color);
            canvas.DrawRect(rect, fill);
            // Dark label band at bottom
            var band = new SKRect(rect.Left, rect.Bottom - 14f, rect.Right, rect.Bottom);
            using var bandFill = FUIRenderer.CreateFillPaint(new SKColor(0, 0, 0, 130));
            canvas.DrawRect(band, bandFill);
            float lblW = FUIRenderer.MeasureText(row2[i].label, 9f);
            FUIRenderer.DrawText(canvas, row2[i].label,
                new SKPoint(sx + swatchW / 2f - lblW / 2f, rect.Bottom - 2f),
                new SKColor(0xFF, 0xFF, 0xFF, 200), 9f);
        }
        y += swatchH + 4;

        // Background effects section
        FUIWidgets.DrawSectionLabel(canvas, "BACKGROUND", leftMargin, ref y);

        string[] sliderLabels = { "Grid", "Glow", "Noise", "Scanlines", "Vignette" };
        float maxLabelWidth = 0;
        foreach (var label in sliderLabels)
        {
            float w = FUIRenderer.MeasureText(label, 14f);
            if (w > maxLabelWidth) maxLabelWidth = w;
        }

        float labelColumnWidth = maxLabelWidth + 10f;
        float valueColumnWidth = FUIRenderer.MeasureText("100", 13f) + 8f;
        float sliderLeft = leftMargin + labelColumnWidth;
        float sliderRight = rightMargin - valueColumnWidth;
        float sliderRowHeight = 22f;
        float sliderRowGap = 8f;

        if (sliderRight - sliderLeft < 50)
        {
            sliderLeft = leftMargin + 50;
            sliderRight = rightMargin - 30;
        }

        float sliderHeight = 12f;
        float sliderYOff = (sliderRowHeight - sliderHeight) / 2;
        float textY = sliderRowHeight / 2 + 4;

        var bg = _ctx.Background;

        FUIRenderer.DrawTextTruncated(canvas, "Grid", new SKPoint(leftMargin, y + textY), labelColumnWidth - 5, FUIColors.TextPrimary, 14f);
        _bgGridSliderBounds = new SKRect(sliderLeft, y + sliderYOff, sliderRight, y + sliderYOff + sliderHeight);
        FUIWidgets.DrawSettingsSlider(canvas, _bgGridSliderBounds, bg.GridStrength, 100);
        FUIRenderer.DrawText(canvas, bg.GridStrength.ToString(), new SKPoint(sliderRight + 8, y + textY), FUIColors.TextDim, 13f);
        y += sliderRowHeight + sliderRowGap;

        FUIRenderer.DrawTextTruncated(canvas, "Glow", new SKPoint(leftMargin, y + textY), labelColumnWidth - 5, FUIColors.TextPrimary, 14f);
        _bgGlowSliderBounds = new SKRect(sliderLeft, y + sliderYOff, sliderRight, y + sliderYOff + sliderHeight);
        FUIWidgets.DrawSettingsSlider(canvas, _bgGlowSliderBounds, bg.GlowIntensity, 100);
        FUIRenderer.DrawText(canvas, bg.GlowIntensity.ToString(), new SKPoint(sliderRight + 8, y + textY), FUIColors.TextDim, 13f);
        y += sliderRowHeight + sliderRowGap;

        FUIRenderer.DrawTextTruncated(canvas, "Noise", new SKPoint(leftMargin, y + textY), labelColumnWidth - 5, FUIColors.TextPrimary, 14f);
        _bgNoiseSliderBounds = new SKRect(sliderLeft, y + sliderYOff, sliderRight, y + sliderYOff + sliderHeight);
        FUIWidgets.DrawSettingsSlider(canvas, _bgNoiseSliderBounds, bg.NoiseIntensity, 100);
        FUIRenderer.DrawText(canvas, bg.NoiseIntensity.ToString(), new SKPoint(sliderRight + 8, y + textY), FUIColors.TextDim, 13f);
        y += sliderRowHeight + sliderRowGap;

        FUIRenderer.DrawTextTruncated(canvas, "Scanlines", new SKPoint(leftMargin, y + textY), labelColumnWidth - 5, FUIColors.TextPrimary, 14f);
        _bgScanlineSliderBounds = new SKRect(sliderLeft, y + sliderYOff, sliderRight, y + sliderYOff + sliderHeight);
        FUIWidgets.DrawSettingsSlider(canvas, _bgScanlineSliderBounds, bg.ScanlineIntensity, 100);
        FUIRenderer.DrawText(canvas, bg.ScanlineIntensity.ToString(), new SKPoint(sliderRight + 8, y + textY), FUIColors.TextDim, 13f);
        y += sliderRowHeight + sliderRowGap;

        FUIRenderer.DrawTextTruncated(canvas, "Vignette", new SKPoint(leftMargin, y + textY), labelColumnWidth - 5, FUIColors.TextPrimary, 14f);
        _bgVignetteSliderBounds = new SKRect(sliderLeft, y + sliderYOff, sliderRight, y + sliderYOff + sliderHeight);
        FUIWidgets.DrawSettingsSlider(canvas, _bgVignetteSliderBounds, bg.VignetteStrength, 100);
        FUIRenderer.DrawText(canvas, bg.VignetteStrength.ToString(), new SKPoint(sliderRight + 8, y + textY), FUIColors.TextDim, 13f);
        y += sliderRowHeight + sectionSpacing;

    }

    private void StoreThemeButtonBounds(int index, SKRect bounds)
    {
        if (index >= 0 && index < _themeButtonBounds.Length)
        {
            _themeButtonBounds[index] = bounds;
        }
    }

    private void UpdateBgSliderFromPoint(float x)
    {
        SKRect bounds = _draggingBgSlider switch
        {
            "grid" => _bgGridSliderBounds,
            "glow" => _bgGlowSliderBounds,
            "noise" => _bgNoiseSliderBounds,
            "scanline" => _bgScanlineSliderBounds,
            "vignette" => _bgVignetteSliderBounds,
            _ => default
        };

        if (bounds == default) return;

        float ratio = Math.Clamp((x - bounds.Left) / bounds.Width, 0f, 1f);
        int value = (int)(ratio * 100);
        var bg = _ctx.Background;

        switch (_draggingBgSlider)
        {
            case "grid": bg.GridStrength = value; break;
            case "glow": bg.GlowIntensity = value; break;
            case "noise": bg.NoiseIntensity = value; break;
            case "scanline": bg.ScanlineIntensity = value; break;
            case "vignette": bg.VignetteStrength = value; break;
        }

        _ctx.BackgroundDirty = true;
        _ctx.InvalidateCanvas();
    }

    private void SaveBackgroundSettings()
    {
        var bg = _ctx.Background;
        _ctx.ThemeService.SaveBackgroundSettings(
            bg.GridStrength, bg.GlowIntensity, bg.NoiseIntensity,
            bg.ScanlineIntensity, bg.VignetteStrength);
    }

}