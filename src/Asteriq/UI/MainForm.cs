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
public class MainForm : Form
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
    private bool _isCapturingKey = false;

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
            if (imported != null)
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
        if (_profileService.ActiveProfile == null)
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
                if (map != null)
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
                        System.Diagnostics.Debug.WriteLine($"Loaded device map (name match): {path} for device: {deviceName}");
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

            if (_deviceMap != null && isLeftHand)
            {
                // Override mirror setting for left-hand devices using generic map
                _deviceMap.Mirror = true;
                System.Diagnostics.Debug.WriteLine($"Loaded default device map with MIRROR for left-hand device: {deviceName}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Loaded default device map: joystick.json for device: {deviceName}");
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
        if (_deviceMap == null)
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
        if (transform != null && transform.StartsWith("translate("))
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
        Size = new Size(1280, 800);
        MinimumSize = new Size(1024, 768);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.Black;
        DoubleBuffered = true;
        KeyPreview = true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Handle key capture for keyboard output mapping
        if (_isCapturingKey)
        {
            // Extract modifiers and key name
            var (keyName, modifiers) = GetKeyNameAndModifiersFromKeys(keyData);
            if (!string.IsNullOrEmpty(keyName))
            {
                _selectedKeyName = keyName;
                _selectedModifiers = modifiers.Count > 0 ? modifiers : null;
                _isCapturingKey = false;
                UpdateKeyNameForSelected();
            }
            return true; // Consume the key
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
        }

        return base.ProcessCmdKey(ref msg, keyData);
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
        if (_selectedDevice >= 0 && _selectedDevice < _devices.Count &&
            state.DeviceIndex == _devices[_selectedDevice].DeviceIndex)
        {
            _currentInputState = state;

            // Track input activity for dynamic lead-lines
            TrackInputActivity(state);
        }
    }

    private void TrackInputActivity(DeviceInputState state)
    {
        if (_deviceMap == null) return;

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

            if (disconnected != null)
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

            if (disconnectedDevice != null && !disconnectedDevice.IsVirtual)
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
        if (selectedGuid == null && selectedName == null)
            return;

        // Try to find the device by GUID first, then by name
        int newIndex = -1;
        for (int i = 0; i < _devices.Count; i++)
        {
            if (_devices[i].InstanceGuid == selectedGuid ||
                (selectedName != null && _devices[i].Name == selectedName))
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
                if (devices != null)
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

        // Handle background slider dragging (works across all tabs since it's global)
        if (_draggingBgSlider != null)
        {
            UpdateBgSliderFromPoint(e.X);
            return;
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

                // Key capture field
                if (_outputTypeIsKeyboard && _keyCaptureBounds.Contains(e.X, e.Y))
                {
                    _keyCaptureBoundsHovered = true;
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
        if (_silhouetteBounds.Contains(e.X, e.Y) && _joystickSvg != null)
        {
            var hitControlId = HitTestSvg(new SKPoint(e.X, e.Y));
            if (hitControlId != _hoveredControlId)
            {
                _hoveredControlId = hitControlId;
                Cursor = hitControlId != null ? Cursors.Hand : Cursors.Default;
            }
        }
        else if (_hoveredControlId != null)
        {
            _hoveredControlId = null;
        }
    }

    private void OnCanvasMouseDown(object? sender, MouseEventArgs e)
    {
        // Handle right-click to remove curve control points
        if (e.Button == MouseButtons.Right)
        {
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

        // Device list clicks
        if (_hoveredDevice >= 0 && _hoveredDevice < _devices.Count)
        {
            _selectedDevice = _hoveredDevice;
            _currentInputState = null;
            // Load device map for the selected device
            LoadDeviceMapForDevice(_devices[_selectedDevice].Name);
            _activeInputTracker.Clear(); // Clear lead-lines when switching devices
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

            // Right panel: Key capture field (button category)
            if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _outputTypeIsKeyboard && _keyCaptureBoundsHovered)
            {
                _isCapturingKey = true;
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
        if (_hoveredControlId != null)
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
        if (_draggingBgSlider != null)
        {
            _draggingBgSlider = null;
            SaveBackgroundSettings();
            _canvas.Invalidate();
        }
    }

    private void OnCanvasMouseWheel(object? sender, MouseEventArgs e)
    {
        // Only handle scroll on MAPPINGS tab when mouse is over the bindings list
        if (_activeTab == 1 && _bindingsListBounds.Contains(e.X, e.Y))
        {
            float scrollAmount = -e.Delta / 4f; // Delta is usually ±120, divide for smooth scrolling
            float maxScroll = Math.Max(0, _bindingsContentHeight - _bindingsListBounds.Height);

            _bindingsScrollOffset = Math.Clamp(_bindingsScrollOffset + scrollAmount, 0, maxScroll);
            _canvas.Invalidate();
        }
    }

    private string? HitTestSvg(SKPoint screenPoint)
    {
        if (_joystickSvg?.Picture == null || _controlBounds.Count == 0) return null;

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
        else if (_activeTab == 3) // SETTINGS tab
        {
            DrawSettingsTabContent(canvas, bounds, pad, contentTop, contentBottom);
        }
        else
        {
            // Default: Device tab layout
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

    private void DrawSettingsTabContent(SKCanvas canvas, SKRect bounds, float pad, float contentTop, float contentBottom)
    {
        float frameInset = 5f;
        var contentBounds = new SKRect(pad, contentTop, bounds.Right - pad, contentBottom);

        // Two-panel layout: Left (profile management) | Right (application settings)
        float leftPanelWidth = 400f;
        float rightPanelWidth = contentBounds.Width - leftPanelWidth - 15;

        var leftBounds = new SKRect(contentBounds.Left, contentBounds.Top,
            contentBounds.Left + leftPanelWidth, contentBounds.Bottom);
        var rightBounds = new SKRect(leftBounds.Right + 15, contentBounds.Top,
            contentBounds.Right, contentBounds.Bottom);

        // LEFT PANEL - Profile Management
        DrawProfileManagementPanel(canvas, leftBounds, frameInset);

        // RIGHT PANEL - Application Settings
        DrawApplicationSettingsPanel(canvas, rightBounds, frameInset);
    }

    private void DrawProfileManagementPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(160),
            IsAntialias = true
        };
        canvas.DrawRect(bounds.Inset(frameInset, frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Primary, 30f, 8f);

        // Consistent padding from L-corner frame (same for left and top)
        float cornerPadding = 20f;
        float y = bounds.Top + frameInset + cornerPadding;
        float leftMargin = bounds.Left + frameInset + cornerPadding;
        float rightMargin = bounds.Right - frameInset - 15;
        float width = rightMargin - leftMargin;
        float sectionSpacing = FUIRenderer.ScaleLineHeight(18f);
        float rowHeight = FUIRenderer.ScaleLineHeight(18f);

        // Title
        FUIRenderer.DrawText(canvas, "PROFILE MANAGEMENT", new SKPoint(leftMargin, y), FUIColors.TextBright, 14f, true);
        y += FUIRenderer.ScaleLineHeight(30f);

        // Current profile info
        var profile = _profileService.ActiveProfile;
        if (profile != null)
        {
            // Active profile header
            FUIRenderer.DrawText(canvas, "ACTIVE PROFILE", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
            y += sectionSpacing;

            // Profile name with highlight
            float nameBoxHeight = FUIRenderer.ScaleLineHeight(32f);
            var nameBounds = new SKRect(leftMargin, y, rightMargin, y + nameBoxHeight);
            using var nameBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(30) };
            canvas.DrawRoundRect(nameBounds, 4, 4, nameBgPaint);

            using var nameFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active, StrokeWidth = 1f };
            canvas.DrawRoundRect(nameBounds, 4, 4, nameFramePaint);

            float nameTextY = y + (nameBoxHeight - FUIRenderer.ScaleFont(13f)) / 2 + FUIRenderer.ScaleFont(13f) - 3;
            FUIRenderer.DrawText(canvas, profile.Name, new SKPoint(leftMargin + 10, nameTextY), FUIColors.TextBright, 13f, true);
            y += nameBoxHeight + FUIRenderer.ScaleLineHeight(12f);

            // Profile stats
            float lineHeight = FUIRenderer.ScaleLineHeight(18f);
            FUIRenderer.DrawText(canvas, "STATISTICS", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
            y += lineHeight;

            DrawProfileStat(canvas, leftMargin, y, "Axis Mappings", profile.AxisMappings.Count.ToString());
            y += lineHeight;
            DrawProfileStat(canvas, leftMargin, y, "Button Mappings", profile.ButtonMappings.Count.ToString());
            y += lineHeight;
            DrawProfileStat(canvas, leftMargin, y, "Hat Mappings", profile.HatMappings.Count.ToString());
            y += lineHeight;
            DrawProfileStat(canvas, leftMargin, y, "Shift Layers", profile.ShiftLayers.Count.ToString());
            y += lineHeight + FUIRenderer.ScaleSpacing(6f);

            // Timestamps
            DrawProfileStat(canvas, leftMargin, y, "Created", profile.CreatedAt.ToLocalTime().ToString("g"));
            y += lineHeight;
            DrawProfileStat(canvas, leftMargin, y, "Modified", profile.ModifiedAt.ToLocalTime().ToString("g"));
            y += lineHeight + FUIRenderer.ScaleSpacing(10f);
        }
        else
        {
            // No profile active
            FUIRenderer.DrawText(canvas, "No profile active", new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
            y += FUIRenderer.ScaleLineHeight(40f);
        }

        // Actions section
        FUIRenderer.DrawText(canvas, "ACTIONS", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += sectionSpacing;

        // Action buttons - scale height for larger fonts
        float buttonHeight = FUIRenderer.ScaleLineHeight(28f);
        float buttonGap = FUIRenderer.ScaleSpacing(8f);

        // New Profile button
        DrawSettingsButton(canvas, new SKRect(leftMargin, y, leftMargin + (width - buttonGap) / 2, y + buttonHeight), "New Profile", false);
        // Duplicate button
        DrawSettingsButton(canvas, new SKRect(rightMargin - (width - buttonGap) / 2, y, rightMargin, y + buttonHeight),
            profile != null ? "Duplicate" : "---", profile == null);
        y += buttonHeight + buttonGap;

        // Import/Export buttons
        DrawSettingsButton(canvas, new SKRect(leftMargin, y, leftMargin + (width - buttonGap) / 2, y + buttonHeight), "Import", false);
        DrawSettingsButton(canvas, new SKRect(rightMargin - (width - buttonGap) / 2, y, rightMargin, y + buttonHeight),
            profile != null ? "Export" : "---", profile == null);
        y += buttonHeight + buttonGap;

        // Delete button (danger)
        if (profile != null)
        {
            var deleteBounds = new SKRect(leftMargin, y, rightMargin, y + buttonHeight);
            using var delBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Danger.WithAlpha(30) };
            canvas.DrawRoundRect(deleteBounds, 4, 4, delBgPaint);

            using var delFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Danger.WithAlpha(150), StrokeWidth = 1f };
            canvas.DrawRoundRect(deleteBounds, 4, 4, delFramePaint);

            FUIRenderer.DrawTextCentered(canvas, "Delete Profile", deleteBounds, FUIColors.Danger, 11f);
            y += buttonHeight + FUIRenderer.ScaleLineHeight(20f);

            // Shift Layers section
            DrawShiftLayersSection(canvas, leftMargin, rightMargin, y, bounds.Bottom - frameInset - 15, profile);
        }
    }

    private void DrawShiftLayersSection(SKCanvas canvas, float leftMargin, float rightMargin, float y, float bottom, MappingProfile profile)
    {
        float width = rightMargin - leftMargin;
        float lineHeight = FUIRenderer.ScaleLineHeight(16f);

        // Section header
        FUIRenderer.DrawText(canvas, "SHIFT LAYERS", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += lineHeight;

        // Shift layers explanation
        FUIRenderer.DrawText(canvas, "Hold a button to activate alternative mappings", new SKPoint(leftMargin, y), FUIColors.TextDim, 9f);
        y += lineHeight + 4;

        // List existing shift layers
        float layerRowHeight = 36f;
        foreach (var layer in profile.ShiftLayers)
        {
            if (y + layerRowHeight > bottom - 50) break;

            // Layer row background
            var rowBounds = new SKRect(leftMargin, y, rightMargin, y + layerRowHeight - 4);
            using var rowBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
            canvas.DrawRoundRect(rowBounds, 4, 4, rowBgPaint);

            using var rowFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
            canvas.DrawRoundRect(rowBounds, 4, 4, rowFramePaint);

            // Layer name
            FUIRenderer.DrawText(canvas, layer.Name, new SKPoint(leftMargin + 10, y + 11), FUIColors.TextPrimary, 11f);

            // Activator button info
            string activatorText = layer.ActivatorButton != null
                ? $"Button {layer.ActivatorButton.Index + 1} on {layer.ActivatorButton.DeviceName}"
                : "Not assigned";
            FUIRenderer.DrawText(canvas, activatorText, new SKPoint(leftMargin + 100, y + 11),
                layer.ActivatorButton != null ? FUIColors.TextDim : FUIColors.Warning.WithAlpha(150), 9f);

            // Delete button
            float delSize = 20f;
            var delBounds = new SKRect(rightMargin - delSize - 8, y + (layerRowHeight - delSize) / 2 - 2,
                rightMargin - 8, y + (layerRowHeight + delSize) / 2 - 2);

            using var delPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Danger.WithAlpha(60) };
            canvas.DrawRoundRect(delBounds, 2, 2, delPaint);
            FUIRenderer.DrawTextCentered(canvas, "x", delBounds, FUIColors.Danger, 12f);

            y += layerRowHeight;
        }

        // Add new layer button
        if (y + 35 < bottom)
        {
            var addBounds = new SKRect(leftMargin, y, rightMargin, y + 30);
            using var addBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Success.WithAlpha(20) };
            canvas.DrawRoundRect(addBounds, 4, 4, addBgPaint);

            using var addFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Success.WithAlpha(100), StrokeWidth = 1f };
            canvas.DrawRoundRect(addBounds, 4, 4, addFramePaint);

            FUIRenderer.DrawTextCentered(canvas, "+ Add Shift Layer", addBounds, FUIColors.Success, 11f);
        }
    }

    private void DrawProfileStat(SKCanvas canvas, float x, float y, string label, string value, float valueOffset = 130f)
    {
        FUIRenderer.DrawText(canvas, label, new SKPoint(x, y), FUIColors.TextDim, 10f);
        FUIRenderer.DrawText(canvas, value, new SKPoint(x + valueOffset, y), FUIColors.TextPrimary, 10f);
    }

    private void DrawSettingsButton(SKCanvas canvas, SKRect bounds, string text, bool disabled)
    {
        var bgColor = disabled ? FUIColors.Background2.WithAlpha(100) : FUIColors.Background2;
        var frameColor = disabled ? FUIColors.Frame.WithAlpha(80) : FUIColors.Frame;
        var textColor = disabled ? FUIColors.TextDim.WithAlpha(100) : FUIColors.TextPrimary;

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRoundRect(bounds, 4, 4, bgPaint);

        using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = frameColor, StrokeWidth = 1f };
        canvas.DrawRoundRect(bounds, 4, 4, framePaint);

        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 11f);
    }

    private void DrawApplicationSettingsPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Split into left (system settings) and right (visual settings) sub-panels
        float gap = 10f;
        float leftWidth = (bounds.Width - gap) * 0.45f;  // System settings - narrower
        float rightWidth = (bounds.Width - gap) * 0.55f; // Visual settings - wider for sliders

        var leftBounds = new SKRect(bounds.Left, bounds.Top, bounds.Left + leftWidth, bounds.Bottom);
        var rightBounds = new SKRect(bounds.Left + leftWidth + gap, bounds.Top, bounds.Right, bounds.Bottom);

        // LEFT: System Settings
        DrawSystemSettingsSubPanel(canvas, leftBounds, frameInset);

        // RIGHT: Visual Settings (Theme, Background)
        DrawVisualSettingsSubPanel(canvas, rightBounds, frameInset);
    }

    private void DrawSystemSettingsSubPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(160),
            IsAntialias = true
        };
        canvas.DrawRect(bounds.Inset(frameInset, frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Primary, 30f, 8f);

        float cornerPadding = 20f;
        float y = bounds.Top + frameInset + cornerPadding;
        float leftMargin = bounds.Left + frameInset + cornerPadding;
        float rightMargin = bounds.Right - frameInset - 15;
        float sectionSpacing = FUIRenderer.ScaleLineHeight(20f);
        float rowHeight = FUIRenderer.ScaleLineHeight(24f);

        // Title
        FUIRenderer.DrawText(canvas, "SYSTEM", new SKPoint(leftMargin, y), FUIColors.TextBright, 14f, true);
        y += FUIRenderer.ScaleLineHeight(30f);

        // Auto-load setting
        FUIRenderer.DrawText(canvas, "Auto-load last profile", new SKPoint(leftMargin, y + 4), FUIColors.TextPrimary, 11f);
        _autoLoadToggleBounds = new SKRect(rightMargin - 45, y, rightMargin, y + rowHeight);
        DrawToggleSwitch(canvas, _autoLoadToggleBounds, _profileService.AutoLoadLastProfile);
        y += rowHeight + sectionSpacing;

        // Font size section
        FUIRenderer.DrawText(canvas, "Font Size", new SKPoint(leftMargin, y + 6), FUIColors.TextPrimary, 11f);

        FontSizeOption[] fontSizeValues = { FontSizeOption.Small, FontSizeOption.Medium, FontSizeOption.Large };
        float[] fontSizePreviews = { 9f, 11f, 13f };
        float fontBtnWidth = 36f;
        float fontBtnHeight = 24f;
        float fontBtnGap = 3f;
        float fontBtnsStartX = rightMargin - (fontBtnWidth * 3 + fontBtnGap * 2);

        for (int i = 0; i < fontSizeValues.Length; i++)
        {
            var fontBounds = new SKRect(
                fontBtnsStartX + i * (fontBtnWidth + fontBtnGap), y,
                fontBtnsStartX + i * (fontBtnWidth + fontBtnGap) + fontBtnWidth, y + fontBtnHeight);

            _fontSizeButtonBounds[i] = fontBounds;

            bool isActive = _profileService.FontSize == fontSizeValues[i];
            var bgColor = isActive ? FUIColors.Active.WithAlpha(60) : FUIColors.Background2;
            var frameColor = isActive ? FUIColors.Active : FUIColors.Frame;
            var textColor = isActive ? FUIColors.TextBright : FUIColors.TextDim;

            using var fontBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
            canvas.DrawRect(fontBounds, fontBgPaint);

            using var fontFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = frameColor, StrokeWidth = isActive ? 1.5f : 1f };
            canvas.DrawRect(fontBounds, fontFramePaint);

            FUIRenderer.DrawTextCentered(canvas, "aA", fontBounds, textColor, fontSizePreviews[i], scaleFont: false);
        }
        y += fontBtnHeight + sectionSpacing;

        // vJoy section
        FUIRenderer.DrawText(canvas, "VJOY STATUS", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += sectionSpacing;

        var devices = _vjoyService.EnumerateDevices();
        bool vjoyEnabled = devices.Count > 0;
        string vjoyStatus = vjoyEnabled ? "Driver installed and active" : "Driver not available";
        var statusColor = vjoyEnabled ? FUIColors.Success : FUIColors.Danger;

        // Measure text height for proper vertical centering of dot
        float statusTextSize = FUIRenderer.ScaleFont(11f);
        float statusLineHeight = statusTextSize + 4; // Approximate line height
        float statusDotRadius = 4f;
        float statusDotY = y + (statusLineHeight / 2); // Center dot vertically with text

        using var statusDot = new SKPaint { Style = SKPaintStyle.Fill, Color = statusColor, IsAntialias = true };
        canvas.DrawCircle(leftMargin + statusDotRadius + 1, statusDotY, statusDotRadius, statusDot);
        FUIRenderer.DrawText(canvas, vjoyStatus, new SKPoint(leftMargin + statusDotRadius * 2 + 8, y), vjoyEnabled ? FUIColors.TextPrimary : FUIColors.Danger, 11f);
        y += rowHeight;

        if (vjoyEnabled)
        {
            FUIRenderer.DrawText(canvas, $"Available devices: {devices.Count}", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        }
        y += rowHeight + sectionSpacing;

        // Keyboard simulation section
        FUIRenderer.DrawText(canvas, "KEYBOARD OUTPUT", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += sectionSpacing;

        float fieldHeight = FUIRenderer.ScaleLineHeight(26f);
        float textVerticalOffset = (fieldHeight - FUIRenderer.ScaleFont(11f)) / 2 + FUIRenderer.ScaleFont(11f) - 2;
        FUIRenderer.DrawText(canvas, "Key repeat delay (ms)", new SKPoint(leftMargin, y + textVerticalOffset - FUIRenderer.ScaleFont(11f) + 4), FUIColors.TextPrimary, 11f);
        DrawSettingsValueField(canvas, new SKRect(rightMargin - 70, y, rightMargin, y + fieldHeight), "50");
        y += fieldHeight + 8;

        FUIRenderer.DrawText(canvas, "Key repeat rate (ms)", new SKPoint(leftMargin, y + textVerticalOffset - FUIRenderer.ScaleFont(11f) + 4), FUIColors.TextPrimary, 11f);
        DrawSettingsValueField(canvas, new SKRect(rightMargin - 70, y, rightMargin, y + fieldHeight), "30");
    }

    private void DrawVisualSettingsSubPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(160),
            IsAntialias = true
        };
        canvas.DrawRect(bounds.Inset(frameInset, frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Primary, 30f, 8f);

        float cornerPadding = 20f;
        float y = bounds.Top + frameInset + cornerPadding;
        float leftMargin = bounds.Left + frameInset + cornerPadding;
        float rightMargin = bounds.Right - frameInset - 15;
        float sectionSpacing = FUIRenderer.ScaleLineHeight(16f);
        float rowHeight = FUIRenderer.ScaleLineHeight(24f);

        // Title
        FUIRenderer.DrawText(canvas, "VISUAL", new SKPoint(leftMargin, y), FUIColors.TextBright, 14f, true);
        y += FUIRenderer.ScaleLineHeight(30f);

        // Theme section - Core themes
        FUIRenderer.DrawText(canvas, "Core", new SKPoint(leftMargin, y + 4), FUIColors.TextDim, 9f);

        FUITheme[] coreThemes = { FUITheme.Midnight, FUITheme.Matrix, FUITheme.Amber, FUITheme.Ice };
        string[] coreNames = { "MID", "MTX", "AMB", "ICE" };
        SKColor[] coreColors = {
            new SKColor(0x40, 0xA0, 0xFF),  // Midnight - blue
            new SKColor(0x40, 0xFF, 0x40),  // Matrix - green
            new SKColor(0xFF, 0xA0, 0x40),  // Amber - orange
            new SKColor(0x40, 0xE0, 0xFF)   // Ice - cyan
        };

        float themeBtnWidth = 38f;
        float themeBtnHeight = 20f;
        float themeBtnGap = 3f;
        float themeBtnsStartX = leftMargin + 35f;

        for (int i = 0; i < coreThemes.Length; i++)
        {
            var themeBounds = new SKRect(
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap), y,
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap) + themeBtnWidth, y + themeBtnHeight);

            StoreThemeButtonBounds(i, themeBounds);
            DrawThemeButton(canvas, themeBounds, coreNames[i], coreColors[i], FUIColors.CurrentTheme == coreThemes[i]);
        }
        y += themeBtnHeight + 6;

        // Manufacturer themes - Row 1
        FUIRenderer.DrawText(canvas, "Mfr", new SKPoint(leftMargin, y + 4), FUIColors.TextDim, 9f);

        FUITheme[] mfrThemes1 = { FUITheme.Drake, FUITheme.Aegis, FUITheme.Anvil, FUITheme.Argo };
        string[] mfrNames1 = { "DRK", "AEG", "ANV", "ARG" };
        SKColor[] mfrColors1 = {
            new SKColor(0xFF, 0x80, 0x20),  // Drake - orange
            new SKColor(0x40, 0x90, 0xE0),  // Aegis - blue
            new SKColor(0x90, 0xC0, 0x40),  // Anvil - olive
            new SKColor(0xFF, 0xC0, 0x00)   // Argo - yellow
        };

        for (int i = 0; i < mfrThemes1.Length; i++)
        {
            var themeBounds = new SKRect(
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap), y,
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap) + themeBtnWidth, y + themeBtnHeight);

            StoreThemeButtonBounds(4 + i, themeBounds);
            DrawThemeButton(canvas, themeBounds, mfrNames1[i], mfrColors1[i], FUIColors.CurrentTheme == mfrThemes1[i]);
        }
        y += themeBtnHeight + 4;

        // Manufacturer themes - Row 2
        FUITheme[] mfrThemes2 = { FUITheme.Crusader, FUITheme.Origin, FUITheme.MISC, FUITheme.RSI };
        string[] mfrNames2 = { "CRU", "ORI", "MSC", "RSI" };
        SKColor[] mfrColors2 = {
            new SKColor(0x40, 0x90, 0xE0),  // Crusader - blue
            new SKColor(0xD4, 0xAF, 0x37),  // Origin - gold
            new SKColor(0x40, 0xC0, 0x90),  // MISC - teal
            new SKColor(0x50, 0xA0, 0xF0)   // RSI - blue
        };

        for (int i = 0; i < mfrThemes2.Length; i++)
        {
            var themeBounds = new SKRect(
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap), y,
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap) + themeBtnWidth, y + themeBtnHeight);

            StoreThemeButtonBounds(8 + i, themeBounds);
            DrawThemeButton(canvas, themeBounds, mfrNames2[i], mfrColors2[i], FUIColors.CurrentTheme == mfrThemes2[i]);
        }
        y += themeBtnHeight + sectionSpacing;

        // Background effects section
        FUIRenderer.DrawText(canvas, "BACKGROUND", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += sectionSpacing;

        // Slider layout with more space
        float labelWidth = 70f;
        float valueWidth = 28f;
        float sliderLeft = leftMargin + labelWidth;
        float sliderRight = rightMargin - valueWidth - 8;
        float sliderRowHeight = 22f;
        float sliderRowGap = 8f;

        // Grid strength slider
        FUIRenderer.DrawText(canvas, "Grid", new SKPoint(leftMargin, y + 5), FUIColors.TextPrimary, 11f);
        _bgGridSliderBounds = new SKRect(sliderLeft, y + 3, sliderRight, y + sliderRowHeight - 3);
        DrawSettingsSlider(canvas, _bgGridSliderBounds, _background.GridStrength, 100);
        FUIRenderer.DrawText(canvas, _background.GridStrength.ToString(), new SKPoint(sliderRight + 10, y + 5), FUIColors.TextDim, 10f);
        y += sliderRowHeight + sliderRowGap;

        // Glow intensity slider
        FUIRenderer.DrawText(canvas, "Glow", new SKPoint(leftMargin, y + 5), FUIColors.TextPrimary, 11f);
        _bgGlowSliderBounds = new SKRect(sliderLeft, y + 3, sliderRight, y + sliderRowHeight - 3);
        DrawSettingsSlider(canvas, _bgGlowSliderBounds, _background.GlowIntensity, 100);
        FUIRenderer.DrawText(canvas, _background.GlowIntensity.ToString(), new SKPoint(sliderRight + 10, y + 5), FUIColors.TextDim, 10f);
        y += sliderRowHeight + sliderRowGap;

        // Noise intensity slider
        FUIRenderer.DrawText(canvas, "Noise", new SKPoint(leftMargin, y + 5), FUIColors.TextPrimary, 11f);
        _bgNoiseSliderBounds = new SKRect(sliderLeft, y + 3, sliderRight, y + sliderRowHeight - 3);
        DrawSettingsSlider(canvas, _bgNoiseSliderBounds, _background.NoiseIntensity, 100);
        FUIRenderer.DrawText(canvas, _background.NoiseIntensity.ToString(), new SKPoint(sliderRight + 10, y + 5), FUIColors.TextDim, 10f);
        y += sliderRowHeight + sliderRowGap;

        // Scanline intensity slider
        FUIRenderer.DrawText(canvas, "Scanlines", new SKPoint(leftMargin, y + 5), FUIColors.TextPrimary, 11f);
        _bgScanlineSliderBounds = new SKRect(sliderLeft, y + 3, sliderRight, y + sliderRowHeight - 3);
        DrawSettingsSlider(canvas, _bgScanlineSliderBounds, _background.ScanlineIntensity, 100);
        FUIRenderer.DrawText(canvas, _background.ScanlineIntensity.ToString(), new SKPoint(sliderRight + 10, y + 5), FUIColors.TextDim, 10f);
        y += sliderRowHeight + sliderRowGap;

        // Vignette intensity slider
        FUIRenderer.DrawText(canvas, "Vignette", new SKPoint(leftMargin, y + 5), FUIColors.TextPrimary, 11f);
        _bgVignetteSliderBounds = new SKRect(sliderLeft, y + 3, sliderRight, y + sliderRowHeight - 3);
        DrawSettingsSlider(canvas, _bgVignetteSliderBounds, _background.VignetteStrength, 100);
        FUIRenderer.DrawText(canvas, _background.VignetteStrength.ToString(), new SKPoint(sliderRight + 10, y + 5), FUIColors.TextDim, 10f);
    }

    private void DrawSettingsValueField(SKCanvas canvas, SKRect bounds, string value)
    {
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        canvas.DrawRoundRect(bounds, 3, 3, bgPaint);

        using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
        canvas.DrawRoundRect(bounds, 3, 3, framePaint);

        FUIRenderer.DrawTextCentered(canvas, value, bounds, FUIColors.TextPrimary, 11f);
    }

    private void StoreThemeButtonBounds(int index, SKRect bounds)
    {
        if (index >= 0 && index < _themeButtonBounds.Length)
        {
            _themeButtonBounds[index] = bounds;
        }
    }

    private void DrawThemeButton(SKCanvas canvas, SKRect bounds, string name, SKColor previewColor, bool isActive)
    {
        var bgColor = isActive ? previewColor.WithAlpha(60) : FUIColors.Background2;
        var frameColor = isActive ? previewColor : FUIColors.Frame;
        var textColor = isActive ? FUIColors.TextBright : FUIColors.TextDim;

        using var themeBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, themeBgPaint);

        using var themeFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = frameColor, StrokeWidth = isActive ? 1.5f : 1f };
        canvas.DrawRect(bounds, themeFramePaint);

        FUIRenderer.DrawTextCentered(canvas, name, bounds, textColor, 8f);

        var indicatorBounds = new SKRect(bounds.Left + 2, bounds.Bottom - 2,
            bounds.Right - 2, bounds.Bottom - 1);
        using var indicatorPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = previewColor.WithAlpha((byte)(isActive ? 200 : 100)) };
        canvas.DrawRect(indicatorBounds, indicatorPaint);
    }

    private void HandleSettingsTabClick(SKPoint pt)
    {
        // Check font size button clicks
        FontSizeOption[] fontSizes = { FontSizeOption.Small, FontSizeOption.Medium, FontSizeOption.Large };
        for (int i = 0; i < _fontSizeButtonBounds.Length; i++)
        {
            if (_fontSizeButtonBounds[i].Contains(pt))
            {
                _profileService.FontSize = fontSizes[i];
                FUIRenderer.FontSizeOption = fontSizes[i];
                _canvas.Invalidate();
                return;
            }
        }

        // Check theme button clicks
        FUITheme[] themes = {
            // Core themes (0-3)
            FUITheme.Midnight, FUITheme.Matrix, FUITheme.Amber, FUITheme.Ice,
            // Manufacturer themes row 1 (4-7)
            FUITheme.Drake, FUITheme.Aegis, FUITheme.Anvil, FUITheme.Argo,
            // Manufacturer themes row 2 (8-11)
            FUITheme.Crusader, FUITheme.Origin, FUITheme.MISC, FUITheme.RSI
        };
        for (int i = 0; i < _themeButtonBounds.Length && i < themes.Length; i++)
        {
            if (_themeButtonBounds[i].Contains(pt))
            {
                FUIColors.SetTheme(themes[i]);
                _profileService.Theme = themes[i];
                _canvas.Invalidate();
                return;
            }
        }

        // Check auto-load toggle click
        if (_autoLoadToggleBounds.Contains(pt))
        {
            _profileService.AutoLoadLastProfile = !_profileService.AutoLoadLastProfile;
            _canvas.Invalidate();
            return;
        }

        // Check background slider clicks (start drag)
        if (_bgGridSliderBounds.Contains(pt))
        {
            _draggingBgSlider = "grid";
            UpdateBgSliderFromPoint(pt.X);
            return;
        }
        if (_bgGlowSliderBounds.Contains(pt))
        {
            _draggingBgSlider = "glow";
            UpdateBgSliderFromPoint(pt.X);
            return;
        }
        if (_bgNoiseSliderBounds.Contains(pt))
        {
            _draggingBgSlider = "noise";
            UpdateBgSliderFromPoint(pt.X);
            return;
        }
        if (_bgScanlineSliderBounds.Contains(pt))
        {
            _draggingBgSlider = "scanline";
            UpdateBgSliderFromPoint(pt.X);
            return;
        }
        if (_bgVignetteSliderBounds.Contains(pt))
        {
            _draggingBgSlider = "vignette";
            UpdateBgSliderFromPoint(pt.X);
            return;
        }
    }

    private void UpdateBgSliderFromPoint(float x)
    {
        SKRect bounds;
        switch (_draggingBgSlider)
        {
            case "grid": bounds = _bgGridSliderBounds; break;
            case "glow": bounds = _bgGlowSliderBounds; break;
            case "noise": bounds = _bgNoiseSliderBounds; break;
            case "scanline": bounds = _bgScanlineSliderBounds; break;
            case "vignette": bounds = _bgVignetteSliderBounds; break;
            default: return;
        }

        float ratio = Math.Clamp((x - bounds.Left) / bounds.Width, 0f, 1f);
        int value = (int)(ratio * 100);

        switch (_draggingBgSlider)
        {
            case "grid": _background.GridStrength = value; break;
            case "glow": _background.GlowIntensity = value; break;
            case "noise": _background.NoiseIntensity = value; break;
            case "scanline": _background.ScanlineIntensity = value; break;
            case "vignette": _background.VignetteStrength = value; break;
        }

        _canvas.Invalidate();
    }

    private void SaveBackgroundSettings()
    {
        _profileService.SaveBackgroundSettings(
            _background.GridStrength,
            _background.GlowIntensity,
            _background.NoiseIntensity,
            _background.ScanlineIntensity,
            _background.VignetteStrength);
    }

    private void DrawMappingsTabContent(SKCanvas canvas, SKRect bounds, float sideTabPad, float contentTop, float contentBottom)
    {
        float frameInset = 5f;
        float pad = FUIRenderer.SpaceLG;  // Standard padding for right side
        var contentBounds = new SKRect(sideTabPad, contentTop, bounds.Right - pad, contentBottom);

        // Three-panel layout: Left (bindings list) | Center (device view) | Right (settings)
        float leftPanelWidth = 400f;  // Match Settings panel width
        float rightPanelWidth = 330f;
        float centerPanelWidth = contentBounds.Width - leftPanelWidth - rightPanelWidth - 20;

        var leftBounds = new SKRect(contentBounds.Left, contentBounds.Top,
            contentBounds.Left + leftPanelWidth, contentBounds.Bottom);
        var centerBounds = new SKRect(leftBounds.Right + 10, contentBounds.Top,
            leftBounds.Right + 10 + centerPanelWidth, contentBounds.Bottom);
        var rightBounds = new SKRect(centerBounds.Right + 10, contentBounds.Top,
            contentBounds.Right, contentBounds.Bottom);

        // Refresh vJoy devices list
        if (_vjoyDevices.Count == 0)
        {
            _vjoyDevices = _vjoyService.EnumerateDevices();
        }

        // LEFT PANEL - Bindings List
        DrawBindingsPanel(canvas, leftBounds, frameInset);

        // CENTER PANEL - Device Visualization
        DrawDeviceVisualizationPanel(canvas, centerBounds, frameInset);

        // RIGHT PANEL - Settings
        DrawMappingSettingsPanel(canvas, rightBounds, frameInset);
    }

    private void DrawBindingsPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        float pad = FUIRenderer.PanelPadding;
        float itemGap = FUIRenderer.ItemSpacing;

        // Vertical side tabs width
        float sideTabWidth = 28f;

        // Panel shadow
        FUIRenderer.DrawPanelShadow(canvas, bounds, 3f, 3f, 10f);

        // Panel background (shifted right to make room for side tabs)
        var contentBounds = new SKRect(bounds.Left + frameInset + sideTabWidth, bounds.Top + frameInset,
                                        bounds.Right - frameInset, bounds.Bottom - frameInset);
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(140),
            IsAntialias = true
        };
        canvas.DrawRect(contentBounds, bgPaint);

        // Draw vertical side tabs (M1 Axes, M2 Buttons)
        DrawMappingCategorySideTabs(canvas, bounds.Left + frameInset, bounds.Top + frameInset,
            sideTabWidth, bounds.Height - frameInset * 2);

        // L-corner frame (adjusted for side tabs)
        var frameBounds = new SKRect(bounds.Left + sideTabWidth, bounds.Top, bounds.Right, bounds.Bottom);
        FUIRenderer.DrawLCornerFrame(canvas, frameBounds, FUIColors.Frame, 40f, 10f);

        float y = contentBounds.Top + 10;
        float leftMargin = contentBounds.Left + 10;
        float rightMargin = contentBounds.Right - 10;

        // Header with category code
        string categoryCode = _mappingCategory == 0 ? "M1" : "M2";
        string categoryName = "VJOY MAPPINGS";
        FUIRenderer.DrawText(canvas, categoryCode, new SKPoint(leftMargin, y + 12), FUIColors.Active, 12f);
        FUIRenderer.DrawText(canvas, categoryName, new SKPoint(leftMargin + 30, y + 12), FUIColors.TextBright, 14f, true);
        y += 30;

        // vJoy device selector: [<] vJoy Device 1 [>]
        float arrowButtonSize = 28f;
        _vjoyPrevButtonBounds = new SKRect(leftMargin, y, leftMargin + arrowButtonSize, y + arrowButtonSize);
        DrawArrowButton(canvas, _vjoyPrevButtonBounds, "<", _vjoyPrevHovered, _selectedVJoyDeviceIndex > 0);

        string deviceName = _vjoyDevices.Count > 0 && _selectedVJoyDeviceIndex < _vjoyDevices.Count
            ? $"vJoy Device {_vjoyDevices[_selectedVJoyDeviceIndex].Id}"
            : "No vJoy Devices";
        // Center the device name between the two arrow buttons
        var labelBounds = new SKRect(leftMargin + arrowButtonSize, y, rightMargin - arrowButtonSize, y + arrowButtonSize);
        FUIRenderer.DrawTextCentered(canvas, deviceName, labelBounds, FUIColors.TextBright, 12f);

        _vjoyNextButtonBounds = new SKRect(rightMargin - arrowButtonSize, y, rightMargin, y + arrowButtonSize);
        DrawArrowButton(canvas, _vjoyNextButtonBounds, ">", _vjoyNextHovered, _selectedVJoyDeviceIndex < _vjoyDevices.Count - 1);
        y += arrowButtonSize + 15;

        // Scrollable binding rows (filtered by category)
        float listBottom = contentBounds.Bottom - 10;
        DrawBindingsList(canvas, new SKRect(leftMargin - 5, y, rightMargin + 5, listBottom));
    }

    private void DrawMappingCategorySideTabs(SKCanvas canvas, float x, float y, float width, float height)
    {
        // Style matching Device category tabs: narrow vertical tabs with text reading bottom-to-top
        float tabHeight = 80f;
        float tabGap = 4f;

        // Calculate total tabs height and start from bottom of available space
        float totalTabsHeight = tabHeight * 2 + tabGap;
        float startY = y + height - totalTabsHeight - 10f;

        // M1 Buttons tab (bottom)
        var buttonsBounds = new SKRect(x, startY + tabHeight + tabGap, x + width, startY + tabHeight * 2 + tabGap);
        _mappingCategoryButtonsBounds = buttonsBounds;
        DrawVerticalSideTab(canvas, buttonsBounds, "BUTTONS_01", _mappingCategory == 0, _hoveredMappingCategory == 0);

        // M2 Axes tab (above M1)
        var axesBounds = new SKRect(x, startY, x + width, startY + tabHeight);
        _mappingCategoryAxesBounds = axesBounds;
        DrawVerticalSideTab(canvas, axesBounds, "AXES_02", _mappingCategory == 1, _hoveredMappingCategory == 1);
    }

    private void DrawBindingsList(SKCanvas canvas, SKRect bounds)
    {
        _mappingRowBounds.Clear();
        _mappingAddButtonBounds.Clear();
        _mappingRemoveButtonBounds.Clear();
        _bindingsListBounds = bounds;

        var profile = _profileService.ActiveProfile;

        bool hasVJoy = _vjoyDevices.Count > 0 && _selectedVJoyDeviceIndex < _vjoyDevices.Count;
        VJoyDeviceInfo? vjoyDevice = hasVJoy ? _vjoyDevices[_selectedVJoyDeviceIndex] : null;

        float rowHeight = 32f;  // Compact rows
        float rowGap = 4f;

        // Get counts based on current category
        string[] axisNames = { "X Axis", "Y Axis", "Z Axis", "RX Axis", "RY Axis", "RZ Axis", "Slider 1", "Slider 2" };
        int axisCount = hasVJoy ? Math.Min(axisNames.Length, 8) : 0;
        int buttonCount = vjoyDevice?.ButtonCount ?? 0;

        // Calculate content height based on selected category (no section headers when filtered)
        // Category 0 = Buttons, Category 1 = Axes
        int itemCount = _mappingCategory == 0 ? buttonCount : axisCount;
        _bindingsContentHeight = itemCount * (rowHeight + rowGap);

        // Clamp scroll offset
        float maxScroll = Math.Max(0, _bindingsContentHeight - bounds.Height);
        _bindingsScrollOffset = Math.Clamp(_bindingsScrollOffset, 0, maxScroll);

        // Set up clipping
        canvas.Save();
        canvas.ClipRect(bounds);

        float y = bounds.Top - _bindingsScrollOffset;
        int rowIndex = 0;

        // Show BUTTONS when category is 0
        if (_mappingCategory == 0 && hasVJoy && buttonCount > 0)
        {
            for (int i = 0; i < buttonCount; i++)
            {
                float rowTop = y;
                float rowBottom = y + rowHeight;

                // Only draw if visible
                if (rowBottom > bounds.Top && rowTop < bounds.Bottom)
                {
                    var rowBounds = new SKRect(bounds.Left, rowTop, bounds.Right, rowBottom);
                    string binding = GetButtonBindingText(profile, vjoyDevice!.Id, i);
                    var keyParts = GetButtonKeyParts(profile, vjoyDevice!.Id, i);
                    bool isSelected = rowIndex == _selectedMappingRow;
                    bool isHovered = rowIndex == _hoveredMappingRow;

                    DrawChunkyBindingRow(canvas, rowBounds, $"Button {i + 1}", binding, isSelected, isHovered, rowIndex, keyParts);
                    _mappingRowBounds.Add(rowBounds);
                }
                else
                {
                    _mappingRowBounds.Add(new SKRect(bounds.Left, rowTop, bounds.Right, rowBottom));
                }

                y += rowHeight + rowGap;
                rowIndex++;
            }
        }

        // Show AXES when category is 1
        if (_mappingCategory == 1 && hasVJoy && axisCount > 0)
        {
            for (int i = 0; i < axisCount; i++)
            {
                float rowTop = y;
                float rowBottom = y + rowHeight;

                // Only draw if visible
                if (rowBottom > bounds.Top && rowTop < bounds.Bottom)
                {
                    var rowBounds = new SKRect(bounds.Left, rowTop, bounds.Right, rowBottom);
                    string binding = GetAxisBindingText(profile, vjoyDevice!.Id, i);
                    bool isSelected = rowIndex == _selectedMappingRow;
                    bool isHovered = rowIndex == _hoveredMappingRow;

                    DrawChunkyBindingRow(canvas, rowBounds, axisNames[i], binding, isSelected, isHovered, rowIndex);
                    _mappingRowBounds.Add(rowBounds);
                }
                else
                {
                    // Add placeholder bounds for hit testing even when not visible
                    _mappingRowBounds.Add(new SKRect(bounds.Left, rowTop, bounds.Right, rowBottom));
                }

                y += rowHeight + rowGap;
                rowIndex++;
            }
        }

        canvas.Restore();

        // Draw scroll indicator if content overflows
        if (_bindingsContentHeight > bounds.Height)
        {
            DrawScrollIndicator(canvas, bounds, _bindingsScrollOffset, _bindingsContentHeight);
        }
    }

    /// <summary>
    /// Get the keyboard key parts for a button mapping (modifiers + key as separate items)
    /// </summary>
    private List<string>? GetKeyboardMappingParts(ButtonMapping mapping)
    {
        var output = mapping.Output;
        if (string.IsNullOrEmpty(output.KeyName)) return null;

        var parts = new List<string>();
        if (output.Modifiers != null && output.Modifiers.Count > 0)
        {
            parts.AddRange(output.Modifiers);
        }
        parts.Add(output.KeyName);
        return parts;
    }

    /// <summary>
    /// Get the keyboard key parts for a button slot (if it outputs to keyboard)
    /// Returns list of key parts (e.g., ["LCtrl", "LShift", "A"]) for drawing as separate keycaps
    /// </summary>
    private List<string>? GetButtonKeyParts(MappingProfile? profile, uint vjoyId, int buttonIndex)
    {
        if (profile == null) return null;

        // Find mapping for this button slot that has keyboard output
        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyId &&
            m.Output.Index == buttonIndex &&
            !string.IsNullOrEmpty(m.Output.KeyName));

        if (mapping == null) return null;

        return GetKeyboardMappingParts(mapping);
    }

    private void DrawScrollIndicator(SKCanvas canvas, SKRect bounds, float scrollOffset, float contentHeight)
    {
        float trackHeight = bounds.Height - 20;
        float thumbRatio = bounds.Height / contentHeight;
        float thumbHeight = Math.Max(20, trackHeight * thumbRatio);
        float thumbOffset = (scrollOffset / (contentHeight - bounds.Height)) * (trackHeight - thumbHeight);

        // Position track outside the list bounds, aligned with corner frame edge
        float trackX = bounds.Right + 8; // Outside cells, inline with frame
        float trackTop = bounds.Top + 10;
        float trackWidth = 3f;

        // Track (subtle)
        using var trackPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Frame.WithAlpha(40)
        };
        canvas.DrawRoundRect(new SKRect(trackX, trackTop, trackX + trackWidth, trackTop + trackHeight), 1.5f, 1.5f, trackPaint);

        // Thumb
        using var thumbPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Primary.WithAlpha(200)
        };
        canvas.DrawRoundRect(new SKRect(trackX, trackTop + thumbOffset, trackX + trackWidth, trackTop + thumbOffset + thumbHeight), 1.5f, 1.5f, thumbPaint);
    }

    private void DrawChunkyBindingRow(SKCanvas canvas, SKRect bounds, string outputName, string binding,
        bool isSelected, bool isHovered, int rowIndex, List<string>? keyParts = null)
    {
        bool hasBinding = !string.IsNullOrEmpty(binding) && binding != "—";
        bool hasKeyParts = keyParts != null && keyParts.Count > 0;

        // Background
        SKColor bgColor;
        if (isSelected)
            bgColor = FUIColors.Active.WithAlpha(50);
        else if (isHovered)
            bgColor = FUIColors.Primary.WithAlpha(35);
        else
            bgColor = FUIColors.Background2.WithAlpha(100);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRoundRect(bounds, 4, 4, bgPaint);

        // Frame
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = isSelected ? FUIColors.Active : (isHovered ? FUIColors.FrameBright : FUIColors.Frame.WithAlpha(100)),
            StrokeWidth = isSelected ? 2f : 1f
        };
        canvas.DrawRoundRect(bounds, 4, 4, framePaint);

        // Output name (centered vertically)
        float leftTextX = bounds.Left + 12;
        FUIRenderer.DrawText(canvas, outputName, new SKPoint(leftTextX, bounds.MidY + 5),
            isSelected ? FUIColors.Active : FUIColors.TextPrimary, 12f, true);

        // Right side indicator: keyboard keycaps or binding dot
        if (hasKeyParts)
        {
            // Draw keycaps right-aligned within available space
            float keycapHeight = 16f;
            float keycapGap = 2f;
            float keycapPadding = 6f;  // Padding inside each keycap (left + right)
            float fontSize = 8f;  // Slightly smaller font for compact display
            float scaledFontSize = FUIRenderer.ScaleFont(fontSize);
            float keycapRight = bounds.Right - 8;
            float keycapTop = bounds.MidY - keycapHeight / 2;

            // Use same font settings as DrawTextCentered for accurate measurement
            using var measurePaint = new SKPaint
            {
                TextSize = scaledFontSize,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal)
            };

            // Draw keycaps from right to left (key rightmost, then modifiers)
            for (int i = keyParts!.Count - 1; i >= 0; i--)
            {
                string keyText = keyParts[i].ToUpperInvariant();
                float textWidth = measurePaint.MeasureText(keyText);
                float keycapWidth = textWidth + keycapPadding * 2;
                float keycapLeft = keycapRight - keycapWidth;

                var keycapBounds = new SKRect(keycapLeft, keycapTop, keycapRight, keycapTop + keycapHeight);

                // Keycap background
                using var keycapBgPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = FUIColors.TextPrimary.WithAlpha(20),
                    IsAntialias = true
                };
                canvas.DrawRoundRect(keycapBounds, 3, 3, keycapBgPaint);

                // Keycap frame
                using var keycapFramePaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = FUIColors.TextPrimary.WithAlpha(100),
                    StrokeWidth = 1f,
                    IsAntialias = true
                };
                canvas.DrawRoundRect(keycapBounds, 3, 3, keycapFramePaint);

                // Keycap text - draw manually centered to ensure padding is respected
                float textX = keycapLeft + keycapPadding;
                float textY = keycapBounds.MidY + scaledFontSize / 3;
                using var textPaint = new SKPaint
                {
                    Color = FUIColors.TextPrimary,
                    TextSize = scaledFontSize,
                    IsAntialias = true,
                    Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal)
                };
                canvas.DrawText(keyText, textX, textY, textPaint);

                // Move left for next keycap
                keycapRight = keycapLeft - keycapGap;
            }
        }
        else if (hasBinding)
        {
            // Binding indicator dot on the right
            float dotX = bounds.Right - 20;
            float dotY = bounds.MidY;
            using var dotPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = FUIColors.Active,
                IsAntialias = true
            };
            canvas.DrawCircle(dotX, dotY, 5f, dotPaint);
        }
    }

    private void DrawDeviceVisualizationPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(100),
            IsAntialias = true
        };
        canvas.DrawRect(new SKRect(bounds.Left + frameInset, bounds.Top + frameInset,
            bounds.Right - frameInset, bounds.Bottom - frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame.WithAlpha(150), 30f, 8f);

        // If we have a selected binding with a device, show the device silhouette
        // For now, show placeholder
        float centerX = bounds.MidX;
        float centerY = bounds.MidY;

        // Draw the joystick SVG if available - use same brightness as device tab
        if (_joystickSvg?.Picture != null)
        {
            // Limit size to 900px max and apply same rendering as device tab
            float maxSize = 900f;
            float maxWidth = Math.Min(bounds.Width - 40, maxSize);
            float maxHeight = Math.Min(bounds.Height - 40, maxSize);

            // Create constrained bounds centered in the panel
            float constrainedWidth = Math.Min(maxWidth, maxHeight); // Keep square-ish
            float constrainedHeight = constrainedWidth;
            var constrainedBounds = new SKRect(
                centerX - constrainedWidth / 2,
                centerY - constrainedHeight / 2,
                centerX + constrainedWidth / 2,
                centerY + constrainedHeight / 2
            );

            // Mappings tab always shows joystick SVG (vJoy is a virtual joystick)
            // Don't use _deviceMap here - that's for the Devices tab context
            DrawSvgInBounds(canvas, _joystickSvg, constrainedBounds, false);
        }
        else
        {
            // Placeholder text
            FUIRenderer.DrawTextCentered(canvas, "Device Preview",
                new SKRect(bounds.Left, centerY - 20, bounds.Right, centerY + 20),
                FUIColors.TextDim, 14f);
        }
    }

    private void DrawMappingSettingsPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(140),
            IsAntialias = true
        };
        canvas.DrawRect(new SKRect(bounds.Left + frameInset, bounds.Top + frameInset,
            bounds.Right - frameInset, bounds.Bottom - frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame, 30f, 8f);

        float y = bounds.Top + frameInset + 10;
        float leftMargin = bounds.Left + frameInset + 15;
        float rightMargin = bounds.Right - frameInset - 15;

        // Title
        FUIRenderer.DrawText(canvas, "MAPPING SETTINGS", new SKPoint(leftMargin, y + 12), FUIColors.TextBright, 14f, true);
        y += 35;

        // Show settings for selected row
        if (_selectedMappingRow < 0)
        {
            FUIRenderer.DrawText(canvas, "Select an output to configure",
                new SKPoint(leftMargin, y + 30), FUIColors.TextDim, 12f);
            return;
        }

        // Determine if axis or button based on current category
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        string outputName = GetSelectedOutputName();

        FUIRenderer.DrawText(canvas, outputName, new SKPoint(leftMargin, y + 15), FUIColors.Active, 13f, true);
        y += 35;

        // INPUT SOURCES section - shows mapped inputs with add/remove
        y = DrawInputSourcesSection(canvas, leftMargin, rightMargin, y);

        float bottomMargin = bounds.Bottom - frameInset - 10;

        if (isAxis)
        {
            DrawAxisSettings(canvas, leftMargin, rightMargin, y, bottomMargin);
        }
        else
        {
            DrawButtonSettings(canvas, leftMargin, rightMargin, y, bottomMargin);
        }
    }

    private float DrawInputSourcesSection(SKCanvas canvas, float leftMargin, float rightMargin, float y)
    {
        _inputSourceRemoveBounds.Clear();

        FUIRenderer.DrawText(canvas, "INPUT SOURCES", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += 18;

        // Get current mappings for selected output
        var inputs = GetInputsForSelectedOutput();
        bool isListening = _isListeningForInput;

        float rowHeight = 40f;  // Two-line layout
        float rowGap = 4f;

        if (inputs.Count == 0 && !isListening)
        {
            // No inputs - show "None" with dashed border
            var emptyBounds = new SKRect(leftMargin, y, rightMargin, y + 28);
            using var emptyBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1.WithAlpha(100) };
            canvas.DrawRoundRect(emptyBounds, 3, 3, emptyBgPaint);

            using var emptyFramePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.FrameDim,
                StrokeWidth = 1f,
                PathEffect = SKPathEffect.CreateDash(new float[] { 4, 2 }, 0)
            };
            canvas.DrawRoundRect(emptyBounds, 3, 3, emptyFramePaint);

            FUIRenderer.DrawText(canvas, "No input mapped", new SKPoint(leftMargin + 10, emptyBounds.MidY + 4), FUIColors.TextDisabled, 11f);
            y += 28 + rowGap;
        }
        else
        {
            // Draw each input source row (two-line layout)
            for (int i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                var rowBounds = new SKRect(leftMargin, y, rightMargin - 30, y + rowHeight);

                // Row background
                using var rowBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1 };
                canvas.DrawRoundRect(rowBounds, 3, 3, rowBgPaint);

                using var rowFramePaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = FUIColors.Frame,
                    StrokeWidth = 1f
                };
                canvas.DrawRoundRect(rowBounds, 3, 3, rowFramePaint);

                // Line 1: Input type and index (e.g., "Button 5") - vertically centered in top half
                string inputTypeText = input.Type == InputType.Button
                    ? $"Button {input.Index + 1}"
                    : $"{input.Type} {input.Index}";
                FUIRenderer.DrawText(canvas, inputTypeText, new SKPoint(leftMargin + 8, y + 16), FUIColors.TextPrimary, 11f);

                // Line 2: Device name (smaller, dimmer) - vertically centered in bottom half
                FUIRenderer.DrawText(canvas, input.DeviceName, new SKPoint(leftMargin + 8, y + 32), FUIColors.TextDim, 9f);

                // Remove [×] button (full height of row)
                var removeBounds = new SKRect(rightMargin - 26, y, rightMargin, y + rowHeight);
                bool removeHovered = _hoveredInputSourceRemove == i;

                using var removeBgPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = removeHovered ? FUIColors.Warning.WithAlpha(40) : FUIColors.Background2
                };
                canvas.DrawRoundRect(removeBounds, 3, 3, removeBgPaint);

                using var removeFramePaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = removeHovered ? FUIColors.Warning : FUIColors.Frame,
                    StrokeWidth = 1f
                };
                canvas.DrawRoundRect(removeBounds, 3, 3, removeFramePaint);

                FUIRenderer.DrawTextCentered(canvas, "×", removeBounds,
                    removeHovered ? FUIColors.Warning : FUIColors.TextDim, 14f);

                _inputSourceRemoveBounds.Add(removeBounds);
                y += rowHeight + rowGap;
            }
        }

        // Listening indicator
        if (isListening)
        {
            var listenBounds = new SKRect(leftMargin, y, rightMargin, y + rowHeight);
            byte alpha = (byte)(180 + MathF.Sin(_pulsePhase * 3) * 60);

            using var listenBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Warning.WithAlpha(40) };
            canvas.DrawRoundRect(listenBounds, 3, 3, listenBgPaint);

            using var listenFramePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.Warning.WithAlpha(alpha),
                StrokeWidth = 2f
            };
            canvas.DrawRoundRect(listenBounds, 3, 3, listenFramePaint);

            FUIRenderer.DrawText(canvas, "Press input...", new SKPoint(leftMargin + 10, y + 18),
                FUIColors.Warning.WithAlpha(alpha), 11f);
            y += rowHeight + rowGap;
        }

        // Add input button [+]
        var addBounds = new SKRect(leftMargin, y, rightMargin, y + 28);
        _addInputButtonBounds = addBounds;
        bool addHovered = _addInputButtonHovered;

        using var addBgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = addHovered ? FUIColors.Active.WithAlpha(40) : FUIColors.Background2
        };
        canvas.DrawRoundRect(addBounds, 3, 3, addBgPaint);

        using var addFramePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = addHovered ? FUIColors.Active : FUIColors.Frame,
            StrokeWidth = addHovered ? 2f : 1f,
            PathEffect = isListening ? null : SKPathEffect.CreateDash(new float[] { 4, 2 }, 0)
        };
        canvas.DrawRoundRect(addBounds, 3, 3, addFramePaint);

        string addText = isListening ? "Cancel" : "+ Add Input";
        FUIRenderer.DrawTextCentered(canvas, addText, addBounds,
            addHovered ? FUIColors.Active : FUIColors.TextPrimary, 11f);
        y += 28 + 15;

        return y;
    }

    private List<InputSource> GetInputsForSelectedOutput()
    {
        var inputs = new List<InputSource>();
        if (_selectedMappingRow < 0) return inputs;
        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count) return inputs;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return inputs;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        int outputIndex = _selectedMappingRow;

        if (isAxis)
        {
            var mapping = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);
            if (mapping != null)
                inputs.AddRange(mapping.Inputs);
        }
        else
        {
            // For button rows, find mapping for this vJoy button slot
            // Check both VJoyButton and Keyboard output types (both map to button slots)
            var mapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);
            if (mapping != null)
                inputs.AddRange(mapping.Inputs);
        }

        return inputs;
    }

    private string GetSelectedOutputName()
    {
        if (_selectedMappingRow < 0) return "";

        // Category 0 = Buttons, Category 1 = Axes
        if (_mappingCategory == 1)
        {
            // Axes
            string[] axisNames = { "X Axis", "Y Axis", "Z Axis", "RX Axis", "RY Axis", "RZ Axis", "Slider 1", "Slider 2" };
            return _selectedMappingRow < axisNames.Length ? axisNames[_selectedMappingRow] : $"Axis {_selectedMappingRow}";
        }
        else
        {
            // Buttons
            return $"Button {_selectedMappingRow + 1}";
        }
    }

    private void DrawAxisSettings(SKCanvas canvas, float leftMargin, float rightMargin, float y, float bottom)
    {
        float width = rightMargin - leftMargin;

        // Response Curve header
        FUIRenderer.DrawText(canvas, "RESPONSE CURVE", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += FUIRenderer.ScaleLineHeight(18f);

        // Symmetrical, Centre, and Invert checkboxes on their own row
        // Symmetrical on left, Centre and Invert on right
        float checkboxSize = FUIRenderer.ScaleLineHeight(12f);
        float rowHeight = FUIRenderer.ScaleLineHeight(16f);
        float checkboxY = y + (rowHeight - checkboxSize) / 2; // Center checkbox in row
        float fontSize = 9f;
        float scaledFontSize = FUIRenderer.ScaleFont(fontSize);
        float textY = y + (rowHeight / 2) + (scaledFontSize / 3); // Center text baseline

        // Measure label widths for positioning
        using var labelPaint = FUIRenderer.CreateTextPaint(FUIColors.TextDim, scaledFontSize);
        float invertLabelWidth = labelPaint.MeasureText("Invert");
        float centreLabelWidth = labelPaint.MeasureText("Centre");
        float symmetricalLabelWidth = labelPaint.MeasureText("Symmetrical");
        float labelGap = FUIRenderer.ScaleSpacing(4f);
        float checkboxGap = FUIRenderer.ScaleSpacing(12f);

        // Symmetrical checkbox (leftmost) - checkbox then label
        _curveSymmetricalCheckboxBounds = new SKRect(leftMargin, checkboxY, leftMargin + checkboxSize, checkboxY + checkboxSize);
        DrawCheckbox(canvas, _curveSymmetricalCheckboxBounds, _curveSymmetrical);
        FUIRenderer.DrawText(canvas, "Symmetrical", new SKPoint(leftMargin + checkboxSize + labelGap, textY), FUIColors.TextDim, fontSize);

        // Invert checkbox (rightmost) - label then checkbox
        float invertCheckX = rightMargin - checkboxSize;
        _invertToggleBounds = new SKRect(invertCheckX, checkboxY, invertCheckX + checkboxSize, checkboxY + checkboxSize);
        DrawCheckbox(canvas, _invertToggleBounds, _axisInverted);
        FUIRenderer.DrawText(canvas, "Invert", new SKPoint(invertCheckX - invertLabelWidth - labelGap, textY), FUIColors.TextDim, fontSize);

        // Centre checkbox (left of Invert) - label then checkbox
        float centreCheckX = invertCheckX - invertLabelWidth - labelGap - checkboxGap - checkboxSize;
        _deadzoneCenterCheckboxBounds = new SKRect(centreCheckX, checkboxY, centreCheckX + checkboxSize, checkboxY + checkboxSize);
        DrawCheckbox(canvas, _deadzoneCenterCheckboxBounds, _deadzoneCenterEnabled);
        FUIRenderer.DrawText(canvas, "Centre", new SKPoint(centreCheckX - centreLabelWidth - labelGap, textY), FUIColors.TextDim, fontSize);

        y += rowHeight + FUIRenderer.ScaleSpacing(6f);

        // Curve preset buttons - store bounds for click handling
        string[] presets = { "LINEAR", "S-CURVE", "EXPO", "CUSTOM" };
        float buttonWidth = (width - 9) / presets.Length;
        float buttonHeight = FUIRenderer.ScaleLineHeight(22f);

        for (int i = 0; i < presets.Length; i++)
        {
            var presetBounds = new SKRect(
                leftMargin + i * (buttonWidth + 3), y,
                leftMargin + i * (buttonWidth + 3) + buttonWidth, y + buttonHeight);

            // Store bounds for click detection
            _curvePresetBounds[i] = presetBounds;

            CurveType presetType = i switch
            {
                0 => CurveType.Linear,
                1 => CurveType.SCurve,
                2 => CurveType.Exponential,
                _ => CurveType.Custom
            };

            bool isActive = _selectedCurveType == presetType;
            var bgColor = isActive ? FUIColors.Active.WithAlpha(60) : FUIColors.Background2;
            var frameColor = isActive ? FUIColors.Active : FUIColors.Frame;
            var textColor = isActive ? FUIColors.TextBright : FUIColors.TextDim;

            using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
            canvas.DrawRect(presetBounds, bgPaint);

            using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = frameColor, StrokeWidth = 1f };
            canvas.DrawRect(presetBounds, framePaint);

            FUIRenderer.DrawTextCentered(canvas, presets[i], presetBounds, textColor, 8f);
        }
        y += buttonHeight + FUIRenderer.ScaleSpacing(6f);

        // Curve editor visualization
        float curveHeight = 140f;
        _curveEditorBounds = new SKRect(leftMargin, y, rightMargin, y + curveHeight);
        DrawCurveVisualization(canvas, _curveEditorBounds);
        y += curveHeight + FUIRenderer.ScaleLineHeight(16f);

        // Deadzone section
        if (y + 100 < bottom)
        {
            // Header row: "DEADZONE" label + preset buttons + selected handle indicator
            FUIRenderer.DrawText(canvas, "DEADZONE", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);

            // Preset buttons - always visible, apply to selected handle
            string[] presetLabels = { "0%", "2%", "5%", "10%" };
            float presetBtnWidth = 32f;
            float presetStartX = rightMargin - (presetBtnWidth * 4 + 9);

            for (int col = 0; col < 4; col++)
            {
                var btnBounds = new SKRect(
                    presetStartX + col * (presetBtnWidth + 3), y - 2,
                    presetStartX + col * (presetBtnWidth + 3) + presetBtnWidth, y + 14);
                _deadzonePresetBounds[col] = btnBounds;

                // Dim buttons if no handle selected
                bool enabled = _selectedDeadzoneHandle >= 0;
                using var btnBg = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
                canvas.DrawRect(btnBounds, btnBg);
                using var btnFrame = new SKPaint { Style = SKPaintStyle.Stroke, Color = enabled ? FUIColors.Frame : FUIColors.Frame.WithAlpha(100), StrokeWidth = 1f };
                canvas.DrawRect(btnBounds, btnFrame);
                FUIRenderer.DrawTextCentered(canvas, presetLabels[col], btnBounds, enabled ? FUIColors.TextDim : FUIColors.TextDim.WithAlpha(100), 9f);
            }

            // Show which handle is selected (if any)
            if (_selectedDeadzoneHandle >= 0)
            {
                string[] handleNames = { "Start", "Ctr-", "Ctr+", "End" };
                string selectedName = handleNames[_selectedDeadzoneHandle];
                FUIRenderer.DrawText(canvas, $"[{selectedName}]", new SKPoint(presetStartX - 45, y), FUIColors.Active, 9f);
            }
            y += FUIRenderer.ScaleLineHeight(20f);

            // Dual deadzone slider (always shows min/max, optionally shows center handles)
            float sliderHeight = FUIRenderer.ScaleLineHeight(24f);
            _deadzoneSliderBounds = new SKRect(leftMargin, y, rightMargin, y + sliderHeight);
            DrawDualDeadzoneSlider(canvas, _deadzoneSliderBounds);
            y += sliderHeight + FUIRenderer.ScaleSpacing(6f);

            // Value labels - fixed positions at track edges (prevents collision)
            if (_deadzoneCenterEnabled)
            {
                // Two-track layout - fixed positions at each track edge
                float gap = 24f;
                float centerX = _deadzoneSliderBounds.MidX;
                float leftTrackRight = centerX - gap / 2;
                float rightTrackLeft = centerX + gap / 2;

                // Min at left edge, CtrMin at right edge of left track
                // CtrMax at left edge of right track, Max at right edge
                FUIRenderer.DrawText(canvas, $"{_deadzoneMin:F2}", new SKPoint(leftMargin, y), FUIColors.TextDim, 9f);
                FUIRenderer.DrawText(canvas, $"{_deadzoneCenterMin:F2}", new SKPoint(leftTrackRight - 25, y), FUIColors.TextDim, 9f);
                FUIRenderer.DrawText(canvas, $"{_deadzoneCenterMax:F2}", new SKPoint(rightTrackLeft, y), FUIColors.TextDim, 9f);
                FUIRenderer.DrawText(canvas, $"{_deadzoneMax:F2}", new SKPoint(rightMargin - 20, y), FUIColors.TextDim, 9f);
            }
            else
            {
                // Single track - just show start and end at edges
                FUIRenderer.DrawText(canvas, $"{_deadzoneMin:F2}", new SKPoint(leftMargin, y), FUIColors.TextDim, 9f);
                FUIRenderer.DrawText(canvas, $"{_deadzoneMax:F2}", new SKPoint(rightMargin - 20, y), FUIColors.TextDim, 9f);
            }
        }
    }

    private void DrawDualDeadzoneSlider(SKCanvas canvas, SKRect bounds)
    {
        // Convert -1..1 values to 0..1 for display
        float minPos = (_deadzoneMin + 1f) / 2f;
        float centerMinPos = (_deadzoneCenterMin + 1f) / 2f;
        float centerMaxPos = (_deadzoneCenterMax + 1f) / 2f;
        float maxPos = (_deadzoneMax + 1f) / 2f;

        float handleRadius = 8f;
        float trackHeight = 8f;
        float trackY = bounds.MidY - trackHeight / 2;

        using var trackBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        using var trackFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
        using var activePaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(150) };

        if (_deadzoneCenterEnabled)
        {
            // Two physically separate tracks like JoystickGremlinEx
            // Gap must be > 2 * handleRadius so handles never overlap when both at center
            float gap = 24f;
            float centerX = bounds.MidX;

            // Left track: from bounds.Left to centerX - gap/2
            var leftTrack = new SKRect(bounds.Left, trackY, centerX - gap / 2, trackY + trackHeight);
            canvas.DrawRoundRect(leftTrack, 4, 4, trackBgPaint);
            canvas.DrawRoundRect(leftTrack, 4, 4, trackFramePaint);

            // Right track: from centerX + gap/2 to bounds.Right
            var rightTrack = new SKRect(centerX + gap / 2, trackY, bounds.Right, trackY + trackHeight);
            canvas.DrawRoundRect(rightTrack, 4, 4, trackBgPaint);
            canvas.DrawRoundRect(rightTrack, 4, 4, trackFramePaint);

            // Active fill on left track (from min handle to center-min handle)
            float leftTrackWidth = leftTrack.Width;
            float minPosInLeft = (minPos - 0f) / 0.5f; // Map 0..0.5 to 0..1 for left track
            float ctrMinPosInLeft = (centerMinPos - 0f) / 0.5f;
            minPosInLeft = Math.Clamp(minPosInLeft, 0f, 1f);
            ctrMinPosInLeft = Math.Clamp(ctrMinPosInLeft, 0f, 1f);

            float leftFillStart = leftTrack.Left + minPosInLeft * leftTrackWidth;
            float leftFillEnd = leftTrack.Left + ctrMinPosInLeft * leftTrackWidth;
            if (leftFillEnd > leftFillStart + 1)
            {
                var leftFill = new SKRect(leftFillStart, trackY + 1, leftFillEnd, trackY + trackHeight - 1);
                canvas.DrawRoundRect(leftFill, 3, 3, activePaint);
            }

            // Active fill on right track (from center-max handle to max handle)
            float rightTrackWidth = rightTrack.Width;
            float ctrMaxPosInRight = (centerMaxPos - 0.5f) / 0.5f; // Map 0.5..1 to 0..1 for right track
            float maxPosInRight = (maxPos - 0.5f) / 0.5f;
            ctrMaxPosInRight = Math.Clamp(ctrMaxPosInRight, 0f, 1f);
            maxPosInRight = Math.Clamp(maxPosInRight, 0f, 1f);

            float rightFillStart = rightTrack.Left + ctrMaxPosInRight * rightTrackWidth;
            float rightFillEnd = rightTrack.Left + maxPosInRight * rightTrackWidth;
            if (rightFillEnd > rightFillStart + 1)
            {
                var rightFill = new SKRect(rightFillStart, trackY + 1, rightFillEnd, trackY + trackHeight - 1);
                canvas.DrawRoundRect(rightFill, 3, 3, activePaint);
            }

            // Draw handles - all same size
            // Min handle on left edge of left track
            float minHandleX = leftTrack.Left + minPosInLeft * leftTrackWidth;
            DrawDeadzoneHandle(canvas, bounds.MidY, minHandleX, 0, FUIColors.Active, handleRadius);

            // CtrMin handle on right edge of left track
            float ctrMinHandleX = leftTrack.Left + ctrMinPosInLeft * leftTrackWidth;
            DrawDeadzoneHandle(canvas, bounds.MidY, ctrMinHandleX, 1, FUIColors.Active, handleRadius);

            // CtrMax handle on left edge of right track
            float ctrMaxHandleX = rightTrack.Left + ctrMaxPosInRight * rightTrackWidth;
            DrawDeadzoneHandle(canvas, bounds.MidY, ctrMaxHandleX, 2, FUIColors.Active, handleRadius);

            // Max handle on right edge of right track
            float maxHandleX = rightTrack.Left + maxPosInRight * rightTrackWidth;
            DrawDeadzoneHandle(canvas, bounds.MidY, maxHandleX, 3, FUIColors.Active, handleRadius);
        }
        else
        {
            // Single track spanning full width
            var track = new SKRect(bounds.Left, trackY, bounds.Right, trackY + trackHeight);
            canvas.DrawRoundRect(track, 4, 4, trackBgPaint);
            canvas.DrawRoundRect(track, 4, 4, trackFramePaint);

            // Active fill from min to max
            float fillStart = bounds.Left + minPos * bounds.Width;
            float fillEnd = bounds.Left + maxPos * bounds.Width;
            if (fillEnd > fillStart + 1)
            {
                var fill = new SKRect(fillStart, trackY + 1, fillEnd, trackY + trackHeight - 1);
                canvas.DrawRoundRect(fill, 3, 3, activePaint);
            }

            // Draw handles - same size
            float minHandleX = bounds.Left + minPos * bounds.Width;
            float maxHandleX = bounds.Left + maxPos * bounds.Width;
            DrawDeadzoneHandle(canvas, bounds.MidY, minHandleX, 0, FUIColors.Active, handleRadius);
            DrawDeadzoneHandle(canvas, bounds.MidY, maxHandleX, 3, FUIColors.Active, handleRadius);
        }
    }

    private void DrawDeadzoneHandle(SKCanvas canvas, float centerY, float x, int handleIndex, SKColor color, float radius)
    {
        bool isDragging = _draggingDeadzoneHandle == handleIndex;
        bool isSelected = _selectedDeadzoneHandle == handleIndex;
        float drawRadius = isDragging ? radius + 2f : radius;

        // Selected handles get a highlighted fill
        SKColor fillColor = isDragging ? color : (isSelected ? color.WithAlpha(200) : FUIColors.TextPrimary);

        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = fillColor,
            IsAntialias = true
        };
        canvas.DrawCircle(x, centerY, drawRadius, fillPaint);

        using var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = isSelected ? 2.5f : 1.5f,
            IsAntialias = true
        };
        canvas.DrawCircle(x, centerY, drawRadius, strokePaint);
    }

    private void DrawCheckbox(SKCanvas canvas, SKRect bounds, bool isChecked)
    {
        // Box background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = isChecked ? FUIColors.Active.WithAlpha(60) : FUIColors.Background2
        };
        canvas.DrawRoundRect(bounds, 2, 2, bgPaint);

        // Box frame
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = isChecked ? FUIColors.Active : FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRoundRect(bounds, 2, 2, framePaint);

        // Checkmark
        if (isChecked)
        {
            using var checkPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.Active,
                StrokeWidth = 2f,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round
            };
            float cx = bounds.MidX;
            float cy = bounds.MidY;
            float s = bounds.Width * 0.3f;
            canvas.DrawLine(cx - s, cy, cx - s * 0.3f, cy + s * 0.7f, checkPaint);
            canvas.DrawLine(cx - s * 0.3f, cy + s * 0.7f, cx + s, cy - s * 0.5f, checkPaint);
        }
    }

    private void DrawInteractiveSlider(SKCanvas canvas, SKRect bounds, float value, SKColor color, bool dragging)
    {
        // Track background
        using var trackPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        canvas.DrawRoundRect(bounds, 4, 4, trackPaint);

        // Track frame
        using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
        canvas.DrawRoundRect(bounds, 4, 4, framePaint);

        // Fill
        float fillWidth = bounds.Width * Math.Clamp(value, 0, 1);
        if (fillWidth > 2)
        {
            var fillBounds = new SKRect(bounds.Left + 1, bounds.Top + 1, bounds.Left + fillWidth - 1, bounds.Bottom - 1);
            using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = color.WithAlpha(100) };
            canvas.DrawRoundRect(fillBounds, 3, 3, fillPaint);
        }

        // Handle
        float handleX = bounds.Left + fillWidth;
        float handleRadius = dragging ? 8f : 6f;
        using var handlePaint = new SKPaint { Style = SKPaintStyle.Fill, Color = dragging ? color : FUIColors.TextPrimary, IsAntialias = true };
        canvas.DrawCircle(handleX, bounds.MidY, handleRadius, handlePaint);

        using var handleStroke = new SKPaint { Style = SKPaintStyle.Stroke, Color = color, StrokeWidth = 1.5f, IsAntialias = true };
        canvas.DrawCircle(handleX, bounds.MidY, handleRadius, handleStroke);
    }

    private void DrawDurationSlider(SKCanvas canvas, SKRect bounds, float value, bool dragging)
    {
        value = Math.Clamp(value, 0f, 1f);

        // Track background
        using var trackPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        canvas.DrawRoundRect(bounds, 4, 4, trackPaint);

        // Track frame
        using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
        canvas.DrawRoundRect(bounds, 4, 4, framePaint);

        // Fill
        float fillWidth = bounds.Width * value;
        if (fillWidth > 2)
        {
            var fillBounds = new SKRect(bounds.Left + 1, bounds.Top + 1, bounds.Left + fillWidth - 1, bounds.Bottom - 1);
            using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(80) };
            canvas.DrawRoundRect(fillBounds, 3, 3, fillPaint);
        }

        // Handle
        float handleX = bounds.Left + fillWidth;
        float handleRadius = dragging ? 8f : 6f;
        using var handlePaint = new SKPaint { Style = SKPaintStyle.Fill, Color = dragging ? FUIColors.Active : FUIColors.TextPrimary, IsAntialias = true };
        canvas.DrawCircle(handleX, bounds.MidY, handleRadius, handlePaint);

        using var handleStroke = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active, StrokeWidth = 1.5f, IsAntialias = true };
        canvas.DrawCircle(handleX, bounds.MidY, handleRadius, handleStroke);
    }

    private void DrawButtonSettings(SKCanvas canvas, float leftMargin, float rightMargin, float y, float bottom)
    {
        float width = rightMargin - leftMargin;

        // OUTPUT TYPE section - vJoy Button vs Keyboard
        FUIRenderer.DrawText(canvas, "OUTPUT TYPE", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += 20;

        // Output type tabs
        string[] outputTypes = { "Button", "Keyboard" };
        float typeButtonWidth = (width - 5) / 2;
        float typeButtonHeight = 28f;

        for (int i = 0; i < 2; i++)
        {
            var typeBounds = new SKRect(leftMargin + i * (typeButtonWidth + 5), y,
                leftMargin + i * (typeButtonWidth + 5) + typeButtonWidth, y + typeButtonHeight);

            if (i == 0) _outputTypeBtnBounds = typeBounds;
            else _outputTypeKeyBounds = typeBounds;

            bool selected = (i == 0 && !_outputTypeIsKeyboard) || (i == 1 && _outputTypeIsKeyboard);
            bool hovered = _hoveredOutputType == i;

            var bgColor = selected
                ? FUIColors.Active.WithAlpha(60)
                : (hovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);
            var textColor = selected ? FUIColors.Active : (hovered ? FUIColors.TextPrimary : FUIColors.TextDim);

            using var typeBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
            canvas.DrawRoundRect(typeBounds, 3, 3, typeBgPaint);

            using var typeFramePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = selected ? FUIColors.Active : FUIColors.Frame,
                StrokeWidth = selected ? 2f : 1f
            };
            canvas.DrawRoundRect(typeBounds, 3, 3, typeFramePaint);

            FUIRenderer.DrawTextCentered(canvas, outputTypes[i], typeBounds, textColor, 11f);
        }
        y += typeButtonHeight + 15;

        // KEY COMBO section (only when Keyboard is selected)
        if (_outputTypeIsKeyboard)
        {
            FUIRenderer.DrawText(canvas, "KEY COMBO", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
            y += 20;

            float keyFieldHeight = 32f;
            _keyCaptureBounds = new SKRect(leftMargin, y, rightMargin, y + keyFieldHeight);

            // Draw key capture field
            var keyBgColor = _isCapturingKey
                ? FUIColors.Warning.WithAlpha(40)
                : (_keyCaptureBoundsHovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);

            using var keyBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = keyBgColor };
            canvas.DrawRoundRect(_keyCaptureBounds, 3, 3, keyBgPaint);

            var keyFrameColor = _isCapturingKey
                ? FUIColors.Warning
                : (_keyCaptureBoundsHovered ? FUIColors.Primary : FUIColors.Frame);

            using var keyFramePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = keyFrameColor,
                StrokeWidth = _isCapturingKey ? 2f : 1f
            };
            canvas.DrawRoundRect(_keyCaptureBounds, 3, 3, keyFramePaint);

            // Display key combo or prompt
            if (_isCapturingKey)
            {
                byte alpha = (byte)(180 + MathF.Sin(_pulsePhase * 3) * 60);
                FUIRenderer.DrawTextCentered(canvas, "Press key combo...", _keyCaptureBounds, FUIColors.Warning.WithAlpha(alpha), 11f);
            }
            else if (!string.IsNullOrEmpty(_selectedKeyName))
            {
                // Draw keycaps centered in the field
                DrawKeycapsInBounds(canvas, _keyCaptureBounds, _selectedKeyName, _selectedModifiers);
            }
            else
            {
                FUIRenderer.DrawTextCentered(canvas, "Click to capture key", _keyCaptureBounds, FUIColors.TextDim, 11f);
            }
            y += keyFieldHeight + 15;
        }

        // Button Mode section
        FUIRenderer.DrawText(canvas, "BUTTON MODE", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += 20;

        // Mode buttons - all on one row
        string[] modes = { "Normal", "Toggle", "Pulse", "Hold" };
        float buttonHeight = 26f;
        float buttonGap = 4f;
        float totalGap = buttonGap * (modes.Length - 1);
        float buttonWidth = (width - totalGap) / modes.Length;

        for (int i = 0; i < modes.Length; i++)
        {
            float buttonX = leftMargin + i * (buttonWidth + buttonGap);
            var modeBounds = new SKRect(buttonX, y, buttonX + buttonWidth, y + buttonHeight);
            bool selected = i == (int)_selectedButtonMode;
            bool hovered = i == _hoveredButtonMode;

            SKColor bgColor = selected ? FUIColors.Active.WithAlpha(60) :
                (hovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);

            using var modeBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
            canvas.DrawRoundRect(modeBounds, 3, 3, modeBgPaint);

            using var modeFramePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = selected ? FUIColors.Active : FUIColors.Frame,
                StrokeWidth = selected ? 2f : 1f
            };
            canvas.DrawRoundRect(modeBounds, 3, 3, modeFramePaint);

            FUIRenderer.DrawTextCentered(canvas, modes[i], modeBounds,
                selected ? FUIColors.Active : FUIColors.TextPrimary, 9f);

            _buttonModeBounds[i] = modeBounds;
        }
        y += buttonHeight + 12;

        // Duration slider for Pulse mode
        if (_selectedButtonMode == ButtonMode.Pulse && y + 50 < bottom)
        {
            FUIRenderer.DrawText(canvas, "PULSE DURATION", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
            y += 18;

            float sliderHeight = 24f;
            _pulseDurationSliderBounds = new SKRect(leftMargin, y, rightMargin - 50, y + sliderHeight);

            // Normalize value: 100-1000ms mapped to 0-1
            float normalizedPulse = (_pulseDurationMs - 100f) / 900f;
            DrawDurationSlider(canvas, _pulseDurationSliderBounds, normalizedPulse, _draggingPulseDuration);

            // Value label
            FUIRenderer.DrawText(canvas, $"{_pulseDurationMs}ms",
                new SKPoint(rightMargin - 45, y + sliderHeight / 2 + 4), FUIColors.TextPrimary, 10f);

            y += sliderHeight + 12;
        }

        // Duration slider for Hold mode
        if (_selectedButtonMode == ButtonMode.HoldToActivate && y + 50 < bottom)
        {
            FUIRenderer.DrawText(canvas, "HOLD DURATION", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
            y += 18;

            float sliderHeight = 24f;
            _holdDurationSliderBounds = new SKRect(leftMargin, y, rightMargin - 50, y + sliderHeight);

            // Normalize value: 200-2000ms mapped to 0-1
            float normalizedHold = (_holdDurationMs - 200f) / 1800f;
            DrawDurationSlider(canvas, _holdDurationSliderBounds, normalizedHold, _draggingHoldDuration);

            // Value label
            FUIRenderer.DrawText(canvas, $"{_holdDurationMs}ms",
                new SKPoint(rightMargin - 45, y + sliderHeight / 2 + 4), FUIColors.TextPrimary, 10f);

            y += sliderHeight + 12;
        }

        // Clear binding button
        if (y + 40 < bottom)
        {
            var clearBounds = new SKRect(leftMargin, y, rightMargin, y + 32);
            _clearAllButtonBounds = clearBounds;

            using var clearBgPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = _clearAllButtonHovered ? FUIColors.Warning.WithAlpha(40) : FUIColors.Background2
            };
            canvas.DrawRoundRect(clearBounds, 3, 3, clearBgPaint);

            using var clearFramePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = _clearAllButtonHovered ? FUIColors.Warning : FUIColors.Warning.WithAlpha(150),
                StrokeWidth = _clearAllButtonHovered ? 2f : 1f
            };
            canvas.DrawRoundRect(clearBounds, 3, 3, clearFramePaint);

            FUIRenderer.DrawTextCentered(canvas, "Clear Binding", clearBounds,
                _clearAllButtonHovered ? FUIColors.Warning : FUIColors.Warning.WithAlpha(200), 11f);
        }
    }

    /// <summary>
    /// Format key combo for display as simple text (used in mapping names)
    /// </summary>
    private string FormatKeyComboForDisplay(string keyName, List<string>? modifiers)
    {
        if (string.IsNullOrEmpty(keyName)) return "";

        var parts = new List<string>();
        if (modifiers != null && modifiers.Count > 0)
        {
            parts.AddRange(modifiers);
        }
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    /// <summary>
    /// Draw keycaps centered within given bounds
    /// </summary>
    private void DrawKeycapsInBounds(SKCanvas canvas, SKRect bounds, string keyName, List<string>? modifiers)
    {
        // Build list of key parts
        var parts = new List<string>();
        if (modifiers != null && modifiers.Count > 0)
        {
            parts.AddRange(modifiers);
        }
        parts.Add(keyName);

        float keycapHeight = 20f;
        float keycapGap = 4f;
        float keycapPadding = 8f;  // Padding on each side of text
        float fontSize = 10f;
        float scaledFontSize = FUIRenderer.ScaleFont(fontSize);

        // Use consistent font for measurement
        using var measurePaint = new SKPaint
        {
            TextSize = scaledFontSize,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal)
        };

        // Calculate total width of all keycaps
        float totalWidth = 0;
        var keycapWidths = new List<float>();
        var textWidths = new List<float>();
        foreach (var part in parts)
        {
            string upperPart = part.ToUpperInvariant();
            float textWidth = measurePaint.MeasureText(upperPart);
            textWidths.Add(textWidth);
            float keycapWidth = textWidth + keycapPadding * 2;
            keycapWidths.Add(keycapWidth);
            totalWidth += keycapWidth;
        }
        totalWidth += (parts.Count - 1) * keycapGap;

        // Start position to center the keycaps
        float startX = bounds.MidX - totalWidth / 2;
        float keycapTop = bounds.MidY - keycapHeight / 2;

        // Draw each keycap
        for (int i = 0; i < parts.Count; i++)
        {
            string keyText = parts[i].ToUpperInvariant();
            float keycapWidth = keycapWidths[i];
            var keycapBounds = new SKRect(startX, keycapTop, startX + keycapWidth, keycapTop + keycapHeight);

            // Keycap background
            using var keycapBgPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = FUIColors.TextPrimary.WithAlpha(25),
                IsAntialias = true
            };
            canvas.DrawRoundRect(keycapBounds, 3, 3, keycapBgPaint);

            // Keycap frame
            using var keycapFramePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.TextPrimary.WithAlpha(150),
                StrokeWidth = 1f,
                IsAntialias = true
            };
            canvas.DrawRoundRect(keycapBounds, 3, 3, keycapFramePaint);

            // Keycap text - draw with explicit padding from left edge
            float textX = startX + keycapPadding;
            float textY = keycapBounds.MidY + scaledFontSize / 3;
            using var textPaint = new SKPaint
            {
                Color = FUIColors.TextPrimary,
                TextSize = scaledFontSize,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal)
            };
            canvas.DrawText(keyText, textX, textY, textPaint);

            startX += keycapWidth + keycapGap;
        }
    }

    private void DrawCurveVisualization(SKCanvas canvas, SKRect bounds)
    {
        // Background - darker than the panel
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background0 };
        canvas.DrawRect(bounds, bgPaint);

        // Grid lines (10% increments) - visible but subtle
        using var gridPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(60, 70, 80), // Visible gray grid lines
            StrokeWidth = 1f
        };

        for (float t = 0.1f; t < 1f; t += 0.1f)
        {
            // Skip 50% line - we'll draw it brighter
            if (Math.Abs(t - 0.5f) < 0.01f) continue;

            float x = bounds.Left + t * bounds.Width;
            float y = bounds.Bottom - t * bounds.Height;
            canvas.DrawLine(x, bounds.Top, x, bounds.Bottom, gridPaint);
            canvas.DrawLine(bounds.Left, y, bounds.Right, y, gridPaint);
        }

        // Center lines (brighter, 50% mark)
        using var centerPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(80, 95, 110), // More visible center lines
            StrokeWidth = 1f
        };
        canvas.DrawLine(bounds.MidX, bounds.Top, bounds.MidX, bounds.Bottom, centerPaint);
        canvas.DrawLine(bounds.Left, bounds.MidY, bounds.Right, bounds.MidY, centerPaint);

        // Reference linear line (dashed diagonal)
        using var refPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame.WithAlpha(50),
            StrokeWidth = 1f,
            PathEffect = SKPathEffect.CreateDash(new[] { 4f, 4f }, 0)
        };
        canvas.DrawLine(bounds.Left, bounds.Bottom, bounds.Right, bounds.Top, refPaint);

        // Draw the curve
        DrawCurvePath(canvas, bounds);

        // Draw control points (only for custom curve)
        if (_selectedCurveType == CurveType.Custom)
        {
            DrawCurveControlPoints(canvas, bounds);
        }

        // Frame
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        // Tick marks and labels on edges
        using var tickPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame.WithAlpha(150),
            StrokeWidth = 1f
        };

        float tickLen = 4f;
        float labelOffset = 3f;

        // Draw tick marks at 0%, 50%, 100% on bottom edge (IN axis)
        float[] tickPositions = { 0f, 0.5f, 1f };
        string[] tickLabels = { "0", "50", "100" };

        for (int i = 0; i < tickPositions.Length; i++)
        {
            float t = tickPositions[i];
            float x = bounds.Left + t * bounds.Width;

            // Bottom tick
            canvas.DrawLine(x, bounds.Bottom, x, bounds.Bottom + tickLen, tickPaint);

            // Label below tick
            float labelX = x - (t == 0 ? 0 : (t == 1 ? 12 : 6));
            FUIRenderer.DrawText(canvas, tickLabels[i], new SKPoint(labelX, bounds.Bottom + tickLen + labelOffset + 7), FUIColors.TextDim, 7f);
        }

        // Draw tick marks at 0%, 50%, 100% on left edge (OUT axis)
        for (int i = 0; i < tickPositions.Length; i++)
        {
            float t = tickPositions[i];
            float y = bounds.Bottom - t * bounds.Height;

            // Left tick
            canvas.DrawLine(bounds.Left - tickLen, y, bounds.Left, y, tickPaint);

            // Label left of tick
            float labelY = y + (t == 0 ? 3 : (t == 1 ? 7 : 3));
            float labelX = bounds.Left - tickLen - labelOffset - (tickLabels[i].Length > 1 ? 12 : 6);
            FUIRenderer.DrawText(canvas, tickLabels[i], new SKPoint(labelX, labelY), FUIColors.TextDim, 7f);
        }

        // Axis labels
        FUIRenderer.DrawText(canvas, "IN", new SKPoint(bounds.MidX - 6, bounds.Bottom + 22), FUIColors.TextDim, 8f);

        // Rotated "OUT" label
        canvas.Save();
        canvas.Translate(bounds.Left - 24, bounds.MidY + 8);
        canvas.RotateDegrees(-90);
        FUIRenderer.DrawText(canvas, "OUT", new SKPoint(0, 0), FUIColors.TextDim, 8f);
        canvas.Restore();
    }

    private void DrawCurvePath(SKCanvas canvas, SKRect bounds)
    {
        using var path = new SKPath();
        bool first = true;

        // Sample the curve at many points
        for (float t = 0; t <= 1.001f; t += 0.01f)
        {
            float input = Math.Min(t, 1f);
            float output = ComputeCurveValue(input);

            float x = bounds.Left + input * bounds.Width;
            float y = bounds.Bottom - output * bounds.Height;

            if (first)
            {
                path.MoveTo(x, y);
                first = false;
            }
            else
            {
                path.LineTo(x, y);
            }
        }

        // Glow
        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active.WithAlpha(50),
            StrokeWidth = 5f,
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(4f, 4f)
        };
        canvas.DrawPath(path, glowPaint);

        // Main line
        using var linePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active,
            StrokeWidth = 2f,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };
        canvas.DrawPath(path, linePaint);
    }

    private float ComputeCurveValue(float input)
    {
        // Apply curve type only - deadzone is handled separately
        float output = _selectedCurveType switch
        {
            CurveType.Linear => input,
            CurveType.SCurve => ApplySCurve(input),
            CurveType.Exponential => ApplyExponential(input),
            CurveType.Custom => InterpolateControlPoints(input),
            _ => input
        };

        output = Math.Clamp(output, 0f, 1f);

        // Apply inversion
        if (_axisInverted)
            output = 1f - output;

        return output;
    }

    private float ApplySCurve(float x)
    {
        // S-curve using smoothstep-like function
        return x * x * (3f - 2f * x);
    }

    private float ApplyExponential(float x)
    {
        // Exponential curve (steeper at the end)
        return x * x;
    }

    private float InterpolateControlPoints(float x)
    {
        if (_curveControlPoints.Count < 2) return x;

        // Find segment containing x
        for (int i = 0; i < _curveControlPoints.Count - 1; i++)
        {
            var p1 = _curveControlPoints[i];
            var p2 = _curveControlPoints[i + 1];

            if (x >= p1.X && x <= p2.X)
            {
                if (Math.Abs(p2.X - p1.X) < 0.001f) return p1.Y;
                float t = (x - p1.X) / (p2.X - p1.X);

                // Use Catmull-Rom spline interpolation for smooth curves
                // Need 4 points: p0, p1, p2, p3
                var p0 = i > 0 ? _curveControlPoints[i - 1] : new SKPoint(p1.X - (p2.X - p1.X), p1.Y - (p2.Y - p1.Y));
                var p3 = i < _curveControlPoints.Count - 2 ? _curveControlPoints[i + 2] : new SKPoint(p2.X + (p2.X - p1.X), p2.Y + (p2.Y - p1.Y));

                return CatmullRomInterpolate(p0.Y, p1.Y, p2.Y, p3.Y, t);
            }
        }

        // Extrapolate
        return x < _curveControlPoints[0].X ? _curveControlPoints[0].Y : _curveControlPoints[^1].Y;
    }

    /// <summary>
    /// Catmull-Rom spline interpolation for smooth curves through control points.
    /// t ranges from 0 to 1, output is between p1 and p2.
    /// </summary>
    private static float CatmullRomInterpolate(float p0, float p1, float p2, float p3, float t)
    {
        // Catmull-Rom spline formula with tension = 0.5 (centripetal)
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    private void DrawCurveControlPoints(SKCanvas canvas, SKRect bounds)
    {
        const float PointRadius = 7f;
        const float CenterPointRadius = 3.5f; // Half size for center point

        for (int i = 0; i < _curveControlPoints.Count; i++)
        {
            var pt = _curveControlPoints[i];
            float x = bounds.Left + pt.X * bounds.Width;

            // Apply inversion to display Y position to match the curve
            float displayY = _axisInverted ? (1f - pt.Y) : pt.Y;
            float y = bounds.Bottom - displayY * bounds.Height;

            bool isHovered = i == _hoveredCurvePoint;
            bool isDragging = i == _draggingCurvePoint;
            bool isEndpoint = i == 0 || i == _curveControlPoints.Count - 1;
            bool isCenterPoint = Math.Abs(pt.X - 0.5f) < 0.01f && Math.Abs(pt.Y - 0.5f) < 0.01f;

            // Center point is smaller and not interactive
            float baseRadius = isCenterPoint ? CenterPointRadius : PointRadius;
            float radius = (isHovered || isDragging) && !isCenterPoint ? baseRadius + 2 : baseRadius;
            var color = isDragging ? FUIColors.Warning : (isHovered && !isCenterPoint ? FUIColors.TextBright : FUIColors.Active);

            // Glow (skip for center point)
            if (!isCenterPoint)
            {
                using var glowPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = color.WithAlpha(40),
                    IsAntialias = true,
                    ImageFilter = SKImageFilter.CreateBlur(5f, 5f)
                };
                canvas.DrawCircle(x, y, radius + 4, glowPaint);
            }

            // Fill
            using var fillPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = isEndpoint || isCenterPoint ? FUIColors.Background1 : color.WithAlpha(60),
                IsAntialias = true
            };
            canvas.DrawCircle(x, y, radius, fillPaint);

            // Stroke
            using var strokePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = isCenterPoint ? FUIColors.Frame : color,
                StrokeWidth = isEndpoint ? 2f : (isCenterPoint ? 1f : 1.5f),
                IsAntialias = true
            };
            canvas.DrawCircle(x, y, radius, strokePaint);

            // Value label when hovered/dragged (not for center point)
            if ((isHovered || isDragging) && !isCenterPoint)
            {
                string label = $"({pt.X:F2}, {pt.Y:F2})";
                float labelY = y - radius - 10;
                if (labelY < bounds.Top + 10)
                    labelY = y + radius + 14;

                FUIRenderer.DrawText(canvas, label, new SKPoint(x - 22, labelY), FUIColors.TextBright, 8f);
            }
        }
    }

    private SKPoint CurveScreenToGraph(SKPoint screenPt, SKRect bounds)
    {
        float x = (screenPt.X - bounds.Left) / bounds.Width;
        float y = (bounds.Bottom - screenPt.Y) / bounds.Height;

        // If inverted, convert screen Y back to graph Y (uninvert)
        if (_axisInverted)
            y = 1f - y;

        return new SKPoint(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
    }

    private int FindCurvePointAt(SKPoint screenPt, SKRect bounds)
    {
        const float HitRadius = 12f;

        for (int i = 0; i < _curveControlPoints.Count; i++)
        {
            var pt = _curveControlPoints[i];

            // Skip center point - it's not selectable
            bool isCenterPoint = Math.Abs(pt.X - 0.5f) < 0.01f && Math.Abs(pt.Y - 0.5f) < 0.01f;
            if (isCenterPoint)
                continue;

            float x = bounds.Left + pt.X * bounds.Width;

            // Apply inversion to display Y position to match the visual
            float displayY = _axisInverted ? (1f - pt.Y) : pt.Y;
            float y = bounds.Bottom - displayY * bounds.Height;

            float dist = MathF.Sqrt(MathF.Pow(screenPt.X - x, 2) + MathF.Pow(screenPt.Y - y, 2));
            if (dist <= HitRadius)
                return i;
        }
        return -1;
    }

    private int FindDeadzoneHandleAt(SKPoint screenPt)
    {
        const float HitRadius = 12f;
        var bounds = _deadzoneSliderBounds;
        if (bounds.Width <= 0) return -1;

        // Convert deadzone values to 0..1 range
        float minPos = (_deadzoneMin + 1f) / 2f;
        float centerMinPos = (_deadzoneCenterMin + 1f) / 2f;
        float centerMaxPos = (_deadzoneCenterMax + 1f) / 2f;
        float maxPos = (_deadzoneMax + 1f) / 2f;

        if (_deadzoneCenterEnabled)
        {
            // Two separate tracks - must calculate handle positions on each track
            // Gap must match DrawDualDeadzoneSlider
            float gap = 24f;
            float centerX = bounds.MidX;

            // Left track: from bounds.Left to centerX - gap/2
            float leftTrackLeft = bounds.Left;
            float leftTrackRight = centerX - gap / 2;
            float leftTrackWidth = leftTrackRight - leftTrackLeft;

            // Right track: from centerX + gap/2 to bounds.Right
            float rightTrackLeft = centerX + gap / 2;
            float rightTrackRight = bounds.Right;
            float rightTrackWidth = rightTrackRight - rightTrackLeft;

            // Map positions to track coordinates
            float minPosInLeft = Math.Clamp((minPos - 0f) / 0.5f, 0f, 1f);
            float ctrMinPosInLeft = Math.Clamp((centerMinPos - 0f) / 0.5f, 0f, 1f);
            float ctrMaxPosInRight = Math.Clamp((centerMaxPos - 0.5f) / 0.5f, 0f, 1f);
            float maxPosInRight = Math.Clamp((maxPos - 0.5f) / 0.5f, 0f, 1f);

            // Calculate screen positions for each handle
            float minHandleX = leftTrackLeft + minPosInLeft * leftTrackWidth;
            float ctrMinHandleX = leftTrackLeft + ctrMinPosInLeft * leftTrackWidth;
            float ctrMaxHandleX = rightTrackLeft + ctrMaxPosInRight * rightTrackWidth;
            float maxHandleX = rightTrackLeft + maxPosInRight * rightTrackWidth;

            // Check each handle (check all 4)
            float[] handleXs = { minHandleX, ctrMinHandleX, ctrMaxHandleX, maxHandleX };
            for (int i = 0; i < 4; i++)
            {
                float dist = MathF.Sqrt(MathF.Pow(screenPt.X - handleXs[i], 2) + MathF.Pow(screenPt.Y - bounds.MidY, 2));
                if (dist <= HitRadius)
                    return i;
            }
        }
        else
        {
            // Single track - only min (0) and max (3) handles
            float minHandleX = bounds.Left + minPos * bounds.Width;
            float maxHandleX = bounds.Left + maxPos * bounds.Width;

            float distMin = MathF.Sqrt(MathF.Pow(screenPt.X - minHandleX, 2) + MathF.Pow(screenPt.Y - bounds.MidY, 2));
            if (distMin <= HitRadius) return 0;

            float distMax = MathF.Sqrt(MathF.Pow(screenPt.X - maxHandleX, 2) + MathF.Pow(screenPt.Y - bounds.MidY, 2));
            if (distMax <= HitRadius) return 3;
        }

        return -1;
    }

    private void UpdateDraggedDeadzoneHandle(SKPoint screenPt)
    {
        if (_draggingDeadzoneHandle < 0) return;
        var bounds = _deadzoneSliderBounds;
        if (bounds.Width <= 0) return;

        float value;

        if (_deadzoneCenterEnabled)
        {
            // Two-track layout - convert screen position to value based on which track
            // Gap must match DrawDualDeadzoneSlider
            float gap = 24f;
            float centerX = bounds.MidX;

            // Left track: maps to -1..0 (handles 0 and 1)
            float leftTrackLeft = bounds.Left;
            float leftTrackRight = centerX - gap / 2;
            float leftTrackWidth = leftTrackRight - leftTrackLeft;

            // Right track: maps to 0..1 (handles 2 and 3)
            float rightTrackLeft = centerX + gap / 2;
            float rightTrackRight = bounds.Right;
            float rightTrackWidth = rightTrackRight - rightTrackLeft;

            switch (_draggingDeadzoneHandle)
            {
                case 0: // Min handle on left track
                    float normLeft0 = Math.Clamp((screenPt.X - leftTrackLeft) / leftTrackWidth, 0f, 1f);
                    value = normLeft0 - 1f; // Maps 0..1 to -1..0
                    value = Math.Clamp(value, -1f, _deadzoneCenterMin - 0.02f);
                    _deadzoneMin = value;
                    break;
                case 1: // CenterMin handle on left track (right edge)
                    float normLeft1 = Math.Clamp((screenPt.X - leftTrackLeft) / leftTrackWidth, 0f, 1f);
                    value = normLeft1 - 1f; // Maps 0..1 to -1..0
                    value = Math.Clamp(value, _deadzoneMin + 0.02f, 0f);
                    _deadzoneCenterMin = value;
                    break;
                case 2: // CenterMax handle on right track (left edge)
                    float normRight2 = Math.Clamp((screenPt.X - rightTrackLeft) / rightTrackWidth, 0f, 1f);
                    value = normRight2; // Maps 0..1 to 0..1
                    value = Math.Clamp(value, 0f, _deadzoneMax - 0.02f);
                    _deadzoneCenterMax = value;
                    break;
                case 3: // Max handle on right track
                    float normRight3 = Math.Clamp((screenPt.X - rightTrackLeft) / rightTrackWidth, 0f, 1f);
                    value = normRight3; // Maps 0..1 to 0..1
                    value = Math.Clamp(value, _deadzoneCenterMax + 0.02f, 1f);
                    _deadzoneMax = value;
                    break;
            }
        }
        else
        {
            // Single track layout - convert screen X to -1..1 range
            float normalized = (screenPt.X - bounds.Left) / bounds.Width;
            value = normalized * 2f - 1f;

            switch (_draggingDeadzoneHandle)
            {
                case 0: // Min handle
                    value = Math.Clamp(value, -1f, _deadzoneMax - 0.1f);
                    _deadzoneMin = value;
                    break;
                case 3: // Max handle
                    value = Math.Clamp(value, _deadzoneMin + 0.1f, 1f);
                    _deadzoneMax = value;
                    break;
            }
        }
    }

    private void UpdateDraggedCurvePoint(SKPoint screenPt)
    {
        if (_draggingCurvePoint < 0 || _draggingCurvePoint >= _curveControlPoints.Count)
            return;

        var graphPt = CurveScreenToGraph(screenPt, _curveEditorBounds);

        // Constrain endpoints to X edges
        if (_draggingCurvePoint == 0)
            graphPt.X = 0;
        else if (_draggingCurvePoint == _curveControlPoints.Count - 1)
            graphPt.X = 1;
        else
        {
            // Interior points: constrain X between neighbors
            float minX = _curveControlPoints[_draggingCurvePoint - 1].X + 0.02f;
            float maxX = _curveControlPoints[_draggingCurvePoint + 1].X - 0.02f;
            graphPt.X = Math.Clamp(graphPt.X, minX, maxX);
        }

        _curveControlPoints[_draggingCurvePoint] = graphPt;

        // If symmetrical mode is enabled, mirror the change
        if (_curveSymmetrical)
        {
            UpdateSymmetricalPoint(_draggingCurvePoint, graphPt);
        }
    }

    /// <summary>
    /// Update the symmetrical counterpart of a curve point.
    /// Points are mirrored around the center (0.5, 0.5).
    /// </summary>
    private void UpdateSymmetricalPoint(int pointIndex, SKPoint graphPt)
    {
        // Mirror point: (x, y) -> (1-x, 1-y)
        float mirrorX = 1f - graphPt.X;
        float mirrorY = 1f - graphPt.Y;
        var mirrorPt = new SKPoint(mirrorX, mirrorY);

        // Find the corresponding mirror point in the list
        // Points are stored sorted by X, so we need to find the one with matching mirror X
        int mirrorIndex = FindMirrorPointIndex(pointIndex, mirrorX);

        if (mirrorIndex >= 0 && mirrorIndex != pointIndex)
        {
            // Update mirror point, but constrain to valid range
            if (mirrorIndex > 0 && mirrorIndex < _curveControlPoints.Count - 1)
            {
                // Interior point - constrain X between neighbors
                float minX = _curveControlPoints[mirrorIndex - 1].X + 0.02f;
                float maxX = _curveControlPoints[mirrorIndex + 1].X - 0.02f;
                mirrorPt = new SKPoint(Math.Clamp(mirrorPt.X, minX, maxX), mirrorPt.Y);
            }
            else if (mirrorIndex == 0)
            {
                mirrorPt = new SKPoint(0, mirrorPt.Y);
            }
            else if (mirrorIndex == _curveControlPoints.Count - 1)
            {
                mirrorPt = new SKPoint(1, mirrorPt.Y);
            }

            _curveControlPoints[mirrorIndex] = mirrorPt;
        }
    }

    /// <summary>
    /// Find the index of the mirror point for symmetry.
    /// Returns -1 if no suitable mirror point exists.
    /// </summary>
    private int FindMirrorPointIndex(int sourceIndex, float targetX)
    {
        // Special cases for endpoints
        if (sourceIndex == 0) return _curveControlPoints.Count - 1;
        if (sourceIndex == _curveControlPoints.Count - 1) return 0;

        // For interior points, find the one closest to the mirror X position
        int bestIndex = -1;
        float bestDist = float.MaxValue;

        for (int i = 0; i < _curveControlPoints.Count; i++)
        {
            if (i == sourceIndex) continue;

            float dist = Math.Abs(_curveControlPoints[i].X - targetX);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    /// <summary>
    /// Make the current curve points symmetrical around the center.
    /// </summary>
    private void MakeCurveSymmetrical()
    {
        if (_curveControlPoints.Count < 2) return;

        // Create a new symmetrical set of points
        var newPoints = new List<SKPoint>();

        // Always include start point
        newPoints.Add(new SKPoint(0, 0));

        // For each point in the left half (X < 0.5), create its mirror
        var leftHalf = _curveControlPoints
            .Where(p => p.X > 0 && p.X < 0.5f)
            .OrderBy(p => p.X)
            .ToList();

        foreach (var pt in leftHalf)
        {
            newPoints.Add(pt);
        }

        // Add center point if there's one
        var centerPoint = _curveControlPoints.FirstOrDefault(p => Math.Abs(p.X - 0.5f) < 0.02f);
        if (centerPoint.X > 0.4f && centerPoint.X < 0.6f)
        {
            newPoints.Add(new SKPoint(0.5f, 0.5f)); // Center is always (0.5, 0.5) for perfect symmetry
        }
        else if (leftHalf.Count > 0)
        {
            // Add a center point if we have left half points
            newPoints.Add(new SKPoint(0.5f, 0.5f));
        }

        // Add mirrored points from left half (in reverse order for right half)
        for (int i = leftHalf.Count - 1; i >= 0; i--)
        {
            var pt = leftHalf[i];
            newPoints.Add(new SKPoint(1f - pt.X, 1f - pt.Y));
        }

        // Always include end point
        newPoints.Add(new SKPoint(1, 1));

        _curveControlPoints = newPoints;
    }

    private void AddCurveControlPoint(SKPoint graphPt)
    {
        // Don't add points at exact endpoints
        if (graphPt.X <= 0.01f || graphPt.X >= 0.99f) return;

        // Find insertion position (maintain sorted order by X)
        int insertIndex = 0;
        for (int i = 0; i < _curveControlPoints.Count; i++)
        {
            if (_curveControlPoints[i].X < graphPt.X)
                insertIndex = i + 1;
        }

        _curveControlPoints.Insert(insertIndex, graphPt);

        // If symmetrical mode is enabled, also add the mirror point
        if (_curveSymmetrical)
        {
            float mirrorX = 1f - graphPt.X;
            float mirrorY = 1f - graphPt.Y;

            // Don't add if mirror point would be too close to existing point
            bool tooClose = _curveControlPoints.Any(p => Math.Abs(p.X - mirrorX) < 0.04f);
            if (!tooClose && mirrorX > 0.01f && mirrorX < 0.99f)
            {
                // Find insertion position for mirror point
                int mirrorInsertIndex = 0;
                for (int i = 0; i < _curveControlPoints.Count; i++)
                {
                    if (_curveControlPoints[i].X < mirrorX)
                        mirrorInsertIndex = i + 1;
                }

                _curveControlPoints.Insert(mirrorInsertIndex, new SKPoint(mirrorX, mirrorY));
            }
        }

        _selectedCurveType = CurveType.Custom;
        _canvas.Invalidate();
    }

    private void RemoveCurveControlPoint(int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= _curveControlPoints.Count)
            return;

        var pt = _curveControlPoints[pointIndex];

        // Don't remove endpoints (0,0) or (1,1)
        bool isEndpoint = pointIndex == 0 || pointIndex == _curveControlPoints.Count - 1;
        if (isEndpoint)
            return;

        // Don't remove center point (0.5, 0.5)
        bool isCenterPoint = Math.Abs(pt.X - 0.5f) < 0.01f && Math.Abs(pt.Y - 0.5f) < 0.01f;
        if (isCenterPoint)
            return;

        // Remove the point
        _curveControlPoints.RemoveAt(pointIndex);

        // If symmetrical mode is enabled, also remove the mirror point
        if (_curveSymmetrical)
        {
            float mirrorX = 1f - pt.X;

            // Find and remove the mirror point
            for (int i = _curveControlPoints.Count - 1; i >= 0; i--)
            {
                var mirrorPt = _curveControlPoints[i];
                // Skip endpoints and center
                if (i == 0 || i == _curveControlPoints.Count - 1)
                    continue;
                if (Math.Abs(mirrorPt.X - 0.5f) < 0.01f && Math.Abs(mirrorPt.Y - 0.5f) < 0.01f)
                    continue;

                if (Math.Abs(mirrorPt.X - mirrorX) < 0.02f)
                {
                    _curveControlPoints.RemoveAt(i);
                    break;
                }
            }
        }

        _canvas.Invalidate();
    }

    private bool HandleCurvePresetClick(SKPoint pt)
    {
        // Check each stored preset button bound
        for (int i = 0; i < _curvePresetBounds.Length; i++)
        {
            if (_curvePresetBounds[i].Contains(pt))
            {
                _selectedCurveType = i switch
                {
                    0 => CurveType.Linear,
                    1 => CurveType.SCurve,
                    2 => CurveType.Exponential,
                    _ => CurveType.Custom
                };

                // Reset control points when switching to custom
                if (_selectedCurveType == CurveType.Custom && _curveControlPoints.Count == 2)
                {
                    // Add a middle point for custom curve
                    _curveControlPoints = new List<SKPoint>
                    {
                        new(0, 0),
                        new(0.5f, 0.5f),
                        new(1, 1)
                    };
                }

                _canvas.Invalidate();
                return true;
            }
        }

        // Check invert checkbox
        if (_invertToggleBounds.Contains(pt))
        {
            _axisInverted = !_axisInverted;
            _canvas.Invalidate();
            return true;
        }

        // Check symmetrical checkbox (only for Custom curve)
        if (!_curveSymmetricalCheckboxBounds.IsEmpty && _curveSymmetricalCheckboxBounds.Contains(pt))
        {
            _curveSymmetrical = !_curveSymmetrical;
            if (_curveSymmetrical)
            {
                // When enabling symmetry, mirror existing points around center
                MakeCurveSymmetrical();
            }
            _canvas.Invalidate();
            return true;
        }

        // Check centre checkbox and deadzone presets
        if (HandleDeadzonePresetClick(pt))
            return true;

        return false;
    }

    private bool HandleDeadzonePresetClick(SKPoint pt)
    {
        // Centre checkbox click
        if (_deadzoneCenterCheckboxBounds.Contains(pt))
        {
            _deadzoneCenterEnabled = !_deadzoneCenterEnabled;
            // When disabling center, reset center values and clear selection if center handle was selected
            if (!_deadzoneCenterEnabled)
            {
                _deadzoneCenterMin = 0.0f;
                _deadzoneCenterMax = 0.0f;
                if (_selectedDeadzoneHandle == 1 || _selectedDeadzoneHandle == 2)
                    _selectedDeadzoneHandle = -1;
            }
            _canvas.Invalidate();
            return true;
        }

        // Preset buttons - apply to selected handle
        if (_selectedDeadzoneHandle >= 0)
        {
            // Preset values: 0%, 2%, 5%, 10%
            float[] presetValues = { 0.0f, 0.02f, 0.05f, 0.10f };

            for (int i = 0; i < _deadzonePresetBounds.Length; i++)
            {
                if (!_deadzonePresetBounds[i].IsEmpty && _deadzonePresetBounds[i].Contains(pt))
                {
                    float presetVal = presetValues[i];

                    switch (_selectedDeadzoneHandle)
                    {
                        case 0: // Min (Start) - set distance from -1
                            _deadzoneMin = -1.0f + presetVal;
                            break;
                        case 1: // CenterMin - set negative offset from 0
                            _deadzoneCenterMin = -presetVal;
                            break;
                        case 2: // CenterMax - set positive offset from 0
                            _deadzoneCenterMax = presetVal;
                            break;
                        case 3: // Max (End) - set distance from 1
                            _deadzoneMax = 1.0f - presetVal;
                            break;
                    }
                    _canvas.Invalidate();
                    return true;
                }
            }
        }
        return false;
    }

    private void DrawSlider(SKCanvas canvas, SKRect bounds, float value)
    {
        // Track background
        using var trackPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        canvas.DrawRoundRect(bounds, 4, 4, trackPaint);

        // Fill
        float fillWidth = bounds.Width * Math.Clamp(value, 0, 1);
        var fillBounds = new SKRect(bounds.Left, bounds.Top, bounds.Left + fillWidth, bounds.Bottom);
        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active };
        canvas.DrawRoundRect(fillBounds, 4, 4, fillPaint);

        // Handle
        float handleX = bounds.Left + fillWidth;
        float handleRadius = bounds.Height;
        using var handlePaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.TextBright };
        canvas.DrawCircle(handleX, bounds.MidY, handleRadius, handlePaint);
    }

    private void DrawToggleSwitch(SKCanvas canvas, SKRect bounds, bool on)
    {
        // Track
        SKColor trackColor = on ? FUIColors.Active.WithAlpha(150) : FUIColors.Background2;
        using var trackPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = trackColor };
        canvas.DrawRoundRect(bounds, bounds.Height / 2, bounds.Height / 2, trackPaint);

        // Frame
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = on ? FUIColors.Active : FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRoundRect(bounds, bounds.Height / 2, bounds.Height / 2, framePaint);

        // Knob
        float knobRadius = bounds.Height / 2 - 3;
        float knobX = on ? bounds.Right - knobRadius - 3 : bounds.Left + knobRadius + 3;
        using var knobPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.TextBright };
        canvas.DrawCircle(knobX, bounds.MidY, knobRadius, knobPaint);
    }

    private void DrawSettingsSlider(SKCanvas canvas, SKRect bounds, int value, int maxValue)
    {
        float trackHeight = 4f;
        float trackY = bounds.MidY - trackHeight / 2;
        var trackRect = new SKRect(bounds.Left, trackY, bounds.Right, trackY + trackHeight);

        // Track background
        using var trackBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        canvas.DrawRoundRect(trackRect, 2, 2, trackBgPaint);

        // Track frame
        using var trackFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
        canvas.DrawRoundRect(trackRect, 2, 2, trackFramePaint);

        // Filled portion
        float fillWidth = (bounds.Width - 6) * (value / (float)maxValue);
        if (fillWidth > 0)
        {
            var fillRect = new SKRect(bounds.Left + 2, trackY + 1, bounds.Left + 2 + fillWidth, trackY + trackHeight - 1);
            using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(180) };
            canvas.DrawRoundRect(fillRect, 1, 1, fillPaint);
        }

        // Knob
        float knobX = bounds.Left + 3 + (bounds.Width - 6) * (value / (float)maxValue);
        float knobRadius = 6f;
        using var knobPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.TextBright, IsAntialias = true };
        canvas.DrawCircle(knobX, bounds.MidY, knobRadius, knobPaint);

        using var knobFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawCircle(knobX, bounds.MidY, knobRadius, knobFramePaint);
    }

    private void DrawMappingEditorPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(160),
            IsAntialias = true
        };
        canvas.DrawRect(bounds.Inset(frameInset, frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Active, 30f, 8f);

        float y = bounds.Top + frameInset + 15;
        float leftMargin = bounds.Left + frameInset + 15;
        float rightMargin = bounds.Right - frameInset - 15;

        // Title
        string outputName = GetEditingOutputName();
        FUIRenderer.DrawText(canvas, $"EDIT: {outputName}", new SKPoint(leftMargin, y),
            FUIColors.Active, 14f, true);
        y += 30;

        // INPUT SOURCE section
        FUIRenderer.DrawText(canvas, "INPUT SOURCE", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += 20;

        // Input field - double-click to listen for input
        float inputFieldHeight = 36f;
        _inputFieldBounds = new SKRect(leftMargin, y, rightMargin, y + inputFieldHeight);
        DrawInputField(canvas, _inputFieldBounds);
        y += inputFieldHeight + 10;

        // Manual entry toggle button
        _manualEntryButtonBounds = new SKRect(leftMargin, y, leftMargin + 120, y + 24);
        DrawToggleButton(canvas, _manualEntryButtonBounds, "Manual Entry", _manualEntryMode, _manualEntryButtonHovered);
        y += 34;

        // Manual entry dropdowns (if enabled)
        if (_manualEntryMode)
        {
            y = DrawManualEntrySection(canvas, bounds, y, leftMargin, rightMargin);
        }

        // Output type and button mode section (only for button outputs)
        if (!_isEditingAxis)
        {
            // Output type selector (Button vs Keyboard)
            y += 10;
            FUIRenderer.DrawText(canvas, "OUTPUT TYPE", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
            y += 20;
            DrawOutputTypeSelector(canvas, leftMargin, y, rightMargin - leftMargin);
            y += 38;

            // Key capture field (only when Keyboard is selected)
            if (_outputTypeIsKeyboard)
            {
                FUIRenderer.DrawText(canvas, "KEY", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
                y += 20;
                float keyFieldHeight = 32f;
                _keyCaptureBounds = new SKRect(leftMargin, y, rightMargin, y + keyFieldHeight);
                DrawKeyCapture(canvas, _keyCaptureBounds);
                y += keyFieldHeight + 10;
            }

            // Button mode selector
            y += 10;
            FUIRenderer.DrawText(canvas, "BUTTON MODE", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
            y += 20;
            DrawButtonModeSelector(canvas, leftMargin, y, rightMargin - leftMargin);
            y += 40;
        }

        // Action buttons at bottom
        float buttonWidth = 80f;
        float buttonHeight = 32f;
        float buttonY = bounds.Bottom - frameInset - buttonHeight - 15;

        _cancelButtonBounds = new SKRect(rightMargin - buttonWidth * 2 - 10, buttonY,
            rightMargin - buttonWidth - 10, buttonY + buttonHeight);
        _saveButtonBounds = new SKRect(rightMargin - buttonWidth, buttonY,
            rightMargin, buttonY + buttonHeight);

        DrawActionButton(canvas, _cancelButtonBounds, "Cancel", _cancelButtonHovered, false);
        DrawActionButton(canvas, _saveButtonBounds, "Save", _saveButtonHovered, true);
    }

    private string GetEditingOutputName()
    {
        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count)
            return "Unknown";

        if (_isEditingAxis)
        {
            string[] axisNames = { "X Axis", "Y Axis", "Z Axis", "RX Axis", "RY Axis", "RZ Axis", "Slider 1", "Slider 2" };
            int axisIndex = _editingRowIndex;
            return axisIndex < axisNames.Length ? axisNames[axisIndex] : $"Axis {axisIndex}";
        }
        else
        {
            int buttonIndex = _editingRowIndex - 8;
            return $"Button {buttonIndex + 1}";
        }
    }

    private void DrawInputField(SKCanvas canvas, SKRect bounds)
    {
        // Background
        var bgColor = _isListeningForInput
            ? FUIColors.Warning.WithAlpha(40)
            : (_inputFieldHovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        // Frame
        var frameColor = _isListeningForInput
            ? FUIColors.Warning
            : (_inputFieldHovered ? FUIColors.Primary : FUIColors.Frame);
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = frameColor,
            StrokeWidth = _isListeningForInput ? 2f : 1f
        };
        canvas.DrawRect(bounds, framePaint);

        // Text content
        float textY = bounds.MidY + 5;
        if (_isListeningForInput)
        {
            byte alpha = (byte)(180 + MathF.Sin(_pulsePhase * 3) * 60);
            FUIRenderer.DrawText(canvas, "Press a button or move an axis...",
                new SKPoint(bounds.Left + 10, textY), FUIColors.Warning.WithAlpha(alpha), 12f);
        }
        else if (_pendingInput != null)
        {
            FUIRenderer.DrawText(canvas, _pendingInput.ToString(),
                new SKPoint(bounds.Left + 10, textY), FUIColors.TextBright, 12f);
        }
        else
        {
            FUIRenderer.DrawText(canvas, "Double-click to detect input",
                new SKPoint(bounds.Left + 10, textY), FUIColors.TextDisabled, 12f);
        }

        // Clear button if there's input
        if (_pendingInput != null && !_isListeningForInput)
        {
            var clearBounds = new SKRect(bounds.Right - 28, bounds.Top + 6, bounds.Right - 6, bounds.Bottom - 6);
            DrawSmallIconButton(canvas, clearBounds, "×", false, true);
        }
    }

    private void DrawToggleButton(SKCanvas canvas, SKRect bounds, string text, bool active, bool hovered)
    {
        var bgColor = active
            ? FUIColors.Active.WithAlpha(60)
            : (hovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);
        var textColor = active ? FUIColors.Active : (hovered ? FUIColors.TextPrimary : FUIColors.TextDim);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = active ? FUIColors.Active : FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 11f);
    }

    private float DrawManualEntrySection(SKCanvas canvas, SKRect bounds, float y, float leftMargin, float rightMargin)
    {
        // Device dropdown
        FUIRenderer.DrawText(canvas, "Device:", new SKPoint(leftMargin, y + 12), FUIColors.TextDim, 10f);
        float dropdownX = leftMargin + 55;
        _deviceDropdownBounds = new SKRect(dropdownX, y, rightMargin, y + 28);
        string deviceText = _devices.Count > 0 && _selectedSourceDevice < _devices.Count
            ? _devices[_selectedSourceDevice].Name
            : "No devices";
        DrawDropdown(canvas, _deviceDropdownBounds, deviceText, _deviceDropdownOpen);
        y += 36;

        // Control dropdown
        string controlLabel = _isEditingAxis ? "Axis:" : "Button:";
        FUIRenderer.DrawText(canvas, controlLabel, new SKPoint(leftMargin, y + 12), FUIColors.TextDim, 10f);
        _controlDropdownBounds = new SKRect(dropdownX, y, rightMargin, y + 28);
        string controlText = GetControlDropdownText();
        DrawDropdown(canvas, _controlDropdownBounds, controlText, _controlDropdownOpen);
        y += 36;

        // Draw dropdown lists if open
        if (_deviceDropdownOpen)
        {
            DrawDeviceDropdownList(canvas, _deviceDropdownBounds);
        }
        else if (_controlDropdownOpen)
        {
            DrawControlDropdownList(canvas, _controlDropdownBounds);
        }

        return y;
    }

    private string GetControlDropdownText()
    {
        if (_devices.Count == 0 || _selectedSourceDevice >= _devices.Count)
            return "—";

        var device = _devices[_selectedSourceDevice];
        if (_isEditingAxis)
        {
            int axisCount = 8; // Typical axis count
            if (_selectedSourceControl < axisCount)
                return $"Axis {_selectedSourceControl}";
        }
        else
        {
            if (_selectedSourceControl < 128)
                return $"Button {_selectedSourceControl + 1}";
        }
        return "—";
    }

    private void DrawDropdown(SKCanvas canvas, SKRect bounds, string text, bool open)
    {
        var bgColor = open ? FUIColors.Primary.WithAlpha(40) : FUIColors.Background2;
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = open ? FUIColors.Primary : FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        FUIRenderer.DrawText(canvas, text, new SKPoint(bounds.Left + 8, bounds.MidY + 4),
            FUIColors.TextPrimary, 11f);

        // Arrow indicator
        string arrow = open ? "▲" : "▼";
        FUIRenderer.DrawText(canvas, arrow, new SKPoint(bounds.Right - 18, bounds.MidY + 4),
            FUIColors.TextDim, 10f);
    }

    private void DrawDeviceDropdownList(SKCanvas canvas, SKRect anchorBounds)
    {
        float itemHeight = 26f;
        float listHeight = Math.Min(_devices.Count * itemHeight, 200);
        var listBounds = new SKRect(anchorBounds.Left, anchorBounds.Bottom + 2,
            anchorBounds.Right, anchorBounds.Bottom + 2 + listHeight);

        // Draw shadow/backdrop for visual separation
        using var shadowPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.Black.WithAlpha(120)
        };
        var shadowBounds = new SKRect(listBounds.Left - 1, listBounds.Top - 1, listBounds.Right + 5, listBounds.Bottom + 5);
        canvas.DrawRect(shadowBounds, shadowPaint);

        // Solid opaque background
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1 };
        canvas.DrawRect(listBounds, bgPaint);

        // Draw items
        float y = listBounds.Top;
        for (int i = 0; i < _devices.Count && y < listBounds.Bottom; i++)
        {
            var itemBounds = new SKRect(listBounds.Left, y, listBounds.Right, y + itemHeight);
            bool hovered = i == _hoveredDeviceIndex;

            if (hovered)
            {
                using var hoverPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Primary.WithAlpha(60) };
                canvas.DrawRect(itemBounds, hoverPaint);
            }

            FUIRenderer.DrawText(canvas, _devices[i].Name, new SKPoint(itemBounds.Left + 8, itemBounds.MidY + 4),
                hovered ? FUIColors.TextBright : FUIColors.TextPrimary, 11f);
            y += itemHeight;
        }

        // Frame on top
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Primary,
            StrokeWidth = 1f
        };
        canvas.DrawRect(listBounds, framePaint);
    }

    private void DrawControlDropdownList(SKCanvas canvas, SKRect anchorBounds)
    {
        int controlCount = _isEditingAxis ? 8 : 32; // Show first 8 axes or 32 buttons
        float itemHeight = 24f;
        float listHeight = Math.Min(controlCount * itemHeight, 200);
        var listBounds = new SKRect(anchorBounds.Left, anchorBounds.Bottom + 2,
            anchorBounds.Right, anchorBounds.Bottom + 2 + listHeight);

        // Draw shadow/backdrop for visual separation
        using var shadowPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.Black.WithAlpha(120)
        };
        var shadowBounds = new SKRect(listBounds.Left - 1, listBounds.Top - 1, listBounds.Right + 5, listBounds.Bottom + 5);
        canvas.DrawRect(shadowBounds, shadowPaint);

        // Solid opaque background
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1 };
        canvas.DrawRect(listBounds, bgPaint);

        // Draw items
        float y = listBounds.Top;
        for (int i = 0; i < controlCount && y < listBounds.Bottom; i++)
        {
            var itemBounds = new SKRect(listBounds.Left, y, listBounds.Right, y + itemHeight);
            bool hovered = i == _hoveredControlIndex;

            if (hovered)
            {
                using var hoverPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Primary.WithAlpha(60) };
                canvas.DrawRect(itemBounds, hoverPaint);
            }

            string name = _isEditingAxis ? $"Axis {i}" : $"Button {i + 1}";
            FUIRenderer.DrawText(canvas, name, new SKPoint(itemBounds.Left + 8, itemBounds.MidY + 4),
                hovered ? FUIColors.TextBright : FUIColors.TextPrimary, 11f);
            y += itemHeight;
        }

        // Frame on top
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Primary,
            StrokeWidth = 1f
        };
        canvas.DrawRect(listBounds, framePaint);
    }

    private void DrawButtonModeSelector(SKCanvas canvas, float x, float y, float width)
    {
        ButtonMode[] modes = { ButtonMode.Normal, ButtonMode.Toggle, ButtonMode.Pulse, ButtonMode.HoldToActivate };
        string[] labels = { "Normal", "Toggle", "Pulse", "Hold" };
        float buttonWidth = (width - 15) / 4;
        float buttonHeight = 28f;

        for (int i = 0; i < modes.Length; i++)
        {
            var modeBounds = new SKRect(x + i * (buttonWidth + 5), y, x + i * (buttonWidth + 5) + buttonWidth, y + buttonHeight);
            _buttonModeBounds[i] = modeBounds;

            bool selected = _selectedButtonMode == modes[i];
            bool hovered = _hoveredButtonMode == i;

            var bgColor = selected
                ? FUIColors.Active.WithAlpha(60)
                : (hovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);
            var textColor = selected ? FUIColors.Active : (hovered ? FUIColors.TextPrimary : FUIColors.TextDim);

            using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
            canvas.DrawRect(modeBounds, bgPaint);

            using var framePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = selected ? FUIColors.Active : FUIColors.Frame,
                StrokeWidth = selected ? 2f : 1f
            };
            canvas.DrawRect(modeBounds, framePaint);

            FUIRenderer.DrawTextCentered(canvas, labels[i], modeBounds, textColor, 10f);
        }
    }

    private void DrawOutputTypeSelector(SKCanvas canvas, float x, float y, float width)
    {
        string[] labels = { "Button", "Keyboard" };
        float buttonWidth = (width - 5) / 2;
        float buttonHeight = 28f;

        for (int i = 0; i < 2; i++)
        {
            var typeBounds = new SKRect(x + i * (buttonWidth + 5), y, x + i * (buttonWidth + 5) + buttonWidth, y + buttonHeight);
            if (i == 0) _outputTypeBtnBounds = typeBounds;
            else _outputTypeKeyBounds = typeBounds;

            bool selected = (i == 0 && !_outputTypeIsKeyboard) || (i == 1 && _outputTypeIsKeyboard);
            bool hovered = _hoveredOutputType == i;

            var bgColor = selected
                ? FUIColors.Active.WithAlpha(60)
                : (hovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);
            var textColor = selected ? FUIColors.Active : (hovered ? FUIColors.TextPrimary : FUIColors.TextDim);

            using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
            canvas.DrawRect(typeBounds, bgPaint);

            using var framePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = selected ? FUIColors.Active : FUIColors.Frame,
                StrokeWidth = selected ? 2f : 1f
            };
            canvas.DrawRect(typeBounds, framePaint);

            FUIRenderer.DrawTextCentered(canvas, labels[i], typeBounds, textColor, 11f);
        }
    }

    private void DrawKeyCapture(SKCanvas canvas, SKRect bounds)
    {
        // Background
        var bgColor = _isCapturingKey
            ? FUIColors.Warning.WithAlpha(40)
            : (_keyCaptureBoundsHovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        // Frame
        var frameColor = _isCapturingKey
            ? FUIColors.Warning
            : (_keyCaptureBoundsHovered ? FUIColors.Primary : FUIColors.Frame);
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = frameColor,
            StrokeWidth = _isCapturingKey ? 2f : 1f
        };
        canvas.DrawRect(bounds, framePaint);

        // Text content
        float textY = bounds.MidY + 5;
        if (_isCapturingKey)
        {
            byte alpha = (byte)(180 + MathF.Sin(_pulsePhase * 3) * 60);
            FUIRenderer.DrawText(canvas, "Press a key...",
                new SKPoint(bounds.Left + 10, textY), FUIColors.Warning.WithAlpha(alpha), 12f);
        }
        else if (!string.IsNullOrEmpty(_selectedKeyName))
        {
            FUIRenderer.DrawText(canvas, _selectedKeyName,
                new SKPoint(bounds.Left + 10, textY), FUIColors.TextBright, 12f);
        }
        else
        {
            FUIRenderer.DrawText(canvas, "Click to capture key",
                new SKPoint(bounds.Left + 10, textY), FUIColors.TextDisabled, 12f);
        }

        // Clear button if there's a key
        if (!string.IsNullOrEmpty(_selectedKeyName) && !_isCapturingKey)
        {
            var clearBounds = new SKRect(bounds.Right - 28, bounds.Top + 6, bounds.Right - 6, bounds.Bottom - 6);
            DrawSmallIconButton(canvas, clearBounds, "×", false, true);
        }
    }

    private void DrawActionButton(SKCanvas canvas, SKRect bounds, string text, bool hovered, bool isPrimary)
    {
        var bgColor = isPrimary
            ? (hovered ? FUIColors.Active : FUIColors.Active.WithAlpha(180))
            : (hovered ? FUIColors.Primary.WithAlpha(60) : FUIColors.Background2);
        var textColor = isPrimary
            ? FUIColors.Background1
            : (hovered ? FUIColors.TextBright : FUIColors.TextPrimary);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = isPrimary ? FUIColors.Active : FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 12f);
    }

    private void DrawArrowButton(SKCanvas canvas, SKRect bounds, string arrow, bool hovered, bool enabled)
    {
        var bgColor = enabled
            ? (hovered ? FUIColors.Primary.WithAlpha(80) : FUIColors.Background2)
            : FUIColors.Background1;
        var arrowColor = enabled
            ? (hovered ? FUIColors.TextBright : FUIColors.TextPrimary)
            : FUIColors.TextDisabled;

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = enabled ? FUIColors.Frame : FUIColors.FrameDim,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        // Draw arrow shape instead of text (more reliable rendering)
        float centerX = bounds.MidX;
        float centerY = bounds.MidY;
        float arrowSize = 8f;

        using var arrowPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = arrowColor,
            IsAntialias = true
        };

        using var path = new SKPath();
        if (arrow == "<")
        {
            // Left arrow: <
            path.MoveTo(centerX + arrowSize / 2, centerY - arrowSize);
            path.LineTo(centerX - arrowSize / 2, centerY);
            path.LineTo(centerX + arrowSize / 2, centerY + arrowSize);
            path.Close();
        }
        else
        {
            // Right arrow: >
            path.MoveTo(centerX - arrowSize / 2, centerY - arrowSize);
            path.LineTo(centerX + arrowSize / 2, centerY);
            path.LineTo(centerX - arrowSize / 2, centerY + arrowSize);
            path.Close();
        }
        canvas.DrawPath(path, arrowPaint);
    }

    private void DrawOutputMappingList(SKCanvas canvas, SKRect bounds)
    {
        _mappingRowBounds.Clear();
        _mappingAddButtonBounds.Clear();
        _mappingRemoveButtonBounds.Clear();

        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count)
        {
            FUIRenderer.DrawText(canvas, "No vJoy devices available",
                new SKPoint(bounds.Left + 20, bounds.Top + 20), FUIColors.TextDim, 12f);
            FUIRenderer.DrawText(canvas, "Install vJoy driver to create mappings",
                new SKPoint(bounds.Left + 20, bounds.Top + 40), FUIColors.TextDisabled, 11f);
            return;
        }

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        var profile = _profileService.ActiveProfile;

        float rowHeight = 32f;
        float rowGap = 4f;
        float y = bounds.Top;
        int rowIndex = 0;

        // Section: AXES
        FUIRenderer.DrawText(canvas, "AXES", new SKPoint(bounds.Left + 5, y + 14), FUIColors.Active, 11f);
        y += 20;

        string[] axisNames = { "X Axis", "Y Axis", "Z Axis", "RX Axis", "RY Axis", "RZ Axis", "Slider 1", "Slider 2" };
        for (int i = 0; i < Math.Min(axisNames.Length, 8); i++)
        {
            if (y + rowHeight > bounds.Bottom) break;

            var rowBounds = new SKRect(bounds.Left, y, bounds.Right, y + rowHeight);
            string binding = GetAxisBindingText(profile, vjoyDevice.Id, i);
            bool isSelected = rowIndex == _selectedMappingRow;
            bool isHovered = rowIndex == _hoveredMappingRow;
            bool isEditing = _mappingEditorOpen && rowIndex == _editingRowIndex;

            DrawMappingRow(canvas, rowBounds, axisNames[i], binding, isSelected, isHovered, isEditing, rowIndex, !string.IsNullOrEmpty(binding) && binding != "—");

            _mappingRowBounds.Add(rowBounds);
            y += rowHeight + rowGap;
            rowIndex++;
        }

        // Section: BUTTONS
        y += 10;
        if (y + 20 < bounds.Bottom)
        {
            FUIRenderer.DrawText(canvas, "BUTTONS", new SKPoint(bounds.Left + 5, y + 14), FUIColors.Active, 11f);
            y += 20;
        }

        for (int i = 0; i < vjoyDevice.ButtonCount && y + rowHeight <= bounds.Bottom; i++)
        {
            var rowBounds = new SKRect(bounds.Left, y, bounds.Right, y + rowHeight);
            string binding = GetButtonBindingText(profile, vjoyDevice.Id, i);
            bool isSelected = rowIndex == _selectedMappingRow;
            bool isHovered = rowIndex == _hoveredMappingRow;
            bool isEditing = _mappingEditorOpen && rowIndex == _editingRowIndex;

            DrawMappingRow(canvas, rowBounds, $"Button {i + 1}", binding, isSelected, isHovered, isEditing, rowIndex, !string.IsNullOrEmpty(binding) && binding != "—");

            _mappingRowBounds.Add(rowBounds);
            y += rowHeight + rowGap;
            rowIndex++;
        }
    }

    private string GetAxisBindingText(MappingProfile? profile, uint vjoyId, int axisIndex)
    {
        if (profile == null) return "—";

        var mapping = profile.AxisMappings.FirstOrDefault(m =>
            m.Output.Type == OutputType.VJoyAxis &&
            m.Output.VJoyDevice == vjoyId &&
            m.Output.Index == axisIndex);

        if (mapping == null || mapping.Inputs.Count == 0) return "—";

        var input = mapping.Inputs[0];
        return $"{input.DeviceName} - Axis {input.Index}";
    }

    private string GetButtonBindingText(MappingProfile? profile, uint vjoyId, int buttonIndex)
    {
        if (profile == null) return "—";

        // Find mapping for this button slot (either VJoyButton or Keyboard output type)
        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyId &&
            m.Output.Index == buttonIndex);

        if (mapping == null || mapping.Inputs.Count == 0) return "—";

        var input = mapping.Inputs[0];
        if (input.Type == InputType.Button)
            return $"{input.DeviceName} - Button {input.Index + 1}";
        return $"{input.DeviceName} - {input.Type} {input.Index}";
    }

    private void DrawMappingRow(SKCanvas canvas, SKRect bounds, string outputName, string binding,
        bool isSelected, bool isHovered, bool isEditing, int rowIndex, bool hasBind)
    {
        // Background
        SKColor bgColor;
        if (isEditing)
            bgColor = FUIColors.Active.WithAlpha(60);
        else if (isSelected)
            bgColor = FUIColors.Active.WithAlpha(40);
        else if (isHovered)
            bgColor = FUIColors.Primary.WithAlpha(30);
        else
            bgColor = FUIColors.Background2.WithAlpha(60);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        // Frame
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = isEditing ? FUIColors.Active : (isSelected ? FUIColors.Active.WithAlpha(150) : (isHovered ? FUIColors.FrameBright : FUIColors.Frame.WithAlpha(80))),
            StrokeWidth = isEditing ? 2f : (isSelected ? 1.5f : 1f)
        };
        canvas.DrawRect(bounds, framePaint);

        // Output name (left)
        float textY = bounds.MidY + 5;
        FUIRenderer.DrawText(canvas, outputName, new SKPoint(bounds.Left + 10, textY),
            isEditing ? FUIColors.Active : FUIColors.TextPrimary, 12f);

        // Binding (center)
        float bindingX = bounds.Left + 100;
        var bindColor = binding == "—" ? FUIColors.TextDisabled : FUIColors.TextDim;
        FUIRenderer.DrawText(canvas, binding, new SKPoint(bindingX, textY), bindColor, 11f);

        // [+] button (Edit/Add)
        float buttonSize = 24f;
        float buttonY = bounds.MidY - buttonSize / 2;
        float addButtonX = bounds.Right - (hasBind ? 60 : 35);
        var addBounds = new SKRect(addButtonX, buttonY, addButtonX + buttonSize, buttonY + buttonSize);
        _mappingAddButtonBounds.Add(addBounds);

        bool addHovered = rowIndex == _hoveredAddButton;
        string addIcon = hasBind ? "✎" : "+";  // Pencil for edit, plus for add
        DrawSmallIconButton(canvas, addBounds, addIcon, addHovered);

        // [×] button (only if bound)
        if (hasBind)
        {
            float removeButtonX = bounds.Right - 32;
            var removeBounds = new SKRect(removeButtonX, buttonY, removeButtonX + buttonSize, buttonY + buttonSize);
            _mappingRemoveButtonBounds.Add(removeBounds);

            bool removeHovered = rowIndex == _hoveredRemoveButton;
            DrawSmallIconButton(canvas, removeBounds, "×", removeHovered, true);
        }
        else
        {
            _mappingRemoveButtonBounds.Add(SKRect.Empty);
        }
    }

    private void DrawSmallIconButton(SKCanvas canvas, SKRect bounds, string icon, bool hovered, bool isDanger = false)
    {
        var bgColor = hovered
            ? (isDanger ? FUIColors.Warning.WithAlpha(60) : FUIColors.Active.WithAlpha(60))
            : FUIColors.Background2.WithAlpha(100);
        var textColor = hovered
            ? (isDanger ? FUIColors.Warning : FUIColors.Active)
            : FUIColors.TextDim;

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = hovered ? (isDanger ? FUIColors.Warning : FUIColors.Active) : FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        FUIRenderer.DrawTextCentered(canvas, icon, bounds, textColor, 14f);
    }

    private void OpenMappingEditor(int rowIndex)
    {
        if (!_profileService.HasActiveProfile)
        {
            CreateNewProfilePrompt();
            if (!_profileService.HasActiveProfile) return;
        }
        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count) return;

        // Cancel any existing listening
        CancelInputListening();

        _mappingEditorOpen = true;
        _editingRowIndex = rowIndex;
        _selectedMappingRow = rowIndex;
        _isEditingAxis = rowIndex < 8;
        _pendingInput = null;
        _manualEntryMode = false;
        _selectedButtonMode = ButtonMode.Normal;
        _selectedSourceDevice = 0;
        _selectedSourceControl = 0;

        // Load existing binding if present
        LoadExistingBinding(rowIndex);
    }

    private void LoadExistingBinding(int rowIndex)
    {
        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        bool isAxis = rowIndex < 8;
        int outputIndex = isAxis ? rowIndex : rowIndex - 8;

        if (isAxis)
        {
            var mapping = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (mapping != null && mapping.Inputs.Count > 0)
            {
                var input = mapping.Inputs[0];
                _pendingInput = new DetectedInput
                {
                    DeviceGuid = Guid.TryParse(input.DeviceId, out var guid) ? guid : Guid.Empty,
                    DeviceName = input.DeviceName,
                    Type = input.Type,
                    Index = input.Index,
                    Value = 0
                };

                // Set selected device in dropdown
                for (int i = 0; i < _devices.Count; i++)
                {
                    if (_devices[i].InstanceGuid.ToString() == input.DeviceId)
                    {
                        _selectedSourceDevice = i;
                        break;
                    }
                }
                _selectedSourceControl = input.Index;
            }
        }
        else
        {
            var mapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyButton &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (mapping != null && mapping.Inputs.Count > 0)
            {
                var input = mapping.Inputs[0];
                _pendingInput = new DetectedInput
                {
                    DeviceGuid = Guid.TryParse(input.DeviceId, out var guid) ? guid : Guid.Empty,
                    DeviceName = input.DeviceName,
                    Type = input.Type,
                    Index = input.Index,
                    Value = 0
                };
                _selectedButtonMode = mapping.Mode;

                // Set selected device in dropdown
                for (int i = 0; i < _devices.Count; i++)
                {
                    if (_devices[i].InstanceGuid.ToString() == input.DeviceId)
                    {
                        _selectedSourceDevice = i;
                        break;
                    }
                }
                _selectedSourceControl = input.Index;
            }
        }
    }

    private void CloseMappingEditor()
    {
        CancelInputListening();
        _mappingEditorOpen = false;
        _editingRowIndex = -1;
        _pendingInput = null;
        _deviceDropdownOpen = false;
        _controlDropdownOpen = false;
    }

    private async void StartListeningForInput()
    {
        if (_isListeningForInput) return;
        if (!_mappingEditorOpen) return;

        _isListeningForInput = true;
        _pendingInput = null;

        // Determine input type based on what we're editing
        var filter = _isEditingAxis ? InputDetectionFilter.Axes : InputDetectionFilter.Buttons;

        _inputDetectionService ??= new InputDetectionService(_inputService);

        try
        {
            // Wait for actual input change - use a delay to skip initial state
            await Task.Delay(200); // Small delay to let user release any currently pressed buttons

            var detected = await _inputDetectionService.WaitForInputAsync(filter, 0.5f, 15000);

            if (detected != null && _mappingEditorOpen)
            {
                _pendingInput = detected;

                // Update manual entry dropdowns to match detected input
                for (int i = 0; i < _devices.Count; i++)
                {
                    if (_devices[i].InstanceGuid == detected.DeviceGuid)
                    {
                        _selectedSourceDevice = i;
                        break;
                    }
                }
                _selectedSourceControl = detected.Index;
            }
        }
        catch (Exception)
        {
            // Cancelled or error
        }
        finally
        {
            _isListeningForInput = false;
        }
    }

    private void SaveMapping()
    {
        if (!_mappingEditorOpen || _pendingInput == null) return;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        int outputIndex = _isEditingAxis ? _editingRowIndex : _editingRowIndex - 8;

        // Remove existing binding
        RemoveBindingAtRow(_editingRowIndex, save: false);

        if (_isEditingAxis)
        {
            var mapping = new AxisMapping
            {
                Name = $"{_pendingInput.DeviceName} Axis {_pendingInput.Index} -> vJoy {vjoyDevice.Id} Axis {outputIndex}",
                Inputs = new List<InputSource> { _pendingInput.ToInputSource() },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyAxis,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                },
                Curve = new AxisCurve()
            };
            profile.AxisMappings.Add(mapping);
        }
        else
        {
            var mapping = new ButtonMapping
            {
                Name = $"{_pendingInput.DeviceName} Button {_pendingInput.Index + 1} -> vJoy {vjoyDevice.Id} Button {outputIndex + 1}",
                Inputs = new List<InputSource> { _pendingInput.ToInputSource() },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyButton,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                },
                Mode = _selectedButtonMode
            };
            profile.ButtonMappings.Add(mapping);
        }

        _profileService.SaveActiveProfile();
        CloseMappingEditor();
    }

    private void CreateBindingFromManualEntry()
    {
        if (!_manualEntryMode || _devices.Count == 0 || _selectedSourceDevice >= _devices.Count) return;

        var device = _devices[_selectedSourceDevice];
        _pendingInput = new DetectedInput
        {
            DeviceGuid = device.InstanceGuid,
            DeviceName = device.Name,
            Type = _isEditingAxis ? InputType.Axis : InputType.Button,
            Index = _selectedSourceControl,
            Value = 0
        };
    }

    /// <summary>
    /// Create 1:1 mappings from the selected physical device to a user-selected vJoy device.
    /// Maps all axes, buttons, and hats directly without any curves or modifications.
    /// </summary>
    private void CreateOneToOneMappings()
    {
        // Validate selection
        if (_selectedDevice < 0 || _selectedDevice >= _devices.Count) return;

        var physicalDevice = _devices[_selectedDevice];
        if (physicalDevice.IsVirtual) return; // Only map physical devices

        // Ensure vJoy devices are loaded
        if (_vjoyDevices.Count == 0)
        {
            _vjoyDevices = _vjoyService.EnumerateDevices();
        }

        // Check if we have any vJoy devices
        if (_vjoyDevices.Count == 0)
        {
            ShowVJoyConfigurationHelp(physicalDevice, noDevices: true);
            return;
        }

        // Show vJoy device selection dialog
        var vjoyDevice = ShowVJoyDeviceSelectionDialog(physicalDevice);
        if (vjoyDevice == null) return; // User cancelled

        // Update selected vJoy device index
        _selectedVJoyDeviceIndex = _vjoyDevices.IndexOf(vjoyDevice);

        // Ensure we have an active profile
        var profile = _profileService.ActiveProfile;
        if (profile == null)
        {
            profile = _profileService.CreateAndActivateProfile($"1:1 - {physicalDevice.Name}");
        }

        // Build device ID for InputSource (using GUID)
        string deviceId = physicalDevice.InstanceGuid.ToString();

        // Remove any existing mappings from this device to this vJoy device
        profile.AxisMappings.RemoveAll(m =>
            m.Inputs.Any(i => i.DeviceId == deviceId) &&
            m.Output.VJoyDevice == vjoyDevice.Id);
        profile.ButtonMappings.RemoveAll(m =>
            m.Inputs.Any(i => i.DeviceId == deviceId) &&
            m.Output.VJoyDevice == vjoyDevice.Id);
        profile.HatMappings.RemoveAll(m =>
            m.Inputs.Any(i => i.DeviceId == deviceId) &&
            m.Output.VJoyDevice == vjoyDevice.Id);

        // Create axis mappings using actual axis types when available
        // Maps each physical axis to the corresponding vJoy axis based on type (X->X, Slider->Slider, etc.)
        var vjoyAxisIndices = GetVJoyAxisIndices(vjoyDevice);
        var usedVJoyAxes = new HashSet<int>();
        var sliderCount = 0; // Track how many sliders we've mapped

        LogMapping($"=== 1:1 Mapping for {physicalDevice.Name} ===");
        LogMapping($"Device: {physicalDevice.Name}, AxisCount: {physicalDevice.AxisCount}, AxisInfos.Count: {physicalDevice.AxisInfos.Count}");
        LogMapping($"HidDevicePath: {physicalDevice.HidDevicePath}");
        foreach (var ai in physicalDevice.AxisInfos)
        {
            LogMapping($"  AxisInfo: Index={ai.Index}, Type={ai.Type}, vJoyIndex={ai.ToVJoyAxisIndex()}");
        }

        for (int i = 0; i < physicalDevice.AxisCount; i++)
        {
            // Determine the target vJoy axis index based on axis type
            int vjoyAxisIndex;
            var axisInfo = physicalDevice.AxisInfos.FirstOrDefault(a => a.Index == i);

            LogMapping($"Processing axis {i}: axisInfo={axisInfo?.Type.ToString() ?? "NULL"}");

            if (axisInfo != null && axisInfo.Type != AxisType.Unknown)
            {
                // Use actual axis type to determine vJoy target
                vjoyAxisIndex = axisInfo.ToVJoyAxisIndex();

                // Handle multiple sliders (Slider0 = 6, Slider1 = 7)
                if (axisInfo.Type == AxisType.Slider)
                {
                    vjoyAxisIndex = 6 + sliderCount; // Map to Slider0 first, then Slider1
                    sliderCount++;
                }
            }
            else
            {
                // Fallback: use sequential mapping if axis type is unknown
                vjoyAxisIndex = i < vjoyAxisIndices.Count ? vjoyAxisIndices[i] : -1;
            }

            // Skip if the vJoy axis isn't available or already used
            if (vjoyAxisIndex < 0 || !vjoyAxisIndices.Contains(vjoyAxisIndex) || usedVJoyAxes.Contains(vjoyAxisIndex))
            {
                // Try to find an unused vJoy axis as fallback
                vjoyAxisIndex = vjoyAxisIndices.FirstOrDefault(idx => !usedVJoyAxes.Contains(idx), -1);
                if (vjoyAxisIndex < 0) continue; // No more vJoy axes available
            }

            usedVJoyAxes.Add(vjoyAxisIndex);
            string vjoyAxisName = GetVJoyAxisName(vjoyAxisIndex);
            string physicalAxisName = axisInfo?.TypeName ?? $"Axis {i}";

            var mapping = new AxisMapping
            {
                Name = $"{physicalDevice.Name} {physicalAxisName} -> vJoy {vjoyDevice.Id} {vjoyAxisName}",
                Inputs = new List<InputSource>
                {
                    new InputSource
                    {
                        DeviceId = deviceId,
                        DeviceName = physicalDevice.Name,
                        Type = InputType.Axis,
                        Index = i
                    }
                },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyAxis,
                    VJoyDevice = vjoyDevice.Id,
                    Index = vjoyAxisIndex
                },
                Curve = new AxisCurve { Type = CurveType.Linear }
            };
            profile.AxisMappings.Add(mapping);
        }

        // Create button mappings
        int buttonsToMap = Math.Min(physicalDevice.ButtonCount, vjoyDevice.ButtonCount);

        for (int i = 0; i < buttonsToMap; i++)
        {
            var mapping = new ButtonMapping
            {
                Name = $"{physicalDevice.Name} Btn {i + 1} -> vJoy {vjoyDevice.Id} Btn {i + 1}",
                Inputs = new List<InputSource>
                {
                    new InputSource
                    {
                        DeviceId = deviceId,
                        DeviceName = physicalDevice.Name,
                        Type = InputType.Button,
                        Index = i
                    }
                },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyButton,
                    VJoyDevice = vjoyDevice.Id,
                    Index = i
                },
                Mode = ButtonMode.Normal
            };
            profile.ButtonMappings.Add(mapping);
        }

        // Create hat/POV mappings
        int hatsToMap = Math.Min(physicalDevice.HatCount, vjoyDevice.ContPovCount + vjoyDevice.DiscPovCount);

        for (int i = 0; i < hatsToMap; i++)
        {
            var mapping = new HatMapping
            {
                Name = $"{physicalDevice.Name} Hat {i} -> vJoy {vjoyDevice.Id} POV {i}",
                Inputs = new List<InputSource>
                {
                    new InputSource
                    {
                        DeviceId = deviceId,
                        DeviceName = physicalDevice.Name,
                        Type = InputType.Hat,
                        Index = i
                    }
                },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyPov,
                    VJoyDevice = vjoyDevice.Id,
                    Index = i
                },
                UseContinuous = vjoyDevice.ContPovCount > i
            };
            profile.HatMappings.Add(mapping);
        }

        // Save the profile
        _profileService.SaveActiveProfile();

        // Refresh profiles list
        _profiles = _profileService.ListProfiles();

        // Switch to Mappings tab to show the new mappings
        _activeTab = 1;
        _canvas.Invalidate();
    }

    /// <summary>
    /// Count the number of axes configured on a vJoy device
    /// </summary>
    private int CountVJoyAxes(VJoyDeviceInfo vjoy)
    {
        int count = 0;
        if (vjoy.HasAxisX) count++;
        if (vjoy.HasAxisY) count++;
        if (vjoy.HasAxisZ) count++;
        if (vjoy.HasAxisRX) count++;
        if (vjoy.HasAxisRY) count++;
        if (vjoy.HasAxisRZ) count++;
        if (vjoy.HasSlider0) count++;
        if (vjoy.HasSlider1) count++;
        return count;
    }

    /// <summary>
    /// Get the list of available vJoy axis indices in standard order.
    /// Returns indices 0-7 corresponding to X, Y, Z, RX, RY, RZ, Slider0, Slider1.
    /// </summary>
    private List<int> GetVJoyAxisIndices(VJoyDeviceInfo vjoy)
    {
        var indices = new List<int>();
        if (vjoy.HasAxisX) indices.Add(0);   // X
        if (vjoy.HasAxisY) indices.Add(1);   // Y
        if (vjoy.HasAxisZ) indices.Add(2);   // Z
        if (vjoy.HasAxisRX) indices.Add(3);  // RX
        if (vjoy.HasAxisRY) indices.Add(4);  // RY
        if (vjoy.HasAxisRZ) indices.Add(5);  // RZ
        if (vjoy.HasSlider0) indices.Add(6); // Slider0
        if (vjoy.HasSlider1) indices.Add(7); // Slider1
        return indices;
    }

    /// <summary>
    /// Get a human-readable name for a vJoy axis index.
    /// </summary>
    private string GetVJoyAxisName(int index)
    {
        return index switch
        {
            0 => "X",
            1 => "Y",
            2 => "Z",
            3 => "RX",
            4 => "RY",
            5 => "RZ",
            6 => "Slider1",
            7 => "Slider2",
            _ => $"Axis{index}"
        };
    }

    /// <summary>
    /// Find the best vJoy device that can accommodate all controls from the physical device
    /// </summary>
    private VJoyDeviceInfo? FindBestVJoyDevice(PhysicalDeviceInfo physical)
    {
        VJoyDeviceInfo? best = null;
        int bestScore = -1;

        foreach (var vjoy in _vjoyDevices)
        {
            int axes = CountVJoyAxes(vjoy);
            int buttons = vjoy.ButtonCount;
            int povs = vjoy.ContPovCount + vjoy.DiscPovCount;

            // Check if this vJoy can accommodate all controls
            if (axes >= physical.AxisCount &&
                buttons >= physical.ButtonCount &&
                povs >= physical.HatCount)
            {
                // Score based on how close the match is (lower excess = better)
                int excess = (axes - physical.AxisCount) +
                            (buttons - physical.ButtonCount) +
                            (povs - physical.HatCount);
                int score = 1000 - excess; // Higher score = better match

                if (score > bestScore)
                {
                    bestScore = score;
                    best = vjoy;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Show help dialog for vJoy configuration with recommended settings
    /// </summary>
    private void ShowVJoyConfigurationHelp(PhysicalDeviceInfo physical, bool noDevices)
    {
        string message;
        if (noDevices)
        {
            message = "No vJoy devices are configured.\n\n";
        }
        else
        {
            message = "No vJoy device has enough capacity for this physical device.\n\n";
        }

        message += $"To create a 1:1 mapping for {physical.Name}, configure a vJoy device with:\n\n" +
                   $"  Axes: {physical.AxisCount} (X, Y, Z, Rx, Ry, Rz, Slider, Dial)\n" +
                   $"  Buttons: {physical.ButtonCount}\n" +
                   $"  POV Hats: {physical.HatCount} (Continuous recommended)\n\n" +
                   "Would you like to open the vJoy Configuration utility?";

        var result = FUIMessageBox.ShowQuestion(this, message, "vJoy Configuration Required");

        if (result)
        {
            LaunchVJoyConfigurator();
        }
    }

    /// <summary>
    /// Attempt to launch the vJoy configuration utility
    /// </summary>
    private void LaunchVJoyConfigurator()
    {
        // Common vJoy installation paths
        string[] possiblePaths = new[]
        {
            @"C:\Program Files\vJoy\x64\vJoyConf.exe",
            @"C:\Program Files\vJoy\x86\vJoyConf.exe",
            @"C:\Program Files (x86)\vJoy\x64\vJoyConf.exe",
            @"C:\Program Files (x86)\vJoy\x86\vJoyConf.exe"
        };

        string? vjoyConfPath = possiblePaths.FirstOrDefault(File.Exists);

        if (vjoyConfPath != null)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = vjoyConfPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                FUIMessageBox.ShowError(this,
                    $"Failed to launch vJoy Configurator:\n{ex.Message}",
                    "Launch Failed");
            }
        }
        else
        {
            FUIMessageBox.ShowWarning(this,
                "vJoy Configuration utility (vJoyConf.exe) was not found.\n\n" +
                "Please install vJoy from:\nhttps://github.com/jshafer817/vJoy/releases\n\n" +
                "Or manually run vJoyConf.exe from your vJoy installation folder.",
                "vJoy Not Found");
        }
    }

    /// <summary>
    /// Show a dialog to select a vJoy device for 1:1 mapping.
    /// Returns the selected device or null if cancelled.
    /// </summary>
    private VJoyDeviceInfo? ShowVJoyDeviceSelectionDialog(PhysicalDeviceInfo physicalDevice)
    {
        var items = new List<FUISelectionDialog.SelectionItem>();

        // Add vJoy devices to list
        foreach (var vjoy in _vjoyDevices)
        {
            int axes = CountVJoyAxes(vjoy);
            int buttons = vjoy.ButtonCount;
            int povs = vjoy.ContPovCount + vjoy.DiscPovCount;

            string status;
            if (axes >= physicalDevice.AxisCount &&
                buttons >= physicalDevice.ButtonCount &&
                povs >= physicalDevice.HatCount)
            {
                status = "[OK]";
            }
            else
            {
                status = "[partial]";
            }

            items.Add(new FUISelectionDialog.SelectionItem
            {
                Text = $"vJoy #{vjoy.Id}: {axes} axes, {buttons} buttons, {povs} POVs",
                Status = status,
                Tag = vjoy
            });
        }

        // Add option to configure new vJoy device
        items.Add(new FUISelectionDialog.SelectionItem
        {
            Text = "+ Configure new vJoy device...",
            IsAction = true
        });

        string description = $"Select a vJoy device to map {physicalDevice.Name}:\n" +
                           $"({physicalDevice.AxisCount} axes, {physicalDevice.ButtonCount} buttons, {physicalDevice.HatCount} hats)";

        int selectedIndex = FUISelectionDialog.Show(this, "Select vJoy Device", description, items, "Map 1:1", "Cancel");

        if (selectedIndex < 0)
            return null;

        // Check if user selected "Configure new vJoy device"
        if (selectedIndex == _vjoyDevices.Count)
        {
            ShowVJoyConfigurationHelp(physicalDevice, noDevices: false);
            return null;
        }

        if (selectedIndex >= 0 && selectedIndex < _vjoyDevices.Count)
        {
            var selectedVJoy = _vjoyDevices[selectedIndex];

            // Warn about partial mappings if necessary
            int axes = CountVJoyAxes(selectedVJoy);
            int buttons = selectedVJoy.ButtonCount;
            int povs = selectedVJoy.ContPovCount + selectedVJoy.DiscPovCount;

            if (axes < physicalDevice.AxisCount ||
                buttons < physicalDevice.ButtonCount ||
                povs < physicalDevice.HatCount)
            {
                var result = FUIMessageBox.ShowQuestion(this,
                    $"vJoy #{selectedVJoy.Id} doesn't have enough capacity.\n\n" +
                    $"Physical device: {physicalDevice.AxisCount} axes, {physicalDevice.ButtonCount} buttons, {physicalDevice.HatCount} hats\n" +
                    $"vJoy #{selectedVJoy.Id}: {axes} axes, {buttons} buttons, {povs} POVs\n\n" +
                    "Some controls will not be mapped. Continue?",
                    "Partial Mapping");

                if (!result)
                    return null;
            }

            return selectedVJoy;
        }

        return null;
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private static void LogMapping(string message)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Asteriq", "axis_types.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] [Mapping] {message}\n");
    }

    /// <summary>
    /// Clear all mappings for the selected physical device.
    /// </summary>
    private void ClearDeviceMappings()
    {
        if (_selectedDevice < 0 || _selectedDevice >= _devices.Count) return;

        var physicalDevice = _devices[_selectedDevice];
        if (physicalDevice.IsVirtual) return;

        var result = FUIMessageBox.ShowQuestion(this,
            $"Remove all mappings for {physicalDevice.Name}?\n\nThis will remove axis, button, and hat mappings from all vJoy devices.",
            "Clear Mappings");

        if (!result) return;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        string deviceId = physicalDevice.InstanceGuid.ToString();

        // Remove all mappings from this device
        int axisRemoved = profile.AxisMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));
        int buttonRemoved = profile.ButtonMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));
        int hatRemoved = profile.HatMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));

        _profileService.SaveActiveProfile();

        FUIMessageBox.ShowInfo(this,
            $"Removed {axisRemoved} axis, {buttonRemoved} button, and {hatRemoved} hat mappings.",
            "Mappings Cleared");

        _canvas.Invalidate();
    }

    /// <summary>
    /// Remove a disconnected device completely from the app's data.
    /// This clears all mappings and removes it from the disconnected devices list.
    /// </summary>
    private void RemoveDisconnectedDevice()
    {
        if (_selectedDevice < 0 || _selectedDevice >= _devices.Count) return;

        var device = _devices[_selectedDevice];
        if (device.IsConnected || device.IsVirtual) return; // Only works for disconnected physical devices

        var result = FUIMessageBox.ShowQuestion(this,
            $"Permanently remove {device.Name}?\n\n" +
            "This will:\n" +
            "• Clear all axis, button, and hat mappings\n" +
            "• Remove the device from the disconnected list\n\n" +
            "This cannot be undone.",
            "Remove Device");

        if (!result) return;

        var profile = _profileService.ActiveProfile;
        string deviceId = device.InstanceGuid.ToString();

        // Remove all mappings from this device
        int axisRemoved = 0, buttonRemoved = 0, hatRemoved = 0;
        if (profile != null)
        {
            axisRemoved = profile.AxisMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));
            buttonRemoved = profile.ButtonMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));
            hatRemoved = profile.HatMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));
            _profileService.SaveActiveProfile();
        }

        // Remove from disconnected devices list
        _disconnectedDevices.RemoveAll(d => d.InstanceGuid == device.InstanceGuid);
        SaveDisconnectedDevices();

        // Refresh and update selection
        RefreshDevices();
        if (_selectedDevice >= _devices.Count)
        {
            _selectedDevice = Math.Max(0, _devices.Count - 1);
        }

        FUIMessageBox.ShowInfo(this,
            $"Device removed.\n\nCleared {axisRemoved} axis, {buttonRemoved} button, and {hatRemoved} hat mappings.",
            "Device Removed");

        _canvas.Invalidate();
    }

    private void CreateBindingForRow(int rowIndex, DetectedInput input)
    {
        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        bool isAxis = rowIndex < 8;
        int outputIndex = isAxis ? rowIndex : rowIndex - 8;

        // Remove existing binding for this output
        RemoveBindingAtRow(rowIndex, save: false);

        if (isAxis)
        {
            var mapping = new AxisMapping
            {
                Name = $"{input.DeviceName} Axis {input.Index} -> vJoy {vjoyDevice.Id} Axis {outputIndex}",
                Inputs = new List<InputSource> { input.ToInputSource() },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyAxis,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                },
                Curve = new AxisCurve()
            };
            profile.AxisMappings.Add(mapping);
        }
        else
        {
            var mapping = new ButtonMapping
            {
                Name = $"{input.DeviceName} Button {input.Index + 1} -> vJoy {vjoyDevice.Id} Button {outputIndex + 1}",
                Inputs = new List<InputSource> { input.ToInputSource() },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyButton,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                },
                Mode = ButtonMode.Normal
            };
            profile.ButtonMappings.Add(mapping);
        }

        _profileService.SaveActiveProfile();
    }

    private void RemoveBindingAtRow(int rowIndex, bool save = true)
    {
        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        bool isAxis = rowIndex < 8;
        int outputIndex = isAxis ? rowIndex : rowIndex - 8;

        if (isAxis)
        {
            var existing = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);
            if (existing != null)
            {
                profile.AxisMappings.Remove(existing);
            }
        }
        else
        {
            var existing = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyButton &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);
            if (existing != null)
            {
                profile.ButtonMappings.Remove(existing);
            }
        }

        if (save)
        {
            _profileService.SaveActiveProfile();
        }
    }

    private void CancelInputListening()
    {
        if (_isListeningForInput)
        {
            _inputDetectionService?.Cancel();
            _isListeningForInput = false;
        }
    }

    /// <summary>
    /// Check if a physical input is already mapped anywhere in the profile.
    /// Returns the mapping name if found, null otherwise.
    /// </summary>
    private string? FindExistingMappingForInput(MappingProfile profile, InputSource inputToCheck)
    {
        // Check axis mappings
        foreach (var mapping in profile.AxisMappings)
        {
            foreach (var input in mapping.Inputs)
            {
                if (input.DeviceId == inputToCheck.DeviceId &&
                    input.Type == inputToCheck.Type &&
                    input.Index == inputToCheck.Index)
                {
                    return mapping.Name;
                }
            }
        }

        // Check button mappings
        foreach (var mapping in profile.ButtonMappings)
        {
            foreach (var input in mapping.Inputs)
            {
                if (input.DeviceId == inputToCheck.DeviceId &&
                    input.Type == inputToCheck.Type &&
                    input.Index == inputToCheck.Index)
                {
                    return mapping.Name;
                }
            }
        }

        // Check hat mappings
        foreach (var mapping in profile.HatMappings)
        {
            foreach (var input in mapping.Inputs)
            {
                if (input.DeviceId == inputToCheck.DeviceId &&
                    input.Type == inputToCheck.Type &&
                    input.Index == inputToCheck.Index)
                {
                    return mapping.Name;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Show a confirmation dialog when a duplicate mapping is detected.
    /// Returns true if the user wants to proceed and replace the existing mapping.
    /// </summary>
    private bool ConfirmDuplicateMapping(string existingMappingName, string newMappingTarget)
    {
        using var dialog = new FUIConfirmDialog(
            "Duplicate Mapping",
            $"This input is already mapped to:\n\n{existingMappingName}\n\nRemove existing and create new mapping for {newMappingTarget}?",
            "Replace",
            "Cancel");
        return dialog.ShowDialog(this) == DialogResult.Yes;
    }

    /// <summary>
    /// Remove any existing mappings that use the specified input source.
    /// </summary>
    private void RemoveExistingMappingsForInput(MappingProfile profile, InputSource inputToRemove)
    {
        // Remove from axis mappings
        foreach (var mapping in profile.AxisMappings.ToList())
        {
            mapping.Inputs.RemoveAll(i =>
                i.DeviceId == inputToRemove.DeviceId &&
                i.Type == inputToRemove.Type &&
                i.Index == inputToRemove.Index);

            // If no inputs remain, remove the mapping entirely
            if (mapping.Inputs.Count == 0)
            {
                profile.AxisMappings.Remove(mapping);
            }
        }

        // Remove from button mappings
        foreach (var mapping in profile.ButtonMappings.ToList())
        {
            mapping.Inputs.RemoveAll(i =>
                i.DeviceId == inputToRemove.DeviceId &&
                i.Type == inputToRemove.Type &&
                i.Index == inputToRemove.Index);

            if (mapping.Inputs.Count == 0)
            {
                profile.ButtonMappings.Remove(mapping);
            }
        }

        // Remove from hat mappings
        foreach (var mapping in profile.HatMappings.ToList())
        {
            mapping.Inputs.RemoveAll(i =>
                i.DeviceId == inputToRemove.DeviceId &&
                i.Type == inputToRemove.Type &&
                i.Index == inputToRemove.Index);

            if (mapping.Inputs.Count == 0)
            {
                profile.HatMappings.Remove(mapping);
            }
        }
    }

    private async void StartInputListening(int rowIndex)
    {
        if (_isListeningForInput) return;
        if (rowIndex < 0) return;

        _isListeningForInput = true;
        _pendingInput = null;

        // Determine input type based on row (first 8 are axes)
        bool isAxis = rowIndex < 8;
        var filter = isAxis ? InputDetectionFilter.Axes : InputDetectionFilter.Buttons;

        _inputDetectionService ??= new InputDetectionService(_inputService);

        try
        {
            // Small delay to let user release any currently pressed buttons
            await Task.Delay(200);

            var detected = await _inputDetectionService.WaitForInputAsync(filter, 0.5f, 15000);

            if (detected != null && _selectedMappingRow == rowIndex)
            {
                _pendingInput = detected;
                var inputSource = detected.ToInputSource();

                // Check for duplicate mapping
                var profile = _profileService.ActiveProfile;
                if (profile != null)
                {
                    var existingMapping = FindExistingMappingForInput(profile, inputSource);
                    if (existingMapping != null)
                    {
                        string newTarget = isAxis ? $"vJoy Axis {rowIndex}" : $"vJoy Button {rowIndex - 8 + 1}";
                        if (!ConfirmDuplicateMapping(existingMapping, newTarget))
                        {
                            // User cancelled, don't create the mapping
                            return;
                        }
                        // User confirmed, remove existing mapping first
                        RemoveExistingMappingsForInput(profile, inputSource);
                    }
                }

                // Save the mapping using current panel settings (output type, key combo, button mode)
                SaveMappingForRow(rowIndex, detected, isAxis);
            }
        }
        catch (Exception)
        {
            // Cancelled or error
        }
        finally
        {
            _isListeningForInput = false;
        }
    }

    private void SaveMappingForRow(int rowIndex, DetectedInput input, bool isAxis)
    {
        var profile = _profileService.ActiveProfile;
        if (profile == null) return;
        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        int outputIndex = isAxis ? rowIndex : rowIndex - 8;
        var newInputSource = input.ToInputSource();

        if (isAxis)
        {
            // Find existing mapping or create new one
            var existingMapping = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (existingMapping != null)
            {
                // Add input to existing mapping (support multiple inputs)
                existingMapping.Inputs.Add(newInputSource);
                existingMapping.Name = $"vJoy {vjoyDevice.Id} Axis {outputIndex} ({existingMapping.Inputs.Count} inputs)";
            }
            else
            {
                // Create new mapping
                var mapping = new AxisMapping
                {
                    Name = $"{input.DeviceName} Axis {input.Index} -> vJoy {vjoyDevice.Id} Axis {outputIndex}",
                    Inputs = new List<InputSource> { newInputSource },
                    Output = new OutputTarget
                    {
                        Type = OutputType.VJoyAxis,
                        VJoyDevice = vjoyDevice.Id,
                        Index = outputIndex
                    },
                    Curve = new AxisCurve()
                };
                profile.AxisMappings.Add(mapping);
            }
        }
        else
        {
            // Find existing mapping for this button slot (regardless of output type)
            var existingMapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (existingMapping != null)
            {
                // Add input to existing mapping (support multiple inputs)
                existingMapping.Inputs.Add(newInputSource);

                // Update with current panel settings
                existingMapping.Output.Type = _outputTypeIsKeyboard ? OutputType.Keyboard : OutputType.VJoyButton;
                if (_outputTypeIsKeyboard)
                {
                    existingMapping.Output.KeyName = _selectedKeyName;
                    existingMapping.Output.Modifiers = _selectedModifiers?.ToList();
                }
                else
                {
                    existingMapping.Output.KeyName = null;
                    existingMapping.Output.Modifiers = null;
                }
                existingMapping.Mode = _selectedButtonMode;
                existingMapping.Name = $"vJoy {vjoyDevice.Id} Button {outputIndex + 1} ({existingMapping.Inputs.Count} inputs)";
            }
            else
            {
                // Create new mapping using current panel settings
                var outputType = _outputTypeIsKeyboard ? OutputType.Keyboard : OutputType.VJoyButton;
                var outputTarget = new OutputTarget
                {
                    Type = outputType,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                };

                if (_outputTypeIsKeyboard)
                {
                    outputTarget.KeyName = _selectedKeyName;
                    outputTarget.Modifiers = _selectedModifiers?.ToList();
                }

                string mappingName = _outputTypeIsKeyboard && !string.IsNullOrEmpty(_selectedKeyName)
                    ? $"{input.DeviceName} Button {input.Index + 1} -> {FormatKeyComboForDisplay(_selectedKeyName, _selectedModifiers)}"
                    : $"{input.DeviceName} Button {input.Index + 1} -> vJoy {vjoyDevice.Id} Button {outputIndex + 1}";

                var mapping = new ButtonMapping
                {
                    Name = mappingName,
                    Inputs = new List<InputSource> { newInputSource },
                    Output = outputTarget,
                    Mode = _selectedButtonMode
                };
                profile.ButtonMappings.Add(mapping);
            }
        }

        profile.ModifiedAt = DateTime.UtcNow;
        _profileService.SaveActiveProfile();
        _pendingInput = null;
    }

    private void RemoveInputSourceAtIndex(int inputIndex)
    {
        if (_selectedMappingRow < 0) return;
        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count) return;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        int outputIndex = _selectedMappingRow;

        if (isAxis)
        {
            var mapping = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (mapping != null && inputIndex >= 0 && inputIndex < mapping.Inputs.Count)
            {
                mapping.Inputs.RemoveAt(inputIndex);
                if (mapping.Inputs.Count == 0)
                {
                    // Remove the entire mapping if no inputs left
                    profile.AxisMappings.Remove(mapping);
                }
            }
        }
        else
        {
            var mapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyButton &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (mapping != null && inputIndex >= 0 && inputIndex < mapping.Inputs.Count)
            {
                mapping.Inputs.RemoveAt(inputIndex);
                if (mapping.Inputs.Count == 0)
                {
                    // Remove the entire mapping if no inputs left
                    profile.ButtonMappings.Remove(mapping);
                }
            }
        }

        profile.ModifiedAt = DateTime.UtcNow;
        _profileService.SaveActiveProfile();
    }

    private void LoadOutputTypeStateForRow()
    {
        // Reset state
        _outputTypeIsKeyboard = false;
        _selectedKeyName = "";
        _selectedModifiers = null;
        _isCapturingKey = false;
        _selectedButtonMode = ButtonMode.Normal;
        _pulseDurationMs = 100;
        _holdDurationMs = 500;

        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count) return;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping != null)
        {
            _outputTypeIsKeyboard = mapping.Output.Type == OutputType.Keyboard;
            _selectedKeyName = mapping.Output.KeyName ?? "";
            _selectedModifiers = mapping.Output.Modifiers?.ToList();
            _selectedButtonMode = mapping.Mode;
            _pulseDurationMs = mapping.PulseDurationMs;
            _holdDurationMs = mapping.HoldDurationMs;
        }
    }

    private void UpdateButtonModeForSelected()
    {
        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count) return;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        // Find mapping for this button slot (either VJoyButton or Keyboard output)
        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping != null)
        {
            mapping.Mode = _selectedButtonMode;
            profile.ModifiedAt = DateTime.UtcNow;
            _profileService.SaveActiveProfile();
        }
    }

    private void UpdateOutputTypeForSelected()
    {
        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count) return;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        // Find mapping for this button slot
        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping != null)
        {
            // Update output type and clear/set key name
            mapping.Output.Type = _outputTypeIsKeyboard ? OutputType.Keyboard : OutputType.VJoyButton;
            if (!_outputTypeIsKeyboard)
            {
                mapping.Output.KeyName = null;
                mapping.Output.Modifiers = null;
            }
            else if (!string.IsNullOrEmpty(_selectedKeyName))
            {
                mapping.Output.KeyName = _selectedKeyName;
            }
            profile.ModifiedAt = DateTime.UtcNow;
            _profileService.SaveActiveProfile();
        }
    }

    private void UpdateKeyNameForSelected()
    {
        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count) return;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping != null)
        {
            mapping.Output.KeyName = _selectedKeyName;
            mapping.Output.Modifiers = _selectedModifiers?.ToList();
            profile.ModifiedAt = DateTime.UtcNow;
            _profileService.SaveActiveProfile();
        }
    }

    private void UpdatePulseDurationFromMouse(float mouseX)
    {
        if (_pulseDurationSliderBounds.Width <= 0) return;

        float normalized = (mouseX - _pulseDurationSliderBounds.Left) / _pulseDurationSliderBounds.Width;
        normalized = Math.Clamp(normalized, 0f, 1f);

        // Map 0-1 to 100-1000ms
        _pulseDurationMs = (int)(100f + normalized * 900f);
    }

    private void UpdateHoldDurationFromMouse(float mouseX)
    {
        if (_holdDurationSliderBounds.Width <= 0) return;

        float normalized = (mouseX - _holdDurationSliderBounds.Left) / _holdDurationSliderBounds.Width;
        normalized = Math.Clamp(normalized, 0f, 1f);

        // Map 0-1 to 200-2000ms
        _holdDurationMs = (int)(200f + normalized * 1800f);
    }

    private void UpdateDurationForSelectedMapping()
    {
        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count) return;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping != null)
        {
            mapping.PulseDurationMs = _pulseDurationMs;
            mapping.HoldDurationMs = _holdDurationMs;
            profile.ModifiedAt = DateTime.UtcNow;
            _profileService.SaveActiveProfile();
        }
    }

    private void DrawAddMappingButton(SKCanvas canvas, SKRect bounds, bool hovered)
    {
        var bgColor = hovered ? FUIColors.Active.WithAlpha(60) : FUIColors.Primary.WithAlpha(30);
        var frameColor = hovered ? FUIColors.Active : FUIColors.Primary;

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = frameColor,
            StrokeWidth = hovered ? 2f : 1f,
            IsAntialias = true
        };
        canvas.DrawRect(bounds, framePaint);

        // Plus icon
        float iconX = bounds.Left + 15;
        float iconY = bounds.MidY;
        using var iconPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = hovered ? FUIColors.TextBright : FUIColors.TextPrimary,
            StrokeWidth = 2f,
            IsAntialias = true
        };
        canvas.DrawLine(iconX - 6, iconY, iconX + 6, iconY, iconPaint);
        canvas.DrawLine(iconX, iconY - 6, iconX, iconY + 6, iconPaint);

        FUIRenderer.DrawText(canvas, "ADD MAPPING",
            new SKPoint(bounds.Left + 30, bounds.MidY + 5),
            hovered ? FUIColors.TextBright : FUIColors.TextPrimary, 12f);
    }

    private void DrawMappingList(SKCanvas canvas, SKRect bounds)
    {
        float itemHeight = 50f;
        float itemGap = 8f;
        float y = bounds.Top;

        var profile = _profileService.ActiveProfile;
        if (profile == null)
        {
            FUIRenderer.DrawText(canvas, "No profile selected",
                new SKPoint(bounds.Left + 20, y + 20), FUIColors.TextDim, 12f);
            FUIRenderer.DrawText(canvas, "Select or create a profile to add mappings",
                new SKPoint(bounds.Left + 20, y + 40), FUIColors.TextDisabled, 11f);
            return;
        }

        var allMappings = new List<(string source, string target, string type, bool enabled)>();

        // Collect all mappings
        foreach (var m in profile.ButtonMappings)
        {
            string source = m.Inputs.Count > 0 ? $"{m.Inputs[0].DeviceName} Btn {m.Inputs[0].Index + 1}" : "Unknown";
            string target = m.Output.Type == OutputType.VJoyButton
                ? $"vJoy {m.Output.VJoyDevice} Btn {m.Output.Index}"
                : $"Key {m.Output.Index}";
            allMappings.Add((source, target, "BUTTON", m.Enabled));
        }

        foreach (var m in profile.AxisMappings)
        {
            string source = m.Inputs.Count > 0 ? $"{m.Inputs[0].DeviceName} Axis {m.Inputs[0].Index}" : "Unknown";
            string target = $"vJoy {m.Output.VJoyDevice} Axis {m.Output.Index}";
            allMappings.Add((source, target, "AXIS", m.Enabled));
        }

        if (allMappings.Count == 0)
        {
            FUIRenderer.DrawText(canvas, "No mappings configured",
                new SKPoint(bounds.Left + 20, y + 20), FUIColors.TextDim, 12f);
            FUIRenderer.DrawText(canvas, "Click '+ ADD MAPPING' to create your first mapping",
                new SKPoint(bounds.Left + 20, y + 40), FUIColors.TextDisabled, 11f);
            return;
        }

        // Draw mapping items
        foreach (var (source, target, type, enabled) in allMappings)
        {
            if (y + itemHeight > bounds.Bottom) break;

            var itemBounds = new SKRect(bounds.Left, y, bounds.Right, y + itemHeight);
            DrawMappingItem(canvas, itemBounds, source, target, type, enabled);
            y += itemHeight + itemGap;
        }
    }

    private void DrawMappingItem(SKCanvas canvas, SKRect bounds, string source, string target, string type, bool enabled)
    {
        // Background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = enabled ? FUIColors.Background2.WithAlpha(100) : FUIColors.Background1.WithAlpha(80)
        };
        canvas.DrawRect(bounds, bgPaint);

        // Frame
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = enabled ? FUIColors.Frame : FUIColors.FrameDim,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        // Type badge
        var typeColor = type == "BUTTON" ? FUIColors.Active : FUIColors.Primary;
        FUIRenderer.DrawText(canvas, type, new SKPoint(bounds.Left + 10, bounds.Top + 18),
            enabled ? typeColor : typeColor.WithAlpha(100), 10f);

        // Source
        FUIRenderer.DrawText(canvas, source, new SKPoint(bounds.Left + 80, bounds.Top + 18),
            enabled ? FUIColors.TextPrimary : FUIColors.TextDim, 12f);

        // Arrow
        FUIRenderer.DrawText(canvas, "->", new SKPoint(bounds.Left + 80, bounds.Top + 36),
            FUIColors.TextDim, 11f);

        // Target
        FUIRenderer.DrawText(canvas, target, new SKPoint(bounds.Left + 110, bounds.Top + 36),
            enabled ? FUIColors.TextPrimary : FUIColors.TextDim, 12f);

        // Status indicator
        var statusColor = enabled ? FUIColors.Success : FUIColors.TextDisabled;
        FUIRenderer.DrawGlowingDot(canvas, new SKPoint(bounds.Right - 20, bounds.MidY),
            statusColor, 4f, enabled ? 6f : 2f);
    }

    private void OpenAddMappingDialog()
    {
        // Ensure we have an active profile
        if (!_profileService.HasActiveProfile)
        {
            CreateNewProfilePrompt();
            if (!_profileService.HasActiveProfile) return;
        }

        using var dialog = new MappingDialog(_inputService, _vjoyService);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result.Success)
        {
            var result = dialog.Result;

            // Create the mapping based on detected input type
            if (result.Input!.Type == InputType.Button)
            {
                var mapping = new ButtonMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    Mode = result.ButtonMode
                };
                _profileService.ActiveProfile!.ButtonMappings.Add(mapping);
            }
            else if (result.Input.Type == InputType.Axis)
            {
                var mapping = new AxisMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    Curve = result.AxisCurve ?? new AxisCurve()
                };
                _profileService.ActiveProfile!.AxisMappings.Add(mapping);
            }
            else if (result.Input.Type == InputType.Hat)
            {
                var mapping = new HatMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    UseContinuous = true // Default to continuous POV
                };
                _profileService.ActiveProfile!.HatMappings.Add(mapping);
            }

            // Save the profile
            _profileService.SaveActiveProfile();
        }
    }

    private void OpenMappingDialogForControl(string controlId)
    {
        // Need device map, selected device, and control info
        if (_deviceMap == null || _selectedDevice < 0 || _selectedDevice >= _devices.Count)
            return;

        // Find the control definition in the device map
        if (!_deviceMap.Controls.TryGetValue(controlId, out var control))
            return;

        // Get the binding from the control (e.g., "button0", "x", "hat0")
        if (control.Bindings == null || control.Bindings.Count == 0)
            return;

        var device = _devices[_selectedDevice];
        var binding = control.Bindings[0];

        // Parse the binding to determine input type and index
        var (inputType, inputIndex) = ParseBinding(binding, control.Type);
        if (inputType == null)
            return;

        // Ensure we have an active profile
        if (!_profileService.HasActiveProfile)
        {
            CreateNewProfilePrompt();
            if (!_profileService.HasActiveProfile) return;
        }

        // Create a pre-selected DetectedInput
        var preSelectedInput = new DetectedInput
        {
            DeviceGuid = device.InstanceGuid,
            DeviceName = device.Name,
            Type = inputType.Value,
            Index = inputIndex,
            Value = 0
        };

        // Open dialog with pre-selected input (skips "wait for input" phase)
        using var dialog = new MappingDialog(_inputService, _vjoyService, preSelectedInput);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result.Success)
        {
            var result = dialog.Result;

            // Create the mapping based on detected input type
            if (result.Input!.Type == InputType.Button)
            {
                var mapping = new ButtonMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    Mode = result.ButtonMode
                };
                _profileService.ActiveProfile!.ButtonMappings.Add(mapping);
            }
            else if (result.Input.Type == InputType.Axis)
            {
                var mapping = new AxisMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    Curve = result.AxisCurve ?? new AxisCurve()
                };
                _profileService.ActiveProfile!.AxisMappings.Add(mapping);
            }
            else if (result.Input.Type == InputType.Hat)
            {
                var mapping = new HatMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    UseContinuous = true
                };
                _profileService.ActiveProfile!.HatMappings.Add(mapping);
            }

            // Save the profile
            _profileService.SaveActiveProfile();
        }
    }

    private (InputType? type, int index) ParseBinding(string binding, string controlType)
    {
        // Handle button bindings: "button0", "button1", etc.
        if (binding.StartsWith("button", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(binding.Substring(6), out int buttonIndex))
                return (InputType.Button, buttonIndex);
        }

        // Handle axis bindings: "x", "y", "z", "rx", "ry", "rz", "slider0", "slider1"
        var axisMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "x", 0 }, { "y", 1 }, { "z", 2 },
            { "rx", 3 }, { "ry", 4 }, { "rz", 5 },
            { "slider0", 6 }, { "slider1", 7 }
        };
        if (axisMap.TryGetValue(binding, out int axisIndex))
            return (InputType.Axis, axisIndex);

        // Handle hat bindings: "hat0", "hat1", etc.
        if (binding.StartsWith("hat", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(binding.Substring(3), out int hatIndex))
                return (InputType.Hat, hatIndex);
        }

        // Fall back to control type if binding doesn't parse
        return controlType.ToUpperInvariant() switch
        {
            "BUTTON" => (InputType.Button, 0),
            "AXIS" => (InputType.Axis, 0),
            "HAT" or "POV" => (InputType.Hat, 0),
            _ => (null, 0)
        };
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
        bool canExport = _profileService.ActiveProfile != null;
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

    private void DrawDeviceListPanel(SKCanvas canvas, SKRect bounds)
    {
        float pad = FUIRenderer.PanelPadding;
        float itemGap = FUIRenderer.ItemSpacing;
        float frameInset = 5f;

        // Vertical side tabs width
        float sideTabWidth = 28f;

        // Panel shadow
        FUIRenderer.DrawPanelShadow(canvas, bounds, 3f, 3f, 10f);

        // Panel background (shifted right to make room for side tabs)
        var contentBounds = new SKRect(bounds.Left + frameInset + sideTabWidth, bounds.Top + frameInset,
                                        bounds.Right - frameInset, bounds.Bottom - frameInset);
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(140),
            IsAntialias = true
        };
        canvas.DrawRect(contentBounds, bgPaint);

        // Draw vertical side tabs (D1 Physical, D2 Virtual)
        DrawDeviceCategorySideTabs(canvas, bounds.Left + frameInset, bounds.Top + frameInset,
            sideTabWidth, bounds.Height - frameInset * 2);

        // L-corner frame (adjusted for side tabs)
        bool panelHovered = _hoveredDevice >= 0;
        var frameBounds = new SKRect(bounds.Left + sideTabWidth, bounds.Top, bounds.Right, bounds.Bottom);
        FUIRenderer.DrawLCornerFrame(canvas, frameBounds,
            panelHovered ? FUIColors.FrameBright : FUIColors.Frame, 40f, 10f, 1.5f, panelHovered);

        // Header - show D1 or D2 based on selected category
        float titleBarHeight = 32f;
        var titleBounds = new SKRect(contentBounds.Left, contentBounds.Top, contentBounds.Right, contentBounds.Top + titleBarHeight);
        string categoryCode = _deviceCategory == 0 ? "D1" : "D2";
        //string categoryName = _deviceCategory == 0 ? "PHYSICAL DEVICES" : "VIRTUAL DEVICES";
        string categoryName = _deviceCategory == 0 ? "DEVICES" : "DEVICES";
        FUIRenderer.DrawPanelTitle(canvas, titleBounds, categoryCode, categoryName);

        // Filter devices by category
        var filteredDevices = _deviceCategory == 0
            ? _devices.Where(d => !d.IsVirtual).ToList()
            : _devices.Where(d => d.IsVirtual).ToList();

        // Device list
        float itemY = contentBounds.Top + titleBarHeight + pad;
        float itemHeight = 60f;

        if (filteredDevices.Count == 0)
        {
            string emptyMsg = _deviceCategory == 0
                ? "No physical devices detected"
                : "No virtual devices detected";
            string helpMsg = _deviceCategory == 0
                ? "Connect a joystick or gamepad"
                : "Install vJoy or start a virtual device";
            FUIRenderer.DrawText(canvas, emptyMsg,
                new SKPoint(contentBounds.Left + pad, itemY + 20), FUIColors.TextDim, 12f);
            FUIRenderer.DrawText(canvas, helpMsg,
                new SKPoint(contentBounds.Left + pad, itemY + 38), FUIColors.TextDisabled, 10f);
        }
        else
        {
            for (int i = 0; i < filteredDevices.Count && itemY + itemHeight < contentBounds.Bottom - 40; i++)
            {
                // Find the actual device index in _devices
                int actualIndex = _devices.IndexOf(filteredDevices[i]);
                string status = filteredDevices[i].IsConnected ? "ONLINE" : "DISCONNECTED";
                DrawDeviceListItem(canvas, contentBounds.Left + pad - 10, itemY, contentBounds.Width - pad,
                    filteredDevices[i].Name, status, actualIndex == _selectedDevice, actualIndex == _hoveredDevice);
                itemY += itemHeight + itemGap;
            }
        }

        // "Scan for devices" prompt
        float promptY = bounds.Bottom - pad - 20;
        FUIRenderer.DrawText(canvas, "+ SCAN FOR DEVICES",
            new SKPoint(contentBounds.Left + pad, promptY), FUIColors.TextDim, 12f);

        using var bracketPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.FrameDim,
            StrokeWidth = 1f
        };
        canvas.DrawLine(contentBounds.Left + pad - 20, promptY - 10, contentBounds.Left + pad - 20, promptY + 5, bracketPaint);
        canvas.DrawLine(contentBounds.Left + pad - 20, promptY - 10, contentBounds.Left + pad - 8, promptY - 10, bracketPaint);
    }

    private void DrawDeviceCategorySideTabs(SKCanvas canvas, float x, float y, float width, float height)
    {
        // Style based on reference: narrow vertical tabs with text reading bottom-to-top
        // Accent bar on right side for selected tab, 4px space between tabs
        float tabHeight = 80f;
        float tabGap = 4f; // 4px space between tabs

        // Calculate total tabs height and start from bottom of available space
        float totalTabsHeight = tabHeight * 2 + tabGap;
        float startY = y + height - totalTabsHeight - 10f; // Align tabs near bottom

        // D1 Physical Devices tab (bottom)
        var d1Bounds = new SKRect(x, startY + tabHeight + tabGap, x + width, startY + tabHeight * 2 + tabGap);
        _deviceCategoryD1Bounds = d1Bounds;
        DrawVerticalSideTab(canvas, d1Bounds, "DEVICES_01", _deviceCategory == 0, _hoveredDeviceCategory == 0);

        // D2 Virtual Devices tab (above D1)
        var d2Bounds = new SKRect(x, startY, x + width, startY + tabHeight);
        _deviceCategoryD2Bounds = d2Bounds;
        DrawVerticalSideTab(canvas, d2Bounds, "DEVICES_02", _deviceCategory == 1, _hoveredDeviceCategory == 1);
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

    private void DrawDeviceListItem(SKCanvas canvas, float x, float y, float width,
        string name, string status, bool isSelected, bool isHovered)
    {
        var itemBounds = new SKRect(x, y, x + width, y + 60);
        bool isDisconnected = status == "DISCONNECTED";

        // Selection/hover background
        if (isSelected || isHovered)
        {
            var bgColor = isSelected
                ? (isDisconnected ? FUIColors.Danger.WithAlpha(20) : FUIColors.Active.WithAlpha(30))
                : FUIColors.Primary.WithAlpha(15);
            FUIRenderer.FillFrame(canvas, itemBounds, bgColor, 6f);
        }

        // Item frame
        var frameColor = isSelected
            ? (isDisconnected ? FUIColors.Danger : FUIColors.Active)
            : (isHovered ? FUIColors.FrameBright : FUIColors.FrameDim);
        FUIRenderer.DrawFrame(canvas, itemBounds, frameColor, 6f, isSelected ? 1.5f : 1f, isSelected);

        // Status indicator dot
        var statusColor = isDisconnected ? FUIColors.Danger : FUIColors.Active;
        FUIRenderer.DrawGlowingDot(canvas, new SKPoint(x + 18, y + 22), statusColor, 4f,
            isDisconnected ? 4f : 8f);

        // Device name (truncate if needed) - dim for disconnected
        string displayName = name.Length > 28 ? name.Substring(0, 25) + "..." : name;
        var nameColor = isDisconnected
            ? FUIColors.TextDim
            : (isSelected ? FUIColors.TextBright : FUIColors.TextPrimary);
        FUIRenderer.DrawText(canvas, displayName, new SKPoint(x + 35, y + 26), nameColor, 13f, isSelected && !isDisconnected);

        // Status text
        var statusTextColor = isDisconnected ? FUIColors.Danger : FUIColors.Active;
        FUIRenderer.DrawText(canvas, status, new SKPoint(x + 35, y + 45), statusTextColor, 11f);

        // vJoy assignment indicator (positioned to avoid chevron overlap)
        if (!isDisconnected)
        {
            FUIRenderer.DrawText(canvas, "VJOY:1", new SKPoint(x + width - 85, y + 45),
                FUIColors.TextDim, 11f);
        }

        // Selection chevron
        if (isSelected)
        {
            using var chevronPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = isDisconnected ? FUIColors.Danger : FUIColors.Active,
                StrokeWidth = 2f,
                IsAntialias = true
            };
            canvas.DrawLine(x + width - 18, y + 25, x + width - 10, y + 30, chevronPaint);
            canvas.DrawLine(x + width - 10, y + 30, x + width - 18, y + 35, chevronPaint);
        }
    }

    private void DrawDeviceDetailsPanel(SKCanvas canvas, SKRect bounds)
    {
        float pad = FUIRenderer.PanelPadding;
        float frameInset = 5f;

        // Panel background (matching mappings view)
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(100),
            IsAntialias = true
        };
        canvas.DrawRect(new SKRect(bounds.Left + frameInset, bounds.Top + frameInset,
            bounds.Right - frameInset, bounds.Bottom - frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame.WithAlpha(150), 30f, 8f);

        if (_devices.Count == 0 || _selectedDevice < 0 || _selectedDevice >= _devices.Count)
        {
            FUIRenderer.DrawText(canvas, "Select a device to view details",
                new SKPoint(bounds.Left + pad, bounds.Top + 50), FUIColors.TextDim, 14f);
            return;
        }

        var device = _devices[_selectedDevice];

        // Draw the device silhouette centered in panel (matching mappings view style)
        float centerX = bounds.MidX;
        float centerY = bounds.MidY;

        if (_joystickSvg?.Picture != null)
        {
            // Limit size to 900px max and apply same rendering as mappings tab
            float maxSize = 900f;
            float maxWidth = Math.Min(bounds.Width - 40, maxSize);
            float maxHeight = Math.Min(bounds.Height - 40, maxSize);

            // Create constrained bounds centered in the panel
            float constrainedWidth = Math.Min(maxWidth, maxHeight); // Keep square-ish
            float constrainedHeight = constrainedWidth;
            _silhouetteBounds = new SKRect(
                centerX - constrainedWidth / 2,
                centerY - constrainedHeight / 2,
                centerX + constrainedWidth / 2,
                centerY + constrainedHeight / 2
            );

            // Draw the silhouette using shared method
            DrawDeviceSilhouette(canvas, _silhouetteBounds);
        }
        else
        {
            // Placeholder when no SVG
            _silhouetteBounds = SKRect.Empty;
            FUIRenderer.DrawTextCentered(canvas, "Device Preview",
                new SKRect(bounds.Left, centerY - 20, bounds.Right, centerY + 20),
                FUIColors.TextDim, 14f);
        }

        // Draw dynamic lead-lines for active inputs
        DrawActiveInputLeadLines(canvas, bounds);
    }

    private void DrawActiveInputLeadLines(SKCanvas canvas, SKRect panelBounds)
    {
        if (_deviceMap == null || _joystickSvg?.Picture == null) return;

        var visibleInputs = _activeInputTracker.GetVisibleInputs();
        int inputIndex = 0;

        foreach (var input in visibleInputs)
        {
            var control = input.Control;
            if (control?.Anchor == null) continue; // Must have JSON anchor

            float opacity = input.GetOpacity(_activeInputTracker.FadeDelay, _activeInputTracker.FadeDuration);
            if (opacity < 0.01f) continue;

            // Use the JSON anchor point (in viewBox coordinates 0-2048)
            // Convert to screen coordinates using the stored SVG transform
            SKPoint anchorScreen = ViewBoxToScreen(control.Anchor.X, control.Anchor.Y);

            // Label position: use JSON labelOffset if specified, otherwise auto-stack
            float labelX, labelY;
            bool goesRight = true;

            if (control.LabelOffset != null)
            {
                // labelOffset is relative to anchor, in viewBox units
                float labelVbX = control.Anchor.X + control.LabelOffset.X;
                float labelVbY = control.Anchor.Y + control.LabelOffset.Y;
                var labelScreen = ViewBoxToScreen(labelVbX, labelVbY);
                labelX = labelScreen.X;
                labelY = labelScreen.Y;
                goesRight = control.LabelOffset.X >= 0;
            }
            else
            {
                // Fallback: auto-stack labels to the right of silhouette
                labelY = panelBounds.Top + 80 + (inputIndex * 55);
                if (labelY > panelBounds.Bottom - 60)
                {
                    labelY = panelBounds.Top + 80 + ((inputIndex % 8) * 55);
                }
                labelX = _silhouetteBounds.Right + 20;
            }

            // Draw the lead-line with fade - from anchor on joystick to label
            DrawInputLeadLine(canvas, anchorScreen, new SKPoint(labelX, labelY), goesRight, opacity, input);

            inputIndex++;
        }
    }

    /// <summary>
    /// Convert viewBox coordinates (0-2048) to screen coordinates.
    /// The SVG viewBox is 0 0 2048 2048, and we render it scaled/translated.
    /// Handles mirroring: when SVG is mirrored, viewBox X is inverted.
    /// </summary>
    private SKPoint ViewBoxToScreen(float viewBoxX, float viewBoxY)
    {
        if (_joystickSvg?.Picture == null)
            return new SKPoint(viewBoxX, viewBoxY);

        float screenX, screenY;

        if (_svgMirrored)
        {
            // When mirrored, the SVG is drawn with Scale(-scale, scale) after
            // Translate(scaledWidth, 0). This means viewBox X is inverted:
            // viewBox 0 -> screen offsetX + scaledWidth
            // viewBox 2048 -> screen offsetX
            var svgBounds = _joystickSvg.Picture.CullRect;
            float scaledWidth = svgBounds.Width * _svgScale;
            screenX = _svgOffset.X + scaledWidth - viewBoxX * _svgScale;
        }
        else
        {
            screenX = _svgOffset.X + viewBoxX * _svgScale;
        }

        screenY = _svgOffset.Y + viewBoxY * _svgScale;
        return new SKPoint(screenX, screenY);
    }

    private void DrawInputLeadLine(SKCanvas canvas, SKPoint anchor, SKPoint labelPos, bool goesRight,
        float opacity, ActiveInputState input)
    {
        byte alpha = (byte)(255 * opacity * input.AppearProgress);
        var lineColor = FUIColors.Active.WithAlpha(alpha);

        // Animation: line grows from anchor to label
        float progress = Math.Min(1f, input.AppearProgress * 1.5f);

        // Build the path points based on LeadLine definition or use default
        // Pass labelPos so the line ends at the label
        var pathPoints = BuildLeadLinePath(anchor, labelPos, input.Control?.LeadLine, goesRight);

        // Draw line with animation
        using var linePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = lineColor,
            StrokeWidth = 1.5f,
            IsAntialias = true
        };

        // Calculate total path length for animation
        float totalLength = 0f;
        for (int i = 1; i < pathPoints.Count; i++)
        {
            totalLength += Distance(pathPoints[i - 1], pathPoints[i]);
        }

        // Draw path up to current animation progress
        float targetLength = totalLength * progress;
        var path = new SKPath();
        path.MoveTo(pathPoints[0]);

        float drawnLength = 0f;
        for (int i = 1; i < pathPoints.Count && drawnLength < targetLength; i++)
        {
            float segmentLength = Distance(pathPoints[i - 1], pathPoints[i]);
            float remainingLength = targetLength - drawnLength;

            if (remainingLength >= segmentLength)
            {
                // Draw full segment
                path.LineTo(pathPoints[i]);
                drawnLength += segmentLength;
            }
            else
            {
                // Draw partial segment
                float t = remainingLength / segmentLength;
                var partialEnd = new SKPoint(
                    pathPoints[i - 1].X + (pathPoints[i].X - pathPoints[i - 1].X) * t,
                    pathPoints[i - 1].Y + (pathPoints[i].Y - pathPoints[i - 1].Y) * t);
                path.LineTo(partialEnd);
                drawnLength += remainingLength;
            }
        }
        canvas.DrawPath(path, linePaint);

        // Draw endpoint dot at anchor
        if (progress > 0.8f)
        {
            using var dotPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = lineColor,
                IsAntialias = true
            };
            canvas.DrawCircle(anchor, 4f, dotPaint);
        }

        // Draw label at the specified label position when line is complete
        if (progress > 0.95f)
        {
            DrawInputLabel(canvas, labelPos, goesRight, input, alpha);
        }
    }

    /// <summary>
    /// Build the lead-line path points from anchor to label position.
    /// Uses LeadLine definition for intermediate segments, always ends at labelPos.
    /// Returns a list of points: anchor -> shelf end -> segment ends... -> label position
    /// </summary>
    private List<SKPoint> BuildLeadLinePath(SKPoint anchor, SKPoint labelPos, LeadLineDefinition? leadLine, bool defaultGoesRight)
    {
        var points = new List<SKPoint> { anchor };

        if (leadLine == null)
        {
            // Default: simple path from anchor to label
            // Add a midpoint to create a nice angled line
            bool goesRight = defaultGoesRight;
            float midX = goesRight ? anchor.X + 40 : anchor.X - 40;
            points.Add(new SKPoint(midX, anchor.Y)); // Horizontal shelf
            points.Add(labelPos); // End at label
            return points;
        }

        // Use JSON-defined lead-line shape
        bool shelfGoesRight = leadLine.ShelfSide.Equals("right", StringComparison.OrdinalIgnoreCase);
        float scaledShelfLength = leadLine.ShelfLength * _svgScale;

        // Shelf (horizontal)
        float shelfEndX = shelfGoesRight ? anchor.X + scaledShelfLength : anchor.X - scaledShelfLength;
        var shelfEndPoint = new SKPoint(shelfEndX, anchor.Y);
        points.Add(shelfEndPoint);

        // Process intermediate segments (if any)
        if (leadLine.Segments != null && leadLine.Segments.Count > 0)
        {
            var currentPoint = shelfEndPoint;
            int shelfDirection = shelfGoesRight ? 1 : -1;

            // Process all but the last segment normally
            for (int i = 0; i < leadLine.Segments.Count - 1; i++)
            {
                var segment = leadLine.Segments[i];
                float scaledLength = segment.Length * _svgScale;
                float angleRad = segment.Angle * MathF.PI / 180f;

                float dx = MathF.Cos(angleRad) * scaledLength * shelfDirection;
                float dy = -MathF.Sin(angleRad) * scaledLength;

                var segmentEnd = new SKPoint(currentPoint.X + dx, currentPoint.Y + dy);
                points.Add(segmentEnd);
                currentPoint = segmentEnd;
            }
        }

        // Always end at the label position
        points.Add(labelPos);

        return points;
    }

    private static float Distance(SKPoint a, SKPoint b)
    {
        float dx = b.X - a.X;
        float dy = b.Y - a.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private void DrawInputLabel(SKCanvas canvas, SKPoint pos, bool goesRight, ActiveInputState input, byte alpha)
    {
        var control = input.Control;
        string label = control?.Label ?? input.Binding.ToUpper();

        var textColor = FUIColors.TextBright.WithAlpha(alpha);
        var dimColor = FUIColors.TextDim.WithAlpha(alpha);
        var activeColor = FUIColors.Active.WithAlpha(alpha);

        float labelWidth = 140f;
        float labelHeight = input.IsAxis ? 32f : 22f;
        float x = goesRight ? pos.X : pos.X - labelWidth;
        float y = pos.Y - labelHeight / 2;

        // Background frame
        var frameBounds = new SKRect(x, y, x + labelWidth, y + labelHeight);
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha((byte)(160 * alpha / 255)),
            IsAntialias = true
        };
        canvas.DrawRect(frameBounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = activeColor,
            StrokeWidth = 1f,
            IsAntialias = true
        };
        canvas.DrawRect(frameBounds, framePaint);

        // Label text
        FUIRenderer.DrawText(canvas, label, new SKPoint(x + 5, y + 14), textColor, 11f);

        // Value indicator
        if (input.IsAxis)
        {
            // Data bar for axes
            float barWidth = labelWidth - 10;
            float barHeight = 10f;
            float value = (input.Value + 1f) / 2f; // Normalize -1..1 to 0..1
            var barBounds = new SKRect(x + 5, y + 18, x + 5 + barWidth, y + 18 + barHeight);
            FUIRenderer.DrawDataBar(canvas, barBounds, value, activeColor, FUIColors.Frame.WithAlpha(alpha));
        }
        else
        {
            // Button indicator: show PRESSED or binding
            string valueText = input.Value > 0.5f ? "PRESSED" : input.Binding.ToUpper();
            var valueColor = input.Value > 0.5f ? activeColor : dimColor;
            FUIRenderer.DrawText(canvas, valueText, new SKPoint(x + labelWidth - 60, y + 14), valueColor, 9f);
        }
    }

    private SKPoint SvgToScreen(float svgX, float svgY, SKRect svgBounds)
    {
        // Convert SVG coordinates to screen coordinates
        // Account for the fact that the SVG Picture.CullRect may not start at (0,0)
        // The svgBounds is the CullRect (actual content bounds), not viewBox bounds
        // SVG coordinates are in viewBox space, but rendering uses CullRect
        float relativeX = svgX - svgBounds.Left;
        float relativeY = svgY - svgBounds.Top;
        float screenX = _svgOffset.X + relativeX * _svgScale;
        float screenY = _svgOffset.Y + relativeY * _svgScale;
        return new SKPoint(screenX, screenY);
    }

    private void DrawDeviceSilhouette(SKCanvas canvas, SKRect bounds)
    {
        // Draw the actual SVG if loaded, otherwise fallback to simple outline
        var activeSvg = GetActiveSvg();
        if (activeSvg?.Picture != null)
        {
            bool mirror = _deviceMap?.Mirror ?? false;
            DrawSvgInBounds(canvas, activeSvg, bounds, mirror);
        }
        else
        {
            DrawJoystickOutlineFallback(canvas, bounds);
        }
    }

    private void DrawSvgInBounds(SKCanvas canvas, SKSvg svg, SKRect bounds, bool mirror = false)
    {
        if (svg.Picture == null) return;

        var svgBounds = svg.Picture.CullRect;
        if (svgBounds.Width <= 0 || svgBounds.Height <= 0) return;

        // Calculate scale to fit SVG within bounds while maintaining aspect ratio
        float scaleX = bounds.Width / svgBounds.Width;
        float scaleY = bounds.Height / svgBounds.Height;
        float scale = Math.Min(scaleX, scaleY) * 0.95f; // 95% - make it larger!

        float scaledWidth = svgBounds.Width * scale;
        float scaledHeight = svgBounds.Height * scale;

        // Center the SVG within bounds
        // Account for CullRect origin - we need to translate so content starts at the center position
        float offsetX = bounds.Left + (bounds.Width - scaledWidth) / 2 - svgBounds.Left * scale;
        float offsetY = bounds.Top + (bounds.Height - scaledHeight) / 2 - svgBounds.Top * scale;

        // Store transform info for hit testing and coordinate conversion
        _svgScale = scale;
        _svgOffset = new SKPoint(offsetX, offsetY);
        _svgMirrored = mirror;

        canvas.Save();
        canvas.Translate(offsetX, offsetY);

        if (mirror)
        {
            // Flip horizontally: translate to right edge, then scale X by -1
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

    private void DrawJoystickOutlineFallback(SKCanvas canvas, SKRect bounds)
    {
        // Fallback simple outline when SVG not loaded
        using var outlinePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Primary.WithAlpha(60),
            StrokeWidth = 1.5f,
            IsAntialias = true
        };

        float centerX = bounds.MidX;
        float stickWidth = 36f;
        float baseWidth = 70f;

        // Stick shaft
        canvas.DrawLine(centerX, bounds.Top + 35, centerX, bounds.Bottom - 55, outlinePaint);

        // Stick top (grip area)
        var gripRect = new SKRect(centerX - stickWidth / 2, bounds.Top + 25,
                                   centerX + stickWidth / 2, bounds.Top + 85);
        canvas.DrawRoundRect(gripRect, 8, 8, outlinePaint);

        // Base
        var baseRect = new SKRect(centerX - baseWidth / 2, bounds.Bottom - 65,
                                   centerX + baseWidth / 2, bounds.Bottom - 30);
        canvas.DrawRoundRect(baseRect, 4, 4, outlinePaint);

        // Hat indicator (small circle on top)
        canvas.DrawCircle(centerX, bounds.Top + 45, 7, outlinePaint);

        // Trigger area (small rect on front)
        var triggerRect = new SKRect(centerX + stickWidth / 2 - 4, bounds.Top + 65,
                                      centerX + stickWidth / 2 + 12, bounds.Top + 82);
        canvas.DrawRect(triggerRect, outlinePaint);

        // Button cluster on side
        canvas.DrawCircle(centerX - stickWidth / 2 - 8, bounds.Top + 55, 5, outlinePaint);
        canvas.DrawCircle(centerX - stickWidth / 2 - 8, bounds.Top + 70, 5, outlinePaint);
    }

    private void DrawDeviceActionsPanel(SKCanvas canvas, SKRect bounds)
    {
        float pad = FUIRenderer.PanelPadding;
        float frameInset = 5f;

        // Panel shadow
        FUIRenderer.DrawPanelShadow(canvas, bounds, 3f, 3f, 10f);

        // Panel background
        var contentBounds = new SKRect(bounds.Left + frameInset, bounds.Top + frameInset,
                                        bounds.Right - frameInset, bounds.Bottom - frameInset);
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(140),
            IsAntialias = true
        };
        canvas.DrawRect(contentBounds, bgPaint);

        // L-corner frame
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame, 35f, 10f);

        // Header
        float titleBarHeight = 32f;
        var titleBounds = new SKRect(contentBounds.Left, contentBounds.Top, contentBounds.Right, contentBounds.Top + titleBarHeight);
        FUIRenderer.DrawPanelTitle(canvas, titleBounds, "D3", "DEVICE ACTIONS");

        float y = contentBounds.Top + titleBarHeight + pad;
        float buttonHeight = 32f;
        float buttonGap = 8f;

        // Check if we have a physical device selected
        bool hasPhysicalDevice = _deviceCategory == 0 && _selectedDevice >= 0 &&
                                  _selectedDevice < _devices.Count && !_devices[_selectedDevice].IsVirtual;

        if (hasPhysicalDevice)
        {
            var device = _devices[_selectedDevice];
            bool isDisconnected = !device.IsConnected;

            // Device info
            FUIRenderer.DrawText(canvas, isDisconnected ? "DISCONNECTED DEVICE" : "SELECTED DEVICE",
                new SKPoint(contentBounds.Left + pad, y), isDisconnected ? FUIColors.Danger : FUIColors.TextDim, 9f);
            y += 16f;

            string shortName = device.Name.Length > 22 ? device.Name.Substring(0, 20) + "..." : device.Name;
            FUIRenderer.DrawText(canvas, shortName, new SKPoint(contentBounds.Left + pad, y),
                isDisconnected ? FUIColors.TextDim : FUIColors.TextPrimary, 11f);
            y += 20f;

            // Device stats
            FUIRenderer.DrawText(canvas, $"{device.AxisCount} axes  {device.ButtonCount} btns  {device.HatCount} hats",
                new SKPoint(contentBounds.Left + pad, y), FUIColors.TextDim, 9f);
            y += 24f;

            float buttonWidth = contentBounds.Width - pad * 2;

            if (isDisconnected)
            {
                // Disconnected device: Show Clear Mappings and Remove Device buttons
                _map1to1ButtonBounds = SKRect.Empty;

                // Clear mappings button
                _clearMappingsButtonBounds = new SKRect(contentBounds.Left + pad, y, contentBounds.Left + pad + buttonWidth, y + buttonHeight);
                DrawDeviceActionButton(canvas, _clearMappingsButtonBounds, "CLEAR MAPPINGS", _clearMappingsButtonHovered, isDestructive: true);
                y += buttonHeight + buttonGap;

                // Remove device button (dangerous - removes all trace)
                _removeDeviceButtonBounds = new SKRect(contentBounds.Left + pad, y, contentBounds.Left + pad + buttonWidth, y + buttonHeight);
                DrawDeviceActionButton(canvas, _removeDeviceButtonBounds, "REMOVE DEVICE", _removeDeviceButtonHovered, isDangerous: true);
            }
            else
            {
                // Connected device: Show Map 1:1 and Clear Mappings buttons
                _removeDeviceButtonBounds = SKRect.Empty;

                // Map 1:1 to vJoy button
                _map1to1ButtonBounds = new SKRect(contentBounds.Left + pad, y, contentBounds.Left + pad + buttonWidth, y + buttonHeight);
                DrawDeviceActionButton(canvas, _map1to1ButtonBounds, "MAP 1:1 TO VJOY", _map1to1ButtonHovered);
                y += buttonHeight + buttonGap;

                // Clear mappings button
                _clearMappingsButtonBounds = new SKRect(contentBounds.Left + pad, y, contentBounds.Left + pad + buttonWidth, y + buttonHeight);
                DrawDeviceActionButton(canvas, _clearMappingsButtonBounds, "CLEAR MAPPINGS", _clearMappingsButtonHovered, isDestructive: true);
            }
        }
        else
        {
            // No device selected
            _map1to1ButtonBounds = SKRect.Empty;
            _clearMappingsButtonBounds = SKRect.Empty;
            _removeDeviceButtonBounds = SKRect.Empty;

            FUIRenderer.DrawText(canvas, "Select a physical device",
                new SKPoint(contentBounds.Left + pad, y + 20), FUIColors.TextDim, 10f);
            FUIRenderer.DrawText(canvas, "to see available actions",
                new SKPoint(contentBounds.Left + pad, y + 36), FUIColors.TextDim, 10f);
        }
    }

    private void DrawDeviceActionButton(SKCanvas canvas, SKRect bounds, string text, bool isHovered,
        bool isDestructive = false, bool isDangerous = false)
    {
        // isDangerous uses Danger (red/orange) color, isDestructive uses Warning (yellow)
        var accentColor = isDangerous ? FUIColors.Danger : (isDestructive ? FUIColors.Warning : FUIColors.Active);

        // Button background
        var bgColor = isHovered
            ? accentColor.WithAlpha(40)
            : (isDangerous ? FUIColors.Danger.WithAlpha(15) : FUIColors.Background2.WithAlpha(80));
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = bgColor,
            IsAntialias = true
        };
        canvas.DrawRect(bounds, bgPaint);

        // Button border
        var borderColor = isHovered ? accentColor : (isDangerous ? FUIColors.Danger.WithAlpha(100) : FUIColors.Frame);
        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = borderColor,
            StrokeWidth = 1f,
            IsAntialias = true
        };
        canvas.DrawRect(bounds, borderPaint);

        // Button text
        var textColor = isHovered ? accentColor : (isDangerous ? FUIColors.Danger : FUIColors.TextPrimary);
        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 10f);
    }

    private void DrawStatusPanel(SKCanvas canvas, SKRect bounds)
    {
        float pad = FUIRenderer.PanelPadding;
        float itemGap = FUIRenderer.SpaceSM;
        float frameInset = 5f;

        // Panel shadow
        FUIRenderer.DrawPanelShadow(canvas, bounds, 3f, 3f, 10f);

        // Panel background
        var contentBounds = new SKRect(bounds.Left + frameInset, bounds.Top + frameInset,
                                        bounds.Right - frameInset, bounds.Bottom - frameInset);
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(140),
            IsAntialias = true
        };
        canvas.DrawRect(contentBounds, bgPaint);

        // L-corner frame
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame, 35f, 10f);

        // Header
        float titleBarHeight = 32f;
        var titleBounds = new SKRect(contentBounds.Left, contentBounds.Top, contentBounds.Right, contentBounds.Top + titleBarHeight);
        FUIRenderer.DrawPanelTitle(canvas, titleBounds, "S1", "STATUS");

        // Status items
        float statusItemHeight = 32f;
        float itemY = contentBounds.Top + titleBarHeight + pad;
        DrawStatusItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2, "VJOY DRIVER", "ACTIVE", FUIColors.Active);
        itemY += statusItemHeight + itemGap;
        DrawStatusItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2, "HIDHIDE", "ENABLED", FUIColors.Active);
        itemY += statusItemHeight + itemGap;
        DrawStatusItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2, "INPUT RATE", "100 HZ", FUIColors.TextPrimary);
        itemY += statusItemHeight + itemGap;
        DrawStatusItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2, "MAPPING", "ACTIVE", FUIColors.Active);
        itemY += statusItemHeight + itemGap;
        DrawStatusItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2, "PROFILE", "DEFAULT", FUIColors.TextPrimary);

        // Active layers
        itemY += statusItemHeight + FUIRenderer.SpaceLG;
        FUIRenderer.DrawText(canvas, "ACTIVE LAYERS",
            new SKPoint(bounds.Left + pad, itemY + 12), FUIColors.TextDim, 11f);
        itemY += FUIRenderer.SpaceLG;
        float layerBtnWidth = 52f;
        float layerBtnGap = FUIRenderer.SpaceSM;
        DrawLayerIndicator(canvas, bounds.Left + pad, itemY, layerBtnWidth, "BASE", true);
        DrawLayerIndicator(canvas, bounds.Left + pad + layerBtnWidth + layerBtnGap, itemY, layerBtnWidth, "SHIFT", false);
        DrawLayerIndicator(canvas, bounds.Left + pad + (layerBtnWidth + layerBtnGap) * 2, itemY, layerBtnWidth, "ALT", false);
    }

    private void DrawStatusItem(SKCanvas canvas, float x, float y, float width, string label, string value, SKColor valueColor)
    {
        FUIRenderer.DrawText(canvas, label, new SKPoint(x, y + 12), FUIColors.TextDim, 11f);

        var dotColor = valueColor == FUIColors.Active ? valueColor : FUIColors.Primary.WithAlpha(100);
        float dotX = x + width - 70;
        FUIRenderer.DrawGlowingDot(canvas, new SKPoint(dotX, y + 8), dotColor, 2f, 4f);

        FUIRenderer.DrawText(canvas, value, new SKPoint(dotX + 10, y + 12), valueColor, 11f);
    }

    private void DrawLayerIndicator(SKCanvas canvas, float x, float y, float width, string name, bool isActive)
    {
        float height = 22f;
        var bounds = new SKRect(x, y, x + width, y + height);
        var frameColor = isActive ? FUIColors.Active : FUIColors.FrameDim;
        var fillColor = isActive ? FUIColors.Active.WithAlpha(40) : SKColors.Transparent;

        FUIRenderer.FillFrame(canvas, bounds, fillColor, 4f);
        FUIRenderer.DrawFrame(canvas, bounds, frameColor, 4f, 1f, isActive);

        var textColor = isActive ? FUIColors.TextBright : FUIColors.TextDim;
        FUIRenderer.DrawTextCentered(canvas, name, bounds, textColor, 10f, isActive);
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

    #region Cleanup

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
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
