using SkiaSharp;

namespace Asteriq.UI;

/// <summary>
/// Available color themes
/// </summary>
public enum FUITheme
{
    // Core themes
    Midnight,   // Default blue-white theme
    Matrix,     // Green/terminal theme
    Amber,      // Amber/gold retro theme
    Ice,        // Cyan/frost theme

    // Star Citizen manufacturer themes
    Drake,      // Orange/industrial - rugged, utilitarian
    Aegis,      // Military blue - cold, professional
    Anvil,      // Military green - rugged military
    Argo,       // Yellow/industrial - work/utility
    Crusader,   // White/blue - clean, corporate
    Origin,     // White/gold - luxury, premium
    MISC,       // Teal/industrial - functional, reliable
    RSI         // Blue/white - classic Star Citizen
}

/// <summary>
/// FUI color palette with theme support
/// Colors are designed to feel like light emission, not paint
/// </summary>
public static class FUIColors
{
    // ---------------------------------------------------------------------------
    // Alpha constants — single source of truth for all WithAlpha() calls.
    // Use these instead of magic numbers so bulk visual changes are one-line edits.
    // ---------------------------------------------------------------------------

    /// <summary>Barely-visible tint (subtle overlap, glow edge) — 15/255</summary>
    public const byte AlphaGhost = 15;

    /// <summary>Very light tint (soft hover overlay, background shimmer) — 30/255</summary>
    public const byte AlphaLightTint = 30;

    /// <summary>Light interactive tint (default hover-bg, selection overlay) — 40/255</summary>
    public const byte AlphaHoverBg = 40;

    /// <summary>Medium glow / secondary highlight — 60/255</summary>
    public const byte AlphaGlow = 60;

    /// <summary>Strong hover tint / active-state emphasis — 80/255</summary>
    public const byte AlphaHoverStrong = 80;

    /// <summary>Subtle panel background (default closed state) — 120/255</summary>
    public const byte AlphaPanelSubtle = 120;

    /// <summary>Soft border / secondary indicator — 150/255</summary>
    public const byte AlphaBorderSoft = 150;

