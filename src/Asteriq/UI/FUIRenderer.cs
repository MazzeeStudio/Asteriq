using Asteriq.Services;
using SkiaSharp;

namespace Asteriq.UI;

/// <summary>
/// Core FUI rendering primitives.
/// All drawing is done with these primitives to maintain visual consistency.
/// </summary>
public static class FUIRenderer
{
    // Corner style options
    public enum CornerStyle { Rounded, Hard, Chamfered }
    public static CornerStyle CurrentCornerStyle = CornerStyle.Chamfered;

    // Font size scaling for accessibility
    private static FontSizeOption _fontSizeOption = FontSizeOption.Medium;

    /// <summary>
    /// Current font size option (Small/Medium/Large)
    /// </summary>
    public static FontSizeOption FontSizeOption
    {
        get => _fontSizeOption;
        set => _fontSizeOption = value;
    }

    /// <summary>
    /// Get the font size offset based on current setting
    /// Small = 0, Medium = +2, Large = +4
    /// </summary>
    public static float FontSizeOffset => _fontSizeOption switch
    {
        FontSizeOption.Small => 0f,
        FontSizeOption.Medium => 2f,
        FontSizeOption.Large => 4f,
        _ => 2f
    };

    /// <summary>
    /// Scale a font size according to the current font size setting
    /// </summary>
    public static float ScaleFont(float baseSize) => baseSize + FontSizeOffset;

    /// <summary>
    /// Scale spacing/padding to account for larger fonts
    /// Uses a smaller multiplier than fonts to avoid excessive spacing
    /// </summary>
    public static float ScaleSpacing(float baseSpacing) => baseSpacing + FontSizeOffset * 0.5f;

    /// <summary>
    /// Scale line height for text rows
    /// </summary>
    public static float ScaleLineHeight(float baseHeight) => baseHeight + FontSizeOffset;

    // Standard measurements
    public const float CornerRadius = 8f;
    public const float CornerRadiusSmall = 4f;
    public const float CornerRadiusLarge = 12f;
    public const float ChamferSize = 8f;
    public const float ChamferSizeSmall = 5f;
    public const float ChamferSizeLarge = 12f;
    public const float BracketSize = 8f;
    public const float BracketGap = 3f;
    public const float LineWeight = 1.5f;
    public const float LineWeightThin = 1f;
    public const float LineWeightThick = 2f;
    public const float GlowRadius = 8f;
    public const float GlowRadiusLarge = 16f;

    // Spacing system
    public const float SpaceXS = 4f;
    public const float SpaceSM = 8f;
    public const float SpaceMD = 16f;
    public const float SpaceLG = 24f;
    public const float SpaceXL = 32f;
    public const float PanelPadding = 16f;
    public const float ItemSpacing = 12f;
    public const float SectionSpacing = 24f;
    public const float FrameInset = 5f;

    #region Frame Drawing

    public static SKPath CreateFrame(SKRect bounds, float cornerSize = CornerRadius)
    {
        var path = new SKPath();

        if (CurrentCornerStyle == CornerStyle.Chamfered && cornerSize > 0)
        {
            float c = cornerSize;
            path.MoveTo(bounds.Left, bounds.Top);
            path.LineTo(bounds.Right, bounds.Top);
            path.LineTo(bounds.Right, bounds.Bottom - c);
            path.LineTo(bounds.Right - c, bounds.Bottom);
            path.LineTo(bounds.Left, bounds.Bottom);
            path.Close();
        }
        else if (CurrentCornerStyle == CornerStyle.Hard || cornerSize <= 0)
        {
            path.AddRect(bounds);
        }
        else
        {
            path.AddRoundRect(bounds, cornerSize, cornerSize);
        }

        return path;
    }

