using System.Diagnostics;
using Asteriq.Models;
using Asteriq.Services.Abstractions;

namespace Asteriq.Services;

/// <summary>
/// Detects and manages Star Citizen installations.
/// Scans for SC installations (LIVE, PTU, EPTU) and provides paths to key files.
/// </summary>
public class SCInstallationService : ISCInstallationService
{
    private readonly List<SCInstallation> _installations = new();
    private string? _customInstallPath;

    /// <summary>
    /// Optional custom installation path configured by user
    /// </summary>
    public string? CustomInstallPath
    {
        get => _customInstallPath;
        set
        {
            _customInstallPath = value;
            // Clear cache when custom path changes
            _installations.Clear();
        }
    }

    /// <summary>
    /// Gets all detected SC installations (cached after first call)
    /// </summary>
    public IReadOnlyList<SCInstallation> Installations
    {
        get
        {
            if (_installations.Count == 0)
            {
                DetectInstallations();
            }
            return _installations;
        }
    }

    /// <summary>
    /// Force re-detection of installations
    /// </summary>
    public void Refresh()
    {
        _installations.Clear();
        DetectInstallations();
    }

    /// <summary>
    /// Check if Star Citizen is currently running
    /// </summary>
    public static bool IsStarCitizenRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("StarCitizen");
            return processes.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the preferred installation (LIVE first, then PTU, etc.)
    /// </summary>
    public SCInstallation? GetPreferredInstallation()
    {
        var installations = Installations;
        if (installations.Count == 0)
            return null;

        // Return first match in priority order
        foreach (var env in SCEnvironments.Priority)
        {
            var match = installations.FirstOrDefault(i =>
                i.Environment.Equals(env, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        // Fallback to first found
        return installations.First();
    }

    /// <summary>
    /// Gets installation by environment name
    /// </summary>
    public SCInstallation? GetInstallation(string environment)
    {
        return Installations.FirstOrDefault(i =>
            i.Environment.Equals(environment, StringComparison.OrdinalIgnoreCase));
    }

    private void DetectInstallations()
    {
        _installations.Clear();

        // First check custom path if configured
        if (!string.IsNullOrEmpty(_customInstallPath) && Directory.Exists(_customInstallPath))
        {
            CheckCustomPath(_customInstallPath);
        }

        // Then do auto-detection
        var searchRoots = GetSearchRoots();
        foreach (var root in searchRoots)
        {
            var rsiPath = Path.Combine(root, "Roberts Space Industries", "StarCitizen");
            if (Directory.Exists(rsiPath))
            {
                CheckForVersionsInPath(rsiPath);
            }
        }

        System.Diagnostics.Debug.WriteLine($"[SCInstallationService] Found {_installations.Count} SC installation(s)");
        foreach (var inst in _installations)
        {
            System.Diagnostics.Debug.WriteLine($"  - {inst.DisplayName} at {inst.InstallPath}");
        }
    }

    private void CheckCustomPath(string customPath)
    {
        // The custom path could be:
        // 1. A specific version folder (e.g., C:\Games\StarCitizen\LIVE) - contains Data.p4k
        // 2. The StarCitizen folder (containing LIVE, PTU, etc.)
        // 3. The Roberts Space Industries folder

        // Check if it's a version folder directly (contains Data.p4k)
        if (File.Exists(Path.Combine(customPath, "Data.p4k")))
        {
            var envName = Path.GetFileName(customPath).ToUpperInvariant();
            if (!SCEnvironments.All.Contains(envName))
                envName = "CUSTOM";

            var installation = ValidateInstallation(customPath, envName);
            if (installation is not null)
            {
                _installations.Add(installation);
            }
        }
        else
        {
            // Check if it contains version subfolders
            CheckForVersionsInPath(customPath);

            // Also check if it's Roberts Space Industries folder
            var scPath = Path.Combine(customPath, "StarCitizen");
            if (Directory.Exists(scPath))
            {
                CheckForVersionsInPath(scPath);
            }
        }
    }

    private void CheckForVersionsInPath(string basePath)
    {
        foreach (var env in SCEnvironments.All)
        {
            var versionPath = Path.Combine(basePath, env);
            if (!Directory.Exists(versionPath))
                continue;

            // Skip if we already have this installation
            if (_installations.Any(i => i.InstallPath.Equals(versionPath, StringComparison.OrdinalIgnoreCase)))
                continue;

            var installation = ValidateInstallation(versionPath, env);
            if (installation is not null)
            {
                _installations.Add(installation);
            }
        }
    }

    private SCInstallation? ValidateInstallation(string path, string environment)
    {
        // Check for Data.p4k to verify it's a valid installation
        var dataP4kPath = Path.Combine(path, "Data.p4k");
        if (!File.Exists(dataP4kPath))
        {
            return null;
        }

        var p4kInfo = new FileInfo(dataP4kPath);

        var installation = new SCInstallation
        {
            Environment = environment,
            InstallPath = path,
            DataP4kPath = dataP4kPath,
            DataP4kSize = p4kInfo.Length,
            DataP4kLastModified = p4kInfo.LastWriteTimeUtc,
            MappingsPath = GetMappingsPath(path),
            DefaultProfilePath = GetDefaultProfilePath(path)
        };

        // Try to get build ID from manifest
        installation.BuildId = ReadBuildId(path);

        return installation;
    }

    /// <summary>
    /// Reads the BuildId from build_manifest.id file
    /// Format is JSON: {"Data": {"Version": "4.4.164.18318", "BuildId": "...", ...}}
    /// We extract Version as the primary identifier (e.g., "4.4.164.18318")
    /// </summary>
    private static string? ReadBuildId(string installPath)
    {
        var manifestPath = Path.Combine(installPath, "build_manifest.id");
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            var json = File.ReadAllText(manifestPath).Trim();

            // Parse the Version field from JSON
            // Format: {"Data": {"Version": "4.4.164.18318", ...}}
            var versionMatch = System.Text.RegularExpressions.Regex.Match(
                json,
                @"""Version""\s*:\s*""([^""]+)""");

            if (versionMatch.Success)
            {
                return versionMatch.Groups[1].Value;
            }

            // Fallback: try to get RequestedP4ChangeNum (unique per build)
            var changeNumMatch = System.Text.RegularExpressions.Regex.Match(
                json,
                @"""RequestedP4ChangeNum""\s*:\s*""([^""]+)""");

            if (changeNumMatch.Success)
            {
                return changeNumMatch.Groups[1].Value;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the path to defaultProfile.xml if it exists on filesystem (rare)
    /// </summary>
    private static string? GetDefaultProfilePath(string installPath)
    {
        // Usually inside Data.p4k, but might be extracted on dev builds
        var defaultProfilePath = Path.Combine(installPath, "Data", "Libs", "Config", "defaultProfile.xml");
        return File.Exists(defaultProfilePath) ? defaultProfilePath : null;
    }

    /// <summary>
    /// Gets the user mappings path for exported profiles
    /// </summary>
    private static string GetMappingsPath(string installPath)
    {
        // Modern path structure (3.18+)
        var modernPath = Path.Combine(installPath, "USER", "Client", "0", "Controls", "Mappings");

        // Check if parent exists (Mappings folder might not exist yet)
        var controlsPath = Path.GetDirectoryName(modernPath);
        if (controlsPath is not null && Directory.Exists(controlsPath))
            return modernPath;

        // Try older path structure
        var olderPath = Path.Combine(installPath, "USER", "Controls", "Mappings");
        controlsPath = Path.GetDirectoryName(olderPath);
        if (controlsPath is not null && Directory.Exists(controlsPath))
            return olderPath;

        // Default to modern path (will be created on export)
        return modernPath;
    }

    /// <summary>
    /// Get search roots for auto-detection
    /// </summary>
    private static List<string> GetSearchRoots()
    {
        var roots = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        // Add all fixed drive roots
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType == DriveType.Fixed && drive.IsReady)
            {
                roots.Add(drive.RootDirectory.FullName);
                roots.Add(Path.Combine(drive.RootDirectory.FullName, "Games"));
                roots.Add(Path.Combine(drive.RootDirectory.FullName, "Program Files"));
            }
        }

        return roots.Distinct().Where(Directory.Exists).ToList();
    }

    /// <summary>
    /// Ensures the mappings directory exists for an installation
    /// </summary>
    public static bool EnsureMappingsDirectory(SCInstallation installation)
    {
        try
        {
            if (!Directory.Exists(installation.MappingsPath))
            {
                Directory.CreateDirectory(installation.MappingsPath);
            }
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SCInstallationService] Failed to create mappings directory at " +
                                               $"'{installation.MappingsPath}'. Error type: {ex.GetType().Name}, Details: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets a list of existing XML profile files in the mappings folder
    /// </summary>
    public static List<SCMappingFile> GetExistingProfiles(SCInstallation installation)
    {
        var profiles = new List<SCMappingFile>();

        try
        {
            if (!Directory.Exists(installation.MappingsPath))
                return profiles;

            foreach (var file in Directory.GetFiles(installation.MappingsPath, "*.xml"))
            {
                var fileInfo = new FileInfo(file);
                var mappingFile = new SCMappingFile
                {
                    FileName = fileInfo.Name,
                    FilePath = file,
                    FileSize = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTime
                };

                // Try to read profile name from inside the XML
                try
                {
                    using var reader = new StreamReader(file);
                    // Read just enough to find the profileName attribute (first ~2KB)
                    var buffer = new char[2048];
                    int read = reader.Read(buffer, 0, buffer.Length);
                    var content = new string(buffer, 0, read);

                    // Look for profileName="..." in the content
                    var match = System.Text.RegularExpressions.Regex.Match(content, @"profileName=""([^""]+)""");
                    if (match.Success)
                    {
                        mappingFile.ProfileName = match.Groups[1].Value;
                    }

                    // Also check CustomisationUIHeader label
                    if (string.IsNullOrEmpty(mappingFile.ProfileName))
                    {
                        match = System.Text.RegularExpressions.Regex.Match(content, @"<CustomisationUIHeader\s+label=""([^""]+)""");
                        if (match.Success)
                        {
                            mappingFile.ProfileName = match.Groups[1].Value;
                        }
                    }
                }
                catch
                {
                    // Ignore errors reading profile name - will fall back to filename
                }

                profiles.Add(mappingFile);
            }

            // Sort by last modified (newest first)
            profiles.Sort((a, b) => b.LastModified.CompareTo(a.LastModified));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SCInstallationService] Error listing profiles: {ex.Message}");
        }

        return profiles;
    }
}

/// <summary>
/// Represents an existing SC mapping file that can be imported
/// </summary>
public class SCMappingFile
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime LastModified { get; set; }

    /// <summary>
    /// Profile name extracted from inside the XML file
    /// </summary>
    public string? ProfileName { get; set; }

    /// <summary>
    /// Display name - uses profile name from XML if available, otherwise filename
    /// </summary>
    public string DisplayName => !string.IsNullOrEmpty(ProfileName)
        ? ProfileName
        : Path.GetFileNameWithoutExtension(FileName);
}
