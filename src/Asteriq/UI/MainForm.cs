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

    // Window sizing â€” ResizeBorder is measured from the visible window edge inward.
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
    // CA2213: SKControl is a WinForms child control â€” disposed automatically via Controls collection
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
        BindingDescriptionService bindingDescriptionService,
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
        _directInputService = directInputService; // nullable â€” unavailable in tests or headless environments
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
            bindingDescriptionService ?? throw new ArgumentNullException(nameof(bindingDescriptionService)),
            _directInputService);

        // Apply correct MinimumSize now that font settings are loaded
        ApplyFontScaleToWindowSize();

        // Snap to a visible tab on startup (e.g. client-only mode hides Devices/Mappings)
        SnapToValidTab();

        // Eagerly trigger SC Bindings activation at startup so the schema starts loading in
        // the background regardless of which tab the user lands on. Makes the first SC tab
        // visit and the Mappings â†’ Keybindings deep-link feel instant. BeginInvoke inside
        // StartSchemaLoad needs a valid HWND, so we hook Shown rather than the constructor.
        Shown += (_, _) => _scBindingsController.OnActivated();

        // Start network services if enabled in settings
        if (_appSettings.NetworkEnabled)
            InitializeNetworking();

        // Apply startup preferences once the form is visible
        Shown += OnStartupPreferencesApply;

        // Silent background update check â€” only if user has enabled auto-check
        if (_appSettings.AutoCheckUpdates)
            _ = _updateService.CheckAsync().ContinueWith(
                _ => _canvas?.Invoke(() => _canvas.Invalidate()),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
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
        // Minimum window size at VSmall (1.0x) scale: 1280Ã—1024.
        // Scaled up proportionally with the combined canvas scale factor
        // (DPI Ã— text scale Ã— user preference) so the window is large enough
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
            // Icon generation failed â€” taskbar will show default
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
        // Form is hidden to the system tray â€” skip all work, nothing to display.
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



    // Windows 11 rounded-corner preference (DWM attr 33)
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    private const int TrayIconSize = 16;
    private static readonly Padding TrayItemPadding = new(0, 5, 10, 5); // 5px top/bottom, 10px right

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

}