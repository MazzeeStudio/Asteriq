using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using Asteriq.UI.Controllers;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Svg.Skia;

namespace Asteriq.UI;

/// <summary>
/// Main application window with custom FUI chrome.
/// Borderless window with custom title bar and window controls.
/// Layout matches the FUIExplorer mockup design.
/// </summary>
public partial class MainForm : Form
{
    // Win32 constants for borderless window support
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int WM_SIZE = 0x0005;
    private const int WM_NCCALCSIZE = 0x0083;
    private const int WM_ENTERSIZEMOVE = 0x0231;
    private const int WM_EXITSIZEMOVE = 0x0232;
    private const int SC_MAXIMIZE = 0xF030;
    private const int SC_RESTORE = 0xF120;
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    // SetWindowPos flags
    private const uint SWP_NOCOPYBITS = 0x0100;  // Discards entire window content (prevents ghost window)
    private const uint SWP_FRAMECHANGED = 0x0020;  // Recalculates frame
    private const uint SWP_NOZORDER = 0x0004;     // Retains current Z order

    // DWM (Desktop Window Manager) for Windows 11 border color
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_BORDER_COLOR = 34;  // Windows 11+ only

    // Window sizing — ResizeBorder is measured from the visible window edge inward.
    // FramePadding accounts for the invisible frame that Windows adds around Sizable windows
    // (WM_NCCALCSIZE returning 0 makes this invisible frame part of the client area).
    private static readonly int FramePadding = SystemInformation.FrameBorderSize.Width
                                             + SystemInformation.BorderSize.Width;
    private const int ResizeBorder = 6;
    private const int TitleBarHeight = 75;

    // Manual maximize state (we handle maximize ourselves for better control)
    private bool _isManuallyMaximized = false;
    private Rectangle _restoreBounds;  // Bounds to restore to when unmaximizing

    // Version from assembly (set at build time via MSBuild)
    private static readonly string s_appVersion = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    // Services
    private readonly IInputService _inputService;
    private readonly IProfileManager _profileManager;
    private readonly IProfileRepository _profileRepository;
    private readonly IApplicationSettingsService _appSettings;
    private readonly IUIThemeService _themeService;
    private readonly IWindowStateManager _windowState;
    private readonly IVJoyService _vjoyService;
    private readonly IMappingEngine _mappingEngine;
    private readonly SystemTrayIcon _trayIcon;
    private readonly IUpdateService _updateService;
    private readonly DriverSetupManager _driverSetupManager;
    private readonly DirectInput.DirectInputService? _directInputService;
    private readonly INetworkDiscoveryService _networkDiscovery;
    private readonly INetworkInputService _networkInput;
    private readonly ILogger<MainForm> _logger;
    private readonly IHidHideService _hidHideService;
    private readonly DeviceMatchingService _deviceMatching;

    // Tab controllers
    private SettingsTabController _settingsController = null!;
    private DevicesTabController _devicesController = null!;
    private SCBindingsTabController _scBindingsController = null!;
    private MappingsTabController _mappingsController = null!;
    private TabContext _tabContext = null!;

    // Profile UI state
    private List<ProfileInfo> _profiles = new();
    private bool _profileDropdownOpen;
    private int _hoveredProfileIndex = -1;
    private SKRect _profileSelectorBounds;
    private SKRect _profileDropdownBounds;

    // UI State
    // CA2213: SKControl is a WinForms child control — disposed automatically via Controls collection
#pragma warning disable CA2213
    private SKControl _canvas = null!;
#pragma warning restore CA2213
    private System.Windows.Forms.Timer _renderTimer = null!;
    private FUIBackground _background = new();
    private float _scanLineProgress = 0f;
    private float _dashPhase = 0f;
    private float _pulsePhase = 0f;
    private float _leadLineProgress = 0f;

    // Custom form icon (tracked separately so we never accidentally Dispose the shared WinForms DefaultIcon)
    private Icon? _customFormIcon;

    // Performance optimization
    private bool _isDirty = true;  // Force initial render
    private bool _enableAnimations = true;  // Can be toggled for performance
    private bool _isResizing = false;  // Suppress renders during resize
    private int _unfocusedFrameCount;  // Tracks ticks for background frame-rate throttle

    // Phase 2: Render caching
    private SKBitmap? _cachedBackground;  // Cached background layer
    private bool _backgroundDirty = true;  // Background needs redraw

    private int _selectedDevice = -1;  // Start with no selection, will be set in RefreshDevices
    private List<PhysicalDeviceInfo> _devices = new();
    private List<PhysicalDeviceInfo> _disconnectedDevices = new(); // Devices that were seen but are now disconnected
    private DeviceInputState? _currentInputState;

    // (Mapping tab fields moved to MappingsTabController)

    // Tab state
    private int _activeTab = 0;
    private readonly string[] _tabNames = { "DEVICES", "MAPPINGS", "KEYBINDINGS", "SETTINGS" };
    private float _tabsStartX; // cached each draw pass, used by HitTest

    // Window control hover state
    private int _hoveredWindowControl = -1;

    // Title bar logo
    private SKSvg? _logoSvg;

    // SVG device silhouettes (fallback generics)
    private SKSvg? _joystickSvg;
    private SKSvg? _throttleSvg;

    // Per-device image caches (loaded on first use)
    private readonly Dictionary<string, SKSvg> _svgCache = new();
    private readonly Dictionary<string, SKBitmap> _bitmapCache = new();

    // SVG interaction state
    private string? _hoveredControlId;
    private string? _selectedControlId;
    private SKRect _silhouetteBounds;
    private float _svgScale = 1f;
    private SKPoint _svgOffset;
    private bool _svgMirrored;
    private Dictionary<string, SKRect> _controlBounds = new();
    private Point _mousePosition; // For debug display

    // Device mapping and active input tracking
    private DeviceMap? _deviceMap;
    private DeviceMap? _mappingsPrimaryDeviceMap; // Device map for Mappings tab based on vJoy primary device
    private readonly ActiveInputTracker _activeInputTracker = new();

    // Shared vJoy state (used by SyncTabContext, UpdateMappingsPrimaryDeviceMap)
    private int _selectedVJoyDeviceIndex = 0;
    private List<VJoyDeviceInfo> _vjoyDevices = new();

    // (Mappings tab UI fields moved to MappingsTabController)
    // (Settings tab fields moved to SettingsTabController)
    // (SC Bindings tab fields moved to SCBindingsTabController)

    // (Network forwarding state fields moved to MainForm.Networking.cs)

