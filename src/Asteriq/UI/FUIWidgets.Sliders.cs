using Asteriq.Models;
using SkiaSharp;

namespace Asteriq.UI;

internal static partial class FUIWidgets
{
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

}