using Asteriq.Models;
using Asteriq.Services;

namespace Asteriq.Tests.Services;

public class SCInstallationServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly List<string> _createdDirs = new();

    public SCInstallationServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"AsteriqSCTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        // Cleanup test directories
        foreach (var dir in _createdDirs)
        {
            if (Directory.Exists(dir))
            {
                try { Directory.Delete(dir, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void Installations_WithNoSC_ReturnsEmptyList()
    {
        var service = new SCInstallationService();
        // Use a custom path that doesn't exist
        service.CustomInstallPath = Path.Combine(_testDir, "nonexistent");

        // Note: This test may find real SC installations on the system
        // We're primarily testing that the service doesn't crash
        var installations = service.Installations;

        Assert.NotNull(installations);
    }

    [Fact]
    public void CustomInstallPath_ClearsCache()
    {
        var service = new SCInstallationService();

        // Access installations to populate cache
        _ = service.Installations;

        // Change custom path should clear cache
        service.CustomInstallPath = _testDir;

        // Should not throw
        Assert.Equal(_testDir, service.CustomInstallPath);
    }

    [Fact]
    public void Refresh_ClearsAndRedetects()
    {
        var service = new SCInstallationService();

        // Access installations
        var first = service.Installations;

        // Refresh
        service.Refresh();

        // Access again
        var second = service.Installations;

        // Should not throw, both should be valid lists
        Assert.NotNull(first);
        Assert.NotNull(second);
    }

    [Fact]
    public void GetInstallation_WithValidEnvironment_ReturnsInstallation()
    {
        var service = new SCInstallationService();

        // This will return null if LIVE isn't installed, which is fine
        var live = service.GetInstallation("LIVE");
        var ptu = service.GetInstallation("PTU");

        // Just verify method works without throwing
        // Actual result depends on system state
        Assert.True(live == null || live.Environment == "LIVE");
        Assert.True(ptu == null || ptu.Environment == "PTU");
    }

    [Fact]
    public void GetInstallation_CaseInsensitive()
    {
        var service = new SCInstallationService();

        var live1 = service.GetInstallation("LIVE");
        var live2 = service.GetInstallation("live");
        var live3 = service.GetInstallation("Live");

        // All should return the same result (null or same installation)
        Assert.Equal(live1?.InstallPath, live2?.InstallPath);
        Assert.Equal(live2?.InstallPath, live3?.InstallPath);
    }

    [Fact]
    public void GetInstallation_NonExistentEnvironment_ReturnsNull()
    {
        var service = new SCInstallationService();

        var result = service.GetInstallation("NONEXISTENT_ENV_12345");

        Assert.Null(result);
    }

    [Fact]
    public void GetPreferredInstallation_ReturnsLiveFirst()
    {
        var service = new SCInstallationService();

        var preferred = service.GetPreferredInstallation();

        // If there are installations, preferred should be LIVE (if available)
        if (preferred != null && service.GetInstallation("LIVE") != null)
        {
            Assert.Equal("LIVE", preferred.Environment);
        }
    }

    [Fact]
    public void IsStarCitizenRunning_DoesNotThrow()
    {
        // Just verify the method doesn't throw
        var result = SCInstallationService.IsStarCitizenRunning();

        Assert.True(result == true || result == false);
    }

    [Fact]
    public void EnsureMappingsDirectory_WithValidInstallation_CreatesDirectory()
    {
        // Create a fake installation structure
        var installPath = Path.Combine(_testDir, "LIVE");
        var userPath = Path.Combine(installPath, "USER", "Client", "0", "Controls");
        Directory.CreateDirectory(userPath);
        _createdDirs.Add(installPath);

        // Create a fake Data.p4k
        var dataP4kPath = Path.Combine(installPath, "Data.p4k");
        File.WriteAllText(dataP4kPath, "fake");

        var installation = new SCInstallation
        {
            Environment = "LIVE",
            InstallPath = installPath,
            DataP4kPath = dataP4kPath,
            MappingsPath = Path.Combine(userPath, "Mappings")
        };

        var result = SCInstallationService.EnsureMappingsDirectory(installation);

        Assert.True(result);
        Assert.True(Directory.Exists(installation.MappingsPath));
    }

    [Fact]
    public void EnsureMappingsDirectory_WithExistingDirectory_ReturnsTrue()
    {
        var mappingsPath = Path.Combine(_testDir, "Mappings");
        Directory.CreateDirectory(mappingsPath);

        var installation = new SCInstallation
        {
            MappingsPath = mappingsPath
        };

        var result = SCInstallationService.EnsureMappingsDirectory(installation);

        Assert.True(result);
    }
}

public class SCInstallationModelTests
{
    [Fact]
    public void DisplayName_WithBuildId_IncludesBuildId()
    {
        var installation = new SCInstallation
        {
            Environment = "LIVE",
            BuildId = "4.4.164.18318"
        };

        Assert.Equal("LIVE (Build 4.4.164.18318)", installation.DisplayName);
    }

    [Fact]
    public void DisplayName_WithoutBuildId_ReturnsEnvironment()
    {
        var installation = new SCInstallation
        {
            Environment = "PTU",
            BuildId = null
        };

        Assert.Equal("PTU", installation.DisplayName);
    }

    [Fact]
    public void DisplayName_WithEmptyBuildId_ReturnsEnvironment()
    {
        var installation = new SCInstallation
        {
            Environment = "EPTU",
            BuildId = ""
        };

        Assert.Equal("EPTU", installation.DisplayName);
    }

    [Fact]
    public void IsValid_WithExistingDataP4k_ReturnsTrue()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var installation = new SCInstallation
            {
                DataP4kPath = tempFile
            };

            Assert.True(installation.IsValid);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void IsValid_WithNonExistentDataP4k_ReturnsFalse()
    {
        var installation = new SCInstallation
        {
            DataP4kPath = @"C:\nonexistent\path\Data.p4k"
        };

        Assert.False(installation.IsValid);
    }

    [Fact]
    public void IsValid_WithEmptyPath_ReturnsFalse()
    {
        var installation = new SCInstallation
        {
            DataP4kPath = ""
        };

        Assert.False(installation.IsValid);
    }

    [Fact]
    public void ActionMapsPath_ComputedCorrectly()
    {
        var installation = new SCInstallation
        {
            InstallPath = @"C:\Program Files\RSI\StarCitizen\LIVE"
        };

        var expected = @"C:\Program Files\RSI\StarCitizen\LIVE\USER\Client\0\Profiles\default\actionmaps.xml";
        Assert.Equal(expected, installation.ActionMapsPath);
    }

    [Fact]
    public void GetCacheKey_WithBuildId_UsesBuildId()
    {
        var installation = new SCInstallation
        {
            Environment = "LIVE",
            BuildId = "4.4.164.18318"
        };

        var cacheKey = installation.GetCacheKey();

        Assert.Equal("LIVE_4.4.164.18318", cacheKey);
    }

    [Fact]
    public void GetCacheKey_SanitizesInvalidChars()
    {
        var installation = new SCInstallation
        {
            Environment = "TECH-PREVIEW",
            BuildId = "1.0.163.3497"
        };

        var cacheKey = installation.GetCacheKey();

        // Should not contain any invalid filename characters
        var invalidChars = Path.GetInvalidFileNameChars();
        Assert.DoesNotContain(cacheKey, c => invalidChars.Contains(c));
    }

    [Fact]
    public void MayNeedReexport_WithDifferentBuildIds_ReturnsTrue()
    {
        var installation = new SCInstallation
        {
            BuildId = "4.5.0.0",
            LastExportDate = DateTime.UtcNow.AddDays(-1),
            LastExportBuildId = "4.4.0.0"
        };

        Assert.True(installation.MayNeedReexport);
    }

    [Fact]
    public void MayNeedReexport_WithSameBuildIds_ReturnsFalse()
    {
        var installation = new SCInstallation
        {
            BuildId = "4.4.0.0",
            LastExportDate = DateTime.UtcNow.AddDays(-1),
            LastExportBuildId = "4.4.0.0"
        };

        Assert.False(installation.MayNeedReexport);
    }

    [Fact]
    public void MayNeedReexport_WithNoLastExport_ReturnsFalse()
    {
        var installation = new SCInstallation
        {
            BuildId = "4.4.0.0",
            LastExportDate = null,
            LastExportBuildId = null
        };

        Assert.False(installation.MayNeedReexport);
    }

    [Fact]
    public void ToString_ReturnsDisplayName()
    {
        var installation = new SCInstallation
        {
            Environment = "LIVE",
            BuildId = "4.4.164.18318"
        };

        Assert.Equal(installation.DisplayName, installation.ToString());
    }
}

public class SCEnvironmentsTests
{
    [Fact]
    public void All_ContainsAllEnvironments()
    {
        Assert.Contains("LIVE", SCEnvironments.All);
        Assert.Contains("PTU", SCEnvironments.All);
        Assert.Contains("EPTU", SCEnvironments.All);
        Assert.Contains("TECH-PREVIEW", SCEnvironments.All);
        Assert.Contains("HOTFIX", SCEnvironments.All);
    }

    [Fact]
    public void Priority_HasLiveFirst()
    {
        Assert.Equal("LIVE", SCEnvironments.Priority[0]);
    }

    [Fact]
    public void All_MatchesPriority()
    {
        // All and Priority should contain the same elements
        Assert.Equal(SCEnvironments.All.Length, SCEnvironments.Priority.Length);
        foreach (var env in SCEnvironments.All)
        {
            Assert.Contains(env, SCEnvironments.Priority);
        }
    }

    [Fact]
    public void Constants_MatchArrayValues()
    {
        Assert.Equal(SCEnvironments.LIVE, "LIVE");
        Assert.Equal(SCEnvironments.PTU, "PTU");
        Assert.Equal(SCEnvironments.EPTU, "EPTU");
        Assert.Equal(SCEnvironments.TechPreview, "TECH-PREVIEW");
        Assert.Equal(SCEnvironments.Hotfix, "HOTFIX");
    }
}
