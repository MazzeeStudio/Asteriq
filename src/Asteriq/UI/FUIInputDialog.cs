using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Asteriq.UI;

/// <summary>
/// FUI-styled single-field text input dialog.
/// Matches the dark sci-fi aesthetic of the main application.
/// </summary>
public class FUIInputDialog : Form
{
    private readonly string _title;
    private readonly string _label;
    private readonly string _confirmText;
    private readonly TextBox _textBox;
    private readonly SKControl _canvas;

    private SKRect _confirmButtonBounds;
    private SKRect _cancelButtonBounds;
    private int _hoveredButton = -1; // 0=confirm, 1=cancel

    // Dragging support
    private bool _isDragging;
    private Point _dragStart;

    private const float TitleBarHeight = 36f;
    private const float DialogWidth = 340f;
    private const float DialogHeight = 160f;

    public string Value => _textBox.Text;

    private FUIInputDialog(string title, string label, string defaultValue, string confirmText)
    {
        _title = title;
        _label = label;
        _confirmText = confirmText;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(6, 8, 10);
        ShowInTaskbar = false;
        KeyPreview = true;
        ClientSize = new Size((int)DialogWidth, (int)DialogHeight);

        // Canvas for FUI chrome (title bar, buttons, label)
        _canvas = new SKControl
        {
            Dock = DockStyle.None,
            Location = Point.Empty,
            Size = ClientSize
        };
        _canvas.PaintSurface += OnPaintSurface;
        _canvas.MouseMove += OnCanvasMouseMove;
        _canvas.MouseDown += OnCanvasMouseDown;
        _canvas.MouseUp += OnCanvasMouseUp;
        _canvas.MouseLeave += OnCanvasMouseLeave;
        Controls.Add(_canvas);

        // Native text box overlaid on canvas (handles all input naturally)
        _textBox = new TextBox
        {
            Text = defaultValue,
            Left = 16,
            Top = (int)TitleBarHeight + 38,
            Width = (int)DialogWidth - 32,
            Height = 24,
            BackColor = Color.FromArgb(18, 22, 30),
            ForeColor = Color.FromArgb(200, 220, 240),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 10f)
        };
        _textBox.SelectAll();
        Controls.Add(_textBox);
        _textBox.BringToFront();

        // Keyboard shortcuts
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            else if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); }
        };
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var bounds = new SKRect(0, 0, e.Info.Width, e.Info.Height);

        canvas.Clear(FUIColors.Background0);

        // Outer frame
        FUIRenderer.DrawFrame(canvas, bounds.Inset(-1, -1), FUIColors.Frame, FUIRenderer.ChamferSize);

        // Title bar background
        var titleBar = new SKRect(bounds.Left + 2, bounds.Top + 2, bounds.Right - 2, bounds.Top + TitleBarHeight);
        using var titleBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        canvas.DrawRect(titleBar, titleBgPaint);

        // Title bar separator
        using var sepPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
        canvas.DrawLine(titleBar.Left, titleBar.Bottom, titleBar.Right, titleBar.Bottom, sepPaint);

        // Title text
        FUIRenderer.DrawText(canvas, _title.ToUpperInvariant(), new SKPoint(16, titleBar.MidY + 5),
            FUIColors.TextBright, 13f, false);

        // Field label
        FUIRenderer.DrawText(canvas, _label, new SKPoint(16, titleBar.Bottom + 20),
            FUIColors.TextPrimary, 11f);

        // Text field outline (native TextBox sits here, canvas just draws the border)
        var fieldBounds = new SKRect(15, titleBar.Bottom + 34, bounds.Right - 15, titleBar.Bottom + 62);
        using var fieldBorderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(fieldBounds, fieldBorderPaint);

        // Buttons
        float buttonWidth = 80f;
        float buttonHeight = 32f;
        float buttonSpacing = 12f;
        float totalWidth = buttonWidth * 2 + buttonSpacing;
        float buttonStartX = (bounds.Width - totalWidth) / 2;
        float buttonY = bounds.Bottom - 48;

        _cancelButtonBounds = new SKRect(buttonStartX, buttonY, buttonStartX + buttonWidth, buttonY + buttonHeight);
        _confirmButtonBounds = new SKRect(buttonStartX + buttonWidth + buttonSpacing, buttonY,
            buttonStartX + totalWidth, buttonY + buttonHeight);

        FUIRenderer.DrawButton(canvas, _cancelButtonBounds, "CANCEL",
            _hoveredButton == 1 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
        FUIRenderer.DrawButton(canvas, _confirmButtonBounds, _confirmText.ToUpperInvariant(),
            _hoveredButton == 0 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal,
            FUIColors.Active);

        // L-corner decorations
        FUIRenderer.DrawLCornerFrame(canvas, bounds.Inset(-4, -4), FUIColors.Frame.WithAlpha(100), 20f, 6f, 1f);
    }

    private void OnCanvasMouseMove(object? sender, MouseEventArgs e)
    {
        var pt = new SKPoint(e.X, e.Y);
        int newHovered = -1;

        if (_confirmButtonBounds.Contains(pt)) newHovered = 0;
        else if (_cancelButtonBounds.Contains(pt)) newHovered = 1;

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

    private void OnCanvasMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        if (e.Y < TitleBarHeight)
        {
            _isDragging = true;
            _dragStart = e.Location;
        }
    }

    private void OnCanvasMouseUp(object? sender, MouseEventArgs e)
    {
        _isDragging = false;

        if (e.Button != MouseButtons.Left) return;

        if (_hoveredButton == 0) { DialogResult = DialogResult.OK; Close(); }
        else if (_hoveredButton == 1) { DialogResult = DialogResult.Cancel; Close(); }
    }

    private void OnCanvasMouseLeave(object? sender, EventArgs e)
    {
        if (_hoveredButton >= 0)
        {
            _hoveredButton = -1;
            _canvas.Invalidate();
        }
    }

    /// <summary>
    /// Show an input dialog and return the entered text, or null if cancelled.
    /// </summary>
    public static string? Show(IWin32Window? owner, string title, string label,
        string defaultValue = "", string confirmText = "OK")
    {
        using var dialog = new FUIInputDialog(title, label, defaultValue, confirmText);
        if (dialog.ShowDialog(owner) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.Value))
            return dialog.Value.Trim();
        return null;
    }
}
