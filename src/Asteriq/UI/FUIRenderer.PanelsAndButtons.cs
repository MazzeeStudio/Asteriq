using Asteriq.Models;
using Asteriq.Services;
using Microsoft.Win32;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI;

public static partial class FUIRenderer
{
    public static void DrawWindowControls(SKCanvas canvas, float x, float y,
        bool minimizeHovered = false, bool maximizeHovered = false, bool closeHovered = false)
    {
        float btnSize = TouchTargetCompact;  // 32px - was 28f, meets touch target minimum
        float btnGap = SpaceSM;  // 8px

        var minBounds = new SKRect(x, y, x + btnSize, y + btnSize);
        DrawWindowControlButton(canvas, minBounds, WindowControlType.Minimize, minimizeHovered);

        var maxBounds = new SKRect(x + btnSize + btnGap, y, x + btnSize * 2 + btnGap, y + btnSize);
        DrawWindowControlButton(canvas, maxBounds, WindowControlType.Maximize, maximizeHovered);

        var closeBounds = new SKRect(x + (btnSize + btnGap) * 2, y, x + btnSize * 3 + btnGap * 2, y + btnSize);
        DrawWindowControlButton(canvas, closeBounds, WindowControlType.Close, closeHovered);
    }

    public enum WindowControlType { Minimize, Maximize, Close }

    public static void DrawWindowControlButton(SKCanvas canvas, SKRect bounds, WindowControlType type, bool isHovered)
    {
        // FUI style: chamfered corner boxes (like other FUI elements)
        var frameColor = isHovered ? FUIColors.Primary : FUIColors.Frame.WithAlpha(150);
        float chamfer = 4f; // Chamfer size for corner cut

        // Draw chamfered rectangle frame (top-right corner cut)
        using var framePaint = CreateStrokePaint(frameColor);

        using var path = new SKPath();
        path.MoveTo(bounds.Left, bounds.Top);
        path.LineTo(bounds.Right, bounds.Top);
        path.LineTo(bounds.Right, bounds.Bottom - chamfer);
        path.LineTo(bounds.Right - chamfer, bounds.Bottom);
        path.LineTo(bounds.Left, bounds.Bottom);
        path.Close();
        canvas.DrawPath(path, framePaint);

        // Icon color - brighter on hover
        var iconColor = isHovered ? FUIColors.Primary : FUIColors.TextDim;

        // Larger padding for more space around icons
        float pad = 9f;
        using var iconPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = iconColor,
            StrokeWidth = 1.5f,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Square
        };

