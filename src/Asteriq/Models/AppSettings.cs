using Asteriq.UI;

namespace Asteriq.Models;

/// <summary>
/// Font size options for accessibility
/// </summary>
public enum FontSizeOption
{
    Small,   // Original sizes
    Medium,  // +2pt (default)
    Large    // +4pt
}

/// <summary>
/// Application settings
/// </summary>
public class AppSettings
{
    public Guid? LastProfileId { get; set; }
    public bool AutoLoadLastProfile { get; set; } = true;
    public FontSizeOption FontSize { get; set; } = FontSizeOption.Medium;
    public FUITheme Theme { get; set; } = FUITheme.Midnight;

    // Background effect settings (0-100 intensity scale)
    public int GridStrength { get; set; } = 35;
    public int GlowIntensity { get; set; } = 45;
    public int NoiseIntensity { get; set; } = 45;
    public int ScanlineIntensity { get; set; } = 60;
    public int VignetteStrength { get; set; } = 15;

    // SC Export profile settings
    public string? LastSCExportProfile { get; set; }
    public bool AutoLoadLastSCExportProfile { get; set; } = true;

    // Window state
    public int WindowWidth { get; set; } = 0;  // 0 = use default
    public int WindowHeight { get; set; } = 0; // 0 = use default
    public int WindowX { get; set; } = int.MinValue; // MinValue = center on screen
    public int WindowY { get; set; } = int.MinValue; // MinValue = center on screen

    // System tray settings
    public bool CloseToTray { get; set; } = false; // Default: clicking X exits the app
}
