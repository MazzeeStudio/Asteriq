using System.Runtime.InteropServices;
using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

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

    public MainForm()
    {
        _inputService = new InputService();
        InitializeForm();
        InitializeCanvas();
        InitializeInput();
        InitializeRenderLoop();
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

        _canvas.Invalidate();
    }

    private void RefreshDevices()
    {
        _devices = _inputService.EnumerateDevices();
        if (_devices.Count > 0 && _selectedDevice < 0)
        {
            _selectedDevice = 0;
        }
    }

    private void OnInputReceived(object? sender, DeviceInputState state)
    {
        if (_selectedDevice >= 0 && _selectedDevice < _devices.Count &&
            state.DeviceIndex == _devices[_selectedDevice].DeviceIndex)
        {
            _currentInputState = state;
        }
    }

    private void OnDeviceDisconnected(object? sender, int deviceIndex)
    {
        BeginInvoke(() =>
        {
            RefreshDevices();
            if (_selectedDevice >= _devices.Count)
            {
                _selectedDevice = Math.Max(0, _devices.Count - 1);
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
    }

    private void OnCanvasMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

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
    }

    private void OnCanvasMouseLeave(object? sender, EventArgs e)
    {
        _hoveredDevice = -1;
        _hoveredWindowControl = -1;
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

        // Axis visualization
        float axisX = bounds.Left + 30;
        float axisY = bounds.Top + 70;
        float axisWidth = 180;
        float axisHeight = 20;
        float axisSpacing = 35;

        string[] axisNames = { "X-AXIS", "Y-AXIS", "Z-AXIS", "RX-AXIS", "RY-AXIS", "RZ-AXIS", "SLIDER0", "SLIDER1" };
        var axes = _currentInputState?.Axes ?? Array.Empty<float>();
        int axisCount = Math.Min(device.AxisCount, 8);

        for (int i = 0; i < axisCount; i++)
        {
            float y = axisY + i * axisSpacing;
            float value = i < axes.Length ? (axes[i] + 1f) / 2f : 0.5f; // Convert -1..1 to 0..1

            // Axis label
            FUIRenderer.DrawText(canvas, axisNames[i], new SKPoint(axisX, y + 14), FUIColors.TextDim, 11f);

            // Axis bar
            var barBounds = new SKRect(axisX + 75, y, axisX + 75 + axisWidth, y + axisHeight);
            FUIRenderer.DrawDataBar(canvas, barBounds, value, FUIColors.Active, FUIColors.Frame);

            // Value display
            float rawValue = i < axes.Length ? axes[i] : 0f;
            FUIRenderer.DrawText(canvas, $"[{(int)(rawValue * 100):+00;-00}]",
                new SKPoint(axisX + 75 + axisWidth + 10, y + 14), FUIColors.TextPrimary, 10f);
        }

        // Button grid
        float buttonGridX = bounds.Left + 320;
        float buttonGridY = bounds.Top + 70;
        float buttonSize = 28;
        float buttonSpacing = 35;

        FUIRenderer.DrawText(canvas, "BUTTONS", new SKPoint(buttonGridX, buttonGridY - 10),
            FUIColors.TextDim, 11f);

        var buttons = _currentInputState?.Buttons ?? Array.Empty<bool>();
        int buttonCount = Math.Min(device.ButtonCount, 32);
        int buttonsPerRow = 8;

        for (int i = 0; i < buttonCount; i++)
        {
            int row = i / buttonsPerRow;
            int col = i % buttonsPerRow;
            float bx = buttonGridX + col * buttonSpacing;
            float by = buttonGridY + row * buttonSpacing + 10;

            bool isPressed = i < buttons.Length && buttons[i];
            DrawButtonIndicator(canvas, bx, by, buttonSize, i + 1, isPressed);
        }

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

    private void DrawButtonIndicator(SKCanvas canvas, float x, float y, float size, int buttonNum, bool isPressed)
    {
        var buttonBounds = new SKRect(x, y, x + size, y + size);

        var frameColor = isPressed ? FUIColors.Active : FUIColors.Frame;
        var fillColor = isPressed ? FUIColors.Active.WithAlpha(60) : FUIColors.Background2;

        FUIRenderer.FillFrame(canvas, buttonBounds, fillColor, 4f);
        FUIRenderer.DrawFrame(canvas, buttonBounds, frameColor, 4f, 1f, isPressed);

        var textColor = isPressed ? FUIColors.TextBright : FUIColors.TextDim;
        FUIRenderer.DrawTextCentered(canvas, buttonNum.ToString(), buttonBounds, textColor, 10f, isPressed);
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

        // Left: connection status
        string deviceText = _devices.Count == 1 ? "1 DEVICE CONNECTED" : $"{_devices.Count} DEVICES CONNECTED";
        FUIRenderer.DrawText(canvas, deviceText,
            new SKPoint(40, y + 22), FUIColors.TextDim, 12f);

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
