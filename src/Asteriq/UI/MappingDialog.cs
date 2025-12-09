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

    // UI state
    private int _hoveredButton = -1;
    private SKRect[] _buttonBounds = Array.Empty<SKRect>();

    public MappingDialogResult Result { get; private set; } = new();

    public MappingDialog(InputService inputService, VJoyService vjoyService)
    {
        _inputService = inputService;
        _vjoyService = vjoyService;
        _detectionService = new InputDetectionService(_inputService);
        _vjoyDevices = _vjoyService.EnumerateDevices();

        InitializeForm();
        InitializeCanvas();
        InitializeTimers();

        // Start waiting for input
        StartInputDetection();
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

    private async void StartInputDetection()
    {
        _state = MappingDialogState.WaitingForInput;
        _timeoutRemaining = 30;

        try
        {
            _detectedInput = await _detectionService.WaitForInputAsync(
                InputDetectionFilter.Buttons | InputDetectionFilter.Axes,
                axisThreshold: 0.5f,
                timeoutMs: 30000);

            if (_detectedInput != null)
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
        catch (Exception)
        {
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
        if (_detectedInput == null || _vjoyDevices.Count == 0)
        {
            Cancel();
            return;
        }

        var vjoyDevice = _vjoyDevices[_selectedVJoyDevice];

        Result.Success = true;
        Result.Input = _detectedInput;
        Result.Output = new OutputTarget
        {
            Type = _detectedInput.Type == InputType.Button ? OutputType.VJoyButton : OutputType.VJoyAxis,
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
        switch (_hoveredButton)
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
                // Clamp to max available
                var device = _vjoyDevices[_selectedVJoyDevice];
                int max = _detectedInput?.Type == InputType.Button ? device.ButtonCount : 8;
                if (_selectedOutputIndex >= max) _selectedOutputIndex = max - 1;
                break;
            case 4: // Cancel
                Cancel();
                break;
            case 5: // Create mapping
                Complete();
                break;
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
            "PRESS A BUTTON OR MOVE AN AXIS",
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
        if (_detectedInput == null || _vjoyDevices.Count == 0)
        {
            FUIRenderer.DrawTextCentered(canvas, "No vJoy devices available",
                new SKRect(0, bounds.Height / 2, bounds.Width, bounds.Height / 2 + 20),
                FUIColors.Warning, 14f);
            return;
        }

        float y = 60f;
        float pad = 20f;

        // Detected input info
        FUIRenderer.DrawText(canvas, "INPUT DETECTED", new SKPoint(pad, y), FUIColors.Active, 12f);
        y += 25;

        var inputFrame = new SKRect(pad, y, bounds.Width - pad, y + 50);
        using var inputBgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Active.WithAlpha(30)
        };
        canvas.DrawRect(inputFrame, inputBgPaint);
        using var inputFramePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active,
            StrokeWidth = 1f
        };
        canvas.DrawRect(inputFrame, inputFramePaint);

        FUIRenderer.DrawText(canvas, _detectedInput.ToString(),
            new SKPoint(pad + 10, y + 20), FUIColors.TextBright, 13f, true);

        string inputTypeStr = _detectedInput.Type == InputType.Button ? "BUTTON" :
                              _detectedInput.Type == InputType.Axis ? "AXIS" : "HAT";
        FUIRenderer.DrawText(canvas, inputTypeStr,
            new SKPoint(pad + 10, y + 38), FUIColors.TextDim, 11f);

        y += 70;

        // Output selection
        FUIRenderer.DrawText(canvas, "OUTPUT TARGET", new SKPoint(pad, y), FUIColors.Primary, 12f);
        y += 25;

        var buttons = new List<SKRect>();
        var device = _vjoyDevices[_selectedVJoyDevice];

        // vJoy device selector
        FUIRenderer.DrawText(canvas, "vJoy Device:", new SKPoint(pad, y + 12), FUIColors.TextDim, 11f);

        var prevDeviceBtn = new SKRect(pad + 100, y, pad + 130, y + 28);
        var nextDeviceBtn = new SKRect(pad + 200, y, pad + 230, y + 28);
        buttons.Add(prevDeviceBtn);
        buttons.Add(nextDeviceBtn);

        DrawSmallButton(canvas, prevDeviceBtn, "<", _hoveredButton == 0, _selectedVJoyDevice > 0);
        FUIRenderer.DrawText(canvas, $"vJoy {device.Id}",
            new SKPoint(pad + 140, y + 18), FUIColors.TextBright, 12f);
        DrawSmallButton(canvas, nextDeviceBtn, ">", _hoveredButton == 1, _selectedVJoyDevice < _vjoyDevices.Count - 1);

        y += 35;

        // Output index selector
        string outputLabel = _detectedInput.Type == InputType.Button ? "Button:" : "Axis:";
        int maxIndex = _detectedInput.Type == InputType.Button ? device.ButtonCount : 8;

        FUIRenderer.DrawText(canvas, outputLabel, new SKPoint(pad, y + 12), FUIColors.TextDim, 11f);

        var prevIndexBtn = new SKRect(pad + 100, y, pad + 130, y + 28);
        var nextIndexBtn = new SKRect(pad + 200, y, pad + 230, y + 28);
        buttons.Add(prevIndexBtn);
        buttons.Add(nextIndexBtn);

        DrawSmallButton(canvas, prevIndexBtn, "<", _hoveredButton == 2, _selectedOutputIndex > 0);

        string indexText = _detectedInput.Type == InputType.Button
            ? $"Button {_selectedOutputIndex + 1}"
            : GetAxisName(_selectedOutputIndex);
        FUIRenderer.DrawText(canvas, indexText,
            new SKPoint(pad + 140, y + 18), FUIColors.TextBright, 12f);

        DrawSmallButton(canvas, nextIndexBtn, ">", _hoveredButton == 3, _selectedOutputIndex < maxIndex - 1);

        // Action buttons
        var cancelBounds = new SKRect(bounds.MidX - 120, bounds.Height - 60, bounds.MidX - 20, bounds.Height - 30);
        var createBounds = new SKRect(bounds.MidX + 20, bounds.Height - 60, bounds.MidX + 120, bounds.Height - 30);
        buttons.Add(cancelBounds);
        buttons.Add(createBounds);

        _buttonBounds = buttons.ToArray();

        DrawButton(canvas, cancelBounds, "CANCEL", _hoveredButton == 4, false);
        DrawButton(canvas, createBounds, "CREATE", _hoveredButton == 5, true);
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
        if (keyData == Keys.Escape)
        {
            Cancel();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
