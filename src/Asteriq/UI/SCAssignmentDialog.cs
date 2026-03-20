using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Asteriq.UI;

/// <summary>
/// FUI-styled dialog for assigning SC actions to joystick inputs.
/// Works with both vJoy devices and physical devices.
/// </summary>
public class SCAssignmentDialog : FUIBaseDialog
{
    private readonly SCAction _action;

    /// <summary>
    /// Generic device entry for the selector.
    /// </summary>
    private readonly record struct DeviceEntry(string DisplayName, uint VJoyId, int PhysicalIndex);

    private readonly List<DeviceEntry> _devices;
    private readonly bool _isPhysicalMode;

    // CA2213: SKControl is a WinForms child control — disposed automatically via Controls collection
#pragma warning disable CA2213
    private SKControl _canvas = null!;
#pragma warning restore CA2213
    private System.Windows.Forms.Timer _renderTimer = null!;

    // Dialog state
    private int _selectedDevice = 0;
    private int _selectedInputIndex = 0; // Index in the combined list of axes + buttons
    private bool _inverted = false;

    // UI state
    private int _hoveredButton = -1;
    private SKRect[] _buttonBounds = Array.Empty<SKRect>();
    private SKPoint _mousePosition;

    // Result
    public uint SelectedVJoyId { get; private set; }
    public int SelectedPhysicalIndex { get; private set; } = -1;
    public string SelectedInputName { get; private set; } = "";
    public bool IsInverted => _inverted;

    /// <summary>
    /// Constructor for vJoy devices (existing behavior)
    /// </summary>
    public SCAssignmentDialog(SCAction action, List<VJoyDeviceInfo> availableVJoy)
    {
        _action = action;
        _isPhysicalMode = false;
        _devices = availableVJoy.Select(v => new DeviceEntry($"vJoy {v.Id}", v.Id, -1)).ToList();

        InitializeForm();
        InitializeCanvas();
        InitializeTimer();

        _selectedInputIndex = action.InputType == SCInputType.Axis ? 0 : 9;
    }

    /// <summary>
    /// Constructor for physical devices (no vJoy)
    /// </summary>
    public SCAssignmentDialog(SCAction action, List<PhysicalDeviceInfo> physicalDevices)
    {
        _action = action;
        _isPhysicalMode = true;
        _devices = physicalDevices.Select((d, idx) => new DeviceEntry(d.Name, 0, idx)).ToList();

        InitializeForm();
        InitializeCanvas();
        InitializeTimer();

        _selectedInputIndex = action.InputType == SCInputType.Axis ? 0 : 9;
    }

