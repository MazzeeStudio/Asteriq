using System.Runtime.InteropServices;
using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Asteriq.UI;

/// <summary>
/// State of the mapping dialog workflow
/// </summary>
public enum MappingDialogState
{
    WaitingForInput,    // "Press a button or move an axis..."
    SelectingOutput,    // User selects output target
    ConfiguringOptions, // Advanced options (curve, mode, etc.)
    Complete           // Mapping created
}

/// <summary>
/// Dialog result containing the configured mapping
/// </summary>
public class MappingDialogResult
{
    public bool Success { get; set; }
    public DetectedInput? Input { get; set; }
    public OutputTarget? Output { get; set; }
    public ButtonMode ButtonMode { get; set; } = ButtonMode.Normal;
    public AxisCurve? AxisCurve { get; set; }
    public string MappingName { get; set; } = "";
}

/// <summary>
/// FUI-styled dialog for creating a new mapping.
/// Flow: Wait for input -> Select output -> Configure options -> Done
/// </summary>
public class MappingDialog : Form
{
    private readonly InputService _inputService;
    private readonly InputDetectionService _detectionService;
    private readonly VJoyService _vjoyService;
    private readonly List<VJoyDeviceInfo> _vjoyDevices;

    private SKControl _canvas = null!;
    private System.Windows.Forms.Timer _renderTimer = null!;
    private System.Windows.Forms.Timer _timeoutTimer = null!;

    // Animation state
    private float _pulsePhase;
    private float _dashPhase;

    // Dialog state
    private MappingDialogState _state = MappingDialogState.WaitingForInput;
    private DetectedInput? _detectedInput;
    private int _selectedVJoyDevice = 0;
    private int _selectedOutputIndex = 0;
    private ButtonMode _selectedButtonMode = ButtonMode.Normal;
    private int _timeoutRemaining = 30; // seconds

    // Output mode state (vJoy vs Keyboard)
    private bool _keyboardMode = false;
    private string _selectedKey = "";
    private List<string> _capturedModifiers = new();  // Stores captured modifiers (LCtrl, RCtrl, LShift, RShift, LAlt, RAlt)
    private bool _waitingForKeyCapture = false;

    // Windows API for detecting held keys
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    // Virtual key codes for left/right modifiers
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;  // Left Alt
    private const int VK_RMENU = 0xA5;  // Right Alt

    // UI state
    private int _hoveredButton = -1;
    private SKRect[] _buttonBounds = Array.Empty<SKRect>();

    public MappingDialogResult Result { get; private set; } = new();

    public MappingDialog(InputService inputService, VJoyService vjoyService)
        : this(inputService, vjoyService, null)
    {
    }

    public MappingDialog(InputService inputService, VJoyService vjoyService, DetectedInput? preSelectedInput)
    {
        _inputService = inputService;
        _vjoyService = vjoyService;
        _detectionService = new InputDetectionService(_inputService);
        _vjoyDevices = _vjoyService.EnumerateDevices();

        InitializeForm();
        InitializeCanvas();
        InitializeTimers();

        if (preSelectedInput is not null)
        {
            // Skip input detection, go straight to output selection
            _detectedInput = preSelectedInput;
            _state = MappingDialogState.SelectingOutput;
        }
        else
        {
            // Start waiting for input
            StartInputDetection();
        }
    }

    private void InitializeForm()
    {
        Text = "New Mapping";
        Size = new Size(500, 400);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(15, 18, 25);
        ShowInTaskbar = false;
    }

    private void InitializeCanvas()
    {
        _canvas = new SKControl { Dock = DockStyle.Fill };
        _canvas.PaintSurface += OnPaintSurface;
        _canvas.MouseMove += OnCanvasMouseMove;
        _canvas.MouseDown += OnCanvasMouseDown;
        Controls.Add(_canvas);
    }

    private void InitializeTimers()
    {
        // Render timer
        _renderTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _renderTimer.Tick += (s, e) =>
        {
            _pulsePhase += 0.08f;
            _dashPhase += 0.5f;
            if (_dashPhase > 10f) _dashPhase = 0f;
            _canvas.Invalidate();
        };
        _renderTimer.Start();

        // Timeout timer
        _timeoutTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timeoutTimer.Tick += (s, e) =>
        {
            if (_state == MappingDialogState.WaitingForInput)
            {
                _timeoutRemaining--;
                if (_timeoutRemaining <= 0)
                {
                    Cancel();
                }
            }
        };
        _timeoutTimer.Start();
    }

