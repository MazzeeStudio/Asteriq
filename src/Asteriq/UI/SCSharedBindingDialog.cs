using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Asteriq.UI;

/// <summary>
/// Result of the shared binding dialog
/// </summary>
public enum SCSharedBindingResult
{
    Cancel,     // User cancelled - don't apply the binding
    Share,      // Reroute physical button so both inputs trigger the primary binding
    Replace     // Remove the existing binding and assign new one normally
}

/// <summary>
/// FUI-styled dialog shown when the user assigns an SC action to a second joystick device
/// that already has a binding on a different joystick device.
/// Allows the user to Share (reroute), Replace, or Cancel.
/// </summary>
public class SCSharedBindingDialog : FUIBaseDialog
{
    private readonly string _actionDisplayName;
    private readonly string _primaryDeviceLabel;
    private readonly string _primaryInputDisplay;
    private readonly string _secondaryDeviceLabel;
    private readonly string _secondaryInputDisplay;
    private readonly List<string> _affectedActionNames;
    // CA2213: SKControl is a WinForms child control — disposed automatically via Controls collection
#pragma warning disable CA2213
    private SKControl _canvas = null!;
#pragma warning restore CA2213

    private SKRect _cancelButtonBounds;
    private SKRect _shareButtonBounds;
    private SKRect _replaceButtonBounds;
    private int _hoveredButton = -1; // 0=Cancel, 1=Share, 2=Replace

    // Dragging support
    private bool _isDragging;
    private Point _dragStart;

    public SCSharedBindingResult Result { get; private set; } = SCSharedBindingResult.Cancel;

