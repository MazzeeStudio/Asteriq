using SkiaSharp;

namespace Asteriq.UI;

/// <summary>
/// Available color themes
/// </summary>
public enum FUITheme
{
    Midnight,   // Default blue-white theme
    Matrix,     // Green/terminal theme
    Amber,      // Amber/gold retro theme
    Ice         // Cyan/frost theme
}

/// <summary>
/// FUI color palette with theme support
/// Colors are designed to feel like light emission, not paint
/// </summary>
public static class FUIColors
{
    private static FUITheme _currentTheme = FUITheme.Midnight;

    /// <summary>
    /// Current active theme
    /// </summary>
    public static FUITheme CurrentTheme
    {
        get => _currentTheme;
        set => _currentTheme = value;
    }

    /// <summary>
    /// Event fired when theme changes
    /// </summary>
    public static event EventHandler? ThemeChanged;

    /// <summary>
    /// Set the theme and notify listeners
    /// </summary>
    public static void SetTheme(FUITheme theme)
    {
        if (_currentTheme != theme)
        {
            _currentTheme = theme;
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    // Background layers (theme-independent)
    public static SKColor Void => new(0x02, 0x02, 0x04);
    public static SKColor Background0 => new(0x06, 0x08, 0x0A);
    public static SKColor Background1 => new(0x0A, 0x0D, 0x10);
    public static SKColor Background2 => new(0x10, 0x14, 0x18);

    // Grid/structure
    public static SKColor Grid => new(0x18, 0x1C, 0x22);
    public static SKColor GridAccent => new(0x25, 0x2A, 0x32);

    // Theme-dependent colors
    public static SKColor Primary => _currentTheme switch
    {
        FUITheme.Midnight => new(0xE0, 0xE6, 0xED),
        FUITheme.Matrix => new(0x40, 0xFF, 0x40),
        FUITheme.Amber => new(0xFF, 0xC0, 0x40),
        FUITheme.Ice => new(0x80, 0xE0, 0xFF),
        _ => new(0xE0, 0xE6, 0xED)
    };

    public static SKColor PrimaryDim => _currentTheme switch
    {
        FUITheme.Midnight => new(0xA4, 0xB4, 0xC5),
        FUITheme.Matrix => new(0x30, 0xB0, 0x30),
        FUITheme.Amber => new(0xC0, 0x90, 0x30),
        FUITheme.Ice => new(0x60, 0xA0, 0xC0),
        _ => new(0xA4, 0xB4, 0xC5)
    };

    public static SKColor PrimaryFaint => _currentTheme switch
    {
        FUITheme.Midnight => new(0x60, 0x70, 0x80),
        FUITheme.Matrix => new(0x20, 0x60, 0x20),
        FUITheme.Amber => new(0x80, 0x60, 0x20),
        FUITheme.Ice => new(0x40, 0x70, 0x80),
        _ => new(0x60, 0x70, 0x80)
    };

    public static SKColor Glow => _currentTheme switch
    {
        FUITheme.Midnight => new(0x80, 0xC0, 0xE0, 0x60),
        FUITheme.Matrix => new(0x40, 0xFF, 0x40, 0x60),
        FUITheme.Amber => new(0xFF, 0xC0, 0x40, 0x60),
        FUITheme.Ice => new(0x80, 0xE0, 0xFF, 0x60),
        _ => new(0x80, 0xC0, 0xE0, 0x60)
    };

    public static SKColor GlowStrong => _currentTheme switch
    {
        FUITheme.Midnight => new(0xA0, 0xE0, 0xFF, 0x90),
        FUITheme.Matrix => new(0x60, 0xFF, 0x60, 0x90),
        FUITheme.Amber => new(0xFF, 0xD0, 0x60, 0x90),
        FUITheme.Ice => new(0xA0, 0xF0, 0xFF, 0x90),
        _ => new(0xA0, 0xE0, 0xFF, 0x90)
    };

    public static SKColor Active => _currentTheme switch
    {
        FUITheme.Midnight => new(0x40, 0xA0, 0xFF),
        FUITheme.Matrix => new(0x40, 0xFF, 0x40),
        FUITheme.Amber => new(0xFF, 0xA0, 0x40),
        FUITheme.Ice => new(0x40, 0xE0, 0xFF),
        _ => new(0x40, 0xA0, 0xFF)
    };

    public static SKColor ActiveGlow => _currentTheme switch
    {
        FUITheme.Midnight => new(0x40, 0xA0, 0xFF, 0x60),
        FUITheme.Matrix => new(0x40, 0xFF, 0x40, 0x60),
        FUITheme.Amber => new(0xFF, 0xA0, 0x40, 0x60),
        FUITheme.Ice => new(0x40, 0xE0, 0xFF, 0x60),
        _ => new(0x40, 0xA0, 0xFF, 0x60)
    };

    // State colors (theme-aware for visual harmony)
    public static SKColor Warning => _currentTheme switch
    {
        FUITheme.Midnight => new(0xFF, 0xA0, 0x40),  // Orange
        FUITheme.Matrix => new(0xFF, 0xE0, 0x40),    // Yellow (contrasts with green)
        FUITheme.Amber => new(0xFF, 0xE0, 0x60),     // Bright yellow
        FUITheme.Ice => new(0xFF, 0xA0, 0x60),       // Warm orange (contrasts with cyan)
        _ => new(0xFF, 0xA0, 0x40)
    };

    public static SKColor Danger => _currentTheme switch
    {
        FUITheme.Midnight => new(0xFF, 0x50, 0x50),  // Red
        FUITheme.Matrix => new(0xFF, 0x60, 0x40),    // Red-orange (avoids green clash)
        FUITheme.Amber => new(0xFF, 0x50, 0x50),     // Red
        FUITheme.Ice => new(0xFF, 0x50, 0x80),       // Magenta-red
        _ => new(0xFF, 0x50, 0x50)
    };

    public static SKColor Success => _currentTheme switch
    {
        FUITheme.Midnight => new(0x40, 0xFF, 0x90),  // Green
        FUITheme.Matrix => new(0x80, 0xFF, 0x80),    // Bright green (matches theme)
        FUITheme.Amber => new(0x40, 0xE0, 0xC0),     // Teal (contrasts with amber)
        FUITheme.Ice => new(0x40, 0xFF, 0xC0),       // Cyan-green
        _ => new(0x40, 0xFF, 0x90)
    };

    // Text hierarchy (theme-aware for visual harmony)
    public static SKColor TextBright => _currentTheme switch
    {
        FUITheme.Midnight => new(0xF0, 0xF4, 0xF8),  // Cool white
        FUITheme.Matrix => new(0xE0, 0xFF, 0xE0),    // Green-tinted white
        FUITheme.Amber => new(0xFF, 0xF8, 0xF0),     // Warm white
        FUITheme.Ice => new(0xF0, 0xF8, 0xFF),       // Ice white
        _ => new(0xF0, 0xF4, 0xF8)
    };

    public static SKColor TextPrimary => _currentTheme switch
    {
        FUITheme.Midnight => new(0xC0, 0xC8, 0xD0),  // Cool gray
        FUITheme.Matrix => new(0xA0, 0xD0, 0xA0),    // Light green
        FUITheme.Amber => new(0xE0, 0xD0, 0xB0),     // Cream
        FUITheme.Ice => new(0xB0, 0xD0, 0xE0),       // Pale cyan
        _ => new(0xC0, 0xC8, 0xD0)
    };

    public static SKColor TextDim => _currentTheme switch
    {
        FUITheme.Midnight => new(0x70, 0x78, 0x80),  // Slate
        FUITheme.Matrix => new(0x50, 0x80, 0x50),    // Dark green
        FUITheme.Amber => new(0x90, 0x78, 0x50),     // Brown
        FUITheme.Ice => new(0x60, 0x80, 0x90),       // Steel
        _ => new(0x70, 0x78, 0x80)
    };

    public static SKColor TextDisabled => _currentTheme switch
    {
        FUITheme.Midnight => new(0x40, 0x44, 0x48),
        FUITheme.Matrix => new(0x30, 0x48, 0x30),
        FUITheme.Amber => new(0x48, 0x40, 0x30),
        FUITheme.Ice => new(0x38, 0x44, 0x48),
        _ => new(0x40, 0x44, 0x48)
    };

    // Frame/border colors (theme-aware)
    public static SKColor Frame => _currentTheme switch
    {
        FUITheme.Midnight => new(0x50, 0x58, 0x64),  // Blue-gray
        FUITheme.Matrix => new(0x40, 0x60, 0x40),    // Green-gray
        FUITheme.Amber => new(0x64, 0x54, 0x40),     // Brown-gray
        FUITheme.Ice => new(0x48, 0x58, 0x64),       // Cyan-gray
        _ => new(0x50, 0x58, 0x64)
    };

    public static SKColor FrameBright => _currentTheme switch
    {
        FUITheme.Midnight => new(0x90, 0x9C, 0xA8),
        FUITheme.Matrix => new(0x70, 0xA0, 0x70),
        FUITheme.Amber => new(0xA8, 0x94, 0x70),
        FUITheme.Ice => new(0x80, 0x9C, 0xA8),
        _ => new(0x90, 0x9C, 0xA8)
    };

    public static SKColor FrameDim => _currentTheme switch
    {
        FUITheme.Midnight => new(0x30, 0x34, 0x3C),
        FUITheme.Matrix => new(0x28, 0x38, 0x28),
        FUITheme.Amber => new(0x3C, 0x34, 0x28),
        FUITheme.Ice => new(0x2C, 0x34, 0x3C),
        _ => new(0x30, 0x34, 0x3C)
    };
}