    private void InitializeForm()
    {
        Text = $"Assign: {_action.ActionName}";
        float s = FUIRenderer.CanvasScaleFactor;
        Size = new Size((int)(450 * s), (int)(300 * s));
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

    private void InitializeTimer()
    {
        _renderTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _renderTimer.Tick += (s, e) => _canvas.Invalidate();
        _renderTimer.Start();
    }

    private void OnCanvasMouseMove(object? sender, MouseEventArgs e)
    {
        float s = FUIRenderer.CanvasScaleFactor;
        float mx = e.X / s, my = e.Y / s;
        _mousePosition = new SKPoint(mx, my);
        _hoveredButton = -1;
        for (int i = 0; i < _buttonBounds.Length; i++)
        {
            if (_buttonBounds[i].Contains(mx, my))
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

        HandleClick();
    }

    private void HandleClick()
    {
        switch (_hoveredButton)
        {
            case 0: // Previous device
                if (_selectedDevice > 0) _selectedDevice--;
                break;
            case 1: // Next device
                if (_selectedDevice < _devices.Count - 1) _selectedDevice++;
                break;
            case 2: // Previous input
                if (_selectedInputIndex > 0) _selectedInputIndex--;
                break;
            case 3: // Next input
                // Total: 8 axes + 1 separator + 32 buttons = 41 items
                if (_selectedInputIndex < 40) _selectedInputIndex++;
                break;
            case 4: // Invert checkbox (only for axes)
                if (_action.InputType == SCInputType.Axis)
                {
                    _inverted = !_inverted;
                }
                break;
            case 5: // Cancel
                DialogResult = DialogResult.Cancel;
                Close();
                break;
            case 6: // Assign
                Complete();
                break;
        }
    }

    private void Complete()
    {
        var inputName = GetInputNameAtIndex(_selectedInputIndex);

        // Skip if separator
        if (inputName == "---")
            return;

        var entry = _devices[_selectedDevice];
        SelectedVJoyId = entry.VJoyId;
        SelectedPhysicalIndex = entry.PhysicalIndex;
        SelectedInputName = inputName;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string GetInputNameAtIndex(int index)
    {
        if (index < 8)
        {
            return new[] { "x", "y", "z", "rx", "ry", "rz", "slider1", "slider2" }[index];
        }
        else if (index == 8)
        {
            return "---"; // Separator
        }
        else
        {
            return $"button{index - 8}"; // button1, button2, ..., button32
        }
    }

    private static string GetInputDisplayName(int index)
    {
        var name = GetInputNameAtIndex(index);
        if (name == "---") return "---";
        if (name.StartsWith("button")) return name;

        return name switch
        {
            "x" => "X Axis",
            "y" => "Y Axis",
            "z" => "Z Axis",
            "rx" => "RX Axis",
            "ry" => "RY Axis",
            "rz" => "RZ Axis",
            "slider1" => "Slider 1",
            "slider2" => "Slider 2",
            _ => name
        };
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        float scale = FUIRenderer.CanvasScaleFactor;
        canvas.Scale(scale);
        var bounds = new SKRect(0, 0, e.Info.Width / scale, e.Info.Height / scale);

        canvas.Clear(FUIColors.Void);

        // Background gradient
        using (var bgPaint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, bounds.Height),
                new[] { FUIColors.Background1, FUIColors.Void },
                null,
                SKShaderTileMode.Clamp)
        })
        {
            canvas.DrawRect(bounds, bgPaint);
        }

        // Subtle grid
        FUIRenderer.DrawDotGrid(canvas, bounds, 20f, FUIColors.Grid.WithAlpha(30));

        // Draw content
        DrawDialogContent(canvas, bounds);

        // Dialog frame
        DrawDialogFrame(canvas, bounds);
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
        using var framePaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame, 2f);
        canvas.DrawRect(bounds.Inset(1, 1), framePaint);

