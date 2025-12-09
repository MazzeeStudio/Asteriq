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
        SKColor color, float size = 14f, bool withGlow = false, SKTypeface? typeface = null)
    {
        if (withGlow)
        {
            using var glowPaint = CreateTextPaint(color.WithAlpha(80), size, false, true, typeface);
            canvas.DrawText(text, position.X, position.Y, glowPaint);
        }

        using var paint = CreateTextPaint(color, size, false, false, typeface);
        canvas.DrawText(text, position.X, position.Y, paint);
    }

    public static void DrawTextCentered(SKCanvas canvas, string text, SKRect bounds,
        SKColor color, float size = 14f, bool withGlow = false)
    {
        using var paint = CreateTextPaint(color, size);
        float textWidth = paint.MeasureText(text);
        float x = bounds.Left + (bounds.Width - textWidth) / 2;
        float y = bounds.MidY + size / 3;

        DrawText(canvas, text, new SKPoint(x, y), color, size, withGlow);
    }

    #endregion

    #region Window Controls

    public static void DrawWindowControls(SKCanvas canvas, float x, float y,
        bool minimizeHovered = false, bool maximizeHovered = false, bool closeHovered = false)
    {
        float btnSize = 24f;
        float btnGap = SpaceSM;

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
        bool isClose = type == WindowControlType.Close;

        if (isClose)
        {
            using var bgPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = isHovered ? FUIColors.Danger : FUIColors.Danger.WithAlpha(180),
                IsAntialias = true
            };
            canvas.DrawRect(bounds, bgPaint);
        }

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = isClose ? FUIColors.Danger.WithAlpha(200) : FUIColors.FrameDim,
            StrokeWidth = 1f,
            IsAntialias = true
        };
        canvas.DrawRect(bounds, framePaint);

        float pad = 7f;
        var iconColor = isClose ? FUIColors.TextBright : FUIColors.TextDim;

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
                canvas.DrawLine(bounds.Left + pad, bounds.MidY, bounds.Right - pad, bounds.MidY, iconPaint);
                break;
            case WindowControlType.Maximize:
                var iconRect = new SKRect(bounds.Left + pad, bounds.Top + pad, bounds.Right - pad, bounds.Bottom - pad);
                canvas.DrawRect(iconRect, iconPaint);
                break;
            case WindowControlType.Close:
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
}