    /// <summary>
    /// Starts input detection asynchronously. Fire-and-forget from UI initialization.
    /// All exceptions are handled internally.
    /// </summary>
    private void StartInputDetection()
    {
        // Fire-and-forget async operation with internal exception handling
        _ = StartInputDetectionAsync();
    }

    private async Task StartInputDetectionAsync()
    {
        _state = MappingDialogState.WaitingForInput;
        _timeoutRemaining = 30;

        try
        {
            _detectedInput = await _detectionService.WaitForInputAsync(
                InputDetectionFilter.All, // Buttons, Axes, and Hats
                axisThreshold: 0.5f,
                timeoutMs: 30000);

            if (_detectedInput is not null)
            {
                _state = MappingDialogState.SelectingOutput;
                _timeoutTimer.Stop();

                // Default output selection based on input type
                if (_vjoyDevices.Count > 0)
                {
                    _selectedVJoyDevice = 0;
                    _selectedOutputIndex = _detectedInput.Type == InputType.Button ? 1 : 0;
                }
            }
            else
            {
                // Timeout or cancelled
                Cancel();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MappingDialog] Input detection failed: {ex.Message}");
            Cancel();
        }
    }

    private void Cancel()
    {
        _detectionService.Cancel();
        Result.Success = false;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void Complete()
    {
        Console.WriteLine($"[MappingDialog] Complete() called");
        Console.WriteLine($"[MappingDialog] _detectedInput: {_detectedInput}");
        Console.WriteLine($"[MappingDialog] _keyboardMode: {_keyboardMode}");
        Console.WriteLine($"[MappingDialog] _selectedKey: '{_selectedKey}'");

        if (_detectedInput is null)
        {
            Console.WriteLine($"[MappingDialog] _detectedInput is null, cancelling");
            Cancel();
            return;
        }

        // Keyboard mode validation
        if (_keyboardMode)
        {
            if (string.IsNullOrEmpty(_selectedKey))
            {
                Console.WriteLine($"[MappingDialog] _selectedKey is empty, returning without saving");
                // No key selected yet
                return;
            }

            Console.WriteLine($"[MappingDialog] Creating keyboard mapping with key: {_selectedKey}");
            Result.Success = true;
            Result.Input = _detectedInput;
            Result.Output = new OutputTarget
            {
                Type = OutputType.Keyboard,
                KeyName = _selectedKey,
                Modifiers = _capturedModifiers.Count > 0 ? _capturedModifiers.ToList() : null
            };
            Result.ButtonMode = _selectedButtonMode;

            Result.MappingName = $"{_detectedInput.DeviceName} {_detectedInput.Type} {_detectedInput.Index} -> {GetKeyComboDisplayString()}";
            Console.WriteLine($"[MappingDialog] MappingName: {Result.MappingName}");

            DialogResult = DialogResult.OK;
            Console.WriteLine($"[MappingDialog] DialogResult set to OK, closing...");
            Close();
            return;
        }

        // vJoy mode validation
        if (_vjoyDevices.Count == 0)
        {
            Cancel();
            return;
        }

        var vjoyDevice = _vjoyDevices[_selectedVJoyDevice];

        Result.Success = true;
        Result.Input = _detectedInput;

        // Determine output type based on input type
        var outputType = _detectedInput.Type switch
        {
            InputType.Button => OutputType.VJoyButton,
            InputType.Axis => OutputType.VJoyAxis,
            InputType.Hat => OutputType.VJoyPov,
            _ => OutputType.VJoyButton
        };

        Result.Output = new OutputTarget
        {
            Type = outputType,
            VJoyDevice = vjoyDevice.Id,
            Index = _selectedOutputIndex
        };
        Result.ButtonMode = _selectedButtonMode;
        Result.MappingName = $"{_detectedInput.DeviceName} {_detectedInput.Type} {_detectedInput.Index} -> vJoy {vjoyDevice.Id}";

        if (_detectedInput.Type == InputType.Axis)
        {
            Result.AxisCurve = new AxisCurve(); // Default linear
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// Build display string showing captured key combo (e.g., "LCtrl+LShift+A")
    /// </summary>
    private string GetKeyComboDisplayString()
    {
        if (string.IsNullOrEmpty(_selectedKey))
            return "";

        if (_capturedModifiers.Count == 0)
            return _selectedKey;

        return string.Join("+", _capturedModifiers) + "+" + _selectedKey;
    }

    /// <summary>
    /// Check if a key is currently held using GetAsyncKeyState
    /// </summary>
    private static bool IsKeyHeld(int vk)
    {
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    /// <summary>
    /// Capture currently held modifier keys
    /// </summary>
    private List<string> CaptureHeldModifiers()
    {
        var mods = new List<string>();

        // Check left/right modifiers separately
        if (IsKeyHeld(VK_LCONTROL)) mods.Add("LCtrl");
        if (IsKeyHeld(VK_RCONTROL)) mods.Add("RCtrl");
        if (IsKeyHeld(VK_LSHIFT)) mods.Add("LShift");
        if (IsKeyHeld(VK_RSHIFT)) mods.Add("RShift");
        if (IsKeyHeld(VK_LMENU)) mods.Add("LAlt");
        if (IsKeyHeld(VK_RMENU)) mods.Add("RAlt");

        return mods;
    }

    #region Mouse Handling

    private void OnCanvasMouseMove(object? sender, MouseEventArgs e)
    {
        _hoveredButton = -1;
        for (int i = 0; i < _buttonBounds.Length; i++)
        {
            if (_buttonBounds[i].Contains(e.X, e.Y))
            {
                _hoveredButton = i;
                Cursor = Cursors.Hand;
                return;
            }
        }
        Cursor = Cursors.Default;
    }

    private void OnCanvasMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        // Force check which button is under the click (in case hover didn't update)
        int clickedButton = -1;
        for (int i = 0; i < _buttonBounds.Length; i++)
        {
            if (_buttonBounds[i].Contains(e.X, e.Y))
            {
                clickedButton = i;
                break;
            }
        }

        Console.WriteLine($"[MappingDialog] MouseDown at ({e.X}, {e.Y}): state={_state}, hoveredButton={_hoveredButton}, clickedButton={clickedButton}, buttonCount={_buttonBounds.Length}");
        for (int i = 0; i < _buttonBounds.Length; i++)
        {
            var b = _buttonBounds[i];
            Console.WriteLine($"  Button {i}: ({b.Left}, {b.Top}, {b.Right}, {b.Bottom})");
        }

        // Use clickedButton instead of _hoveredButton
        _hoveredButton = clickedButton;

        switch (_state)
        {
            case MappingDialogState.WaitingForInput:
                // Cancel button
                if (_hoveredButton == 0) Cancel();
                break;

            case MappingDialogState.SelectingOutput:
                HandleOutputSelection();
                break;
        }
    }

    private void HandleOutputSelection()
    {
        bool canUseKeyboard = _detectedInput?.Type == InputType.Button;
        int buttonOffset = canUseKeyboard ? 2 : 0;

        // Handle mode tabs (only for buttons)
        if (canUseKeyboard)
        {
            if (_hoveredButton == 0) // vJoy tab
            {
                _keyboardMode = false;
                _waitingForKeyCapture = false;
                return;
            }
            if (_hoveredButton == 1) // Keyboard tab
            {
                _keyboardMode = true;
                return;
            }
        }

        if (_keyboardMode && canUseKeyboard)
        {
            Console.WriteLine($"[MappingDialog] HandleOutputSelection keyboard mode: hoveredButton={_hoveredButton}, buttonOffset={buttonOffset}, adjusted={_hoveredButton - buttonOffset}");
            // Keyboard mode button handling (no more checkboxes - modifiers captured with key)
            int adjustedButton = _hoveredButton - buttonOffset;
            switch (adjustedButton)
            {
                case 0: // Key capture field
                    Console.WriteLine($"[MappingDialog] Key capture field clicked");
                    _waitingForKeyCapture = true;
                    _selectedKey = "";           // Clear previous selection
                    _capturedModifiers.Clear();  // Clear previous modifiers
                    return;
                case 1: // Cancel
                    Console.WriteLine($"[MappingDialog] Cancel clicked");
                    Cancel();
                    return;
                case 2: // Create
                    Console.WriteLine($"[MappingDialog] Create clicked, calling Complete()");
                    Complete();
                    return;
                default:
                    Console.WriteLine($"[MappingDialog] No match for adjustedButton={adjustedButton}");
                    break;
            }
        }
        else
        {
            // vJoy mode button handling
            switch (_hoveredButton - buttonOffset)
            {
                case 0: // Previous vJoy device
                    if (_selectedVJoyDevice > 0) _selectedVJoyDevice--;
                    break;
                case 1: // Next vJoy device
                    if (_selectedVJoyDevice < _vjoyDevices.Count - 1) _selectedVJoyDevice++;
                    break;
                case 2: // Previous output index
                    if (_selectedOutputIndex > 0) _selectedOutputIndex--;
                    break;
                case 3: // Next output index
                    _selectedOutputIndex++;
                    // Clamp to max available based on input type
                    if (_vjoyDevices.Count > 0)
                    {
                        var device = _vjoyDevices[_selectedVJoyDevice];
                        int max = _detectedInput?.Type switch
                        {
                            InputType.Button => device.ButtonCount,
                            InputType.Axis => 8,
                            InputType.Hat => Math.Max(device.ContPovCount, device.DiscPovCount),
                            _ => 1
                        };
                        if (max < 1) max = 1;
                        if (_selectedOutputIndex >= max) _selectedOutputIndex = max - 1;
                    }
                    break;
                case 4: // Cancel
                    Cancel();
                    break;
                case 5: // Create mapping
                    Complete();
                    break;
            }
        }
    }

    #endregion

    #region Rendering

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var bounds = new SKRect(0, 0, e.Info.Width, e.Info.Height);

        canvas.Clear(FUIColors.Void);

        // Background
        DrawBackground(canvas, bounds);

        // Content based on state
        switch (_state)
        {
            case MappingDialogState.WaitingForInput:
                DrawWaitingForInput(canvas, bounds);
                break;
            case MappingDialogState.SelectingOutput:
                DrawSelectingOutput(canvas, bounds);
                break;
        }

        // Border frame
        DrawDialogFrame(canvas, bounds);
    }

    private void DrawBackground(SKCanvas canvas, SKRect bounds)
    {
        // Gradient background
        using var bgPaint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, bounds.Height),
                new[] { FUIColors.Background1, FUIColors.Void },
                null,
                SKShaderTileMode.Clamp)
        };
        canvas.DrawRect(bounds, bgPaint);

