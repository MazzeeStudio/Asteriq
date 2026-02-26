using Asteriq.Models;
using Asteriq.Services;
using Microsoft.Win32;
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

    // Font scaling - combines Windows system setting with user preference
    private static float _interfaceScale = 1.0f;
    private static UIFontFamily _fontFamily = UIFontFamily.Carbon;
    private static float _windowsTextScaleFactor = 1.0f;
    private static float _displayScaleFactor = 1.0f;  // DPI scale (150% = 1.5)
    private static bool _windowsScaleDetected = false;

    /// <summary>
    /// Initialize font scaling by detecting Windows text scale setting.
    /// Call this once at application startup.
    /// </summary>
    public static void InitializeFontScaling()
    {
        if (_windowsScaleDetected) return;

        try
        {
            // Read Windows text scale factor from registry
            // Location: HKEY_CURRENT_USER\SOFTWARE\Microsoft\Accessibility\TextScaleFactor
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Accessibility");
            if (key is not null)
            {
                var value = key.GetValue("TextScaleFactor");
                if (value is int scaleFactor)
                {
                    _windowsTextScaleFactor = scaleFactor / 100f;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Security.SecurityException)
        {
            // If we can't read the registry, use default scale of 1.0
            _windowsTextScaleFactor = 1.0f;
        }

        _windowsScaleDetected = true;
    }

    /// <summary>
    /// Windows text scale factor (1.0 = 100%, 1.5 = 150%, etc.)
    /// </summary>
    public static float WindowsTextScaleFactor => _windowsTextScaleFactor;

    /// <summary>
    /// Display scale factor from DPI (1.0 = 100%/96dpi, 1.5 = 150%/144dpi, etc.)
    /// </summary>
    public static float DisplayScaleFactor => _displayScaleFactor;

    /// <summary>
    /// Set the display scale factor from a form's DPI.
    /// Call this after the form is created: SetDisplayScale(DeviceDpi)
    /// </summary>
    /// <param name="deviceDpi">The form's DeviceDpi property (e.g., 96, 144, 192)</param>
    public static void SetDisplayScale(int deviceDpi)
    {
        _displayScaleFactor = deviceDpi / 96f;
    }

    /// <summary>
    /// User interface scale factor (0.8 – 1.5, default 1.0)
    /// </summary>
    public static float InterfaceScale
    {
        get => _interfaceScale;
        set => _interfaceScale = value;
    }

    /// <summary>
    /// Current font family (Carbon/Consolas)
    /// </summary>
    public static UIFontFamily FontFamily
    {
        get => _fontFamily;
        set => _fontFamily = value;
    }

    /// <summary>
    /// User interface scale multiplier (same as InterfaceScale).
    /// </summary>
    public static float UserScaleMultiplier => _interfaceScale;

    /// <summary>
    /// Combined canvas scale factor (DPI × Windows text setting × user preference).
    /// Applied via canvas.Scale() in OnPaintSurface so ALL drawn elements scale uniformly.
    /// </summary>
    public static float CanvasScaleFactor => _displayScaleFactor * _windowsTextScaleFactor * UserScaleMultiplier;

    /// <summary>
    /// Combined font scale factor (DPI × Windows text setting × user preference)
    /// </summary>
    public static float FontScaleFactor => _displayScaleFactor * _windowsTextScaleFactor * UserScaleMultiplier;

    /// <summary>
    /// Maximum interface scale that keeps the UI usable on a given screen width.
    /// Returns the value rounded down to the nearest 0.1.
    /// </summary>
    public static float MaxInterfaceScale(int screenWidth)
    {
        float max = screenWidth / (_displayScaleFactor * _windowsTextScaleFactor * 1100f);
        return MathF.Floor(max * 10f) / 10f;
    }

    /// <summary>
    /// Identity - DPI/text scaling is now handled by the canvas transform.
    /// Retained for source compatibility; simply returns baseSize unchanged.
    /// </summary>
    public static float ScaleFont(float baseSize) => baseSize;

    /// <summary>
    /// Identity - DPI/text scaling is now handled by the canvas transform.
    /// Retained for source compatibility; simply returns baseSpacing unchanged.
    /// </summary>
    public static float ScaleSpacing(float baseSpacing) => baseSpacing;

    /// <summary>
    /// Identity - DPI/text scaling is now handled by the canvas transform.
    /// Retained for source compatibility; simply returns baseValue unchanged.
    /// </summary>
    public static float ScaleLayout(float baseValue) => baseValue;

    /// <summary>
    /// Identity - DPI/text scaling is now handled by the canvas transform.
    /// Retained for source compatibility; simply returns baseHeight unchanged.
    /// </summary>
    public static float ScaleLineHeight(float baseHeight) => baseHeight;

    // Legacy property for compatibility - returns an additive offset approximation
    [Obsolete("Use ScaleFont() instead for proper multiplicative scaling")]
    public static float FontSizeOffset => (FontScaleFactor - 1f) * 10f;

    // Standard measurements (all 4px aligned)
    public const float CornerRadius = 8f;
    public const float CornerRadiusSmall = 4f;
    public const float CornerRadiusLarge = 12f;
    public const float ChamferSize = 8f;
    public const float ChamferSizeSmall = 4f;   // Was 5f - aligned to 4px grid
    public const float ChamferSizeLarge = 12f;
    public const float BracketSize = 8f;
    public const float BracketGap = 4f;         // Was 3f - aligned to 4px grid
    public const float LineWeight = 1.5f;
    public const float LineWeightThin = 1f;
    public const float LineWeightThick = 2f;
    public const float GlowRadius = 8f;
    public const float GlowRadiusLarge = 16f;

    // Spacing system (4px grid aligned)
    // Based on Windows UX Guidelines: all spacing in multiples of 4 epx
    public const float SpaceXS = 4f;      // Tight spacing, minimum gaps
    public const float SpaceSM = 8f;      // Small spacing, between related items
    public const float SpaceMD = 12f;     // Medium spacing, small gutters
    public const float SpaceLG = 16f;     // Standard spacing, panel padding
    public const float SpaceXL = 24f;     // Large spacing, large gutters
    public const float Space2XL = 32f;    // Extra large, section breaks
    public const float Space3XL = 48f;    // Major sections

    // Legacy spacing aliases for compatibility
    public const float PanelPadding = 16f;
    public const float ItemSpacing = 12f;
    public const float SectionSpacing = 24f;
    public const float FrameInset = 4f;   // Was 5f - aligned to 4px grid

    // Typography - pixel sizes in logical canvas space (+3 shift so 12px/9pt is the floor)
    public const float FontCaption = 15f;     // Labels, secondary text
    public const float FontBody = 17f;        // Primary content text
    public const float FontBodyLarge = 21f;   // Emphasized body, intro text
    public const float FontSubtitle = 23f;    // Section headers
    public const float FontTitle = 31f;       // Page/panel titles
    public const float FontTitleLarge = 43f;  // Hero titles

    // Compact sizes (all at or above 12px minimum)
    public const float FontMicro = 11f;       // Tiny labels, icon annotations
    public const float FontSmall = 12f;       // Hints, secondary metadata (9pt floor)
    public const float FontNote = 13f;        // Notes, compact labels, tooltips
    public const float FontBodyCompact = 14f; // Dense body text (tight list views, badges)

    // Control-specific corner radius — smaller than panel CornerRadiusSmall (4f)
    // Used on small interactive controls: badges, checkboxes, inline buttons
    public const float ControlCornerRadius = 3f;

    // Badge and component heights
    public const float BadgeHeightSmall = 16f;     // Tiny inline badges (type indicators)
    public const float BadgeHeightStandard = 20f;  // Standard badges (binding display)
    public const float DropdownItemHeight = 28f;   // Dropdown/list item row height

    // Line heights for proper text spacing
    public const float LineHeightCaption = 19f;
    public const float LineHeightBody = 23f;
    public const float LineHeightBodyLarge = 27f;
    public const float LineHeightSubtitle = 31f;
    public const float LineHeightTitle = 39f;

    // Touch targets - Windows UX Guidelines
    // Standard: 40x40 epx for touch+pointer, Compact: 32x32 for pointer-focused
    public const float TouchTargetStandard = 40f;
    public const float TouchTargetCompact = 32f;
    public const float TouchTargetMinHeight = 24f;  // Absolute minimum for controls

    // Responsive breakpoints - Windows size classes
    public const float BreakpointSmall = 640f;    // 0-640: phones, small windows
    public const float BreakpointLarge = 1008f;   // 1008+: PCs, large windows

    // Gutters per breakpoint
    public const float GutterSmall = 12f;   // For windows < 640px
    public const float GutterLarge = 24f;   // For windows >= 640px

    // Title bar constants - Windows standard
    public const float TitleBarHeight = 32f;
    public const float TitleBarHeightExpanded = 48f;  // With search/avatar
    public const float TitleBarPadding = 16f;

    /// <summary>
    /// Gets the appropriate gutter size based on window width
    /// </summary>
    public static float GetGutter(float windowWidth)
    {
        return windowWidth < BreakpointSmall ? GutterSmall : GutterLarge;
    }

    /// <summary>
    /// Gets content margin based on window width
    /// </summary>
    public static float GetContentMargin(float windowWidth)
    {
        return windowWidth < BreakpointSmall ? SpaceMD : SpaceXL;
    }

    /// <summary>
    /// Determines if window is in small size class
    /// </summary>
    public static bool IsSmallWindow(float windowWidth) => windowWidth < BreakpointSmall;

    /// <summary>
    /// Determines if window is in large size class
    /// </summary>
    public static bool IsLargeWindow(float windowWidth) => windowWidth >= BreakpointLarge;

    /// <summary>
    /// Layout result for responsive panel calculations
    /// </summary>
    public struct ResponsiveLayout
    {
        public float LeftPanelWidth;
        public float CenterWidth;
        public float RightPanelWidth;
        public float Gutter;
        public bool ShowLeftPanel;
        public bool ShowRightPanel;
        public bool IsCompact;
    }

    /// <summary>
    /// Calculate responsive three-column layout based on window width
    /// </summary>
    public static ResponsiveLayout CalculateLayout(float contentWidth, float minLeftPanel = 320f, float minRightPanel = 280f)
    {
        float gutter = GetGutter(contentWidth);
        bool isSmall = IsSmallWindow(contentWidth);
        bool isLarge = IsLargeWindow(contentWidth);

        if (isSmall)
        {
            // Single column - full width, no side panels
            return new ResponsiveLayout
            {
                LeftPanelWidth = contentWidth,
                CenterWidth = 0,
                RightPanelWidth = 0,
                Gutter = gutter,
                ShowLeftPanel = true,
                ShowRightPanel = false,
                IsCompact = true
            };
        }
        else if (isLarge)
        {
            // Three columns with minimum widths respected
            float availableForPanels = contentWidth - gutter * 2;
            float leftWidth = Math.Max(minLeftPanel, availableForPanels * 0.28f);
            float rightWidth = Math.Max(minRightPanel, availableForPanels * 0.24f);
            float centerWidth = availableForPanels - leftWidth - rightWidth;

            return new ResponsiveLayout
            {
                LeftPanelWidth = leftWidth,
                CenterWidth = centerWidth,
                RightPanelWidth = rightWidth,
                Gutter = gutter,
                ShowLeftPanel = true,
                ShowRightPanel = true,
                IsCompact = false
            };
        }
        else
        {
            // Medium: Two columns - left panel + combined center/right
            float availableWidth = contentWidth - gutter;
            float leftWidth = Math.Max(minLeftPanel, availableWidth * 0.40f);
            float rightWidth = availableWidth - leftWidth;

            return new ResponsiveLayout
            {
                LeftPanelWidth = leftWidth,
                CenterWidth = rightWidth,
                RightPanelWidth = 0,
                Gutter = gutter,
                ShowLeftPanel = true,
                ShowRightPanel = false,
                IsCompact = false
            };
        }
    }

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

        return bestFit > 0 ? text.Substring(0, bestFit) + ellipsis : ellipsis;
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

    #endregion

    #region Semantic Typography Helpers

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

    #endregion

    #region Window Controls

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

    #endregion

    #region Panel Layout Helpers

    /// <summary>
    /// Stores layout metrics for a panel - reduces repeated calculations
    /// </summary>
    public struct PanelMetrics
    {
        public float Y;              // Current Y position for content
        public float LeftMargin;     // Left content edge
        public float RightMargin;    // Right content edge
        public float ContentWidth;   // Available content width
        public float RowHeight;      // Standard row height
        public float SectionSpacing; // Space between sections
    }

    /// <summary>
    /// Draws standard panel chrome (background + L-corner frame) and returns layout metrics
    /// </summary>
    public static PanelMetrics DrawPanelChrome(SKCanvas canvas, SKRect bounds, SKColor? frameColor = null)
    {
        frameColor ??= FUIColors.Frame;

        // Panel background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(160),
            IsAntialias = true
        };
        canvas.DrawRect(bounds.Inset(FrameInset, FrameInset), bgPaint);
        DrawLCornerFrame(canvas, bounds, frameColor.Value, 30f, 8f);

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

    #endregion

    #region Buttons

    public enum ButtonState { Normal, Hover, Active, Disabled }

    public static void DrawButton(SKCanvas canvas, SKRect bounds, string text,
        ButtonState state, SKColor? accentColor = null)
    {
        var accent = accentColor ?? FUIColors.Active;
        bool hasCustomAccent = accentColor.HasValue;

        SKColor bgColor, frameColor, textColor;
        bool withGlow = false;

        switch (state)
        {
            case ButtonState.Hover:
                bgColor = accent.WithAlpha(30);
                frameColor = accent;
                textColor = hasCustomAccent ? accent : FUIColors.TextBright;
                withGlow = true;
                break;
            case ButtonState.Active:
                bgColor = accent.WithAlpha(60);
                frameColor = accent;
                textColor = hasCustomAccent ? accent : FUIColors.TextBright;
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

        DrawTextCentered(canvas, text, bounds, textColor, 14f, withGlow && state == ButtonState.Active);
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

        DrawTextCentered(canvas, label, bounds, textColor, 13f);
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

        DrawTextCentered(canvas, text, bounds, textColor, 12f);
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
        SKColor color, float progress = 1f, float fontSize = 13f)
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
        SKColor color, float progress = 1f, float fontSize = 13f)
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
