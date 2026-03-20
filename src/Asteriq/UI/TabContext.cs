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
    public IUpdateService UpdateService { get; }
    public DriverSetupManager DriverSetupManager { get; }

    // Shared mutable state — see SyncTabContext() / SyncFromTabContext() in MainForm.cs
    // for the authoritative sync contract between MainForm and TabContext.

    // OWNER: MainForm (SDL2 thread) — must remain local field for thread safety
    public List<PhysicalDeviceInfo> Devices { get; set; } = new();
    // OWNER: MainForm — pushed via SyncTabContext
    public List<PhysicalDeviceInfo> DisconnectedDevices { get; set; } = new();
    // OWNER: TabContext — synced back via SyncFromTabContext (also read on SDL2 thread)
    public int SelectedDevice { get; set; } = -1;
    // OWNER: TabContext — synced back via SyncFromTabContext (also written on SDL2 thread)
    public DeviceInputState? CurrentInputState { get; set; }
    // OWNER: MainForm — pushed via SyncTabContext
    public List<VJoyDeviceInfo> VJoyDevices { get; set; } = new();
    // OWNER: TabContext — synced back via SyncFromTabContext
    public int SelectedVJoyDeviceIndex { get; set; }
    // OWNER: TabContext — synced back via SyncFromTabContext (also read on SDL2 thread)
    public DeviceMap? DeviceMap { get; set; }
    // OWNER: MainForm — pushed via SyncTabContext (not synced back; see comment in SyncFromTabContext)
    public DeviceMap? MappingsPrimaryDeviceMap { get; set; }
    public ActiveInputTracker ActiveInputTracker { get; }
    // OWNER: TabContext — synced back via SyncFromTabContext (also read on SDL2 thread)
    public bool IsForwarding { get; set; }
    public FUIBackground Background { get; }
    // OWNER: TabContext — synced back via SyncFromTabContext
    public bool BackgroundDirty { get; set; } = true;
    // OWNER: MainForm — pushed via SyncTabContext
    public Point MousePosition { get; set; }
    // OWNER: MainForm — pushed via SyncTabContext
    public float LeadLineProgress { get; set; }
    // OWNER: MainForm — pushed via SyncTabContext
    public float PulsePhase { get; set; }
    // OWNER: MainForm — pushed via SyncTabContext
    public float DashPhase { get; set; }
    // OWNER: MainForm — pushed via SyncTabContext
    public SKSvg? JoystickSvg { get; set; }
    // OWNER: MainForm — pushed via SyncTabContext
    public SKSvg? ThrottleSvg { get; set; }
    public Form OwnerForm { get; }

    // SVG interaction state (shared between Devices and Mappings tabs)
    // OWNER: TabContext — synced back via SyncFromTabContext
    public string? HoveredControlId { get; set; }
    // OWNER: TabContext — synced back via SyncFromTabContext
    public string? SelectedControlId { get; set; }
    // OWNER: TabContext — synced back via SyncFromTabContext
    public SKRect SilhouetteBounds { get; set; }
    // OWNER: TabContext — synced back via SyncFromTabContext
    public float SvgScale { get; set; } = 1f;
    // OWNER: TabContext — synced back via SyncFromTabContext
    public SKPoint SvgOffset { get; set; }
    // OWNER: TabContext — synced back via SyncFromTabContext
    public bool SvgMirrored { get; set; }
    /// <summary>Source image width in viewBox units — used by ViewBoxToScreen for mirror math. Set by DrawSvgInBounds / DrawBitmapInBounds.</summary>
    public float SilhouetteSourceWidth { get; set; } = 2048f;
    // OWNER: MainForm — pushed via SyncTabContext
    public Dictionary<string, SKRect> ControlBounds { get; set; } = new();

    // Profile UI state (shared, dropdown drawn in MainForm)
    // OWNER: MainForm — pushed via SyncTabContext
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

    /// <summary>
    /// All device maps available for silhouette selection.
    /// Key = filename without extension (e.g. "joystick", "throttle", "virpil_alpha_prime_r").
    /// DisplayName = the human-readable device name from the map (e.g. "VPC WarBRD").
    /// Populated once at startup by MainForm; empty until then.
    /// </summary>
    public List<(string Key, string DisplayName)> AvailableDeviceMaps { get; set; } = new();

    // Callbacks (extended - set after construction for cross-tab operations)
    public Action? CreateOneToOneMappings { get; set; }
    public Action? ClearDeviceMappings { get; set; }
    public Action? RemoveDisconnectedDevice { get; set; }
    public Action<string>? OpenMappingDialogForControl { get; set; }
    public Action? CreateNewProfilePrompt { get; set; }
    public Action? DuplicateActiveProfile { get; set; }
    public Action? ImportProfile { get; set; }
    public Action? ExportActiveProfile { get; set; }
    public Action? DeleteActiveProfile { get; set; }
    public Action? SaveDisconnectedDevices { get; set; }
    public Action? SaveDeviceOrder { get; set; }
    public Action? SelectFirstDeviceInCategory { get; set; }
    public Action? UpdateTrayMenu { get; set; }
    public Action? ApplyFontScale { get; set; }
    public Func<SKSvg?>? GetActiveSvg { get; set; }
    public Func<DeviceMap?, SKSvg?>? GetSvgForDeviceMap { get; set; }
    public Func<SKBitmap?>? GetActiveBitmap { get; set; }
    public Func<DeviceMap?, SKBitmap?>? GetBitmapForDeviceMap { get; set; }
    public Action? OpenDriverSetup { get; set; }
    public Action? RefreshVJoyDevices { get; set; }

    /// <summary>
    /// Returns the active SC export profile from the Keybindings tab.
    /// Used by the Mappings tab to read/write Device Order (VJoyToSCInstance).
    /// </summary>
    public Func<SCExportProfile?>? GetActiveSCExportProfile { get; set; }

    // Network forwarding (set after construction by MainForm)
    public INetworkDiscoveryService? NetworkDiscovery { get; set; }
    public INetworkInputService? NetworkInput { get; set; }
    // OWNER: MainForm (SDL2 thread) — must remain local field for thread safety
    /// <summary>Current network forwarding mode — updated by MainForm on every sync.</summary>
    public NetworkInputMode NetworkMode { get; set; } = NetworkInputMode.Local;
    // OWNER: MainForm — pushed via SyncTabContext
    /// <summary>True while a master-side connect handshake is in progress. CONNECT button is disabled.</summary>
    public bool IsNetworkConnecting { get; set; }
    /// <summary>
    /// When true, the network forwarding heartbeat skips sending vJoy snapshots to the remote machine.
    /// Set by SCBindingsTabController while button-capture mode is active so that deliberate button
    /// presses used only for search are not forwarded.
    /// </summary>
    public bool SuppressForwarding { get; set; }
    /// <summary>
    /// Clears button/hat states from all forwarding snapshots so that buttons held during a capture
    /// session are not forwarded when suppression ends. Set by MainForm; no-op when not forwarding.
    /// </summary>
    public Action? ClearForwardingSnapshots { get; set; }
    /// <summary>IP address of the RX peer currently connected as TX master. Null when not connected.</summary>
    public string? ConnectedPeerIp { get; set; }
    // OWNER: MainForm — pushed via SyncTabContext
    /// <summary>True when this machine is in client mode (receiving vJoy from master). Tabs 0 and 1 are locked.</summary>
    public bool IsClientConnected { get; set; }
    public Action? StartNetworking { get; set; }
    public Action? ShutdownNetworking { get; set; }
    /// <summary>
    /// Connect to a specific peer as master (starts MappingEngine if needed, sets forwarding mode).
    /// Called from the Settings tab CONNECT button. Returns a task that completes when connected.
    /// </summary>
    public Func<NetworkPeer, Task>? ConnectToPeerAsync { get; set; }
    /// <summary>
    /// Disconnect from the current network peer and resume local mode.
    /// Called from the Settings tab DISCONNECT button.
    /// </summary>
    public Func<Task>? NetworkDisconnectAsync { get; set; }
    /// <summary>
    /// Send all saved SC control profiles to the connected Rx client.
    /// Set by SCBindingsTabController; called by MainForm after master connect succeeds.
    /// </summary>
    public Action? SendProfileListToClient { get; set; }
    /// <summary>
    /// SC control profiles received from the connected TX master.
    /// Shown as a special section in the SC Bindings profile dropdown on the RX machine.
    /// </summary>
    public List<(string Name, byte[] XmlBytes)> RemoteControlProfiles { get; set; } = new();
    /// <summary>Machine name of the TX master that sent RemoteControlProfiles.</summary>
    public string RemoteControlProfilesMasterName { get; set; } = "";
    /// <summary>
    /// Called when the NET SWITCH button assignment changes.
    /// SCBindingsTabController sets this to its CheckNetworkSwitchConflicts method.
    /// </summary>
    public Action? CheckNetworkSwitchConflicts { get; set; }

    // HidHide device hiding (set after construction by MainForm)
    public IHidHideService? HidHide { get; set; }
    public DeviceMatchingService? DeviceMatching { get; set; }
    public ISCInstallationService? SCInstallation { get; set; }
    /// <summary>Toggle HidHide driver hiding for a specific physical device. Set by DevicesTabController.</summary>
    public Action<PhysicalDeviceInfo>? ToggleHidHideForDevice { get; set; }

    public TabContext(
        IInputService inputService,
        IProfileManager profileManager,
        IProfileRepository profileRepository,
        IApplicationSettingsService appSettings,
        IUIThemeService themeService,
        IVJoyService vjoyService,
        IMappingEngine mappingEngine,
        SystemTrayIcon trayIcon,
        IUpdateService updateService,
        DriverSetupManager driverSetupManager,
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
        UpdateService = updateService;
        DriverSetupManager = driverSetupManager;
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
