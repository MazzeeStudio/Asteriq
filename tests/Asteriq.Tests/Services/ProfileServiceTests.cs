using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Asteriq.Tests.Services;

public class ProfileRepositoryTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _profilesDir;
    private readonly ProfileRepository _repository;

    public ProfileRepositoryTests()
    {
        // Create unique test directory
        _testDir = Path.Combine(Path.GetTempPath(), $"AsteriqTests_{Guid.NewGuid()}");
        _profilesDir = Path.Combine(_testDir, "Profiles");
        _repository = new ProfileRepository(NullLogger<ProfileRepository>.Instance, _profilesDir);
    }

    public void Dispose()
    {
        // Cleanup test directory
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public void SaveProfile_CreatesFile()
    {
        var profile = CreateTestProfile("Test Profile");

        _repository.SaveProfile(profile);

        var filePath = Path.Combine(_profilesDir, $"{profile.Id}.json");
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public void LoadProfile_ReturnsProfile()
    {
        var profile = CreateTestProfile("Load Test");
        _repository.SaveProfile(profile);

        var loaded = _repository.LoadProfile(profile.Id);

        Assert.NotNull(loaded);
        Assert.Equal(profile.Id, loaded.Id);
        Assert.Equal(profile.Name, loaded.Name);
    }

    [Fact]
    public void LoadProfile_NonExistent_ReturnsNull()
    {
        var loaded = _repository.LoadProfile(Guid.NewGuid());

        Assert.Null(loaded);
    }

    [Fact]
    public void SaveProfile_PreservesAxisMappings()
    {
        var profile = CreateTestProfile("Axis Test");
        profile.AxisMappings.Add(new AxisMapping
        {
            Name = "Test Axis",
            Inputs = new List<InputSource>
            {
                new InputSource
                {
                    DeviceId = "device-1",
                    DeviceName = "Test Device",
                    Type = InputType.Axis,
                    Index = 0
                }
            },
            Output = new OutputTarget
            {
                Type = OutputType.VJoyAxis,
                VJoyDevice = 1,
                Index = 0
            },
            Curve = new AxisCurve
            {
                Type = CurveType.SCurve,
                Curvature = 0.5f,
                Deadzone = 0.05f
            }
        });

        _repository.SaveProfile(profile);
        var loaded = _repository.LoadProfile(profile.Id);

        Assert.NotNull(loaded);
        Assert.Single(loaded.AxisMappings);
        var mapping = loaded.AxisMappings[0];
        Assert.Equal("Test Axis", mapping.Name);
        Assert.Equal(CurveType.SCurve, mapping.Curve.Type);
        Assert.Equal(0.5f, mapping.Curve.Curvature);
        Assert.Equal(0.05f, mapping.Curve.Deadzone);
    }

    [Fact]
    public void SaveProfile_PreservesButtonMappings()
    {
        var profile = CreateTestProfile("Button Test");
        profile.ButtonMappings.Add(new ButtonMapping
        {
            Name = "Test Button",
            Mode = ButtonMode.Toggle,
            PulseDurationMs = 200,
            HoldDurationMs = 750,
            Inputs = new List<InputSource>
            {
                new InputSource
                {
                    DeviceId = "device-1",
                    DeviceName = "Test Device",
                    Type = InputType.Button,
                    Index = 5
                }
            },
            Output = new OutputTarget
            {
                Type = OutputType.VJoyButton,
                VJoyDevice = 1,
                Index = 1
            }
        });

        _repository.SaveProfile(profile);
        var loaded = _repository.LoadProfile(profile.Id);

        Assert.NotNull(loaded);
        Assert.Single(loaded.ButtonMappings);
        var mapping = loaded.ButtonMappings[0];
        Assert.Equal("Test Button", mapping.Name);
        Assert.Equal(ButtonMode.Toggle, mapping.Mode);
        Assert.Equal(200, mapping.PulseDurationMs);
        Assert.Equal(750, mapping.HoldDurationMs);
    }

    [Fact]
    public void DeleteProfile_RemovesFile()
    {
        var profile = CreateTestProfile("Delete Test");
        _repository.SaveProfile(profile);

        var result = _repository.DeleteProfile(profile.Id);

        Assert.True(result);
        var filePath = Path.Combine(_profilesDir, $"{profile.Id}.json");
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public void DeleteProfile_NonExistent_ReturnsFalse()
    {
        var result = _repository.DeleteProfile(Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public void ListProfiles_ReturnsAllProfiles()
    {
        var profile1 = CreateTestProfile("Profile 1");
        var profile2 = CreateTestProfile("Profile 2");
        var profile3 = CreateTestProfile("Profile 3");

        _repository.SaveProfile(profile1);
        _repository.SaveProfile(profile2);
        _repository.SaveProfile(profile3);

        var profiles = _repository.ListProfiles();

        Assert.Equal(3, profiles.Count);
        Assert.Contains(profiles, p => p.Name == "Profile 1");
        Assert.Contains(profiles, p => p.Name == "Profile 2");
        Assert.Contains(profiles, p => p.Name == "Profile 3");
    }

    [Fact]
    public void ListProfiles_Empty_ReturnsEmptyList()
    {
        var profiles = _repository.ListProfiles();

        Assert.Empty(profiles);
    }

    [Fact]
    public void ListProfiles_IncludesMappingCounts()
    {
        var profile = CreateTestProfile("Count Test");
        profile.AxisMappings.Add(new AxisMapping { Name = "Axis 1" });
        profile.AxisMappings.Add(new AxisMapping { Name = "Axis 2" });
        profile.ButtonMappings.Add(new ButtonMapping { Name = "Button 1" });

        _repository.SaveProfile(profile);
        var profiles = _repository.ListProfiles();

        var info = profiles.First(p => p.Id == profile.Id);
        Assert.Equal(2, info.AxisMappingCount);
        Assert.Equal(1, info.ButtonMappingCount);
        Assert.Equal(3, info.TotalMappings);
    }

    [Fact]
    public void DuplicateProfile_CreatesNewProfile()
    {
        var original = CreateTestProfile("Original");
        original.AxisMappings.Add(new AxisMapping { Name = "Axis 1" });
        _repository.SaveProfile(original);

        var duplicate = _repository.DuplicateProfile(original.Id, "Copy of Original");

        Assert.NotNull(duplicate);
        Assert.NotEqual(original.Id, duplicate.Id);
        Assert.Equal("Copy of Original", duplicate.Name);
        Assert.Single(duplicate.AxisMappings);
    }

    [Fact]
    public void DuplicateProfile_NonExistent_ReturnsNull()
    {
        var duplicate = _repository.DuplicateProfile(Guid.NewGuid(), "Copy");

        Assert.Null(duplicate);
    }

    [Fact]
    public void ExportProfile_CreatesFile()
    {
        var profile = CreateTestProfile("Export Test");
        _repository.SaveProfile(profile);

        var exportPath = Path.Combine(_testDir, "exported.json");
        var result = _repository.ExportProfile(profile.Id, exportPath);

        Assert.True(result);
        Assert.True(File.Exists(exportPath));
    }

    [Fact]
    public void ImportProfile_LoadsFromFile()
    {
        var profile = CreateTestProfile("Import Test");
        profile.AxisMappings.Add(new AxisMapping { Name = "Imported Axis" });
        _repository.SaveProfile(profile);

        var exportPath = Path.Combine(_testDir, "to_import.json");
        _repository.ExportProfile(profile.Id, exportPath);

        // Delete original
        _repository.DeleteProfile(profile.Id);

        // Import
        var imported = _repository.ImportProfile(exportPath);

        Assert.NotNull(imported);
        Assert.NotEqual(profile.Id, imported.Id); // New ID generated
        Assert.Equal("Import Test", imported.Name);
        Assert.Single(imported.AxisMappings);
    }

    [Fact]
    public void ImportProfile_InvalidFile_ReturnsNull()
    {
        var invalidPath = Path.Combine(_testDir, "invalid.json");
        File.WriteAllText(invalidPath, "not valid json {{{");

        var imported = _repository.ImportProfile(invalidPath);

        Assert.Null(imported);
    }

    [Fact]
    public void SaveProfile_UpdatesModifiedAt()
    {
        var profile = CreateTestProfile("Modified Test");
        var originalModified = profile.ModifiedAt;

        Thread.Sleep(10); // Ensure time difference
        _repository.SaveProfile(profile);

        var loaded = _repository.LoadProfile(profile.Id);
        Assert.NotNull(loaded);
        Assert.True(loaded.ModifiedAt > originalModified);
    }

    [Fact]
    public void SaveProfile_PreservesDeviceAssignments()
    {
        var profile = CreateTestProfile("Device Assignment Test");
        profile.DeviceAssignments.Add(new DeviceAssignment
        {
            PhysicalDevice = new PhysicalDeviceRef
            {
                Name = "VPC Stick WarBRD",
                Guid = "abc123-def456",
                VidPid = "3344:0194"
            },
            VJoyDevice = 1,
            DeviceMapOverride = "custom_map"
        });

        _repository.SaveProfile(profile);
        var loaded = _repository.LoadProfile(profile.Id);

        Assert.NotNull(loaded);
        Assert.Single(loaded.DeviceAssignments);
        var assignment = loaded.DeviceAssignments[0];
        Assert.Equal("VPC Stick WarBRD", assignment.PhysicalDevice.Name);
        Assert.Equal("abc123-def456", assignment.PhysicalDevice.Guid);
        Assert.Equal("3344:0194", assignment.PhysicalDevice.VidPid);
        Assert.Equal(1u, assignment.VJoyDevice);
        Assert.Equal("custom_map", assignment.DeviceMapOverride);
    }

    [Fact]
    public void DuplicateProfile_CopiesDeviceAssignments()
    {
        var original = CreateTestProfile("Original With Assignments");
        original.DeviceAssignments.Add(new DeviceAssignment
        {
            PhysicalDevice = new PhysicalDeviceRef
            {
                Name = "Test Device",
                Guid = "guid-123",
                VidPid = "1234:5678"
            },
            VJoyDevice = 2
        });
        _repository.SaveProfile(original);

        var duplicate = _repository.DuplicateProfile(original.Id, "Copy");

        Assert.NotNull(duplicate);
        Assert.Single(duplicate.DeviceAssignments);
        Assert.Equal("Test Device", duplicate.DeviceAssignments[0].PhysicalDevice.Name);
        Assert.Equal(2u, duplicate.DeviceAssignments[0].VJoyDevice);
    }

    [Fact]
    public void CreateProfile_CreatesAndSavesProfile()
    {
        var profile = _repository.CreateProfile("New Profile", "Test description");

        Assert.NotNull(profile);
        Assert.Equal("New Profile", profile.Name);
        Assert.Equal("Test description", profile.Description);

        // Verify it was saved
        var loaded = _repository.LoadProfile(profile.Id);
        Assert.NotNull(loaded);
        Assert.Equal("New Profile", loaded.Name);
    }

    [Fact]
    public void ListProfiles_IncludesDeviceAssignmentCount()
    {
        var profile = CreateTestProfile("Assignment Count Test");
        profile.DeviceAssignments.Add(new DeviceAssignment
        {
            PhysicalDevice = new PhysicalDeviceRef { Name = "Device 1" },
            VJoyDevice = 1
        });
        profile.DeviceAssignments.Add(new DeviceAssignment
        {
            PhysicalDevice = new PhysicalDeviceRef { Name = "Device 2" },
            VJoyDevice = 2
        });

        _repository.SaveProfile(profile);
        var profiles = _repository.ListProfiles();

        var info = profiles.First(p => p.Id == profile.Id);
        Assert.Equal(2, info.DeviceAssignmentCount);
    }

    private static MappingProfile CreateTestProfile(string name)
    {
        return new MappingProfile
        {
            Name = name,
            Description = $"Test profile: {name}"
        };
    }
}

public class ProfileManagerTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _profilesDir;
    private readonly ProfileRepository _repository;
    private readonly ApplicationSettingsService _appSettings;
    private readonly ProfileManager _manager;

    public ProfileManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"AsteriqTests_{Guid.NewGuid()}");
        _profilesDir = Path.Combine(_testDir, "Profiles");
        _repository = new ProfileRepository(NullLogger<ProfileRepository>.Instance, _profilesDir);
        _appSettings = new ApplicationSettingsService(NullLogger<ApplicationSettingsService>.Instance);
        _manager = new ProfileManager(_repository, _appSettings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public void ActivateProfile_SetsActiveProfile()
    {
        var profile = CreateTestProfile("Activate Test");
        _repository.SaveProfile(profile);

        var result = _manager.ActivateProfile(profile.Id);

        Assert.True(result);
        Assert.NotNull(_manager.ActiveProfile);
        Assert.Equal(profile.Id, _manager.ActiveProfile.Id);
        Assert.True(_manager.HasActiveProfile);
    }

    [Fact]
    public void ActivateProfile_NonExistent_ReturnsFalse()
    {
        var result = _manager.ActivateProfile(Guid.NewGuid());

        Assert.False(result);
        Assert.Null(_manager.ActiveProfile);
        Assert.False(_manager.HasActiveProfile);
    }

    [Fact]
    public void ActivateProfile_UpdatesLastProfileId()
    {
        var profile = CreateTestProfile("Last Profile Test");
        _repository.SaveProfile(profile);

        _manager.ActivateProfile(profile.Id);

        Assert.Equal(profile.Id, _appSettings.LastProfileId);
    }

    [Fact]
    public void DeactivateProfile_ClearsActiveProfile()
    {
        var profile = CreateTestProfile("Deactivate Test");
        _repository.SaveProfile(profile);
        _manager.ActivateProfile(profile.Id);

        _manager.DeactivateProfile();

        Assert.Null(_manager.ActiveProfile);
        Assert.False(_manager.HasActiveProfile);
    }

    [Fact]
    public void CreateAndActivateProfile_CreatesAndActivates()
    {
        var profile = _manager.CreateAndActivateProfile("Active Profile", "Activated");

        Assert.NotNull(profile);
        Assert.Equal("Active Profile", profile.Name);
        Assert.True(_manager.HasActiveProfile);
        Assert.Equal(profile.Id, _manager.ActiveProfile!.Id);
    }

    [Fact]
    public void SaveActiveProfile_SavesCurrentProfile()
    {
        var profile = _manager.CreateAndActivateProfile("Save Active Test");
        _manager.ActiveProfile!.Description = "Modified description";

        _manager.SaveActiveProfile();

        var loaded = _repository.LoadProfile(profile.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Modified description", loaded.Description);
    }

    [Fact]
    public void ProfileChanged_FiresOnActivation()
    {
        var profile = CreateTestProfile("Event Test");
        _repository.SaveProfile(profile);

        MappingProfile? newProfile = null;
        MappingProfile? oldProfile = null;
        bool eventFired = false;

        _manager.ProfileChanged += (sender, args) =>
        {
            eventFired = true;
            oldProfile = args.OldProfile;
            newProfile = args.NewProfile;
        };

        _manager.ActivateProfile(profile.Id);

        Assert.True(eventFired);
        Assert.Null(oldProfile);
        Assert.NotNull(newProfile);
        Assert.Equal(profile.Id, newProfile.Id);
    }

    [Fact]
    public void ProfileChanged_FiresOnDeactivation()
    {
        var profile = CreateTestProfile("Deactivate Event Test");
        _repository.SaveProfile(profile);
        _manager.ActivateProfile(profile.Id);

        MappingProfile? newProfile = null;
        MappingProfile? oldProfile = null;
        bool eventFired = false;

        _manager.ProfileChanged += (sender, args) =>
        {
            eventFired = true;
            oldProfile = args.OldProfile;
            newProfile = args.NewProfile;
        };

        _manager.DeactivateProfile();

        Assert.True(eventFired);
        Assert.NotNull(oldProfile);
        Assert.Equal(profile.Id, oldProfile.Id);
        Assert.Null(newProfile);
    }

    [Fact]
    public void ProfileChanged_FiresOnSwitch()
    {
        var profile1 = CreateTestProfile("Profile 1");
        var profile2 = CreateTestProfile("Profile 2");
        _repository.SaveProfile(profile1);
        _repository.SaveProfile(profile2);
        _manager.ActivateProfile(profile1.Id);

        MappingProfile? newProfile = null;
        MappingProfile? oldProfile = null;

        _manager.ProfileChanged += (sender, args) =>
        {
            oldProfile = args.OldProfile;
            newProfile = args.NewProfile;
        };

        _manager.ActivateProfile(profile2.Id);

        Assert.NotNull(oldProfile);
        Assert.NotNull(newProfile);
        Assert.Equal(profile1.Id, oldProfile.Id);
        Assert.Equal(profile2.Id, newProfile.Id);
    }

    private static MappingProfile CreateTestProfile(string name)
    {
        return new MappingProfile
        {
            Name = name,
            Description = $"Test profile: {name}"
        };
    }
}
