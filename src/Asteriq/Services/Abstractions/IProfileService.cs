using Asteriq.Models;
using Asteriq.UI;

namespace Asteriq.Services.Abstractions;

/// <summary>
/// Interface for managing mapping profiles (save, load, list, delete)
/// </summary>
public interface IProfileService
{
    /// <summary>
    /// Event fired when the active profile changes
    /// </summary>
    event EventHandler<ProfileChangedEventArgs>? ProfileChanged;

    /// <summary>
    /// Event fired when font size setting changes
    /// </summary>
    event EventHandler<FontSizeOption>? FontSizeChanged;

    /// <summary>
    /// Event fired when theme setting changes
    /// </summary>
    event EventHandler<FUITheme>? ThemeChanged;

    /// <summary>
    /// The currently active profile (null if no profile is loaded)
    /// </summary>
    MappingProfile? ActiveProfile { get; }

    /// <summary>
    /// Whether a profile is currently active
    /// </summary>
    bool HasActiveProfile { get; }

    /// <summary>
    /// Get the profiles directory path
    /// </summary>
    string ProfilesDirectory { get; }

    /// <summary>
    /// Get or set the last used profile ID
    /// </summary>
    Guid? LastProfileId { get; set; }

    /// <summary>
    /// Get or set auto-load setting
    /// </summary>
    bool AutoLoadLastProfile { get; set; }

    /// <summary>
    /// Font size setting for accessibility (Small/Medium/Large)
    /// </summary>
    FontSizeOption FontSize { get; set; }

    /// <summary>
    /// Font family setting (Carbon/Consolas)
    /// </summary>
    UIFontFamily FontFamily { get; set; }

    /// <summary>
    /// Theme setting
    /// </summary>
    FUITheme Theme { get; set; }

    /// <summary>
    /// Get or set the last used SC export profile name
    /// </summary>
    string? LastSCExportProfile { get; set; }

    /// <summary>
    /// Get or set auto-load setting for SC export profiles
    /// </summary>
    bool AutoLoadLastSCExportProfile { get; set; }

    /// <summary>
    /// Get or set whether clicking the close button minimizes to tray instead of exiting
    /// </summary>
    bool CloseToTray { get; set; }

    /// <summary>
    /// Get or set the system tray icon type (joystick or throttle)
    /// </summary>
    TrayIconType TrayIconType { get; set; }

    /// <summary>
    /// Save a profile to disk
    /// </summary>
    void SaveProfile(MappingProfile profile);

    /// <summary>
    /// Load a profile by ID
    /// </summary>
    MappingProfile? LoadProfile(Guid profileId);

    /// <summary>
    /// Load a profile from a file path
    /// </summary>
    MappingProfile? LoadProfileFromPath(string filePath);

    /// <summary>
    /// Delete a profile by ID
    /// </summary>
    bool DeleteProfile(Guid profileId);

    /// <summary>
    /// List all available profiles (metadata only)
    /// </summary>
    List<ProfileInfo> ListProfiles();

    /// <summary>
    /// Duplicate an existing profile
    /// </summary>
    MappingProfile? DuplicateProfile(Guid sourceId, string newName);

    /// <summary>
    /// Export a profile to a specified path
    /// </summary>
    bool ExportProfile(Guid profileId, string exportPath);

    /// <summary>
    /// Import a profile from a specified path
    /// </summary>
    MappingProfile? ImportProfile(string importPath, bool generateNewId = true);

    /// <summary>
    /// Load background settings from app settings
    /// </summary>
    (int gridStrength, int glowIntensity, int noiseIntensity, int scanlineIntensity, int vignetteStrength) LoadBackgroundSettings();

    /// <summary>
    /// Save background settings to app settings
    /// </summary>
    void SaveBackgroundSettings(int gridStrength, int glowIntensity, int noiseIntensity, int scanlineIntensity, int vignetteStrength);

    /// <summary>
    /// Load the last used profile if auto-load is enabled
    /// </summary>
    MappingProfile? LoadLastProfileIfEnabled();

    /// <summary>
    /// Load window state (size and position)
    /// </summary>
    (int width, int height, int x, int y) LoadWindowState();

    /// <summary>
    /// Save window state (size and position)
    /// </summary>
    void SaveWindowState(int width, int height, int x, int y);

    /// <summary>
    /// Activate a profile by ID, making it the current active profile
    /// </summary>
    bool ActivateProfile(Guid profileId);

    /// <summary>
    /// Deactivate the current profile (mappings will stop)
    /// </summary>
    void DeactivateProfile();

    /// <summary>
    /// Create a new empty profile with the given name
    /// </summary>
    MappingProfile CreateProfile(string name, string description = "");

    /// <summary>
    /// Create and activate a new profile
    /// </summary>
    MappingProfile CreateAndActivateProfile(string name, string description = "");

    /// <summary>
    /// Save the currently active profile
    /// </summary>
    void SaveActiveProfile();

    /// <summary>
    /// Initialize the profile service and load last profile if enabled
    /// </summary>
    void Initialize();
}
