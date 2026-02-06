using Asteriq.Models;

namespace Asteriq.Services.Abstractions;

/// <summary>
/// Interface for profile storage and retrieval (file I/O)
/// </summary>
public interface IProfileRepository
{
    /// <summary>
    /// Get the profiles directory path
    /// </summary>
    string ProfilesDirectory { get; }

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
    /// Create a new empty profile with the given name
    /// </summary>
    MappingProfile CreateProfile(string name, string description = "");
}
