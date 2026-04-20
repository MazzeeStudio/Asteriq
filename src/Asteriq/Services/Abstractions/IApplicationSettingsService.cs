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
    /// Which update channel to follow: Stable (default) checks only full releases;
    /// Nightly also checks pre-releases tagged "nightly".
    /// </summary>
    UpdateChannel UpdateChannel { get; set; }

    /// <summary>
    /// Interface scale factor (0.8 – 1.5, default 1.0)
    /// </summary>
    float FontSize { get; set; }

    /// <summary>
    /// Event fired when interface scale setting changes
    /// </summary>
    event EventHandler<float>? FontSizeChanged;

    /// <summary>
    /// Font family setting (Carbon/Consolas)
    /// </summary>
    UIFontFamily FontFamily { get; set; }

    /// <summary>
    /// Get or set whether clicking the close button minimizes to tray instead of exiting
    /// </summary>
    bool CloseToTray { get; set; }

    /// <summary>
    /// Launch Asteriq automatically when the user logs into Windows (HKCU Run key).
    /// </summary>
    bool LaunchOnWindowsStart { get; set; }

    /// <summary>
    /// Start input forwarding automatically once the active profile has loaded on startup.
    /// </summary>
    bool AutoStartForwarding { get; set; }

    /// <summary>
    /// Start Asteriq minimized. Combined with <see cref="CloseToTray"/>, starts hidden in the tray;
    /// otherwise starts minimized to the taskbar.
    /// </summary>
    bool OpenMinimized { get; set; }

    /// <summary>
    /// Get or set the last used SC export profile name
    /// </summary>
    string? LastSCExportProfile { get; set; }

    /// <summary>
    /// Get or set auto-load setting for SC export profiles
    /// </summary>
    bool AutoLoadLastSCExportProfile { get; set; }

    /// <summary>
    /// Get or set whether SC Bindings column headers show physical device names (default true).
    /// When false, shows JS slot references instead ("Show JS ref" checkbox).
    /// </summary>
    bool SCBindingsShowPhysicalHeaders { get; set; }

    /// <summary>
    /// Get or set whether SC Bindings table shows only bound actions (default false).
    /// </summary>
    bool SCBindingsShowBoundOnly { get; set; }

    /// <summary>
    /// Whether the "Include hidden" checkbox in the Devices tab is enabled.
    /// </summary>
    bool DevicesIncludeHidden { get; set; }

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
    /// Whether to skip the driver setup modal on startup
    /// </summary>
    bool SkipDriverSetup { get; set; }

    /// <summary>
    /// Hide Devices and Mappings tabs; skip vJoy driver check.
    /// For users who only receive input over the network and have no local HOTAS.
    /// </summary>
    bool ClientOnlyMode { get; set; }

    /// <summary>
    /// Which panel is expanded in the Settings right panel accordion ("visual" or "network").
    /// Default "network" so network config is visible when networking is enabled.
    /// </summary>
    string SettingsRightPanel { get; set; }

    /// <summary>
    /// Timestamp of the last successful update check
    /// </summary>
    DateTime? LastUpdateCheck { get; set; }

    /// <summary>Enable network input forwarding (default false).</summary>
    bool NetworkEnabled { get; set; }

    /// <summary>
    /// Override for the broadcast machine name.
    /// Empty string means use <see cref="Environment.MachineName"/>.
    /// </summary>
    string NetworkMachineName { get; set; }

    /// <summary>TCP/UDP port for network discovery and input forwarding (default 47191).</summary>
    int NetworkListenPort { get; set; }

    /// <summary>Whether this instance acts as master, client, or has no network role.</summary>
    NetworkRole NetworkRole { get; set; }

    /// <summary>
    /// Permanent 6-digit code for this master instance.
    /// Auto-generated on first read if empty.
    /// </summary>
    string NetworkMasterCode { get; set; }

    /// <summary>
    /// Client-side trust record for the known master.
    /// Null means no master has been trusted yet.
    /// </summary>
    TrustedPeerConfig? TrustedMaster { get; set; }

    /// <summary>
    /// User-configured additional search paths for SC installations.
    /// </summary>
    List<string> CustomSCSearchPaths { get; set; }

    /// <summary>
    /// Add a custom SC search path if not already present.
    /// </summary>
    void AddCustomSCSearchPath(string path);

    /// <summary>
    /// Remove a custom SC search path.
    /// </summary>
    void RemoveCustomSCSearchPath(string path);

    /// <summary>
    /// Returns true if the device with the given SDL instance GUID has been hidden from the Devices list.
    /// This is a UI-only preference and does not affect HidHide driver state.
    /// </summary>
    bool IsDeviceHidden(string instanceGuid);

    /// <summary>
    /// Show or hide a device from the Devices list by its SDL instance GUID.
    /// Persisted across sessions.
    /// </summary>
    void SetDeviceHidden(string instanceGuid, bool hidden);

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