    public static void DrawLCornerFrame(SKCanvas canvas, SKRect bounds, SKColor color,
        float cornerLength = 30f, float chamfer = ChamferSize, float lineWeight = LineWeight, bool withGlow = false)
    {
        if (withGlow)
        {
            using var glowPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = color.WithAlpha(50),
                StrokeWidth = lineWeight + 4f,
                IsAntialias = true,
                ImageFilter = SKImageFilter.CreateBlur(GlowRadius, GlowRadius)
            };
            DrawLCornerPaths(canvas, bounds, cornerLength, chamfer, glowPaint);
        }

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = lineWeight,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Square
        };
        DrawLCornerPaths(canvas, bounds, cornerLength, chamfer, paint);
    }

    private static void DrawLCornerPaths(SKCanvas canvas, SKRect bounds, float cornerLength, float chamfer, SKPaint paint)
    {
        canvas.DrawLine(bounds.Left, bounds.Top + cornerLength, bounds.Left, bounds.Top, paint);
        canvas.DrawLine(bounds.Left, bounds.Top, bounds.Left + cornerLength, bounds.Top, paint);

        float c = chamfer;
        using var path = new SKPath();
        path.MoveTo(bounds.Right - cornerLength, bounds.Bottom);
        path.LineTo(bounds.Right - c, bounds.Bottom);
        path.LineTo(bounds.Right, bounds.Bottom - c);
        path.LineTo(bounds.Right, bounds.Bottom - cornerLength);
        canvas.DrawPath(path, paint);
    }

    public static void DrawFrame(SKCanvas canvas, SKRect bounds, SKColor color,
        float cornerRadius = CornerRadius, float lineWeight = LineWeight, bool withGlow = false)
    {
        using var path = CreateFrame(bounds, cornerRadius);

        if (withGlow)
        {
            using var glowPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = color.WithAlpha(60),
                StrokeWidth = lineWeight + 4f,
                IsAntialias = true,
                ImageFilter = SKImageFilter.CreateBlur(GlowRadius, GlowRadius)
            };
            canvas.DrawPath(path, glowPaint);
        }

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = lineWeight,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };
        canvas.DrawPath(path, paint);
    }

    public static void FillFrame(SKCanvas canvas, SKRect bounds, SKColor fillColor,
        float cornerRadius = CornerRadius)
    {
        using var path = CreateFrame(bounds, cornerRadius);

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(bounds.MidX, bounds.Top),
                new SKPoint(bounds.MidX, bounds.Bottom),
                new[] { fillColor.WithAlpha((byte)Math.Min(255, fillColor.Alpha * 1.2f)), fillColor },
                SKShaderTileMode.Clamp)
        };
        canvas.DrawPath(path, paint);
    }

    public static void DrawAngularFrame(SKCanvas canvas, SKRect bounds, SKColor color,
        float cornerRadius = CornerRadius, float lineWeight = LineWeight, bool withGlow = false)
        => DrawFrame(canvas, bounds, color, cornerRadius, lineWeight, withGlow);

    public static void FillAngularFrame(SKCanvas canvas, SKRect bounds, SKColor fillColor,
        float cornerRadius = CornerRadius)
        => FillFrame(canvas, bounds, fillColor, cornerRadius);

    #endregion

    #region Glow Effects

    public static SKPaint CreateGlowPaint(SKColor color, float radius = GlowRadius)
    {
        return new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = color,
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(radius, radius)
        };
    }

    public static void DrawGlowingLine(SKCanvas canvas, SKPoint start, SKPoint end,
        SKColor color, float lineWeight = LineWeight, float glowRadius = GlowRadius)
    {
        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color.WithAlpha(80),
            StrokeWidth = lineWeight + 3f,
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(glowRadius, glowRadius)
        };
        canvas.DrawLine(start, end, glowPaint);

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = lineWeight,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Square
        };
        canvas.DrawLine(start, end, paint);
    }

    public static void DrawGlowingDot(SKCanvas canvas, SKPoint center, SKColor color,
        float radius = 4f, float glowRadius = GlowRadius)
    {
        using var glowPaint = CreateGlowPaint(color.WithAlpha(100), glowRadius);
        canvas.DrawCircle(center, radius + glowRadius / 2, glowPaint);

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = color,
            IsAntialias = true
        };
        canvas.DrawCircle(center, radius, paint);
    }

    #endregion

    #region Scan Line Effects

    public static void DrawScanLine(SKCanvas canvas, SKRect bounds, float progress,
        SKColor color, float thickness = 2f)
    {
        float y = bounds.Top + bounds.Height * progress;

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thickness,
            Color = color.WithAlpha((byte)(color.Alpha * 0.4f)),
            IsAntialias = true
        };
        canvas.DrawLine(bounds.Left, y, bounds.Right, y, paint);

        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thickness + 4f,
            Color = color.WithAlpha((byte)(color.Alpha * 0.15f)),
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(6f, 6f)
        };
        canvas.DrawLine(bounds.Left, y, bounds.Right, y, glowPaint);
    }

    public static void DrawScanLineOverlay(SKCanvas canvas, SKRect bounds,
        float lineSpacing = 3f, byte alpha = 8)
    {
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(0, 0, 0, alpha),
            StrokeWidth = 1f
        };

        for (float y = bounds.Top; y < bounds.Bottom; y += lineSpacing)
        {
            canvas.DrawLine(bounds.Left, y, bounds.Right, y, paint);
        }
    }

    #endregion

    #region Grid Background

    public static void DrawDotGrid(SKCanvas canvas, SKRect bounds, float spacing = 20f, SKColor? color = null)
    {
        var gridColor = color ?? FUIColors.Grid;

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = gridColor,
            IsAntialias = true
        };

        for (float x = bounds.Left; x <= bounds.Right; x += spacing)
        {
            for (float y = bounds.Top; y <= bounds.Bottom; y += spacing)
            {
                canvas.DrawCircle(x, y, 1f, paint);
            }
        }
    }

    public static void DrawLineGrid(SKCanvas canvas, SKRect bounds, float spacing = 40f, SKColor? color = null)
    {
        var gridColor = color ?? FUIColors.Grid;

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = gridColor,
            StrokeWidth = 0.5f,
            IsAntialias = true
        };

        for (float x = bounds.Left; x <= bounds.Right; x += spacing)
        {
            canvas.DrawLine(x, bounds.Top, x, bounds.Bottom, paint);
        }

        for (float y = bounds.Top; y <= bounds.Bottom; y += spacing)
        {
            canvas.DrawLine(bounds.Left, y, bounds.Right, y, paint);
        }
    }

    #endregion

    #region Data Visualization

    public static void DrawDataBar(SKCanvas canvas, SKRect bounds, float value,
        SKColor fillColor, SKColor frameColor, bool horizontal = true)
    {
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = frameColor,
            StrokeWidth = LineWeightThin,
            IsAntialias = true
        };
        canvas.DrawRect(bounds, framePaint);

        value = Math.Clamp(value, 0f, 1f);
        SKRect fillRect;

        if (horizontal)
        {
            fillRect = new SKRect(bounds.Left + 1, bounds.Top + 1,
                                  bounds.Left + 1 + (bounds.Width - 2) * value, bounds.Bottom - 1);
        }
        else
        {
            float fillHeight = (bounds.Height - 2) * value;
            fillRect = new SKRect(bounds.Left + 1, bounds.Bottom - 1 - fillHeight,
                                  bounds.Right - 1, bounds.Bottom - 1);
        }

        if (fillRect.Width > 0 && fillRect.Height > 0)
        {
            using var glowPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = fillColor.WithAlpha(60),
                ImageFilter = SKImageFilter.CreateBlur(4f, 4f)
            };
            canvas.DrawRect(fillRect, glowPaint);

            using var fillPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = fillColor,
                IsAntialias = true
            };
            canvas.DrawRect(fillRect, fillPaint);
        }
    }

    #endregion

    #region Text

    public static SKPaint CreateTextPaint(SKColor color, float size = 14f,
        bool bold = false, bool withGlow = false, SKTypeface? typeface = null)
    {
        var paint = new SKPaint
        {
            Color = color,
            TextSize = size,
            IsAntialias = true,
            Typeface = typeface ?? SKTypeface.FromFamilyName("Consolas",
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
        SKColor color, float size = 14f, bool withGlow = false, SKTypeface? typeface = null, bool scaleFont = true)
    {
        float scaledSize = scaleFont ? ScaleFont(size) : size;

        if (withGlow)
        {
            using var glowPaint = CreateTextPaint(color.WithAlpha(80), scaledSize, false, true, typeface);
            canvas.DrawText(text, position.X, position.Y, glowPaint);
        }

        using var paint = CreateTextPaint(color, scaledSize, false, false, typeface);
        canvas.DrawText(text, position.X, position.Y, paint);
    }

    public static void DrawTextCentered(SKCanvas canvas, string text, SKRect bounds,
        SKColor color, float size = 14f, bool withGlow = false, bool scaleFont = true)
    {
        float scaledSize = scaleFont ? ScaleFont(size) : size;

        using var paint = CreateTextPaint(color, scaledSize);
        float textWidth = paint.MeasureText(text);
        float x = bounds.Left + (bounds.Width - textWidth) / 2;
        float y = bounds.MidY + scaledSize / 3;

        DrawText(canvas, text, new SKPoint(x, y), color, size, withGlow, null, scaleFont);
    }

    /// <summary>
    /// Measures the width of text at the given font size
    /// </summary>
    public static float MeasureText(string text, float size = 14f, bool scaleFont = true)
    {
        float scaledSize = scaleFont ? ScaleFont(size) : size;
        using var paint = CreateTextPaint(SKColors.White, scaledSize);
        return paint.MeasureText(text);
    }

    #endregion

    #region Window Controls

    public static void DrawWindowControls(SKCanvas canvas, float x, float y,
        bool minimizeHovered = false, bool maximizeHovered = false, bool closeHovered = false)
    {
        float btnSize = 28f;
        float btnGap = 8f;

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
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = frameColor,
            StrokeWidth = 1f,
            IsAntialias = true
        };

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
                    using var fillPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Fill,
                        Color = FUIColors.Background1,
                        IsAntialias = true
                    };
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

    #endregion

    #region Panel Components

    public static void DrawPanelTitle(SKCanvas canvas, SKRect bounds, string prefixCode, string title,
        bool showCloseButton = false, SKColor? accentColor = null)
    {
        var accent = accentColor ?? FUIColors.Active;

        float textY = bounds.MidY + 4f;
        float textX = bounds.Left + SpaceMD;
        DrawText(canvas, prefixCode, new SKPoint(textX, textY), accent, 12f);

        using var prefixPaint = CreateTextPaint(accent, 12f);
        float prefixWidth = prefixPaint.MeasureText(prefixCode);
        DrawText(canvas, title, new SKPoint(textX + prefixWidth + SpaceSM, textY),
            FUIColors.TextBright, 14f, true);

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

    #endregion

    #region Buttons

    public enum ButtonState { Normal, Hover, Active, Disabled }

    public static void DrawButton(SKCanvas canvas, SKRect bounds, string text,
        ButtonState state, SKColor? accentColor = null)
    {
        var accent = accentColor ?? FUIColors.Active;

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
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = bgColor,
            IsAntialias = true
        };
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

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = frameColor,
            StrokeWidth = LineWeight,
            IsAntialias = true
        };
        canvas.DrawPath(bgPath, framePaint);

        DrawTextCentered(canvas, text, bounds, textColor, 11f, withGlow && state == ButtonState.Active);
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

        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = bgColor,
            IsAntialias = true
        };
        canvas.DrawRect(bounds, bgPaint);

        if (!isActive)
        {
            using var framePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = frameColor,
                StrokeWidth = LineWeightThin,
                IsAntialias = true
            };
            canvas.DrawRect(bounds, framePaint);
        }

        DrawTextCentered(canvas, label, bounds, textColor, 10f);
    }

    #endregion

    #region Status Indicators

    public static void DrawStatusBadge(SKCanvas canvas, SKRect bounds, string text, bool isPositive)
    {
        var bgColor = isPositive ? FUIColors.Success.WithAlpha(40) : FUIColors.Danger.WithAlpha(40);
        var textColor = isPositive ? FUIColors.Success : FUIColors.Danger;

        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = bgColor,
            IsAntialias = true
        };
        canvas.DrawRect(bounds, bgPaint);

        DrawTextCentered(canvas, text, bounds, textColor, 9f);
    }

    #endregion

    #region Lead Lines and Callouts

    /// <summary>
    /// Draws a lead line (callout line) from a label to a target point.
    /// FUI standard pattern: horizontal shelf → 90° vertical → horizontal to target
    ///
    /// LABEL ──────┐
    ///             │
    ///             └──────● target
    ///
    /// Never diagonal. Always orthogonal segments.
    /// </summary>
    public static void DrawLeadLine(SKCanvas canvas, SKPoint labelEnd, SKPoint target,
        SKColor color, float progress = 1f, bool showEndpoint = true, float lineWeight = LineWeightThin)
    {
        progress = Math.Clamp(progress, 0f, 1f);
        if (progress <= 0) return;

        // Calculate the path segments
        // Segment 1: Horizontal from label end to above/below target X
        // Segment 2: Vertical down/up to target Y
        // Segment 3: Horizontal to target

        float horizontalExtend = 20f; // How far horizontal before turning down
        float verticalX = target.X - horizontalExtend; // X position for vertical segment

        var point1 = labelEnd;
        var point2 = new SKPoint(verticalX, labelEnd.Y);  // End of first horizontal
        var point3 = new SKPoint(verticalX, target.Y);     // End of vertical (corner)
        var point4 = target;

        // Calculate total path length for animation
        float seg1Len = Math.Abs(point2.X - point1.X);
        float seg2Len = Math.Abs(point3.Y - point2.Y);
        float seg3Len = Math.Abs(point4.X - point3.X);
        float totalLen = seg1Len + seg2Len + seg3Len;

        if (totalLen < 1f) return;

        float seg1Ratio = seg1Len / totalLen;
        float seg2Ratio = seg2Len / totalLen;
        // seg3Ratio = 1 - seg1Ratio - seg2Ratio

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = lineWeight,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Square
        };

        using var path = new SKPath();
        path.MoveTo(point1);

        SKPoint currentEnd = point1;

        if (progress <= seg1Ratio)
        {
            // Drawing segment 1
            float segProgress = progress / seg1Ratio;
            currentEnd = Lerp(point1, point2, segProgress);
            path.LineTo(currentEnd);
        }
        else if (progress <= seg1Ratio + seg2Ratio)
        {
            // Drawing segment 2
            path.LineTo(point2);
            float segProgress = (progress - seg1Ratio) / seg2Ratio;
            currentEnd = Lerp(point2, point3, segProgress);
            path.LineTo(currentEnd);
        }
        else
        {
            // Drawing segment 3
            path.LineTo(point2);
            path.LineTo(point3);
            float segProgress = (progress - seg1Ratio - seg2Ratio) / (1f - seg1Ratio - seg2Ratio);
            currentEnd = Lerp(point3, point4, segProgress);
            path.LineTo(currentEnd);
        }

        canvas.DrawPath(path, paint);

        // Draw endpoint dot at current end position
        if (showEndpoint && progress > 0.05f)
        {
            DrawLeadLineEndpoint(canvas, currentEnd, color);
        }
    }

    /// <summary>
    /// Draws the small circle/dot at the end of a lead line
    /// Simple clean circle with subtle glow
    /// </summary>
    public static void DrawLeadLineEndpoint(SKCanvas canvas, SKPoint position, SKColor color, float radius = 3f)
    {
        // Glow first (behind)
        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = color.WithAlpha(50),
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(3f, 3f)
        };
        canvas.DrawCircle(position.X, position.Y, radius + 2f, glowPaint);

        // Outer ring
        using var ringPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = 1.5f,
            IsAntialias = true
        };
        canvas.DrawCircle(position.X, position.Y, radius, ringPaint);

        // Inner filled dot
        using var dotPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = color,
            IsAntialias = true
        };
        canvas.DrawCircle(position.X, position.Y, radius * 0.35f, dotPaint);
    }

    /// <summary>
    /// Draws a complete callout with label on horizontal shelf and lead line to target.
    /// Label sits horizontally, line goes: horizontal → vertical → horizontal to target
    /// </summary>
    public static void DrawCallout(SKCanvas canvas, string label, SKPoint labelPos, SKPoint target,
        SKColor color, float progress = 1f, float fontSize = 10f)
    {
        // Draw label
        DrawText(canvas, label, labelPos, color, fontSize);

        // Calculate label end position (after text)
        using var textPaint = CreateTextPaint(color, fontSize);
        float textWidth = textPaint.MeasureText(label);
        var labelEnd = new SKPoint(labelPos.X + textWidth + 8, labelPos.Y - fontSize / 3);

        // Draw lead line (always uses standard FUI pattern now)
        DrawLeadLine(canvas, labelEnd, target, color, progress);
    }

    /// <summary>
    /// Draws a callout from the right side (label after target)
    /// Line goes: target → horizontal → vertical → horizontal to label
    /// </summary>
    public static void DrawCalloutFromRight(SKCanvas canvas, string label, SKPoint labelPos, SKPoint target,
        SKColor color, float progress = 1f, float fontSize = 10f)
    {
        // For right-side callouts, we draw the line first, then the label
        // The geometry is mirrored

        float horizontalExtend = 20f;
        float verticalX = target.X + horizontalExtend;

        var point1 = target;
        var point2 = new SKPoint(verticalX, target.Y);
        var point3 = new SKPoint(verticalX, labelPos.Y - fontSize / 3);
        var point4 = new SKPoint(labelPos.X - 8, labelPos.Y - fontSize / 3);

        float seg1Len = Math.Abs(point2.X - point1.X);
        float seg2Len = Math.Abs(point3.Y - point2.Y);
        float seg3Len = Math.Abs(point4.X - point3.X);
        float totalLen = seg1Len + seg2Len + seg3Len;

        if (totalLen < 1f) return;

        progress = Math.Clamp(progress, 0f, 1f);

        float seg1Ratio = seg1Len / totalLen;
        float seg2Ratio = seg2Len / totalLen;

        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = LineWeightThin,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Square
        };

        using var path = new SKPath();
        path.MoveTo(point1);

        SKPoint currentEnd = point1;

        if (progress <= seg1Ratio)
        {
            float segProgress = progress / seg1Ratio;
            currentEnd = Lerp(point1, point2, segProgress);
            path.LineTo(currentEnd);
        }
        else if (progress <= seg1Ratio + seg2Ratio)
        {
            path.LineTo(point2);
            float segProgress = (progress - seg1Ratio) / seg2Ratio;
            currentEnd = Lerp(point2, point3, segProgress);
            path.LineTo(currentEnd);
        }
        else
        {
            path.LineTo(point2);
            path.LineTo(point3);
            float segProgress = (progress - seg1Ratio - seg2Ratio) / (1f - seg1Ratio - seg2Ratio);
            currentEnd = Lerp(point3, point4, segProgress);
            path.LineTo(currentEnd);
        }

        canvas.DrawPath(path, paint);

        // Draw endpoint at target (start of line)
        if (progress > 0.05f)
        {
            DrawLeadLineEndpoint(canvas, point1, color);
        }

        // Draw label
        DrawText(canvas, label, labelPos, color, fontSize);
    }

    private static SKPoint Lerp(SKPoint a, SKPoint b, float t)
    {
        return new SKPoint(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t
        );
    }

    #endregion
}
