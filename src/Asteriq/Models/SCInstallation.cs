namespace Asteriq.Models;

/// <summary>
/// Represents a detected Star Citizen installation
/// </summary>
public class SCInstallation
{
    /// <summary>
    /// Environment/version name (LIVE, PTU, EPTU, TECH-PREVIEW, etc.)
    /// </summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>
    /// Build ID from build_manifest.id (e.g., "9557671")
    /// Changes with each game patch - used for cache invalidation and schema change detection
    /// </summary>
    public string? BuildId { get; set; }

    /// <summary>
    /// Root installation path (e.g., C:\Program Files\Roberts Space Industries\StarCitizen\LIVE)
    /// </summary>
    public string InstallPath { get; set; } = string.Empty;

    /// <summary>
    /// Path to Data.p4k archive
    /// </summary>
    public string DataP4kPath { get; set; } = string.Empty;

    /// <summary>
    /// Path to user mappings folder for exported profiles
    /// (USER/Client/0/Controls/Mappings)
    /// </summary>
    public string MappingsPath { get; set; } = string.Empty;

    /// <summary>
    /// Path to user's current actionmaps.xml
    /// (USER/Client/0/Profiles/default/actionmaps.xml)
    /// </summary>
    public string ActionMapsPath => Path.Combine(InstallPath, "USER", "Client", "0", "Profiles", "default", "actionmaps.xml");

    /// <summary>
    /// Path to defaultProfile.xml if found on filesystem (rare, usually in p4k)
    /// </summary>
    public string? DefaultProfilePath { get; set; }

    /// <summary>
    /// Size of Data.p4k in bytes (for cache key generation)
    /// </summary>
    public long DataP4kSize { get; set; }

    /// <summary>
    /// Last modified time of Data.p4k (for cache key generation)
    /// </summary>
    public DateTime DataP4kLastModified { get; set; }

    /// <summary>
    /// When we last exported a profile to this installation
    /// </summary>
    public DateTime? LastExportDate { get; set; }

    /// <summary>
    /// BuildId at the time of last export (for detecting if re-export needed)
    /// </summary>
    public string? LastExportBuildId { get; set; }

    /// <summary>
    /// Display name for UI (includes BuildId if available)
    /// </summary>
    public string DisplayName => string.IsNullOrEmpty(BuildId)
        ? Environment
        : $"{Environment} (Build {BuildId})";

    /// <summary>
    /// Whether this installation appears valid (has Data.p4k)
    /// </summary>
    public bool IsValid => !string.IsNullOrEmpty(DataP4kPath) && File.Exists(DataP4kPath);

    /// <summary>
    /// Whether a re-export might be needed (BuildId changed since last export)
    /// </summary>
    public bool MayNeedReexport =>
        LastExportDate.HasValue &&
        !string.IsNullOrEmpty(LastExportBuildId) &&
        !string.IsNullOrEmpty(BuildId) &&
        LastExportBuildId != BuildId;

    /// <summary>
    /// Generates a cache key for this installation based on environment and build
    /// </summary>
    public string GetCacheKey()
    {
        if (!string.IsNullOrEmpty(BuildId))
        {
            return SanitizeFileName($"{Environment}_{BuildId}");
        }

        // Fallback: use p4k metadata
        var hashInput = $"{Environment}_{DataP4kSize}_{DataP4kLastModified.Ticks}";
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(hashInput));
        var hashString = Convert.ToHexString(hashBytes)[..16];
        return SanitizeFileName($"{Environment}_{hashString}");
    }

    private static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var result = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            result.Append(invalidChars.Contains(c) ? '_' : c);
        }
        return result.ToString();
    }

    public override string ToString() => DisplayName;
}

/// <summary>
/// Known Star Citizen environment folder names
/// </summary>
public static class SCEnvironments
{
    public const string LIVE = "LIVE";
    public const string PTU = "PTU";
    public const string EPTU = "EPTU";
    public const string TechPreview = "TECH-PREVIEW";
    public const string Hotfix = "HOTFIX";

    /// <summary>
    /// All known environment names in priority order
    /// </summary>
    public static readonly string[] All = { LIVE, PTU, EPTU, TechPreview, Hotfix };

    /// <summary>
    /// Priority order for selecting default installation
    /// </summary>
    public static readonly string[] Priority = { LIVE, PTU, EPTU, TechPreview, Hotfix };
}