        switch (type)
        {
            case WindowControlType.Minimize:
                // Single horizontal line positioned lower (near bottom third)
                float underscoreY = bounds.Bottom - pad - 1;
                canvas.DrawLine(bounds.Left + pad, underscoreY, bounds.Right - pad, underscoreY, iconPaint);
                break;
            case WindowControlType.Maximize:
                {
                    // Two overlapping squares (restore/maximize icon style)
                    float iconPad = pad + 1;
                    float offset = 3f;
                    // Back square (smaller, offset up-right)
                    var backRect = new SKRect(bounds.Left + iconPad + offset, bounds.Top + iconPad,
                                              bounds.Right - iconPad, bounds.Bottom - iconPad - offset);
                    canvas.DrawRect(backRect, iconPaint);
                    // Front square (offset down-left) - partial to show overlap
                    var frontRect = new SKRect(bounds.Left + iconPad, bounds.Top + iconPad + offset,
                                               bounds.Right - iconPad - offset, bounds.Bottom - iconPad);
                    // Fill area behind front square to occlude back square
                    using var fillPaint = CreateFillPaint(FUIColors.Background1);
                    canvas.DrawRect(frontRect, fillPaint);
                    canvas.DrawRect(frontRect, iconPaint);
                    break;
                }
            case WindowControlType.Close:
                // X shape
                canvas.DrawLine(bounds.Left + pad, bounds.Top + pad, bounds.Right - pad, bounds.Bottom - pad, iconPaint);
                canvas.DrawLine(bounds.Right - pad, bounds.Top + pad, bounds.Left + pad, bounds.Bottom - pad, iconPaint);
                break;
        }
    }



    public static void DrawPanelTitle(SKCanvas canvas, SKRect bounds, string prefixCode, string title,
        bool showCloseButton = false, SKColor? accentColor = null)
    {
        var accent = accentColor ?? FUIColors.Active;

        float textY = bounds.MidY + 4f;
        float textX = bounds.Left + SpaceMD;
        DrawText(canvas, prefixCode, new SKPoint(textX, textY), accent, 15f);

        using var prefixPaint = CreateTextPaint(accent, 15f);
        float prefixWidth = prefixPaint.MeasureText(prefixCode);
        DrawText(canvas, title, new SKPoint(textX + prefixWidth + SpaceSM, textY),
            FUIColors.TextBright, 17f, true);

        if (showCloseButton)
        {
            float btnSize = bounds.Height - 8;
            var closeBounds = new SKRect(bounds.Right - btnSize - 4, bounds.Top + 4, bounds.Right - 4, bounds.Bottom - 4);
            DrawWindowControlButton(canvas, closeBounds, WindowControlType.Close, false);
        }
    }

    public static void DrawPanelShadow(SKCanvas canvas, SKRect bounds, float offsetX = 4f, float offsetY = 4f, float blur = 12f)
    {
        var shadowBounds = new SKRect(bounds.Left + offsetX, bounds.Top + offsetY,
                                       bounds.Right + offsetX, bounds.Bottom + offsetY);

        using var shadowPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = new SKColor(0, 0, 0, 60),
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(blur, blur)
        };
        canvas.DrawRect(shadowBounds, shadowPaint);
    }



    /// <summary>
    /// Stores layout metrics for a panel - reduces repeated calculations
    /// </summary>
    public struct PanelMetrics
    {
        public float Y { get; set; }              // Current Y position for content
        public float LeftMargin { get; set; }     // Left content edge
        public float RightMargin { get; set; }    // Right content edge
        public float ContentWidth { get; set; }   // Available content width
        public float RowHeight { get; set; }      // Standard row height
        public float SectionSpacing { get; set; } // Space between sections
    }

    /// <summary>
    /// Draws standard panel chrome (background + L-corner frame) and returns layout metrics
    /// </summary>
    public static PanelMetrics DrawPanelChrome(SKCanvas canvas, SKRect bounds, SKColor? frameColor = null, float cornerLength = 30f)
    {
        frameColor ??= FUIColors.Frame;

        // Panel background
        using var bgPaint = CreateFillPaint(FUIColors.Background1.WithAlpha(160));
        canvas.DrawRect(bounds.Inset(FrameInset, FrameInset), bgPaint);
        DrawLCornerFrame(canvas, bounds, frameColor.Value, cornerLength, 8f);

        // Calculate standard layout metrics
        float cornerPadding = SpaceXL;  // 24px
        return new PanelMetrics
        {
            Y = bounds.Top + FrameInset + cornerPadding,
            LeftMargin = bounds.Left + FrameInset + cornerPadding,
            RightMargin = bounds.Right - FrameInset - SpaceLG,
            ContentWidth = (bounds.Right - FrameInset - SpaceLG) - (bounds.Left + FrameInset + cornerPadding),
            RowHeight = LineHeightBody,
            SectionSpacing = LineHeightBody
        };
    }

    /// <summary>
    /// Draws a panel title with glow effect and returns updated Y position
    /// </summary>
    public static float DrawPanelHeader(SKCanvas canvas, string title, float x, float y)
    {
        DrawText(canvas, title, new SKPoint(x, y), FUIColors.TextBright, FontBody, true);
        return y + LineHeightTitle;  // 36px line height for title
    }

    /// <summary>
    /// Draws a section header (caption style) and returns updated Y position
    /// </summary>
    public static float DrawSectionHeader(SKCanvas canvas, string text, float x, float y)
    {
        DrawCaption(canvas, text, new SKPoint(x, y));
        return y + LineHeightBody;  // Add standard line height after header
    }

    /// <summary>
    /// Calculates row Y positions based on line count
    /// </summary>
    public static float AdvanceRow(float currentY, float lineHeight = 0)
    {
        return currentY + (lineHeight > 0 ? lineHeight : LineHeightBody);
    }



    public enum ButtonState { Normal, Hover, Active, Disabled }

    public static void DrawButton(SKCanvas canvas, SKRect bounds, string text,
        ButtonState state, bool isDanger = false, float fontSize = 14f)
    {
        var accent = isDanger ? FUIColors.Danger : FUIColors.Active;

        SKColor bgColor, frameColor, textColor;
        bool withGlow = false;

        switch (state)
        {
            case ButtonState.Hover:
                bgColor = accent.WithAlpha(30);
                frameColor = accent;
                textColor = FUIColors.TextBright;
                withGlow = true;
                break;
            case ButtonState.Active:
                bgColor = accent.WithAlpha(60);
                frameColor = accent;
                textColor = FUIColors.TextBright;
                withGlow = true;
                break;
            case ButtonState.Disabled:
                bgColor = FUIColors.Background2;
                frameColor = FUIColors.FrameDim;
                textColor = FUIColors.TextDisabled;
                break;
            default:
                bgColor = FUIColors.Background2;
                frameColor = FUIColors.Frame;
                textColor = FUIColors.TextPrimary;
                break;
        }

        using var bgPath = CreateFrame(bounds, ChamferSizeSmall);
        using var bgPaint = CreateFillPaint(bgColor);
        canvas.DrawPath(bgPath, bgPaint);

        if (withGlow)
        {
            using var glowPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = frameColor.WithAlpha(50),
                StrokeWidth = 6f,
                IsAntialias = true,
                ImageFilter = SKImageFilter.CreateBlur(GlowRadius, GlowRadius)
            };
            canvas.DrawPath(bgPath, glowPaint);
        }

        using var framePaint = CreateStrokePaint(frameColor, LineWeight);
        canvas.DrawPath(bgPath, framePaint);

        DrawTextCentered(canvas, text, bounds, textColor, fontSize, withGlow && state == ButtonState.Active);
    }

    public static void DrawTabButtonRow(SKCanvas canvas, float x, float y, int count, int activeIndex,
        float buttonSize = 24f, float gap = 4f)
    {
        for (int i = 0; i < count; i++)
        {
            var bounds = new SKRect(x + i * (buttonSize + gap), y, x + i * (buttonSize + gap) + buttonSize, y + buttonSize);
            DrawTabButton(canvas, bounds, (i + 1).ToString("00"), i == activeIndex);
        }
    }

    public static void DrawTabButton(SKCanvas canvas, SKRect bounds, string label, bool isActive, bool isHovered = false)
    {
        var bgColor = isActive ? FUIColors.Active : (isHovered ? FUIColors.Primary.WithAlpha(20) : FUIColors.Background2);
        var frameColor = isActive ? FUIColors.Active : (isHovered ? FUIColors.FrameBright : FUIColors.FrameDim);
        var textColor = isActive ? FUIColors.Void : (isHovered ? FUIColors.TextBright : FUIColors.TextDim);

        using var bgPaint = CreateFillPaint(bgColor);
        canvas.DrawRect(bounds, bgPaint);

        if (!isActive)
        {
            using var framePaint = CreateStrokePaint(frameColor, LineWeightThin);
            canvas.DrawRect(bounds, framePaint);
        }

        DrawTextCentered(canvas, label, bounds, textColor, 13f);
    }



    public static void DrawStatusBadge(SKCanvas canvas, SKRect bounds, string text, bool isPositive)
    {
        var bgColor = isPositive ? FUIColors.Success.WithAlpha(40) : FUIColors.DangerTint;
        var textColor = isPositive ? FUIColors.Success : FUIColors.Danger;

        using var bgPaint = CreateFillPaint(bgColor);
        canvas.DrawRect(bounds, bgPaint);

        DrawTextCentered(canvas, text, bounds, textColor, 12f);
    }



}