        // Title bar
        float titleHeight = 40f;
        using var titleBgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background2.WithAlpha(200));
        canvas.DrawRect(new SKRect(0, 0, bounds.Width, titleHeight), titleBgPaint);

        string title = $"ASSIGN: {_action.ActionName.ToUpper()}";
        FUIRenderer.DrawText(canvas, title, new SKPoint(15, 26), FUIColors.Primary, 15f, true);
    }

    private void DrawDialogContent(SKCanvas canvas, SKRect bounds)
    {
        float y = 60f;
        float pad = 20f;
        var buttons = new List<SKRect>();

        if (_devices.Count == 0)
        {
            string noDeviceMsg = _isPhysicalMode
                ? "No joystick devices connected"
                : "No vJoy devices available";
            FUIRenderer.DrawText(canvas, noDeviceMsg,
                new SKPoint(pad, y), FUIColors.Warning, 15f);

            var noDeviceCancelBounds = new SKRect(bounds.MidX - 50, bounds.Height - 60, bounds.MidX + 50, bounds.Height - 30);
            buttons.Add(noDeviceCancelBounds);
            FUIRenderer.DrawButton(canvas, noDeviceCancelBounds, "CANCEL", _hoveredButton == 0 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
            _buttonBounds = buttons.ToArray();
            return;
        }

        var device = _devices[_selectedDevice];

        // Device selector
        string deviceLabel = _isPhysicalMode ? "Device:" : "vJoy Device:";
        FUIRenderer.DrawText(canvas, deviceLabel, new SKPoint(pad, y + 12), FUIColors.TextDim, 14f);

        float labelWidth = _isPhysicalMode ? 70f : 110f;
        var prevDeviceBtn = new SKRect(pad + labelWidth, y, pad + labelWidth + 30, y + 28);
        var nextDeviceBtn = new SKRect(pad + 280, y, pad + 310, y + 28);
        buttons.Add(prevDeviceBtn);  // 0
        buttons.Add(nextDeviceBtn);  // 1

        FUIRenderer.DrawButton(canvas, prevDeviceBtn, "<", _selectedDevice > 0 ? (_hoveredButton == 0 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal) : FUIRenderer.ButtonState.Disabled);

        // Truncate device display name to fit between the < > buttons
        float nameAreaLeft = pad + labelWidth + 38;
        float nameAreaRight = pad + 272;
        float nameAreaWidth = nameAreaRight - nameAreaLeft;
        string displayName = device.DisplayName;
        float nameWidth = FUIRenderer.MeasureText(displayName, 14f);
        if (nameWidth > nameAreaWidth)
        {
            // Truncate with ".."
            while (displayName.Length > 3 && FUIRenderer.MeasureText(displayName + "..", 14f) > nameAreaWidth)
                displayName = displayName[..^1];
            displayName += "..";
        }

        FUIRenderer.DrawTextCentered(canvas, displayName,
            new SKRect(nameAreaLeft, y, nameAreaRight, y + 28), FUIColors.TextBright, 14f);
        FUIRenderer.DrawButton(canvas, nextDeviceBtn, ">", _selectedDevice < _devices.Count - 1 ? (_hoveredButton == 1 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal) : FUIRenderer.ButtonState.Disabled);

        y += 44;

        // Input selector
        string inputLabel = _action.InputType == SCInputType.Axis ? "Axis:" : "Button:";
        FUIRenderer.DrawText(canvas, inputLabel, new SKPoint(pad, y + 12), FUIColors.TextDim, 14f);

        var prevInputBtn = new SKRect(pad + 110, y, pad + 140, y + 28);
        var nextInputBtn = new SKRect(pad + 280, y, pad + 310, y + 28);
        buttons.Add(prevInputBtn);  // 2
        buttons.Add(nextInputBtn);  // 3

        FUIRenderer.DrawButton(canvas, prevInputBtn, "<", _selectedInputIndex > 0 ? (_hoveredButton == 2 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal) : FUIRenderer.ButtonState.Disabled);
        string inputDisplay = GetInputDisplayName(_selectedInputIndex);
        FUIRenderer.DrawTextCentered(canvas, inputDisplay,
            new SKRect(pad + 150, y, pad + 270, y + 28), FUIColors.TextBright, 15f);
        FUIRenderer.DrawButton(canvas, nextInputBtn, ">", _selectedInputIndex < 40 ? (_hoveredButton == 3 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal) : FUIRenderer.ButtonState.Disabled);

        y += 44;

        // Invert checkbox (only for axes)
        if (_action.InputType == SCInputType.Axis)
        {
            var checkboxBounds = new SKRect(pad, y, pad + 120, y + 28);
            buttons.Add(checkboxBounds);  // 4
            var checkboxBoxBounds = new SKRect(pad, y + 4, pad + 20, y + 24);
            FUIWidgets.DrawSCCheckbox(canvas, checkboxBoxBounds, _inverted, _hoveredButton == 4);
            FUIRenderer.DrawText(canvas, "Inverted", new SKPoint(pad + 26, y + 18),
                _inverted ? FUIColors.TextBright : FUIColors.TextDim, 13f);
            y += 36;
        }
        else
        {
            // Add placeholder button to keep indices consistent
            buttons.Add(SKRect.Empty); // 4
        }

        // Action buttons
        var cancelBounds = new SKRect(bounds.MidX - 120, bounds.Height - 60, bounds.MidX - 20, bounds.Height - 30);
        var assignBounds = new SKRect(bounds.MidX + 20, bounds.Height - 60, bounds.MidX + 120, bounds.Height - 30);
        buttons.Add(cancelBounds);  // 5
        buttons.Add(assignBounds);  // 6

        FUIRenderer.DrawButton(canvas, cancelBounds, "CANCEL", _hoveredButton == 5 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
        FUIRenderer.DrawButton(canvas, assignBounds, "ASSIGN", _hoveredButton == 6 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

        _buttonBounds = buttons.ToArray();
    }


    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _renderTimer?.Stop();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _renderTimer?.Dispose();
        base.Dispose(disposing);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
