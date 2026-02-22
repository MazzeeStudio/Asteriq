using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI;

/// <summary>
/// Shared state, services, and callbacks available to all tab controllers.
/// </summary>
public class TabContext
{
    // Services (readonly)
    public IInputService InputService { get; }
    public IProfileManager ProfileManager { get; }
    public IProfileRepository ProfileRepository { get; }
    public IApplicationSettingsService AppSettings { get; }
    public IUIThemeService ThemeService { get; }
    public IVJoyService VJoyService { get; }
    public IMappingEngine MappingEngine { get; }
    public SystemTrayIcon TrayIcon { get; }

    // Shared mutable state
    public List<PhysicalDeviceInfo> Devices { get; set; } = new();
    public List<PhysicalDeviceInfo> DisconnectedDevices { get; set; } = new();
    public int SelectedDevice { get; set; } = -1;
    public DeviceInputState? CurrentInputState { get; set; }
    public List<VJoyDeviceInfo> VJoyDevices { get; set; } = new();
    public int SelectedVJoyDeviceIndex { get; set; }
    public DeviceMap? DeviceMap { get; set; }
    public DeviceMap? MappingsPrimaryDeviceMap { get; set; }
    public ActiveInputTracker ActiveInputTracker { get; }
    public bool IsForwarding { get; set; }
    public FUIBackground Background { get; }
    public bool BackgroundDirty { get; set; } = true;
    public Point MousePosition { get; set; }
    public float LeadLineProgress { get; set; }
    public float PulsePhase { get; set; }
    public float DashPhase { get; set; }
    public SKSvg? JoystickSvg { get; set; }
    public SKSvg? ThrottleSvg { get; set; }
    public Form OwnerForm { get; }

    // SVG interaction state (shared between Devices and Mappings tabs)
    public string? HoveredControlId { get; set; }
    public string? SelectedControlId { get; set; }
    public SKRect SilhouetteBounds { get; set; }
    public float SvgScale { get; set; } = 1f;
    public SKPoint SvgOffset { get; set; }
    public bool SvgMirrored { get; set; }
    public Dictionary<string, SKRect> ControlBounds { get; set; } = new();

    // Profile UI state (shared, dropdown drawn in MainForm)
    public List<ProfileInfo> Profiles { get; set; } = new();

    // Callbacks (core - set via constructor)
    public Action MarkDirty { get; }
    public Action InvalidateCanvas { get; }
    public Action RefreshDevices { get; }
    public Action RefreshProfileList { get; }
    public Action<PhysicalDeviceInfo?> LoadDeviceMapForDevice { get; }
    public Action UpdateMappingsPrimaryDeviceMap { get; }
    public Func<SKPoint, string?> HitTestSvg { get; }
    public Action OnMappingsChanged { get; }

    // Callbacks (extended - set after construction for cross-tab operations)
    public Action? CreateOneToOneMappings { get; set; }
    public Action? ClearDeviceMappings { get; set; }
    public Action? RemoveDisconnectedDevice { get; set; }
    public Action<string>? OpenMappingDialogForControl { get; set; }
    public Action? CreateNewProfilePrompt { get; set; }
    public Action? SaveDisconnectedDevices { get; set; }
    public Action? SaveDeviceOrder { get; set; }
    public Action? SelectFirstDeviceInCategory { get; set; }
    public Action? UpdateTrayMenu { get; set; }
    public Func<SKSvg?>? GetActiveSvg { get; set; }
    public Func<DeviceMap?, SKSvg?>? GetSvgForDeviceMap { get; set; }

    public TabContext(
        IInputService inputService,
        IProfileManager profileManager,
        IProfileRepository profileRepository,
        IApplicationSettingsService appSettings,
        IUIThemeService themeService,
        IVJoyService vjoyService,
        IMappingEngine mappingEngine,
        SystemTrayIcon trayIcon,
        ActiveInputTracker activeInputTracker,
        FUIBackground background,
        Form ownerForm,
        Action markDirty,
        Action invalidateCanvas,
        Action refreshDevices,
        Action refreshProfileList,
        Action<PhysicalDeviceInfo?> loadDeviceMapForDevice,
        Action updateMappingsPrimaryDeviceMap,
        Func<SKPoint, string?> hitTestSvg,
        Action onMappingsChanged)
    {
        InputService = inputService;
        ProfileManager = profileManager;
        ProfileRepository = profileRepository;
        AppSettings = appSettings;
        ThemeService = themeService;
        VJoyService = vjoyService;
        MappingEngine = mappingEngine;
        TrayIcon = trayIcon;
        ActiveInputTracker = activeInputTracker;
        Background = background;
        OwnerForm = ownerForm;
        MarkDirty = markDirty;
        InvalidateCanvas = invalidateCanvas;
        RefreshDevices = refreshDevices;
        RefreshProfileList = refreshProfileList;
        LoadDeviceMapForDevice = loadDeviceMapForDevice;
        UpdateMappingsPrimaryDeviceMap = updateMappingsPrimaryDeviceMap;
        HitTestSvg = hitTestSvg;
        OnMappingsChanged = onMappingsChanged;
    }
}
