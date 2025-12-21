using System.Runtime.InteropServices;
using System.Xml.Linq;
using Asteriq.Models;
using Asteriq.Services;
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

    // Window sizing
    private const int ResizeBorder = 6;
    private const int TitleBarHeight = 75;

    // Services
    private readonly InputService _inputService;
    private readonly ProfileService _profileService;
    private readonly VJoyService _vjoyService;
    private readonly MappingEngine _mappingEngine;

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
    private int _hoveredDevice = -1;
    private int _selectedDevice = -1;  // Start with no selection, will be set in RefreshDevices
    private List<PhysicalDeviceInfo> _devices = new();
    private List<PhysicalDeviceInfo> _disconnectedDevices = new(); // Devices that were seen but are now disconnected
    private DeviceInputState? _currentInputState;

    // Device category tabs (D1 = Physical, D2 = Virtual)
    private int _deviceCategory = 0;  // 0 = Physical, 1 = Virtual
    private int _hoveredDeviceCategory = -1;
    private SKRect _deviceCategoryD1Bounds;
    private SKRect _deviceCategoryD2Bounds;

    
    // Device list drag-to-reorder state
    private bool _isDraggingDevice = false;
    private int _dragDeviceIndex = -1;
    private int _dragDropTargetIndex = -1;
    private SKPoint _dragStartPoint;
    private SKPoint _dragCurrentPoint;
    private List<SKRect> _deviceItemBounds = new();  // Bounds of each device item for hit testing

    // Device Actions panel buttons
    private SKRect _map1to1ButtonBounds;
    private bool _map1to1ButtonHovered;
    private SKRect _clearMappingsButtonBounds;
    private bool _clearMappingsButtonHovered;
    private SKRect _removeDeviceButtonBounds;
    private bool _removeDeviceButtonHovered;

    // Mapping category tabs (M1 = Buttons, M2 = Axes)
    private int _mappingCategory = 0;  // 0 = Buttons, 1 = Axes
    private int _hoveredMappingCategory = -1;
    private SKRect _mappingCategoryButtonsBounds;
    private SKRect _mappingCategoryAxesBounds;

    // Tab state
    private int _activeTab = 0;
    private readonly string[] _tabNames = { "DEVICES", "MAPPINGS", "BINDINGS", "SETTINGS" };

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
    private DateTime _lastSvgControlClick = DateTime.MinValue;
    private string? _lastClickedControlId;

    // Device mapping and active input tracking
    private DeviceMap? _deviceMap;
    private readonly ActiveInputTracker _activeInputTracker = new();

    // Mappings tab UI state - Left panel (output list)
    private int _selectedVJoyDeviceIndex = 0;
    private List<VJoyDeviceInfo> _vjoyDevices = new();
    private int _selectedMappingRow = -1;
    private int _hoveredMappingRow = -1;
    private SKRect _vjoyPrevButtonBounds;
    private SKRect _vjoyNextButtonBounds;
    private bool _vjoyPrevHovered;
    private bool _vjoyNextHovered;
    private List<SKRect> _mappingRowBounds = new();
    private List<SKRect> _mappingAddButtonBounds = new();
    private List<SKRect> _mappingRemoveButtonBounds = new();
    private int _hoveredAddButton = -1;
    private int _hoveredRemoveButton = -1;
    private float _bindingsScrollOffset = 0;
    private float _bindingsContentHeight = 0;
    private SKRect _bindingsListBounds;

    // Mappings tab UI state - Right panel (mapping editor)
    private bool _mappingEditorOpen = false;
    private int _editingRowIndex = -1;
    private bool _isEditingAxis = false;
    private InputDetectionService? _inputDetectionService;

    // Mapping editor - input detection
    private bool _isListeningForInput = false;
    private SKRect _inputFieldBounds;
    private bool _inputFieldHovered;
    private DetectedInput? _pendingInput;
    private DateTime _lastInputFieldClick;
    private const int DoubleClickThresholdMs = 400;

    // Mapping editor - manual entry
    private bool _manualEntryMode = false;
    private SKRect _manualEntryButtonBounds;
    private bool _manualEntryButtonHovered;
    private int _selectedSourceDevice = 0;
    private int _selectedSourceControl = 0;
    private SKRect _deviceDropdownBounds;
    private SKRect _controlDropdownBounds;
    private bool _deviceDropdownOpen = false;
    private bool _controlDropdownOpen = false;
    private int _hoveredDeviceIndex = -1;
    private int _hoveredControlIndex = -1;

    // Mapping editor - button modes
    private ButtonMode _selectedButtonMode = ButtonMode.Normal;
    private SKRect[] _buttonModeBounds = new SKRect[4];
    private int _hoveredButtonMode = -1;

    // Button mode duration settings
    private int _pulseDurationMs = 100;      // Duration for Pulse mode (100-1000ms)
    private int _holdDurationMs = 500;       // Duration for HoldToActivate mode (200-2000ms)
    private SKRect _pulseDurationSliderBounds;
    private SKRect _holdDurationSliderBounds;
    private bool _draggingPulseDuration = false;
    private bool _draggingHoldDuration = false;

    // Mapping editor - output type (Button vs Keyboard)
    private bool _outputTypeIsKeyboard = false;
    private SKRect _outputTypeBtnBounds;
    private SKRect _outputTypeKeyBounds;
    private int _hoveredOutputType = -1; // 0=Button, 1=Keyboard
    private string _selectedKeyName = "";
    private List<string>? _selectedModifiers = null;
    private SKRect _keyCaptureBounds;
    private bool _keyCaptureBoundsHovered;
    private SKRect _keyClearButtonBounds;
    private bool _keyClearButtonHovered;
    private bool _isCapturingKey = false;
    private DateTime _keyCaptureStartTime = DateTime.MinValue;
    private const int KeyCaptureTimeoutMs = 10000; // 10 second timeout for key capture

    // Input listening timeout
    private DateTime _inputListeningStartTime = DateTime.MinValue;
    private const int InputListeningTimeoutMs = 15000; // 15 second timeout for input listening

    // Double-click detection for binding rows
    private DateTime _lastRowClickTime = DateTime.MinValue;
    private int _lastClickedRow = -1;
    private const int DoubleClickMs = 400;

    // Right panel - input sources and actions
    private SKRect _addInputButtonBounds;
    private SKRect _clearAllButtonBounds;
    private List<SKRect> _inputSourceRemoveBounds = new();
    private bool _addInputButtonHovered;
    private bool _clearAllButtonHovered;
    private int _hoveredInputSourceRemove = -1;

    // Mapping editor - action buttons
    private SKRect _saveButtonBounds;
    private SKRect _cancelButtonBounds;
    private bool _saveButtonHovered;
    private bool _cancelButtonHovered;

    // Input-to-mapping highlight (attention effect when physical input is pressed)
    private int _highlightedMappingRow = -1;  // Which row to highlight (-1 = none)
    private uint _highlightedVJoyDevice = 0;  // Which vJoy device the highlighted row belongs to
    private DateTime _highlightStartTime = DateTime.MinValue;
    private const int HighlightDurationMs = 1500; // How long the attention highlight lasts (1.5 seconds)
    private Dictionary<string, bool[]> _highlightPrevButtonState = new(); // Previous frame button states (for rising-edge detection)
    private Dictionary<string, DateTime> _highlightDebounce = new(); // Debounce: last highlight time per button
    private const int HighlightDebounceCooldownMs = 500; // Minimum time between highlights for same button

    // Curve editor state
    private SKRect _curveEditorBounds;
    private List<SKPoint> _curveControlPoints = new() { new(0, 0), new(1, 1) };
    private int _hoveredCurvePoint = -1;
    private int _draggingCurvePoint = -1;
    private CurveType _selectedCurveType = CurveType.Linear;
    private bool _curveSymmetrical = false;  // When true, curve points mirror around center
    private SKRect _curveSymmetricalCheckboxBounds;

    // Deadzone state (4-parameter model like JoystickGremlinEx)
    private float _deadzoneMin = -1.0f;        // Left edge (start)
    private float _deadzoneCenterMin = 0.0f;   // Center left (start of center deadzone)
    private float _deadzoneCenterMax = 0.0f;   // Center right (end of center deadzone)
    private float _deadzoneMax = 1.0f;         // Right edge (end)
    private bool _deadzoneCenterEnabled = false; // Whether center deadzone handles are shown

    // Deadzone UI bounds
    private SKRect _deadzoneSliderBounds;
    private SKRect _deadzoneCenterCheckboxBounds; // "Centre" toggle checkbox
    private SKRect[] _deadzonePresetBounds = new SKRect[4]; // Presets: 0%, 2%, 5%, 10%
    private int _draggingDeadzoneHandle = -1; // 0=min, 1=centerMin, 2=centerMax, 3=max
    private int _selectedDeadzoneHandle = -1; // Currently selected handle for preset application

    // Legacy compatibility
    private float _axisDeadzone
    {
        get => Math.Max(Math.Abs(_deadzoneCenterMin), Math.Abs(_deadzoneCenterMax));
        set
        {
            _deadzoneCenterMin = -Math.Abs(value);
            _deadzoneCenterMax = Math.Abs(value);
        }
    }

    private SKRect[] _curvePresetBounds = new SKRect[4]; // Bounds for Linear, S-Curve, Expo, Custom buttons
    private SKRect _invertToggleBounds;
    private bool _axisInverted = false;

    // Theme selector state
    private SKRect[] _themeButtonBounds = new SKRect[12];

    // Font size selector state
    private SKRect[] _fontSizeButtonBounds = new SKRect[3];

    // Background settings slider bounds
    private SKRect _bgGridSliderBounds;
    private SKRect _bgGlowSliderBounds;
    private SKRect _bgNoiseSliderBounds;
    private SKRect _bgScanlineSliderBounds;
    private SKRect _bgVignetteSliderBounds;
    private SKRect _autoLoadToggleBounds;
    private string? _draggingBgSlider;  // Which slider is being dragged

    // Star Citizen bindings tab state
    private SCInstallationService? _scInstallationService;
    private SCProfileCacheService? _scProfileCacheService;
    private SCSchemaService? _scSchemaService;
    private SCXmlExportService? _scExportService;
    private List<SCInstallation> _scInstallations = new();
    private int _selectedSCInstallation = 0;
    private SCExportProfile _scExportProfile = new();
    private List<SCAction>? _scActions;
    private string? _scExportStatus;
    private DateTime _scExportStatusTime;

    // SC Bindings tab UI bounds
    private SKRect _scInstallationSelectorBounds;
    private bool _scInstallationDropdownOpen;
    private SKRect _scInstallationDropdownBounds;
    private int _hoveredSCInstallation = -1;
    private SKRect _scExportButtonBounds;
    private bool _scExportButtonHovered;
    private SKRect _scExportFilenameBoxBounds;
    private bool _scExportFilenameBoxFocused;
    private string _scExportFilename = "";  // Custom export filename, empty = auto-generate
    private SKRect _scImportButtonBounds;
    private bool _scImportButtonHovered;
    private List<SCMappingFile> _scAvailableProfiles = new();
    private bool _scImportDropdownOpen;
    private SKRect _scImportDropdownBounds;
    private int _scHoveredImportProfile = -1;
    private SKRect _scRefreshButtonBounds;
    private bool _scRefreshButtonHovered;
    private SKRect _scClearAllButtonBounds;
    private bool _scClearAllButtonHovered;
    private SKRect _scResetDefaultsButtonBounds;
    private bool _scResetDefaultsButtonHovered;
    private SKRect _scProfileNameBounds;
    private bool _scProfileNameHovered;
    private List<SKRect> _scVJoyMappingBounds = new();
    private int _hoveredVJoyMapping = -1;

    // SC Bindings table state
    private List<SCAction>? _scFilteredActions;
    private string _scActionMapFilter = "";  // Empty = show all, otherwise filter by action map
    private int _scSelectedActionIndex = -1;
    private int _scHoveredActionIndex = -1;
    private float _scBindingsScrollOffset = 0;
    private float _scBindingsContentHeight = 0;
    private SKRect _scBindingsListBounds;
    private List<SKRect> _scActionRowBounds = new();
    private SKRect _scActionMapFilterBounds;
    private bool _scActionMapFilterDropdownOpen;
    private SKRect _scActionMapFilterDropdownBounds;
    private int _scHoveredActionMapFilter = -1;
    private float _scActionMapFilterScrollOffset = 0;
    private float _scActionMapFilterMaxScroll = 0;
    private List<string> _scActionMaps = new();  // List of unique action maps

    // SC Bindings grid column state
    private float _scGridActionColWidth = 300f;   // Action name column
    private float _scGridDeviceColMinWidth = 160f;   // Minimum device column width
    private Dictionary<string, float> _scGridDeviceColWidths = new();  // Calculated width per column based on content
    private float _scGridHorizontalScroll = 0f;   // Horizontal scroll offset for device columns
    private float _scGridTotalWidth = 0f;         // Total width of all columns
    private List<SCGridColumn>? _scGridColumns;   // Cached column definitions
    private float _scDeviceColsStart = 0f;        // X position where device columns start
    private float _scVisibleDeviceWidth = 0f;     // Width of visible device column area

    // SC Bindings cell interaction state
    private (int actionIndex, int colIndex) _scSelectedCell = (-1, -1);  // Currently selected cell
    private (int actionIndex, int colIndex) _scHoveredCell = (-1, -1);   // Currently hovered cell
    private bool _scIsListeningForInput = false;  // True when waiting for user input
    private DateTime _scListeningStartTime;       // When listening started (for timeout)
    private DateTime _scLastCellClickTime;        // When the last cell was clicked (for double-click detection)
    private const int SCListeningTimeoutMs = 5000; // 5 second timeout for input listening
    private SCGridColumn? _scListeningColumn;     // Which column we're listening for
    private HashSet<string> _scConflictingBindings = new();  // Set of binding keys that have conflicts
    private int _scHighlightedColumn = -1;        // Column index to highlight (-1 = none)

    // SC Bindings scrollbar interaction state
    private bool _scIsDraggingVScroll = false;    // Dragging vertical scrollbar
    private bool _scIsDraggingHScroll = false;    // Dragging horizontal scrollbar
    private float _scScrollDragStartY = 0;        // Y position when drag started
    private float _scScrollDragStartX = 0;        // X position when drag started
    private float _scScrollDragStartOffset = 0;   // Scroll offset when drag started
    private SKRect _scVScrollbarBounds;           // Vertical scrollbar track bounds
    private SKRect _scHScrollbarBounds;           // Horizontal scrollbar track bounds
    private SKRect _scVScrollThumbBounds;         // Vertical scrollbar thumb bounds
    private SKRect _scHScrollThumbBounds;         // Horizontal scrollbar thumb bounds
    private SKRect _scColumnHeadersBounds;        // Bounds of the column headers row

    // SC Bindings search and filter state
    private string _scSearchText = "";           // Search text for filtering actions
    private bool _scShowBoundOnly = false;       // Show only actions with bindings
    private SKRect _scSearchBoxBounds;
    private bool _scSearchBoxFocused = false;
    private SKRect _scShowBoundOnlyBounds;
    private bool _scShowBoundOnlyHovered = false;

    // SC Category collapse state
    private HashSet<string> _scCollapsedCategories = new();  // Action maps that are collapsed
    private Dictionary<string, SKRect> _scCategoryHeaderBounds = new();  // Bounds for category headers

    // SC Binding assignment state
    private bool _scAssigningInput = false;
    private SKRect _scAssignInputButtonBounds;
    private bool _scAssignInputButtonHovered;
    private SKRect _scClearBindingButtonBounds;
    private bool _scClearBindingButtonHovered;

    // SC Export profile management
    private SCExportProfileService? _scExportProfileService;
    private List<SCExportProfileInfo> _scExportProfiles = new();
    private SKRect _scProfileDropdownBounds;
    private bool _scProfileDropdownOpen;
    private SKRect _scProfileDropdownListBounds;
    private int _scHoveredProfileIndex = -1;
    private SKRect _scNewProfileButtonBounds;
    private bool _scNewProfileButtonHovered;
    private SKRect _scSaveProfileButtonBounds;
    private bool _scSaveProfileButtonHovered;
    private SKRect _scDeleteProfileButtonBounds;
    private bool _scDeleteProfileButtonHovered;

    // Input forwarding state (physical → vJoy)
    private bool _isForwarding = false;
    private SKRect _startForwardingButtonBounds;
    private SKRect _stopForwardingButtonBounds;
    private bool _startForwardingButtonHovered;
    private bool _stopForwardingButtonHovered;

    public MainForm()
    {
        _inputService = new InputService();
        _profileService = new ProfileService();
        _vjoyService = new VJoyService();
        _mappingEngine = new MappingEngine(_vjoyService);
        InitializeForm();
        InitializeCanvas();
        InitializeInput();
        InitializeVJoy();
        InitializeRenderLoop();
        LoadSvgAssets();
        InitializeProfiles();
        InitializeSCBindings();
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
        _profileService.Initialize();
        RefreshProfileList();

        // Apply font size setting
        FUIRenderer.FontSizeOption = _profileService.FontSize;

        // Apply theme setting
        FUIColors.SetTheme(_profileService.Theme);

        // Apply background settings
        var bgSettings = _profileService.LoadBackgroundSettings();
        _background.GridStrength = bgSettings.gridStrength;
        _background.GlowIntensity = bgSettings.glowIntensity;
        _background.NoiseIntensity = bgSettings.noiseIntensity;
        _background.ScanlineIntensity = bgSettings.scanlineIntensity;
        _background.VignetteStrength = bgSettings.vignetteStrength;
    }

    private void RefreshProfileList()
    {
        _profiles = _profileService.ListProfiles();
    }

    private void CreateNewProfilePrompt()
    {
        // Simple input dialog for profile name
        // In production, this would be a proper FUI-styled dialog
        string defaultName = $"Profile {_profiles.Count + 1}";

        using var dialog = new Form
        {
            Text = "New Profile",
            Width = 320,
            Height = 140,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(20, 22, 30)
        };

        var label = new Label
        {
            Text = "Profile Name:",
            Left = 15,
            Top = 15,
            Width = 280,
            ForeColor = Color.FromArgb(180, 190, 210)
        };

        var textBox = new TextBox
        {
            Text = defaultName,
            Left = 15,
            Top = 40,
            Width = 275,
            BackColor = Color.FromArgb(35, 38, 50),
            ForeColor = Color.FromArgb(220, 230, 240)
        };
        textBox.SelectAll();

        var okButton = new Button
        {
            Text = "Create",
            Left = 130,
            Top = 70,
            Width = 75,
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(40, 90, 120),
            ForeColor = Color.White
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            Left = 215,
            Top = 70,
            Width = 75,
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(50, 52, 60),
            ForeColor = Color.FromArgb(180, 190, 210)
        };

        dialog.Controls.AddRange(new Control[] { label, textBox, okButton, cancelButton });
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(textBox.Text))
        {
            _profileService.CreateAndActivateProfile(textBox.Text.Trim());
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
            var imported = _profileService.ImportProfile(openDialog.FileName, generateNewId: true);
            if (imported is not null)
            {
                _profileService.ActivateProfile(imported.Id);
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
        if (_profileService.ActiveProfile is null)
        {
            FUIMessageBox.ShowInfo(this,
                "No profile is currently active. Please select a profile first.",
                "Export");
            return;
        }

        var profile = _profileService.ActiveProfile;
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
            bool success = _profileService.ExportProfile(profile.Id, saveDialog.FileName);
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
    /// Load the appropriate device map based on device name.
    /// Searches for maps matching the device name, falls back to device type, then generic joystick.json.
    /// Also detects left-hand devices by "LEFT" prefix and sets mirror flag.
    /// </summary>
    private void LoadDeviceMapForDevice(string? deviceName)
    {
        var mapsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Devices", "Maps");

        // Try to find a device-specific map by matching device name
        if (!string.IsNullOrEmpty(deviceName))
        {
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

            // Step 1: Try exact device name match (skip generic maps)
            foreach (var (path, map) in allMaps)
            {
                if (!string.IsNullOrEmpty(map.Device) &&
                    !map.Device.StartsWith("Generic", StringComparison.OrdinalIgnoreCase))
                {
                    if (deviceName.Contains(map.Device, StringComparison.OrdinalIgnoreCase) ||
                        map.Device.Contains(deviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        _deviceMap = map;
                        return;
                    }
                }
            }

            // Step 2: Try device type match based on keywords in device name
            string detectedType = DetectDeviceType(deviceName);
            if (detectedType != "Joystick")
            {
                foreach (var (path, map) in allMaps)
                {
                    if (map.DeviceType.Equals(detectedType, StringComparison.OrdinalIgnoreCase))
                    {
                        _deviceMap = map;
                        System.Diagnostics.Debug.WriteLine($"Loaded device map (type match '{detectedType}'): {path} for device: {deviceName}");
                        return;
                    }
                }
            }

            // Step 3: No specific map found - check if device name indicates left-hand
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
            return;
        }

        // Fall back to generic joystick map
        var defaultMapPath2 = Path.Combine(mapsDir, "joystick.json");
        _deviceMap = DeviceMap.Load(defaultMapPath2);
        System.Diagnostics.Debug.WriteLine($"Loaded default device map: joystick.json");
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
        catch (Exception ex)
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
        MinimumSize = new Size(1024, 768);
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.Black;
        DoubleBuffered = true;
        KeyPreview = true;

        // Load saved window state or use defaults
        var (width, height, x, y) = _profileService.LoadWindowState();
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

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Handle key capture for keyboard output mapping
        if (_isCapturingKey)
        {
            // Get the base key without modifiers
            var baseKey = keyData & Keys.KeyCode;

            // Check if this is a modifier-only key press (e.g., just RCtrl)
            bool isModifierOnly = baseKey == Keys.ControlKey || baseKey == Keys.ShiftKey ||
                baseKey == Keys.Menu || baseKey == Keys.Control ||
                baseKey == Keys.Shift || baseKey == Keys.Alt ||
                baseKey == Keys.LControlKey || baseKey == Keys.RControlKey ||
                baseKey == Keys.LShiftKey || baseKey == Keys.RShiftKey ||
                baseKey == Keys.LMenu || baseKey == Keys.RMenu;

            if (isModifierOnly)
            {
                // Capture just the modifier key itself (e.g., just RCtrl or just LShift)
                _selectedKeyName = GetModifierKeyName(baseKey);
                _selectedModifiers = null; // No additional modifiers when capturing a modifier itself
                _outputTypeIsKeyboard = true; // Capturing a key means keyboard output mode
                _isCapturingKey = false;
                UpdateKeyNameForSelected();
                return true;
            }

            // Extract modifiers and key name for regular keys
            var (keyName, modifiers) = GetKeyNameAndModifiersFromKeys(keyData);
            if (!string.IsNullOrEmpty(keyName))
            {
                _selectedKeyName = keyName;
                _selectedModifiers = modifiers.Count > 0 ? modifiers : null;
                _outputTypeIsKeyboard = true; // Capturing a key means keyboard output mode
                _isCapturingKey = false;
                UpdateKeyNameForSelected();
            }
            return true; // Consume the key
        }

        // Handle SC Bindings search box input
        if (_scSearchBoxFocused && _activeTab == 2)
        {
            return HandleSearchBoxKey(keyData);
        }

        // Handle export filename box input
        if (_scExportFilenameBoxFocused && _activeTab == 2)
        {
            return HandleExportFilenameBoxKey(keyData);
        }

        // Cancel key capture with Escape
        if (keyData == Keys.Escape)
        {
            if (_isCapturingKey)
            {
                _isCapturingKey = false;
                return true;
            }
            if (_isListeningForInput)
            {
                CancelInputListening();
                return true;
            }
            if (_scSearchBoxFocused)
            {
                _scSearchBoxFocused = false;
                return true;
            }
            if (_scExportFilenameBoxFocused)
            {
                _scExportFilenameBoxFocused = false;
                return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool HandleSearchBoxKey(Keys keyData)
    {
        // Remove modifiers for comparison
        var key = keyData & Keys.KeyCode;

        if (key == Keys.Escape)
        {
            _scSearchBoxFocused = false;
            return true;
        }

        if (key == Keys.Back)
        {
            if (_scSearchText.Length > 0)
            {
                _scSearchText = _scSearchText.Substring(0, _scSearchText.Length - 1);
                RefreshFilteredActions();
            }
            return true;
        }

        if (key == Keys.Delete)
        {
            _scSearchText = "";
            RefreshFilteredActions();
            return true;
        }

        // Allow alphanumeric and common characters
        char c = KeyToChar(key, (keyData & Keys.Shift) == Keys.Shift);
        if (c != '\0' && _scSearchText.Length < 50)
        {
            _scSearchText += c;
            RefreshFilteredActions();
            return true;
        }

        return false; // Let other keys pass through
    }

    private bool HandleExportFilenameBoxKey(Keys keyData)
    {
        // Remove modifiers for comparison
        var key = keyData & Keys.KeyCode;

        if (key == Keys.Escape || key == Keys.Enter)
        {
            _scExportFilenameBoxFocused = false;
            return true;
        }

        if (key == Keys.Back)
        {
            if (_scExportFilename.Length > 0)
            {
                _scExportFilename = _scExportFilename.Substring(0, _scExportFilename.Length - 1);
            }
            return true;
        }

        if (key == Keys.Delete)
        {
            _scExportFilename = "";
            return true;
        }

        // Allow alphanumeric and filename-safe characters (no spaces, special chars)
        char c = KeyToFilenameChar(key, (keyData & Keys.Shift) == Keys.Shift);
        if (c != '\0' && _scExportFilename.Length < 50)
        {
            _scExportFilename += c;
            return true;
        }

        return false; // Let other keys pass through
    }

    private static char KeyToFilenameChar(Keys key, bool shift)
    {
        // Letters
        if (key >= Keys.A && key <= Keys.Z)
        {
            char c = (char)('a' + (key - Keys.A));
            return shift ? char.ToUpper(c) : c;
        }

        // Numbers
        if (key >= Keys.D0 && key <= Keys.D9)
        {
            return (char)('0' + (key - Keys.D0));
        }

        // Only filename-safe characters
        return key switch
        {
            Keys.OemMinus => shift ? '_' : '-',
            Keys.Oemplus => '=',
            Keys.OemPeriod => '.',
            _ => '\0'
        };
    }

    private static char KeyToChar(Keys key, bool shift)
    {
        // Letters
        if (key >= Keys.A && key <= Keys.Z)
        {
            char c = (char)('a' + (key - Keys.A));
            return shift ? char.ToUpper(c) : c;
        }

        // Numbers
        if (key >= Keys.D0 && key <= Keys.D9)
        {
            return (char)('0' + (key - Keys.D0));
        }

        // Space and common characters
        return key switch
        {
            Keys.Space => ' ',
            Keys.OemMinus => shift ? '_' : '-',
            Keys.Oemplus => shift ? '+' : '=',
            Keys.OemPeriod => '.',
            Keys.Oemcomma => ',',
            _ => '\0'
        };
    }

    private static string? GetKeyNameFromKeys(Keys keys)
    {
        var (keyName, _) = GetKeyNameAndModifiersFromKeys(keys);
        return keyName;
    }

    private static (string? keyName, List<string> modifiers) GetKeyNameAndModifiersFromKeys(Keys keys)
    {
        var modifiers = new List<string>();

        // Check for AltGr first - Windows sends LCtrl+RAlt for AltGr
        // We detect this by checking if both LCtrl and RAlt are held simultaneously
        bool isAltGr = IsKeyHeld(VK_RMENU) && IsKeyHeld(VK_LCONTROL) && !IsKeyHeld(VK_RCONTROL);

        if (isAltGr)
        {
            // AltGr pressed - just add AltGr, skip the phantom LCtrl
            modifiers.Add("AltGr");
        }
        else
        {
            // Extract modifiers using GetAsyncKeyState for left/right detection
            if ((keys & Keys.Control) == Keys.Control)
            {
                // Try to detect left vs right using GetAsyncKeyState
                if (IsKeyHeld(VK_RCONTROL))
                    modifiers.Add("RCtrl");
                else if (IsKeyHeld(VK_LCONTROL))
                    modifiers.Add("LCtrl");
            }
            if ((keys & Keys.Alt) == Keys.Alt)
            {
                if (IsKeyHeld(VK_RMENU))
                    modifiers.Add("RAlt");
                else if (IsKeyHeld(VK_LMENU))
                    modifiers.Add("LAlt");
            }
        }

        // Shift is independent of AltGr
        if ((keys & Keys.Shift) == Keys.Shift)
        {
            if (IsKeyHeld(VK_RSHIFT))
                modifiers.Add("RShift");
            else
                modifiers.Add("LShift");
        }

        // Remove modifiers to get the base key
        var baseKey = keys & ~Keys.Modifiers;

        // Skip modifier-only presses
        if (baseKey == Keys.ControlKey || baseKey == Keys.ShiftKey ||
            baseKey == Keys.Menu || baseKey == Keys.LWin || baseKey == Keys.RWin ||
            baseKey == Keys.LControlKey || baseKey == Keys.RControlKey ||
            baseKey == Keys.LShiftKey || baseKey == Keys.RShiftKey ||
            baseKey == Keys.LMenu || baseKey == Keys.RMenu)
            return (null, modifiers);

        var keyName = baseKey switch
        {
            Keys.A => "A", Keys.B => "B", Keys.C => "C", Keys.D => "D",
            Keys.E => "E", Keys.F => "F", Keys.G => "G", Keys.H => "H",
            Keys.I => "I", Keys.J => "J", Keys.K => "K", Keys.L => "L",
            Keys.M => "M", Keys.N => "N", Keys.O => "O", Keys.P => "P",
            Keys.Q => "Q", Keys.R => "R", Keys.S => "S", Keys.T => "T",
            Keys.U => "U", Keys.V => "V", Keys.W => "W", Keys.X => "X",
            Keys.Y => "Y", Keys.Z => "Z",
            Keys.D0 => "0", Keys.D1 => "1", Keys.D2 => "2", Keys.D3 => "3",
            Keys.D4 => "4", Keys.D5 => "5", Keys.D6 => "6", Keys.D7 => "7",
            Keys.D8 => "8", Keys.D9 => "9",
            Keys.F1 => "F1", Keys.F2 => "F2", Keys.F3 => "F3", Keys.F4 => "F4",
            Keys.F5 => "F5", Keys.F6 => "F6", Keys.F7 => "F7", Keys.F8 => "F8",
            Keys.F9 => "F9", Keys.F10 => "F10", Keys.F11 => "F11", Keys.F12 => "F12",
            Keys.Space => "Space",
            Keys.Enter => "Enter",
            Keys.Tab => "Tab",
            Keys.Back => "Backspace",
            Keys.Delete => "Delete",
            Keys.Insert => "Insert",
            Keys.Home => "Home",
            Keys.End => "End",
            Keys.PageUp => "PageUp",
            Keys.PageDown => "PageDown",
            Keys.Up => "Up",
            Keys.Down => "Down",
            Keys.Left => "Left",
            Keys.Right => "Right",
            Keys.NumPad0 => "Num0", Keys.NumPad1 => "Num1", Keys.NumPad2 => "Num2",
            Keys.NumPad3 => "Num3", Keys.NumPad4 => "Num4", Keys.NumPad5 => "Num5",
            Keys.NumPad6 => "Num6", Keys.NumPad7 => "Num7", Keys.NumPad8 => "Num8",
            Keys.NumPad9 => "Num9",
            Keys.Multiply => "Num*",
            Keys.Add => "Num+",
            Keys.Subtract => "Num-",
            Keys.Decimal => "Num.",
            Keys.Divide => "Num/",
            _ => null
        };

        return (keyName, modifiers);
    }

    // Windows API for detecting held keys
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    // Virtual key codes for left/right modifiers
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;  // Left Alt
    private const int VK_RMENU = 0xA5;  // Right Alt

    private static bool IsKeyHeld(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>
    /// Get the specific modifier key name (left/right variant) for modifier-only key presses
    /// </summary>
    private static string GetModifierKeyName(Keys key)
    {
        // Use GetAsyncKeyState to determine if it's left or right variant
        if (key == Keys.ControlKey || key == Keys.Control)
        {
            if (IsKeyHeld(VK_RCONTROL)) return "RCtrl";
            return "LCtrl";
        }
        if (key == Keys.LControlKey) return "LCtrl";
        if (key == Keys.RControlKey) return "RCtrl";

        if (key == Keys.ShiftKey || key == Keys.Shift)
        {
            if (IsKeyHeld(VK_RSHIFT)) return "RShift";
            return "LShift";
        }
        if (key == Keys.LShiftKey) return "LShift";
        if (key == Keys.RShiftKey) return "RShift";

        if (key == Keys.Menu || key == Keys.Alt)
        {
            if (IsKeyHeld(VK_RMENU)) return "RAlt";
            return "LAlt";
        }
        if (key == Keys.LMenu) return "LAlt";
        if (key == Keys.RMenu) return "RAlt";

        return key.ToString();
    }

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

        // Update active input animations (~16ms per tick = 0.016s)
        _activeInputTracker.UpdateAnimations(0.016f);

        // Check for SC binding input when in listening mode
        if (_activeTab == 2 && _scIsListeningForInput)
        {
            CheckSCBindingInput();
        }

        // Update background animations
        _background.Update(0.016f);

        _canvas.Invalidate();
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
        // Find first device matching current category
        var filteredDevices = _deviceCategory == 0
            ? _devices.Where(d => !d.IsVirtual).ToList()
            : _devices.Where(d => d.IsVirtual).ToList();

        if (filteredDevices.Count > 0)
        {
            // Get the actual index in _devices
            _selectedDevice = _devices.IndexOf(filteredDevices[0]);
            if (_selectedDevice >= 0)
            {
                LoadDeviceMapForDevice(_devices[_selectedDevice].Name);
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
        if (_activeTab == 1 && _profileService.ActiveProfile is not null)
        {
            int prevHighlightRow = _highlightedMappingRow;
            uint prevHighlightDevice = _highlightedVJoyDevice;

            CheckForMappingHighlight(state);

            // Invalidate canvas if highlight changed to show the shimmer effect
            if (_highlightedMappingRow != prevHighlightRow || _highlightedVJoyDevice != prevHighlightDevice)
            {
                _canvas.Invalidate();
            }
        }
    }

    /// <summary>
    /// Check if any pressed input maps to a vJoy output and highlight it
    /// </summary>
    private void CheckForMappingHighlight(DeviceInputState state)
    {
        var profile = _profileService.ActiveProfile;
        if (profile is null) return;

        // Get previous button state for this device (for rising-edge detection)
        _highlightPrevButtonState.TryGetValue(state.DeviceName, out var prevButtons);

        // Check button presses - only trigger on rising edge (was NOT pressed, now IS pressed)
        for (int i = 0; i < state.Buttons.Length; i++)
        {
            bool isPressed = state.Buttons[i];
            bool wasPressed = prevButtons is not null && i < prevButtons.Length && prevButtons[i];

            // Only trigger on rising edge (transition from not-pressed to pressed)
            if (!isPressed || wasPressed) continue;

            // Check debounce - don't re-highlight the same button too quickly
            string debounceKey = $"{state.DeviceName}:{i}";
            if (_highlightDebounce.TryGetValue(debounceKey, out var lastTime))
            {
                var elapsed = (DateTime.Now - lastTime).TotalMilliseconds;
                if (elapsed < HighlightDebounceCooldownMs)
                    continue; // Skip - too soon since last highlight for this button
            }

            // Look for button mapping from this device/button (match by device name and input)
            var mapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Inputs.Any(input =>
                    input.DeviceName == state.DeviceName &&
                    input.Type == InputType.Button &&
                    input.Index == i));

            if (mapping is not null)
            {
                // Found a mapping - highlight this row
                _highlightedMappingRow = mapping.Output.Index;
                _highlightedVJoyDevice = mapping.Output.VJoyDevice;
                _highlightStartTime = DateTime.Now;
                _highlightDebounce[debounceKey] = DateTime.Now; // Record highlight time for debounce
                break;
            }
        }

        // Store current button state for next frame comparison
        _highlightPrevButtonState[state.DeviceName] = (bool[])state.Buttons.Clone();
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

            _canvas.Invalidate();
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

            _canvas.Invalidate();
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
            LoadDeviceMapForDevice(_devices[_selectedDevice].Name);
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
            catch
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
        catch
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
        if (m.Msg == WM_NCHITTEST)
        {
            var result = HitTest(PointToClient(Cursor.Position));
            if (result != HTCLIENT)
            {
                m.Result = (IntPtr)result;
                return;
            }
        }

        base.WndProc(ref m);
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
            // Exclude tab area (tabs start at Width - 540 and span ~400px)
            float tabStartX = ClientSize.Width - 540;
            if (clientPoint.X >= tabStartX && clientPoint.Y >= 35 && clientPoint.Y <= 65)
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

        // Handle SC Bindings scrollbar dragging
        if (_scIsDraggingVScroll)
        {
            float deltaY = e.Y - _scScrollDragStartY;
            float maxScroll = Math.Max(0, _scBindingsContentHeight - _scBindingsListBounds.Height);
            float trackHeight = _scVScrollbarBounds.Height - _scVScrollThumbBounds.Height;
            if (trackHeight > 0 && maxScroll > 0)
            {
                float scrollDelta = (deltaY / trackHeight) * maxScroll;
                _scBindingsScrollOffset = Math.Clamp(_scScrollDragStartOffset + scrollDelta, 0, maxScroll);
            }
            _canvas.Invalidate();
            return;
        }

        if (_scIsDraggingHScroll)
        {
            float deltaX = e.X - _scScrollDragStartX;
            float maxHScroll = Math.Max(0, _scGridTotalWidth - _scVisibleDeviceWidth);
            float trackWidth = _scHScrollbarBounds.Width - _scHScrollThumbBounds.Width;
            if (trackWidth > 0 && maxHScroll > 0)
            {
                float scrollDelta = (deltaX / trackWidth) * maxHScroll;
                _scGridHorizontalScroll = Math.Clamp(_scScrollDragStartOffset + scrollDelta, 0, maxHScroll);
            }
            _canvas.Invalidate();
            return;
        }

        // Handle background slider dragging (works across all tabs since it's global)
        if (_draggingBgSlider is not null)
        {
            UpdateBgSliderFromPoint(e.X);
            return;
        }

        // Handle device list drag-to-reorder (physical devices only)
        if (_activeTab == 0 && _deviceCategory == 0 && _dragDeviceIndex >= 0)
        {
            var currentPoint = new SKPoint(e.X, e.Y);
            float dragDistance = SKPoint.Distance(currentPoint, _dragStartPoint);

            // Start actual drag if moved beyond threshold (5 pixels)
            if (!_isDraggingDevice && dragDistance > 5)
            {
                _isDraggingDevice = true;
                Cursor = Cursors.SizeAll;
            }

            if (_isDraggingDevice)
            {
                _dragCurrentPoint = currentPoint;

                // Calculate drop target index based on Y position
                float dragItemHeight = 60f;
                float dragItemGap = FUIRenderer.ItemSpacing;
                float dragContentTop = 90 + 32 + FUIRenderer.PanelPadding;

                // Get filtered devices (physical only)
                var physicalDevices = _devices.Where(d => !d.IsVirtual).ToList();

                // Calculate which slot the cursor is over
                float relativeY = e.Y - dragContentTop;
                int targetIndex = (int)(relativeY / (dragItemHeight + dragItemGap));
                targetIndex = Math.Clamp(targetIndex, 0, physicalDevices.Count);

                _dragDropTargetIndex = targetIndex;
                _canvas.Invalidate();
                return;
            }
        }

        // Mappings tab hover handling
        if (_activeTab == 1)
        {
            // Reset hover states
            _vjoyPrevHovered = false;
            _vjoyNextHovered = false;
            _hoveredMappingRow = -1;
            _hoveredAddButton = -1;
            _hoveredRemoveButton = -1;
            _hoveredButtonMode = -1;
            _hoveredOutputType = -1;
            _keyCaptureBoundsHovered = false;
            _keyClearButtonHovered = false;
            _addInputButtonHovered = false;
            _clearAllButtonHovered = false;
            _hoveredInputSourceRemove = -1;

            // Right panel: Add input button
            if (_addInputButtonBounds.Contains(e.X, e.Y))
            {
                _addInputButtonHovered = true;
                Cursor = Cursors.Hand;
                return;
            }

            // Right panel: Input source remove buttons
            for (int i = 0; i < _inputSourceRemoveBounds.Count; i++)
            {
                if (_inputSourceRemoveBounds[i].Contains(e.X, e.Y))
                {
                    _hoveredInputSourceRemove = i;
                    Cursor = Cursors.Hand;
                    return;
                }
            }

            // Right panel: Button mode buttons (for button category)
            if (_mappingCategory == 0 && _selectedMappingRow >= 0)
            {
                // Handle duration slider dragging
                if (_draggingPulseDuration)
                {
                    UpdatePulseDurationFromMouse(e.X);
                    _canvas.Invalidate();
                    return;
                }
                if (_draggingHoldDuration)
                {
                    UpdateHoldDurationFromMouse(e.X);
                    _canvas.Invalidate();
                    return;
                }

                for (int i = 0; i < _buttonModeBounds.Length; i++)
                {
                    if (_buttonModeBounds[i].Contains(e.X, e.Y))
                    {
                        _hoveredButtonMode = i;
                        Cursor = Cursors.Hand;
                        return;
                    }
                }

                // Output type buttons
                if (_outputTypeBtnBounds.Contains(e.X, e.Y))
                {
                    _hoveredOutputType = 0;
                    Cursor = Cursors.Hand;
                    return;
                }
                if (_outputTypeKeyBounds.Contains(e.X, e.Y))
                {
                    _hoveredOutputType = 1;
                    Cursor = Cursors.Hand;
                    return;
                }

                // Key clear button (check before key capture field so it takes precedence)
                if (_outputTypeIsKeyboard && !_keyClearButtonBounds.IsEmpty && _keyClearButtonBounds.Contains(e.X, e.Y))
                {
                    _keyClearButtonHovered = true;
                    Cursor = Cursors.Hand;
                    return;
                }

                // Key capture field
                if (_outputTypeIsKeyboard && _keyCaptureBounds.Contains(e.X, e.Y))
                {
                    _keyCaptureBoundsHovered = true;
                    Cursor = Cursors.Hand;
                    return;
                }

                // Clear Mapping button
                if (_clearAllButtonBounds.Contains(e.X, e.Y))
                {
                    _clearAllButtonHovered = true;
                    Cursor = Cursors.Hand;
                    return;
                }
            }

            // Right panel: Curve editor handling (for axis category)
            if (_mappingCategory == 1 && _selectedMappingRow >= 0)
            {
                var pt = new SKPoint(e.X, e.Y);

                // Handle dragging operations
                if (_draggingCurvePoint >= 0)
                {
                    UpdateDraggedCurvePoint(pt);
                    _canvas.Invalidate();
                    return;
                }
                if (_draggingDeadzoneHandle >= 0)
                {
                    UpdateDraggedDeadzoneHandle(pt);
                    _canvas.Invalidate();
                    return;
                }
                // Check curve point hover
                if (_selectedCurveType == CurveType.Custom && _curveEditorBounds.Contains(pt))
                {
                    int newHovered = FindCurvePointAt(pt, _curveEditorBounds);
                    if (newHovered != _hoveredCurvePoint)
                    {
                        _hoveredCurvePoint = newHovered;
                        Cursor = newHovered >= 0 ? Cursors.Hand : Cursors.Cross;
                        _canvas.Invalidate();
                    }
                    return;
                }
                else if (_hoveredCurvePoint >= 0)
                {
                    _hoveredCurvePoint = -1;
                    _canvas.Invalidate();
                }
            }

            // Left panel: vJoy device selector buttons
            if (_vjoyPrevButtonBounds.Contains(e.X, e.Y) && _selectedVJoyDeviceIndex > 0)
            {
                _vjoyPrevHovered = true;
                Cursor = Cursors.Hand;
                return;
            }
            if (_vjoyNextButtonBounds.Contains(e.X, e.Y) && _selectedVJoyDeviceIndex < _vjoyDevices.Count - 1)
            {
                _vjoyNextHovered = true;
                Cursor = Cursors.Hand;
                return;
            }

            // Left panel: Mapping row hover
            for (int i = 0; i < _mappingRowBounds.Count; i++)
            {
                if (_mappingRowBounds[i].Contains(e.X, e.Y))
                {
                    _hoveredMappingRow = i;
                    Cursor = Cursors.Hand;
                    return;
                }
            }
        }
        else
        {
            _hoveredMappingRow = -1;
            _hoveredAddButton = -1;
            _hoveredRemoveButton = -1;
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

        // SC Installation dropdown hover detection (Bindings tab)
        if (_activeTab == 2 && _scInstallationDropdownOpen && _scInstallationDropdownBounds.Contains(e.X, e.Y))
        {
            float itemHeight = 28f;
            int itemIndex = (int)((e.Y - _scInstallationDropdownBounds.Top - 2) / itemHeight);
            _hoveredSCInstallation = itemIndex >= 0 && itemIndex < _scInstallations.Count ? itemIndex : -1;
            Cursor = Cursors.Hand;
            return;
        }
        else if (_activeTab == 2)
        {
            _hoveredSCInstallation = -1;
        }

        // SC Bindings tab hover detection
        if (_activeTab == 2)
        {
            // Reset hover states
            _hoveredVJoyMapping = -1;
            _scHoveredActionIndex = -1;
            _scHoveredActionMapFilter = -1;
            _scHoveredCell = (-1, -1);

            // Action map filter dropdown hover
            if (_scActionMapFilterDropdownOpen && _scActionMapFilterDropdownBounds.Contains(e.X, e.Y))
            {
                float itemHeight = 24f;
                // Account for scroll offset when calculating hovered item
                float relativeY = e.Y - _scActionMapFilterDropdownBounds.Top - 2 + _scActionMapFilterScrollOffset;
                int itemIndex = (int)(relativeY / itemHeight) - 1;
                _scHoveredActionMapFilter = itemIndex >= -1 && itemIndex < _scActionMaps.Count ? itemIndex : -1;
                Cursor = Cursors.Hand;
            }

            // Action row and cell hover detection
            if (_scBindingsListBounds.Contains(e.X, e.Y) && _scFilteredActions is not null)
            {
                // Find which row is hovered, accounting for scroll offset and group headers
                float rowHeight = 28f;  // Must match drawing code
                float rowGap = 2f;
                float categoryHeaderHeight = 28f;
                float relativeY = e.Y - _scBindingsListBounds.Top + _scBindingsScrollOffset;

                string? lastActionMap = null;
                float currentY = 0;

                for (int i = 0; i < _scFilteredActions.Count; i++)
                {
                    var action = _scFilteredActions[i];

                    // Account for category header
                    if (action.ActionMap != lastActionMap)
                    {
                        lastActionMap = action.ActionMap;
                        currentY += categoryHeaderHeight;

                        // If collapsed, skip all actions in this category
                        if (_scCollapsedCategories.Contains(action.ActionMap))
                        {
                            while (i < _scFilteredActions.Count - 1 &&
                                   _scFilteredActions[i + 1].ActionMap == action.ActionMap)
                            {
                                i++;
                            }
                            continue;
                        }
                    }

                    float rowTop = currentY;
                    float rowBottom = currentY + rowHeight;

                    if (relativeY >= rowTop && relativeY < rowBottom)
                    {
                        _scHoveredActionIndex = i;

                        // Check for cell hover
                        int hoveredCol = GetHoveredColumnIndex(e.X);
                        if (hoveredCol >= 0)
                        {
                            _scHoveredCell = (i, hoveredCol);
                        }

                        Cursor = Cursors.Hand;
                        break;
                    }

                    currentY += rowHeight + rowGap;
                }
            }

            // vJoy mapping rows
            for (int i = 0; i < _scVJoyMappingBounds.Count; i++)
            {
                if (_scVJoyMappingBounds[i].Contains(e.X, e.Y))
                {
                    _hoveredVJoyMapping = i;
                    Cursor = Cursors.Hand;
                    break;
                }
            }

            // Buttons and selectors
            if (_scRefreshButtonBounds.Contains(e.X, e.Y) ||
                _scExportButtonBounds.Contains(e.X, e.Y) ||
                _scInstallationSelectorBounds.Contains(e.X, e.Y) ||
                _scProfileNameBounds.Contains(e.X, e.Y) ||
                _scActionMapFilterBounds.Contains(e.X, e.Y) ||
                _scAssignInputButtonBounds.Contains(e.X, e.Y) ||
                _scClearBindingButtonBounds.Contains(e.X, e.Y))
            {
                Cursor = Cursors.Hand;
            }

            // Check for listening timeout
            if (_scIsListeningForInput && (DateTime.Now - _scListeningStartTime).TotalMilliseconds > SCListeningTimeoutMs)
            {
                _scIsListeningForInput = false;
                _scListeningColumn = null;
            }
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

        // Device category tabs hover detection (for Devices tab)
        _hoveredDeviceCategory = -1;
        if (_activeTab == 0)
        {
            if (_deviceCategoryD1Bounds.Contains(e.X, e.Y))
            {
                _hoveredDeviceCategory = 0;
                Cursor = Cursors.Hand;
            }
            else if (_deviceCategoryD2Bounds.Contains(e.X, e.Y))
            {
                _hoveredDeviceCategory = 1;
                Cursor = Cursors.Hand;
            }
        }

        // Mapping category tabs hover detection (for Mappings tab)
        _hoveredMappingCategory = -1;
        if (_activeTab == 1)
        {
            if (_mappingCategoryButtonsBounds.Contains(e.X, e.Y))
            {
                _hoveredMappingCategory = 0;
                Cursor = Cursors.Hand;
            }
            else if (_mappingCategoryAxesBounds.Contains(e.X, e.Y))
            {
                _hoveredMappingCategory = 1;
                Cursor = Cursors.Hand;
            }
        }

        // Device list hover detection
        float sideTabPad = 8f;  // Reduced padding for side-tabbed panels
        float contentTop = 90;
        float leftPanelWidth = 400f;  // Matches Settings panel width
        float sideTabWidth = 28f;

        if (e.X >= sideTabPad + sideTabWidth && e.X <= sideTabPad + leftPanelWidth)
        {
            float itemY = contentTop + 32 + FUIRenderer.PanelPadding;
            float itemHeight = 60f;
            float itemGap = FUIRenderer.ItemSpacing;

            // Filter devices by category and find hovered index
            var filteredDevices = _deviceCategory == 0
                ? _devices.Where(d => !d.IsVirtual).ToList()
                : _devices.Where(d => d.IsVirtual).ToList();

            int filteredIndex = (int)((e.Y - itemY) / (itemHeight + itemGap));
            if (filteredIndex >= 0 && filteredIndex < filteredDevices.Count)
            {
                // Map back to actual device index
                _hoveredDevice = _devices.IndexOf(filteredDevices[filteredIndex]);
            }
            else
            {
                _hoveredDevice = -1;
            }
        }
        else
        {
            _hoveredDevice = -1;
        }

        // Device action button hover detection
        _map1to1ButtonHovered = !_map1to1ButtonBounds.IsEmpty && _map1to1ButtonBounds.Contains(e.X, e.Y);
        _clearMappingsButtonHovered = !_clearMappingsButtonBounds.IsEmpty && _clearMappingsButtonBounds.Contains(e.X, e.Y);
        _removeDeviceButtonHovered = !_removeDeviceButtonBounds.IsEmpty && _removeDeviceButtonBounds.Contains(e.X, e.Y);
        
        // Forwarding button hover detection
        _startForwardingButtonHovered = !_startForwardingButtonBounds.IsEmpty && _startForwardingButtonBounds.Contains(e.X, e.Y);
        _stopForwardingButtonHovered = !_stopForwardingButtonBounds.IsEmpty && _stopForwardingButtonBounds.Contains(e.X, e.Y);

        // Window controls hover (matches FUIRenderer.DrawWindowControls sizing)
        float pad = FUIRenderer.SpaceLG;  // Standard padding for window controls
        float btnSize = 28f;
        float btnGap = 8f;
        float btnTotalWidth = btnSize * 3 + btnGap * 2; // 100px
        float windowControlsX = ClientSize.Width - pad - btnTotalWidth; // Align with page padding
        float titleBarY = 15;
        if (e.Y >= titleBarY + 12 && e.Y <= titleBarY + 40)
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

        // SVG silhouette hover detection
        if (_silhouetteBounds.Contains(e.X, e.Y) && _joystickSvg is not null)
        {
            var hitControlId = HitTestSvg(new SKPoint(e.X, e.Y));
            if (hitControlId != _hoveredControlId)
            {
                _hoveredControlId = hitControlId;
                Cursor = hitControlId is not null ? Cursors.Hand : Cursors.Default;
            }
        }
        else if (_hoveredControlId is not null)
        {
            _hoveredControlId = null;
        }
    }

    private void OnCanvasMouseDown(object? sender, MouseEventArgs e)
    {
        // Handle right-click
        if (e.Button == MouseButtons.Right)
        {
            // Right-click on SC Bindings tab to clear cell binding
            if (_activeTab == 2)
            {
                HandleBindingsTabRightClick(new SKPoint(e.X, e.Y));
                return;
            }

            // Right-click on curve control points in Mappings tab
            if (_activeTab == 1 && _mappingCategory == 1 && _selectedCurveType == CurveType.Custom)
            {
                var pt = new SKPoint(e.X, e.Y);
                if (_curveEditorBounds.Contains(pt))
                {
                    int pointIndex = FindCurvePointAt(pt, _curveEditorBounds);
                    if (pointIndex >= 0)
                    {
                        RemoveCurveControlPoint(pointIndex);
                        return;
                    }
                }
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
                    _profileService.ActivateProfile(_profiles[_hoveredProfileIndex].Id);
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
                    WindowState = WindowState == FormWindowState.Maximized
                        ? FormWindowState.Normal
                        : FormWindowState.Maximized;
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

        // Device category tab clicks (D1 Physical / D2 Virtual)
        if (_activeTab == 0 && _hoveredDeviceCategory >= 0)
        {
            _deviceCategory = _hoveredDeviceCategory;
            _selectedDevice = -1; // Reset before selecting first in new category
            _currentInputState = null;
            SelectFirstDeviceInCategory();
            return;
        }

        // Mapping category tab clicks (M1 Axes / M2 Buttons)
        if (_activeTab == 1 && _hoveredMappingCategory >= 0)
        {
            _mappingCategory = _hoveredMappingCategory;
            _selectedMappingRow = -1; // Reset selection when switching categories
            _bindingsScrollOffset = 0; // Reset scroll when switching categories
            CancelInputListening();
            return;
        }

        // Device list clicks - initiate potential drag on physical devices
        if (_activeTab == 0 && _deviceCategory == 0 && _hoveredDevice >= 0 && _hoveredDevice < _devices.Count)
        {
            _selectedDevice = _hoveredDevice;
            _currentInputState = null;
            // Load device map for the selected device
            LoadDeviceMapForDevice(_devices[_selectedDevice].Name);
            _activeInputTracker.Clear(); // Clear lead-lines when switching devices

            // Start potential drag - will only become a drag if mouse moves enough
            _dragDeviceIndex = _hoveredDevice;
            _dragStartPoint = new SKPoint(e.X, e.Y);
            _dragCurrentPoint = _dragStartPoint;
        }
        else if (_hoveredDevice >= 0 && _hoveredDevice < _devices.Count)
        {
            // Non-draggable device selection (virtual devices tab)
            _selectedDevice = _hoveredDevice;
            _currentInputState = null;
            LoadDeviceMapForDevice(_devices[_selectedDevice].Name);
            _activeInputTracker.Clear();
        }

        // Device action button clicks (only on Devices tab)
        if (_activeTab == 0)
        {
            if (_map1to1ButtonHovered && !_map1to1ButtonBounds.IsEmpty)
            {
                CreateOneToOneMappings();
                return;
            }
            if (_clearMappingsButtonHovered && !_clearMappingsButtonBounds.IsEmpty)
            {
                ClearDeviceMappings();
                return;
            }
            if (_removeDeviceButtonHovered && !_removeDeviceButtonBounds.IsEmpty)
            {
                RemoveDisconnectedDevice();
                return;
            }
            
            // Forwarding button clicks
            if (_startForwardingButtonHovered && !_startForwardingButtonBounds.IsEmpty)
            {
                StartForwarding();
                return;
            }
            if (_stopForwardingButtonHovered && !_stopForwardingButtonBounds.IsEmpty)
            {
                StopForwarding();
                return;
            }
        }

        // Tab clicks - match positions calculated in DrawTitleBar
        float pad = FUIRenderer.SpaceLG;
        float btnTotalWidth = 28f * 3 + 8f * 2; // Window control buttons
        float windowControlsX = ClientSize.Width - pad - btnTotalWidth;
        float tabWindowGap = 40f;
        float tabSpacing = 90f;
        float lastTabWidth = 70f; // Approximate width of "SETTINGS"
        float totalTabsWidth = tabSpacing * (_tabNames.Length - 1) + lastTabWidth;
        float tabStartX = windowControlsX - tabWindowGap - totalTabsWidth;
        float tabY = 15;

        if (e.Y >= tabY + 20 && e.Y <= tabY + 50)
        {
            for (int i = 0; i < _tabNames.Length; i++)
            {
                float tabX = tabStartX + i * tabSpacing;
                if (e.X >= tabX && e.X < tabX + tabSpacing - 10)
                {
                    if (_activeTab != i)
                    {
                        // Clear any dragging state when switching tabs
                        _draggingBgSlider = null;
                        _draggingPulseDuration = false;
                        _draggingHoldDuration = false;

                        // Reset highlight state when switching to Mappings tab
                        if (i == 1)
                        {
                            _highlightPrevButtonState.Clear();
                            _highlightDebounce.Clear();
                            _highlightedMappingRow = -1;
                        }
                    }
                    _activeTab = i;
                    break;
                }
            }
        }

        // Settings tab click handling
        if (_activeTab == 3)
        {
            HandleSettingsTabClick(new SKPoint(e.X, e.Y));
        }

        // Bindings (SC) tab click handling
        if (_activeTab == 2)
        {
            HandleBindingsTabClick(new SKPoint(e.X, e.Y));
            return;
        }

        // Mappings tab click handling
        if (_activeTab == 1)
        {
            // Right panel: Add input button - toggles listening
            if (_addInputButtonHovered && _selectedMappingRow >= 0)
            {
                if (_isListeningForInput)
                {
                    CancelInputListening();
                }
                else
                {
                    StartInputListening(_selectedMappingRow);
                }
                return;
            }

            // Right panel: Remove input source
            if (_hoveredInputSourceRemove >= 0)
            {
                RemoveInputSourceAtIndex(_hoveredInputSourceRemove);
                return;
            }

            // Right panel: Button mode selection (button category)
            if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _hoveredButtonMode >= 0)
            {
                _selectedButtonMode = (ButtonMode)_hoveredButtonMode;
                UpdateButtonModeForSelected();
                return;
            }

            // Right panel: Pulse duration slider (button category, Pulse mode)
            if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _selectedButtonMode == ButtonMode.Pulse)
            {
                var pt = new SKPoint(e.X, e.Y);
                if (_pulseDurationSliderBounds.Contains(pt))
                {
                    _draggingPulseDuration = true;
                    UpdatePulseDurationFromMouse(e.X);
                    return;
                }
            }

            // Right panel: Hold duration slider (button category, HoldToActivate mode)
            if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _selectedButtonMode == ButtonMode.HoldToActivate)
            {
                var pt = new SKPoint(e.X, e.Y);
                if (_holdDurationSliderBounds.Contains(pt))
                {
                    _draggingHoldDuration = true;
                    UpdateHoldDurationFromMouse(e.X);
                    return;
                }
            }

            // Right panel: Output type selection (button category)
            if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _hoveredOutputType >= 0)
            {
                _outputTypeIsKeyboard = (_hoveredOutputType == 1);
                if (!_outputTypeIsKeyboard)
                {
                    _selectedKeyName = ""; // Clear key when switching to Button mode
                }
                UpdateOutputTypeForSelected();
                return;
            }

            // Right panel: Key clear button (button category)
            if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _outputTypeIsKeyboard && _keyClearButtonHovered)
            {
                ClearKeyboardBinding();
                return;
            }

            // Right panel: Key capture field (button category)
            if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _outputTypeIsKeyboard && _keyCaptureBoundsHovered)
            {
                _isCapturingKey = true;
                _keyCaptureStartTime = DateTime.Now;
                return;
            }

            // Right panel: Clear Mapping button (button category)
            if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _clearAllButtonHovered)
            {
                ClearSelectedButtonMapping();
                return;
            }

            // Right panel: Axis settings - curve type selection (axis category)
            if (_mappingCategory == 1 && _selectedMappingRow >= 0)
            {
                // Check curve preset clicks
                var pt = new SKPoint(e.X, e.Y);
                if (HandleCurvePresetClick(pt))
                    return;

                // Check for curve control point drag start
                if (_selectedCurveType == CurveType.Custom && _curveEditorBounds.Contains(pt))
                {
                    int pointIndex = FindCurvePointAt(pt, _curveEditorBounds);
                    if (pointIndex >= 0)
                    {
                        _draggingCurvePoint = pointIndex;
                        return;
                    }
                    else
                    {
                        // Click in curve area but not on point - add new point
                        var graphPt = CurveScreenToGraph(pt, _curveEditorBounds);
                        AddCurveControlPoint(graphPt);
                        return;
                    }
                }

                // Check deadzone handle click - select and start drag
                int dzHandle = FindDeadzoneHandleAt(pt);
                if (dzHandle >= 0)
                {
                    _selectedDeadzoneHandle = dzHandle;
                    _draggingDeadzoneHandle = dzHandle;
                    _canvas.Invalidate();
                    return;
                }

                // Click on slider background (not on handle) - deselect
                if (_deadzoneSliderBounds.Contains(pt))
                {
                    _selectedDeadzoneHandle = -1;
                    _canvas.Invalidate();
                    return;
                }
            }

            // Left panel: vJoy device navigation
            if (_vjoyPrevHovered && _selectedVJoyDeviceIndex > 0)
            {
                _selectedVJoyDeviceIndex--;
                _selectedMappingRow = -1;
                _bindingsScrollOffset = 0; // Reset scroll when changing device
                CancelInputListening();
                return;
            }
            if (_vjoyNextHovered && _selectedVJoyDeviceIndex < _vjoyDevices.Count - 1)
            {
                _selectedVJoyDeviceIndex++;
                _selectedMappingRow = -1;
                _bindingsScrollOffset = 0; // Reset scroll when changing device
                CancelInputListening();
                return;
            }

            // Left panel: Output row clicked - select it
            if (_hoveredMappingRow >= 0)
            {
                if (_hoveredMappingRow != _selectedMappingRow)
                {
                    // Selecting a different row - cancel listening
                    CancelInputListening();
                    _selectedMappingRow = _hoveredMappingRow;
                    // Load output type state for button rows
                    LoadOutputTypeStateForRow();
                }
                else
                {
                    _selectedMappingRow = _hoveredMappingRow;
                }
                return;
            }
        }

        // SVG control clicks
        if (_hoveredControlId is not null)
        {
            // Check for double-click (same control clicked within 500ms)
            bool isDoubleClick = _lastClickedControlId == _hoveredControlId &&
                                 (DateTime.Now - _lastSvgControlClick).TotalMilliseconds < 500;

            if (isDoubleClick)
            {
                // Double-click - open mapping dialog for this control
                OpenMappingDialogForControl(_hoveredControlId);
                _lastClickedControlId = null;
            }
            else
            {
                // Single click - select the control
                _selectedControlId = _hoveredControlId;
                _leadLineProgress = 0f; // Reset animation for new selection
                _lastClickedControlId = _hoveredControlId;
                _lastSvgControlClick = DateTime.Now;
            }
        }
        else if (_silhouetteBounds.Contains(e.X, e.Y))
        {
            // Clicked inside silhouette but not on a control - deselect
            _selectedControlId = null;
            _lastClickedControlId = null;
        }
    }

    private void OnCanvasMouseLeave(object? sender, EventArgs e)
    {
        _hoveredDevice = -1;
        _hoveredWindowControl = -1;
        _hoveredControlId = null;
        _draggingCurvePoint = -1;
        _draggingDeadzoneHandle = -1;
    }

    private void OnCanvasMouseUp(object? sender, MouseEventArgs e)
    {
        // Complete device drag-to-reorder
        if (_isDraggingDevice && _dragDeviceIndex >= 0 && _dragDeviceIndex < _devices.Count)
        {
            var filteredDevices = _devices.Where(d => !d.IsVirtual).ToList();

            // _dragDeviceIndex is actual index in _devices, find filtered index
            var draggedDevice = _devices[_dragDeviceIndex];
            int sourceFilteredIndex = filteredDevices.IndexOf(draggedDevice);

            // Only reorder if valid source and different target
            if (sourceFilteredIndex >= 0 && _dragDropTargetIndex >= 0 && _dragDropTargetIndex != sourceFilteredIndex)
            {
                // Calculate target filtered index (adjusted for removal)
                int targetFilteredIndex = _dragDropTargetIndex;
                if (targetFilteredIndex > sourceFilteredIndex)
                    targetFilteredIndex--;

                // Get the device at target position (or use end of list)
                int targetActualIndex;
                if (targetFilteredIndex >= 0 && targetFilteredIndex < filteredDevices.Count)
                {
                    var targetDevice = filteredDevices[targetFilteredIndex];
                    targetActualIndex = _devices.IndexOf(targetDevice);
                    // If dropping after source in the filtered list, insert after target
                    if (_dragDropTargetIndex > sourceFilteredIndex)
                        targetActualIndex++;
                }
                else
                {
                    // Dropping at end - find last physical device and insert after it
                    targetActualIndex = _devices.Count;
                    for (int i = _devices.Count - 1; i >= 0; i--)
                    {
                        if (!_devices[i].IsVirtual)
                        {
                            targetActualIndex = i + 1;
                            break;
                        }
                    }
                }

                // Perform the move
                int sourceActualIndex = _devices.IndexOf(draggedDevice);
                _devices.RemoveAt(sourceActualIndex);
                if (targetActualIndex > sourceActualIndex)
                    targetActualIndex--;
                _devices.Insert(targetActualIndex, draggedDevice);

                // Update selection to follow the moved device
                _selectedDevice = _devices.IndexOf(draggedDevice);

                // Save the new order
                SaveDeviceOrder();
            }

            // Reset drag state
            _isDraggingDevice = false;
            _dragDeviceIndex = -1;
            _dragDropTargetIndex = -1;
            Cursor = Cursors.Default;
            _canvas.Invalidate();
            return;
        }

        // Reset potential drag state even if we didn't actually drag
        if (_dragDeviceIndex >= 0)
        {
            _dragDeviceIndex = -1;
            _dragDropTargetIndex = -1;
        }

        // Release SC Bindings scrollbar dragging
        if (_scIsDraggingVScroll || _scIsDraggingHScroll)
        {
            _scIsDraggingVScroll = false;
            _scIsDraggingHScroll = false;
            _canvas.Invalidate();
        }

        if (_draggingCurvePoint >= 0 || _draggingDeadzoneHandle >= 0)
        {
            _draggingCurvePoint = -1;
            _draggingDeadzoneHandle = -1;
            _canvas.Invalidate();
        }

        // Release duration slider dragging
        if (_draggingPulseDuration || _draggingHoldDuration)
        {
            _draggingPulseDuration = false;
            _draggingHoldDuration = false;
            UpdateDurationForSelectedMapping();
            _canvas.Invalidate();
        }

        // Release background slider dragging
        if (_draggingBgSlider is not null)
        {
            _draggingBgSlider = null;
            SaveBackgroundSettings();
            _canvas.Invalidate();
        }
    }

    private void OnCanvasMouseWheel(object? sender, MouseEventArgs e)
    {
        // Handle scroll on action map filter dropdown when open
        if (_activeTab == 2 && _scActionMapFilterDropdownOpen && _scActionMapFilterDropdownBounds.Contains(e.X, e.Y))
        {
            float scrollAmount = -e.Delta / 4f;
            _scActionMapFilterScrollOffset = Math.Clamp(_scActionMapFilterScrollOffset + scrollAmount, 0, _scActionMapFilterMaxScroll);
            _canvas.Invalidate();
            return;
        }

        // Handle scroll on MAPPINGS tab when mouse is over the bindings list
        if (_activeTab == 1 && _bindingsListBounds.Contains(e.X, e.Y))
        {
            float scrollAmount = -e.Delta / 4f; // Delta is usually ±120, divide for smooth scrolling
            float maxScroll = Math.Max(0, _bindingsContentHeight - _bindingsListBounds.Height);

            _bindingsScrollOffset = Math.Clamp(_bindingsScrollOffset + scrollAmount, 0, maxScroll);
            _canvas.Invalidate();
        }

        // Handle scroll on BINDINGS (SC) tab when mouse is over the SC bindings list
        if (_activeTab == 2 && _scBindingsListBounds.Contains(e.X, e.Y))
        {
            float scrollAmount = -e.Delta / 4f;

            // Shift+Wheel = horizontal scroll
            if (Control.ModifierKeys.HasFlag(Keys.Shift))
            {
                float maxHScroll = Math.Max(0, _scGridTotalWidth - _scVisibleDeviceWidth);
                if (maxHScroll > 0)
                {
                    _scGridHorizontalScroll = Math.Clamp(_scGridHorizontalScroll + scrollAmount, 0, maxHScroll);
                }
            }
            else
            {
                // Normal vertical scroll
                float maxScroll = Math.Max(0, _scBindingsContentHeight - _scBindingsListBounds.Height);
                _scBindingsScrollOffset = Math.Clamp(_scBindingsScrollOffset + scrollAmount, 0, maxScroll);
            }
            _canvas.Invalidate();
        }
    }

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
    private int GetHoveredColumnIndex(float x)
    {
        if (_scGridColumns is null || x < _scDeviceColsStart || x > _scDeviceColsStart + _scVisibleDeviceWidth)
            return -1;

        float relativeX = x - _scDeviceColsStart + _scGridHorizontalScroll;

        // Walk through columns to find which one contains this X
        float cumX = 0f;
        for (int c = 0; c < _scGridColumns.Count; c++)
        {
            float colW = _scGridDeviceColWidths.TryGetValue(_scGridColumns[c].Id, out var w) ? w : _scGridDeviceColMinWidth;
            if (relativeX >= cumX && relativeX < cumX + colW)
                return c;
            cumX += colW;
        }

        return -1;
    }

    #endregion

    #region Rendering

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var bounds = new SKRect(0, 0, e.Info.Width, e.Info.Height);

        // Clear to void
        canvas.Clear(FUIColors.Void);

        // Layer 0: Background grid
        DrawBackgroundLayer(canvas, bounds);

        // Layer 1: Main structure panels
        DrawStructureLayer(canvas, bounds);

        // Layer 2: Overlay effects
        DrawOverlayLayer(canvas, bounds);
    }

    private void DrawBackgroundLayer(SKCanvas canvas, SKRect bounds)
    {
        // Render FUI background with all effects
        _background.Render(canvas, bounds);
    }

    private void DrawStructureLayer(SKCanvas canvas, SKRect bounds)
    {
        // Title bar
        DrawTitleBar(canvas, bounds);

        // Main content area
        float pad = FUIRenderer.SpaceLG;
        float gap = FUIRenderer.SpaceMD;
        float contentTop = 90;
        float contentBottom = bounds.Bottom - 55;

        // Calculate panel widths
        // Side-tabbed panels (Devices, Mappings) use reduced left padding to put tabs closer to edge
        float sideTabPad = 8f;  // Reduced padding for panels with side tabs
        float leftPanelWidth = 400f;  // Match Settings panel width
        float rightPanelWidth = 330f;
        float centerStart = sideTabPad + leftPanelWidth + gap;
        float centerEnd = bounds.Right - pad - rightPanelWidth - gap;

        // Content based on active tab
        if (_activeTab == 1) // MAPPINGS tab
        {
            DrawMappingsTabContent(canvas, bounds, sideTabPad, contentTop, contentBottom);
        }
        else if (_activeTab == 2) // BINDINGS tab (Star Citizen integration)
        {
            DrawBindingsTabContent(canvas, bounds, pad, contentTop, contentBottom);
        }
        else if (_activeTab == 3) // SETTINGS tab
        {
            DrawSettingsTabContent(canvas, bounds, pad, contentTop, contentBottom);
        }
        else
        {
            // Tab 0: DEVICES tab layout
            // Left panel: Device List (using reduced padding for side tabs)
            var deviceListBounds = new SKRect(sideTabPad, contentTop, sideTabPad + leftPanelWidth, contentBottom);
            DrawDeviceListPanel(canvas, deviceListBounds);

            // Center panel: Device Details
            var detailsBounds = new SKRect(centerStart, contentTop, centerEnd, contentBottom);
            DrawDeviceDetailsPanel(canvas, detailsBounds);

            // Right panel: Split into Device Actions (top) and Status (bottom)
            float rightPanelX = bounds.Right - pad - rightPanelWidth;
            float rightPanelMid = contentTop + (contentBottom - contentTop) / 2f;
            float panelGap = 8f;

            // Top half: Device Actions panel
            var deviceActionsBounds = new SKRect(rightPanelX, contentTop, bounds.Right - pad, rightPanelMid - panelGap / 2f);
            DrawDeviceActionsPanel(canvas, deviceActionsBounds);

            // Bottom half: Status panel
            var statusBounds = new SKRect(rightPanelX, rightPanelMid + panelGap / 2f, bounds.Right - pad, contentBottom);
            DrawStatusPanel(canvas, statusBounds);
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
            // Get position from profile selector bounds
            DrawProfileDropdown(canvas, _profileSelectorBounds.Left, _profileSelectorBounds.Bottom);
        }
    }

    private void DrawSelector(SKCanvas canvas, SKRect bounds, string text, bool isHovered, bool isEnabled)
    {
        // FUI style matching profile selector dropdown
        var bgColor = isEnabled
            ? (isHovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background1.WithAlpha(150))
            : FUIColors.Background1.WithAlpha(100);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        canvas.DrawRect(bounds, bgPaint);

        // Border - highlight when hovered (FUI style)
        var borderColor = isEnabled
            ? (isHovered ? FUIColors.FrameBright : FUIColors.Frame)
            : FUIColors.Frame.WithAlpha(100);
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = borderColor, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRect(bounds, borderPaint);

        // Truncate text to leave room for arrow prefix and padding
        float textPadding = 5f;
        float arrowPrefixWidth = 16f;  // Space for "▾ "
        float maxTextWidth = bounds.Width - textPadding - arrowPrefixWidth - 8f;
        string truncatedText = TruncateTextToWidth(text, maxTextWidth, 11f);

        // Display text with dropdown arrow prefix (FUI style like profile selector)
        string displayText = isEnabled ? $"▾ {truncatedText}" : truncatedText;
        var textColor = isEnabled ? FUIColors.TextPrimary : FUIColors.TextDim;
        FUIRenderer.DrawText(canvas, displayText, new SKPoint(bounds.Left + textPadding, bounds.MidY + 4), textColor, 11f);
    }

    private void DrawTextFieldReadOnly(SKCanvas canvas, SKRect bounds, string text, bool isHovered)
    {
        var bgColor = isHovered ? FUIColors.Background2.WithAlpha(180) : FUIColors.Background1.WithAlpha(140);
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        canvas.DrawRect(bounds, bgPaint);

        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRect(bounds, borderPaint);

        FUIRenderer.DrawText(canvas, text, new SKPoint(bounds.Left + 10, bounds.MidY + 4), FUIColors.TextPrimary, 11f);
    }



    private void DrawTitleBar(SKCanvas canvas, SKRect bounds)
    {
        float titleBarY = 15;
        float titleBarHeight = 50;
        float pad = FUIRenderer.SpaceLG;

        // Title text - aligned with left panel L-corner frame
        // Panel starts at sideTabPad(8) + sideTabWidth(28) = 36
        float titleX = 36f;
        FUIRenderer.DrawText(canvas, "ASTERIQ", new SKPoint(titleX, titleBarY + 38), FUIColors.Primary, 26f, true);

        // Window controls - always at fixed position from right edge
        float btnTotalWidth = 28f * 3 + 8f * 2; // 3 buttons at 28px + 2 gaps at 8px = 100px
        float windowControlsX = bounds.Right - pad - btnTotalWidth;

        // Navigation tabs - positioned with 40px gap from window controls
        float tabWindowGap = 40f;
        float tabSpacing = 90f;
        using var tabMeasurePaint = FUIRenderer.CreateTextPaint(FUIColors.TextDim, FUIRenderer.ScaleFont(13f));

        // Calculate total tabs width
        float totalTabsWidth = tabSpacing * (_tabNames.Length - 1) + tabMeasurePaint.MeasureText(_tabNames[_tabNames.Length - 1]);
        float tabStartX = windowControlsX - tabWindowGap - totalTabsWidth;

        // Left side elements positioning (title starts at titleX, title is ~145px wide)
        float subtitleX = titleX + 160;
        float profileSelectorWidth = 130f;
        float profileGap = 15f;

        // Subtitle - show if there's room before tabs
        bool showSubtitle = subtitleX + 280 < tabStartX - profileSelectorWidth - profileGap - 20;

        // Profile selector position - after subtitle (or after title if no subtitle)
        float profileSelectorX;
        if (showSubtitle)
        {
            profileSelectorX = subtitleX + 290; // After subtitle
        }
        else
        {
            profileSelectorX = titleX + 160; // After title, where subtitle would be
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
                canvas.DrawLine(subtitleX - 15, titleBarY + 18, subtitleX - 15, titleBarY + 48, sepPaint);
            }
            FUIRenderer.DrawText(canvas, "UNIFIED HOTAS MANAGEMENT SYSTEM", new SKPoint(subtitleX, titleBarY + 38),
                FUIColors.TextDim, 12f);
        }

        // Profile selector (on the left, after subtitle or title)
        if (showProfileSelector)
        {
            DrawProfileSelector(canvas, profileSelectorX, titleBarY + 22);
        }

        // Draw navigation tabs
        float tabX = tabStartX;
        for (int i = 0; i < _tabNames.Length; i++)
        {
            bool isActive = i == _activeTab;
            var tabColor = isActive ? FUIColors.Active : FUIColors.TextDim;

            FUIRenderer.DrawText(canvas, _tabNames[i], new SKPoint(tabX, titleBarY + 38), tabColor, 13f);

            if (isActive)
            {
                float actualTextWidth = tabMeasurePaint.MeasureText(_tabNames[i]);

                using var paint = new SKPaint
                {
                    Color = FUIColors.Active,
                    StrokeWidth = 2f,
                    IsAntialias = true
                };
                canvas.DrawLine(tabX, titleBarY + 44, tabX + actualTextWidth, titleBarY + 44, paint);

                using var glowPaint = new SKPaint
                {
                    Color = FUIColors.ActiveGlow,
                    StrokeWidth = 6f,
                    ImageFilter = SKImageFilter.CreateBlur(4f, 4f)
                };
                canvas.DrawLine(tabX, titleBarY + 44, tabX + actualTextWidth, titleBarY + 44, glowPaint);
            }

            tabX += tabSpacing;
        }

        // Window controls - always drawn
        FUIRenderer.DrawWindowControls(canvas, windowControlsX, titleBarY + 12,
            _hoveredWindowControl == 0, _hoveredWindowControl == 1, _hoveredWindowControl == 2);
    }

    private void DrawProfileSelector(SKCanvas canvas, float x, float y)
    {
        float width = 130f;  // Wider to accommodate longer names
        float height = 26f;
        _profileSelectorBounds = new SKRect(x, y, x + width, y + height);

        // Get profile name
        string profileName = _profileService.HasActiveProfile
            ? _profileService.ActiveProfile!.Name
            : "No Profile";

        // Measure text to determine truncation
        float maxTextWidth = width - 25f; // Account for arrow and padding
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

        // Profile name (with dropdown arrow prefix)
        string displayText = $"▾ {profileName}";
        FUIRenderer.DrawText(canvas, displayText, new SKPoint(x + 5, y + 17),
            _profileDropdownOpen ? FUIColors.Active : FUIColors.TextPrimary, 11f);

        // Note: Dropdown is drawn separately in DrawOpenDropdowns() to render on top of all panels
    }

    private void DrawProfileDropdown(SKCanvas canvas, float x, float y)
    {
        float itemHeight = 26f;
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
            bool isActive = _profileService.ActiveProfile?.Id == profile.Id;

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
        bool canExport = _profileService.ActiveProfile is not null;
        DrawDropdownItem(canvas, x, itemY, width, itemHeight, "↑ Export...",
            _hoveredProfileIndex == _profiles.Count + 2, false, canExport);
    }

    private void DrawDropdownItem(SKCanvas canvas, float x, float itemY, float width, float itemHeight,
        string text, bool isHovered, bool isActive, bool isEnabled)
    {
        var itemBounds = new SKRect(x + 4, itemY, x + width - 4, itemY + itemHeight);

        if (isHovered && isEnabled)
        {
            // Hover background
            using var hoverPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = FUIColors.Active.WithAlpha(40),
                IsAntialias = true
            };
            canvas.DrawRect(itemBounds, hoverPaint);

            // Left accent bar
            using var accentPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = FUIColors.Active,
                IsAntialias = true
            };
            canvas.DrawRect(new SKRect(x + 4, itemY + 2, x + 6, itemY + itemHeight - 2), accentPaint);
        }

        var color = !isEnabled ? FUIColors.TextDisabled
            : isHovered ? FUIColors.TextBright
            : FUIColors.TextDim;
        FUIRenderer.DrawText(canvas, text, new SKPoint(x + 12, itemY + 17), color, 11f);
    }

    private void DrawVerticalSideTab(SKCanvas canvas, SKRect bounds, string label, bool isSelected, bool isHovered)
    {
        // No background box - minimalist style like reference image
        // Just text and accent bar for selected state

        // Accent bar on right edge for selected tab (facing the content)
        if (isSelected)
        {
            using var accentPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.Active,
                StrokeWidth = 3f,
                IsAntialias = true
            };
            canvas.DrawLine(bounds.Right - 1, bounds.Top + 5, bounds.Right - 1, bounds.Bottom - 5, accentPaint);

            // Add glow effect for selected
            using var glowPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.Active.WithAlpha(60),
                StrokeWidth = 8f,
                IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f)
            };
            canvas.DrawLine(bounds.Right - 1, bounds.Top + 5, bounds.Right - 1, bounds.Bottom - 5, glowPaint);
        }

        // Vertical text (rotated 90 degrees, reading bottom-to-top)
        canvas.Save();
        canvas.Translate(bounds.MidX - 2, bounds.MidY);
        canvas.RotateDegrees(-90);

        var textColor = isSelected ? FUIColors.Active : (isHovered ? FUIColors.TextBright : FUIColors.TextDim.WithAlpha(150));
        using var textPaint = new SKPaint
        {
            Color = textColor,
            TextSize = 10f,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
            TextAlign = SKTextAlign.Center
        };
        canvas.DrawText(label, 0, 4f, textPaint);
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

        // Right: version
        FUIRenderer.DrawText(canvas, $"v0.7.0 | {DateTime.Now:HH:mm:ss}",
            new SKPoint(bounds.Right - 160, y + 22), FUIColors.TextDim, 12f);
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
        if (_profileService.ActiveProfile is null)
            return;

        var profile = _profileService.ActiveProfile;

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

        _profileService.SaveActiveProfile();

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
        if (_profileService.ActiveProfile is null)
            return;

        var savedOrder = _profileService.ActiveProfile.DeviceOrder;
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

    #region Cleanup

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Save window state before closing
        if (WindowState == FormWindowState.Normal)
        {
            _profileService?.SaveWindowState(Width, Height, Left, Top);
        }

        _renderTimer?.Stop();
        _renderTimer?.Dispose();
        _inputService?.StopPolling();
        _inputService?.Dispose();
        _joystickSvg?.Dispose();
        _throttleSvg?.Dispose();
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
