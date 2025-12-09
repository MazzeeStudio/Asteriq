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

    public MainForm()
    {
        _inputService = new InputService();
        _profileService = new ProfileService();
        InitializeForm();
        InitializeCanvas();
        InitializeInput();
        InitializeRenderLoop();
        LoadSvgAssets();
        InitializeProfiles();
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
    /// Searches for maps matching the device name, falls back to generic joystick.json.
    /// Also detects left-hand devices by "LEFT" prefix and sets mirror flag.
    /// </summary>
    private void LoadDeviceMapForDevice(string? deviceName)
    {
        var mapsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Devices", "Maps");

        // Try to find a device-specific map by matching device name
        if (!string.IsNullOrEmpty(deviceName))
        {
            // Check all JSON files in the maps directory
            foreach (var mapFile in Directory.GetFiles(mapsDir, "*.json"))
            {
                if (Path.GetFileName(mapFile).Equals("device-control-map.schema.json", StringComparison.OrdinalIgnoreCase))
                    continue;

                var map = DeviceMap.Load(mapFile);
                if (map != null && !string.IsNullOrEmpty(map.Device))
                {
                    // Match by device name (case-insensitive contains)
                    if (deviceName.Contains(map.Device, StringComparison.OrdinalIgnoreCase) ||
                        map.Device.Contains(deviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        _deviceMap = map;
                        System.Diagnostics.Debug.WriteLine($"Loaded device map: {mapFile} for device: {deviceName}");
                        return;
                    }
                }
            }

            // No specific map found - check if device name indicates left-hand
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

        // Title bar area for dragging (but not over buttons)
        if (clientPoint.Y < TitleBarHeight)
        {
            // Exclude window controls area
            if (clientPoint.X >= ClientSize.Width - 120)
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

        // Device list hover detection
        float pad = FUIRenderer.SpaceLG;
        float contentTop = 90;
        float leftPanelWidth = 300f;

        if (e.X >= pad && e.X <= pad + leftPanelWidth)
        {
            float itemY = contentTop + 32 + FUIRenderer.PanelPadding;
            float itemHeight = 60f;
            float itemGap = FUIRenderer.ItemSpacing;

            int deviceIndex = (int)((e.Y - itemY) / (itemHeight + itemGap));
            if (deviceIndex >= 0 && deviceIndex < _devices.Count)
            {
                _hoveredDevice = deviceIndex;
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

        // Left panel: Device List
        var deviceListBounds = new SKRect(pad, contentTop, pad + leftPanelWidth, contentBottom);
        DrawDeviceListPanel(canvas, deviceListBounds);

        // Center panel: Device Details
        var detailsBounds = new SKRect(centerStart, contentTop, centerEnd, contentBottom);
        DrawDeviceDetailsPanel(canvas, detailsBounds);

        // Right panel: Status
        var statusBounds = new SKRect(bounds.Right - pad - rightPanelWidth, contentTop, bounds.Right - pad, contentBottom);
        DrawStatusPanel(canvas, statusBounds);

        // Status bar
        DrawStatusBar(canvas, bounds);
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
        bool panelHovered = _hoveredDevice >= 0;
        FUIRenderer.DrawLCornerFrame(canvas, bounds,
            panelHovered ? FUIColors.FrameBright : FUIColors.Frame, 40f, 10f, 1.5f, panelHovered);

        // Header
        float titleBarHeight = 32f;
        var titleBounds = new SKRect(contentBounds.Left, contentBounds.Top, contentBounds.Right, contentBounds.Top + titleBarHeight);
        FUIRenderer.DrawPanelTitle(canvas, titleBounds, "D1", "DEVICES");

        FUIRenderer.DrawGlowingLine(canvas,
            new SKPoint(contentBounds.Left, contentBounds.Top + titleBarHeight),
            new SKPoint(contentBounds.Right, contentBounds.Top + titleBarHeight),
            FUIColors.Primary.WithAlpha(100), 1f, 3f);

        // Device list
        float itemY = contentBounds.Top + titleBarHeight + pad;
        float itemHeight = 60f;

        if (_devices.Count == 0)
        {
            FUIRenderer.DrawText(canvas, "No devices detected",
                new SKPoint(bounds.Left + pad + 30, itemY + 20), FUIColors.TextDim, 12f);
            FUIRenderer.DrawText(canvas, "Connect a joystick or gamepad",
                new SKPoint(bounds.Left + pad + 30, itemY + 38), FUIColors.TextDisabled, 10f);
        }
        else
        {
            for (int i = 0; i < _devices.Count && itemY + itemHeight < contentBounds.Bottom - 40; i++)
            {
                DrawDeviceListItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2,
                    _devices[i].Name, "ONLINE", i == _selectedDevice, i == _hoveredDevice);
                itemY += itemHeight + itemGap;
            }
        }

        // "Scan for devices" prompt
        float promptY = bounds.Bottom - pad - 20;
        FUIRenderer.DrawText(canvas, "+ SCAN FOR DEVICES",
            new SKPoint(bounds.Left + pad + 30, promptY), FUIColors.TextDim, 12f);

        using var bracketPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.FrameDim,
            StrokeWidth = 1f
        };
        canvas.DrawLine(bounds.Left + pad + 10, promptY - 10, bounds.Left + pad + 10, promptY + 5, bracketPaint);
        canvas.DrawLine(bounds.Left + pad + 10, promptY - 10, bounds.Left + pad + 22, promptY - 10, bracketPaint);
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
        float leadLineMargin = 180f; // Space for labels on left/right sides
        float silhouetteLeft = bounds.Left + leadLineMargin;
        float silhouetteTop = bounds.Top + 45;
        float silhouetteRight = bounds.Right - leadLineMargin;
        float silhouetteBottom = bounds.Bottom - 20;

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
