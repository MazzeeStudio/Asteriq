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
    private float _scanLineProgress = 0f;
    private float _dashPhase = 0f;
    private float _pulsePhase = 0f;
    private float _leadLineProgress = 0f;
    private int _hoveredDevice = -1;
    private int _selectedDevice = 0;
    private List<PhysicalDeviceInfo> _devices = new();
    private DeviceInputState? _currentInputState;

    // Device category tabs (D1 = Physical, D2 = Virtual)
    private int _deviceCategory = 0;  // 0 = Physical, 1 = Virtual
    private int _hoveredDeviceCategory = -1;
    private SKRect _deviceCategoryD1Bounds;
    private SKRect _deviceCategoryD2Bounds;

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

        // Throttle keywords - MongooseT, 50CM, TWCS, etc.
        if (name.Contains("THROTTLE") || name.Contains("50CM") || name.Contains("TM50") ||
            name.Contains("TWCS") || name.Contains("MONGOOSE"))
            return "Throttle";

        // Pedals keywords
        if (name.Contains("PEDAL") || name.Contains("RUDDER") || name.Contains("TPR") ||
            name.Contains("MFG") || name.Contains("CROSSWIND"))
            return "Pedals";

        // Default to joystick
        return "Joystick";
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
        _canvas.MouseLeave += OnCanvasMouseLeave;
        Controls.Add(_canvas);
    }

    private void InitializeInput()
    {
        if (!_inputService.Initialize())
        {
            return;
        }

        _inputService.InputReceived += OnInputReceived;
        _inputService.DeviceDisconnected += OnDeviceDisconnected;
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

        _canvas.Invalidate();
    }

    private void RefreshDevices()
    {
        _devices = _inputService.EnumerateDevices();
        if (_devices.Count > 0 && _selectedDevice < 0)
        {
            _selectedDevice = 0;
            // Load device map for the first device
            LoadDeviceMapForDevice(_devices[0].Name);
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

    private void OnDeviceDisconnected(object? sender, int deviceIndex)
    {
        BeginInvoke(() =>
        {
            RefreshDevices();
            if (_selectedDevice >= _devices.Count)
            {
                _selectedDevice = Math.Max(0, _devices.Count - 1);
                // Load device map for the new selected device
                if (_devices.Count > 0)
                    LoadDeviceMapForDevice(_devices[_selectedDevice].Name);
            }
        });
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

            // Right panel: Button mode buttons
            if (_selectedMappingRow >= 8)
            {
                for (int i = 0; i < _buttonModeBounds.Length; i++)
                {
                    if (_buttonModeBounds[i].Contains(e.X, e.Y))
                    {
                        _hoveredButtonMode = i;
                        Cursor = Cursors.Hand;
                        return;
                    }
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

        // Device list hover detection
        float pad = FUIRenderer.SpaceLG;
        float contentTop = 90;
        float leftPanelWidth = 300f;
        float sideTabWidth = 28f;

        if (e.X >= pad + sideTabWidth && e.X <= pad + leftPanelWidth)
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

        // Window controls hover
        float windowControlsX = ClientSize.Width - pad - 88;
        float titleBarY = 15;
        if (e.Y >= titleBarY + 10 && e.Y <= titleBarY + 34)
        {
            float relX = e.X - windowControlsX;
            float btnSize = 24f;
            float btnGap = 8f;

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
            _selectedDevice = -1; // Reset selection when switching categories
            _currentInputState = null;
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

        // Tab clicks
        float tabStartX = ClientSize.Width - 540;
        float tabY = 15;
        float tabSpacing = 100;
        if (e.Y >= tabY + 20 && e.Y <= tabY + 50)
        {
            for (int i = 0; i < _tabNames.Length; i++)
            {
                float tabX = tabStartX + i * tabSpacing;
                if (e.X >= tabX && e.X < tabX + 70)
                {
                    _activeTab = i;
                    break;
                }
            }
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

            // Right panel: Button mode selection
            if (_selectedMappingRow >= 8 && _hoveredButtonMode >= 0)
            {
                _selectedButtonMode = (ButtonMode)_hoveredButtonMode;
                UpdateButtonModeForSelected();
                return;
            }

            // Left panel: vJoy device navigation
            if (_vjoyPrevHovered && _selectedVJoyDeviceIndex > 0)
            {
                _selectedVJoyDeviceIndex--;
                _selectedMappingRow = -1;
                CancelInputListening();
                return;
            }
            if (_vjoyNextHovered && _selectedVJoyDeviceIndex < _vjoyDevices.Count - 1)
            {
                _selectedVJoyDeviceIndex++;
                _selectedMappingRow = -1;
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
                }
                _selectedMappingRow = _hoveredMappingRow;
                return;
            }
        }

        // SVG control clicks
        if (_hoveredControlId != null)
        {
            _selectedControlId = _hoveredControlId;
            _leadLineProgress = 0f; // Reset animation for new selection
        }
        else if (_silhouetteBounds.Contains(e.X, e.Y))
        {
            // Clicked inside silhouette but not on a control - deselect
            _selectedControlId = null;
        }
    }

    private void OnCanvasMouseLeave(object? sender, EventArgs e)
    {
        _hoveredDevice = -1;
        _hoveredWindowControl = -1;
        _hoveredControlId = null;
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
        // Subtle dot grid across entire surface
        FUIRenderer.DrawDotGrid(canvas, bounds, 30f, FUIColors.Grid.WithAlpha(40));

        // Slightly brighter grid in main work area
        var workArea = new SKRect(20, 80, bounds.Right - 20, bounds.Bottom - 50);
        FUIRenderer.DrawLineGrid(canvas, workArea, 60f, FUIColors.Grid.WithAlpha(25));
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
        float leftPanelWidth = 300f;
        float rightPanelWidth = 230f;
        float centerStart = pad + leftPanelWidth + gap;
        float centerEnd = bounds.Right - pad - rightPanelWidth - gap;

        // Content based on active tab
        if (_activeTab == 1) // MAPPINGS tab
        {
            DrawMappingsTabContent(canvas, bounds, pad, contentTop, contentBottom);
        }
        else
        {
            // Default: Device tab layout
            // Left panel: Device List
            var deviceListBounds = new SKRect(pad, contentTop, pad + leftPanelWidth, contentBottom);
            DrawDeviceListPanel(canvas, deviceListBounds);

            // Center panel: Device Details
            var detailsBounds = new SKRect(centerStart, contentTop, centerEnd, contentBottom);
            DrawDeviceDetailsPanel(canvas, detailsBounds);

            // Right panel: Status
            var statusBounds = new SKRect(bounds.Right - pad - rightPanelWidth, contentTop, bounds.Right - pad, contentBottom);
            DrawStatusPanel(canvas, statusBounds);
        }

        // Status bar
        DrawStatusBar(canvas, bounds);
    }

    private void DrawMappingsTabContent(SKCanvas canvas, SKRect bounds, float pad, float contentTop, float contentBottom)
    {
        float frameInset = 5f;
        var contentBounds = new SKRect(pad, contentTop, bounds.Right - pad, contentBottom);

        // Three-panel layout: Left (bindings list) | Center (device view) | Right (settings)
        float leftPanelWidth = 320f;
        float rightPanelWidth = 320f;
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
        // Panel background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(140),
            IsAntialias = true
        };
        canvas.DrawRect(new SKRect(bounds.Left + frameInset, bounds.Top + frameInset,
            bounds.Right - frameInset, bounds.Bottom - frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame, 40f, 10f);

        float y = bounds.Top + frameInset + 10;
        float leftMargin = bounds.Left + frameInset + 10;
        float rightMargin = bounds.Right - frameInset - 10;

        // Title
        FUIRenderer.DrawText(canvas, "VJOY BINDINGS", new SKPoint(leftMargin, y + 12), FUIColors.TextBright, 14f, true);
        y += 30;

        // vJoy device selector: [<] vJoy Device 1 [>]
        float arrowButtonSize = 28f;
        _vjoyPrevButtonBounds = new SKRect(leftMargin, y, leftMargin + arrowButtonSize, y + arrowButtonSize);
        DrawArrowButton(canvas, _vjoyPrevButtonBounds, "◀", _vjoyPrevHovered, _selectedVJoyDeviceIndex > 0);

        string deviceName = _vjoyDevices.Count > 0 && _selectedVJoyDeviceIndex < _vjoyDevices.Count
            ? $"vJoy Device {_vjoyDevices[_selectedVJoyDeviceIndex].Id}"
            : "No vJoy Devices";
        float deviceNameX = leftMargin + arrowButtonSize + 10;
        FUIRenderer.DrawText(canvas, deviceName, new SKPoint(deviceNameX, y + 18), FUIColors.TextBright, 12f);

        _vjoyNextButtonBounds = new SKRect(rightMargin - arrowButtonSize, y, rightMargin, y + arrowButtonSize);
        DrawArrowButton(canvas, _vjoyNextButtonBounds, "▶", _vjoyNextHovered, _selectedVJoyDeviceIndex < _vjoyDevices.Count - 1);
        y += arrowButtonSize + 15;

        FUIRenderer.DrawGlowingLine(canvas,
            new SKPoint(bounds.Left + frameInset, y),
            new SKPoint(bounds.Right - frameInset, y),
            FUIColors.Primary.WithAlpha(80), 1f, 2f);
        y += 10;

        // Scrollable binding rows
        float listBottom = bounds.Bottom - frameInset - 10;
        DrawBindingsList(canvas, new SKRect(leftMargin - 5, y, rightMargin + 5, listBottom));
    }

    private void DrawBindingsList(SKCanvas canvas, SKRect bounds)
    {
        _mappingRowBounds.Clear();
        _mappingAddButtonBounds.Clear();
        _mappingRemoveButtonBounds.Clear();

        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count)
        {
            FUIRenderer.DrawText(canvas, "No vJoy devices available",
                new SKPoint(bounds.Left + 10, bounds.Top + 30), FUIColors.TextDim, 12f);
            return;
        }

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        var profile = _profileService.ActiveProfile;

        float rowHeight = 32f;  // Compact rows (bindings shown in right panel now)
        float rowGap = 4f;
        float y = bounds.Top;
        int rowIndex = 0;

        // Section: AXES
        FUIRenderer.DrawText(canvas, "AXES", new SKPoint(bounds.Left + 5, y + 12), FUIColors.Active, 10f);
        y += 22;

        string[] axisNames = { "X Axis", "Y Axis", "Z Axis", "RX Axis", "RY Axis", "RZ Axis", "Slider 1", "Slider 2" };
        for (int i = 0; i < Math.Min(axisNames.Length, 8); i++)
        {
            if (y + rowHeight > bounds.Bottom) break;

            var rowBounds = new SKRect(bounds.Left, y, bounds.Right, y + rowHeight);
            string binding = GetAxisBindingText(profile, vjoyDevice.Id, i);
            bool isSelected = rowIndex == _selectedMappingRow;
            bool isHovered = rowIndex == _hoveredMappingRow;

            DrawChunkyBindingRow(canvas, rowBounds, axisNames[i], binding, isSelected, isHovered, rowIndex);

            _mappingRowBounds.Add(rowBounds);
            y += rowHeight + rowGap;
            rowIndex++;
        }

        // Section: BUTTONS
        y += 8;
        if (y + 22 < bounds.Bottom)
        {
            FUIRenderer.DrawText(canvas, "BUTTONS", new SKPoint(bounds.Left + 5, y + 12), FUIColors.Active, 10f);
            y += 22;
        }

        for (int i = 0; i < vjoyDevice.ButtonCount && y + rowHeight <= bounds.Bottom; i++)
        {
            var rowBounds = new SKRect(bounds.Left, y, bounds.Right, y + rowHeight);
            string binding = GetButtonBindingText(profile, vjoyDevice.Id, i);
            bool isSelected = rowIndex == _selectedMappingRow;
            bool isHovered = rowIndex == _hoveredMappingRow;

            DrawChunkyBindingRow(canvas, rowBounds, $"Button {i + 1}", binding, isSelected, isHovered, rowIndex);

            _mappingRowBounds.Add(rowBounds);
            y += rowHeight + rowGap;
            rowIndex++;
        }
    }

    private void DrawChunkyBindingRow(SKCanvas canvas, SKRect bounds, string outputName, string binding,
        bool isSelected, bool isHovered, int rowIndex)
    {
        bool hasBinding = !string.IsNullOrEmpty(binding) && binding != "—";

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

        // Binding indicator dot on the right (shows if has binding)
        if (hasBinding)
        {
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

            bool mirror = _deviceMap?.Mirror ?? false;
            DrawSvgInBounds(canvas, _joystickSvg, constrainedBounds, mirror);
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

        FUIRenderer.DrawGlowingLine(canvas,
            new SKPoint(bounds.Left + frameInset, y - 5),
            new SKPoint(bounds.Right - frameInset, y - 5),
            FUIColors.Primary.WithAlpha(80), 1f, 2f);

        // Show settings for selected row
        if (_selectedMappingRow < 0)
        {
            FUIRenderer.DrawText(canvas, "Select an output to configure",
                new SKPoint(leftMargin, y + 30), FUIColors.TextDim, 12f);
            return;
        }

        // Determine if axis or button
        bool isAxis = _selectedMappingRow < 8;
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

        float rowHeight = 28f;
        float rowGap = 4f;

        if (inputs.Count == 0 && !isListening)
        {
            // No inputs - show "None" with dashed border
            var emptyBounds = new SKRect(leftMargin, y, rightMargin, y + rowHeight);
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

            FUIRenderer.DrawText(canvas, "No input mapped", new SKPoint(leftMargin + 10, y + 18), FUIColors.TextDisabled, 11f);
            y += rowHeight + rowGap;
        }
        else
        {
            // Draw each input source row
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

                // Input text
                string inputText = $"{input.DeviceName} - {input.Type} {input.Index}";
                if (inputText.Length > 30) inputText = inputText.Substring(0, 27) + "...";
                FUIRenderer.DrawText(canvas, inputText, new SKPoint(leftMargin + 8, y + 18), FUIColors.TextPrimary, 10f);

                // Remove [×] button
                var removeBounds = new SKRect(rightMargin - 26, y + 2, rightMargin, y + rowHeight - 2);
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

        // Separator line before settings
        FUIRenderer.DrawGlowingLine(canvas,
            new SKPoint(leftMargin - 5, y - 5),
            new SKPoint(rightMargin + 5, y - 5),
            FUIColors.Frame.WithAlpha(60), 1f, 1f);

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
        bool isAxis = _selectedMappingRow < 8;
        int outputIndex = isAxis ? _selectedMappingRow : _selectedMappingRow - 8;

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
            var mapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyButton &&
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

        if (_selectedMappingRow < 8)
        {
            string[] axisNames = { "X Axis", "Y Axis", "Z Axis", "RX Axis", "RY Axis", "RZ Axis", "Slider 1", "Slider 2" };
            return _selectedMappingRow < axisNames.Length ? axisNames[_selectedMappingRow] : $"Axis {_selectedMappingRow}";
        }
        else
        {
            return $"Button {_selectedMappingRow - 8 + 1}";
        }
    }

    private void DrawAxisSettings(SKCanvas canvas, float leftMargin, float rightMargin, float y, float bottom)
    {
        float width = rightMargin - leftMargin;

        // Response Curve section
        FUIRenderer.DrawText(canvas, "RESPONSE CURVE", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += 20;

        // Curve type selector with arrows
        float curveSelectWidth = width;
        float arrowSize = 28f;
        var prevCurveBounds = new SKRect(leftMargin, y, leftMargin + arrowSize, y + arrowSize);
        var nextCurveBounds = new SKRect(rightMargin - arrowSize, y, rightMargin, y + arrowSize);

        DrawArrowButton(canvas, prevCurveBounds, "◀", false, true);
        DrawArrowButton(canvas, nextCurveBounds, "▶", false, true);

        // Curve name in center
        string curveName = "Linear";  // Would come from selected mapping
        FUIRenderer.DrawTextCentered(canvas, curveName,
            new SKRect(leftMargin + arrowSize + 5, y, rightMargin - arrowSize - 5, y + arrowSize),
            FUIColors.TextPrimary, 12f);
        y += arrowSize + 15;

        // Curve visualization
        float curveHeight = 120f;
        var curveBounds = new SKRect(leftMargin, y, rightMargin, y + curveHeight);
        DrawCurveVisualization(canvas, curveBounds);
        y += curveHeight + 20;

        // Deadzone slider
        if (y + 50 < bottom)
        {
            FUIRenderer.DrawText(canvas, "DEADZONE", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
            FUIRenderer.DrawText(canvas, "5%", new SKPoint(rightMargin - 25, y), FUIColors.TextPrimary, 10f);
            y += 18;
            DrawSlider(canvas, new SKRect(leftMargin, y, rightMargin, y + 8), 0.05f);
            y += 25;
        }

        // Saturation slider
        if (y + 50 < bottom)
        {
            FUIRenderer.DrawText(canvas, "SATURATION", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
            FUIRenderer.DrawText(canvas, "100%", new SKPoint(rightMargin - 30, y), FUIColors.TextPrimary, 10f);
            y += 18;
            DrawSlider(canvas, new SKRect(leftMargin, y, rightMargin, y + 8), 1.0f);
            y += 25;
        }

        // Invert toggle
        if (y + 30 < bottom)
        {
            FUIRenderer.DrawText(canvas, "Invert Axis", new SKPoint(leftMargin, y + 8), FUIColors.TextPrimary, 11f);
            DrawToggleSwitch(canvas, new SKRect(rightMargin - 45, y, rightMargin, y + 24), false);
        }
    }

    private void DrawButtonSettings(SKCanvas canvas, float leftMargin, float rightMargin, float y, float bottom)
    {
        // Button Mode section
        FUIRenderer.DrawText(canvas, "BUTTON MODE", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += 20;

        // Mode buttons
        string[] modes = { "Normal", "Toggle", "Pulse", "Hold" };
        float buttonHeight = 32f;
        float buttonGap = 6f;

        for (int i = 0; i < modes.Length && y + buttonHeight < bottom; i++)
        {
            var modeBounds = new SKRect(leftMargin, y, rightMargin, y + buttonHeight);
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

            FUIRenderer.DrawText(canvas, modes[i], new SKPoint(modeBounds.Left + 12, modeBounds.MidY + 4),
                selected ? FUIColors.Active : FUIColors.TextPrimary, 11f);

            _buttonModeBounds[i] = modeBounds;
            y += buttonHeight + buttonGap;
        }

        y += 10;

        // Clear binding button
        if (y + 40 < bottom)
        {
            var clearBounds = new SKRect(leftMargin, y, rightMargin, y + 32);
            using var clearBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
            canvas.DrawRoundRect(clearBounds, 3, 3, clearBgPaint);

            using var clearFramePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.Warning.WithAlpha(150),
                StrokeWidth = 1f
            };
            canvas.DrawRoundRect(clearBounds, 3, 3, clearFramePaint);

            FUIRenderer.DrawTextCentered(canvas, "Clear Binding", clearBounds, FUIColors.Warning, 11f);
        }
    }

    private void DrawCurveVisualization(SKCanvas canvas, SKRect bounds)
    {
        // Background
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1 };
        canvas.DrawRect(bounds, bgPaint);

        // Grid
        using var gridPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Grid.WithAlpha(50),
            StrokeWidth = 1f
        };
        // Vertical center
        canvas.DrawLine(bounds.MidX, bounds.Top, bounds.MidX, bounds.Bottom, gridPaint);
        // Horizontal center
        canvas.DrawLine(bounds.Left, bounds.MidY, bounds.Right, bounds.MidY, gridPaint);

        // Frame
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        // Draw linear curve (diagonal line)
        using var curvePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active,
            StrokeWidth = 2f,
            IsAntialias = true
        };
        canvas.DrawLine(bounds.Left, bounds.Bottom, bounds.Right, bounds.Top, curvePaint);
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

        FUIRenderer.DrawGlowingLine(canvas,
            new SKPoint(bounds.Left + frameInset, y - 10),
            new SKPoint(bounds.Right - frameInset, y - 10),
            FUIColors.Active.WithAlpha(80), 1f, 2f);

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

        // Button mode section (only for button outputs)
        if (!_isEditingAxis)
        {
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
        var textColor = enabled
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

        FUIRenderer.DrawTextCentered(canvas, arrow, bounds, textColor, 14f);
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

        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.Type == OutputType.VJoyButton &&
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
                // Auto-save the mapping
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
            // Find existing mapping or create new one
            var existingMapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyButton &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (existingMapping != null)
            {
                // Add input to existing mapping (support multiple inputs)
                existingMapping.Inputs.Add(newInputSource);
                existingMapping.Name = $"vJoy {vjoyDevice.Id} Button {outputIndex + 1} ({existingMapping.Inputs.Count} inputs)";
            }
            else
            {
                // Create new mapping
                var mapping = new ButtonMapping
                {
                    Name = $"{input.DeviceName} Button {input.Index + 1} -> vJoy {vjoyDevice.Id} Button {outputIndex + 1}",
                    Inputs = new List<InputSource> { newInputSource },
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
        bool isAxis = _selectedMappingRow < 8;
        int outputIndex = isAxis ? _selectedMappingRow : _selectedMappingRow - 8;

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

    private void UpdateButtonModeForSelected()
    {
        if (_selectedMappingRow < 8) return; // Only for buttons
        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count) return;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow - 8;

        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.Type == OutputType.VJoyButton &&
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping != null)
        {
            mapping.Mode = _selectedButtonMode;
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

            // Save the profile
            _profileService.SaveActiveProfile();
        }
    }

    private void DrawTitleBar(SKCanvas canvas, SKRect bounds)
    {
        float titleBarY = 15;
        float titleBarHeight = 50;
        float pad = FUIRenderer.SpaceLG;

        // Left L-corner accent
        using (var accentPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Primary.WithAlpha(100),
            StrokeWidth = 2f,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Square
        })
        {
            canvas.DrawLine(pad, titleBarY + 8, pad, titleBarY + titleBarHeight - 5, accentPaint);
            canvas.DrawLine(pad, titleBarY + 8, pad + 25, titleBarY + 8, accentPaint);
        }

        // Title text
        FUIRenderer.DrawText(canvas, "ASTERIQ", new SKPoint(pad + 40, titleBarY + 38), FUIColors.Primary, 26f, true);

        // Subtitle
        float subtitleX = pad + 185;
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

        // Profile selector (positioned between subtitle and tabs)
        DrawProfileSelector(canvas, bounds.Right - 650, titleBarY + 22);

        // Horizontal base line
        FUIRenderer.DrawGlowingLine(canvas,
            new SKPoint(pad, titleBarY + titleBarHeight + 8),
            new SKPoint(bounds.Right - pad, titleBarY + titleBarHeight + 8),
            FUIColors.Frame.WithAlpha(80), 1f, 2f);

        // Tab indicators
        float tabStartX = bounds.Right - 540;
        float tabX = tabStartX;
        float tabSpacing = 100;

        for (int i = 0; i < _tabNames.Length; i++)
        {
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
                float textWidth = 60f;
                canvas.DrawLine(tabX, titleBarY + 44, tabX + textWidth, titleBarY + 44, paint);

                using var glowPaint = new SKPaint
                {
                    Color = FUIColors.ActiveGlow,
                    StrokeWidth = 6f,
                    ImageFilter = SKImageFilter.CreateBlur(4f, 4f)
                };
                canvas.DrawLine(tabX, titleBarY + 44, tabX + textWidth, titleBarY + 44, glowPaint);
            }

            tabX += tabSpacing;
        }

        // Window controls
        float windowControlsX = bounds.Right - pad - 88;
        FUIRenderer.DrawWindowControls(canvas, windowControlsX, titleBarY + 10,
            _hoveredWindowControl == 0, _hoveredWindowControl == 1, _hoveredWindowControl == 2);

        // Right L-corner accent
        using (var accentPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Primary.WithAlpha(100),
            StrokeWidth = 2f,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Square
        })
        {
            canvas.DrawLine(bounds.Right - pad, titleBarY + 8, bounds.Right - pad, titleBarY + titleBarHeight - 5, accentPaint);
            canvas.DrawLine(bounds.Right - pad - 25, titleBarY + 8, bounds.Right - pad, titleBarY + 8, accentPaint);
        }
    }

    private void DrawProfileSelector(SKCanvas canvas, float x, float y)
    {
        float width = 100f;
        float height = 26f;
        _profileSelectorBounds = new SKRect(x, y, x + width, y + height);

        // Get profile name
        string profileName = _profileService.HasActiveProfile
            ? _profileService.ActiveProfile!.Name
            : "No Profile";

        // Truncate if too long
        if (profileName.Length > 12)
            profileName = profileName.Substring(0, 11) + "…";

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

        // Draw dropdown if open
        if (_profileDropdownOpen)
        {
            DrawProfileDropdown(canvas, x, y + height);
        }
    }

    private void DrawProfileDropdown(SKCanvas canvas, float x, float y)
    {
        float itemHeight = 24f;
        float width = 150f;
        int itemCount = Math.Max(_profiles.Count + 1, 2); // +1 for "New Profile", minimum 2
        float height = itemHeight * itemCount + 4;

        _profileDropdownBounds = new SKRect(x, y, x + width, y + height);

        // Shadow
        using var shadowPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.Black.WithAlpha(80),
            ImageFilter = SKImageFilter.CreateBlur(8f, 8f)
        };
        canvas.DrawRect(new SKRect(x + 4, y + 4, x + width + 4, y + height + 4), shadowPaint);

        // Background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(240),
            IsAntialias = true
        };
        canvas.DrawRect(_profileDropdownBounds, bgPaint);

        // Border
        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame,
            StrokeWidth = 1f,
            IsAntialias = true
        };
        canvas.DrawRect(_profileDropdownBounds, borderPaint);

        // Draw profile items
        float itemY = y + 2;
        for (int i = 0; i < _profiles.Count; i++)
        {
            var profile = _profiles[i];
            var itemBounds = new SKRect(x + 2, itemY, x + width - 2, itemY + itemHeight);
            bool isHovered = _hoveredProfileIndex == i;
            bool isActive = _profileService.ActiveProfile?.Id == profile.Id;

            // Hover background
            if (isHovered)
            {
                using var hoverPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = FUIColors.Primary.WithAlpha(40),
                    IsAntialias = true
                };
                canvas.DrawRect(itemBounds, hoverPaint);
            }

            // Active indicator
            if (isActive)
            {
                using var activePaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = FUIColors.Active,
                    IsAntialias = true
                };
                canvas.DrawRect(new SKRect(x + 2, itemY, x + 4, itemY + itemHeight), activePaint);
            }

            // Profile name
            string name = profile.Name;
            if (name.Length > 16)
                name = name.Substring(0, 15) + "…";

            var color = isActive ? FUIColors.Active : (isHovered ? FUIColors.TextPrimary : FUIColors.TextDim);
            FUIRenderer.DrawText(canvas, name, new SKPoint(x + 10, itemY + 16), color, 11f);

            itemY += itemHeight;
        }

        // "New Profile" option
        var newItemBounds = new SKRect(x + 2, itemY, x + width - 2, itemY + itemHeight);
        bool newHovered = _hoveredProfileIndex == _profiles.Count;

        if (newHovered)
        {
            using var hoverPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = FUIColors.Primary.WithAlpha(40),
                IsAntialias = true
            };
            canvas.DrawRect(newItemBounds, hoverPaint);
        }

        // Separator
        using var sepPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame.WithAlpha(100),
            StrokeWidth = 1f
        };
        canvas.DrawLine(x + 8, itemY, x + width - 8, itemY, sepPaint);

        FUIRenderer.DrawText(canvas, "+ New Profile", new SKPoint(x + 10, itemY + 16),
            newHovered ? FUIColors.Active : FUIColors.TextDim, 11f);
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

        FUIRenderer.DrawGlowingLine(canvas,
            new SKPoint(contentBounds.Left, contentBounds.Top + titleBarHeight),
            new SKPoint(contentBounds.Right, contentBounds.Top + titleBarHeight),
            FUIColors.Primary.WithAlpha(100), 1f, 3f);

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
                DrawDeviceListItem(canvas, contentBounds.Left + pad - 10, itemY, contentBounds.Width - pad,
                    filteredDevices[i].Name, "ONLINE", actualIndex == _selectedDevice, actualIndex == _hoveredDevice);
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

        // Selection/hover background
        if (isSelected || isHovered)
        {
            var bgColor = isSelected ? FUIColors.Active.WithAlpha(30) : FUIColors.Primary.WithAlpha(15);
            FUIRenderer.FillFrame(canvas, itemBounds, bgColor, 6f);
        }

        // Item frame
        var frameColor = isSelected ? FUIColors.Active : (isHovered ? FUIColors.FrameBright : FUIColors.FrameDim);
        FUIRenderer.DrawFrame(canvas, itemBounds, frameColor, 6f, isSelected ? 1.5f : 1f, isSelected);

        // Status indicator dot
        var statusColor = status == "ONLINE" ? FUIColors.Success : FUIColors.Warning;
        FUIRenderer.DrawGlowingDot(canvas, new SKPoint(x + 18, y + 22), statusColor, 4f,
            status == "ONLINE" ? 8f : 4f);

        // Device name (truncate if needed)
        string displayName = name.Length > 28 ? name.Substring(0, 25) + "..." : name;
        var nameColor = isSelected ? FUIColors.TextBright : FUIColors.TextPrimary;
        FUIRenderer.DrawText(canvas, displayName, new SKPoint(x + 35, y + 26), nameColor, 13f, isSelected);

        // Status text
        var statusTextColor = status == "ONLINE" ? FUIColors.Success : FUIColors.Warning;
        FUIRenderer.DrawText(canvas, status, new SKPoint(x + 35, y + 45), statusTextColor, 11f);

        // vJoy assignment indicator
        if (status == "ONLINE")
        {
            FUIRenderer.DrawText(canvas, "VJOY:1", new SKPoint(x + width - 65, y + 45),
                FUIColors.TextDim, 11f);
        }

        // Selection chevron
        if (isSelected)
        {
            using var chevronPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.Active,
                StrokeWidth = 2f,
                IsAntialias = true
            };
            canvas.DrawLine(x + width - 20, y + 25, x + width - 12, y + 30, chevronPaint);
            canvas.DrawLine(x + width - 12, y + 30, x + width - 20, y + 35, chevronPaint);
        }
    }

    private void DrawDeviceDetailsPanel(SKCanvas canvas, SKRect bounds)
    {
        float pad = FUIRenderer.PanelPadding;

        if (_devices.Count == 0 || _selectedDevice < 0 || _selectedDevice >= _devices.Count)
        {
            FUIRenderer.DrawText(canvas, "Select a device to view details",
                new SKPoint(bounds.Left + pad, bounds.Top + 50), FUIColors.TextDim, 14f);
            return;
        }

        var device = _devices[_selectedDevice];

        // Component header
        FUIRenderer.DrawText(canvas, "VK01", new SKPoint(bounds.Left + pad, bounds.Top + 20), FUIColors.Active, 12f);
        FUIRenderer.DrawText(canvas, device.Name.Length > 30 ? device.Name.Substring(0, 27) + "..." : device.Name,
            new SKPoint(bounds.Left + pad + 55, bounds.Top + 20), FUIColors.TextBright, 14f);

        // Underline
        FUIRenderer.DrawGlowingLine(canvas,
            new SKPoint(bounds.Left + pad, bounds.Top + 30),
            new SKPoint(bounds.Left + pad + 220, bounds.Top + 30),
            FUIColors.Primary.WithAlpha(60), 1f, 2f);

        // Device silhouette takes the full panel area (with margin for lead-lines)
        // But limit max size to 900px for consistent appearance
        float leadLineMargin = 180f; // Space for labels on left/right sides
        float silhouetteLeft = bounds.Left + leadLineMargin;
        float silhouetteTop = bounds.Top + 45;
        float silhouetteRight = bounds.Right - leadLineMargin;
        float silhouetteBottom = bounds.Bottom - 20;

        // Apply 900px max size limit
        float maxSize = 900f;
        float availWidth = silhouetteRight - silhouetteLeft;
        float availHeight = silhouetteBottom - silhouetteTop;

        if (availWidth > maxSize || availHeight > maxSize)
        {
            float constrainedSize = Math.Min(Math.Min(availWidth, availHeight), maxSize);
            float centerX = (silhouetteLeft + silhouetteRight) / 2;
            float centerY = (silhouetteTop + silhouetteBottom) / 2;
            silhouetteLeft = centerX - constrainedSize / 2;
            silhouetteRight = centerX + constrainedSize / 2;
            silhouetteTop = centerY - constrainedSize / 2;
            silhouetteBottom = centerY + constrainedSize / 2;
        }

        _silhouetteBounds = new SKRect(silhouetteLeft, silhouetteTop, silhouetteRight, silhouetteBottom);
        DrawDeviceSilhouette(canvas, _silhouetteBounds);

        // Draw dynamic lead-lines for active inputs
        DrawActiveInputLeadLines(canvas, bounds);

        // Connection line from device list (animated)
        var lineStart = new SKPoint(bounds.Left - 10, bounds.Top + 90);
        var lineEnd = new SKPoint(bounds.Left + 15, bounds.Top + 90);
        using var connectorPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active,
            StrokeWidth = 1f,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new[] { 6f, 4f }, _dashPhase)
        };
        canvas.DrawLine(lineStart, lineEnd, connectorPaint);
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
        // L-corner frame for "targeting" feel
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame.WithAlpha(100), 20f, 6f);

        // Draw the actual SVG if loaded, otherwise fallback to simple outline
        if (_joystickSvg?.Picture != null)
        {
            bool mirror = _deviceMap?.Mirror ?? false;
            DrawSvgInBounds(canvas, _joystickSvg, bounds, mirror);
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

        FUIRenderer.DrawGlowingLine(canvas,
            new SKPoint(contentBounds.Left, contentBounds.Top + titleBarHeight),
            new SKPoint(contentBounds.Right, contentBounds.Top + titleBarHeight),
            FUIColors.Primary.WithAlpha(80), 1f, 3f);

        // Status items
        float statusItemHeight = 32f;
        float itemY = contentBounds.Top + titleBarHeight + pad;
        DrawStatusItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2, "VJOY DRIVER", "ACTIVE", FUIColors.Success);
        itemY += statusItemHeight + itemGap;
        DrawStatusItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2, "HIDHIDE", "ENABLED", FUIColors.Success);
        itemY += statusItemHeight + itemGap;
        DrawStatusItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2, "INPUT RATE", "100 HZ", FUIColors.TextPrimary);
        itemY += statusItemHeight + itemGap;
        DrawStatusItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2, "MAPPING", "ACTIVE", FUIColors.Active);
        itemY += statusItemHeight + itemGap;
        DrawStatusItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2, "PROFILE", "DEFAULT", FUIColors.TextPrimary);

        // Separator
        itemY += statusItemHeight + FUIRenderer.SpaceMD;
        FUIRenderer.DrawGlowingLine(canvas,
            new SKPoint(bounds.Left + pad, itemY),
            new SKPoint(bounds.Right - pad, itemY),
            FUIColors.FrameDim, 1f, 2f);

        // Active layers
        itemY += FUIRenderer.SpaceMD;
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

        var dotColor = valueColor == FUIColors.Success ? valueColor : FUIColors.Primary.WithAlpha(100);
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

        FUIRenderer.DrawGlowingLine(canvas,
            new SKPoint(30, y),
            new SKPoint(bounds.Right - 30, y),
            FUIColors.Frame.WithAlpha(100), 1f, 2f);

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