        // Subtle grid
        FUIRenderer.DrawDotGrid(canvas, bounds, 20f, FUIColors.Grid.WithAlpha(30));
    }

    private void DrawDialogFrame(SKCanvas canvas, SKRect bounds)
    {
        // Outer glow
        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Primary.WithAlpha(40),
            StrokeWidth = 4f,
            ImageFilter = SKImageFilter.CreateBlur(8f, 8f)
        };
        canvas.DrawRect(bounds.Inset(2, 2), glowPaint);

        // Frame border
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame,
            StrokeWidth = 2f,
            IsAntialias = true
        };
        canvas.DrawRect(bounds.Inset(1, 1), framePaint);

        // Title bar
        float titleHeight = 40f;
        using var titleBgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background2.WithAlpha(200)
        };
        canvas.DrawRect(new SKRect(0, 0, bounds.Width, titleHeight), titleBgPaint);

        FUIRenderer.DrawText(canvas, "NEW MAPPING", new SKPoint(15, 26), FUIColors.Primary, 14f, true);

        // Close button
        using var closePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.TextDim,
            StrokeWidth = 2f,
            IsAntialias = true
        };
        float closeX = bounds.Width - 30;
        canvas.DrawLine(closeX, 12, closeX + 16, 28, closePaint);
        canvas.DrawLine(closeX + 16, 12, closeX, 28, closePaint);
    }

    private void DrawWaitingForInput(SKCanvas canvas, SKRect bounds)
    {
        float centerY = bounds.Height / 2;

        // Pulsing circle
        float radius = 60f + MathF.Sin(_pulsePhase) * 10f;
        byte alpha = (byte)(100 + MathF.Sin(_pulsePhase) * 50);

        using var circlePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Primary.WithAlpha(alpha),
            StrokeWidth = 3f,
            IsAntialias = true
        };
        canvas.DrawCircle(bounds.MidX, centerY - 20, radius, circlePaint);

        // Inner circle
        using var innerCirclePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active.WithAlpha((byte)(150 + MathF.Sin(_pulsePhase * 2) * 50)),
            StrokeWidth = 2f,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new[] { 8f, 4f }, _dashPhase)
        };
        canvas.DrawCircle(bounds.MidX, centerY - 20, 40f, innerCirclePaint);

        // Main instruction
        FUIRenderer.DrawTextCentered(canvas,
            "PRESS A BUTTON, MOVE AN AXIS, OR HAT",
            new SKRect(0, centerY + 50, bounds.Width, centerY + 75),
            FUIColors.TextBright, 14f, true);

        // Subtitle
        FUIRenderer.DrawTextCentered(canvas,
            "on your device to map it",
            new SKRect(0, centerY + 75, bounds.Width, centerY + 95),
            FUIColors.TextDim, 12f);

        // Timeout indicator
        FUIRenderer.DrawTextCentered(canvas,
            $"Timeout in {_timeoutRemaining}s",
            new SKRect(0, centerY + 110, bounds.Width, centerY + 130),
            FUIColors.TextDisabled, 11f);

        // Cancel button
        var buttons = new List<SKRect>();
        var cancelBounds = new SKRect(bounds.MidX - 50, bounds.Height - 60, bounds.MidX + 50, bounds.Height - 30);
        buttons.Add(cancelBounds);
        _buttonBounds = buttons.ToArray();

        DrawButton(canvas, cancelBounds, "CANCEL", _hoveredButton == 0, false);
    }

    private void DrawSelectingOutput(SKCanvas canvas, SKRect bounds)
    {
        if (_detectedInput is null)
        {
            FUIRenderer.DrawTextCentered(canvas, "No input detected",
                new SKRect(0, bounds.Height / 2, bounds.Width, bounds.Height / 2 + 20),
                FUIColors.Warning, 14f);
            return;
        }

        float y = 60f;
        float pad = 20f;
        var buttons = new List<SKRect>();

        // Detected input info
        FUIRenderer.DrawText(canvas, "INPUT DETECTED", new SKPoint(pad, y), FUIColors.Active, 12f);
        y += 25;

        var inputFrame = new SKRect(pad, y, bounds.Width - pad, y + 50);
        using (var inputBgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Active.WithAlpha(30)
        })
        {
            canvas.DrawRect(inputFrame, inputBgPaint);
        }
        using (var inputFramePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active,
            StrokeWidth = 1f
        })
        {
            canvas.DrawRect(inputFrame, inputFramePaint);
        }

        FUIRenderer.DrawText(canvas, _detectedInput.ToString(),
            new SKPoint(pad + 10, y + 20), FUIColors.TextBright, 13f, true);

        string inputTypeStr = _detectedInput.Type == InputType.Button ? "BUTTON" :
                              _detectedInput.Type == InputType.Axis ? "AXIS" : "HAT";
        FUIRenderer.DrawText(canvas, inputTypeStr,
            new SKPoint(pad + 10, y + 38), FUIColors.TextDim, 11f);

        y += 70;

        // Output mode toggle (only for buttons - axes/hats can only go to vJoy)
        bool canUseKeyboard = _detectedInput.Type == InputType.Button;

        FUIRenderer.DrawText(canvas, "OUTPUT TARGET", new SKPoint(pad, y), FUIColors.Primary, 12f);
        y += 25;

        if (canUseKeyboard)
        {
            // Mode toggle tabs
            var vjoyTabBounds = new SKRect(pad, y, pad + 100, y + 28);
            var keyboardTabBounds = new SKRect(pad + 110, y, pad + 210, y + 28);
            buttons.Add(vjoyTabBounds);    // Button 0
            buttons.Add(keyboardTabBounds); // Button 1

            DrawModeTab(canvas, vjoyTabBounds, "vJoy", !_keyboardMode, _hoveredButton == 0);
            DrawModeTab(canvas, keyboardTabBounds, "Keyboard", _keyboardMode, _hoveredButton == 1);

            y += 40;
        }

        if (_keyboardMode && canUseKeyboard)
        {
            // Keyboard mode UI
            DrawKeyboardOutput(canvas, bounds, y, pad, buttons, canUseKeyboard ? 2 : 0);
        }
        else
        {
            // vJoy mode UI
            DrawVJoyOutput(canvas, bounds, y, pad, buttons, canUseKeyboard ? 2 : 0);
        }

        _buttonBounds = buttons.ToArray();
    }

    private void DrawVJoyOutput(SKCanvas canvas, SKRect bounds, float y, float pad, List<SKRect> buttons, int buttonOffset)
    {
        if (_vjoyDevices.Count == 0)
        {
            FUIRenderer.DrawText(canvas, "No vJoy devices available",
                new SKPoint(pad, y + 10), FUIColors.Warning, 12f);

            // Still add action buttons at the bottom
            var noDeviceCancelBounds = new SKRect(bounds.MidX - 120, bounds.Height - 60, bounds.MidX - 20, bounds.Height - 30);
            buttons.Add(noDeviceCancelBounds);
            DrawButton(canvas, noDeviceCancelBounds, "CANCEL", _hoveredButton == buttonOffset, false);
            return;
        }

        var device = _vjoyDevices[_selectedVJoyDevice];

        // vJoy device selector
        FUIRenderer.DrawText(canvas, "vJoy Device:", new SKPoint(pad, y + 12), FUIColors.TextDim, 11f);

        var prevDeviceBtn = new SKRect(pad + 100, y, pad + 130, y + 28);
        var nextDeviceBtn = new SKRect(pad + 200, y, pad + 230, y + 28);
        buttons.Add(prevDeviceBtn);  // buttonOffset + 0
        buttons.Add(nextDeviceBtn);  // buttonOffset + 1

        DrawSmallButton(canvas, prevDeviceBtn, "<", _hoveredButton == buttonOffset, _selectedVJoyDevice > 0);
        FUIRenderer.DrawText(canvas, $"vJoy {device.Id}",
            new SKPoint(pad + 140, y + 18), FUIColors.TextBright, 12f);
        DrawSmallButton(canvas, nextDeviceBtn, ">", _hoveredButton == buttonOffset + 1, _selectedVJoyDevice < _vjoyDevices.Count - 1);

        y += 35;

        // Output index selector - handle Button, Axis, and Hat types
        string outputLabel = _detectedInput!.Type switch
        {
            InputType.Button => "Button:",
            InputType.Axis => "Axis:",
            InputType.Hat => "POV:",
            _ => "Output:"
        };
        int maxIndex = _detectedInput.Type switch
        {
            InputType.Button => device.ButtonCount,
            InputType.Axis => 8,
            InputType.Hat => Math.Max(device.ContPovCount, device.DiscPovCount),
            _ => 1
        };
        if (maxIndex < 1) maxIndex = 1;

        FUIRenderer.DrawText(canvas, outputLabel, new SKPoint(pad, y + 12), FUIColors.TextDim, 11f);

        var prevIndexBtn = new SKRect(pad + 100, y, pad + 130, y + 28);
        var nextIndexBtn = new SKRect(pad + 200, y, pad + 230, y + 28);
        buttons.Add(prevIndexBtn);  // buttonOffset + 2
        buttons.Add(nextIndexBtn);  // buttonOffset + 3

        DrawSmallButton(canvas, prevIndexBtn, "<", _hoveredButton == buttonOffset + 2, _selectedOutputIndex > 0);

        string indexText = _detectedInput.Type switch
        {
            InputType.Button => $"Button {_selectedOutputIndex + 1}",
            InputType.Axis => GetAxisName(_selectedOutputIndex),
            InputType.Hat => $"POV {_selectedOutputIndex + 1}",
            _ => $"{_selectedOutputIndex}"
        };
        FUIRenderer.DrawText(canvas, indexText,
            new SKPoint(pad + 140, y + 18), FUIColors.TextBright, 12f);

        DrawSmallButton(canvas, nextIndexBtn, ">", _hoveredButton == buttonOffset + 3, _selectedOutputIndex < maxIndex - 1);

        // Action buttons
        var cancelBounds = new SKRect(bounds.MidX - 120, bounds.Height - 60, bounds.MidX - 20, bounds.Height - 30);
        var createBounds = new SKRect(bounds.MidX + 20, bounds.Height - 60, bounds.MidX + 120, bounds.Height - 30);
        buttons.Add(cancelBounds);  // buttonOffset + 4
        buttons.Add(createBounds);  // buttonOffset + 5

        DrawButton(canvas, cancelBounds, "CANCEL", _hoveredButton == buttonOffset + 4, false);
        DrawButton(canvas, createBounds, "CREATE", _hoveredButton == buttonOffset + 5, true);
    }

    private void DrawKeyboardOutput(SKCanvas canvas, SKRect bounds, float y, float pad, List<SKRect> buttons, int buttonOffset)
    {
        // Key capture button/field - now shows full key combo
        FUIRenderer.DrawText(canvas, "Key Combo:", new SKPoint(pad, y + 12), FUIColors.TextDim, 11f);

        var keyCaptureBtn = new SKRect(pad + 85, y, bounds.Width - pad, y + 32);
        buttons.Add(keyCaptureBtn);  // buttonOffset + 0

        // Draw the key capture field
        using (var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = _waitingForKeyCapture ? FUIColors.Active.WithAlpha(40) : FUIColors.Background2
        })
        {
            canvas.DrawRect(keyCaptureBtn, bgPaint);
        }
        using (var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = _waitingForKeyCapture ? FUIColors.Active : (_hoveredButton == buttonOffset ? FUIColors.FrameBright : FUIColors.Frame),
            StrokeWidth = 1f
        })
        {
            canvas.DrawRect(keyCaptureBtn, framePaint);
        }

        // Display key combo (e.g., "LCtrl+LShift+A") or prompt
        string keyText = _waitingForKeyCapture ? "Press key combo (e.g., Ctrl+A)..." :
                         string.IsNullOrEmpty(_selectedKey) ? "Click to capture key combo" : GetKeyComboDisplayString();
        var keyColor = _waitingForKeyCapture ? FUIColors.Active :
                       string.IsNullOrEmpty(_selectedKey) ? FUIColors.TextDim : FUIColors.TextBright;
        FUIRenderer.DrawTextCentered(canvas, keyText, keyCaptureBtn, keyColor, 12f);

        y += 45;

        // Help text
        FUIRenderer.DrawText(canvas, "Hold modifiers (Ctrl, Shift, Alt) when pressing the key",
            new SKPoint(pad, y + 10), FUIColors.TextDisabled, 10f);

        // Action buttons
        var cancelBounds = new SKRect(bounds.MidX - 120, bounds.Height - 60, bounds.MidX - 20, bounds.Height - 30);
        var createBounds = new SKRect(bounds.MidX + 20, bounds.Height - 60, bounds.MidX + 120, bounds.Height - 30);
        buttons.Add(cancelBounds);  // buttonOffset + 1
        buttons.Add(createBounds);  // buttonOffset + 2

        DrawButton(canvas, cancelBounds, "CANCEL", _hoveredButton == buttonOffset + 1, false);
        bool canCreate = !string.IsNullOrEmpty(_selectedKey);
        DrawButton(canvas, createBounds, "CREATE", _hoveredButton == buttonOffset + 2, canCreate);
    }

    private void DrawModeTab(SKCanvas canvas, SKRect bounds, string text, bool isActive, bool isHovered)
    {
        var bgColor = isActive ? FUIColors.Primary.WithAlpha(60) : (isHovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);
        var frameColor = isActive ? FUIColors.Primary : (isHovered ? FUIColors.FrameBright : FUIColors.Frame);
        var textColor = isActive ? FUIColors.TextBright : FUIColors.TextDim;

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = frameColor,
            StrokeWidth = isActive ? 2f : 1f
        };
        canvas.DrawRect(bounds, framePaint);

        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 11f);
    }

    private void DrawCheckbox(SKCanvas canvas, SKRect bounds, string text, bool isChecked, bool isHovered)
    {
        var bgColor = isChecked ? FUIColors.Active.WithAlpha(60) : (isHovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);
        var frameColor = isChecked ? FUIColors.Active : (isHovered ? FUIColors.FrameBright : FUIColors.Frame);
        var textColor = isChecked ? FUIColors.TextBright : FUIColors.TextDim;

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = frameColor,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 10f);
    }

    private void DrawButton(SKCanvas canvas, SKRect bounds, string text, bool hovered, bool isPrimary)
    {
        var bgColor = isPrimary
            ? (hovered ? FUIColors.Active.WithAlpha(80) : FUIColors.Active.WithAlpha(50))
            : (hovered ? FUIColors.Primary.WithAlpha(40) : FUIColors.Background2);
        var frameColor = isPrimary
            ? (hovered ? FUIColors.Active : FUIColors.Active.WithAlpha(180))
            : (hovered ? FUIColors.FrameBright : FUIColors.Frame);
        var textColor = isPrimary ? FUIColors.TextBright : FUIColors.TextPrimary;

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = frameColor,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 11f);
    }

    private void DrawSmallButton(SKCanvas canvas, SKRect bounds, string text, bool hovered, bool enabled)
    {
        var bgColor = enabled
            ? (hovered ? FUIColors.Primary.WithAlpha(60) : FUIColors.Background2)
            : FUIColors.Background1;
        var frameColor = enabled
            ? (hovered ? FUIColors.FrameBright : FUIColors.Frame)
            : FUIColors.FrameDim;
        var textColor = enabled ? FUIColors.TextPrimary : FUIColors.TextDisabled;

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = frameColor,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 12f);
    }

    private string GetAxisName(int index)
    {
        return index switch
        {
            0 => "X Axis",
            1 => "Y Axis",
            2 => "Z Axis",
            3 => "RX Axis",
            4 => "RY Axis",
            5 => "RZ Axis",
            6 => "Slider 1",
            7 => "Slider 2",
            _ => $"Axis {index}"
        };
    }

    #endregion

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _renderTimer?.Stop();
        _renderTimer?.Dispose();
        _timeoutTimer?.Stop();
        _timeoutTimer?.Dispose();
        _detectionService?.Dispose();
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Key capture mode - capture any key press with its modifiers
        if (_waitingForKeyCapture && _state == MappingDialogState.SelectingOutput)
        {
            // Get the base key without modifiers
            var baseKey = keyData & Keys.KeyCode;

            // Check if this is a modifier-only key press
            bool isModifierOnly = baseKey == Keys.ControlKey || baseKey == Keys.ShiftKey ||
                baseKey == Keys.Menu || baseKey == Keys.Control ||
                baseKey == Keys.Shift || baseKey == Keys.Alt ||
                baseKey == Keys.LControlKey || baseKey == Keys.RControlKey ||
                baseKey == Keys.LShiftKey || baseKey == Keys.RShiftKey ||
                baseKey == Keys.LMenu || baseKey == Keys.RMenu;

            if (isModifierOnly)
            {
                // Capture just the modifier key itself (e.g., just LCtrl or just RShift)
                _selectedKey = GetModifierKeyName(baseKey);
                _capturedModifiers.Clear(); // No additional modifiers when capturing a modifier itself
                _waitingForKeyCapture = false;
                return true;
            }

            // Regular key - convert to friendly key name
            _selectedKey = KeyToString(baseKey);

            // Capture held modifiers using GetAsyncKeyState (detects left/right)
            _capturedModifiers = CaptureHeldModifiers();

            _waitingForKeyCapture = false;

            return true;
        }

        if (keyData == Keys.Escape)
        {
            if (_waitingForKeyCapture)
            {
                _waitingForKeyCapture = false;
                return true;
            }
            Cancel();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// Get the specific modifier key name (left/right variant)
    /// </summary>
    private string GetModifierKeyName(Keys key)
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

    private static string KeyToString(Keys key)
    {
        return key switch
        {
            // Letter keys
            >= Keys.A and <= Keys.Z => key.ToString(),

            // Number keys
            >= Keys.D0 and <= Keys.D9 => key.ToString().Substring(1), // Remove the 'D' prefix

            // Numpad
            >= Keys.NumPad0 and <= Keys.NumPad9 => $"Num{(int)key - (int)Keys.NumPad0}",
            Keys.Multiply => "NumMul",
            Keys.Add => "NumAdd",
            Keys.Subtract => "NumSub",
            Keys.Decimal => "NumDec",
            Keys.Divide => "NumDiv",

            // Function keys
            >= Keys.F1 and <= Keys.F24 => key.ToString(),

            // Common keys
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

            // Arrow keys
            Keys.Up => "Up",
            Keys.Down => "Down",
            Keys.Left => "Left",
            Keys.Right => "Right",

            // Punctuation
            Keys.OemMinus => "-",
            Keys.Oemplus => "=",
            Keys.OemOpenBrackets => "[",
            Keys.OemCloseBrackets => "]",
            Keys.OemPipe => "\\",
            Keys.OemSemicolon => ";",
            Keys.OemQuotes => "'",
            Keys.Oemcomma => ",",
            Keys.OemPeriod => ".",
            Keys.OemQuestion => "/",
            Keys.Oemtilde => "`",

            // Default
            _ => key.ToString()
        };
    }
}
