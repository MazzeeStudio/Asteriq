using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Asteriq.UI;

/// <summary>
/// Dialog for configuring a vJoy device's axes, buttons, and POVs.
/// Used during device creation and from the CONFIGURE button in the Devices tab.
/// </summary>
public class VJoyConfigDialog : FUIBaseDialog
{
    private const float TitleBarH = 36f;
    private const float LogicalW = 360f;
    private const float SectionGap = 14f;
    private const float CheckboxSize = 16f;
    private const float RowH = 26f;

    // Result
    public List<string> AxisFlags { get; private set; } = new();
    public int ButtonCount { get; private set; } = 32;
    public int PovCount { get; private set; }
    public bool PovContinuous { get; private set; }

    // State
    private readonly string _deviceName;
    private bool _axisX = true, _axisY = true, _axisZ = true;
    private bool _axisRX, _axisRY, _axisRZ;
    private bool _axisSlider, _axisDial;
    private int _buttonCount = 32;
    private int _povCount;
    private bool _povContinuous;

    // UI bounds — axes
    private SKRect _axisXBounds, _axisYBounds, _axisZBounds;
    private SKRect _axisRXBounds, _axisRYBounds, _axisRZBounds;
    private SKRect _axisSliderBounds, _axisDialBounds;
    // UI bounds — buttons + POV (same row)
    private SKRect _buttonMinusBounds, _buttonPlusBounds;
    private SKRect _povMinusBounds, _povPlusBounds;
    private SKRect _povTypeBounds;
    // UI bounds — action buttons
    private SKRect _applyBounds, _cancelBounds;
    private int _hoveredRegion = -1;

    // Dragging
    private bool _isDragging;
    private Point _dragStart;

#pragma warning disable CA2213
    private readonly SKControl _canvas;
#pragma warning restore CA2213

