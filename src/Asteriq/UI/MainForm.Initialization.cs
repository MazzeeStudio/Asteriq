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

public partial class MainForm
{
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
        BindingDescriptionService bindingDescriptionService,
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
            bindingDescriptionService, directInputService);
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

        // Wire up Mappings â†’ SC Bindings deep-link (used when a Mappings row is shared-away).
        // Mirror what a normal tab click does in OnCanvasMouseDown â€” deactivate the source
        // tab and activate the destination â€” otherwise SC's schema load / state init never
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
    /// via OnInputReceived â€” the MainForm local field is the authoritative copy.
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
    /// as local MainForm fields â€” accessing TabContext from the SDL2 thread is unsafe.
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

}