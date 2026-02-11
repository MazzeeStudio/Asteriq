using System.Text.Json;
using System.Text.Json.Serialization;
using Asteriq.Models;
using Asteriq.Services.Abstractions;
using Asteriq.UI;

namespace Asteriq.Services;

/// <summary>
/// Service for managing mapping profiles (save, load, list, delete)
/// </summary>
public class ProfileService : IProfileService
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
    public bool HasActiveProfile => _activeProfile is not null;

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
            var profile = JsonSerializer.Deserialize<MappingProfile>(json, _jsonOptions);
            if (profile is not null)
            {
                Console.WriteLine($"[ProfileService] Loaded profile '{profile.Name}' with {profile.ButtonMappings.Count} button mappings");
                foreach (var mapping in profile.ButtonMappings)
                {
                    Console.WriteLine($"  - {mapping.Name}: Output.Type={mapping.Output.Type}, KeyName={mapping.Output.KeyName}");
                }
            }
            return profile;
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
                if (profile is not null)
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
        if (source is null)
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
                ActivatorButton = layer.ActivatorButton is null ? null : new InputSource
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
                    Modifiers = mapping.Output.Modifiers?.ToList()
                },
                MergeOp = mapping.MergeOp,
                Invert = mapping.Invert,
                Curve = new AxisCurve
                {
                    Type = mapping.Curve.Type,
                    Curvature = mapping.Curve.Curvature,
                    Saturation = mapping.Curve.Saturation,
                    DeadzoneLow = mapping.Curve.DeadzoneLow,
                    DeadzoneCenterLow = mapping.Curve.DeadzoneCenterLow,
                    DeadzoneCenterHigh = mapping.Curve.DeadzoneCenterHigh,
                    DeadzoneHigh = mapping.Curve.DeadzoneHigh,
                    DeadzoneMode = mapping.Curve.DeadzoneMode,
                    ControlPoints = mapping.Curve.ControlPoints?.Select(p => new CurvePoint(p.Input, p.Output)).ToList(),
                    Symmetrical = mapping.Curve.Symmetrical
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
                    Modifiers = mapping.Output.Modifiers?.ToList()
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
                    Modifiers = mapping.Output.Modifiers?.ToList()
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
                    Modifiers = mapping.Output.Modifiers?.ToList()
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
                    Modifiers = mapping.Output.Modifiers?.ToList()
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
        if (profile is null)
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
        if (profile is null)
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
    /// Font size setting for accessibility (Small/Medium/Large)
    /// </summary>
    public FontSizeOption FontSize
    {
        get
        {
            var settings = LoadSettings();
            return settings.FontSize;
        }
        set
        {
            var settings = LoadSettings();
            settings.FontSize = value;
            SaveSettings(settings);
            FontSizeChanged?.Invoke(this, value);
        }
    }

    /// <summary>
    /// Font family setting (Carbon/Consolas)
    /// </summary>
    public UIFontFamily FontFamily
    {
        get
        {
            var settings = LoadSettings();
            return settings.FontFamily;
        }
        set
        {
            var settings = LoadSettings();
            settings.FontFamily = value;
            SaveSettings(settings);
        }
    }

    /// <summary>
    /// Event fired when font size setting changes
    /// </summary>
    public event EventHandler<FontSizeOption>? FontSizeChanged;

    /// <summary>
    /// Theme setting
    /// </summary>
    public FUITheme Theme
    {
        get
        {
            var settings = LoadSettings();
            return settings.Theme;
        }
        set
        {
            var settings = LoadSettings();
            settings.Theme = value;
            SaveSettings(settings);
            ThemeChanged?.Invoke(this, value);
        }
    }

    /// <summary>
    /// Event fired when theme setting changes
    /// </summary>
    public event EventHandler<FUITheme>? ThemeChanged;

    /// <summary>
    /// Load background settings from app settings
    /// </summary>
    public (int gridStrength, int glowIntensity, int noiseIntensity, int scanlineIntensity, int vignetteStrength) LoadBackgroundSettings()
    {
        var settings = LoadSettings();
        return (settings.GridStrength, settings.GlowIntensity, settings.NoiseIntensity,
                settings.ScanlineIntensity, settings.VignetteStrength);
    }

    /// <summary>
    /// Save background settings to app settings
    /// </summary>
    public void SaveBackgroundSettings(int gridStrength, int glowIntensity, int noiseIntensity, int scanlineIntensity, int vignetteStrength)
    {
        var settings = LoadSettings();
        settings.GridStrength = gridStrength;
        settings.GlowIntensity = glowIntensity;
        settings.NoiseIntensity = noiseIntensity;
        settings.ScanlineIntensity = scanlineIntensity;
        settings.VignetteStrength = vignetteStrength;
        SaveSettings(settings);
    }

    /// <summary>
    /// Load the last used profile if auto-load is enabled
    /// </summary>
    public MappingProfile? LoadLastProfileIfEnabled()
    {
        var settings = LoadSettings();
        if (!settings.AutoLoadLastProfile || settings.LastProfileId is null)
            return null;

        return LoadProfile(settings.LastProfileId.Value);
    }

    /// <summary>
    /// Get or set the last used SC export profile name
    /// </summary>
    public string? LastSCExportProfile
    {
        get
        {
            var settings = LoadSettings();
            return settings.LastSCExportProfile;
        }
        set
        {
            var settings = LoadSettings();
            settings.LastSCExportProfile = value;
            SaveSettings(settings);
        }
    }

    /// <summary>
    /// Get or set auto-load setting for SC export profiles
    /// </summary>
    public bool AutoLoadLastSCExportProfile
    {
        get
        {
            var settings = LoadSettings();
            return settings.AutoLoadLastSCExportProfile;
        }
        set
        {
            var settings = LoadSettings();
            settings.AutoLoadLastSCExportProfile = value;
            SaveSettings(settings);
        }
    }

    /// <summary>
    /// Load window state (size and position)
    /// </summary>
    public (int width, int height, int x, int y) LoadWindowState()
    {
        var settings = LoadSettings();
        return (settings.WindowWidth, settings.WindowHeight, settings.WindowX, settings.WindowY);
    }

    /// <summary>
    /// Save window state (size and position)
    /// </summary>
    public void SaveWindowState(int width, int height, int x, int y)
    {
        var settings = LoadSettings();
        settings.WindowWidth = width;
        settings.WindowHeight = height;
        settings.WindowX = x;
        settings.WindowY = y;
        SaveSettings(settings);
    }

    /// <summary>
    /// Get or set whether clicking the close button minimizes to tray instead of exiting
    /// </summary>
    public bool CloseToTray
    {
        get
        {
            var settings = LoadSettings();
            return settings.CloseToTray;
        }
        set
        {
            var settings = LoadSettings();
            settings.CloseToTray = value;
            SaveSettings(settings);
        }
    }

    /// <summary>
    /// Get or set the system tray icon type (joystick or throttle)
    /// </summary>
    public TrayIconType TrayIconType
    {
        get
        {
            var settings = LoadSettings();
            return settings.TrayIconType;
        }
        set
        {
            var settings = LoadSettings();
            settings.TrayIconType = value;
            SaveSettings(settings);
        }
    }

    /// <summary>
    /// Activate a profile by ID, making it the current active profile
    /// </summary>
    public bool ActivateProfile(Guid profileId)
    {
        var profile = LoadProfile(profileId);
        if (profile is null)
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
        if (ActiveProfile is not null)
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
        if (profile is not null)
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
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
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
