using System.Text;

namespace Asteriq.Services;

/// <summary>
/// Loads SC's <c>global.ini</c> localisation file and exposes a simple key→value lookup.
/// Used to hydrate human-readable labels and descriptions onto <c>SCAction</c> records
/// so the UI can show "Cycle Master Mode (Long Press)" instead of <c>v_master_mode_cycle_long</c>.
/// </summary>
public class SCLocalisationService
{
    private const char BomChar = '﻿';

    private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);

    public bool Loaded { get; private set; }
    public string? LoadedLocale { get; private set; }
    public int StringCount => _strings.Count;

    /// <summary>
    /// Loads <c>&lt;install&gt;/data/Localization/&lt;locale&gt;/global.ini</c>.
    /// Returns true on success. Missing files are treated as a clean "no localisation
    /// available" state — callers should fall back to mechanical name formatting.
    /// </summary>
    public bool Load(string installPath, string locale = "english")
    {
        _strings.Clear();
        Loaded = false;
        LoadedLocale = null;

        if (string.IsNullOrEmpty(installPath)) return false;
        var path = Path.Combine(installPath, "data", "Localization", locale, "global.ini");
        if (!File.Exists(path)) return false;

        try
        {
            // global.ini is UTF-16 LE with a BOM. Encoding.Unicode handles both.
            foreach (var rawLine in File.ReadLines(path, Encoding.Unicode))
            {
                var line = rawLine;
                if (string.IsNullOrEmpty(line)) continue;
                if (line[0] == BomChar) line = line[1..]; // defensive: strip stray BOM codepoint
                if (line.Length == 0 || line[0] == ';' || line[0] == '[') continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line[..eq].Trim();
                var value = line[(eq + 1)..];

                // Some keys carry a ",P"-style suffix (plural/format variant) e.g.
                // "ui_v_master_mode_cycle,P=...". Strip the suffix so the first entry wins.
                int comma = key.IndexOf(',');
                if (comma > 0) key = key[..comma];
                if (key.Length == 0) continue;

                if (!_strings.ContainsKey(key))
                    _strings[key] = value;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[SCLocalisationService] Failed to load {path}: {ex.Message}");
            return false;
        }

        Loaded = _strings.Count > 0;
        LoadedLocale = locale;
        System.Diagnostics.Debug.WriteLine($"[SCLocalisationService] Loaded {_strings.Count} strings from {path}");
        return Loaded;
    }

    /// <summary>
    /// Looks up a localised string. Returns null when the key is absent or the service
    /// has not been loaded.
    /// </summary>
    public string? Get(string key)
    {
        return _strings.TryGetValue(key, out var value) ? value : null;
    }
}
