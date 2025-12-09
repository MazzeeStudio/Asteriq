using SkiaSharp;

namespace Asteriq.UI;

/// <summary>
/// FUI color palette - Mono scheme (greyscale with white accents)
/// Colors are designed to feel like light emission, not paint
/// </summary>
public static class FUIColors
{
    // Background layers (the "void" behind the display)
    public static SKColor Void => new(0x02, 0x02, 0x04);           // Near black
    public static SKColor Background0 => new(0x06, 0x08, 0x0A);    // Deepest layer
    public static SKColor Background1 => new(0x0A, 0x0D, 0x10);    // Panel backgrounds
    public static SKColor Background2 => new(0x10, 0x14, 0x18);    // Elevated surfaces

    // Grid/structure (subtle coordinate system feel)
    public static SKColor Grid => new(0x18, 0x1C, 0x22);           // Faint grid lines
    public static SKColor GridAccent => new(0x25, 0x2A, 0x32);     // Emphasized grid

    // Primary accent (the "light" color - what glows)
    public static SKColor Primary => new(0xE0, 0xE6, 0xED);        // Bright white-blue
    public static SKColor PrimaryDim => new(0xA4, 0xB4, 0xC5);     // Dimmed primary
    public static SKColor PrimaryFaint => new(0x60, 0x70, 0x80);   // Very dim

    // Glow colors (same hue, for bloom effects)
    public static SKColor Glow => new(0x80, 0xC0, 0xE0, 0x60);     // Translucent glow
    public static SKColor GlowStrong => new(0xA0, 0xE0, 0xFF, 0x90); // Intense glow

    // State colors
    public static SKColor Active => new(0x40, 0xA0, 0xFF);         // Selected/active blue
    public static SKColor ActiveGlow => new(0x40, 0xA0, 0xFF, 0x60);
    public static SKColor Warning => new(0xFF, 0xA0, 0x40);        // Amber warning
    public static SKColor Danger => new(0xFF, 0x50, 0x50);         // Red alert
    public static SKColor Success => new(0x40, 0xFF, 0x90);        // Green online

    // Text hierarchy
    public static SKColor TextBright => new(0xF0, 0xF4, 0xF8);     // Headers, important
    public static SKColor TextPrimary => new(0xC0, 0xC8, 0xD0);    // Body text
    public static SKColor TextDim => new(0x70, 0x78, 0x80);        // Secondary info
    public static SKColor TextDisabled => new(0x40, 0x44, 0x48);   // Disabled state

    // Frame/border colors
    public static SKColor Frame => new(0x50, 0x58, 0x64);          // Default frame
    public static SKColor FrameBright => new(0x90, 0x9C, 0xA8);    // Highlighted frame
    public static SKColor FrameDim => new(0x30, 0x34, 0x3C);       // Subtle frame
}
