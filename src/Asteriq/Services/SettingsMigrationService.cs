using System.Text.Json;
using System.Text.Json.Serialization;
using Asteriq.Models;
using Asteriq.Services.Abstractions;
using Asteriq.UI;
using Microsoft.Extensions.Logging;

namespace Asteriq.Services;

/// <summary>
/// Migrates settings from the old unified settings.json to the new split files
/// </summary>
public class SettingsMigrationService
{
    private readonly ILogger<SettingsMigrationService> _logger;
    private readonly IApplicationSettingsService _appSettings;
    private readonly IUIThemeService _themeService;
    private readonly IWindowStateManager _windowState;
    private readonly string _oldSettingsFile;
    private readonly string _migrationMarkerFile;

    public SettingsMigrationService(
        ILogger<SettingsMigrationService> logger,
        IApplicationSettingsService appSettings,
        IUIThemeService themeService,
        IWindowStateManager windowState)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _appSettings = appSettings;
        _themeService = themeService;
        _windowState = windowState;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var asteriqDir = Path.Combine(appData, "Asteriq");
        _oldSettingsFile = Path.Combine(asteriqDir, "settings.json");
        _migrationMarkerFile = Path.Combine(asteriqDir, ".settings_migrated");
    }

    /// <summary>
    /// Check if migration is needed and perform it
    /// </summary>
    public bool MigrateIfNeeded()
    {
        // If already migrated, skip
        if (File.Exists(_migrationMarkerFile))
            return false;

        // If old settings file doesn't exist, mark as migrated (fresh install)
        if (!File.Exists(_oldSettingsFile))
        {
            File.WriteAllText(_migrationMarkerFile, DateTime.UtcNow.ToString("O"));
            return false;
        }

        try
        {
            _logger.LogInformation("Migrating settings from old settings.json...");

            // Read old settings
            var json = File.ReadAllText(_oldSettingsFile);
            var oldSettings = JsonSerializer.Deserialize<OldAppSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            });

            if (oldSettings is null)
            {
                _logger.LogWarning("Failed to parse old settings");
                return false;
            }

            // Migrate to new services
            _logger.LogInformation("Migrating application settings...");
            _appSettings.LastProfileId = oldSettings.LastProfileId;
            _appSettings.AutoLoadLastProfile = oldSettings.AutoLoadLastProfile;
            #pragma warning disable CS0618 // FontSizeOption is obsolete
            _appSettings.FontSize = oldSettings.FontSize switch
            {
                FontSizeOption.VSmall => 1.0f,
                FontSizeOption.Small => 1.2f,
                FontSizeOption.Medium => 1.3f,
                FontSizeOption.Large => 1.4f,
                FontSizeOption.XLarge => 1.6f,
                _ => 1.0f
            };
            #pragma warning restore CS0618
            _appSettings.FontFamily = oldSettings.FontFamily;
            _appSettings.CloseToTray = oldSettings.CloseToTray;
            _appSettings.TrayIconType = oldSettings.TrayIconType;
            _appSettings.LastSCExportProfile = oldSettings.LastSCExportProfile;
            _appSettings.AutoLoadLastSCExportProfile = oldSettings.AutoLoadLastSCExportProfile;

            _logger.LogInformation("Migrating theme settings...");
            _themeService.Theme = oldSettings.Theme;
            _themeService.SaveBackgroundSettings(
                oldSettings.GridStrength,
                oldSettings.GlowIntensity,
                oldSettings.NoiseIntensity,
                oldSettings.ScanlineIntensity,
                oldSettings.VignetteStrength);

            _logger.LogInformation("Migrating window state...");
            _windowState.SaveWindowState(
                oldSettings.WindowWidth,
                oldSettings.WindowHeight,
                oldSettings.WindowX,
                oldSettings.WindowY);

            // Backup old settings file
            var backupFile = _oldSettingsFile + ".backup";
            File.Copy(_oldSettingsFile, backupFile, overwrite: true);
            _logger.LogInformation("Backed up old settings to {BackupFile}", backupFile);

            // Mark as migrated
            File.WriteAllText(_migrationMarkerFile, DateTime.UtcNow.ToString("O"));
            _logger.LogInformation("Migration completed successfully!");

            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Settings migration failed - marking as migrated to prevent infinite retry");
            try { File.WriteAllText(_migrationMarkerFile, DateTime.UtcNow.ToString("O")); }
            catch (Exception markerEx) when (markerEx is IOException or UnauthorizedAccessException) { _logger.LogWarning(markerEx, "Failed to write migration marker"); }
            return false;
        }
    }

    /// <summary>
    /// Old unified settings structure
    /// </summary>
    private class OldAppSettings
    {
        public Guid? LastProfileId { get; set; }
        public bool AutoLoadLastProfile { get; set; } = true;
        #pragma warning disable CS0618 // FontSizeOption is obsolete — needed for reading legacy settings
        public FontSizeOption FontSize { get; set; } = FontSizeOption.Medium;
        #pragma warning restore CS0618
        public UIFontFamily FontFamily { get; set; } = UIFontFamily.Carbon;
        public bool CloseToTray { get; set; }
        public TrayIconType TrayIconType { get; set; } = TrayIconType.Throttle;
        public string? LastSCExportProfile { get; set; }
        public bool AutoLoadLastSCExportProfile { get; set; } = true;
        public FUITheme Theme { get; set; } = FUITheme.Midnight;
        public int GridStrength { get; set; } = 30;
        public int GlowIntensity { get; set; } = 80;
        public int NoiseIntensity { get; set; } = 15;
        public int ScanlineIntensity { get; set; } = 20;
        public int VignetteStrength { get; set; } = 40;
        public int WindowWidth { get; set; } = 1280;
        public int WindowHeight { get; set; } = 800;
        public int WindowX { get; set; } = 100;
        public int WindowY { get; set; } = 100;
    }
}
