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

    // State colors (mostly theme-independent for clarity)
    public static SKColor Warning => new(0xFF, 0xA0, 0x40);
    public static SKColor Danger => new(0xFF, 0x50, 0x50);
    public static SKColor Success => new(0x40, 0xFF, 0x90);

    // Text hierarchy
    public static SKColor TextBright => new(0xF0, 0xF4, 0xF8);
    public static SKColor TextPrimary => new(0xC0, 0xC8, 0xD0);
    public static SKColor TextDim => new(0x70, 0x78, 0x80);
    public static SKColor TextDisabled => new(0x40, 0x44, 0x48);

    // Frame/border colors
    public static SKColor Frame => new(0x50, 0x58, 0x64);
    public static SKColor FrameBright => new(0x90, 0x9C, 0xA8);
    public static SKColor FrameDim => new(0x30, 0x34, 0x3C);
}