    private VJoyConfigDialog(string deviceName, List<string> currentFlags, int buttonCount, int povCount, bool povContinuous)
    {
        _deviceName = deviceName;
        _buttonCount = Math.Max(1, buttonCount);
        _povCount = Math.Clamp(povCount, 0, 4);
        _povContinuous = povContinuous;

        // Parse current axis flags
        _axisX = false; _axisY = false; _axisZ = false;
        foreach (var flag in currentFlags)
        {
            switch (flag.ToUpperInvariant())
            {
                case "X": _axisX = true; break;
                case "Y": _axisY = true; break;
                case "Z": _axisZ = true; break;
                case "RX": _axisRX = true; break;
                case "RY": _axisRY = true; break;
                case "RZ": _axisRZ = true; break;
                case "SL0": _axisSlider = true; break;
                case "SL1": _axisDial = true; break;
            }
        }

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(6, 8, 10);
        ShowInTaskbar = false;
        KeyPreview = true;

        float logicalH = CalculateHeight();
        float s = FUIRenderer.CanvasScaleFactor;
        ClientSize = new Size((int)(LogicalW * s), (int)(logicalH * s));

        _canvas = new SKControl { Dock = DockStyle.Fill };
        _canvas.PaintSurface += OnPaintSurface;
        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseDown += OnMouseDown;
        _canvas.MouseUp += OnMouseUp;
        _canvas.MouseLeave += (_, _) => { _hoveredRegion = -1; Cursor = Cursors.Default; _canvas.Invalidate(); };
        Controls.Add(_canvas);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            else if (e.KeyCode == Keys.Enter) { Apply(); }
        };
    }

    private static float CalculateHeight()
    {
        // Title + device name + axes (4 rows) + section labels + buttons/POV row + POV type + action buttons + padding
        return TitleBarH + 24f + 20f + (4 * RowH) + SectionGap + 20f + RowH + SectionGap + 20f + RowH + SectionGap + 36f + ContentPadding * 2;
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        float scale = FUIRenderer.CanvasScaleFactor;
        canvas.Scale(scale);
        var b = new SKRect(0, 0, e.Info.Width / scale, e.Info.Height / scale);

        canvas.Clear(FUIColors.Background0);

        // Frame
        FUIRenderer.DrawFrame(canvas, b.Inset(-1, -1), FUIColors.Frame, FUIRenderer.ChamferSize);

        // Title bar
        var titleBar = new SKRect(b.Left + 2, b.Top + 2, b.Right - 2, b.Top + TitleBarH);
        using (var titleBg = FUIRenderer.CreateFillPaint(FUIColors.Background2))
            canvas.DrawRect(titleBar, titleBg);
        using (var sep = FUIRenderer.CreateStrokePaint(FUIColors.Frame))
            canvas.DrawLine(titleBar.Left, titleBar.Bottom, titleBar.Right, titleBar.Bottom, sep);
        FUIRenderer.DrawText(canvas, "VJOY CONFIGURATION", new SKPoint(ContentPadding, titleBar.MidY + 5),
            FUIColors.TextBright, 14f);

        float pad = ContentPadding;
        float x = pad;
        float y = TitleBarH + ContentPadding;
        float contentW = b.Width - pad * 2;
        float col1 = x;
        float col2 = x + contentW / 2;

        // Device name
        FUIRenderer.DrawText(canvas, _deviceName, new SKPoint(x, y + 12f), FUIColors.Active, 12f);
        y += 24f;

        // ── AXES ──
        FUIRenderer.DrawText(canvas, "AXES", new SKPoint(x, y + 12f), FUIColors.TextDim, 12f, true);
        y += 20f;

        // Row 1: X, RX
        _axisXBounds = new SKRect(col1, y, col1 + CheckboxSize, y + CheckboxSize);
        FUIWidgets.DrawCheckboxWithLabel(canvas, _axisXBounds, _axisX, _hoveredRegion == 0, "X", 12f);
        _axisRXBounds = new SKRect(col2, y, col2 + CheckboxSize, y + CheckboxSize);
        FUIWidgets.DrawCheckboxWithLabel(canvas, _axisRXBounds, _axisRX, _hoveredRegion == 3, "RX", 12f);
        y += RowH;

        // Row 2: Y, RY
        _axisYBounds = new SKRect(col1, y, col1 + CheckboxSize, y + CheckboxSize);
        FUIWidgets.DrawCheckboxWithLabel(canvas, _axisYBounds, _axisY, _hoveredRegion == 1, "Y", 12f);
        _axisRYBounds = new SKRect(col2, y, col2 + CheckboxSize, y + CheckboxSize);
        FUIWidgets.DrawCheckboxWithLabel(canvas, _axisRYBounds, _axisRY, _hoveredRegion == 4, "RY", 12f);
        y += RowH;

        // Row 3: Z, RZ
        _axisZBounds = new SKRect(col1, y, col1 + CheckboxSize, y + CheckboxSize);
        FUIWidgets.DrawCheckboxWithLabel(canvas, _axisZBounds, _axisZ, _hoveredRegion == 2, "Z", 12f);
        _axisRZBounds = new SKRect(col2, y, col2 + CheckboxSize, y + CheckboxSize);
        FUIWidgets.DrawCheckboxWithLabel(canvas, _axisRZBounds, _axisRZ, _hoveredRegion == 5, "RZ", 12f);
        y += RowH;

        // Row 4: Slider, Dial/Slider2
        _axisSliderBounds = new SKRect(col1, y, col1 + CheckboxSize, y + CheckboxSize);
        FUIWidgets.DrawCheckboxWithLabel(canvas, _axisSliderBounds, _axisSlider, _hoveredRegion == 6, "Slider", 12f);
        _axisDialBounds = new SKRect(col2, y, col2 + CheckboxSize, y + CheckboxSize);
        FUIWidgets.DrawCheckboxWithLabel(canvas, _axisDialBounds, _axisDial, _hoveredRegion == 7, "Dial/Slider2", 12f);
        y += RowH + SectionGap;

        // ── BUTTONS + POV HATS (same row) ──
        float halfW = (contentW - 12f) / 2;

        // Buttons label + input
        FUIRenderer.DrawText(canvas, "BUTTONS", new SKPoint(x, y + 12f), FUIColors.TextDim, 12f, true);
        FUIRenderer.DrawText(canvas, "POV HATS", new SKPoint(x + halfW + 12f, y + 12f), FUIColors.TextDim, 12f, true);
        y += 20f;

        DrawNumberInput(canvas, x, y, halfW, _buttonCount, 1, 128,
            ref _buttonMinusBounds, ref _buttonPlusBounds, 10, 11);
        DrawNumberInput(canvas, x + halfW + 12f, y, halfW, _povCount, 0, 4,
            ref _povMinusBounds, ref _povPlusBounds, 12, 13);
        y += RowH + SectionGap;

        // POV type toggle (only if POV > 0)
        if (_povCount > 0)
        {
            FUIRenderer.DrawText(canvas, "POV TYPE", new SKPoint(x, y + 12f), FUIColors.TextDim, 12f, true);
            y += 20f;
            _povTypeBounds = new SKRect(x, y, x + contentW, y + RowH);
            string typeLabel = _povContinuous ? "Continuous" : "4 Directions";
            var typeState = _hoveredRegion == 14
                ? FUIRenderer.ButtonState.Hover
                : FUIRenderer.ButtonState.Normal;
            FUIRenderer.DrawButton(canvas, _povTypeBounds, typeLabel, typeState, false, 12f);
            y += RowH + SectionGap;
        }
        else
        {
            _povTypeBounds = SKRect.Empty;
        }

        // ── Action buttons ──
        float btnW = 100f;
        float btnH = 32f;
        float btnY = b.Bottom - ContentPadding - btnH;
        _cancelBounds = new SKRect(x, btnY, x + btnW, btnY + btnH);
        _applyBounds = new SKRect(b.Right - pad - btnW, btnY, b.Right - pad, btnY + btnH);

        FUIRenderer.DrawButton(canvas, _cancelBounds, "CANCEL",
            _hoveredRegion == 20 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
        FUIRenderer.DrawButton(canvas, _applyBounds, "APPLY",
            _hoveredRegion == 21 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

        // Corner decorations
        FUIRenderer.DrawLCornerFrame(canvas, b.Inset(-4, -4), FUIColors.Frame.WithAlpha(100), 20f, 6f, 1f);
    }

    private void DrawNumberInput(SKCanvas canvas, float x, float y, float width, int value,
        int min, int max, ref SKRect minusBounds, ref SKRect plusBounds, int minusRegion, int plusRegion)
    {
        float btnW = 32f;
        float h = RowH;

        minusBounds = new SKRect(x, y, x + btnW, y + h);
        plusBounds = new SKRect(x + width - btnW, y, x + width, y + h);

        bool canDecrease = value > min;
        bool canIncrease = value < max;

        FUIRenderer.DrawButton(canvas, minusBounds, "-",
            !canDecrease ? FUIRenderer.ButtonState.Disabled
            : _hoveredRegion == minusRegion ? FUIRenderer.ButtonState.Hover
            : FUIRenderer.ButtonState.Normal, false, 14f);

        FUIRenderer.DrawButton(canvas, plusBounds, "+",
            !canIncrease ? FUIRenderer.ButtonState.Disabled
            : _hoveredRegion == plusRegion ? FUIRenderer.ButtonState.Hover
            : FUIRenderer.ButtonState.Normal, false, 14f);

        // Value display in the middle
        var valueBounds = new SKRect(x + btnW, y, x + width - btnW, y + h);
        using (var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background1))
            canvas.DrawRect(valueBounds, bgPaint);
        using (var borderPaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame))
            canvas.DrawRect(valueBounds, borderPaint);
        FUIRenderer.DrawTextCentered(canvas, value.ToString(), valueBounds, FUIColors.TextBright, 13f);
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        float s = FUIRenderer.CanvasScaleFactor;
        var pt = new SKPoint(e.X / s, e.Y / s);
        int newHovered = HitTest(pt);

        if (newHovered != _hoveredRegion)
        {
            _hoveredRegion = newHovered;
            Cursor = newHovered >= 0 ? Cursors.Hand : Cursors.Default;
            _canvas.Invalidate();
        }

        if (_isDragging)
        {
            var screen = PointToScreen(e.Location);
            Location = new Point(screen.X - _dragStart.X, screen.Y - _dragStart.Y);
        }
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        float s = FUIRenderer.CanvasScaleFactor;
        if (e.Y / s < TitleBarH)
        {
            _isDragging = true;
            _dragStart = e.Location;
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        _isDragging = false;
        if (e.Button != MouseButtons.Left) return;

        switch (_hoveredRegion)
        {
            case 0: _axisX = !_axisX; break;
            case 1: _axisY = !_axisY; break;
            case 2: _axisZ = !_axisZ; break;
            case 3: _axisRX = !_axisRX; break;
            case 4: _axisRY = !_axisRY; break;
            case 5: _axisRZ = !_axisRZ; break;
            case 6: _axisSlider = !_axisSlider; break;
            case 7: _axisDial = !_axisDial; break;
            case 10: if (_buttonCount > 1) _buttonCount--; break;
            case 11: if (_buttonCount < 128) _buttonCount++; break;
            case 12: if (_povCount > 0) _povCount--; break;
            case 13: if (_povCount < 4) _povCount++; break;
            case 14: _povContinuous = !_povContinuous; break;
            case 20: DialogResult = DialogResult.Cancel; Close(); return;
            case 21: Apply(); return;
            default: return;
        }
        _canvas.Invalidate();
    }

    private int HitTest(SKPoint pt)
    {
        if (HitCheckbox(_axisXBounds, pt, "X")) return 0;
        if (HitCheckbox(_axisYBounds, pt, "Y")) return 1;
        if (HitCheckbox(_axisZBounds, pt, "Z")) return 2;
        if (HitCheckbox(_axisRXBounds, pt, "RX")) return 3;
        if (HitCheckbox(_axisRYBounds, pt, "RY")) return 4;
        if (HitCheckbox(_axisRZBounds, pt, "RZ")) return 5;
        if (HitCheckbox(_axisSliderBounds, pt, "Slider")) return 6;
        if (HitCheckbox(_axisDialBounds, pt, "Dial/Slider2")) return 7;
        if (_buttonMinusBounds.Contains(pt)) return 10;
        if (_buttonPlusBounds.Contains(pt)) return 11;
        if (_povMinusBounds.Contains(pt)) return 12;
        if (_povPlusBounds.Contains(pt)) return 13;
        if (!_povTypeBounds.IsEmpty && _povTypeBounds.Contains(pt)) return 14;
        if (_cancelBounds.Contains(pt)) return 20;
        if (_applyBounds.Contains(pt)) return 21;
        return -1;
    }

    private static bool HitCheckbox(SKRect cbBounds, SKPoint pt, string label)
    {
        float labelW = FUIRenderer.MeasureText(label, 12f);
        var expandedBounds = new SKRect(cbBounds.Left, cbBounds.Top,
            cbBounds.Right + 7f + labelW, cbBounds.Bottom);
        return expandedBounds.Contains(pt);
    }

    private void Apply()
    {
        var flags = new List<string>();
        if (_axisX) flags.Add("X");
        if (_axisY) flags.Add("Y");
        if (_axisZ) flags.Add("Z");
        if (_axisRX) flags.Add("RX");
        if (_axisRY) flags.Add("RY");
        if (_axisRZ) flags.Add("RZ");
        if (_axisSlider) flags.Add("SL0");
        if (_axisDial) flags.Add("SL1");

        if (flags.Count == 0 && _buttonCount == 0)
        {
            FUIMessageBox.ShowWarning(this, "At least one axis or button must be enabled.", "Invalid Configuration");
            return;
        }

        AxisFlags = flags;
        ButtonCount = _buttonCount;
        PovCount = _povCount;
        PovContinuous = _povContinuous;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// Shows the vJoy configuration dialog, optionally pre-populated from existing config.
    /// Returns null if cancelled.
    /// </summary>
    public static VJoyConfigDialog? ShowConfig(IWin32Window? owner, string deviceName,
        List<string>? currentFlags = null, int buttonCount = 32, int povCount = 0, bool povContinuous = false)
    {
        using var dialog = new VJoyConfigDialog(deviceName, currentFlags ?? new(), buttonCount, povCount, povContinuous);
        if (dialog.ShowDialog(owner) == DialogResult.OK)
            return dialog;
        return null;
    }
}
