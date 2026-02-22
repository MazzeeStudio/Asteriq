using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using Asteriq.UI.Controllers;
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
    private const uint SWP_NOACTIVATE = 0x0010;   // Does not activate window

    // DWM (Desktop Window Manager) for Windows 11 border color
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_BORDER_COLOR = 34;  // Windows 11+ only

    // Window sizing
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
    private SKControl _canvas = null!;
    private System.Windows.Forms.Timer _renderTimer = null!;
    private FUIBackground _background = new();
    private float _scanLineProgress = 0f;
    private float _dashPhase = 0f;
    private float _pulsePhase = 0f;
    private float _leadLineProgress = 0f;

    // Performance optimization
    private bool _isDirty = true;  // Force initial render
    private bool _enableAnimations = true;  // Can be toggled for performance
    private bool _isResizing = false;  // Suppress renders during resize

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
    private readonly string[] _tabNames = { "DEVICES", "MAPPINGS", "BINDINGS", "SETTINGS" };
    private float _tabsStartX; // cached each draw pass, used by HitTest

    // Window control hover state
    private int _hoveredWindowControl = -1;

    // SVG device silhouettes
    private SKSvg? _joystickSvg;
    private SKSvg? _throttleSvg;

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

    // Input forwarding state (physical → vJoy)
    private bool _isForwarding = false;

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
        ISCInstallationService scInstallationService,
        SCProfileCacheService scProfileCacheService,
        SCSchemaService scSchemaService,
        SCXmlExportService scExportService,
        SCExportProfileService scExportProfileService)
    {
        // Assign injected services
        _inputService = inputService ?? throw new ArgumentNullException(nameof(inputService));
        _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
        _profileRepository = profileRepository ?? throw new ArgumentNullException(nameof(profileRepository));
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _windowState = windowState ?? throw new ArgumentNullException(nameof(windowState));
        _vjoyService = vjoyService ?? throw new ArgumentNullException(nameof(vjoyService));
        _mappingEngine = mappingEngine ?? throw new ArgumentNullException(nameof(mappingEngine));
        _trayIcon = trayIcon ?? throw new ArgumentNullException(nameof(trayIcon));
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
            scExportProfileService ?? throw new ArgumentNullException(nameof(scExportProfileService)));
    }

    private void InitializeTabControllers(
        ISCInstallationService scInstallationService,
        SCProfileCacheService scProfileCacheService,
        SCSchemaService scSchemaService,
        SCXmlExportService scExportService,
        SCExportProfileService scExportProfileService)
    {
        _tabContext = new TabContext(
            _inputService, _profileManager, _profileRepository, _appSettings,
            _themeService, _vjoyService, _mappingEngine, _trayIcon,
            _activeInputTracker, _background, this,
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
        _tabContext.GetActiveSvg = GetActiveSvg;
        _tabContext.GetSvgForDeviceMap = GetSvgForDeviceMap;

        _settingsController = new SettingsTabController(_tabContext);
        _devicesController = new DevicesTabController(_tabContext);
        _mappingsController = new MappingsTabController(_tabContext);
        _scBindingsController = new SCBindingsTabController(
            _tabContext, scInstallationService, scProfileCacheService,
            scSchemaService, scExportService, scExportProfileService);
        _scBindingsController.Initialize();

        // Wire up mapping-related callbacks (now delegated to MappingsTabController)
        _tabContext.CreateOneToOneMappings = _mappingsController.CreateOneToOneMappingsPublic;
        _tabContext.ClearDeviceMappings = _mappingsController.ClearDeviceMappingsPublic;
        _tabContext.RemoveDisconnectedDevice = _mappingsController.RemoveDisconnectedDevicePublic;
        _tabContext.OpenMappingDialogForControl = _mappingsController.OpenMappingDialogForControlPublic;
    }

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

    private void SyncFromTabContext()
    {
        _backgroundDirty = _tabContext.BackgroundDirty;
        _isForwarding = _tabContext.IsForwarding;
        _selectedDevice = _tabContext.SelectedDevice;
        _currentInputState = _tabContext.CurrentInputState;
        _selectedVJoyDeviceIndex = _tabContext.SelectedVJoyDeviceIndex;
        _deviceMap = _tabContext.DeviceMap;
        _mappingsPrimaryDeviceMap = _tabContext.MappingsPrimaryDeviceMap;
        _hoveredControlId = _tabContext.HoveredControlId;
        _selectedControlId = _tabContext.SelectedControlId;
        _silhouetteBounds = _tabContext.SilhouetteBounds;
        _svgScale = _tabContext.SvgScale;
        _svgOffset = _tabContext.SvgOffset;
        _svgMirrored = _tabContext.SvgMirrored;
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
        FUIRenderer.FontSizeOption = _appSettings.FontSize;

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

        bool confirmed = FUIMessageBox.ShowQuestion(this,
            $"Delete profile '{profile.Name}'?\n\nThis cannot be undone.",
            "Delete Profile");
        if (!confirmed) return;

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
            foreach (var (path, map) in allMaps)
            {
                if (!string.IsNullOrEmpty(map.Device) &&
                    !map.Device.StartsWith("Generic", StringComparison.OrdinalIgnoreCase))
                {
                    if (deviceName.Contains(map.Device, StringComparison.OrdinalIgnoreCase) ||
                        map.Device.Contains(deviceName, StringComparison.OrdinalIgnoreCase))
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
    private DeviceMap? LoadDeviceMapForDeviceInfo(PhysicalDeviceInfo? device)
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

            // Try device name match
            foreach (var (path, map) in allMaps)
            {
                if (!string.IsNullOrEmpty(map.Device) &&
                    !map.Device.StartsWith("Generic", StringComparison.OrdinalIgnoreCase))
                {
                    if (deviceName.Contains(map.Device, StringComparison.OrdinalIgnoreCase) ||
                        map.Device.Contains(deviceName, StringComparison.OrdinalIgnoreCase))
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
    /// Get the appropriate SVG for a given device map
    /// </summary>
    private SKSvg? GetSvgForDeviceMap(DeviceMap? map)
    {
        if (map is null)
            return _joystickSvg;

        var svgFile = map.SvgFile?.ToLowerInvariant() ?? "";
        if (svgFile.Contains("throttle"))
            return _throttleSvg ?? _joystickSvg;

        return _joystickSvg;
    }

    /// <summary>
    /// Update the device map used for Mappings tab visualization based on vJoy primary device
    /// </summary>
    private void UpdateMappingsPrimaryDeviceMap()
    {
        _mappingsPrimaryDeviceMap = null;

        // Ensure vJoy devices are populated
        if (_vjoyDevices.Count == 0 && _vjoyService.IsInitialized)
        {
            _vjoyDevices = _vjoyService.EnumerateDevices();
        }

        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count)
            return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        var profile = _profileManager.ActiveProfile;
        if (profile is null)
            return;

        var primaryGuid = profile.GetPrimaryDeviceForVJoy(vjoyDevice.Id);
        if (string.IsNullOrEmpty(primaryGuid))
            return;

        // Find the physical device by GUID
        var primaryDevice = _devices.FirstOrDefault(d =>
            d.InstanceGuid.ToString().Equals(primaryGuid, StringComparison.OrdinalIgnoreCase));

        if (primaryDevice is not null)
        {
            _mappingsPrimaryDeviceMap = LoadDeviceMapForDeviceInfo(primaryDevice);
        }
    }

    /// <summary>
    /// Called when mappings are created or removed to update primary device detection
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
    }

    /// <summary>
    /// Detect device type from device name using common keywords
    /// </summary>
    private string DetectDeviceType(string deviceName)
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

    /// <summary>
    /// Get the appropriate SVG for the current device map
    /// </summary>
    private SKSvg? GetActiveSvg()
    {
        if (_deviceMap is null)
            return _joystickSvg;

        // Check the device map's svgFile field
        var svgFile = _deviceMap.SvgFile?.ToLowerInvariant() ?? "";

        if (svgFile.Contains("throttle"))
            return _throttleSvg ?? _joystickSvg;

        // Default to joystick
        return _joystickSvg;
    }

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

    private SKRect? CalculateGroupBounds(XElement group, XNamespace svg)
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

    private SKRect? GetPathApproximateBounds(string d, float tx, float ty)
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
        MinimumSize = new Size(1570, 1000);  // Increased to fit SC Bindings panel content
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
    }

    private void LoadApplicationIcon()
    {
        try
        {
            // Load the application icon from the asteriq.ico file
            // This is in the same directory as the exe
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "asteriq.ico");

            if (File.Exists(iconPath))
            {
                Icon = new Icon(iconPath);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            // Icon loading failed, continue without icon
            // The app will still work, just won't show icon in taskbar
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

    private void InitializeCanvas()
    {
        _canvas = new SKControl
        {
            Dock = DockStyle.Fill
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
        _inputService.StartPolling(100);
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
            _background.Update(0.016f);

            needsUpdate = true;
        }

        // Update active input animations (~16ms per tick = 0.016s)
        // Always update these as they track real device input
        if (_activeInputTracker.UpdateAnimations(0.016f))
        {
            needsUpdate = true;
        }

        // SC Bindings tab tick (input listening check)
        if (_activeTab == 2)
        {
            SyncTabContext();
            _scBindingsController.OnTick();
            SyncFromTabContext();
        }

        // Only invalidate if animations ran or something is dirty, and we're not resizing
        if ((needsUpdate || _isDirty) && !_isResizing)
        {
            _canvas.Invalidate();
            _isDirty = false;
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
            if (_selectedDevice >= 0)
            {
                LoadDeviceMapForDevice(_devices[_selectedDevice]);
            }
        }
    }

    private void OnInputReceived(object? sender, DeviceInputState state)
    {
        // Forward input to vJoy if forwarding is active
        if (_isForwarding && _mappingEngine.IsRunning)
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

    private string GetAxisBindingName(int axisIndex)
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

    #region Window Chrome

    protected override void WndProc(ref Message m)
    {
        // Handle single-instance activation request
        if (m.Msg == SingleInstanceManager.ActivationMessage)
        {
            ShowAndActivateWindow();
            return;
        }

        // Handle WM_NCCALCSIZE to remove title bar but keep resize borders
        if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            // Return 0 to use entire window rectangle as client area
            // This removes the title bar but keeps resize borders
            m.Result = IntPtr.Zero;
            return;
        }

        // Intercept maximize/restore commands and handle manually
        if (m.Msg == WM_SYSCOMMAND)
        {
            int command = (int)m.WParam & 0xFFF0;
            if (command == SC_MAXIMIZE)
            {
                MaximizeWindow();
                return;  // Prevent Windows from handling it
            }
            else if (command == SC_RESTORE && _isManuallyMaximized)
            {
                // Only intercept restore if we're in our manual maximized state
                // Otherwise let Windows handle restore from minimize
                RestoreWindow();
                return;  // Prevent Windows from handling it
            }
        }
        else if (m.Msg == WM_NCHITTEST)
        {
            var result = HitTest(PointToClient(Cursor.Position));
            if (result != HTCLIENT)
            {
                m.Result = (IntPtr)result;
                return;
            }
        }
        else if (m.Msg == WM_ENTERSIZEMOVE)
        {
            // User started resizing or moving - suppress renders during drag
            _isResizing = true;
        }
        else if (m.Msg == WM_EXITSIZEMOVE)
        {
            // Resize/move finished - mark dirty and resume rendering
            _isResizing = false;
            _backgroundDirty = true;  // Background needs regeneration at new size
            MarkDirty();
        }
        else if (m.Msg == WM_SIZE)
        {
            // Window size changed - mark dirty for redraw
            _backgroundDirty = true;  // Background needs regeneration at new size
            MarkDirty();
        }

        base.WndProc(ref m);
    }

    /// <summary>
    /// Show and activate the window, restoring from minimized or tray state if needed.
    /// </summary>
    private void ShowAndActivateWindow()
    {
        Show();
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }
        Activate();
        BringToFront();
    }

    /// <summary>
    /// Mark the canvas as dirty, requiring a redraw on next animation tick
    /// </summary>
    private void MarkDirty()
    {
        _isDirty = true;
    }

    private void MaximizeWindow()
    {
        if (_isManuallyMaximized) return;

        // Store current bounds for restore
        _restoreBounds = new Rectangle(Location, Size);

        // Get the working area of the screen the window is currently on
        var screen = Screen.FromHandle(Handle);
        var workingArea = screen.WorkingArea;

        // Use SetWindowPos with SWP_NOCOPYBITS to prevent ghost window
        // This moves and resizes in one atomic operation without copying old pixels
        _isManuallyMaximized = true;
        SetWindowPos(
            Handle,
            IntPtr.Zero,
            workingArea.X,
            workingArea.Y,
            workingArea.Width,
            workingArea.Height,
            SWP_NOCOPYBITS | SWP_NOZORDER | SWP_FRAMECHANGED
        );
    }

    private void RestoreWindow()
    {
        if (!_isManuallyMaximized) return;

        // Restore to previous bounds using SetWindowPos to prevent ghost window
        _isManuallyMaximized = false;
        SetWindowPos(
            Handle,
            IntPtr.Zero,
            _restoreBounds.X,
            _restoreBounds.Y,
            _restoreBounds.Width,
            _restoreBounds.Height,
            SWP_NOCOPYBITS | SWP_NOZORDER | SWP_FRAMECHANGED
        );
    }

    private int HitTest(Point clientPoint)
    {
        bool left = clientPoint.X < ResizeBorder;
        bool right = clientPoint.X >= ClientSize.Width - ResizeBorder;
        bool top = clientPoint.Y < ResizeBorder;
        bool bottom = clientPoint.Y >= ClientSize.Height - ResizeBorder;

        if (top && left) return HTTOPLEFT;
        if (top && right) return HTTOPRIGHT;
        if (bottom && left) return HTBOTTOMLEFT;
        if (bottom && right) return HTBOTTOMRIGHT;
        if (left) return HTLEFT;
        if (right) return HTRIGHT;
        if (top) return HTTOP;
        if (bottom) return HTBOTTOM;

        // Title bar area for dragging (but not over buttons or tabs)
        if (clientPoint.Y < TitleBarHeight)
        {
            // Exclude window controls area
            if (clientPoint.X >= ClientSize.Width - 120)
            {
                return HTCLIENT;
            }
            // Exclude tab area - use cached value from last draw pass
            if (_tabsStartX > 0 && clientPoint.X >= _tabsStartX && clientPoint.Y >= 36 && clientPoint.Y <= 66)
            {
                return HTCLIENT;
            }
            return HTCAPTION;
        }

        return HTCLIENT;
    }

    #endregion

    #region Mouse Handling

    private void OnCanvasMouseMove(object? sender, MouseEventArgs e)
    {
        // Store mouse position for debug display
        _mousePosition = e.Location;

        // Handle SC Bindings scrollbar dragging (delegated to SC Bindings controller)
        if (_scBindingsController.IsDraggingVScroll || _scBindingsController.IsDraggingHScroll)
        {
            SyncTabContext();
            _scBindingsController.OnMouseMove(e);
            SyncFromTabContext();
            return;
        }

        // Handle background slider dragging (delegated to Settings controller)
        if (_settingsController.IsDraggingSlider)
        {
            SyncTabContext();
            _settingsController.OnMouseMove(e);
            SyncFromTabContext();
            return;
        }

        // Handle device list drag-to-reorder (delegated to Devices controller)
        if (_activeTab == 0 && _devicesController.HasPendingDrag)
        {
            SyncTabContext();
            _devicesController.OnMouseMove(e);
            SyncFromTabContext();
            if (_devicesController.IsDraggingDevice)
                return;
        }

        // Mappings tab hover handling (delegated to controller)
        if (_activeTab == 1)
        {
            // Handle dragging that needs priority dispatch
            if (_mappingsController.IsDraggingCurve || _mappingsController.IsDraggingDeadzone ||
                _mappingsController.IsDraggingDuration)
            {
                SyncTabContext();
                _mappingsController.OnMouseMove(e);
                SyncFromTabContext();
                return;
            }

            SyncTabContext();
            _mappingsController.OnMouseMove(e);
            SyncFromTabContext();
        }

        // Profile dropdown hover detection
        if (_profileDropdownOpen && _profileDropdownBounds.Contains(e.X, e.Y))
        {
            float itemHeight = 24f;
            int itemIndex = (int)((e.Y - _profileDropdownBounds.Top - 2) / itemHeight);
            _hoveredProfileIndex = itemIndex;
            Cursor = Cursors.Hand;
            return;
        }
        else
        {
            _hoveredProfileIndex = -1;
        }

        // SC Bindings tab hover detection (delegated to SC Bindings controller)
        if (_activeTab == 2)
        {
            SyncTabContext();
            _scBindingsController.OnMouseMove(e);
            SyncFromTabContext();
        }

        // Update cursor based on hit test (for resize feedback)
        int hitResult = HitTest(e.Location);
        switch (hitResult)
        {
            case HTLEFT:
            case HTRIGHT:
                Cursor = Cursors.SizeWE;
                break;
            case HTTOP:
            case HTBOTTOM:
                Cursor = Cursors.SizeNS;
                break;
            case HTTOPLEFT:
            case HTBOTTOMRIGHT:
                Cursor = Cursors.SizeNWSE;
                break;
            case HTTOPRIGHT:
            case HTBOTTOMLEFT:
                Cursor = Cursors.SizeNESW;
                break;
            default:
                Cursor = Cursors.Default;
                break;
        }

        // Devices tab hover handling (delegated to controller)
        if (_activeTab == 0)
        {
            SyncTabContext();
            _devicesController.OnMouseMove(e);
            SyncFromTabContext();
        }

        // (Mapping category tab hover detection moved to MappingsTabController.OnMouseMove)

        // Window controls hover (matches FUIRenderer.DrawWindowControls sizing)
        float pad = FUIRenderer.SpaceLG;  // Standard padding for window controls
        float btnSize = FUIRenderer.TouchTargetCompact;  // 32px - matches DrawWindowControls
        float btnGap = FUIRenderer.SpaceSM;  // 8px
        float btnTotalWidth = btnSize * 3 + btnGap * 2; // 112px with 32px buttons
        float windowControlsX = ClientSize.Width - pad - btnTotalWidth; // Align with page padding
        float titleBarY = FUIRenderer.TitleBarPadding;  // 16px
        if (e.Y >= titleBarY + 12 && e.Y <= titleBarY + FUIRenderer.TitleBarHeightExpanded)
        {
            float relX = e.X - windowControlsX;

            if (relX >= 0 && relX < btnSize) _hoveredWindowControl = 0;
            else if (relX >= btnSize + btnGap && relX < btnSize * 2 + btnGap) _hoveredWindowControl = 1;
            else if (relX >= (btnSize + btnGap) * 2 && relX < btnSize * 3 + btnGap * 2) _hoveredWindowControl = 2;
            else _hoveredWindowControl = -1;
        }
        else
        {
            _hoveredWindowControl = -1;
        }

    }

    private void OnCanvasMouseDown(object? sender, MouseEventArgs e)
    {
        // Handle right-click
        if (e.Button == MouseButtons.Right)
        {
            // Right-click on SC Bindings tab (delegated to SC Bindings controller)
            if (_activeTab == 2)
            {
                SyncTabContext();
                _scBindingsController.OnMouseDown(e);
                SyncFromTabContext();
                return;
            }

            // Right-click on Mappings tab (delegated to controller)
            if (_activeTab == 1)
            {
                SyncTabContext();
                _mappingsController.OnMouseDown(e);
                SyncFromTabContext();
            }
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        // Profile dropdown clicks (must be handled first when dropdown is open)
        if (_profileDropdownOpen)
        {
            if (_profileDropdownBounds.Contains(e.X, e.Y))
            {
                // Click on dropdown item
                if (_hoveredProfileIndex >= 0 && _hoveredProfileIndex < _profiles.Count)
                {
                    // Select existing profile
                    _profileManager.ActivateProfile(_profiles[_hoveredProfileIndex].Id);
                    // Initialize primary devices for migration of old profiles
                    _profileManager.ActiveProfile?.UpdateAllPrimaryDevices();
                    UpdateMappingsPrimaryDeviceMap();
                    _profileDropdownOpen = false;
                    return;
                }
                else if (_hoveredProfileIndex == _profiles.Count)
                {
                    // "New Profile" clicked
                    CreateNewProfilePrompt();
                    _profileDropdownOpen = false;
                    return;
                }
                else if (_hoveredProfileIndex == _profiles.Count + 1)
                {
                    // "Import" clicked
                    ImportProfilePrompt();
                    _profileDropdownOpen = false;
                    return;
                }
                else if (_hoveredProfileIndex == _profiles.Count + 2)
                {
                    // "Export" clicked
                    ExportActiveProfile();
                    _profileDropdownOpen = false;
                    return;
                }
            }
            else
            {
                // Click outside dropdown - close it
                _profileDropdownOpen = false;
                return;
            }
        }

        // Profile selector click (toggle dropdown)
        if (_profileSelectorBounds.Contains(e.X, e.Y))
        {
            _profileDropdownOpen = !_profileDropdownOpen;
            if (_profileDropdownOpen)
            {
                RefreshProfileList();
            }
            return;
        }

        // Window controls
        if (_hoveredWindowControl >= 0)
        {
            switch (_hoveredWindowControl)
            {
                case 0:
                    WindowState = FormWindowState.Minimized;
                    break;
                case 1:
                    // Toggle manual maximize/restore
                    if (_isManuallyMaximized)
                        RestoreWindow();
                    else
                        MaximizeWindow();
                    break;
                case 2:
                    Close();
                    break;
            }
            return;
        }

        // Check for window dragging/resizing
        int hitResult = HitTest(e.Location);
        if (hitResult != HTCLIENT)
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, hitResult, 0);
            return;
        }

        // Devices tab click handling (delegated to controller)
        if (_activeTab == 0)
        {
            SyncTabContext();
            _devicesController.OnMouseDown(e);
            SyncFromTabContext();
        }

        // Mapping category tab clicks (delegated to controller)
        // (handled within MappingsTabController.OnMouseDown)

        // Tab clicks - must match positions calculated in DrawTitleBar exactly
        float pad = FUIRenderer.SpaceLG;
        float btnTotalWidth = 28f * 3 + 8f * 2; // Window control buttons
        float windowControlsX = ClientSize.Width - pad - btnTotalWidth;
        float tabWindowGap = FUIRenderer.Space2XL;          // matches draw code
        float tabGap = FUIRenderer.ScaleSpacing(16f);       // matches draw code

        using var tabMeasurePaint = FUIRenderer.CreateTextPaint(FUIColors.TextDim, FUIRenderer.ScaleFont(13f));
        var visibleTabs = GetVisibleTabIndices();
        float[] tabWidths = new float[_tabNames.Length];    // keyed by semantic index
        float totalTabsWidth = 0;
        for (int vi = 0; vi < visibleTabs.Length; vi++)
        {
            int i = visibleTabs[vi];
            tabWidths[i] = tabMeasurePaint.MeasureText(_tabNames[i]);
            totalTabsWidth += tabWidths[i];
            if (vi < visibleTabs.Length - 1) totalTabsWidth += tabGap;
        }
        float tabStartX = windowControlsX - tabWindowGap - totalTabsWidth;
        float tabY = 16;

        if (e.Y >= tabY + 20 && e.Y <= tabY + 50)
        {
            float tabX = tabStartX;
            for (int vi = 0; vi < visibleTabs.Length; vi++)
            {
                int i = visibleTabs[vi];
                float tabHitWidth = tabWidths[i] + (vi < visibleTabs.Length - 1 ? tabGap / 2 : 0);
                if (e.X >= tabX && e.X < tabX + tabHitWidth)
                {
                    if (_activeTab != i)
                    {
                        if (_activeTab == 1) _mappingsController.OnDeactivated();
                        if (i == 1) _mappingsController.OnActivated();
                    }
                    _activeTab = i;
                    break;
                }
                tabX += tabWidths[i] + tabGap;
            }
        }

        // Settings tab click handling
        if (_activeTab == 3)
        {
            SyncTabContext();
            _settingsController.OnMouseDown(e);
            SyncFromTabContext();
        }

        // Bindings (SC) tab click handling (delegated to SC Bindings controller)
        if (_activeTab == 2)
        {
            SyncTabContext();
            _scBindingsController.OnMouseDown(e);
            SyncFromTabContext();
            return;
        }

        // Mappings tab click handling (delegated to controller)
        if (_activeTab == 1)
        {
            SyncTabContext();
            _mappingsController.OnMouseDown(e);
            SyncFromTabContext();
        }

        // (SVG control clicks handled by DevicesTabController)
    }

    private void OnCanvasMouseLeave(object? sender, EventArgs e)
    {
        _hoveredWindowControl = -1;
        _hoveredControlId = null;
        _mappingsController.OnMouseLeave();
        _devicesController.OnMouseLeave();
        _scBindingsController.OnMouseLeave();
    }

    private void OnCanvasMouseUp(object? sender, MouseEventArgs e)
    {
        // Device drag-to-reorder release (delegated to Devices controller)
        if (_devicesController.HasPendingDrag || _devicesController.IsDraggingDevice)
        {
            SyncTabContext();
            _devicesController.OnMouseUp(e);
            SyncFromTabContext();
            if (_devicesController.IsDraggingDevice)
                return; // Was still dragging, now released
        }

        // Release SC Bindings scrollbar dragging (delegated to SC Bindings controller)
        if (_scBindingsController.IsDraggingVScroll || _scBindingsController.IsDraggingHScroll)
        {
            SyncTabContext();
            _scBindingsController.OnMouseUp(e);
            SyncFromTabContext();
        }

        // Release mapping drag operations (delegated to Mappings controller)
        if (_mappingsController.IsDraggingCurve || _mappingsController.IsDraggingDeadzone ||
            _mappingsController.IsDraggingDuration)
        {
            SyncTabContext();
            _mappingsController.OnMouseUp(e);
            SyncFromTabContext();
        }

        // Release background slider dragging (delegated to Settings controller)
        if (_settingsController.IsDraggingSlider)
        {
            SyncTabContext();
            _settingsController.OnMouseUp(e);
            SyncFromTabContext();
        }
    }

    private void OnCanvasMouseWheel(object? sender, MouseEventArgs e)
    {
        // Handle scroll on SC Bindings tab (delegated to SC Bindings controller)
        if (_activeTab == 2)
        {
            SyncTabContext();
            _scBindingsController.OnMouseWheel(e);
            SyncFromTabContext();
            return;
        }

        // Handle scroll on MAPPINGS tab (delegated to controller)
        if (_activeTab == 1)
        {
            SyncTabContext();
            _mappingsController.OnMouseWheel(e);
            SyncFromTabContext();
        }
    }

    // Returns semantic tab indices (0=DEVICES,1=MAPPINGS,2=BINDINGS,3=SETTINGS) that are
    // currently visible. MAPPINGS is hidden when vJoy is not available.
    private int[] GetVisibleTabIndices() =>
        _vjoyService.IsInitialized
            ? new[] { 0, 1, 2, 3 }
            : new[] { 0, 2, 3 };

    private string? HitTestSvg(SKPoint screenPoint)
    {
        if (_joystickSvg?.Picture is null || _controlBounds.Count == 0) return null;

        // Transform screen coordinates to SVG coordinates
        float svgX = (screenPoint.X - _svgOffset.X) / _svgScale;
        float svgY = (screenPoint.Y - _svgOffset.Y) / _svgScale;

        // Check each control's bounds
        foreach (var (controlId, bounds) in _controlBounds)
        {
            if (bounds.Contains(svgX, svgY))
            {
                return controlId;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the column index at the given X coordinate in SC Bindings grid, or -1 if not in a device column
    /// </summary>
    #endregion

    #region Rendering

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var bounds = new SKRect(0, 0, e.Info.Width, e.Info.Height);

        // Clear to void
        canvas.Clear(FUIColors.Void);

        // Layer 0: Background grid (cached for performance)
        DrawBackgroundLayer(canvas, bounds);

        // Layer 1: Main structure panels
        DrawStructureLayer(canvas, bounds);

        // Layer 2: Overlay effects
        DrawOverlayLayer(canvas, bounds);
    }

    private void DrawBackgroundLayer(SKCanvas canvas, SKRect bounds)
    {
        int width = (int)bounds.Width;
        int height = (int)bounds.Height;

        // Check if we need to regenerate the background cache
        if (_backgroundDirty || _cachedBackground is null ||
            _cachedBackground.Width != width || _cachedBackground.Height != height)
        {
            // Dispose old cache if size changed
            if (_cachedBackground is not null && (_cachedBackground.Width != width || _cachedBackground.Height != height))
            {
                _cachedBackground.Dispose();
                _cachedBackground = null;
            }

            // Create new bitmap if needed
            _cachedBackground ??= new SKBitmap(width, height);

            // Render background to cache
            using var cacheSurface = SKSurface.Create(new SKImageInfo(width, height));
            var cacheCanvas = cacheSurface.Canvas;
            cacheCanvas.Clear(FUIColors.Void);

            // Render FUI background with all effects to cache
            _background.Render(cacheCanvas, bounds);

            // Copy to cached bitmap
            using var image = cacheSurface.Snapshot();
            using var pixmap = image.PeekPixels();
            pixmap.ReadPixels(_cachedBackground.Info, _cachedBackground.GetPixels(), _cachedBackground.RowBytes);

            _backgroundDirty = false;
        }

        // Draw cached background
        canvas.DrawBitmap(_cachedBackground, 0, 0);
    }

    private void DrawStructureLayer(SKCanvas canvas, SKRect bounds)
    {
        // Title bar
        DrawTitleBar(canvas, bounds);

        // Main content area - all values 4px aligned
        float pad = FUIRenderer.SpaceXL;  // 24px
        float contentTop = 88;  // 4px aligned
        float contentBottom = bounds.Bottom - 56;  // 4px aligned

        // Calculate responsive panel widths based on window size
        // Side-tabbed panels (Devices, Mappings) use reduced left padding
        float sideTabPad = FUIRenderer.SpaceSM;  // 8px
        float contentWidth = bounds.Width - sideTabPad - pad;
        var layout = FUIRenderer.CalculateLayout(contentWidth, minLeftPanel: 360f, minRightPanel: 280f);

        float leftPanelWidth = layout.LeftPanelWidth;
        float rightPanelWidth = layout.RightPanelWidth;
        float gap = layout.Gutter;
        float centerStart = sideTabPad + leftPanelWidth + gap;
        float centerEnd = layout.ShowRightPanel
            ? bounds.Right - pad - rightPanelWidth - gap
            : bounds.Right - pad;

        // Content based on active tab
        if (_activeTab == 1) // MAPPINGS tab
        {
            SyncTabContext();
            _mappingsController.Draw(canvas, bounds, sideTabPad, contentTop, contentBottom);
            SyncFromTabContext();
        }
        else if (_activeTab == 2) // BINDINGS tab (Star Citizen integration)
        {
            SyncTabContext();
            _scBindingsController.Draw(canvas, bounds, pad, contentTop, contentBottom);
            SyncFromTabContext();
        }
        else if (_activeTab == 3) // SETTINGS tab
        {
            SyncTabContext();
            _settingsController.Draw(canvas, bounds, pad, contentTop, contentBottom);
            SyncFromTabContext();
        }
        else
        {
            // Tab 0: DEVICES tab (delegated to controller)
            SyncTabContext();
            _devicesController.Draw(canvas, bounds, sideTabPad, contentTop, contentBottom);
            SyncFromTabContext();
        }

        // Status bar
        DrawStatusBar(canvas, bounds);

        // Draw dropdowns last (on top of everything)
        DrawOpenDropdowns(canvas);
    }

    private void DrawOpenDropdowns(SKCanvas canvas)
    {
        // Profile dropdown (rendered on top of all panels)
        if (_profileDropdownOpen)
        {
            // Get position from profile selector bounds with small gap
            DrawProfileDropdown(canvas, _profileSelectorBounds.Left, _profileSelectorBounds.Bottom + 8);
        }
    }

    private void DrawSelector(SKCanvas canvas, SKRect bounds, string text, bool isHovered, bool isEnabled)
        => FUIWidgets.DrawSelector(canvas, bounds, text, isHovered, isEnabled);

    private void DrawTextFieldReadOnly(SKCanvas canvas, SKRect bounds, string text, bool isHovered)
        => FUIWidgets.DrawTextFieldReadOnly(canvas, bounds, text, isHovered);



    private void DrawTitleBar(SKCanvas canvas, SKRect bounds)
    {
        float titleBarY = FUIRenderer.TitleBarPadding;  // 16px - was 15
        // Note: Title bar uses TitleBarHeightExpanded (48px) for the full title area
        float pad = FUIRenderer.SpaceLG;

        // Title text - aligned with left panel L-corner frame
        // Panel starts at sideTabPad(8) + sideTabWidth(28) = 36
        float titleX = 36f;

        // Measure actual title width (title uses scaled font)
        using var titlePaint = FUIRenderer.CreateTextPaint(FUIColors.Primary, FUIRenderer.ScaleFont(26f));
        float titleWidth = titlePaint.MeasureText("ASTERIQ");
        FUIRenderer.DrawText(canvas, "ASTERIQ", new SKPoint(titleX, titleBarY + 38), FUIColors.Primary, 26f, true);

        // Window controls - always at fixed position from right edge
        // 3 buttons at 32px + 2 gaps at 8px = 112px
        float btnTotalWidth = FUIRenderer.TouchTargetCompact * 3 + FUIRenderer.SpaceSM * 2;
        float windowControlsX = bounds.Right - pad - btnTotalWidth;

        // Navigation tabs - positioned with gap from window controls
        float tabWindowGap = FUIRenderer.Space2XL;  // 32px - was 40f
        float tabGap = FUIRenderer.ScaleSpacing(16f);  // 16px - was 15f
        using var tabMeasurePaint = FUIRenderer.CreateTextPaint(FUIColors.TextDim, FUIRenderer.ScaleFont(13f));

        // Calculate total tabs width by measuring each visible tab
        var visibleTabs = GetVisibleTabIndices();
        float[] tabWidths = new float[_tabNames.Length]; // keyed by semantic index
        float totalTabsWidth = 0;
        for (int vi = 0; vi < visibleTabs.Length; vi++)
        {
            int i = visibleTabs[vi];
            tabWidths[i] = tabMeasurePaint.MeasureText(_tabNames[i]);
            totalTabsWidth += tabWidths[i];
            if (vi < visibleTabs.Length - 1) totalTabsWidth += tabGap;
        }
        float tabStartX = windowControlsX - tabWindowGap - totalTabsWidth;
        _tabsStartX = tabStartX; // cache for HitTest

        // Left side elements positioning - measure actual widths
        float elementGap = 20f;  // Gap between title/subtitle/profile selector
        float subtitleX = titleX + titleWidth + elementGap;

        // Measure subtitle width
        using var subtitlePaint = FUIRenderer.CreateTextPaint(FUIColors.TextDim, FUIRenderer.ScaleFont(12f));
        float subtitleWidth = subtitlePaint.MeasureText("UNIFIED HOTAS MANAGEMENT SYSTEM");

        // Profile selector width scales slightly with font
        float profileSelectorWidth = FUIRenderer.ScaleSpacing(140f);
        float profileGap = 15f;

        // Subtitle - show if there's room before tabs (need space for separator line too)
        float separatorWidth = 30f; // Space for separator line
        bool showSubtitle = subtitleX + separatorWidth + subtitleWidth + elementGap + profileSelectorWidth + profileGap < tabStartX;

        // Profile selector position - after subtitle (or after title if no subtitle)
        float profileSelectorX;
        if (showSubtitle)
        {
            profileSelectorX = subtitleX + separatorWidth + subtitleWidth + elementGap;
        }
        else
        {
            profileSelectorX = titleX + titleWidth + elementGap;
        }

        // Check if profile selector fits before tabs
        bool showProfileSelector = profileSelectorX + profileSelectorWidth + profileGap < tabStartX;

        // Draw subtitle if there's room
        if (showSubtitle)
        {
            using (var sepPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.Frame,
                StrokeWidth = 1f,
                IsAntialias = true
            })
            {
                canvas.DrawLine(subtitleX + 10, titleBarY + 18, subtitleX + 10, titleBarY + 48, sepPaint);
            }
            FUIRenderer.DrawText(canvas, "UNIFIED HOTAS MANAGEMENT SYSTEM", new SKPoint(subtitleX + separatorWidth, titleBarY + 38),
                FUIColors.TextDim, 12f);
        }

        // Profile selector (on the left, after subtitle or title)
        if (showProfileSelector)
        {
            DrawProfileSelector(canvas, profileSelectorX, titleBarY + 16, profileSelectorWidth);
        }

        // Draw navigation tabs (only visible ones)
        float tabX = tabStartX;
        for (int vi = 0; vi < visibleTabs.Length; vi++)
        {
            int i = visibleTabs[vi];
            bool isActive = i == _activeTab;
            var tabColor = isActive ? FUIColors.Active : FUIColors.TextDim;

            FUIRenderer.DrawText(canvas, _tabNames[i], new SKPoint(tabX, titleBarY + 38), tabColor, 13f);

            if (isActive)
            {
                using var paint = new SKPaint
                {
                    Color = FUIColors.Active,
                    StrokeWidth = 2f,
                    IsAntialias = true
                };
                canvas.DrawLine(tabX, titleBarY + 44, tabX + tabWidths[i], titleBarY + 44, paint);

                using var glowPaint = new SKPaint
                {
                    Color = FUIColors.ActiveGlow,
                    StrokeWidth = 6f,
                    ImageFilter = SKImageFilter.CreateBlur(4f, 4f)
                };
                canvas.DrawLine(tabX, titleBarY + 44, tabX + tabWidths[i], titleBarY + 44, glowPaint);
            }

            tabX += tabWidths[i] + tabGap;
        }

        // Window controls - always drawn
        FUIRenderer.DrawWindowControls(canvas, windowControlsX, titleBarY + 12,
            _hoveredWindowControl == 0, _hoveredWindowControl == 1, _hoveredWindowControl == 2);
    }

    private void DrawProfileSelector(SKCanvas canvas, float x, float y, float width)
    {
        float height = FUIRenderer.ScaleLineHeight(26f);
        _profileSelectorBounds = new SKRect(x, y, x + width, y + height);

        // Get profile name
        string profileName = _profileManager.HasActiveProfile
            ? _profileManager.ActiveProfile!.Name
            : "No Profile";

        // Measure text to determine truncation (reserve space for arrow on right)
        float arrowWidth = 12f;
        float maxTextWidth = width - arrowWidth - 15f; // Space for arrow and padding
        using var measurePaint = FUIRenderer.CreateTextPaint(FUIColors.TextPrimary, FUIRenderer.ScaleFont(11f));
        float textWidth = measurePaint.MeasureText(profileName);

        // Truncate if too long (based on actual measurement)
        if (textWidth > maxTextWidth)
        {
            while (profileName.Length > 1 && measurePaint.MeasureText(profileName + "…") > maxTextWidth)
            {
                profileName = profileName.Substring(0, profileName.Length - 1);
            }
            profileName += "…";
        }

        // Background
        bool isHovered = _profileSelectorBounds.Contains(_mousePosition.X, _mousePosition.Y);
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = isHovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background1.WithAlpha(150),
            IsAntialias = true
        };
        canvas.DrawRect(_profileSelectorBounds, bgPaint);

        // Border
        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = _profileDropdownOpen ? FUIColors.Active : (isHovered ? FUIColors.FrameBright : FUIColors.Frame),
            StrokeWidth = 1f,
            IsAntialias = true
        };
        canvas.DrawRect(_profileSelectorBounds, borderPaint);

        // Profile name text
        float textY = y + height / 2 + 4;
        FUIRenderer.DrawText(canvas, profileName, new SKPoint(x + 8, textY),
            _profileDropdownOpen ? FUIColors.Active : FUIColors.TextPrimary, 11f);

        // Dropdown arrow on right side (custom drawn triangle)
        float arrowSize = 4f;
        float arrowX = x + width - 12f;
        float arrowY = y + height / 2;
        var arrowColor = _profileDropdownOpen ? FUIColors.Active : FUIColors.TextPrimary;

        using var arrowPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = arrowColor,
            IsAntialias = true
        };

        using var arrowPath = new SKPath();
        arrowPath.MoveTo(arrowX - arrowSize, arrowY - arrowSize / 2);  // Top left
        arrowPath.LineTo(arrowX + arrowSize, arrowY - arrowSize / 2);  // Top right
        arrowPath.LineTo(arrowX, arrowY + arrowSize / 2);              // Bottom center
        arrowPath.Close();
        canvas.DrawPath(arrowPath, arrowPaint);

        // Note: Dropdown is drawn separately in DrawOpenDropdowns() to render on top of all panels
    }

    private void DrawProfileDropdown(SKCanvas canvas, float x, float y)
    {
        float itemHeight = 28f;  // 4px aligned
        float width = 150f;
        float padding = 8f;
        int itemCount = Math.Max(_profiles.Count + 3, 4); // +3 for "New Profile", "Import", "Export", minimum 4
        float height = itemHeight * itemCount + padding * 2 + 2; // Extra for separator

        _profileDropdownBounds = new SKRect(x, y, x + width, y + height);

        // Drop shadow with glow effect
        FUIRenderer.DrawPanelShadow(canvas, _profileDropdownBounds, 4f, 4f, 15f);

        // Outer glow (subtle)
        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active.WithAlpha(30),
            StrokeWidth = 3f,
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(4f, 4f)
        };
        canvas.DrawRect(_profileDropdownBounds, glowPaint);

        // Solid opaque background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Void,
            IsAntialias = true
        };
        canvas.DrawRect(_profileDropdownBounds, bgPaint);

        // Inner background with slight gradient feel
        using var innerBgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background0,
            IsAntialias = true
        };
        canvas.DrawRect(new SKRect(x + 2, y + 2, x + width - 2, y + height - 2), innerBgPaint);

        // L-corner frame (FUI style)
        FUIRenderer.DrawLCornerFrame(canvas, _profileDropdownBounds, FUIColors.Active.WithAlpha(180), 20f, 6f, 1.5f, true);

        // Draw profile items
        float itemY = y + padding;
        for (int i = 0; i < _profiles.Count; i++)
        {
            var profile = _profiles[i];
            var itemBounds = new SKRect(x + 4, itemY, x + width - 4, itemY + itemHeight);
            bool isHovered = _hoveredProfileIndex == i;
            bool isActive = _profileManager.ActiveProfile?.Id == profile.Id;

            // Hover background with FUI glow
            if (isHovered)
            {
                using var hoverPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = FUIColors.Active.WithAlpha(40),
                    IsAntialias = true
                };
                canvas.DrawRect(itemBounds, hoverPaint);

                // Left accent bar on hover
                using var accentPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = FUIColors.Active,
                    IsAntialias = true
                };
                canvas.DrawRect(new SKRect(x + 4, itemY + 2, x + 6, itemY + itemHeight - 2), accentPaint);
            }

            // Active indicator (always show for active profile)
            if (isActive && !isHovered)
            {
                using var activePaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = FUIColors.Active.WithAlpha(60),
                    IsAntialias = true
                };
                canvas.DrawRect(new SKRect(x + 4, itemY + 2, x + 6, itemY + itemHeight - 2), activePaint);
            }

            // Profile name
            string name = profile.Name;
            if (name.Length > 14)
                name = name.Substring(0, 13) + "…";

            var color = isActive ? FUIColors.Active : (isHovered ? FUIColors.TextBright : FUIColors.TextPrimary);
            FUIRenderer.DrawText(canvas, name, new SKPoint(x + 12, itemY + 17), color, 11f);

            itemY += itemHeight;
        }

        // Separator line before actions (FUI style)
        float sepY = itemY + 1;
        using var sepPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame,
            StrokeWidth = 1f,
            IsAntialias = true
        };
        canvas.DrawLine(x + 12, sepY, x + width - 12, sepY, sepPaint);

        // Corner accents on separator
        using var accentLinePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active.WithAlpha(120),
            StrokeWidth = 1f,
            IsAntialias = true
        };
        canvas.DrawLine(x + 8, sepY, x + 12, sepY, accentLinePaint);
        canvas.DrawLine(x + width - 12, sepY, x + width - 8, sepY, accentLinePaint);

        itemY += 4;

        // "New Profile" option
        DrawDropdownItem(canvas, x, itemY, width, itemHeight, "+ New Profile",
            _hoveredProfileIndex == _profiles.Count, false, true);
        itemY += itemHeight;

        // "Import" option
        DrawDropdownItem(canvas, x, itemY, width, itemHeight, "↓ Import...",
            _hoveredProfileIndex == _profiles.Count + 1, false, true);
        itemY += itemHeight;

        // "Export" option
        bool canExport = _profileManager.ActiveProfile is not null;
        DrawDropdownItem(canvas, x, itemY, width, itemHeight, "↑ Export...",
            _hoveredProfileIndex == _profiles.Count + 2, false, canExport);
    }

    private void DrawDropdownItem(SKCanvas canvas, float x, float itemY, float width, float itemHeight,
        string text, bool isHovered, bool isActive, bool isEnabled)
        => FUIWidgets.DrawDropdownItem(canvas, x, itemY, width, itemHeight, text, isHovered, isActive, isEnabled);

    private void DrawVerticalSideTab(SKCanvas canvas, SKRect bounds, string label, bool isSelected, bool isHovered)
        => FUIWidgets.DrawVerticalSideTab(canvas, bounds, label, isSelected, isHovered);

    /// <summary>
    /// Draw SVG in bounds with optional mirroring. Updates shared SVG transform state.
    /// Used by Mappings tab (will move to MappingsTabController in future extraction).
    /// </summary>
    private void DrawSvgInBounds(SKCanvas canvas, SKSvg svg, SKRect bounds, bool mirror = false)
    {
        if (svg.Picture is null) return;

        var svgBounds = svg.Picture.CullRect;
        if (svgBounds.Width <= 0 || svgBounds.Height <= 0) return;

        float scaleX = bounds.Width / svgBounds.Width;
        float scaleY = bounds.Height / svgBounds.Height;
        float scale = Math.Min(scaleX, scaleY) * 0.95f;

        float scaledWidth = svgBounds.Width * scale;
        float scaledHeight = svgBounds.Height * scale;

        float offsetX = bounds.Left + (bounds.Width - scaledWidth) / 2 - svgBounds.Left * scale;
        float offsetY = bounds.Top + (bounds.Height - scaledHeight) / 2 - svgBounds.Top * scale;

        _svgScale = scale;
        _svgOffset = new SKPoint(offsetX, offsetY);
        _svgMirrored = mirror;

        canvas.Save();
        canvas.Translate(offsetX, offsetY);

        if (mirror)
        {
            canvas.Translate(scaledWidth, 0);
            canvas.Scale(-scale, scale);
        }
        else
        {
            canvas.Scale(scale);
        }

        canvas.DrawPicture(svg.Picture);
        canvas.Restore();
    }

    private void DrawStatusBar(SKCanvas canvas, SKRect bounds)
    {
        float y = bounds.Bottom - 40;

        // Far left: mouse position in viewBox coordinates (for JSON anchor editing)
        // Convert screen coords to viewBox coords
        float viewBoxX = (_mousePosition.X - _svgOffset.X) / _svgScale;
        float viewBoxY = (_mousePosition.Y - _svgOffset.Y) / _svgScale;
        string mousePos = $"VB:{viewBoxX,5:F0},{viewBoxY,5:F0}";
        FUIRenderer.DrawText(canvas, mousePos,
            new SKPoint(40, y + 22), FUIColors.TextDim, 10f);

        // Left-center: connection status
        string deviceText = _devices.Count == 1 ? "1 DEVICE CONNECTED" : $"{_devices.Count} DEVICES CONNECTED";
        FUIRenderer.DrawText(canvas, deviceText,
            new SKPoint(180, y + 22), FUIColors.TextDim, 12f);

        // Center: current status
        FUIRenderer.DrawText(canvas, "READY",
            new SKPoint(bounds.MidX - 20, y + 22), FUIColors.Success, 12f);

        // Right: version and time
        string versionTime = $"v{s_appVersion} | {DateTime.Now:HH:mm:ss}";
        float versionWidth = FUIRenderer.MeasureText(versionTime, 12f);
        FUIRenderer.DrawText(canvas, versionTime,
            new SKPoint(bounds.Right - versionWidth - 20, y + 22), FUIColors.TextDim, 12f);
    }

    private void DrawOverlayLayer(SKCanvas canvas, SKRect bounds)
    {
        // Scan line effect
        FUIRenderer.DrawScanLine(canvas, bounds, _scanLineProgress, FUIColors.Primary.WithAlpha(30), 1f);

        // CRT scan line overlay
        FUIRenderer.DrawScanLineOverlay(canvas, bounds, 2f, 4);
    }

    #endregion

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

        _profileManager.SaveActiveProfile();

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

        // Force full redraw at new DPI
        Invalidate();
    }

    #endregion

    #region System Tray

    private void InitializeTrayMenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkContextMenuRenderer()
        };

        // Start/Stop Forwarding (will be updated dynamically)
        var forwardingItem = new ToolStripMenuItem("Start Forwarding");
        forwardingItem.Click += (s, e) =>
        {
            if (_isForwarding)
                StopForwarding();
            else
                StartForwarding();
            UpdateTrayMenu();
        };
        menu.Items.Add(forwardingItem);

        menu.Items.Add(new ToolStripSeparator());

        // Open
        var openItem = new ToolStripMenuItem("Open");
        openItem.Click += (s, e) => ShowAndActivateWindow();
        menu.Items.Add(openItem);

        menu.Items.Add(new ToolStripSeparator());

        // Exit
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (s, e) =>
        {
            _forceClose = true;
            Application.Exit();
        };
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenuStrip = menu;

        // Double-click to open
        _trayIcon.DoubleClick += (s, e) => ShowAndActivateWindow();
    }

    /// <summary>
    /// Start forwarding (thin wrapper for tray menu - actual logic in DevicesTabController).
    /// </summary>
    private void StartForwarding()
    {
        SyncTabContext();
        // Reuse the same logic as the controller by invoking the button path
        if (!_isForwarding)
        {
            var profile = _profileManager.ActiveProfile;
            if (profile is null) return;
            if (profile.AxisMappings.Count == 0 && profile.ButtonMappings.Count == 0 && profile.HatMappings.Count == 0) return;
            _mappingEngine.LoadProfile(profile);
            if (!_vjoyService.IsInitialized) return;
            if (!_mappingEngine.Start()) return;
            _isForwarding = true;
            _trayIcon.SetActive(true);
        }
    }

    /// <summary>
    /// Stop forwarding (thin wrapper for tray menu - actual logic in DevicesTabController).
    /// </summary>
    private void StopForwarding()
    {
        if (!_isForwarding) return;
        _mappingEngine.Stop();
        _isForwarding = false;
        _trayIcon.SetActive(false);
    }

    private void UpdateTrayMenu()
    {
        if (_trayIcon.ContextMenuStrip is null) return;

        // Update the forwarding menu item text
        var forwardingItem = _trayIcon.ContextMenuStrip.Items[0] as ToolStripMenuItem;
        if (forwardingItem is not null)
        {
            forwardingItem.Text = _isForwarding ? "Stop Forwarding" : "Start Forwarding";
        }
    }

    #endregion

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
        _renderTimer?.Dispose();
        _inputService?.StopPolling();
        _inputService?.Dispose();
        _joystickSvg?.Dispose();
        _throttleSvg?.Dispose();
        _trayIcon?.Dispose();
        base.OnFormClosing(e);
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
}
