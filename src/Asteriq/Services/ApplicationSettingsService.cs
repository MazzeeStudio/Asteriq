using System.Text.Json;
using System.Text.Json.Serialization;
using Asteriq.Models;
using Asteriq.Services.Abstractions;
using Asteriq.UI;
using Microsoft.Extensions.Logging;

namespace Asteriq.Services;

/// <summary>
/// Manages application-level settings
/// </summary>
public class ApplicationSettingsService : IApplicationSettingsService
{
    private readonly ILogger<ApplicationSettingsService> _logger;
    private readonly string _settingsFile;
    private readonly JsonSerializerOptions _jsonOptions;
    private AppSettings _cachedSettings;

    public event EventHandler<FontSizeOption>? FontSizeChanged;

    public ApplicationSettingsService(ILogger<ApplicationSettingsService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var asteriqDir = Path.Combine(appData, "Asteriq");
        _settingsFile = Path.Combine(asteriqDir, "appsettings.json");

        Directory.CreateDirectory(asteriqDir);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        // Load settings once and cache in memory
        _cachedSettings = LoadSettings();
        _logger.LogDebug("ApplicationSettingsService initialized. Settings loaded from {SettingsFile}", _settingsFile);
    }

    public Guid? LastProfileId
    {
        get => _cachedSettings.LastProfileId;
        set
        {
            _cachedSettings.LastProfileId = value;
            SaveSettings(_cachedSettings);
        }
    }

    public bool AutoLoadLastProfile
    {
        get => _cachedSettings.AutoLoadLastProfile;
        set
        {
            _cachedSettings.AutoLoadLastProfile = value;
            SaveSettings(_cachedSettings);
        }
    }

    public FontSizeOption FontSize
    {
        get => _cachedSettings.FontSize;
        set
        {
            _cachedSettings.FontSize = value;
            SaveSettings(_cachedSettings);
            FontSizeChanged?.Invoke(this, value);
        }
    }

    public UIFontFamily FontFamily
    {
        get => _cachedSettings.FontFamily;
        set
        {
            _cachedSettings.FontFamily = value;
            SaveSettings(_cachedSettings);
        }
    }

    public bool CloseToTray
    {
        get => _cachedSettings.CloseToTray;
        set
        {
            _cachedSettings.CloseToTray = value;
            SaveSettings(_cachedSettings);
        }
    }

    public TrayIconType TrayIconType
    {
        get => _cachedSettings.TrayIconType;
        set
        {
            _cachedSettings.TrayIconType = value;
            SaveSettings(_cachedSettings);
        }
    }

    public string? LastSCExportProfile
    {
        get => _cachedSettings.LastSCExportProfile;
        set
        {
            _cachedSettings.LastSCExportProfile = value;
            SaveSettings(_cachedSettings);
        }
    }

    public bool AutoLoadLastSCExportProfile
    {
        get => _cachedSettings.AutoLoadLastSCExportProfile;
        set
        {
            _cachedSettings.AutoLoadLastSCExportProfile = value;
            SaveSettings(_cachedSettings);
        }
    }

    public string? PreferredSCEnvironment
    {
        get => _cachedSettings.PreferredSCEnvironment;
        set
        {
            _cachedSettings.PreferredSCEnvironment = value;
            SaveSettings(_cachedSettings);
        }
    }

    private AppSettings LoadSettings()
    {
        if (!File.Exists(_settingsFile))
        {
            _logger.LogDebug("Settings file not found, using defaults");
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(_settingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to load settings from {SettingsFile}, using defaults", _settingsFile);
            return new AppSettings();
        }
    }

    private void SaveSettings(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsFile);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(_settingsFile, json);
    }

    private class AppSettings
    {
        public Guid? LastProfileId { get; set; }
        public bool AutoLoadLastProfile { get; set; } = true;
        public FontSizeOption FontSize { get; set; } = FontSizeOption.Medium;
        public UIFontFamily FontFamily { get; set; } = UIFontFamily.Carbon;
        public bool CloseToTray { get; set; }
        public TrayIconType TrayIconType { get; set; } = TrayIconType.Throttle;
        public string? LastSCExportProfile { get; set; }
        public bool AutoLoadLastSCExportProfile { get; set; } = true;
        public string? PreferredSCEnvironment { get; set; }
    }
}
