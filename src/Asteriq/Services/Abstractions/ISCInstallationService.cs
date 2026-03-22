using Asteriq.Models;

namespace Asteriq.Services.Abstractions;

/// <summary>
/// Interface for detecting and managing Star Citizen installations
/// </summary>
public interface ISCInstallationService
{
    /// <summary>
    /// Additional search paths configured by the user (e.g. custom game library locations)
    /// </summary>
    List<string> CustomSearchPaths { get; set; }

    /// <summary>
    /// Gets all detected SC installations (cached after first call)
    /// </summary>
    IReadOnlyList<SCInstallation> Installations { get; }

    /// <summary>
    /// Force re-detection of installations
    /// </summary>
    void Refresh();

    /// <summary>
    /// Gets the preferred installation (LIVE first, then PTU, etc.)
    /// </summary>
    SCInstallation? GetPreferredInstallation();

    /// <summary>
    /// Gets installation by environment name
    /// </summary>
    SCInstallation? GetInstallation(string environment);

    /// <summary>
    /// Gets the auto-detected library paths where SC installations were found.
    /// </summary>
    List<string> GetAutoDetectedLibraryPaths();
}