    /// <summary>
    /// Constructor with dependency injection
    /// </summary>
    public MainForm(
        IInputService inputService,
        IProfileManager profileManager,
        IProfileRepository profileRepository,
        IApplicationSettingsService appSettings,
        IUIThemeService themeService,
        IWindowStateManager windowState,
        IVJoyService vjoyService,
        IMappingEngine mappingEngine,
        SystemTrayIcon trayIcon,
        IUpdateService updateService,
        DriverSetupManager driverSetupManager,
        ISCInstallationService scInstallationService,
        SCProfileCacheService scProfileCacheService,
        SCSchemaService scSchemaService,
        SCXmlExportService scExportService,
        SCExportProfileService scExportProfileService,
        ILogger<MainForm> logger,
        IHidHideService hidHideService,
        DeviceMatchingService deviceMatchingService,
        DirectInput.DirectInputService? directInputService = null,
        INetworkDiscoveryService? networkDiscovery = null,
        INetworkInputService? networkInput = null)
    {
        // Assign injected services
        _inputService = inputService ?? throw new ArgumentNullException(nameof(inputService));
        _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
        _profileRepository = profileRepository ?? throw new ArgumentNullException(nameof(profileRepository));
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _windowState = windowState ?? throw new ArgumentNullException(nameof(windowState));
        _vjoyService = vjoyService ?? throw new ArgumentNullException(nameof(vjoyService));
        _networkVjoy = vjoyService as NetworkVJoyService
            ?? throw new InvalidOperationException(
                "IVJoyService must be a NetworkVJoyService. Check ServiceConfiguration.");
        _mappingEngine = mappingEngine ?? throw new ArgumentNullException(nameof(mappingEngine));
        _trayIcon = trayIcon ?? throw new ArgumentNullException(nameof(trayIcon));
        _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
        _driverSetupManager = driverSetupManager ?? throw new ArgumentNullException(nameof(driverSetupManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hidHideService = hidHideService ?? throw new ArgumentNullException(nameof(hidHideService));
        _deviceMatching = deviceMatchingService ?? throw new ArgumentNullException(nameof(deviceMatchingService));
        _directInputService = directInputService; // nullable — unavailable in tests or headless environments
        _networkDiscovery = networkDiscovery ?? new NullNetworkDiscoveryService();
        _networkInput = networkInput ?? new NullNetworkInputService();
        // Update tray icon tooltip
        _trayIcon.SetToolTip($"Asteriq v{s_appVersion}");

        InitializeTrayMenu();
        InitializeForm();
        InitializeCanvas();
        InitializeInput();
        InitializeVJoy();
        InitializeRenderLoop();
        LoadSvgAssets();
        InitializeProfiles();
        InitializeTabControllers(
            scInstallationService ?? throw new ArgumentNullException(nameof(scInstallationService)),
            scProfileCacheService ?? throw new ArgumentNullException(nameof(scProfileCacheService)),
            scSchemaService ?? throw new ArgumentNullException(nameof(scSchemaService)),
            scExportService ?? throw new ArgumentNullException(nameof(scExportService)),
            scExportProfileService ?? throw new ArgumentNullException(nameof(scExportProfileService)),
            _directInputService);

        // Apply correct MinimumSize now that font settings are loaded
        ApplyFontScaleToWindowSize();

        // Snap to a visible tab on startup (e.g. client-only mode hides Devices/Mappings)
        SnapToValidTab();

        // If we landed on the SC Bindings tab at startup, trigger OnActivated once the
        // form handle exists (BeginInvoke inside StartSchemaLoad needs a valid HWND).
        if (_activeTab == 2)
            Shown += (_, _) => _scBindingsController.OnActivated();

        // Start network services if enabled in settings
        if (_appSettings.NetworkEnabled)
            InitializeNetworking();

        // Apply startup preferences once the form is visible
        Shown += OnStartupPreferencesApply;

        // Silent background update check — only if user has enabled auto-check
        if (_appSettings.AutoCheckUpdates)
            _ = _updateService.CheckAsync().ContinueWith(
                _ => _canvas?.Invoke(() => _canvas.Invalidate()),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }

    private void OnStartupPreferencesApply(object? sender, EventArgs e)
    {
        Shown -= OnStartupPreferencesApply;

        if (_appSettings.AutoStartForwarding)
            StartForwarding();

        if (_appSettings.OpenMinimized && _appSettings.CloseToTray)
            Hide();
    }

    private void InitializeTabControllers(
        ISCInstallationService scInstallationService,
        SCProfileCacheService scProfileCacheService,
        SCSchemaService scSchemaService,
        SCXmlExportService scExportService,
        SCExportProfileService scExportProfileService,
        DirectInput.DirectInputService? directInputService = null)
    {
        _tabContext = new TabContext(
            _inputService, _profileManager, _profileRepository, _appSettings,
            _themeService, _vjoyService, _mappingEngine, _trayIcon, _updateService,
            _driverSetupManager, _activeInputTracker, _background, this,
            markDirty: MarkDirty,
            invalidateCanvas: () => _canvas.Invalidate(),
            refreshDevices: RefreshDevices,
            refreshProfileList: RefreshProfileList,
            loadDeviceMapForDevice: LoadDeviceMapForDevice,
            updateMappingsPrimaryDeviceMap: UpdateMappingsPrimaryDeviceMap,
            hitTestSvg: HitTestSvg,
            onMappingsChanged: OnMappingsChanged);

        // Sync initial state
        _tabContext.Devices = _devices;
        _tabContext.DisconnectedDevices = _disconnectedDevices;
        _tabContext.SelectedDevice = _selectedDevice;
        _tabContext.VJoyDevices = _vjoyDevices;
        _tabContext.DeviceMap = _deviceMap;
        _tabContext.JoystickSvg = _joystickSvg;
        _tabContext.ThrottleSvg = _throttleSvg;
        _tabContext.ControlBounds = _controlBounds;
        _tabContext.IsForwarding = _isForwarding;
        _tabContext.AvailableDeviceMaps = LoadAvailableDeviceMaps();

        // Wire up extended callbacks for cross-tab operations (non-mapping callbacks)
        _tabContext.CreateNewProfilePrompt = CreateNewProfilePrompt;
        _tabContext.DuplicateActiveProfile = DuplicateActiveProfile;
        _tabContext.ImportProfile = ImportProfilePrompt;
        _tabContext.ExportActiveProfile = ExportActiveProfile;
        _tabContext.DeleteActiveProfile = DeleteActiveProfile;
        _tabContext.SaveDisconnectedDevices = SaveDisconnectedDevices;
        _tabContext.SaveDeviceOrder = SaveDeviceOrder;
        _tabContext.SelectFirstDeviceInCategory = SelectFirstDeviceInCategory;
        _tabContext.UpdateTrayMenu = UpdateTrayMenu;
        _tabContext.ApplyFontScale = ApplyFontScaleToWindowSize;
        _tabContext.GetActiveSvg = GetActiveSvg;
        _tabContext.GetSvgForDeviceMap = GetSvgForDeviceMap;
        _tabContext.GetActiveBitmap = GetActiveBitmap;
        _tabContext.GetBitmapForDeviceMap = GetBitmapForDeviceMap;
        _tabContext.OpenDriverSetup = OpenDriverSetupDialog;
        _tabContext.RefreshVJoyDevices = RefreshVJoyDevicesInternal;
        _tabContext.HidHide = _hidHideService;
        _tabContext.DeviceMatching = _deviceMatching;
        _tabContext.SCInstallation = scInstallationService;
        scInstallationService.CustomSearchPaths = _appSettings.CustomSCSearchPaths;

        // Network forwarding
        _tabContext.NetworkDiscovery = _networkDiscovery;
        _tabContext.NetworkInput = _networkInput;
        _tabContext.StartNetworking = InitializeNetworking;
        _tabContext.ShutdownNetworking = ShutdownNetworking;
        _tabContext.ConnectToPeerAsync = ConnectAsMasterAsync;
        _tabContext.NetworkDisconnectAsync = SwitchToLocalAsync;

        _settingsController = new SettingsTabController(_tabContext);
        _devicesController = new DevicesTabController(_tabContext);
        _mappingsController = new MappingsTabController(_tabContext, scExportProfileService);
        _scBindingsController = new SCBindingsTabController(
            _tabContext, scInstallationService, scProfileCacheService,
            scSchemaService, scExportService, scExportProfileService,
            directInputService);
        _scBindingsController.Initialize();

        // Wire up mapping-related callbacks (now delegated to MappingsTabController)
        _tabContext.CreateOneToOneMappings = _mappingsController.CreateOneToOneMappingsPublic;
        _tabContext.ClearDeviceMappings = _mappingsController.ClearDeviceMappingsPublic;
        _tabContext.RemoveDisconnectedDevice = _mappingsController.RemoveDisconnectedDevicePublic;
        _tabContext.OpenMappingDialogForControl = _mappingsController.OpenMappingDialogForControlPublic;

        // Wire up HidHide toggle (Settings panel calls into DevicesTabController logic)
        _tabContext.ToggleHidHideForDevice = _devicesController.ToggleHidHideForDevicePublic;

        // Wire up network conflict check (delegated to SCBindingsTabController)
        _tabContext.CheckNetworkSwitchConflicts = _scBindingsController.CheckNetworkSwitchConflictsPublic;

        // Wire up forwarding snapshot clear (for button capture mode)
        _tabContext.ClearForwardingSnapshots = _networkVjoy.ClearAllSnapshotButtons;

        // Wire up SC export profile access (for Device Order in Mappings tab)
        _tabContext.GetActiveSCExportProfile = () => _scBindingsController.ActiveSCExportProfile;

        // Wire up Mappings → SC Bindings deep-link (used when a Mappings row is shared-away).
        // Mirror what a normal tab click does in OnCanvasMouseDown — deactivate the source
        // tab and activate the destination — otherwise SC's schema load / state init never
        // fires and the tab renders empty.
        _tabContext.OpenSCBindingsWithSearch = (vjoyDevice, inputName) =>
        {
            if (_activeTab != 2)
            {
                if (_activeTab == 1) _mappingsController.OnDeactivated();
                _scBindingsController.OnActivated();
                _activeTab = 2;
            }
            _scBindingsController.SetButtonCaptureSearch(vjoyDevice, inputName);
            _tabContext.InvalidateCanvas();
        };
    }

    /// <summary>
    /// SyncTabContext pushes MainForm-owned state into TabContext so controllers
    /// can read it during Draw/OnTick. Fields listed here are OWNED by MainForm.
    /// Some fields (e.g. _devices, _networkMode) are also read on the SDL2 thread
    /// via OnInputReceived — the MainForm local field is the authoritative copy.
    /// </summary>
    private void SyncTabContext()
    {
        _tabContext.Devices = _devices;
        _tabContext.DisconnectedDevices = _disconnectedDevices;
        _tabContext.SelectedDevice = _selectedDevice;
        _tabContext.CurrentInputState = _currentInputState;
        _tabContext.VJoyDevices = _vjoyDevices;
        _tabContext.SelectedVJoyDeviceIndex = _selectedVJoyDeviceIndex;
        _tabContext.DeviceMap = _deviceMap;
        _tabContext.MappingsPrimaryDeviceMap = _mappingsPrimaryDeviceMap;
        _tabContext.IsForwarding = _isForwarding;
        _tabContext.BackgroundDirty = _backgroundDirty;
        _tabContext.NetworkMode = _networkMode;
        _tabContext.IsNetworkConnecting = _isNetworkConnecting;
        _tabContext.IsClientConnected = _isClientConnected;
        _tabContext.MousePosition = _mousePosition;
        _tabContext.LeadLineProgress = _leadLineProgress;
        _tabContext.PulsePhase = _pulsePhase;
        _tabContext.DashPhase = _dashPhase;
        _tabContext.JoystickSvg = _joystickSvg;
        _tabContext.ThrottleSvg = _throttleSvg;
        _tabContext.HoveredControlId = _hoveredControlId;
        _tabContext.SelectedControlId = _selectedControlId;
        _tabContext.SilhouetteBounds = _silhouetteBounds;
        _tabContext.SvgScale = _svgScale;
        _tabContext.SvgOffset = _svgOffset;
        _tabContext.SvgMirrored = _svgMirrored;
        _tabContext.ControlBounds = _controlBounds;
        _tabContext.Profiles = _profiles;
    }

    /// <summary>
    /// SyncFromTabContext pulls controller-modified state back into MainForm.
    /// Fields listed here can be MODIFIED by tab controllers via TabContext.
    /// Note: fields also read on the SDL2 thread (OnInputReceived) must remain
    /// as local MainForm fields — accessing TabContext from the SDL2 thread is unsafe.
    /// </summary>
    private void SyncFromTabContext()
    {
        _backgroundDirty = _tabContext.BackgroundDirty;
        _isForwarding = _tabContext.IsForwarding;
        _selectedDevice = _tabContext.SelectedDevice;
        _currentInputState = _tabContext.CurrentInputState;
        _selectedVJoyDeviceIndex = _tabContext.SelectedVJoyDeviceIndex;
        _deviceMap = _tabContext.DeviceMap;
        // Note: _mappingsPrimaryDeviceMap is NOT read back from context here.
        // It is written only by UpdateMappingsPrimaryDeviceMap and pushed to context
        // by SyncTabContext. Pulling it back would clobber updates made mid-frame
        // by tab controller callbacks (e.g. switching vJoy devices in the Mappings tab).
        _hoveredControlId = _tabContext.HoveredControlId;
        _selectedControlId = _tabContext.SelectedControlId;
        _silhouetteBounds = _tabContext.SilhouetteBounds;
        _svgScale = _tabContext.SvgScale;
        _svgOffset = _tabContext.SvgOffset;
        _svgMirrored = _tabContext.SvgMirrored;
    }

    /// <summary>
    /// If the current active tab is not in the visible tab set, snap to a sensible default.
    /// Client-only mode opens to KEYBINDINGS (2); otherwise falls back to SETTINGS (3).
    /// Called on startup and after settings changes that affect tab visibility.
    /// </summary>
    private void SnapToValidTab()
    {
        var visible = GetVisibleTabIndices();
        if (!visible.Contains(_activeTab))
            _activeTab = _appSettings.ClientOnlyMode ? 2 : 3;
    }

    private void InitializeVJoy()
    {
        if (!_vjoyService.Initialize())
        {
            System.Diagnostics.Debug.WriteLine("vJoy driver not available");
        }
    }

    private void InitializeProfiles()
    {
        _profileManager.Initialize();
        RefreshProfileList();

        // Initialize primary devices for loaded profile
        _profileManager.ActiveProfile?.UpdateAllPrimaryDevices();
        UpdateMappingsPrimaryDeviceMap();

        // Initialize font scaling (reads Windows text scale setting)
        FUIRenderer.InitializeFontScaling();

        // Set display scale from form's DPI (e.g., 150% = 144 DPI)
        FUIRenderer.SetDisplayScale(DeviceDpi);

        // Apply user's font size preference
        FUIRenderer.InterfaceScale = _appSettings.FontSize;

        // Apply user's font family preference
        FUIRenderer.FontFamily = _appSettings.FontFamily;

        // Apply theme setting
        FUIColors.SetTheme(_themeService.Theme);

        // Apply background settings
        var bgSettings = _themeService.LoadBackgroundSettings();
        _background.GridStrength = bgSettings.gridStrength;
        _background.GlowIntensity = bgSettings.glowIntensity;
        _background.NoiseIntensity = bgSettings.noiseIntensity;
        _background.ScanlineIntensity = bgSettings.scanlineIntensity;
        _background.VignetteStrength = bgSettings.vignetteStrength;
    }

    private void RefreshProfileList()
    {
        _profiles = _profileRepository.ListProfiles();
    }

    private void OpenDriverSetupDialog()
    {
        using var setupForm = new DriverSetupForm(_driverSetupManager, _themeService, _appSettings, settingsMode: true);
        setupForm.ShowDialog(this);
        _canvas.Invalidate();
    }

    private void CreateNewProfilePrompt()
    {
        string defaultName = $"Profile {_profiles.Count + 1}";
        var name = FUIInputDialog.Show(this, "New Profile", "Profile Name:", defaultName, "Create");
        if (name is not null)
        {
            _profileManager.CreateAndActivateProfile(name);
            UpdateMappingsPrimaryDeviceMap();
            RefreshProfileList();
        }
    }

    private void ImportProfilePrompt()
    {
        using var openDialog = new OpenFileDialog
        {
            Title = "Import Profile",
            Filter = "Asteriq Profile (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = "json",
            CheckFileExists = true
        };

        if (openDialog.ShowDialog(this) == DialogResult.OK)
        {
            var imported = _profileRepository.ImportProfile(openDialog.FileName);
            if (imported is not null)
            {
                _profileManager.ActivateProfile(imported.Id);
                // Initialize primary devices for imported profile
                _profileManager.ActiveProfile?.UpdateAllPrimaryDevices();
                UpdateMappingsPrimaryDeviceMap();
                RefreshProfileList();
            }
            else
            {
                FUIMessageBox.ShowError(this,
                    "Failed to import profile. The file may be corrupted or in an invalid format.",
                    "Import Failed");
            }
        }
    }

    private void ExportActiveProfile()
    {
        if (_profileManager.ActiveProfile is null)
        {
            FUIMessageBox.ShowInfo(this,
                "No profile is currently active. Please select a profile first.",
                "Export");
            return;
        }

        var profile = _profileManager.ActiveProfile;
        string suggestedName = $"{profile.Name.Replace(" ", "_")}.json";

        using var saveDialog = new SaveFileDialog
        {
            Title = "Export Profile",
            Filter = "Asteriq Profile (*.json)|*.json",
            DefaultExt = "json",
            FileName = suggestedName,
            OverwritePrompt = true
        };

        if (saveDialog.ShowDialog(this) == DialogResult.OK)
        {
            bool success = _profileRepository.ExportProfile(profile.Id, saveDialog.FileName);
            if (success)
            {
                FUIMessageBox.ShowInfo(this,
                    $"Profile '{profile.Name}' exported successfully.",
                    "Export Complete");
            }
            else
            {
                FUIMessageBox.ShowError(this,
                    "Failed to export profile.",
                    "Export Failed");
            }
        }
    }

    private void DuplicateActiveProfile()
    {
        var profile = _profileManager.ActiveProfile;
        if (profile is null) return;

        string newName = $"{profile.Name} (copy)";
        var duplicated = _profileRepository.DuplicateProfile(profile.Id, newName);
        if (duplicated is not null)
        {
            _profileManager.ActivateProfile(duplicated.Id);
            _profileManager.ActiveProfile?.UpdateAllPrimaryDevices();
            UpdateMappingsPrimaryDeviceMap();
            RefreshProfileList();
        }
    }

    private void DeleteActiveProfile()
    {
        var profile = _profileManager.ActiveProfile;
        if (profile is null) return;

        int result = FUIMessageBox.Show(this,
            $"Delete profile '{profile.Name}'?\n\nThis cannot be undone.",
            "Delete Profile", FUIMessageBox.MessageBoxType.Question, "Delete", "Cancel");
        if (result != 0) return;

        var profileId = profile.Id;
        _profileManager.DeactivateProfile();
        _profileRepository.DeleteProfile(profileId);
        RefreshProfileList();

        // Activate first remaining profile if any
        if (_profiles.Count > 0)
            _profileManager.ActivateProfile(_profiles[0].Id);

        UpdateMappingsPrimaryDeviceMap();
    }

    private void LoadSvgAssets()
    {
        var imagesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Devices");
        var mapsDir = Path.Combine(imagesDir, "Maps");

        var joystickPath = Path.Combine(imagesDir, "joystick.svg");
        if (File.Exists(joystickPath))
        {
            _joystickSvg = new SKSvg();
            _joystickSvg.Load(joystickPath);
            ParseControlBounds(joystickPath);
        }

        var throttlePath = Path.Combine(imagesDir, "throttle.svg");
        if (File.Exists(throttlePath))
        {
            _throttleSvg = new SKSvg();
            _throttleSvg.Load(throttlePath);
        }

        // Load default device map (will be updated when device is selected)
        LoadDeviceMapForDevice(null);
    }

    /// <summary>
    /// Load the appropriate device map based on device info.
    /// Searches for maps matching VID:PID first, then device name, falls back to device type, then generic joystick.json.
    /// Also detects left-hand devices by "LEFT" prefix and sets mirror flag.
    /// </summary>
    private void LoadDeviceMapForDevice(PhysicalDeviceInfo? device)
    {
        var mapsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Devices", "Maps");
        string? deviceName = device?.Name;

        // For virtual (vJoy) devices, check for a silhouette override first
        if (device is not null && device.IsVirtual)
        {
            // Derive vjoy ID using index-based lookup (most reliable; SDL2 names vary)
            uint vjoyId = 0;
            if (_vjoyDevices.Count > 0)
            {
                var virtualDevices = _devices.Where(d => d.IsVirtual).ToList();
                int virtualIndex = virtualDevices.IndexOf(device);
                if (virtualIndex >= 0 && virtualIndex < _vjoyDevices.Count)
                    vjoyId = _vjoyDevices[virtualIndex].Id;
            }
            // Fallback: parse from name (e.g. "vJoy Device 1" → 1)
            if (vjoyId == 0)
            {
                var vMatch = System.Text.RegularExpressions.Regex.Match(device.Name, @"\d+");
                if (vMatch.Success && uint.TryParse(vMatch.Value, out uint parsedId))
                    vjoyId = parsedId;
            }

            if (vjoyId > 0)
            {
                var overrideKey = _appSettings.GetVJoySilhouetteOverride(vjoyId);
                if (!string.IsNullOrEmpty(overrideKey))
                {
                    var overridePath = Path.Combine(mapsDir, $"{overrideKey}.json");
                    if (File.Exists(overridePath))
                    {
                        _deviceMap = DeviceMap.Load(overridePath);
                        SyncDeviceMapToTabContext();
                        return;
                    }
                }

                // Auto: try to find the physical device assigned to this vJoy slot
                var profile = _profileManager.ActiveProfile;
                if (profile is not null)
                {
                    var primaryGuid = profile.GetPrimaryDeviceForVJoy(vjoyId);
                    if (string.IsNullOrEmpty(primaryGuid))
                    {
                        var assignment = profile.GetAssignmentForVJoy(vjoyId);
                        if (assignment is not null && !string.IsNullOrEmpty(assignment.PhysicalDevice.Guid))
                            primaryGuid = assignment.PhysicalDevice.Guid;
                    }

                    PhysicalDeviceInfo? physicalDevice = null;
                    if (!string.IsNullOrEmpty(primaryGuid))
                        physicalDevice = _devices.FirstOrDefault(d =>
                            !d.IsVirtual && d.InstanceGuid.ToString().Equals(primaryGuid, StringComparison.OrdinalIgnoreCase));

                    // Fallback: match by device name
                    if (physicalDevice is null)
                    {
                        var assignment = profile.GetAssignmentForVJoy(vjoyId);
                        if (assignment is not null && !string.IsNullOrEmpty(assignment.PhysicalDevice.Name))
                            physicalDevice = _devices.FirstOrDefault(d =>
                                !d.IsVirtual && d.Name.Equals(assignment.PhysicalDevice.Name, StringComparison.OrdinalIgnoreCase));
                    }

                    if (physicalDevice is not null)
                    {
                        _deviceMap = LoadDeviceMapForDeviceInfo(physicalDevice);
                        SyncDeviceMapToTabContext();
                        return;
                    }
                }
            }
            // No override and no physical device found - use generic joystick map
            _deviceMap = DeviceMap.Load(Path.Combine(mapsDir, "joystick.json"));
            SyncDeviceMapToTabContext();
            return;
        }

        // Try to find a device-specific map
        if (device is not null && !string.IsNullOrEmpty(deviceName))
        {
            // Extract VID:PID from device's InstanceGuid
            var (vid, pid) = Services.DeviceMatchingService.ExtractVidPidFromSdlGuid(device.InstanceGuid);
            string vidPidStr = vid > 0 ? $"{vid:X4}:{pid:X4}" : "";

            // Load all device maps
            var allMaps = new List<(string path, DeviceMap map)>();
            foreach (var mapFile in Directory.GetFiles(mapsDir, "*.json"))
            {
                if (Path.GetFileName(mapFile).Equals("device-control-map.schema.json", StringComparison.OrdinalIgnoreCase))
                    continue;

                var map = DeviceMap.Load(mapFile);
                if (map is not null)
                    allMaps.Add((mapFile, map));
            }

            // Step 1: Try VID:PID match (most reliable)
            if (!string.IsNullOrEmpty(vidPidStr))
            {
                foreach (var (path, map) in allMaps)
                {
                    if (!string.IsNullOrEmpty(map.VidPid) &&
                        map.VidPid.Equals(vidPidStr, StringComparison.OrdinalIgnoreCase))
                    {
                        _deviceMap = map;
                        SyncDeviceMapToTabContext();
                        return;
                    }
                }
            }

            // Step 2: Try device name match (skip generic maps)
            // Strip manufacturer prefixes before comparing so map display names (e.g. "MongoosT-50CM3 Throttle")
            // still match SDL-reported names (e.g. "VPC MongoosT-50CM3").
            static string StripMfr(string s) => s
                .Replace("VPC ", "", StringComparison.OrdinalIgnoreCase)
                .Replace("VKB ", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            string normDevice = StripMfr(deviceName);
            foreach (var (path, map) in allMaps)
            {
                if (!string.IsNullOrEmpty(map.Device) &&
                    !map.Device.StartsWith("Generic", StringComparison.OrdinalIgnoreCase))
                {
                    string normMap = StripMfr(map.Device);
                    if (normDevice.Contains(normMap, StringComparison.OrdinalIgnoreCase) ||
                        normMap.Contains(normDevice, StringComparison.OrdinalIgnoreCase))
                    {
                        _deviceMap = map;
                        SyncDeviceMapToTabContext();
                        System.Diagnostics.Debug.WriteLine($"Loaded device map (name match): {path} for device: {deviceName}");
                        return;
                    }
                }
            }

            // Step 3: Try device type match based on keywords in device name
            string detectedType = DetectDeviceType(deviceName);
            if (detectedType != "Joystick")
            {
                foreach (var (path, map) in allMaps)
                {
                    if (map.DeviceType.Equals(detectedType, StringComparison.OrdinalIgnoreCase))
                    {
                        _deviceMap = map;
                        SyncDeviceMapToTabContext();
                        System.Diagnostics.Debug.WriteLine($"Loaded device map (type match '{detectedType}'): {path} for device: {deviceName}");
                        return;
                    }
                }
            }

            // Step 4: No specific map found - check if device name indicates left-hand
            // and apply mirror to the default joystick map
            bool isLeftHand = deviceName.StartsWith("LEFT", StringComparison.OrdinalIgnoreCase) ||
                              deviceName.Contains("- L", StringComparison.OrdinalIgnoreCase) ||
                              deviceName.EndsWith(" L", StringComparison.OrdinalIgnoreCase);

            var defaultMapPath = Path.Combine(mapsDir, "joystick.json");
            _deviceMap = DeviceMap.Load(defaultMapPath);

            if (_deviceMap is not null && isLeftHand)
            {
                // Override mirror setting for left-hand devices using generic map
                _deviceMap.Mirror = true;
            }
            SyncDeviceMapToTabContext();
            System.Diagnostics.Debug.WriteLine($"Loaded default device map: joystick.json for device: {deviceName} (left={isLeftHand})");
            return;
        }

        // Fall back to generic joystick map
        var defaultMapPath2 = Path.Combine(mapsDir, "joystick.json");
        _deviceMap = DeviceMap.Load(defaultMapPath2);
        SyncDeviceMapToTabContext();
    }

    /// <summary>
    /// Syncs the current _deviceMap to TabContext so SyncFromTabContext doesn't clobber it.
    /// Called after LoadDeviceMapForDevice modifies _deviceMap via callback.
    /// </summary>
    private void SyncDeviceMapToTabContext()
    {
        if (_tabContext is not null)
            _tabContext.DeviceMap = _deviceMap;
    }

    /// <summary>
    /// Load device map for a device and return it (doesn't modify _deviceMap field)
    /// </summary>
    private static DeviceMap? LoadDeviceMapForDeviceInfo(PhysicalDeviceInfo? device)
    {
        var mapsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Devices", "Maps");
        string? deviceName = device?.Name;

        if (device is not null && !string.IsNullOrEmpty(deviceName))
        {
            // Extract VID:PID from device's InstanceGuid
            var (vid, pid) = Services.DeviceMatchingService.ExtractVidPidFromSdlGuid(device.InstanceGuid);
            string vidPidStr = vid > 0 ? $"{vid:X4}:{pid:X4}" : "";

            var allMaps = new List<(string path, DeviceMap map)>();
            foreach (var mapFile in Directory.GetFiles(mapsDir, "*.json"))
            {
                if (Path.GetFileName(mapFile).Equals("device-control-map.schema.json", StringComparison.OrdinalIgnoreCase))
                    continue;
                var map = DeviceMap.Load(mapFile);
                if (map is not null)
                    allMaps.Add((mapFile, map));
            }

            // Try VID:PID match first (most reliable)
            if (!string.IsNullOrEmpty(vidPidStr))
            {
                foreach (var (path, map) in allMaps)
                {
                    if (!string.IsNullOrEmpty(map.VidPid) &&
                        map.VidPid.Equals(vidPidStr, StringComparison.OrdinalIgnoreCase))
                    {
                        return map;
                    }
                }
            }

            // Try device name match (strip manufacturer prefixes so display names match SDL names)
            static string StripMfr(string s) => s
                .Replace("VPC ", "", StringComparison.OrdinalIgnoreCase)
                .Replace("VKB ", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            string normDevice = StripMfr(deviceName);
            foreach (var (path, map) in allMaps)
            {
                if (!string.IsNullOrEmpty(map.Device) &&
                    !map.Device.StartsWith("Generic", StringComparison.OrdinalIgnoreCase))
                {
                    string normMap = StripMfr(map.Device);
                    if (normDevice.Contains(normMap, StringComparison.OrdinalIgnoreCase) ||
                        normMap.Contains(normDevice, StringComparison.OrdinalIgnoreCase))
                    {
                        return map;
                    }
                }
            }

            // Try device type match
            string detectedType = DetectDeviceType(deviceName);
            if (detectedType != "Joystick")
            {
                foreach (var (path, map) in allMaps)
                {
                    if (map.DeviceType.Equals(detectedType, StringComparison.OrdinalIgnoreCase))
                        return map;
                }
            }

            // Check left-hand and apply mirror
            bool isLeftHand = deviceName.StartsWith("LEFT", StringComparison.OrdinalIgnoreCase) ||
                              deviceName.Contains("- L", StringComparison.OrdinalIgnoreCase) ||
                              deviceName.EndsWith(" L", StringComparison.OrdinalIgnoreCase);

            var defaultMap = DeviceMap.Load(Path.Combine(mapsDir, "joystick.json"));
            if (defaultMap is not null && isLeftHand)
                defaultMap.Mirror = true;
            return defaultMap;
        }

        return DeviceMap.Load(Path.Combine(mapsDir, "joystick.json"));
    }

    /// <summary>
    /// Get the appropriate SVG for a given device map.
    /// Returns null if the map references a non-SVG image — call GetBitmapForDeviceMap instead.
    /// Falls back to the generic joystick/throttle SVG when no per-device file is found.
    /// </summary>
    private SKSvg? GetSvgForDeviceMap(DeviceMap? map)
    {
        if (map is null)
            return _joystickSvg;

        var imageFile = map.SvgFile;
        if (!string.IsNullOrEmpty(imageFile))
        {
            var ext = Path.GetExtension(imageFile).ToLowerInvariant();
            if (ext != ".svg")
                return null; // bitmap — caller should use GetBitmapForDeviceMap

            var svg = LoadSvgFromCache(imageFile);
            if (svg is not null) return svg;
        }

        // Generic fallback
        var lower = (imageFile ?? "").ToLowerInvariant();
        return lower.Contains("throttle") ? _throttleSvg ?? _joystickSvg : _joystickSvg;
    }

    /// <summary>
    /// Get the bitmap for a given device map, if its image file is a raster format.
    /// Returns null for SVG maps or maps with no image file.
    /// </summary>
    private SKBitmap? GetBitmapForDeviceMap(DeviceMap? map)
    {
        if (map is null) return null;

        var imageFile = map.SvgFile;
        if (string.IsNullOrEmpty(imageFile)) return null;

        var ext = Path.GetExtension(imageFile).ToLowerInvariant();
        if (ext == ".svg") return null;

        return LoadBitmapFromCache(imageFile);
    }

    private static SKSvg? LoadLogoSvg()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "AsteriqLogo.svg");
        if (!File.Exists(path)) return null;
        var svg = new SKSvg();
        if (svg.Load(path) is null) { svg.Dispose(); return null; }
        return svg;
    }

    private SKSvg? LoadSvgFromCache(string imageFile)
    {
        if (_svgCache.TryGetValue(imageFile, out var cached)) return cached;

        var imagesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Devices");
        var path = Path.Combine(imagesDir, imageFile);
        if (!File.Exists(path)) return null;

        var svg = new SKSvg();
        if (svg.Load(path) is null) { svg.Dispose(); return null; }
        _svgCache[imageFile] = svg;
        return svg;
    }

    private SKBitmap? LoadBitmapFromCache(string imageFile)
    {
        if (_bitmapCache.TryGetValue(imageFile, out var cached)) return cached;

        var imagesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Devices");
        var path = Path.Combine(imagesDir, imageFile);
        if (!File.Exists(path)) return null;

        var bitmap = SKBitmap.Decode(path);
        if (bitmap is null) return null;
        _bitmapCache[imageFile] = bitmap;
        return bitmap;
    }

    /// <summary>
    /// Enumerates all device map JSON files in the Maps directory and returns them as
    /// (Key, DisplayName) pairs. Called once at startup and stored in TabContext.
    /// </summary>
    private static List<(string Key, string DisplayName)> LoadAvailableDeviceMaps()
    {
        var mapsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Devices", "Maps");
        var result = new List<(string Key, string DisplayName)>();
        if (!Directory.Exists(mapsDir)) return result;

        foreach (var mapFile in Directory.GetFiles(mapsDir, "*.json").OrderBy(f => f))
        {
            if (Path.GetFileName(mapFile).Equals("device-control-map.schema.json", StringComparison.OrdinalIgnoreCase))
                continue;
            var map = DeviceMap.Load(mapFile);
            if (map is null) continue;
            string key = Path.GetFileNameWithoutExtension(mapFile);
            string displayName = string.IsNullOrEmpty(map.Device) ? key : map.Device;
            result.Add((key, displayName));
        }
        return result;
    }

    /// <summary>
    /// Update the device map used for Mappings tab visualization based on vJoy primary device.
    /// Respects the user's visual identity override if set; falls back to auto-detection from
    /// the primary physical device assigned/mapped to this vJoy slot.
    /// </summary>
    private void UpdateMappingsPrimaryDeviceMap()
    {
        _mappingsPrimaryDeviceMap = null;

        // Ensure vJoy devices are populated
        if (_vjoyDevices.Count == 0 && _vjoyService.IsInitialized)
        {
            _vjoyDevices = _vjoyService.EnumerateDevices();
        }

        if (_vjoyDevices.Count == 0)
            return;

        // Use context index when available — tab controllers may update it before SyncFromTabContext runs
        int effectiveIndex = _tabContext?.SelectedVJoyDeviceIndex ?? _selectedVJoyDeviceIndex;
        if (effectiveIndex >= _vjoyDevices.Count)
            return;

        var vjoyDevice = _vjoyDevices[effectiveIndex];

        // Check for a user-specified silhouette override first
        var overrideKey = _appSettings.GetVJoySilhouetteOverride(vjoyDevice.Id);
        if (!string.IsNullOrEmpty(overrideKey))
        {
            var mapsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Devices", "Maps");
            var overridePath = Path.Combine(mapsDir, $"{overrideKey}.json");
            if (File.Exists(overridePath))
            {
                _mappingsPrimaryDeviceMap = DeviceMap.Load(overridePath);
                return;
            }
        }

        // Auto: derive from the primary physical device mapped to this vJoy slot
        var profile = _profileManager.ActiveProfile;
        if (profile is null)
            return;

        var primaryGuid = profile.GetPrimaryDeviceForVJoy(vjoyDevice.Id);

        // Fallback: use device assignment when no primary detected from mappings
        if (string.IsNullOrEmpty(primaryGuid))
        {
            var assignment = profile.GetAssignmentForVJoy(vjoyDevice.Id);
            if (assignment is not null && !string.IsNullOrEmpty(assignment.PhysicalDevice.Guid))
                primaryGuid = assignment.PhysicalDevice.Guid;
        }

        if (string.IsNullOrEmpty(primaryGuid))
            return;

        var primaryDevice = _devices.FirstOrDefault(d =>
            d.InstanceGuid.ToString().Equals(primaryGuid, StringComparison.OrdinalIgnoreCase));

        // Fallback: match by device name if GUID doesn't resolve (e.g. SDL GUID shifted between sessions)
        if (primaryDevice is null)
        {
            var assignment = profile.GetAssignmentForVJoy(vjoyDevice.Id);
            if (assignment is not null && !string.IsNullOrEmpty(assignment.PhysicalDevice.Name))
                primaryDevice = _devices.FirstOrDefault(d =>
                    !d.IsVirtual && d.Name.Equals(assignment.PhysicalDevice.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (primaryDevice is not null)
            _mappingsPrimaryDeviceMap = LoadDeviceMapForDeviceInfo(primaryDevice);
    }

    /// <summary>
    /// Called when mappings are created or removed to update primary device detection
    /// and hot-reload the mapping engine if forwarding is active.
    /// </summary>
    private void OnMappingsChanged()
    {
        var profile = _profileManager.ActiveProfile;
        if (profile is null)
            return;

        // Re-detect primary devices for all vJoy slots based on current mappings
        profile.UpdateAllPrimaryDevices();

        // Refresh the visualization map for current vJoy device
        UpdateMappingsPrimaryDeviceMap();

        // Hot-reload engine if forwarding is active so new/changed mappings take effect
        if (_isForwarding && _mappingEngine.IsRunning)
        {
            _mappingEngine.Stop();
            _mappingEngine.LoadProfile(profile);
            _mappingEngine.Start();
        }
    }

    /// <summary>
    /// Detect device type from device name using common keywords
    /// </summary>
    internal static string DetectDeviceType(string deviceName)
    {
        var name = deviceName.ToUpperInvariant();

        // Throttle keywords - VPC throttles (MongoosT-50CM, CM2, CM3), TWCS, Warthog, etc.
        if (name.Contains("THROTTLE") || name.Contains("-50CM") || name.Contains("50CM") ||
            name.Contains("TM50") || name.Contains("TWCS") || name.Contains("MONGOOST") ||
            name.Contains("MONGOOSE") || name.Contains("CM2") || name.Contains("CM3"))
        {
            System.Diagnostics.Debug.WriteLine($"DetectDeviceType: '{deviceName}' -> Throttle");
            return "Throttle";
        }

        // Pedals keywords
        if (name.Contains("PEDAL") || name.Contains("RUDDER") || name.Contains("TPR") ||
            name.Contains("MFG") || name.Contains("CROSSWIND"))
        {
            System.Diagnostics.Debug.WriteLine($"DetectDeviceType: '{deviceName}' -> Pedals");
            return "Pedals";
        }

        // Default to joystick
        System.Diagnostics.Debug.WriteLine($"DetectDeviceType: '{deviceName}' -> Joystick (default)");
        return "Joystick";
    }

    private SKSvg? GetActiveSvg() => GetSvgForDeviceMap(_deviceMap);

    private SKBitmap? GetActiveBitmap() => GetBitmapForDeviceMap(_deviceMap);

    private void ParseControlBounds(string svgPath)
    {
        _controlBounds.Clear();

        try
        {
            var doc = XDocument.Load(svgPath);
            XNamespace svg = "http://www.w3.org/2000/svg";

            // Find all groups with id starting with "control_"
            var controlGroups = doc.Descendants(svg + "g")
                .Where(g => g.Attribute("id")?.Value?.StartsWith("control_") == true);

            foreach (var group in controlGroups)
            {
                string id = group.Attribute("id")!.Value;

                // Calculate bounding box by examining child elements
                var bounds = CalculateGroupBounds(group, svg);
                if (bounds.HasValue)
                {
                    _controlBounds[id] = bounds.Value;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Xml.XmlException)
        {
            System.Diagnostics.Debug.WriteLine($"Error parsing SVG control bounds: {ex.Message}");
        }
    }

    private static SKRect? CalculateGroupBounds(XElement group, XNamespace svg)
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        bool hasValidBounds = false;

        // Check for transform attribute on the group
        var transform = group.Attribute("transform")?.Value;
        float tx = 0, ty = 0;
        if (transform is not null && transform.StartsWith("translate("))
        {
            var match = System.Text.RegularExpressions.Regex.Match(transform, @"translate\(([\d.-]+),?\s*([\d.-]*)\)");
            if (match.Success)
            {
                float.TryParse(match.Groups[1].Value, out tx);
                if (!string.IsNullOrEmpty(match.Groups[2].Value))
                    float.TryParse(match.Groups[2].Value, out ty);
            }
        }

        // Find all path, rect, circle, ellipse elements and their bounds
        foreach (var element in group.Descendants())
        {
            var localName = element.Name.LocalName;
            SKRect? elementBounds = null;

            switch (localName)
            {
                case "rect":
                    {
                        float.TryParse(element.Attribute("x")?.Value ?? "0", out float x);
                        float.TryParse(element.Attribute("y")?.Value ?? "0", out float y);
                        float.TryParse(element.Attribute("width")?.Value ?? "0", out float w);
                        float.TryParse(element.Attribute("height")?.Value ?? "0", out float h);
                        elementBounds = new SKRect(x + tx, y + ty, x + tx + w, y + ty + h);
                    }
                    break;

                case "circle":
                    {
                        float.TryParse(element.Attribute("cx")?.Value ?? "0", out float cx);
                        float.TryParse(element.Attribute("cy")?.Value ?? "0", out float cy);
                        float.TryParse(element.Attribute("r")?.Value ?? "0", out float r);
                        elementBounds = new SKRect(cx + tx - r, cy + ty - r, cx + tx + r, cy + ty + r);
                    }
                    break;

                case "ellipse":
                    {
                        float.TryParse(element.Attribute("cx")?.Value ?? "0", out float cx);
                        float.TryParse(element.Attribute("cy")?.Value ?? "0", out float cy);
                        float.TryParse(element.Attribute("rx")?.Value ?? "0", out float rx);
                        float.TryParse(element.Attribute("ry")?.Value ?? "0", out float ry);
                        elementBounds = new SKRect(cx + tx - rx, cy + ty - ry, cx + tx + rx, cy + ty + ry);
                    }
                    break;

                case "path":
                    // For paths, extract approximate bounds from d attribute
                    var d = element.Attribute("d")?.Value;
                    if (!string.IsNullOrEmpty(d))
                    {
                        elementBounds = GetPathApproximateBounds(d, tx, ty);
                    }
                    break;
            }

            if (elementBounds.HasValue)
            {
                hasValidBounds = true;
                minX = Math.Min(minX, elementBounds.Value.Left);
                minY = Math.Min(minY, elementBounds.Value.Top);
                maxX = Math.Max(maxX, elementBounds.Value.Right);
                maxY = Math.Max(maxY, elementBounds.Value.Bottom);
            }
        }

        return hasValidBounds ? new SKRect(minX, minY, maxX, maxY) : null;
    }

    private static SKRect? GetPathApproximateBounds(string d, float tx, float ty)
    {
        // Simple extraction of coordinate values from path data
        var numbers = System.Text.RegularExpressions.Regex.Matches(d, @"[-+]?\d*\.?\d+");
        if (numbers.Count < 2) return null;

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        // Process pairs of numbers as x,y coordinates (very simplified)
        for (int i = 0; i < numbers.Count - 1; i += 2)
        {
            if (float.TryParse(numbers[i].Value, out float x) &&
                float.TryParse(numbers[i + 1].Value, out float y))
            {
                minX = Math.Min(minX, x + tx);
                maxX = Math.Max(maxX, x + tx);
                minY = Math.Min(minY, y + ty);
                maxY = Math.Max(maxY, y + ty);
            }
        }

        if (minX == float.MaxValue) return null;

        return new SKRect(minX, minY, maxX, maxY);
    }

    private void InitializeForm()
    {
        Text = "Asteriq";
        MinimumSize = new Size(1570, 1000);  // Overridden by ApplyFontScaleToWindowSize once font settings load
        FormBorderStyle = FormBorderStyle.Sizable;  // Sizable for resize borders + colored borders
        BackColor = Color.Black;
        DoubleBuffered = true;
        KeyPreview = true;

        // Load application icon for taskbar
        LoadApplicationIcon();

        // Load saved window state or use defaults
        var (width, height, x, y) = _windowState.LoadWindowState();
        if (width > 0 && height > 0)
        {
            Size = new Size(width, height);
        }
        else
        {
            Size = new Size(1280, 800);
        }

        // Restore position or center on screen
        if (x != int.MinValue && y != int.MinValue)
        {
            // Validate position is on a visible screen
            var screenBounds = Screen.AllScreens.Select(s => s.Bounds).ToArray();
            bool isVisible = screenBounds.Any(b => b.Contains(x, y) ||
                b.Contains(x + width / 2, y + height / 2));

            if (isVisible)
            {
                StartPosition = FormStartPosition.Manual;
                Location = new Point(x, y);
            }
            else
            {
                StartPosition = FormStartPosition.CenterScreen;
            }
        }
        else
        {
            StartPosition = FormStartPosition.CenterScreen;
        }

        if (_appSettings.OpenMinimized)
            WindowState = FormWindowState.Minimized;
    }

    private void ApplyFontScaleToWindowSize()
    {
        // Minimum window size at VSmall (1.0x) scale: 1280×1024.
        // Scaled up proportionally with the combined canvas scale factor
        // (DPI × text scale × user preference) so the window is large enough
        // for the canvas drawing space at any DPI.
        const float baseMinW = 1280f;
        const float baseMinH = 1024f;
        float scale = FUIRenderer.CanvasScaleFactor;
        MinimumSize = new Size((int)(baseMinW * scale), (int)(baseMinH * scale));
        // Windows enforces MinimumSize automatically; if the current size is smaller it will be grown.
    }

    private void LoadApplicationIcon()
    {
        RefreshFormIcon();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemThemeChanged;
    }

    private void OnSystemThemeChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category == Microsoft.Win32.UserPreferenceCategory.General)
        {
            if (InvokeRequired)
                BeginInvoke(RefreshFormIcon);
            else
                RefreshFormIcon();
        }
    }

    private void RefreshFormIcon()
    {
        try
        {
            // Track our custom icon separately so we never accidentally Dispose the shared
            // WinForms static DefaultIcon (Form.DefaultIcon) that is returned by Form.Icon
            // when no custom icon has been set yet. Disposing DefaultIcon corrupts all
            // undecorated dialogs (e.g. FUIConfirmDialog) that inherit it during CreateHandle.
            var oldIcon = _customFormIcon;
            _customFormIcon = _trayIcon.CreateFormIcon();
            Icon = _customFormIcon;
            oldIcon?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            // Icon generation failed — taskbar will show default
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // Set border color for Windows 11+ (dark theme color)
        if (Environment.OSVersion.Version.Build >= 22000)  // Windows 11
        {
            // Convert RGB to COLORREF (BGR format): 0x00BBGGRR
            // Using FUIColor.BorderDark (#1A1F24) = RGB(26, 31, 36) = BGR(36, 31, 26)
            int borderColor = 0x00241F1A;  // BGR format
            DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Handle Mappings tab key capture (delegated to controller)
        if (_activeTab == 1)
        {
            SyncTabContext();
            bool handled = _mappingsController.ProcessCmdKey(ref msg, keyData);
            SyncFromTabContext();
            if (handled) return true;
        }

        // Handle SC Bindings keyboard input (search box, filename box)
        if (_activeTab == 2)
        {
            SyncTabContext();
            bool handled = _scBindingsController.ProcessCmdKey(ref msg, keyData);
            SyncFromTabContext();
            if (handled) return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    // (Key helper methods moved to MappingsTabController / SCBindingsTabController)

    /// <summary>
    /// SKControl that returns HTTRANSPARENT at window edges so the parent form
    /// can handle resize hit-testing and cursor display.
    /// </summary>
    private sealed class BorderPassthroughSKControl : SKControl
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;

        public Func<Point, bool>? IsInResizeBorder { get; set; }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST && IsInResizeBorder != null)
            {
                var pt = Parent!.PointToClient(System.Windows.Forms.Cursor.Position);
                if (IsInResizeBorder(pt))
                {
                    m.Result = (IntPtr)HTTRANSPARENT;
                    return;
                }
            }
            base.WndProc(ref m);
        }
    }

    private void InitializeCanvas()
    {
        _canvas = new BorderPassthroughSKControl
        {
            Dock = DockStyle.Fill,
            IsInResizeBorder = pt =>
            {
                int border = FramePadding + ResizeBorder;
                return pt.X < border || pt.X >= ClientSize.Width - border ||
                       pt.Y < border || pt.Y >= ClientSize.Height - border;
            }
        };
        _canvas.PaintSurface += OnPaintSurface;
        _canvas.MouseMove += OnCanvasMouseMove;
        _canvas.MouseDown += OnCanvasMouseDown;
        _canvas.MouseUp += OnCanvasMouseUp;
        _canvas.MouseLeave += OnCanvasMouseLeave;
        _canvas.MouseWheel += OnCanvasMouseWheel;
        Controls.Add(_canvas);
    }

    private void InitializeInput()
    {
        if (!_inputService.Initialize())
        {
            return;
        }

        _inputService.InputReceived += OnInputReceived;
        _inputService.DeviceConnected += OnDeviceConnected;
        _inputService.DeviceDisconnected += OnDeviceDisconnected;
        LoadDisconnectedDevices();
        RefreshDevices();

        // Only use the high-frequency poll rate when physical devices are actually connected.
        // With no devices, 10 Hz is enough for hot-plug detection and avoids timeBeginPeriod(1).
        int physicalCount = _devices.Count(d => !d.IsVirtual && d.IsConnected);
        _inputService.StartPolling(physicalCount > 0 ? 500 : 10);

    }

    private void InitializeRenderLoop()
    {
        _renderTimer = new System.Windows.Forms.Timer
        {
            Interval = 16 // ~60 FPS
        };
        _renderTimer.Tick += OnAnimationTick;
        _renderTimer.Start();
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        // Form is hidden to the system tray — skip all work, nothing to display.
        if (!Visible) return;

        bool needsUpdate = false;

        // Only update animations if enabled
        if (_enableAnimations)
        {
            // Update animation states
            _scanLineProgress += 0.005f;
            if (_scanLineProgress > 1f) _scanLineProgress = 0f;

            _dashPhase += 0.5f;
            if (_dashPhase > 10f) _dashPhase = 0f;

            _pulsePhase += 0.05f;
            if (_pulsePhase > MathF.PI * 2) _pulsePhase = 0f;

            // Lead line animation
            _leadLineProgress += 0.02f;
            if (_leadLineProgress > 1.3f) _leadLineProgress = 0f;

            // Update background animations
            FUIBackground.Update(0.016f);

            needsUpdate = true;
        }

        // Update active input animations (~16ms per tick = 0.016s)
        // Always update these as they track real device input
        if (_activeInputTracker.UpdateAnimations(0.016f))
        {
            needsUpdate = true;
        }

        // Mappings tab tick (pending modifier timeout, highlight animations)
        if (_activeTab == 1)
        {
            _mappingsController.OnTick();
        }

        // SC Bindings tab tick (input listening check)
        if (_activeTab == 2)
        {
            SyncTabContext();
            _scBindingsController.OnTick();
            SyncFromTabContext();
        }

        // Settings tab tick (toggle animation)
        if (_enableAnimations)
            _settingsController.OnTick();

        // Throttle redraws to ~10 FPS when the window is in the background.
        // SC Bindings input detection (OnTick above) still runs every tick.
        bool shouldRedraw = ContainsFocus || (++_unfocusedFrameCount % 6 == 0);

        // Only invalidate if animations ran or something is dirty, and we're not resizing
        if (shouldRedraw && (needsUpdate || _isDirty) && !_isResizing)
        {
            _canvas.Invalidate();
            _isDirty = false;
        }
    }

    private void RefreshVJoyDevicesInternal()
    {
        // vJoyConfig.exe reconfigures the driver, invalidating all kernel handles.
        // Release stale acquisitions before re-enumerating so the engine can
        // cleanly re-acquire devices when forwarding restarts.
        bool wasForwarding = _isForwarding;
        if (wasForwarding)
            StopForwarding();

        _vjoyService.ReleaseAllDevices();
        _vjoyDevices = _vjoyService.EnumerateDevices();

        // Immediately sync to context so callers can read the updated list before the next SyncTabContext()
        if (_tabContext is not null)
            _tabContext.VJoyDevices = _vjoyDevices;

        // Clean up mappings that reference axes no longer present on vJoy devices
        CleanupStaleMappings();

        // Restart forwarding if it was active — the engine will re-acquire devices
        if (wasForwarding)
            StartForwarding();

        // NOTE: callers that add a device should explicitly call RefreshDevices() afterwards
        // so the new SDL2 virtual joystick appears in the Devices tab.
        // Callers that remove a device should NOT call RefreshDevices() immediately,
        // because SDL2 still reports the device until the OS sends a device-removed notification.
        _canvas.Invalidate();
    }

    private void CleanupStaleMappings()
    {
        var profile = _profileManager.ActiveProfile;
        if (profile is null) return;

        bool changed = false;
        foreach (var vjoy in _vjoyDevices)
        {
            var validAxes = new HashSet<int>();
            if (vjoy.HasAxisX) validAxes.Add(0);
            if (vjoy.HasAxisY) validAxes.Add(1);
            if (vjoy.HasAxisZ) validAxes.Add(2);
            if (vjoy.HasAxisRX) validAxes.Add(3);
            if (vjoy.HasAxisRY) validAxes.Add(4);
            if (vjoy.HasAxisRZ) validAxes.Add(5);
            if (vjoy.HasSlider0) validAxes.Add(6);
            if (vjoy.HasSlider1) validAxes.Add(7);

            changed |= profile.AxisMappings.RemoveAll(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoy.Id &&
                !validAxes.Contains(m.Output.Index)) > 0;

            changed |= profile.AxisToButtonMappings.RemoveAll(m =>
                m.SourceVJoyDevice == vjoy.Id &&
                !validAxes.Contains(m.SourceAxisIndex)) > 0;
        }

        if (changed)
        {
            profile.ModifiedAt = DateTime.UtcNow;
            _profileManager.SaveActiveProfile();
        }
    }

    private void RefreshDevices()
    {
        var connectedDevices = _inputService.EnumerateDevices();

        // Mark all connected devices
        foreach (var device in connectedDevices)
        {
            device.IsConnected = true;
        }

        // Add disconnected devices that aren't currently connected
        // Only show disconnected physical devices (not virtual)
        var disconnectedToShow = _disconnectedDevices
            .Where(d => !d.IsVirtual && !connectedDevices.Any(c =>
                c.InstanceGuid == d.InstanceGuid ||
                (c.Name == d.Name && c.AxisCount == d.AxisCount && c.ButtonCount == d.ButtonCount)))
            .ToList();

        // Combine connected and disconnected devices
        _devices = connectedDevices.Concat(disconnectedToShow).ToList();

        // Apply user-defined device order from profile
        ApplyDeviceOrder();

        // Auto-select first device in current category if nothing selected
        if (_selectedDevice < 0 && _devices.Count > 0)
        {
            SelectFirstDeviceInCategory();
        }
    }

    private void SelectFirstDeviceInCategory()
    {
        // Use the controller's active device category (0 = physical, 1 = virtual)
        bool selectVirtual = _devicesController is not null && _devicesController.DeviceCategory == 1;
        var filteredDevices = _devices.Where(d => d.IsVirtual == selectVirtual).ToList();

        if (filteredDevices.Count > 0)
        {
            _selectedDevice = _devices.IndexOf(filteredDevices[0]);
            // Keep context in sync so SyncFromTabContext doesn't clobber the new selection
            if (_tabContext is not null)
                _tabContext.SelectedDevice = _selectedDevice;
            if (_selectedDevice >= 0)
            {
                LoadDeviceMapForDevice(_devices[_selectedDevice]);
            }
        }
    }

    private void OnInputReceived(object? sender, DeviceInputState state)
    {
        // ── Network switch button detection (highest priority, rising edge) ──
        var switchCfg = _profileManager.ActiveProfile?.NetworkSwitchButton;
        if (_appSettings.NetworkEnabled && switchCfg is not null)
        {
            bool buttonPressed = state.DeviceIndex == switchCfg.DeviceIndex
                && switchCfg.ButtonIndex < state.Buttons.Length
                && state.Buttons[switchCfg.ButtonIndex];

            if (buttonPressed && !_lastSwitchButtonState)
            {
                // Rising edge — debounce then cycle through peers
                var nowTick = Environment.TickCount64;
                if (nowTick - _lastSwitchButtonTick < SwitchDebounceMs)
                {
                    _logger.LogDebug("[NetToggle] Rising edge DEBOUNCED ({Ms}ms since last)", nowTick - _lastSwitchButtonTick);
                }
                else
                {
                _lastSwitchButtonTick = nowTick;
                var peers = _networkDiscovery.KnownPeers.Values.ToList();
                _logger.LogDebug("[NetToggle] Rising edge | mode={Mode} connectedIp={ConnectedIp} peers={PeerCount} connecting={Connecting}",
                    _networkMode, _tabContext.ConnectedPeerIp ?? "none", peers.Count, _isNetworkConnecting);

                if (_networkMode == NetworkInputMode.Local)
                {
                    if (peers.Count > 0)
                    {
                        _logger.LogDebug("[NetToggle] Disconnected → connecting to peers[0]={Peer}", peers[0].IpAddress);
                        _ = ConnectAsMasterAsync(peers[0]);
                    }
                    else
                    {
                        _logger.LogDebug("[NetToggle] No peers discovered — ignoring");
                    }
                }
                else
                {
                    int cur = peers.FindIndex(p => p.IpAddress == _tabContext.ConnectedPeerIp);
                    int next = cur + 1;
                    _logger.LogDebug("[NetToggle] Connected | curIdx={Cur} nextIdx={Next} peerCount={Count}", cur, next, peers.Count);
                    if (next < peers.Count)
                    {
                        _logger.LogDebug("[NetToggle] Switching → peers[{Next}]={Peer}", next, peers[next].IpAddress);
                        _ = ConnectAsMasterAsync(peers[next]);
                    }
                    else
                    {
                        _logger.LogDebug("[NetToggle] Last peer reached → disconnecting");
                        _ = SwitchToLocalAsync();
                    }
                }
                } // end debounce else
            }
            _lastSwitchButtonState = buttonPressed;
            // Do NOT return — input must still reach the forwarding / local-vJoy path below.
        }

        // ── Master mode: run MappingEngine in capture mode, send snapshot ────
        // ForwardingMode is set exclusively by ConnectAsMasterAsync — no role setting required.
        // SuppressForwarding is true while SC Bindings button-capture is active — skip ProcessInput
        // entirely so the captured button press never reaches the snapshot or the remote machine.
        if (_networkMode == NetworkInputMode.Remote && _networkVjoy.ForwardingMode)
        {
            if (_mappingEngine.IsRunning && !_tabContext.SuppressForwarding)
                _mappingEngine.ProcessInput(state);

            // Do NOT send here — the 20 Hz heartbeat handles transmission.
            // Sending on every SDL2 event would flood the connection with joystick noise.
            return;
        }

        // ── Local forwarding — process through MappingEngine ─────────────────
        if (_isForwarding && _mappingEngine.IsRunning && !_tabContext.SuppressForwarding)
        {
            _mappingEngine.ProcessInput(state);
        }

        if (_selectedDevice >= 0 && _selectedDevice < _devices.Count &&
            state.DeviceIndex == _devices[_selectedDevice].DeviceIndex)
        {
            _currentInputState = state;

            // Track input activity for dynamic lead-lines
            TrackInputActivity(state);
        }

        // Check for button presses to highlight corresponding mapping in Mappings tab
        if (_activeTab == 1 && _profileManager.ActiveProfile is not null)
        {
            int prevHighlightRow = _mappingsController.HighlightedMappingRow;
            uint prevHighlightDevice = _mappingsController.HighlightedVJoyDevice;

            _mappingsController.CheckForMappingHighlight(state);

            // Invalidate canvas if highlight changed to show the shimmer effect
            if (_mappingsController.HighlightedMappingRow != prevHighlightRow ||
                _mappingsController.HighlightedVJoyDevice != prevHighlightDevice)
            {
                MarkDirty();
            }
        }
    }

    private void TrackInputActivity(DeviceInputState state)
    {
        if (_deviceMap is null) return;

        // Track axis changes
        for (int i = 0; i < state.Axes.Length; i++)
        {
            string binding = GetAxisBindingName(i);
            var control = _deviceMap.FindControlByBinding(binding);
            _activeInputTracker.Update(binding, state.Axes[i], isAxis: true, control);
        }

        // Track button changes
        for (int i = 0; i < state.Buttons.Length; i++)
        {
            string binding = $"button{i + 1}";
            var control = _deviceMap.FindControlByBinding(binding);
            _activeInputTracker.Update(binding, state.Buttons[i] ? 1f : 0f, isAxis: false, control);
        }
    }

    private static string GetAxisBindingName(int axisIndex)
    {
        return axisIndex switch
        {
            0 => "x",
            1 => "y",
            2 => "z",
            3 => "rx",
            4 => "ry",
            5 => "rz",
            6 => "slider1",
            7 => "slider2",
            _ => $"axis{axisIndex}"
        };
    }

    private void OnDeviceConnected(object? sender, PhysicalDeviceInfo newDevice)
    {
        BeginInvoke(() =>
        {
            // Remember currently selected device by identity
            Guid? selectedGuid = null;
            string? selectedName = null;
            if (_selectedDevice >= 0 && _selectedDevice < _devices.Count)
            {
                selectedGuid = _devices[_selectedDevice].InstanceGuid;
                selectedName = _devices[_selectedDevice].Name;
            }

            // Check if this device was previously disconnected
            var disconnected = _disconnectedDevices.FirstOrDefault(d =>
                d.InstanceGuid == newDevice.InstanceGuid ||
                (d.Name == newDevice.Name && d.AxisCount == newDevice.AxisCount && d.ButtonCount == newDevice.ButtonCount));

            if (disconnected is not null)
            {
                // Device reconnected - remove from disconnected list
                _disconnectedDevices.Remove(disconnected);
                SaveDisconnectedDevices();
            }

            RefreshDevices();

            // Restore selection by identity
            RestoreDeviceSelection(selectedGuid, selectedName);

            // First physical device connected — bump poll rate for responsive input
            if (_networkMode != NetworkInputMode.Receiving)
            {
                int physCount = _devices.Count(d => !d.IsVirtual && d.IsConnected);
                if (physCount > 0)
                    _inputService.SetPollRate(500);
            }

            MarkDirty();
        });
    }

    private void OnDeviceDisconnected(object? sender, int deviceIndex)
    {
        BeginInvoke(() =>
        {
            // Remember currently selected device by identity
            Guid? selectedGuid = null;
            string? selectedName = null;
            if (_selectedDevice >= 0 && _selectedDevice < _devices.Count)
            {
                selectedGuid = _devices[_selectedDevice].InstanceGuid;
                selectedName = _devices[_selectedDevice].Name;
            }

            // Find the device that was disconnected before we refresh
            var disconnectedDevice = _devices.FirstOrDefault(d => d.DeviceIndex == deviceIndex);

            if (disconnectedDevice is not null && !disconnectedDevice.IsVirtual)
            {
                // Always track physical devices when they disconnect
                // Mark as disconnected and add to tracked list
                disconnectedDevice.IsConnected = false;
                disconnectedDevice.DeviceIndex = -1; // No longer valid

                // Check if we already track this device
                if (!_disconnectedDevices.Any(d => d.InstanceGuid == disconnectedDevice.InstanceGuid))
                {
                    _disconnectedDevices.Add(disconnectedDevice);
                    SaveDisconnectedDevices();
                }
            }

            RefreshDevices();

            // Restore selection by identity
            RestoreDeviceSelection(selectedGuid, selectedName);

            // All physical devices gone — drop to low-rate hot-plug detection
            if (_networkMode != NetworkInputMode.Receiving)
            {
                int physCount = _devices.Count(d => !d.IsVirtual && d.IsConnected);
                if (physCount == 0)
                    _inputService.SetPollRate(10);
            }

            MarkDirty();
        });
    }

    private void RestoreDeviceSelection(Guid? selectedGuid, string? selectedName)
    {
        if (selectedGuid is null && selectedName is null)
            return;

        // Try to find the device by GUID first, then by name
        int newIndex = -1;
        for (int i = 0; i < _devices.Count; i++)
        {
            if (_devices[i].InstanceGuid == selectedGuid ||
                (selectedName is not null && _devices[i].Name == selectedName))
            {
                newIndex = i;
                break;
            }
        }

        if (newIndex >= 0)
        {
            _selectedDevice = newIndex;
        }
        else if (_selectedDevice >= _devices.Count)
        {
            _selectedDevice = Math.Max(0, _devices.Count - 1);
        }

        // Load device map for the selected device
        if (_selectedDevice >= 0 && _selectedDevice < _devices.Count)
        {
            LoadDeviceMapForDevice(_devices[_selectedDevice]);
        }
    }

    private void LoadDisconnectedDevices()
    {
        // Load disconnected devices from settings
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Asteriq", "disconnected_devices.json");

        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var devices = System.Text.Json.JsonSerializer.Deserialize<List<DisconnectedDeviceInfo>>(json);
                if (devices is not null)
                {
                    _disconnectedDevices = devices.Select(d => new PhysicalDeviceInfo
                    {
                        DeviceIndex = -1,
                        Name = d.Name,
                        InstanceGuid = d.InstanceGuid,
                        AxisCount = d.AxisCount,
                        ButtonCount = d.ButtonCount,
                        HatCount = d.HatCount,
                        IsVirtual = false,
                        IsConnected = false
                    }).ToList();
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // Ignore errors loading disconnected devices
            }
        }
    }

    private void SaveDisconnectedDevices()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Asteriq", "disconnected_devices.json");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var devices = _disconnectedDevices.Select(d => new DisconnectedDeviceInfo
            {
                Name = d.Name,
                InstanceGuid = d.InstanceGuid,
                AxisCount = d.AxisCount,
                ButtonCount = d.ButtonCount,
                HatCount = d.HatCount
            }).ToList();

            var json = System.Text.Json.JsonSerializer.Serialize(devices,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Ignore errors saving disconnected devices
        }
    }

    private record DisconnectedDeviceInfo
    {
        public string Name { get; init; } = string.Empty;
        public Guid InstanceGuid { get; init; }
        public int AxisCount { get; init; }
        public int ButtonCount { get; init; }
        public int HatCount { get; init; }
    }


    #region Device Order

    /// <summary>
    /// Save the current device order to the active profile and reassign vJoy slots
    /// Device order = vJoy order (first device -> vJoy 1, second -> vJoy 2, etc.)
    /// </summary>
    private void SaveDeviceOrder()
    {
        if (_profileManager.ActiveProfile is null)
            return;

        var profile = _profileManager.ActiveProfile;

        // Get physical devices only (that's what we reorder)
        var physicalDevices = _devices.Where(d => !d.IsVirtual).ToList();

        // Save the order as list of instance GUIDs
        profile.DeviceOrder = physicalDevices
            .Select(d => d.InstanceGuid.ToString())
            .ToList();

        // Build mapping from device GUID to old assignment info
        var oldAssignmentsByGuid = new Dictionary<string, DeviceAssignment>();
        foreach (var assignment in profile.DeviceAssignments)
        {
            oldAssignmentsByGuid[assignment.PhysicalDevice.Guid] = assignment;
        }

        // Build mapping from old vJoy ID to new vJoy ID based on new order
        var oldToNewVJoy = new Dictionary<uint, uint>();

        // Clear and rebuild device assignments based on new order
        profile.DeviceAssignments.Clear();
        for (int i = 0; i < physicalDevices.Count; i++)
        {
            var device = physicalDevices[i];
            uint newVJoyId = (uint)(i + 1); // vJoy is 1-indexed
            string deviceGuid = device.InstanceGuid.ToString();

            // Track old -> new mapping for updating mappings
            if (oldAssignmentsByGuid.TryGetValue(deviceGuid, out var oldAssignment))
            {
                oldToNewVJoy[oldAssignment.VJoyDevice] = newVJoyId;
            }

            // Get existing VidPid if available from old assignments
            string vidPid = oldAssignment?.PhysicalDevice.VidPid ?? "";

            // Create new assignment
            profile.DeviceAssignments.Add(new DeviceAssignment
            {
                PhysicalDevice = new PhysicalDeviceRef
                {
                    Name = device.Name,
                    Guid = deviceGuid,
                    VidPid = vidPid
                },
                VJoyDevice = newVJoyId
            });
        }

        // Update all mappings to use new vJoy IDs
        foreach (var mapping in profile.AxisMappings)
        {
            if (oldToNewVJoy.TryGetValue(mapping.Output.VJoyDevice, out uint newId))
            {
                mapping.Output.VJoyDevice = newId;
            }
        }
        foreach (var mapping in profile.ButtonMappings)
        {
            if (oldToNewVJoy.TryGetValue(mapping.Output.VJoyDevice, out uint newId))
            {
                mapping.Output.VJoyDevice = newId;
            }
        }
        foreach (var mapping in profile.HatMappings)
        {
            if (oldToNewVJoy.TryGetValue(mapping.Output.VJoyDevice, out uint newId))
            {
                mapping.Output.VJoyDevice = newId;
            }
        }
        foreach (var mapping in profile.AxisToButtonMappings)
        {
            if (oldToNewVJoy.TryGetValue(mapping.Output.VJoyDevice, out uint newId))
            {
                mapping.Output.VJoyDevice = newId;
            }
        }
        foreach (var mapping in profile.ButtonToAxisMappings)
        {
            if (oldToNewVJoy.TryGetValue(mapping.Output.VJoyDevice, out uint newId))
            {
                mapping.Output.VJoyDevice = newId;
            }
        }

        _profileManager.SaveActiveProfile();
        OnMappingsChanged();

        Console.WriteLine($"Device order saved. vJoy assignments updated:");
        for (int i = 0; i < physicalDevices.Count; i++)
        {
            Console.WriteLine($"  {i + 1}: {physicalDevices[i].Name} -> vJoy {i + 1}");
        }
    }

    /// <summary>
    /// Apply saved device order from the active profile
    /// </summary>
    private void ApplyDeviceOrder()
    {
        if (_profileManager.ActiveProfile is null)
            return;

        var savedOrder = _profileManager.ActiveProfile.DeviceOrder;
        if (savedOrder is null || savedOrder.Count == 0)
            return;

        // Separate physical and virtual devices
        var physicalDevices = _devices.Where(d => !d.IsVirtual).ToList();
        var virtualDevices = _devices.Where(d => d.IsVirtual).ToList();

        // Sort physical devices by saved order
        var orderedPhysical = new List<PhysicalDeviceInfo>();
        var unorderedPhysical = new List<PhysicalDeviceInfo>(physicalDevices);

        foreach (var guid in savedOrder)
        {
            var device = unorderedPhysical.FirstOrDefault(d =>
                d.InstanceGuid.ToString().Equals(guid, StringComparison.OrdinalIgnoreCase));
            if (device is not null)
            {
                orderedPhysical.Add(device);
                unorderedPhysical.Remove(device);
            }
        }

        // Add any new devices (not in saved order) at the end
        orderedPhysical.AddRange(unorderedPhysical);

        // Rebuild devices list with physical first, then virtual
        _devices = orderedPhysical.Concat(virtualDevices).ToList();
    }

    #endregion

    #region DPI Handling

    /// <summary>
    /// Handle DPI changes when window moves between monitors with different DPI settings.
    /// Per Microsoft best practices for Per-Monitor V2 DPI awareness.
    /// </summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);

        // Update the renderer's display scale factor
        FUIRenderer.SetDisplayScale(DeviceDpi);

        // Recalculate minimum window size for new DPI
        ApplyFontScaleToWindowSize();

        // Invalidate background cache so it regenerates at new scale
        _backgroundDirty = true;

        // Force full redraw at new DPI
        Invalidate();
    }

    #endregion

    #region System Tray

    // Windows 11 rounded-corner preference (DWM attr 33)
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    private const int TrayIconSize = 16;
    private static readonly Padding TrayItemPadding = new(0, 5, 10, 5); // 5px top/bottom, 10px right

    private void InitializeTrayMenu()
    {
        var textColor = SkiaColorToGdi(FUIColors.TextPrimary);
        var dimColor  = SkiaColorToGdi(FUIColors.TextDim);

        var menu = new ContextMenuStrip
        {
            Renderer         = new DarkContextMenuRenderer(),
            Font             = new Font("Segoe UI", 9.5f),
            ImageScalingSize = new Size(TrayIconSize, TrayIconSize),
            Padding          = new Padding(0, 4, 0, 4),  // top/bottom breathing room
        };

        // Windows 11: rounded corners, no DWM border highlight
        menu.Opened += (s, e) =>
        {
            if (s is ContextMenuStrip strip && strip.IsHandleCreated)
            {
                int pref    = DWMWCP_ROUND;
                int noBorder = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
                DwmSetWindowAttribute(strip.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref,     sizeof(int));
                DwmSetWindowAttribute(strip.Handle, DWMWA_BORDER_COLOR,             ref noBorder, sizeof(int));
            }
        };

        // ── Open Asteriq ─────────────────────────────────────────
        var openItem = new ToolStripMenuItem("Open Asteriq")
        {
            Image   = TrayMenuIcons.Open(TrayIconSize, textColor),
            Padding = TrayItemPadding,
        };
        openItem.Click += (s, e) => ShowAndActivateWindow();
        menu.Items.Add(openItem);

        menu.Items.Add(new ToolStripSeparator());

        // ── Start / Stop Forwarding ───────────────────────────────
        var forwardingItem = new ToolStripMenuItem("Start Forwarding")
        {
            Image   = TrayMenuIcons.Play(TrayIconSize, dimColor),
            Name    = "forwarding",
            Padding = TrayItemPadding,
        };
        forwardingItem.Click += (s, e) =>
        {
            if (_isForwarding)
                StopForwarding();
            else
                StartForwarding();
            UpdateTrayMenu();
        };
        menu.Items.Add(forwardingItem);

        // ── Connect to... (only when networking is available) ─────
        bool hasNetwork = _networkDiscovery is not NullNetworkDiscoveryService;
        if (hasNetwork)
        {
            var connectItem = new ToolStripMenuItem("Connect to...")
            {
                Image   = TrayMenuIcons.Network(TrayIconSize, dimColor),
                Name    = "connect",
                Padding = TrayItemPadding,
            };
            menu.Items.Add(connectItem);

            // Rebuild peer submenu each time the menu opens; also re-evaluate visibility
            menu.Opening += (s, e) =>
            {
                bool isClientRole = _appSettings.NetworkEnabled && _appSettings.NetworkRole == Models.NetworkRole.Client;
                connectItem.Visible = !isClientRole && _networkMode != NetworkInputMode.Receiving;
                if (_trayIcon.ContextMenuStrip?.Items["forwarding"] is ToolStripMenuItem fwd)
                    fwd.Visible = !isClientRole;
                if (connectItem.Visible) RefreshPeerSubmenu(connectItem);
            };
        }

        menu.Items.Add(new ToolStripSeparator());

        // ── Exit ─────────────────────────────────────────────────
        var exitItem = new ToolStripMenuItem("Exit Asteriq")
        {
            Image   = TrayMenuIcons.Exit(TrayIconSize, dimColor),
            Padding = TrayItemPadding,
        };
        exitItem.Click += (s, e) =>
        {
            _forceClose = true;
            Application.Exit();
        };
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (s, e) => ShowAndActivateWindow();
    }

    private void RefreshPeerSubmenu(ToolStripMenuItem connectItem)
    {
        connectItem.DropDownItems.Clear();

        var peers     = _networkDiscovery.KnownPeers.Values.ToList();
        var textColor = SkiaColorToGdi(FUIColors.TextPrimary);
        var dimColor  = SkiaColorToGdi(FUIColors.TextDim);

        if (peers.Count == 0)
        {
            connectItem.DropDownItems.Add(new ToolStripMenuItem("No peers discovered") { Enabled = false });
            return;
        }

        foreach (var peer in peers)
        {
            bool isConnected = _tabContext.ConnectedPeerIp == peer.IpAddress;
            string label     = isConnected
                ? $"Disconnect from {peer.MachineName}"
                : peer.MachineName;

            // CA2000: ToolStripMenuItem ownership transferred to DropDownItems which manages disposal
#pragma warning disable CA2000
            var peerItem = new ToolStripMenuItem(label)
            {
                Image = TrayMenuIcons.Monitor(TrayIconSize, isConnected ? SkiaColorToGdi(FUIColors.Active) : dimColor),
            };
#pragma warning restore CA2000

            var captured = peer;
            peerItem.Click += (s, e) =>
            {
                if (_tabContext.ConnectedPeerIp == captured.IpAddress)
                    _ = SwitchToLocalAsync();
                else
                    _ = ConnectAsMasterAsync(captured);
            };

            connectItem.DropDownItems.Add(peerItem);
        }
    }

    // (StartForwarding / StopForwarding moved to MainForm.Networking.cs)

    private void UpdateTrayMenu()
    {
        if (_trayIcon.ContextMenuStrip is null) return;

        var forwardingItem = _trayIcon.ContextMenuStrip.Items["forwarding"] as ToolStripMenuItem;
        if (forwardingItem is null) return;

        // Read from TabContext which is always up-to-date — _isForwarding may not
        // have been synced yet when controllers invoke this via delegate.
        if (_tabContext.IsForwarding)
        {
            forwardingItem.Text  = "Stop Forwarding";
            forwardingItem.Image = TrayMenuIcons.Stop(TrayIconSize, SkiaColorToGdi(FUIColors.Active));
        }
        else
        {
            forwardingItem.Text  = "Start Forwarding";
            forwardingItem.Image = TrayMenuIcons.Play(TrayIconSize, SkiaColorToGdi(FUIColors.TextDim));
        }

        // Connect to... and forwarding are irrelevant in Rx role
        bool isClientRole = _appSettings.NetworkEnabled && _appSettings.NetworkRole == Models.NetworkRole.Client;
        forwardingItem.Visible = !isClientRole;
        if (_trayIcon.ContextMenuStrip.Items["connect"] is ToolStripMenuItem connectItem)
            connectItem.Visible = !isClientRole && _networkMode != NetworkInputMode.Receiving;
    }

    private static Color SkiaColorToGdi(SkiaSharp.SKColor c) => Color.FromArgb(c.Red, c.Green, c.Blue);

    #endregion

    // (Networking methods moved to MainForm.Networking.cs)

    #region Cleanup

    private bool _forceClose = false;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // If close to tray is enabled and this isn't a forced close, minimize to tray instead
        if (_appSettings.CloseToTray && !_forceClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        // Save window state before closing
        if (WindowState == FormWindowState.Normal)
        {
            _windowState.SaveWindowState(Width, Height, Left, Top);
        }

        _renderTimer?.Stop();
        _inputService?.StopPolling();
        _inputService?.Dispose();
        _trayIcon?.Dispose();
        ShutdownNetworking();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemThemeChanged;
            _renderTimer?.Dispose();
            _customFormIcon?.Dispose();
            _background?.Dispose();
            _logoSvg?.Dispose();
            _joystickSvg?.Dispose();
            _throttleSvg?.Dispose();
            foreach (var svg in _svgCache.Values) svg.Dispose();
            _svgCache.Clear();
            foreach (var bmp in _bitmapCache.Values) bmp.Dispose();
            _bitmapCache.Clear();
            _settingsController?.Dispose();
            _networkVjoy?.Dispose();
            _heartbeatCts?.Dispose();
        }
        base.Dispose(disposing);
    }

    #endregion
}

// Extension method for SKRect
public static class SKRectExtensions
{
    public static SKRect Inset(this SKRect rect, float dx, float dy)
    {
        return new SKRect(rect.Left + dx, rect.Top + dy, rect.Right - dx, rect.Bottom - dy);
    }

    /// <summary>
    /// Safe hit-test: returns false for empty/unset bounds, true if point is inside.
    /// Replaces the repeated <c>!bounds.IsEmpty &amp;&amp; bounds.Contains(x, y)</c> pattern.
    /// </summary>
    public static bool HitTest(this SKRect bounds, float x, float y)
        => !bounds.IsEmpty && bounds.Contains(x, y);

    /// <summary>
    /// Safe hit-test using an SKPoint.
    /// </summary>
    public static bool HitTest(this SKRect bounds, SKPoint pt)
        => !bounds.IsEmpty && bounds.Contains(pt);
}
