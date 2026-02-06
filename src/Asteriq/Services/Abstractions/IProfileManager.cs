using Asteriq.Models;

namespace Asteriq.Services.Abstractions;

/// <summary>
/// Interface for managing the currently active profile
/// </summary>
public interface IProfileManager
{
    /// <summary>
    /// Event fired when the active profile changes
    /// </summary>
    event EventHandler<ProfileChangedEventArgs>? ProfileChanged;

    /// <summary>
    /// The currently active profile (null if no profile is loaded)
    /// </summary>
    MappingProfile? ActiveProfile { get; }

    /// <summary>
    /// Whether a profile is currently active
    /// </summary>
    bool HasActiveProfile { get; }

    /// <summary>
    /// Activate a profile by ID, making it the current active profile
    /// </summary>
    bool ActivateProfile(Guid profileId);

    /// <summary>
    /// Activate an already-loaded profile
    /// </summary>
    void ActivateProfile(MappingProfile profile);

    /// <summary>
    /// Deactivate the current profile (mappings will stop)
    /// </summary>
    void DeactivateProfile();

    /// <summary>
    /// Save the currently active profile
    /// </summary>
    void SaveActiveProfile();

    /// <summary>
    /// Create and activate a new profile
    /// </summary>
    MappingProfile CreateAndActivateProfile(string name, string description = "");

    /// <summary>
    /// Initialize the profile manager and load last profile if enabled
    /// </summary>
    void Initialize();
}
