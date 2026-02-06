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
}
