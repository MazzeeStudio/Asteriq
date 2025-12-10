using System.Xml;
using Asteriq.Models;

namespace Asteriq.Services;

/// <summary>
/// Caches extracted defaultProfile.xml files per Star Citizen version.
/// This avoids re-extracting from Data.p4k on every startup.
/// </summary>
public class SCProfileCacheService
{
    private readonly string _cacheDirectory;

    public SCProfileCacheService()
    {
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Asteriq",
            "cache",
            "sc_profiles");

        // Ensure cache directory exists
        Directory.CreateDirectory(_cacheDirectory);
    }

    /// <summary>
    /// Gets the cache directory path
    /// </summary>
    public string CacheDirectory => _cacheDirectory;

    /// <summary>
    /// Gets cached default profile XML for an installation, if available
    /// </summary>
    public XmlDocument? GetCachedProfile(SCInstallation installation)
    {
        var cacheKey = installation.GetCacheKey();
        var cachePath = GetCachePath(cacheKey);

        if (!File.Exists(cachePath))
        {
            System.Diagnostics.Debug.WriteLine($"[SCProfileCache] No cached profile found for {installation.Environment} (key: {cacheKey})");
            return null;
        }

        try
        {
            var doc = new XmlDocument();
            doc.Load(cachePath);

            System.Diagnostics.Debug.WriteLine($"[SCProfileCache] Loaded cached profile for {installation.Environment} (key: {cacheKey})");
            return doc;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SCProfileCache] Failed to load cached profile for {installation.Environment}: {ex.Message}");

            // Delete corrupted cache file
            try { File.Delete(cachePath); } catch { }
            return null;
        }
    }

    /// <summary>
    /// Saves a default profile XML to the cache
    /// </summary>
    public void CacheProfile(SCInstallation installation, XmlDocument profile)
    {
        var cacheKey = installation.GetCacheKey();
        var cachePath = GetCachePath(cacheKey);

        try
        {
            // Save with proper formatting
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = Environment.NewLine,
                NewLineHandling = NewLineHandling.Replace
            };

            using var writer = XmlWriter.Create(cachePath, settings);
            profile.Save(writer);

            System.Diagnostics.Debug.WriteLine($"[SCProfileCache] Cached profile for {installation.Environment} (key: {cacheKey})");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SCProfileCache] Failed to cache profile for {installation.Environment}: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if a cached profile exists for an installation
    /// </summary>
    public bool HasCachedProfile(SCInstallation installation)
    {
        var cacheKey = installation.GetCacheKey();
        return File.Exists(GetCachePath(cacheKey));
    }

    /// <summary>
    /// Gets a default profile for an installation, either from cache or by extracting from p4k.
    /// Returns null if extraction fails.
    /// </summary>
    public XmlDocument? GetOrExtractProfile(SCInstallation installation, Action<string>? progressCallback = null)
    {
        // Try cache first
        var cached = GetCachedProfile(installation);
        if (cached != null)
        {
            progressCallback?.Invoke($"Loaded cached profile for {installation.Environment}");
            return cached;
        }

        // Need to extract from p4k
        if (!installation.IsValid)
        {
            System.Diagnostics.Debug.WriteLine($"[SCProfileCache] Cannot extract profile - installation {installation.Environment} is invalid");
            return null;
        }

        progressCallback?.Invoke($"Opening Data.p4k for {installation.Environment}...");

        using var extractor = new P4kExtractorService(installation.DataP4kPath);
        if (!extractor.Open())
        {
            System.Diagnostics.Debug.WriteLine($"[SCProfileCache] Failed to open p4k for {installation.Environment}");
            return null;
        }

        progressCallback?.Invoke($"Searching for defaultProfile.xml ({extractor.EntryCount:N0} entries)...");

        var profile = extractor.ExtractDefaultProfile();
        if (profile == null)
        {
            System.Diagnostics.Debug.WriteLine($"[SCProfileCache] Failed to extract profile from p4k for {installation.Environment}");
            return null;
        }

        // Cache the extracted profile
        progressCallback?.Invoke("Caching extracted profile...");
        CacheProfile(installation, profile);

        progressCallback?.Invoke($"Successfully extracted profile for {installation.Environment}");
        return profile;
    }

    /// <summary>
    /// Clears all cached profiles
    /// </summary>
    public void ClearCache()
    {
        try
        {
            if (Directory.Exists(_cacheDirectory))
            {
                foreach (var file in Directory.GetFiles(_cacheDirectory, "*.xml"))
                {
                    File.Delete(file);
                }
                System.Diagnostics.Debug.WriteLine("[SCProfileCache] Cleared profile cache");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SCProfileCache] Failed to clear profile cache: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears cached profile for a specific installation
    /// </summary>
    public void ClearCacheFor(SCInstallation installation)
    {
        var cacheKey = installation.GetCacheKey();
        var cachePath = GetCachePath(cacheKey);

        try
        {
            if (File.Exists(cachePath))
            {
                File.Delete(cachePath);
                System.Diagnostics.Debug.WriteLine($"[SCProfileCache] Cleared cached profile for {installation.Environment}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SCProfileCache] Failed to clear cached profile: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets information about the cache
    /// </summary>
    public CacheInfo GetCacheInfo()
    {
        var info = new CacheInfo { CacheDirectory = _cacheDirectory };

        try
        {
            if (Directory.Exists(_cacheDirectory))
            {
                var files = Directory.GetFiles(_cacheDirectory, "*.xml");
                info.CachedProfileCount = files.Length;
                info.TotalSizeBytes = files.Sum(f => new FileInfo(f).Length);

                // Get list of cached environments
                info.CachedEnvironments = files
                    .Select(f => Path.GetFileNameWithoutExtension(f).Replace("_defaultProfile", ""))
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SCProfileCache] Failed to get cache info: {ex.Message}");
        }

        return info;
    }

    private string GetCachePath(string cacheKey)
    {
        return Path.Combine(_cacheDirectory, $"{cacheKey}_defaultProfile.xml");
    }

    /// <summary>
    /// Information about the cache state
    /// </summary>
    public class CacheInfo
    {
        public int CachedProfileCount { get; set; }
        public long TotalSizeBytes { get; set; }
        public string CacheDirectory { get; set; } = string.Empty;
        public List<string> CachedEnvironments { get; set; } = new();

        public string FormattedSize => TotalSizeBytes switch
        {
            < 1024 => $"{TotalSizeBytes} B",
            < 1024 * 1024 => $"{TotalSizeBytes / 1024.0:F1} KB",
            _ => $"{TotalSizeBytes / (1024.0 * 1024.0):F1} MB"
        };
    }
}
