using System.Text.Json;
using Asteriq.Models;
using Asteriq.Services.Abstractions;
using Asteriq.UI;
using Microsoft.Extensions.Logging;

namespace Asteriq.Services;

/// <summary>
/// Manages UI theme and visual settings
/// </summary>
public class UIThemeService : IUIThemeService
{
    private readonly ILogger<UIThemeService> _logger;
    private readonly string _settingsFile;
    private readonly JsonSerializerOptions _jsonOptions;
    private ThemeSettings _cachedSettings;

    public event EventHandler<FUITheme>? ThemeChanged;

    public UIThemeService(ILogger<UIThemeService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var asteriqDir = Path.Combine(appData, "Asteriq");
        _settingsFile = Path.Combine(asteriqDir, "theme.json");

        Directory.CreateDirectory(asteriqDir);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Load and cache settings in memory
        _cachedSettings = LoadSettings();
        _logger.LogDebug("UIThemeService initialized. Theme: {Theme}", _cachedSettings.Theme);
    }

    public FUITheme Theme
    {
        get => _cachedSettings.Theme;
        set
        {
            _cachedSettings.Theme = value;
            SaveSettings(_cachedSettings);
            ThemeChanged?.Invoke(this, value);
        }
    }

    public (int gridStrength, int glowIntensity, int noiseIntensity, int scanlineIntensity, int vignetteStrength) LoadBackgroundSettings()
    {
        return (_cachedSettings.GridStrength, _cachedSettings.GlowIntensity, _cachedSettings.NoiseIntensity,
                _cachedSettings.ScanlineIntensity, _cachedSettings.VignetteStrength);
    }

    public void SaveBackgroundSettings(int gridStrength, int glowIntensity, int noiseIntensity, int scanlineIntensity, int vignetteStrength)
    {
        _cachedSettings.GridStrength = gridStrength;
        _cachedSettings.GlowIntensity = glowIntensity;
        _cachedSettings.NoiseIntensity = noiseIntensity;
        _cachedSettings.ScanlineIntensity = scanlineIntensity;
        _cachedSettings.VignetteStrength = vignetteStrength;
        SaveSettings(_cachedSettings);
    }

    private ThemeSettings LoadSettings()
    {
        if (!File.Exists(_settingsFile))
        {
            _logger.LogDebug("Theme settings file not found, using defaults");
            return new ThemeSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsFile);
            return JsonSerializer.Deserialize<ThemeSettings>(json, _jsonOptions) ?? new ThemeSettings();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load theme settings from {SettingsFile}, using defaults", _settingsFile);
            return new ThemeSettings();
        }
    }

    private void SaveSettings(ThemeSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsFile);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(_settingsFile, json);
    }

    private class ThemeSettings
    {
        public FUITheme Theme { get; set; } = FUITheme.Midnight;
        public int GridStrength { get; set; } = 30;
        public int GlowIntensity { get; set; } = 80;
        public int NoiseIntensity { get; set; } = 15;
        public int ScanlineIntensity { get; set; } = 20;
        public int VignetteStrength { get; set; } = 40;
    }
}
