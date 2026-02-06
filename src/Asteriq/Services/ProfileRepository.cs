using System.Text.Json;
using System.Text.Json.Serialization;
using Asteriq.Models;
using Asteriq.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Asteriq.Services;

/// <summary>
/// Repository for profile storage and retrieval
/// </summary>
public class ProfileRepository : IProfileRepository
{
    private readonly ILogger<ProfileRepository> _logger;
    private readonly string _profilesDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public string ProfilesDirectory => _profilesDirectory;

    public ProfileRepository(ILogger<ProfileRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Store profiles in %APPDATA%\Asteriq\Profiles
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var asteriqDir = Path.Combine(appData, "Asteriq");
        _profilesDirectory = Path.Combine(asteriqDir, "Profiles");

        // Ensure directories exist
        Directory.CreateDirectory(_profilesDirectory);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        _logger.LogDebug("ProfileRepository initialized. Profiles directory: {ProfilesDirectory}", _profilesDirectory);
    }

    /// <summary>
    /// Constructor for testing with custom paths
    /// </summary>
    public ProfileRepository(ILogger<ProfileRepository> logger, string profilesDirectory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _profilesDirectory = profilesDirectory;
        Directory.CreateDirectory(_profilesDirectory);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        _logger.LogDebug("ProfileRepository initialized with custom path: {ProfilesDirectory}", _profilesDirectory);
    }

    public void SaveProfile(MappingProfile profile)
    {
        profile.ModifiedAt = DateTime.UtcNow;
        var filePath = GetProfilePath(profile.Id);
        var json = JsonSerializer.Serialize(profile, _jsonOptions);
        File.WriteAllText(filePath, json);
    }

    public MappingProfile? LoadProfile(Guid profileId)
    {
        var filePath = GetProfilePath(profileId);
        return LoadProfileFromPath(filePath);
    }

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
                _logger.LogInformation("Loaded profile '{ProfileName}' with {ButtonMappingCount} button mappings",
                    profile.Name, profile.ButtonMappings.Count);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    foreach (var mapping in profile.ButtonMappings)
                    {
                        _logger.LogDebug("  Button mapping: {MappingName}, Output.Type={OutputType}, KeyName={KeyName}",
                            mapping.Name, mapping.Output.Type, mapping.Output.KeyName);
                    }
                }
            }
            return profile;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to load profile from {FilePath}", filePath);
            return null;
        }
    }

    public bool DeleteProfile(Guid profileId)
    {
        var filePath = GetProfilePath(profileId);
        if (!File.Exists(filePath))
            return false;

        File.Delete(filePath);
        return true;
    }

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
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping invalid profile file: {FilePath}", file);
            }
        }

        return profiles.OrderByDescending(p => p.ModifiedAt).ToList();
    }

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

    public bool ExportProfile(Guid profileId, string exportPath)
    {
        var profile = LoadProfile(profileId);
        if (profile is null)
            return false;

        var json = JsonSerializer.Serialize(profile, _jsonOptions);
        File.WriteAllText(exportPath, json);
        return true;
    }

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

    private string GetProfilePath(Guid profileId)
    {
        return Path.Combine(_profilesDirectory, $"{profileId}.json");
    }
}
