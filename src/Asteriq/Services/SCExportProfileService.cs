using System.Text.Json;
using System.Text.Json.Serialization;
using Asteriq.Models;

namespace Asteriq.Services;

/// <summary>
/// Service for managing SC export profiles (save, load, list, delete)
/// </summary>
public class SCExportProfileService
{
    private readonly string _profilesDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public SCExportProfileService()
    {
        // Store SC export profiles in %APPDATA%\Asteriq\SCProfiles
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var asteriqDir = Path.Combine(appData, "Asteriq");
        _profilesDirectory = Path.Combine(asteriqDir, "SCProfiles");

        Directory.CreateDirectory(_profilesDirectory);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    /// <summary>
    /// Constructor for testing with custom path
    /// </summary>
    public SCExportProfileService(string profilesDirectory)
    {
        _profilesDirectory = profilesDirectory;
        Directory.CreateDirectory(_profilesDirectory);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    /// <summary>
    /// Save an SC export profile to disk
    /// </summary>
    public void SaveProfile(SCExportProfile profile)
    {
        profile.Modified = DateTime.UtcNow;
        var filePath = GetProfilePath(profile.ProfileName);
        var json = JsonSerializer.Serialize(profile, _jsonOptions);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Load an SC export profile by name
    /// </summary>
    public SCExportProfile? LoadProfile(string profileName)
    {
        var filePath = GetProfilePath(profileName);
        return LoadProfileFromPath(filePath);
    }

    /// <summary>
    /// Load an SC export profile from a file path
    /// </summary>
    public SCExportProfile? LoadProfileFromPath(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<SCExportProfile>(json, _jsonOptions);
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SCExportProfileService] Failed to load profile: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Delete an SC export profile by name
    /// </summary>
    public bool DeleteProfile(string profileName)
    {
        var filePath = GetProfilePath(profileName);
        if (!File.Exists(filePath))
            return false;

        File.Delete(filePath);
        return true;
    }

    /// <summary>
    /// List all available SC export profiles
    /// </summary>
    public List<SCExportProfileInfo> ListProfiles()
    {
        var profiles = new List<SCExportProfileInfo>();

        if (!Directory.Exists(_profilesDirectory))
            return profiles;

        foreach (var file in Directory.GetFiles(_profilesDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<SCExportProfile>(json, _jsonOptions);
                if (profile != null)
                {
                    profiles.Add(new SCExportProfileInfo
                    {
                        ProfileName = profile.ProfileName,
                        TargetEnvironment = profile.TargetEnvironment,
                        BindingCount = profile.Bindings.Count,
                        VJoyDeviceCount = profile.VJoyToSCInstance.Count,
                        Created = profile.Created,
                        Modified = profile.Modified,
                        FilePath = file
                    });
                }
            }
            catch (JsonException)
            {
                // Skip invalid profile files
            }
        }

        return profiles.OrderByDescending(p => p.Modified).ToList();
    }

    /// <summary>
    /// Duplicate an existing profile with a new name
    /// </summary>
    public SCExportProfile? DuplicateProfile(string sourceName, string newName)
    {
        var source = LoadProfile(sourceName);
        if (source == null)
            return null;

        var duplicate = new SCExportProfile
        {
            ProfileName = newName,
            TargetEnvironment = source.TargetEnvironment,
            TargetBuildId = source.TargetBuildId,
            Created = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
            IncludeKeyboardDefaults = source.IncludeKeyboardDefaults,
            IncludeMouseDefaults = source.IncludeMouseDefaults
        };

        // Copy vJoy mappings
        foreach (var kvp in source.VJoyToSCInstance)
        {
            duplicate.VJoyToSCInstance[kvp.Key] = kvp.Value;
        }

        // Copy bindings
        foreach (var binding in source.Bindings)
        {
            duplicate.Bindings.Add(new SCActionBinding
            {
                ActionMap = binding.ActionMap,
                ActionName = binding.ActionName,
                VJoyDevice = binding.VJoyDevice,
                InputName = binding.InputName,
                InputType = binding.InputType,
                Inverted = binding.Inverted,
                ActivationMode = binding.ActivationMode,
                Modifiers = new List<string>(binding.Modifiers)
            });
        }

        SaveProfile(duplicate);
        return duplicate;
    }

    /// <summary>
    /// Check if a profile with the given name exists
    /// </summary>
    public bool ProfileExists(string profileName)
    {
        var filePath = GetProfilePath(profileName);
        return File.Exists(filePath);
    }

    /// <summary>
    /// Rename a profile
    /// </summary>
    public bool RenameProfile(string oldName, string newName)
    {
        if (oldName == newName)
            return true;

        if (ProfileExists(newName))
            return false;

        var profile = LoadProfile(oldName);
        if (profile == null)
            return false;

        profile.ProfileName = newName;
        SaveProfile(profile);
        DeleteProfile(oldName);
        return true;
    }

    private string GetProfilePath(string profileName)
    {
        // Sanitize the profile name for use as filename
        var sanitized = string.Join("_", profileName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_profilesDirectory, $"{sanitized}.json");
    }
}

/// <summary>
/// Summary information about an SC export profile (for listing)
/// </summary>
public class SCExportProfileInfo
{
    public string ProfileName { get; init; } = "";
    public string TargetEnvironment { get; init; } = "";
    public int BindingCount { get; init; }
    public int VJoyDeviceCount { get; init; }
    public DateTime Created { get; init; }
    public DateTime Modified { get; init; }
    public string FilePath { get; init; } = "";
}