    public SCSharedBindingDialog(
        string actionDisplayName,
        string primaryDeviceLabel,
        string primaryInputDisplay,
        string secondaryDeviceLabel,
        string secondaryInputDisplay,
        List<string>? affectedActionNames = null)
    {
        _actionDisplayName = actionDisplayName;
        _primaryDeviceLabel = primaryDeviceLabel;
        _primaryInputDisplay = primaryInputDisplay;
        _secondaryDeviceLabel = secondaryDeviceLabel;
        _secondaryInputDisplay = secondaryInputDisplay;
        _affectedActionNames = affectedActionNames ?? new();

        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "Shared Binding";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(6, 8, 10);
        ShowInTaskbar = false;
        KeyPreview = true;

        float s = FUIRenderer.CanvasScaleFactor;
        int extraHeight = _affectedActionNames.Count > 0 ? Math.Min(_affectedActionNames.Count, 8) * 16 + 28 : 0;
        ClientSize = new Size((int)(480 * s), (int)((230 + extraHeight) * s));

        _canvas = new SKControl { Dock = DockStyle.Fill };
        _canvas.PaintSurface += OnPaintSurface;
        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseDown += OnMouseDown;
        _canvas.MouseUp += OnMouseUp;
        _canvas.MouseLeave += OnMouseLeave;
        Controls.Add(_canvas);

        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Result = SCSharedBindingResult.Cancel;
                Close();
            }
        };
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        float scale = FUIRenderer.CanvasScaleFactor;
        canvas.Scale(scale);
        var bounds = new SKRect(0, 0, e.Info.Width / scale, e.Info.Height / scale);

        canvas.Clear(FUIColors.Background0);

        FUIRenderer.DrawFrame(canvas, bounds.Inset(-1, -1), FUIColors.Frame, FUIRenderer.ChamferSize);

        // Title bar with primary-color tint (blue/active, not amber warning)
        var titleBarBounds = new SKRect(bounds.Left + 2, bounds.Top + 2, bounds.Right - 2, bounds.Top + 40);
        using var titleBgPaint = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(35));
        canvas.DrawRect(titleBarBounds, titleBgPaint);

        using var sepPaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame);
        canvas.DrawLine(titleBarBounds.Left, titleBarBounds.Bottom, titleBarBounds.Right, titleBarBounds.Bottom, sepPaint);

        // Link icon (two overlapping squares)
        DrawLinkIcon(canvas, new SKPoint(20, titleBarBounds.MidY), FUIColors.Active);

        FUIRenderer.DrawText(canvas, "ACTION ALREADY BOUND TO ANOTHER JOYSTICK",
            new SKPoint(44, titleBarBounds.MidY + 5), FUIColors.Active, 13f, true);

        float y = titleBarBounds.Bottom + 18;

        // Description
        string desc = $"\"{_actionDisplayName}\" is already bound to {_primaryDeviceLabel} / {_primaryInputDisplay}.";
        FUIRenderer.DrawText(canvas, desc, new SKPoint(20, y), FUIColors.TextPrimary, 13f);
        y += 22;

        string desc2 = $"You are assigning it to {_secondaryDeviceLabel} / {_secondaryInputDisplay}.";
        FUIRenderer.DrawText(canvas, desc2, new SKPoint(20, y), FUIColors.TextDim, 13f);
        y += 28;

        // Affected actions list
        if (_affectedActionNames.Count > 0)
        {
            FUIRenderer.DrawText(canvas, "Sharing will also affect these bindings on " + _primaryInputDisplay + ":",
                new SKPoint(20, y), FUIColors.Warning, 11f, true);
            y += 16;
            int shown = 0;
            foreach (var name in _affectedActionNames)
            {
                if (shown >= 8) { FUIRenderer.DrawText(canvas, $"  + {_affectedActionNames.Count - 8} more...", new SKPoint(20, y), FUIColors.TextDim, 11f); y += 16; break; }
                FUIRenderer.DrawText(canvas, $"  • {name}", new SKPoint(20, y), FUIColors.TextPrimary, 11f);
                y += 16;
                shown++;
            }
            y += 12;
        }

        // Share option explanation
        string shareLabel = _affectedActionNames.Count > 0 ? "SHARE ALL" : "SHARE";
        FUIRenderer.DrawText(canvas, shareLabel + " — reroute the physical button on " + _secondaryDeviceLabel +
            " so both inputs trigger the same action.",
            new SKPoint(20, y), FUIColors.TextDim, 11f);
        y += 18;
        FUIRenderer.DrawText(canvas, "REPLACE — remove the " + _primaryDeviceLabel +
            " binding and use " + _secondaryDeviceLabel + " instead.",
            new SKPoint(20, y), FUIColors.TextDim, 11f);

        // Button panel background
        float buttonPanelTop = bounds.Bottom - 55;
        using var buttonPanelPaint = FUIRenderer.CreateFillPaint(FUIColors.Background2);
        canvas.DrawRect(new SKRect(0, buttonPanelTop, bounds.Right, bounds.Bottom), buttonPanelPaint);
        canvas.DrawLine(0, buttonPanelTop, bounds.Right, buttonPanelTop, sepPaint);

        float buttonY = buttonPanelTop + 12;
        float buttonHeight = 32f;

        // Cancel (left)
        _cancelButtonBounds = new SKRect(16, buttonY, 104, buttonY + buttonHeight);
        FUIRenderer.DrawButton(canvas, _cancelButtonBounds, "CANCEL", _hoveredButton == 0 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

        // Share (middle) — primary-colored
        string shareBtnLabel = _affectedActionNames.Count > 0 ? "SHARE ALL" : "SHARE";
        _shareButtonBounds = new SKRect(150, buttonY, 300, buttonY + buttonHeight);
        FUIRenderer.DrawButton(canvas, _shareButtonBounds, shareBtnLabel, _hoveredButton == 1 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

        // Replace (right)
        _replaceButtonBounds = new SKRect(350, buttonY, 460, buttonY + buttonHeight);
        FUIRenderer.DrawButton(canvas, _replaceButtonBounds, "REPLACE", _hoveredButton == 2 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
    }

    private static void DrawLinkIcon(SKCanvas canvas, SKPoint center, SKColor color)
    {
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = 1.5f,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };

        // Simple chain link: two overlapping rectangles
        float r = 5f;
        canvas.DrawRoundRect(center.X - 6, center.Y - r, 10, r * 2, 3, 3, paint);
        canvas.DrawRoundRect(center.X - 4, center.Y - r, 10, r * 2, 3, 3, paint);
    }


    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        float s = FUIRenderer.CanvasScaleFactor;
        float mx = e.X / s, my = e.Y / s;
        int newHovered = -1;

        if (_cancelButtonBounds.Contains(mx, my))
            newHovered = 0;
        else if (_shareButtonBounds.Contains(mx, my))
            newHovered = 1;
        else if (_replaceButtonBounds.Contains(mx, my))
            newHovered = 2;

        if (newHovered != _hoveredButton)
        {
            _hoveredButton = newHovered;
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
        float mx = e.X / s, my = e.Y / s;

        if (_cancelButtonBounds.Contains(mx, my))
        {
            Result = SCSharedBindingResult.Cancel;
            DialogResult = DialogResult.Cancel;
            Close();
        }
        else if (_shareButtonBounds.Contains(mx, my))
        {
            Result = SCSharedBindingResult.Share;
            DialogResult = DialogResult.OK;
            Close();
        }
        else if (_replaceButtonBounds.Contains(mx, my))
        {
            Result = SCSharedBindingResult.Replace;
            DialogResult = DialogResult.OK;
            Close();
        }
        else if (my < 40)
        {
            _isDragging = true;
            _dragStart = e.Location;
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        _isDragging = false;
    }

    private void OnMouseLeave(object? sender, EventArgs e)
    {
        if (_hoveredButton != -1)
        {
            _hoveredButton = -1;
            Cursor = Cursors.Default;
            _canvas.Invalidate();
        }
    }
}
