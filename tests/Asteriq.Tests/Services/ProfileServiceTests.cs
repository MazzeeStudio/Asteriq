using Asteriq.Models;
using Asteriq.Services;

namespace Asteriq.Tests.Services;

public class ProfileServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _profilesDir;
    private readonly string _settingsFile;
    private readonly ProfileService _service;

    public ProfileServiceTests()
    {
        // Create unique test directory
        _testDir = Path.Combine(Path.GetTempPath(), $"AsteriqTests_{Guid.NewGuid()}");
        _profilesDir = Path.Combine(_testDir, "Profiles");
        _settingsFile = Path.Combine(_testDir, "settings.json");
        _service = new ProfileService(_profilesDir, _settingsFile);
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

        _service.SaveProfile(profile);

        var filePath = Path.Combine(_profilesDir, $"{profile.Id}.json");
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public void LoadProfile_ReturnsProfile()
    {
        var profile = CreateTestProfile("Load Test");
        _service.SaveProfile(profile);

        var loaded = _service.LoadProfile(profile.Id);

        Assert.NotNull(loaded);
        Assert.Equal(profile.Id, loaded.Id);
        Assert.Equal(profile.Name, loaded.Name);
    }

    [Fact]
    public void LoadProfile_NonExistent_ReturnsNull()
    {
        var loaded = _service.LoadProfile(Guid.NewGuid());

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

        _service.SaveProfile(profile);
        var loaded = _service.LoadProfile(profile.Id);

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

        _service.SaveProfile(profile);
        var loaded = _service.LoadProfile(profile.Id);

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
        _service.SaveProfile(profile);

        var result = _service.DeleteProfile(profile.Id);

        Assert.True(result);
        var filePath = Path.Combine(_profilesDir, $"{profile.Id}.json");
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public void DeleteProfile_NonExistent_ReturnsFalse()
    {
        var result = _service.DeleteProfile(Guid.NewGuid());

        Assert.False(result);
    }

    [Fact]
    public void ListProfiles_ReturnsAllProfiles()
    {
        var profile1 = CreateTestProfile("Profile 1");
        var profile2 = CreateTestProfile("Profile 2");
        var profile3 = CreateTestProfile("Profile 3");

        _service.SaveProfile(profile1);
        _service.SaveProfile(profile2);
        _service.SaveProfile(profile3);

        var profiles = _service.ListProfiles();

        Assert.Equal(3, profiles.Count);
        Assert.Contains(profiles, p => p.Name == "Profile 1");
        Assert.Contains(profiles, p => p.Name == "Profile 2");
        Assert.Contains(profiles, p => p.Name == "Profile 3");
    }

    [Fact]
    public void ListProfiles_Empty_ReturnsEmptyList()
    {
        var profiles = _service.ListProfiles();

        Assert.Empty(profiles);
    }

    [Fact]
    public void ListProfiles_IncludesMappingCounts()
    {
        var profile = CreateTestProfile("Count Test");
        profile.AxisMappings.Add(new AxisMapping { Name = "Axis 1" });
        profile.AxisMappings.Add(new AxisMapping { Name = "Axis 2" });
        profile.ButtonMappings.Add(new ButtonMapping { Name = "Button 1" });

        _service.SaveProfile(profile);
        var profiles = _service.ListProfiles();

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
        _service.SaveProfile(original);

        var duplicate = _service.DuplicateProfile(original.Id, "Copy of Original");

        Assert.NotNull(duplicate);
        Assert.NotEqual(original.Id, duplicate.Id);
        Assert.Equal("Copy of Original", duplicate.Name);
        Assert.Single(duplicate.AxisMappings);
    }

    [Fact]
    public void DuplicateProfile_NonExistent_ReturnsNull()
    {
        var duplicate = _service.DuplicateProfile(Guid.NewGuid(), "Copy");

        Assert.Null(duplicate);
    }

    [Fact]
    public void ExportProfile_CreatesFile()
    {
        var profile = CreateTestProfile("Export Test");
        _service.SaveProfile(profile);

        var exportPath = Path.Combine(_testDir, "exported.json");
        var result = _service.ExportProfile(profile.Id, exportPath);

        Assert.True(result);
        Assert.True(File.Exists(exportPath));
    }

    [Fact]
    public void ImportProfile_LoadsFromFile()
    {
        var profile = CreateTestProfile("Import Test");
        profile.AxisMappings.Add(new AxisMapping { Name = "Imported Axis" });
        _service.SaveProfile(profile);

        var exportPath = Path.Combine(_testDir, "to_import.json");
        _service.ExportProfile(profile.Id, exportPath);

        // Delete original
        _service.DeleteProfile(profile.Id);

        // Import
        var imported = _service.ImportProfile(exportPath);

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

        var imported = _service.ImportProfile(invalidPath);

        Assert.Null(imported);
    }

    [Fact]
    public void LastProfileId_PersistsAcrossInstances()
    {
        var profile = CreateTestProfile("Last Test");
        _service.SaveProfile(profile);
        _service.LastProfileId = profile.Id;

        // Create new service instance
        var newService = new ProfileService(_profilesDir, _settingsFile);

        Assert.Equal(profile.Id, newService.LastProfileId);
    }

    [Fact]
    public void AutoLoadLastProfile_DefaultTrue()
    {
        Assert.True(_service.AutoLoadLastProfile);
    }

    [Fact]
    public void AutoLoadLastProfile_Persists()
    {
        _service.AutoLoadLastProfile = false;

        var newService = new ProfileService(_profilesDir, _settingsFile);

        Assert.False(newService.AutoLoadLastProfile);
    }

    [Fact]
    public void LoadLastProfileIfEnabled_ReturnsProfile()
    {
        var profile = CreateTestProfile("Auto Load Test");
        _service.SaveProfile(profile);
        _service.LastProfileId = profile.Id;
        _service.AutoLoadLastProfile = true;

        var loaded = _service.LoadLastProfileIfEnabled();

        Assert.NotNull(loaded);
        Assert.Equal(profile.Id, loaded.Id);
    }

    [Fact]
    public void LoadLastProfileIfEnabled_DisabledReturnsNull()
    {
        var profile = CreateTestProfile("Disabled Test");
        _service.SaveProfile(profile);
        _service.LastProfileId = profile.Id;
        _service.AutoLoadLastProfile = false;

        var loaded = _service.LoadLastProfileIfEnabled();

        Assert.Null(loaded);
    }

    [Fact]
    public void SaveProfile_UpdatesModifiedAt()
    {
        var profile = CreateTestProfile("Modified Test");
        var originalModified = profile.ModifiedAt;

        Thread.Sleep(10); // Ensure time difference
        _service.SaveProfile(profile);

        var loaded = _service.LoadProfile(profile.Id);
        Assert.NotNull(loaded);
        Assert.True(loaded.ModifiedAt > originalModified);
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