    /// <summary>Strong panel background (open/active dropdown state) — 180/255</summary>
    public const byte AlphaPanelStrong = 180;

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
        // Manufacturers
        FUITheme.Drake => new(0xFF, 0x90, 0x30),      // Industrial orange
        FUITheme.Aegis => new(0x70, 0xA0, 0xD0),      // Steel blue
        FUITheme.Anvil => new(0xA0, 0xB0, 0x70),      // Olive/khaki
        FUITheme.Argo => new(0xFF, 0xD0, 0x20),       // Safety yellow
        FUITheme.Crusader => new(0xE8, 0xF0, 0xF8),   // Clean white
        FUITheme.Origin => new(0xF0, 0xE8, 0xE0),     // Pearl white
        FUITheme.MISC => new(0x50, 0xC0, 0xA0),       // Teal
        FUITheme.RSI => new(0xD0, 0xE0, 0xF0),        // Light blue-white
        _ => new(0xE0, 0xE6, 0xED)
    };

    public static SKColor PrimaryDim => _currentTheme switch
    {
        FUITheme.Midnight => new(0xA4, 0xB4, 0xC5),
        FUITheme.Matrix => new(0x30, 0xB0, 0x30),
        FUITheme.Amber => new(0xC0, 0x90, 0x30),
        FUITheme.Ice => new(0x60, 0xA0, 0xC0),
        // Manufacturers
        FUITheme.Drake => new(0xC0, 0x70, 0x20),      // Dim orange
        FUITheme.Aegis => new(0x50, 0x70, 0x90),      // Dim steel
        FUITheme.Anvil => new(0x70, 0x80, 0x50),      // Dim olive
        FUITheme.Argo => new(0xC0, 0xA0, 0x18),       // Dim yellow
        FUITheme.Crusader => new(0xA0, 0xB0, 0xC0),   // Dim white-blue
        FUITheme.Origin => new(0xB0, 0xA0, 0x90),     // Dim cream
        FUITheme.MISC => new(0x40, 0x90, 0x70),       // Dim teal
        FUITheme.RSI => new(0x90, 0xA0, 0xB0),        // Dim blue-gray
        _ => new(0xA4, 0xB4, 0xC5)
    };

    public static SKColor PrimaryFaint => _currentTheme switch
    {
        FUITheme.Midnight => new(0x60, 0x70, 0x80),
        FUITheme.Matrix => new(0x20, 0x60, 0x20),
        FUITheme.Amber => new(0x80, 0x60, 0x20),
        FUITheme.Ice => new(0x40, 0x70, 0x80),
        // Manufacturers
        FUITheme.Drake => new(0x70, 0x50, 0x20),      // Faint orange-brown
        FUITheme.Aegis => new(0x40, 0x50, 0x60),      // Faint steel
        FUITheme.Anvil => new(0x50, 0x58, 0x40),      // Faint olive
        FUITheme.Argo => new(0x70, 0x60, 0x20),       // Faint yellow
        FUITheme.Crusader => new(0x60, 0x70, 0x80),   // Faint blue-gray
        FUITheme.Origin => new(0x70, 0x60, 0x50),     // Faint cream
        FUITheme.MISC => new(0x30, 0x60, 0x50),       // Faint teal
        FUITheme.RSI => new(0x50, 0x60, 0x70),        // Faint blue
        _ => new(0x60, 0x70, 0x80)
    };

    public static SKColor Glow => _currentTheme switch
    {
        FUITheme.Midnight => new(0x80, 0xC0, 0xE0, 0x60),
        FUITheme.Matrix => new(0x40, 0xFF, 0x40, 0x60),
        FUITheme.Amber => new(0xFF, 0xC0, 0x40, 0x60),
        FUITheme.Ice => new(0x80, 0xE0, 0xFF, 0x60),
        // Manufacturers
        FUITheme.Drake => new(0xFF, 0x80, 0x20, 0x60),
        FUITheme.Aegis => new(0x60, 0x90, 0xC0, 0x60),
        FUITheme.Anvil => new(0x90, 0xA0, 0x60, 0x60),
        FUITheme.Argo => new(0xFF, 0xC0, 0x00, 0x60),
        FUITheme.Crusader => new(0x80, 0xC0, 0xF0, 0x60),
        FUITheme.Origin => new(0xD4, 0xAF, 0x60, 0x60),
        FUITheme.MISC => new(0x40, 0xB0, 0x90, 0x60),
        FUITheme.RSI => new(0x70, 0xA0, 0xD0, 0x60),
        _ => new(0x80, 0xC0, 0xE0, 0x60)
    };

    public static SKColor GlowStrong => _currentTheme switch
    {
        FUITheme.Midnight => new(0xA0, 0xE0, 0xFF, 0x90),
        FUITheme.Matrix => new(0x60, 0xFF, 0x60, 0x90),
        FUITheme.Amber => new(0xFF, 0xD0, 0x60, 0x90),
        FUITheme.Ice => new(0xA0, 0xF0, 0xFF, 0x90),
        // Manufacturers
        FUITheme.Drake => new(0xFF, 0xA0, 0x40, 0x90),
        FUITheme.Aegis => new(0x80, 0xB0, 0xE0, 0x90),
        FUITheme.Anvil => new(0xB0, 0xC0, 0x80, 0x90),
        FUITheme.Argo => new(0xFF, 0xE0, 0x40, 0x90),
        FUITheme.Crusader => new(0xA0, 0xD0, 0xFF, 0x90),
        FUITheme.Origin => new(0xE0, 0xC8, 0x80, 0x90),
        FUITheme.MISC => new(0x60, 0xD0, 0xB0, 0x90),
        FUITheme.RSI => new(0x90, 0xC0, 0xF0, 0x90),
        _ => new(0xA0, 0xE0, 0xFF, 0x90)
    };

    public static SKColor Active => _currentTheme switch
    {
        FUITheme.Midnight => new(0x40, 0xA0, 0xFF),
        FUITheme.Matrix => new(0x40, 0xFF, 0x40),
        FUITheme.Amber => new(0xFF, 0xA0, 0x40),
        FUITheme.Ice => new(0x40, 0xE0, 0xFF),
        // Manufacturers
        FUITheme.Drake => new(0xFF, 0x80, 0x20),      // Bright orange
        FUITheme.Aegis => new(0x40, 0x90, 0xE0),      // Bright blue
        FUITheme.Anvil => new(0x90, 0xC0, 0x40),      // Bright olive
        FUITheme.Argo => new(0xFF, 0xC0, 0x00),       // Bright yellow
        FUITheme.Crusader => new(0x40, 0x90, 0xE0),   // Crusader blue
        FUITheme.Origin => new(0xD4, 0xAF, 0x37),     // Gold accent
        FUITheme.MISC => new(0x40, 0xC0, 0x90),       // Bright teal
        FUITheme.RSI => new(0x50, 0xA0, 0xF0),        // RSI blue
        _ => new(0x40, 0xA0, 0xFF)
    };

    public static SKColor ActiveGlow => _currentTheme switch
    {
        FUITheme.Midnight => new(0x40, 0xA0, 0xFF, 0x60),
        FUITheme.Matrix => new(0x40, 0xFF, 0x40, 0x60),
        FUITheme.Amber => new(0xFF, 0xA0, 0x40, 0x60),
        FUITheme.Ice => new(0x40, 0xE0, 0xFF, 0x60),
        // Manufacturers
        FUITheme.Drake => new(0xFF, 0x80, 0x20, 0x60),
        FUITheme.Aegis => new(0x40, 0x90, 0xE0, 0x60),
        FUITheme.Anvil => new(0x90, 0xC0, 0x40, 0x60),
        FUITheme.Argo => new(0xFF, 0xC0, 0x00, 0x60),
        FUITheme.Crusader => new(0x40, 0x90, 0xE0, 0x60),
        FUITheme.Origin => new(0xD4, 0xAF, 0x37, 0x60),
        FUITheme.MISC => new(0x40, 0xC0, 0x90, 0x60),
        FUITheme.RSI => new(0x50, 0xA0, 0xF0, 0x60),
        _ => new(0x40, 0xA0, 0xFF, 0x60)
    };

    // State colors (theme-aware for visual harmony)
    public static SKColor Warning => _currentTheme switch
    {
        FUITheme.Midnight => new(0xE0, 0xC0, 0x50),  // Gold-yellow (neutral warning)
        FUITheme.Matrix => new(0xFF, 0xFF, 0x60),    // Bright yellow (terminal style)
        FUITheme.Amber => new(0xFF, 0xF0, 0x80),     // Pale yellow (distinct from amber)
        FUITheme.Ice => new(0xFF, 0xE0, 0xA0),       // Pale icy yellow
        // Manufacturers - use theme-appropriate warning colors
        FUITheme.Drake => new(0xFF, 0xFF, 0x60),     // Bright yellow (contrasts orange)
        FUITheme.Aegis => new(0xFF, 0xC0, 0x40),     // Orange (contrasts blue)
        FUITheme.Anvil => new(0xFF, 0xC0, 0x40),     // Orange (contrasts green)
        FUITheme.Argo => new(0xFF, 0x90, 0x40),      // Orange (contrasts yellow)
        FUITheme.Crusader => new(0xFF, 0xA0, 0x40),  // Orange
        FUITheme.Origin => new(0xFF, 0xC0, 0x60),    // Warm gold
        FUITheme.MISC => new(0xFF, 0xC0, 0x40),      // Orange (contrasts teal)
        FUITheme.RSI => new(0xFF, 0xA0, 0x40),       // Orange
        _ => new(0xFF, 0xA0, 0x40)
    };

    public static SKColor Danger => _currentTheme switch
    {
        FUITheme.Midnight => new(0xF0, 0x60, 0x70),  // Coral-red (fits cool palette)
        FUITheme.Matrix => new(0xFF, 0xFF, 0x40),    // Bright yellow (no red/green clash)
        FUITheme.Amber => new(0xFF, 0x60, 0x40),     // Warm red-orange (fits palette)
        FUITheme.Ice => new(0xFF, 0x50, 0x80),       // Magenta-red
        // Manufacturers
        FUITheme.Drake => new(0xFF, 0xFF, 0x50),     // Bright yellow (avoids orange clash)
        FUITheme.Aegis => new(0xFF, 0x50, 0x50),     // Red (contrasts blue)
        FUITheme.Anvil => new(0xFF, 0x70, 0x50),     // Softer red-orange
        FUITheme.Argo => new(0xFF, 0x70, 0x70),      // Softer red (less saturated)
        FUITheme.Crusader => new(0xFF, 0x50, 0x50),  // Red
        FUITheme.Origin => new(0xFF, 0x60, 0x50),    // Warm red
        FUITheme.MISC => new(0xF0, 0x60, 0x70),      // Coral (softer against teal)
        FUITheme.RSI => new(0xFF, 0x50, 0x50),       // Red
        _ => new(0xFF, 0x50, 0x50)
    };

    public static SKColor Success => _currentTheme switch
    {
        FUITheme.Midnight => new(0x50, 0xD0, 0xA0),  // Cool blue-green
        FUITheme.Matrix => new(0x80, 0xFF, 0x80),    // Bright green (matches theme)
        FUITheme.Amber => new(0xFF, 0xD0, 0x60),     // Bright gold (warm, fits theme)
        FUITheme.Ice => new(0x80, 0xF0, 0xFF),       // Bright ice blue
        // Manufacturers - use theme's Active color for consistency
        FUITheme.Drake => new(0xFF, 0xC0, 0x40),     // Bright orange-yellow
        FUITheme.Aegis => new(0x60, 0xC0, 0xF0),     // Bright blue
        FUITheme.Anvil => new(0xB0, 0xD0, 0x60),     // Bright olive
        FUITheme.Argo => new(0xFF, 0xE0, 0x40),      // Bright yellow
        FUITheme.Crusader => new(0x60, 0xC0, 0xF0),  // Bright blue
        FUITheme.Origin => new(0xE0, 0xC0, 0x60),    // Bright gold
        FUITheme.MISC => new(0x60, 0xE0, 0xB0),      // Bright teal
        FUITheme.RSI => new(0x70, 0xC0, 0xF0),       // Bright blue
        _ => new(0x40, 0xFF, 0x90)
    };

    // Text hierarchy (theme-aware for visual harmony)
    public static SKColor TextBright => _currentTheme switch
    {
        FUITheme.Midnight => new(0xF0, 0xF4, 0xF8),  // Cool white
        FUITheme.Matrix => new(0xE0, 0xFF, 0xE0),    // Green-tinted white
        FUITheme.Amber => new(0xFF, 0xF8, 0xF0),     // Warm white
        FUITheme.Ice => new(0xF0, 0xF8, 0xFF),       // Ice white
        // Manufacturers
        FUITheme.Drake => new(0xFF, 0xF4, 0xE8),     // Warm industrial white
        FUITheme.Aegis => new(0xE8, 0xF0, 0xF8),     // Cool military white
        FUITheme.Anvil => new(0xF0, 0xF4, 0xE8),     // Warm khaki white
        FUITheme.Argo => new(0xFF, 0xF8, 0xE0),      // Yellow-tinted white
        FUITheme.Crusader => new(0xF8, 0xFC, 0xFF),  // Pure clean white
        FUITheme.Origin => new(0xFF, 0xFC, 0xF8),    // Pearl white
        FUITheme.MISC => new(0xE8, 0xF8, 0xF4),      // Teal-tinted white
        FUITheme.RSI => new(0xF0, 0xF4, 0xF8),       // Clean white
        _ => new(0xF0, 0xF4, 0xF8)
    };

    public static SKColor TextPrimary => _currentTheme switch
    {
        FUITheme.Midnight => new(0xC0, 0xC8, 0xD0),  // Cool gray
        FUITheme.Matrix => new(0xA0, 0xD0, 0xA0),    // Light green
        FUITheme.Amber => new(0xE0, 0xD0, 0xB0),     // Cream
        FUITheme.Ice => new(0xB0, 0xD0, 0xE0),       // Pale cyan
        // Manufacturers
        FUITheme.Drake => new(0xD0, 0xC0, 0xA0),     // Tan
        FUITheme.Aegis => new(0xB0, 0xC0, 0xD0),     // Steel gray
        FUITheme.Anvil => new(0xC0, 0xC8, 0xB0),     // Khaki gray
        FUITheme.Argo => new(0xD0, 0xC8, 0xA0),      // Yellow-gray
        FUITheme.Crusader => new(0xC0, 0xC8, 0xD0),  // Blue-gray
        FUITheme.Origin => new(0xD0, 0xC8, 0xC0),    // Warm gray
        FUITheme.MISC => new(0xB0, 0xC8, 0xC0),      // Teal-gray
        FUITheme.RSI => new(0xC0, 0xC8, 0xD0),       // Blue-gray
        _ => new(0xC0, 0xC8, 0xD0)
    };

    public static SKColor TextDim => _currentTheme switch
    {
        FUITheme.Midnight => new(0x70, 0x78, 0x80),  // Slate
        FUITheme.Matrix => new(0x50, 0x80, 0x50),    // Dark green
        FUITheme.Amber => new(0x90, 0x78, 0x50),     // Brown
        FUITheme.Ice => new(0x60, 0x80, 0x90),       // Steel
        // Manufacturers
        FUITheme.Drake => new(0x80, 0x70, 0x50),     // Dim tan
        FUITheme.Aegis => new(0x60, 0x70, 0x80),     // Dim steel
        FUITheme.Anvil => new(0x68, 0x70, 0x58),     // Dim olive
        FUITheme.Argo => new(0x80, 0x78, 0x50),      // Dim yellow
        FUITheme.Crusader => new(0x68, 0x78, 0x88),  // Dim blue-gray
        FUITheme.Origin => new(0x78, 0x70, 0x68),    // Dim warm gray
        FUITheme.MISC => new(0x58, 0x78, 0x70),      // Dim teal
        FUITheme.RSI => new(0x68, 0x78, 0x88),       // Dim blue
        _ => new(0x70, 0x78, 0x80)
    };

    public static SKColor TextDisabled => _currentTheme switch
    {
        FUITheme.Midnight => new(0x40, 0x44, 0x48),
        FUITheme.Matrix => new(0x30, 0x48, 0x30),
        FUITheme.Amber => new(0x48, 0x40, 0x30),
        FUITheme.Ice => new(0x38, 0x44, 0x48),
        // Manufacturers
        FUITheme.Drake => new(0x48, 0x40, 0x30),
        FUITheme.Aegis => new(0x38, 0x40, 0x48),
        FUITheme.Anvil => new(0x40, 0x44, 0x38),
        FUITheme.Argo => new(0x48, 0x44, 0x30),
        FUITheme.Crusader => new(0x40, 0x44, 0x48),
        FUITheme.Origin => new(0x44, 0x40, 0x3C),
        FUITheme.MISC => new(0x38, 0x44, 0x40),
        FUITheme.RSI => new(0x40, 0x44, 0x48),
        _ => new(0x40, 0x44, 0x48)
    };

    // Frame/border colors (theme-aware)
    public static SKColor Frame => _currentTheme switch
    {
        FUITheme.Midnight => new(0x50, 0x58, 0x64),  // Blue-gray
        FUITheme.Matrix => new(0x40, 0x60, 0x40),    // Green-gray
        FUITheme.Amber => new(0x64, 0x54, 0x40),     // Brown-gray
        FUITheme.Ice => new(0x48, 0x58, 0x64),       // Cyan-gray
        // Manufacturers
        FUITheme.Drake => new(0x60, 0x50, 0x38),     // Industrial brown
        FUITheme.Aegis => new(0x48, 0x54, 0x60),     // Military steel
        FUITheme.Anvil => new(0x50, 0x58, 0x48),     // Olive gray
        FUITheme.Argo => new(0x60, 0x58, 0x38),      // Industrial yellow-brown
        FUITheme.Crusader => new(0x50, 0x58, 0x64),  // Clean blue-gray
        FUITheme.Origin => new(0x58, 0x50, 0x48),    // Warm gray
        FUITheme.MISC => new(0x40, 0x58, 0x50),      // Teal-gray
        FUITheme.RSI => new(0x48, 0x54, 0x60),       // Blue-gray
        _ => new(0x50, 0x58, 0x64)
    };

    public static SKColor FrameBright => _currentTheme switch
    {
        FUITheme.Midnight => new(0x90, 0x9C, 0xA8),
        FUITheme.Matrix => new(0x70, 0xA0, 0x70),
        FUITheme.Amber => new(0xA8, 0x94, 0x70),
        FUITheme.Ice => new(0x80, 0x9C, 0xA8),
        // Manufacturers
        FUITheme.Drake => new(0xA0, 0x88, 0x60),     // Bright tan
        FUITheme.Aegis => new(0x80, 0x90, 0xA0),     // Bright steel
        FUITheme.Anvil => new(0x88, 0x98, 0x78),     // Bright olive
        FUITheme.Argo => new(0xA0, 0x98, 0x60),      // Bright yellow-tan
        FUITheme.Crusader => new(0x90, 0x9C, 0xA8),  // Bright blue-gray
        FUITheme.Origin => new(0x98, 0x90, 0x80),    // Bright warm gray
        FUITheme.MISC => new(0x70, 0x98, 0x88),      // Bright teal
        FUITheme.RSI => new(0x88, 0x98, 0xA8),       // Bright blue
        _ => new(0x90, 0x9C, 0xA8)
    };

    public static SKColor FrameDim => _currentTheme switch
    {
        FUITheme.Midnight => new(0x30, 0x34, 0x3C),
        FUITheme.Matrix => new(0x28, 0x38, 0x28),
        FUITheme.Amber => new(0x3C, 0x34, 0x28),
        FUITheme.Ice => new(0x2C, 0x34, 0x3C),
        // Manufacturers
        FUITheme.Drake => new(0x38, 0x30, 0x24),     // Dim brown
        FUITheme.Aegis => new(0x28, 0x30, 0x38),     // Dim steel
        FUITheme.Anvil => new(0x30, 0x34, 0x28),     // Dim olive
        FUITheme.Argo => new(0x38, 0x34, 0x24),      // Dim yellow-brown
        FUITheme.Crusader => new(0x30, 0x34, 0x3C),  // Dim blue-gray
        FUITheme.Origin => new(0x34, 0x30, 0x2C),    // Dim warm gray
        FUITheme.MISC => new(0x28, 0x34, 0x30),      // Dim teal
        FUITheme.RSI => new(0x2C, 0x34, 0x3C),       // Dim blue
        _ => new(0x30, 0x34, 0x3C)
    };

    // ---------------------------------------------------------------------------
    // Semantic compound colors — pre-combined color+alpha for the most common
    // interactive states. Use these instead of inline WithAlpha() expressions.
    // ---------------------------------------------------------------------------

    /// <summary>Panel/dropdown background in default (closed) state.</summary>
    public static SKColor PanelBgDefault => Background2.WithAlpha(AlphaPanelSubtle);

    /// <summary>Panel/dropdown background when hovered.</summary>
    public static SKColor PanelBgHover => Background2.WithAlpha(AlphaPanelStrong);

    /// <summary>Panel/dropdown background when open or selected.</summary>
    public static SKColor PanelBgActive => Active.WithAlpha(AlphaHoverBg);

    /// <summary>Light hover tint overlaid on any surface (Primary colour, very transparent).</summary>
    public static SKColor HoverTint => Primary.WithAlpha(AlphaHoverBg);

    /// <summary>Selection / active-item background highlight.</summary>
    public static SKColor SelectionBg => Active.WithAlpha(AlphaHoverBg);

    /// <summary>Stronger selection background (e.g. focused row, open section).</summary>
    public static SKColor SelectionBgStrong => Active.WithAlpha(AlphaHoverStrong);

    /// <summary>Soft border for active/selected elements.</summary>
    public static SKColor SelectionBorder => Active.WithAlpha(AlphaBorderSoft);

    /// <summary>Danger/conflict tint used as a cell background (very subtle).</summary>
    public static SKColor DangerTint => Danger.WithAlpha(AlphaHoverBg);

    /// <summary>Warning/conflict tint used as a cell background (very subtle).</summary>
    public static SKColor WarningTint => Warning.WithAlpha(AlphaHoverBg);
}
