using Asteriq.Models;
using Asteriq.Services.Abstractions;
using Asteriq.UI;

namespace Asteriq.Services;

/// <summary>
/// Adapter that implements IProfileService by delegating to the new focused services.
/// This maintains backward compatibility while using the refactored architecture.
/// </summary>
[Obsolete("Use specific services (IProfileRepository, IProfileManager, IApplicationSettingsService, etc.) instead")]
public class ProfileServiceAdapter : IProfileService
{
    private readonly IProfileRepository _repository;
    private readonly IProfileManager _manager;
    private readonly IApplicationSettingsService _appSettings;
    private readonly IUIThemeService _themeService;
    private readonly IWindowStateManager _windowState;

    public ProfileServiceAdapter(
        IProfileRepository repository,
        IProfileManager manager,
        IApplicationSettingsService appSettings,
        IUIThemeService themeService,
        IWindowStateManager windowState)
    {
        _repository = repository;
        _manager = manager;
        _appSettings = appSettings;
        _themeService = themeService;
        _windowState = windowState;
    }

    // ProfileChanged event - delegate to manager
    public event EventHandler<ProfileChangedEventArgs>? ProfileChanged
    {
        add => _manager.ProfileChanged += value;
        remove => _manager.ProfileChanged -= value;
    }

    // Font size changed - delegate to app settings
    public event EventHandler<FontSizeOption>? FontSizeChanged
    {
        add => _appSettings.FontSizeChanged += value;
        remove => _appSettings.FontSizeChanged -= value;
    }

    // Theme changed - delegate to theme service
    public event EventHandler<FUITheme>? ThemeChanged
    {
        add => _themeService.ThemeChanged += value;
        remove => _themeService.ThemeChanged -= value;
    }

    // Profile management - delegate to manager
    public MappingProfile? ActiveProfile => _manager.ActiveProfile;
    public bool HasActiveProfile => _manager.HasActiveProfile;
    public bool ActivateProfile(Guid profileId) => _manager.ActivateProfile(profileId);
    public void DeactivateProfile() => _manager.DeactivateProfile();
    public void SaveActiveProfile() => _manager.SaveActiveProfile();
    public MappingProfile CreateAndActivateProfile(string name, string description = "") => _manager.CreateAndActivateProfile(name, description);
    public void Initialize() => _manager.Initialize();

    // Profile repository - delegate to repository
    public string ProfilesDirectory => _repository.ProfilesDirectory;
    public void SaveProfile(MappingProfile profile) => _repository.SaveProfile(profile);
    public MappingProfile? LoadProfile(Guid profileId) => _repository.LoadProfile(profileId);
    public MappingProfile? LoadProfileFromPath(string filePath) => _repository.LoadProfileFromPath(filePath);
    public bool DeleteProfile(Guid profileId) => _repository.DeleteProfile(profileId);
    public List<ProfileInfo> ListProfiles() => _repository.ListProfiles();
    public MappingProfile? DuplicateProfile(Guid sourceId, string newName) => _repository.DuplicateProfile(sourceId, newName);
    public bool ExportProfile(Guid profileId, string exportPath) => _repository.ExportProfile(profileId, exportPath);
    public MappingProfile? ImportProfile(string importPath, bool generateNewId = true) => _repository.ImportProfile(importPath, generateNewId);
    public MappingProfile CreateProfile(string name, string description = "") => _repository.CreateProfile(name, description);
    public MappingProfile? LoadLastProfileIfEnabled() => null; // Handled by Initialize

    // Application settings - delegate to app settings
    public Guid? LastProfileId
    {
        get => _appSettings.LastProfileId;
        set => _appSettings.LastProfileId = value;
    }
    public bool AutoLoadLastProfile
    {
        get => _appSettings.AutoLoadLastProfile;
        set => _appSettings.AutoLoadLastProfile = value;
    }
    public FontSizeOption FontSize
    {
        get => _appSettings.FontSize;
        set => _appSettings.FontSize = value;
    }
    public UIFontFamily FontFamily
    {
        get => _appSettings.FontFamily;
        set => _appSettings.FontFamily = value;
    }
    public bool CloseToTray
    {
        get => _appSettings.CloseToTray;
        set => _appSettings.CloseToTray = value;
    }
    public TrayIconType TrayIconType
    {
        get => _appSettings.TrayIconType;
        set => _appSettings.TrayIconType = value;
    }
    public string? LastSCExportProfile
    {
        get => _appSettings.LastSCExportProfile;
        set => _appSettings.LastSCExportProfile = value;
    }
    public bool AutoLoadLastSCExportProfile
    {
        get => _appSettings.AutoLoadLastSCExportProfile;
        set => _appSettings.AutoLoadLastSCExportProfile = value;
    }

    // Theme - delegate to theme service
    public FUITheme Theme
    {
        get => _themeService.Theme;
        set => _themeService.Theme = value;
    }
    public (int gridStrength, int glowIntensity, int noiseIntensity, int scanlineIntensity, int vignetteStrength) LoadBackgroundSettings()
        => _themeService.LoadBackgroundSettings();
    public void SaveBackgroundSettings(int gridStrength, int glowIntensity, int noiseIntensity, int scanlineIntensity, int vignetteStrength)
        => _themeService.SaveBackgroundSettings(gridStrength, glowIntensity, noiseIntensity, scanlineIntensity, vignetteStrength);

    // Window state - delegate to window state manager
    public (int width, int height, int x, int y) LoadWindowState() => _windowState.LoadWindowState();
    public void SaveWindowState(int width, int height, int x, int y) => _windowState.SaveWindowState(width, height, x, y);
}
