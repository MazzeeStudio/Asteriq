using System.Xml;
using Asteriq.Models;
using Asteriq.Services;

namespace Asteriq.Tests.Services;

/// <summary>
/// Tests for SCProfileCacheService
/// Note: These tests use a real cache directory but with test-specific paths
/// </summary>
public class SCProfileCacheServiceTests : IDisposable
{
    private readonly string _testCacheDir;

    public SCProfileCacheServiceTests()
    {
        // Create isolated test cache directory
        _testCacheDir = Path.Combine(Path.GetTempPath(), $"AsteriqCacheTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testCacheDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testCacheDir))
        {
            try { Directory.Delete(_testCacheDir, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void SCProfileCacheService_CacheDirectory_NotEmpty()
    {
        var service = new SCProfileCacheService();

        Assert.False(string.IsNullOrEmpty(service.CacheDirectory));
        Assert.Contains("Asteriq", service.CacheDirectory);
    }

    [Fact]
    public void CacheInfo_FormattedSize_ByteFormat()
    {
        var info = new SCProfileCacheService.CacheInfo
        {
            TotalSizeBytes = 500
        };

        Assert.EndsWith("B", info.FormattedSize);
        Assert.Contains("500", info.FormattedSize);
    }

    [Fact]
    public void CacheInfo_FormattedSize_KBFormat()
    {
        var info = new SCProfileCacheService.CacheInfo
        {
            TotalSizeBytes = 2048
        };

        Assert.EndsWith("KB", info.FormattedSize);
    }

    [Fact]
    public void CacheInfo_FormattedSize_MBFormat()
    {
        var info = new SCProfileCacheService.CacheInfo
        {
            TotalSizeBytes = 2 * 1024 * 1024
        };

        Assert.EndsWith("MB", info.FormattedSize);
    }

    // Tests using file-based caching logic directly
    [Fact]
    public void CacheKey_WithBuildId_GeneratesCorrectFilename()
    {
        var installation = CreateTestInstallation("LIVE", "4.4.164.18318");

        var cacheKey = installation.GetCacheKey();

        Assert.Equal("LIVE_4.4.164.18318", cacheKey);
    }

    [Fact]
    public void CacheKey_DifferentBuilds_GenerateDifferentKeys()
    {
        var install1 = CreateTestInstallation("LIVE", "4.4.0.0");
        var install2 = CreateTestInstallation("LIVE", "4.5.0.0");

        Assert.NotEqual(install1.GetCacheKey(), install2.GetCacheKey());
    }

    [Fact]
    public void CacheKey_SameBuilds_GenerateSameKeys()
    {
        var install1 = CreateTestInstallation("LIVE", "4.4.0.0");
        var install2 = CreateTestInstallation("LIVE", "4.4.0.0");

        Assert.Equal(install1.GetCacheKey(), install2.GetCacheKey());
    }

    [Fact]
    public void SaveAndLoadXml_PreservesContent()
    {
        var cachePath = Path.Combine(_testCacheDir, "test_profile.xml");
        var originalDoc = CreateTestXmlDocument();

        // Save
        var settings = new XmlWriterSettings { Indent = true };
        using (var writer = XmlWriter.Create(cachePath, settings))
        {
            originalDoc.Save(writer);
        }

        // Load
        var loadedDoc = new XmlDocument();
        loadedDoc.Load(cachePath);

        Assert.Equal("profile", loadedDoc.DocumentElement?.Name);
        var actionmaps = loadedDoc.SelectNodes("//actionmap");
        Assert.NotNull(actionmaps);
        Assert.Equal(2, actionmaps.Count);
    }

    [Fact]
    public void SaveAndLoadXml_PreservesAttributes()
    {
        var cachePath = Path.Combine(_testCacheDir, "test_attrs.xml");
        var doc = new XmlDocument();
        doc.LoadXml("<profile version=\"1\" optionsVersion=\"2\"><actionmap name=\"test\"/></profile>");

        using (var writer = XmlWriter.Create(cachePath, new XmlWriterSettings { Indent = true }))
        {
            doc.Save(writer);
        }

        var loadedDoc = new XmlDocument();
        loadedDoc.Load(cachePath);

        Assert.Equal("1", loadedDoc.DocumentElement?.GetAttribute("version"));
        Assert.Equal("2", loadedDoc.DocumentElement?.GetAttribute("optionsVersion"));
    }

    [Fact]
    public void XmlDocument_CanSelectActionmaps()
    {
        var doc = CreateTestXmlDocument();

        var actionmaps = doc.SelectNodes("//actionmap");
        Assert.NotNull(actionmaps);
        Assert.Equal(2, actionmaps.Count);

        var firstMap = actionmaps[0];
        Assert.Equal("spaceship_movement", firstMap?.Attributes?["name"]?.Value);
    }

    [Fact]
    public void XmlDocument_CanSelectActions()
    {
        var doc = CreateTestXmlDocument();

        var actions = doc.SelectNodes("//action");
        Assert.NotNull(actions);
        Assert.Equal(3, actions.Count);
    }

    [Fact]
    public void FileOperations_CreatesDirectoryIfNeeded()
    {
        var subDir = Path.Combine(_testCacheDir, "subdir");
        var filePath = Path.Combine(subDir, "test.xml");

        Directory.CreateDirectory(subDir);
        File.WriteAllText(filePath, "<test/>");

        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public void FileOperations_DeleteRemovesFile()
    {
        var filePath = Path.Combine(_testCacheDir, "to_delete.xml");
        File.WriteAllText(filePath, "<test/>");

        Assert.True(File.Exists(filePath));

        File.Delete(filePath);

        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public void FileOperations_CountXmlFiles()
    {
        File.WriteAllText(Path.Combine(_testCacheDir, "profile1.xml"), "<test/>");
        File.WriteAllText(Path.Combine(_testCacheDir, "profile2.xml"), "<test/>");
        File.WriteAllText(Path.Combine(_testCacheDir, "other.txt"), "not xml");

        var xmlFiles = Directory.GetFiles(_testCacheDir, "*.xml");

        Assert.Equal(2, xmlFiles.Length);
    }

    private static SCInstallation CreateTestInstallation(string environment, string buildId)
    {
        return new SCInstallation
        {
            Environment = environment,
            BuildId = buildId,
            InstallPath = $@"C:\Test\StarCitizen\{environment}",
            DataP4kPath = $@"C:\Test\StarCitizen\{environment}\Data.p4k",
            MappingsPath = $@"C:\Test\StarCitizen\{environment}\USER\Client\0\Controls\Mappings"
        };
    }

    private static XmlDocument CreateTestXmlDocument()
    {
        var doc = new XmlDocument();
        doc.LoadXml(@"
            <profile version=""1"" optionsVersion=""2"">
                <actionmap name=""spaceship_movement"">
                    <action name=""v_strafe_forward"">
                        <rebind input=""js1_y""/>
                    </action>
                    <action name=""v_strafe_back""/>
                </actionmap>
                <actionmap name=""spaceship_weapons"">
                    <action name=""v_attack1""/>
                </actionmap>
            </profile>");
        return doc;
    }
}
