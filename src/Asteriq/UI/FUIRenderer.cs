using Asteriq.Models;
using Asteriq.Services;
using Microsoft.Win32;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI;

/// <summary>
/// Core FUI rendering primitives.
/// All drawing is done with these primitives to maintain visual consistency.
/// </summary>
public static partial class FUIRenderer
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
    /// User interface scale factor (0.9 â€“ 1.2, default 1.0)
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
    /// Combined canvas scale factor (DPI Ã— Windows text setting Ã— user preference).
    /// Applied via canvas.Scale() in OnPaintSurface so ALL drawn elements scale uniformly.
    /// </summary>
    public static float CanvasScaleFactor => _displayScaleFactor * _windowsTextScaleFactor * UserScaleMultiplier;

    /// <summary>
    /// Combined font scale factor (DPI Ã— Windows text setting Ã— user preference)
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

    // Control-specific corner radius â€” smaller than panel CornerRadiusSmall (4f)
    // Used on small interactive controls: badges, checkboxes, inline buttons
    public const float ControlCornerRadius = 3f;

    // Badge and component heights
    public const float BadgeHeightSmall = 16f;     // Tiny inline badges (type indicators)
    public const float BadgeHeightStandard = 20f;  // Standard badges (binding display)
    public const float DropdownItemHeight = 28f;   // Dropdown/list item row height

    // Layout constants â€” shared sizing for panels, rows, and buttons
    public const float RowHeight = 28f;            // Standard list/grid row height
    public const float RowGap = 2f;                // Gap between adjacent rows
    public const float CategoryHeaderHeight = 28f; // Category/section header row
    public const float CollapsedPanelHeight = 52f; // Collapsed panel header-only height
    public const float ButtonHeight = 32f;         // Standard action button
    public const float ButtonHeightSmall = 24f;    // Compact inline button
    public const float SelectorHeight = 32f;       // Dropdown selector control
    public const float PanelHeaderHeight = 52f;    // Collapsible panel header click area
    public const float SideTabWidth = 28f;         // Vertical side-tab strip width

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
        public float LeftPanelWidth { get; set; }
        public float CenterWidth { get; set; }
        public float RightPanelWidth { get; set; }
        public float Gutter { get; set; }
        public bool ShowLeftPanel { get; set; }
        public bool ShowRightPanel { get; set; }
        public bool IsCompact { get; set; }
    }

    /// <summary>
    /// Calculate responsive three-column layout based on window width
    /// </summary>
    public static ResponsiveLayout CalculateLayout(float contentWidth, float minLeftPanel = 320f, float minRightPanel = 280f, float maxSidePanel = 0f)
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

            if (maxSidePanel > 0f)
            {
                leftWidth = Math.Min(leftWidth, maxSidePanel);
                rightWidth = Math.Min(rightWidth, maxSidePanel);
            }

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

            if (maxSidePanel > 0f)
                leftWidth = Math.Min(leftWidth, maxSidePanel);

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


}