using Asteriq.Models;
using Asteriq.Services;
using Microsoft.Win32;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI;

public static partial class FUIRenderer
{
    public static SKPaint CreateTextPaint(SKColor color, float size = 17f,
        bool bold = false, bool withGlow = false, SKTypeface? typeface = null)
    {
        // Select font family based on user preference
        string fontName = _fontFamily == UIFontFamily.Carbon ? "Carbon" : "Consolas";

        var paint = new SKPaint
        {
            Color = color,
            TextSize = size,
            IsAntialias = true,
            Typeface = typeface ?? SKTypeface.FromFamilyName(fontName,
                bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal,
                SKFontStyleSlant.Upright),
            SubpixelText = true
        };

        if (withGlow)
        {
            paint.ImageFilter = SKImageFilter.CreateBlur(2f, 2f);
        }

        return paint;
    }

    public static void DrawText(SKCanvas canvas, string text, SKPoint position,
        SKColor color, float size = 17f, bool withGlow = false, SKTypeface? typeface = null, bool scaleFont = true)
    {
        float scaledSize = size;

        if (withGlow)
        {
            using var glowPaint = CreateTextPaint(color.WithAlpha(80), scaledSize, false, true, typeface);
            canvas.DrawText(text, position.X, position.Y, glowPaint);
        }

        using var paint = CreateTextPaint(color, scaledSize, false, false, typeface);
        canvas.DrawText(text, position.X, position.Y, paint);
    }

    public static void DrawTextCentered(SKCanvas canvas, string text, SKRect bounds,
        SKColor color, float size = 17f, bool withGlow = false, bool scaleFont = true)
    {
        float scaledSize = size;

        using var paint = CreateTextPaint(color, scaledSize);
        float textWidth = paint.MeasureText(text);
        float x = bounds.Left + (bounds.Width - textWidth) / 2;
        float y = bounds.MidY + scaledSize / 3;

        DrawText(canvas, text, new SKPoint(x, y), color, size, withGlow, null, scaleFont);
    }

    /// <summary>
    /// Measures the width of text at the given font size
    /// </summary>
    public static float MeasureText(string text, float size = 17f, bool scaleFont = true)
    {
        float scaledSize = size;
        using var paint = CreateTextPaint(SKColors.White, scaledSize);
        return paint.MeasureText(text);
    }

    /// <summary>
    /// Truncates text with ellipsis if it exceeds the maximum width
    /// </summary>
    public static string TruncateText(string text, float maxWidth, float size = 17f, bool scaleFont = true)
    {
        if (string.IsNullOrEmpty(text)) return text;

        float textWidth = MeasureText(text, size, scaleFont);
        if (textWidth <= maxWidth) return text;

        // Binary search for the right length
        string ellipsis = "...";
        float ellipsisWidth = MeasureText(ellipsis, size, scaleFont);
        float availableWidth = maxWidth - ellipsisWidth;

        if (availableWidth <= 0) return ellipsis;

        int low = 0;
        int high = text.Length;
        int bestFit = 0;

        while (low <= high)
        {
            int mid = (low + high) / 2;
            string testText = text.Substring(0, mid);
            float testWidth = MeasureText(testText, size, scaleFont);

            if (testWidth <= availableWidth)
            {
                bestFit = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return bestFit > 0 ? string.Concat(text.AsSpan(0, bestFit), ellipsis) : ellipsis;
    }

    /// <summary>
    /// Draws text, truncating with ellipsis if it exceeds the maximum width
    /// </summary>
    public static void DrawTextTruncated(SKCanvas canvas, string text, SKPoint position, float maxWidth,
        SKColor color, float size = 17f, bool withGlow = false, bool scaleFont = true)
    {
        string truncated = TruncateText(text, maxWidth, size, scaleFont);
        DrawText(canvas, truncated, position, color, size, withGlow, null, scaleFont);
    }

    /// <summary>
    /// Calculates the minimum width needed for a label-control row
    /// </summary>
    public static float CalculateLabelWidth(string label, float size = 14f, float minPadding = 10f)
    {
        return MeasureText(label, size) + minPadding;
    }

    /// <summary>
    /// Draws a label-value pair row with proper spacing.
    /// Returns the X position where the value ends.
    /// </summary>
    public static float DrawLabelValueRow(SKCanvas canvas, float x, float y, float rowWidth,
        string label, string value, float fontSize = 14f,
        SKColor? labelColor = null, SKColor? valueColor = null)
    {
        labelColor ??= FUIColors.TextPrimary;
        valueColor ??= FUIColors.TextDim;

        float labelWidth = MeasureText(label, fontSize);
        float valueWidth = MeasureText(value, fontSize);
        float minGap = 10f;

        // If total width exceeds available space, truncate label
        float availableForLabel = rowWidth - valueWidth - minGap;
        if (labelWidth > availableForLabel && availableForLabel > 30)
        {
            DrawTextTruncated(canvas, label, new SKPoint(x, y), availableForLabel, labelColor.Value, fontSize);
        }
        else
        {
            DrawText(canvas, label, new SKPoint(x, y), labelColor.Value, fontSize);
        }

        // Draw value right-aligned
        float valueX = x + rowWidth - valueWidth;
        DrawText(canvas, value, new SKPoint(valueX, y), valueColor.Value, fontSize);

        return valueX + valueWidth;
    }



    /// <summary>
    /// Draws caption text (12px) - for labels, secondary text, metadata
    /// This is the minimum readable size per Windows UX guidelines
    /// </summary>
    public static void DrawCaption(SKCanvas canvas, string text, SKPoint position,
        SKColor? color = null, bool withGlow = false)
    {
        DrawText(canvas, text, position, color ?? FUIColors.TextDim, FontCaption, withGlow);
    }

    /// <summary>
    /// Draws body text (14px) - for primary content
    /// </summary>
    public static void DrawBody(SKCanvas canvas, string text, SKPoint position,
        SKColor? color = null, bool withGlow = false)
    {
        DrawText(canvas, text, position, color ?? FUIColors.TextPrimary, FontBody, withGlow);
    }

    /// <summary>
    /// Draws subtitle text (20px semibold) - for section headers
    /// </summary>
    public static void DrawSubtitle(SKCanvas canvas, string text, SKPoint position,
        SKColor? color = null, bool withGlow = false)
    {
        // Note: Currently using same weight, could add bold parameter if needed
        DrawText(canvas, text, position, color ?? FUIColors.TextBright, FontSubtitle, withGlow);
    }

    /// <summary>
    /// Draws title text (28px) - for panel/page titles
    /// </summary>
    public static void DrawTitle(SKCanvas canvas, string text, SKPoint position,
        SKColor? color = null, bool withGlow = false)
    {
        DrawText(canvas, text, position, color ?? FUIColors.TextBright, FontTitle, withGlow);
    }

    /// <summary>
    /// Gets the scaled line height for a given typography style
    /// </summary>
    public static float GetLineHeight(float fontSize)
    {
        // Map font sizes to their line heights
        return fontSize switch
        {
            <= FontCaption => LineHeightCaption,
            <= FontBody => LineHeightBody,
            <= FontBodyLarge => LineHeightBodyLarge,
            <= FontSubtitle => LineHeightSubtitle,
            _ => LineHeightTitle
        };
    }



}