using Asteriq.Models;
using Asteriq.UI;

namespace Asteriq.Services.Abstractions;

/// <summary>
/// Interface for application-level settings
/// </summary>
public interface IApplicationSettingsService
{
    /// <summary>
    /// Get or set the last used profile ID
    /// </summary>
    Guid? LastProfileId { get; set; }

    /// <summary>
    /// Get or set auto-load setting
    /// </summary>
    bool AutoLoadLastProfile { get; set; }

    /// <summary>
    /// Get or set whether to automatically check for updates on startup
    /// </summary>
    bool AutoCheckUpdates { get; set; }

    /// <summary>
    /// Font size setting for accessibility (Small/Medium/Large)
    /// </summary>
    FontSizeOption FontSize { get; set; }

    /// <summary>
    /// Event fired when font size setting changes
    /// </summary>
    event EventHandler<FontSizeOption>? FontSizeChanged;

    /// <summary>
    /// Font family setting (Carbon/Consolas)
    /// </summary>
    UIFontFamily FontFamily { get; set; }

    /// <summary>
    /// Get or set whether clicking the close button minimizes to tray instead of exiting
    /// </summary>
    bool CloseToTray { get; set; }

    /// <summary>
    /// Get or set the system tray icon type (joystick or throttle)
    /// </summary>
    TrayIconType TrayIconType { get; set; }

    /// <summary>
    /// Get or set the last used SC export profile name
    /// </summary>
    string? LastSCExportProfile { get; set; }

    /// <summary>
    /// Get or set auto-load setting for SC export profiles
    /// </summary>
    bool AutoLoadLastSCExportProfile { get; set; }

    /// <summary>
    /// Get the last used SC export profile name for a specific SC environment (LIVE, TECH-PREVIEW, etc.)
    /// Returns null if no profile has been remembered for that environment.
    /// </summary>
    string? GetLastSCExportProfileForEnvironment(string environment);

    /// <summary>
    /// Set the last used SC export profile name for a specific SC environment.
    /// Pass null to clear the remembered profile for that environment.
    /// </summary>
    void SetLastSCExportProfileForEnvironment(string environment, string? profileName);

    /// <summary>
    /// Get or set the preferred SC environment (LIVE, PTU, EPTU) to select on startup
    /// </summary>
    string? PreferredSCEnvironment { get; set; }

    /// <summary>
    /// Get the user-specified silhouette override for a vJoy slot.
    /// Returns null if no override is set (auto-detect from physical device).
    /// The value is the device map filename key (e.g., "joystick", "throttle", "virpil_alpha_prime_r").
    /// </summary>
    string? GetVJoySilhouetteOverride(uint vjoyId);

    /// <summary>
    /// Set the silhouette override for a vJoy slot.
    /// Pass null to clear the override and return to auto-detection.
    /// </summary>
    void SetVJoySilhouetteOverride(uint vjoyId, string? mapKey);
}
