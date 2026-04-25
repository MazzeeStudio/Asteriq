using System.Text.Json;
using System.Text.Json.Serialization;
using Asteriq.Models;

namespace Asteriq.Services;

/// <summary>
/// Loads Asteriq's authored binding descriptions from <c>Resources/BindingDescriptions/{locale}.json</c>
/// and exposes a per-action lookup. Fails gracefully — missing files, parse errors, schema-version
/// drift, or lookups for unknown actions all return null / empty rather than throwing.
/// </summary>
public class BindingDescriptionService
{
    /// <summary>Schema version this build understands. Files declaring a higher version are refused.</summary>
    public const int SupportedSchemaVersion = 1;

    private const string ResourceFolder = "Resources/BindingDescriptions";
    private const string ManifestFileName = "manifest.json";

    private readonly Dictionary<string, BindingDescription> _entries = new(StringComparer.Ordinal);

    /// <summary>True when a locale file was loaded successfully (even if the actions map was empty).</summary>
    public bool Loaded { get; private set; }
    public string? LoadedLocale { get; private set; }
    public int EntryCount => _entries.Count;

    /// <summary>
    /// Loads the descriptions file for <paramref name="locale"/>, falling back to the manifest's
    /// default locale (typically "en") if the requested locale's file is missing. Returns true on
    /// success; on any failure leaves the service in a not-loaded state with all lookups returning null.
    /// </summary>
    public bool Load(string? locale = null)
    {
        _entries.Clear();
        Loaded = false;
        LoadedLocale = null;

        var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ResourceFolder);
        if (!Directory.Exists(folder))
        {
            System.Diagnostics.Debug.WriteLine($"[BindingDescriptionService] Resource folder missing: {folder}");
            return false;
        }

        var manifest = TryLoadManifest(Path.Combine(folder, ManifestFileName));
        var requestedLocale = string.IsNullOrEmpty(locale) ? manifest?.DefaultLocale ?? "en" : locale;
        var fallbackLocale = manifest?.DefaultLocale ?? "en";

        // Try requested locale first; fall back to the manifest's default if missing or unparseable.
        if (TryLoadLocaleFile(folder, requestedLocale)) return true;
        if (!string.Equals(requestedLocale, fallbackLocale, StringComparison.OrdinalIgnoreCase)
            && TryLoadLocaleFile(folder, fallbackLocale))
            return true;

        return false;
    }

    /// <summary>
    /// Returns the description for <paramref name="actionName"/>, or null when the service is not
    /// loaded or the action has no entry. Callers should render a "no description yet" placeholder
    /// for null results.
    /// </summary>
    public BindingDescription? Get(string actionName)
    {
        if (!Loaded || string.IsNullOrEmpty(actionName)) return null;
        return _entries.TryGetValue(actionName, out var d) ? d : null;
    }

    /// <summary>
    /// Compares the loaded descriptions against the live action set from <paramref name="validActionNames"/>
    /// (typically extracted from <c>defaultProfile.xml</c>). Used by tooling / a future "documentation
    /// progress" surface — Missing actions need descriptions, Stale entries are dead weight that should
    /// be cleaned up after an SC schema rename.
    /// </summary>
    public AuditResult Audit(IEnumerable<string> validActionNames)
    {
        var valid = new HashSet<string>(validActionNames, StringComparer.Ordinal);
        var missing = valid.Where(a => !_entries.ContainsKey(a)).OrderBy(a => a, StringComparer.Ordinal).ToList();
        var stale = _entries.Keys.Where(k => !valid.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        int covered = valid.Count - missing.Count;
        return new AuditResult(covered, valid.Count, missing, stale);
    }

    private bool TryLoadLocaleFile(string folder, string locale)
    {
        var path = Path.Combine(folder, $"{locale}.json");
        if (!File.Exists(path))
        {
            System.Diagnostics.Debug.WriteLine($"[BindingDescriptionService] Locale file not found: {path}");
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var file = JsonSerializer.Deserialize<LocaleFile>(stream, JsonOptions);

            if (file is null)
            {
                System.Diagnostics.Debug.WriteLine($"[BindingDescriptionService] Empty or null deserialise from {path}");
                return false;
            }

            if (file.SchemaVersion > SupportedSchemaVersion)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[BindingDescriptionService] Refusing {path}: schemaVersion {file.SchemaVersion} > supported {SupportedSchemaVersion}");
                return false;
            }

            if (!string.IsNullOrEmpty(file.Locale) && !file.Locale.Equals(locale, StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[BindingDescriptionService] {path} declares locale '{file.Locale}' but filename implies '{locale}'. File field wins.");
            }

            if (file.Actions is not null)
            {
                foreach (var (key, raw) in file.Actions)
                {
                    if (raw is null || string.IsNullOrWhiteSpace(raw.Description)) continue;
                    _entries[key] = new BindingDescription
                    {
                        Description = raw.Description,
                        UseCases = (IReadOnlyList<string>?)raw.UseCases ?? Array.Empty<string>(),
                        Source = string.IsNullOrEmpty(raw.Source) ? "asteriq" : raw.Source,
                        LastEditedBy = raw.LastEditedBy,
                    };
                }
            }

            Loaded = true;
            LoadedLocale = string.IsNullOrEmpty(file.Locale) ? locale : file.Locale;
            System.Diagnostics.Debug.WriteLine(
                $"[BindingDescriptionService] Loaded {_entries.Count} description(s) from {path}");
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[BindingDescriptionService] Failed to load {path}: {ex.Message}");
            _entries.Clear();
            return false;
        }
    }

    private static Manifest? TryLoadManifest(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<Manifest>(stream, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[BindingDescriptionService] Manifest read failed: {ex.Message}");
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Result of an audit pass — coverage stats plus drift detection.</summary>
    public sealed record AuditResult(
        int CoveredCount,
        int TotalActions,
        IReadOnlyList<string> Missing,
        IReadOnlyList<string> Stale);

    private sealed class Manifest
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
        [JsonPropertyName("defaultLocale")] public string? DefaultLocale { get; set; }
        [JsonPropertyName("available")] public List<string>? Available { get; set; }
    }

    private sealed class LocaleFile
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
        [JsonPropertyName("locale")] public string? Locale { get; set; }
        [JsonPropertyName("lastUpdated")] public string? LastUpdated { get; set; }
        [JsonPropertyName("actions")] public Dictionary<string, RawEntry>? Actions { get; set; }
    }

    private sealed class RawEntry
    {
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("useCases")] public List<string>? UseCases { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
        [JsonPropertyName("lastEditedBy")] public string? LastEditedBy { get; set; }
    }
}
