using System.Text.Json;
using System.Text.Json.Serialization;
using Asteriq.Models;

namespace Asteriq.Services;

/// <summary>
/// Service for managing mapping profiles (save, load, list, delete)
/// </summary>
public class ProfileService
{
    private readonly string _profilesDirectory;
    private readonly string _settingsFile;
    private readonly JsonSerializerOptions _jsonOptions;
    private MappingProfile? _activeProfile;

    /// <summary>
    /// Event fired when the active profile changes
    /// </summary>
    public event EventHandler<ProfileChangedEventArgs>? ProfileChanged;

    /// <summary>
    /// The currently active profile (null if no profile is loaded)
    /// </summary>
    public MappingProfile? ActiveProfile
    {
        get => _activeProfile;
        private set
        {
            var oldProfile = _activeProfile;
            _activeProfile = value;
            if (oldProfile != value)
            {
                ProfileChanged?.Invoke(this, new ProfileChangedEventArgs(oldProfile, value));
            }
        }
    }

    /// <summary>
    /// Whether a profile is currently active
    /// </summary>
    public bool HasActiveProfile => _activeProfile != null;

    public ProfileService()
    {
        // Store profiles in %APPDATA%\Asteriq\Profiles
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var asteriqDir = Path.Combine(appData, "Asteriq");
        _profilesDirectory = Path.Combine(asteriqDir, "Profiles");
        _settingsFile = Path.Combine(asteriqDir, "settings.json");

        // Ensure directories exist
        Directory.CreateDirectory(_profilesDirectory);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    /// <summary>
    /// Constructor for testing with custom paths
    /// </summary>
    public ProfileService(string profilesDirectory, string settingsFile)
    {
        _profilesDirectory = profilesDirectory;
        _settingsFile = settingsFile;

        Directory.CreateDirectory(_profilesDirectory);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    /// <summary>
    /// Get the profiles directory path
    /// </summary>
    public string ProfilesDirectory => _profilesDirectory;

    /// <summary>
    /// Save a profile to disk
    /// </summary>
    public void SaveProfile(MappingProfile profile)
    {
        profile.ModifiedAt = DateTime.UtcNow;
        var filePath = GetProfilePath(profile.Id);
        var json = JsonSerializer.Serialize(profile, _jsonOptions);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Load a profile by ID
    /// </summary>
    public MappingProfile? LoadProfile(Guid profileId)
    {
        var filePath = GetProfilePath(profileId);
        return LoadProfileFromPath(filePath);
    }

    /// <summary>
    /// Load a profile from a file path
    /// </summary>
    public MappingProfile? LoadProfileFromPath(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<MappingProfile>(json, _jsonOptions);
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Failed to load profile: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Delete a profile by ID
    /// </summary>
    public bool DeleteProfile(Guid profileId)
    {
        var filePath = GetProfilePath(profileId);
        if (!File.Exists(filePath))
            return false;

        File.Delete(filePath);
        return true;
    }

    /// <summary>
    /// List all available profiles (metadata only)
    /// </summary>
    public List<ProfileInfo> ListProfiles()
    {
        var profiles = new List<ProfileInfo>();

        foreach (var file in Directory.GetFiles(_profilesDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<MappingProfile>(json, _jsonOptions);
                if (profile != null)
                {
                    profiles.Add(new ProfileInfo
                    {
                        Id = profile.Id,
                        Name = profile.Name,
                        Description = profile.Description,
                        DeviceAssignmentCount = profile.DeviceAssignments.Count,
                        AxisMappingCount = profile.AxisMappings.Count,
                        ButtonMappingCount = profile.ButtonMappings.Count,
                        HatMappingCount = profile.HatMappings.Count,
                        CreatedAt = profile.CreatedAt,
                        ModifiedAt = profile.ModifiedAt,
                        FilePath = file
                    });
                }
            }
            catch (JsonException)
            {
                // Skip invalid profile files
            }
        }

        return profiles.OrderByDescending(p => p.ModifiedAt).ToList();
    }

    /// <summary>
    /// Duplicate an existing profile
    /// </summary>
    public MappingProfile? DuplicateProfile(Guid sourceId, string newName)
    {
        var source = LoadProfile(sourceId);
        if (source == null)
            return null;

        var duplicate = new MappingProfile
        {
            Id = Guid.NewGuid(),
            Name = newName,
            Description = source.Description,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };

        // Deep copy device assignments
        foreach (var assignment in source.DeviceAssignments)
        {
            duplicate.DeviceAssignments.Add(new DeviceAssignment
            {
                PhysicalDevice = new PhysicalDeviceRef
                {
                    Name = assignment.PhysicalDevice.Name,
                    Guid = assignment.PhysicalDevice.Guid,
                    VidPid = assignment.PhysicalDevice.VidPid
                },
                VJoyDevice = assignment.VJoyDevice,
                DeviceMapOverride = assignment.DeviceMapOverride
            });
        }

        // Create ID mapping for layers (old ID -> new ID)
        var layerIdMap = new Dictionary<Guid, Guid>();

        // Deep copy shift layers
        foreach (var layer in source.ShiftLayers)
        {
            var newId = Guid.NewGuid();
            layerIdMap[layer.Id] = newId;

            duplicate.ShiftLayers.Add(new ShiftLayer
            {
                Id = newId,
                Name = layer.Name,
                ActivatorButton = layer.ActivatorButton == null ? null : new InputSource
                {
                    DeviceId = layer.ActivatorButton.DeviceId,
                    DeviceName = layer.ActivatorButton.DeviceName,
                    Type = layer.ActivatorButton.Type,
                    Index = layer.ActivatorButton.Index
                }
            });
        }

        // Deep copy axis mappings
        foreach (var mapping in source.AxisMappings)
        {
            duplicate.AxisMappings.Add(new AxisMapping
            {
                Id = Guid.NewGuid(),
                Name = mapping.Name,
                Enabled = mapping.Enabled,
                LayerId = mapping.LayerId.HasValue && layerIdMap.TryGetValue(mapping.LayerId.Value, out var newAxisLayerId) ? newAxisLayerId : null,
                Inputs = mapping.Inputs.Select(i => new InputSource
                {
                    DeviceId = i.DeviceId,
                    DeviceName = i.DeviceName,
                    Type = i.Type,
                    Index = i.Index
                }).ToList(),
                Output = new OutputTarget
                {
                    Type = mapping.Output.Type,
                    VJoyDevice = mapping.Output.VJoyDevice,
                    Index = mapping.Output.Index,
                    Modifiers = mapping.Output.Modifiers?.ToArray()
                },
                MergeOp = mapping.MergeOp,
                Invert = mapping.Invert,
                Curve = new AxisCurve
                {
                    Type = mapping.Curve.Type,
                    Curvature = mapping.Curve.Curvature,
                    Deadzone = mapping.Curve.Deadzone,
                    Saturation = mapping.Curve.Saturation,
                    ControlPoints = mapping.Curve.ControlPoints?.ToList()
                }
            });
        }

        // Deep copy button mappings
        foreach (var mapping in source.ButtonMappings)
        {
            duplicate.ButtonMappings.Add(new ButtonMapping
            {
                Id = Guid.NewGuid(),
                Name = mapping.Name,
                Enabled = mapping.Enabled,
                LayerId = mapping.LayerId.HasValue && layerIdMap.TryGetValue(mapping.LayerId.Value, out var newBtnLayerId) ? newBtnLayerId : null,
                Inputs = mapping.Inputs.Select(i => new InputSource
                {
                    DeviceId = i.DeviceId,
                    DeviceName = i.DeviceName,
                    Type = i.Type,
                    Index = i.Index
                }).ToList(),
                Output = new OutputTarget
                {
                    Type = mapping.Output.Type,
                    VJoyDevice = mapping.Output.VJoyDevice,
                    Index = mapping.Output.Index,
                    Modifiers = mapping.Output.Modifiers?.ToArray()
                },
                MergeOp = mapping.MergeOp,
                Invert = mapping.Invert,
                Mode = mapping.Mode,
                PulseDurationMs = mapping.PulseDurationMs,
                HoldDurationMs = mapping.HoldDurationMs
            });
        }

        // Deep copy hat mappings
        foreach (var mapping in source.HatMappings)
        {
            duplicate.HatMappings.Add(new HatMapping
            {
                Id = Guid.NewGuid(),
                Name = mapping.Name,
                Enabled = mapping.Enabled,
                LayerId = mapping.LayerId.HasValue && layerIdMap.TryGetValue(mapping.LayerId.Value, out var newHatLayerId) ? newHatLayerId : null,
                Inputs = mapping.Inputs.Select(i => new InputSource
                {
                    DeviceId = i.DeviceId,
                    DeviceName = i.DeviceName,
                    Type = i.Type,
                    Index = i.Index
                }).ToList(),
                Output = new OutputTarget
                {
                    Type = mapping.Output.Type,
                    VJoyDevice = mapping.Output.VJoyDevice,
                    Index = mapping.Output.Index,
                    Modifiers = mapping.Output.Modifiers?.ToArray()
                },
                MergeOp = mapping.MergeOp,
                Invert = mapping.Invert,
                UseContinuous = mapping.UseContinuous
            });
        }

        // Deep copy axis-to-button mappings
        foreach (var mapping in source.AxisToButtonMappings)
        {
            duplicate.AxisToButtonMappings.Add(new AxisToButtonMapping
            {
                Id = Guid.NewGuid(),
                Name = mapping.Name,
                Enabled = mapping.Enabled,
                LayerId = mapping.LayerId.HasValue && layerIdMap.TryGetValue(mapping.LayerId.Value, out var newA2BLayerId) ? newA2BLayerId : null,
                Inputs = mapping.Inputs.Select(i => new InputSource
                {
                    DeviceId = i.DeviceId,
                    DeviceName = i.DeviceName,
                    Type = i.Type,
                    Index = i.Index
                }).ToList(),
                Output = new OutputTarget
                {
                    Type = mapping.Output.Type,
                    VJoyDevice = mapping.Output.VJoyDevice,
                    Index = mapping.Output.Index,
                    Modifiers = mapping.Output.Modifiers?.ToArray()
                },
                MergeOp = mapping.MergeOp,
                Invert = mapping.Invert,
                Threshold = mapping.Threshold,
                ActivateAbove = mapping.ActivateAbove,
                Hysteresis = mapping.Hysteresis
            });
        }

        // Deep copy button-to-axis mappings
        foreach (var mapping in source.ButtonToAxisMappings)
        {
            duplicate.ButtonToAxisMappings.Add(new ButtonToAxisMapping
            {
                Id = Guid.NewGuid(),
                Name = mapping.Name,
                Enabled = mapping.Enabled,
                LayerId = mapping.LayerId.HasValue && layerIdMap.TryGetValue(mapping.LayerId.Value, out var newB2ALayerId) ? newB2ALayerId : null,
                Inputs = mapping.Inputs.Select(i => new InputSource
                {
                    DeviceId = i.DeviceId,
                    DeviceName = i.DeviceName,
                    Type = i.Type,
                    Index = i.Index
                }).ToList(),
                Output = new OutputTarget
                {
                    Type = mapping.Output.Type,
                    VJoyDevice = mapping.Output.VJoyDevice,
                    Index = mapping.Output.Index,
                    Modifiers = mapping.Output.Modifiers?.ToArray()
                },
                MergeOp = mapping.MergeOp,
                Invert = mapping.Invert,
                PressedValue = mapping.PressedValue,
                ReleasedValue = mapping.ReleasedValue,
                SmoothingMs = mapping.SmoothingMs
            });
        }

        SaveProfile(duplicate);
        return duplicate;
    }

    /// <summary>
    /// Export a profile to a specified path
    /// </summary>
    public bool ExportProfile(Guid profileId, string exportPath)
    {
        var profile = LoadProfile(profileId);
        if (profile == null)
            return false;

        var json = JsonSerializer.Serialize(profile, _jsonOptions);
        File.WriteAllText(exportPath, json);
        return true;
    }

    /// <summary>
    /// Import a profile from a specified path
    /// </summary>
    public MappingProfile? ImportProfile(string importPath, bool generateNewId = true)
    {
        var profile = LoadProfileFromPath(importPath);
        if (profile == null)
            return null;

        if (generateNewId)
        {
            profile.Id = Guid.NewGuid();
            profile.CreatedAt = DateTime.UtcNow;
        }

        profile.ModifiedAt = DateTime.UtcNow;
        SaveProfile(profile);
        return profile;
    }

    /// <summary>
    /// Get or set the last used profile ID
    /// </summary>
    public Guid? LastProfileId
    {
        get
        {
            var settings = LoadSettings();
            return settings.LastProfileId;
        }
        set
        {
            var settings = LoadSettings();
            settings.LastProfileId = value;
            SaveSettings(settings);
        }
    }

    /// <summary>
    /// Get or set auto-load setting
    /// </summary>
    public bool AutoLoadLastProfile
    {
        get
        {
            var settings = LoadSettings();
            return settings.AutoLoadLastProfile;
        }
        set
        {
            var settings = LoadSettings();
            settings.AutoLoadLastProfile = value;
            SaveSettings(settings);
        }
    }

    /// <summary>
    /// Load the last used profile if auto-load is enabled
    /// </summary>
    public MappingProfile? LoadLastProfileIfEnabled()
    {
        var settings = LoadSettings();
        if (!settings.AutoLoadLastProfile || settings.LastProfileId == null)
            return null;

        return LoadProfile(settings.LastProfileId.Value);
    }

    /// <summary>
    /// Activate a profile by ID, making it the current active profile
    /// </summary>
    public bool ActivateProfile(Guid profileId)
    {
        var profile = LoadProfile(profileId);
        if (profile == null)
            return false;

        ActiveProfile = profile;
        LastProfileId = profileId;
        return true;
    }

    /// <summary>
    /// Deactivate the current profile (mappings will stop)
    /// </summary>
    public void DeactivateProfile()
    {
        ActiveProfile = null;
    }

    /// <summary>
    /// Create a new empty profile with the given name
    /// </summary>
    public MappingProfile CreateProfile(string name, string description = "")
    {
        var profile = new MappingProfile
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };

        SaveProfile(profile);
        return profile;
    }

    /// <summary>
    /// Create and activate a new profile
    /// </summary>
    public MappingProfile CreateAndActivateProfile(string name, string description = "")
    {
        var profile = CreateProfile(name, description);
        ActivateProfile(profile.Id);
        return profile;
    }

    /// <summary>
    /// Save the currently active profile
    /// </summary>
    public void SaveActiveProfile()
    {
        if (ActiveProfile != null)
        {
            SaveProfile(ActiveProfile);
        }
    }

    /// <summary>
    /// Initialize the profile service and load last profile if enabled
    /// </summary>
    public void Initialize()
    {
        var profile = LoadLastProfileIfEnabled();
        if (profile != null)
        {
            ActiveProfile = profile;
        }
    }

    private string GetProfilePath(Guid profileId)
    {
        return Path.Combine(_profilesDirectory, $"{profileId}.json");
    }

    private AppSettings LoadSettings()
    {
        if (!File.Exists(_settingsFile))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_settingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    private void SaveSettings(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsFile);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(_settingsFile, json);
    }
}

/// <summary>
/// Summary information about a profile (for listing)
/// </summary>
public class ProfileInfo
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public int DeviceAssignmentCount { get; init; }
    public int AxisMappingCount { get; init; }
    public int ButtonMappingCount { get; init; }
    public int HatMappingCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ModifiedAt { get; init; }
    public string FilePath { get; init; } = "";

    public int TotalMappings => AxisMappingCount + ButtonMappingCount + HatMappingCount;
}

/// <summary>
/// Application settings
/// </summary>
public class AppSettings
{
    public Guid? LastProfileId { get; set; }
    public bool AutoLoadLastProfile { get; set; } = true;
}

/// <summary>
/// Event args for profile changed events
/// </summary>
public class ProfileChangedEventArgs : EventArgs
{
    public MappingProfile? OldProfile { get; }
    public MappingProfile? NewProfile { get; }

    public ProfileChangedEventArgs(MappingProfile? oldProfile, MappingProfile? newProfile)
    {
        OldProfile = oldProfile;
        NewProfile = newProfile;
    }
}
