using Asteriq.UI;

namespace Asteriq.Models;

/// <summary>
/// Legacy font size options — kept for JSON backward compatibility only.
/// </summary>
[Obsolete("Use float InterfaceScale instead")]
public enum FontSizeOption
{
    VSmall,  // 1.0x
    Small,   // 1.2x
    Medium,  // 1.3x
    Large,   // 1.4x
    XLarge   // 1.6x
}

/// <summary>
/// Font family options
/// </summary>
public enum UIFontFamily
{
    Carbon,    // Futuristic sci-fi font (default)
    Consolas   // Clean monospace font for readability
}

/// <summary>
/// Application settings
/// </summary>
public class AppSettings
{
    public Guid? LastProfileId { get; set; }
    public bool AutoLoadLastProfile { get; set; } = true;
    public float FontSize { get; set; } = 1.0f;
    public UIFontFamily FontFamily { get; set; } = UIFontFamily.Carbon;
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
    public string? PreferredSCEnvironment { get; set; }

    // Window state
    public int WindowWidth { get; set; } = 0;  // 0 = use default
    public int WindowHeight { get; set; } = 0; // 0 = use default
    public int WindowX { get; set; } = int.MinValue; // MinValue = center on screen
    public int WindowY { get; set; } = int.MinValue; // MinValue = center on screen

    // System tray settings
    public bool CloseToTray { get; set; } = false; // Default: clicking X exits the app
    public TrayIconType TrayIconType { get; set; } = TrayIconType.Throttle; // Default: throttle icon
}

/// <summary>
/// System tray icon type options
/// </summary>
public enum TrayIconType
{
    Throttle,
    Joystick
}
