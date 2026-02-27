using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Asteriq.UI;

/// <summary>
/// FUI-styled message box to replace Windows MessageBox.
/// Matches the dark sci-fi aesthetic of the main application.
/// </summary>
public class FUIMessageBox : Form
{
    public enum MessageBoxType
    {
        Information,
        Question,
        Warning,
        Error
    }

    private readonly string _message;
    private readonly string _title;
    private readonly MessageBoxType _type;
    private readonly string[] _buttonLabels;
    private readonly string[]? _detailLines;
    private readonly SKColor? _primaryButtonColor;
    private readonly SKControl _canvas;
    private int _hoveredButton = -1;
    private readonly SKRect[] _buttonBounds;
    private int _result = -1;

    // Dragging support
    private bool _isDragging;
    private Point _dragStart;

    private FUIMessageBox(string message, string title, MessageBoxType type, string[] buttonLabels,
        string[]? detailLines = null, SKColor? primaryButtonColor = null)
    {
        _message = message;
        _title = title;
        _type = type;
        _buttonLabels = buttonLabels;
        _detailLines = detailLines;
        _primaryButtonColor = primaryButtonColor;
        _buttonBounds = new SKRect[buttonLabels.Length];

        // Form setup
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(6, 8, 10);
        ShowInTaskbar = false;
        KeyPreview = true;

        // Calculate size based on content
        var size = CalculateSize();
        float s = FUIRenderer.CanvasScaleFactor;
        ClientSize = new Size((int)(size.Width * s), (int)(size.Height * s));

        // Create canvas
        _canvas = new SKControl
        {
            Dock = DockStyle.Fill
        };
        _canvas.PaintSurface += OnPaintSurface;
        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseDown += OnMouseDown;
        _canvas.MouseUp += OnMouseUp;
        _canvas.MouseLeave += OnMouseLeave;
        Controls.Add(_canvas);

        // Handle Escape key
        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                _result = _buttonLabels.Length - 1; // Last button (usually Cancel/No)
                Close();
            }
            else if (e.KeyCode == Keys.Enter)
            {
                _result = 0; // First button (usually OK/Yes)
                Close();
            }
        };
    }

    private SKSize CalculateSize()
    {
        using var textPaint = FUIRenderer.CreateTextPaint(FUIColors.TextPrimary, 16f);
        using var detailPaint = FUIRenderer.CreateTextPaint(FUIColors.TextDim, 13f);

        var lines = _message.Split('\n');
        float maxWidth = 200f;
        foreach (var line in lines)
            maxWidth = Math.Max(maxWidth, textPaint.MeasureText(line));

        if (_detailLines is not null)
            foreach (var line in _detailLines)
                maxWidth = Math.Max(maxWidth, detailPaint.MeasureText(line) + 40f); // 20px box padding each side

        float width = Math.Max(340f, maxWidth + 80f);
        float height = 120f + lines.Length * 22f + 50f; // title bar + message + buttons

        if (_detailLines is { Length: > 0 })
            height += _detailLines.Length * 18f + 44f; // 12px gap + 10px pad top + lines + 10px pad bottom + 12px gap

        return new SKSize(width, height);
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        float scale = FUIRenderer.CanvasScaleFactor;
        canvas.Scale(scale);
        var bounds = new SKRect(0, 0, e.Info.Width / scale, e.Info.Height / scale);

        canvas.Clear(FUIColors.Background0);

        // Draw outer frame
        FUIRenderer.DrawFrame(canvas, bounds.Inset(-1, -1), FUIColors.Frame, FUIRenderer.ChamferSize);

        // Draw title bar background
        var titleBarBounds = new SKRect(bounds.Left + 2, bounds.Top + 2, bounds.Right - 2, bounds.Top + 36);
        using var titleBgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background2
        };
        canvas.DrawRect(titleBarBounds, titleBgPaint);

        // Draw title bar separator
        using var sepPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawLine(titleBarBounds.Left, titleBarBounds.Bottom, titleBarBounds.Right, titleBarBounds.Bottom, sepPaint);

        // Draw icon based on type
        var iconColor = _type switch
        {
            MessageBoxType.Question => FUIColors.Active,
            MessageBoxType.Warning => FUIColors.Warning,
            MessageBoxType.Error => FUIColors.Danger,
            _ => FUIColors.Active
        };
        DrawIcon(canvas, new SKPoint(20, titleBarBounds.MidY), iconColor, _type);

        // Draw title
        FUIRenderer.DrawText(canvas, _title.ToUpperInvariant(), new SKPoint(44, titleBarBounds.MidY + 5),
            FUIColors.TextBright, 13f, false);

        // Draw message
        float messageY = titleBarBounds.Bottom + 24;
        var lines = _message.Split('\n');
        foreach (var line in lines)
        {
            FUIRenderer.DrawText(canvas, line, new SKPoint(20, messageY), FUIColors.TextPrimary, 16f);
            messageY += 20f;
        }

        // Draw detail box (if provided)
        if (_detailLines is { Length: > 0 })
        {
            const float boxPad = 10f;
            const float lineH = 18f;
            float boxTop = messageY + 12f;
            float boxHeight = _detailLines.Length * lineH + boxPad * 2;
            var detailBounds = new SKRect(16f, boxTop, bounds.Right - 16f, boxTop + boxHeight);

            // Use primary button colour (e.g. Danger red) for the box so it always pops
            var boxColor = _primaryButtonColor ?? iconColor;

            using var boxBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = boxColor.WithAlpha(55) };
            canvas.DrawRect(detailBounds, boxBgPaint);

            using var boxBorderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = boxColor,
                StrokeWidth = 1.5f
            };
            canvas.DrawRect(detailBounds, boxBorderPaint);

            float detailY = boxTop + boxPad + lineH * 0.8f;
            foreach (var line in _detailLines)
            {
                FUIRenderer.DrawText(canvas, line, new SKPoint(detailBounds.Left + boxPad, detailY),
                    FUIColors.TextPrimary, 13f);
                detailY += lineH;
            }
        }

        // Draw buttons
        float buttonWidth = 80f;
        float buttonHeight = 32f;
        float buttonSpacing = 12f;
        float totalButtonsWidth = _buttonLabels.Length * buttonWidth + (_buttonLabels.Length - 1) * buttonSpacing;
        float buttonStartX = (bounds.Width - totalButtonsWidth) / 2;
        float buttonY = bounds.Bottom - 50;

        for (int i = 0; i < _buttonLabels.Length; i++)
        {
            var btnBounds = new SKRect(
                buttonStartX + i * (buttonWidth + buttonSpacing),
                buttonY,
                buttonStartX + i * (buttonWidth + buttonSpacing) + buttonWidth,
                buttonY + buttonHeight
            );
            _buttonBounds[i] = btnBounds;

            var state = _hoveredButton == i ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
            SKColor? accent = i == 0 ? (_primaryButtonColor ?? FUIColors.Active) : null;
            FUIRenderer.DrawButton(canvas, btnBounds, _buttonLabels[i].ToUpperInvariant(), state, accent);
        }

        // Draw L-corner decorations
        FUIRenderer.DrawLCornerFrame(canvas, bounds.Inset(-4, -4), FUIColors.Frame.WithAlpha(100), 20f, 6f, 1f);
    }

    private void DrawIcon(SKCanvas canvas, SKPoint center, SKColor color, MessageBoxType type)
    {
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = 2f,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };

        float r = 10f;

        switch (type)
        {
            case MessageBoxType.Information:
                // Circle with "i"
                canvas.DrawCircle(center, r, paint);
                canvas.DrawLine(center.X, center.Y - 2, center.X, center.Y + 4, paint);
                using (var dotPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = color, IsAntialias = true })
                {
                    canvas.DrawCircle(center.X, center.Y - 6, 2f, dotPaint);
                }
                break;

            case MessageBoxType.Question:
                // Circle with "?"
                canvas.DrawCircle(center, r, paint);
                using (var path = new SKPath())
                {
                    path.MoveTo(center.X - 3, center.Y - 5);
                    path.QuadTo(center.X - 3, center.Y - 8, center.X, center.Y - 8);
                    path.QuadTo(center.X + 4, center.Y - 8, center.X + 4, center.Y - 4);
                    path.QuadTo(center.X + 4, center.Y - 1, center.X, center.Y + 1);
                    canvas.DrawPath(path, paint);
                }
                using (var dotPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = color, IsAntialias = true })
                {
                    canvas.DrawCircle(center.X, center.Y + 6, 2f, dotPaint);
                }
                break;

            case MessageBoxType.Warning:
                // Triangle with "!"
                using (var path = new SKPath())
                {
                    path.MoveTo(center.X, center.Y - r);
                    path.LineTo(center.X + r, center.Y + r - 2);
                    path.LineTo(center.X - r, center.Y + r - 2);
                    path.Close();
                    canvas.DrawPath(path, paint);
                }
                canvas.DrawLine(center.X, center.Y - 3, center.X, center.Y + 2, paint);
                using (var dotPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = color, IsAntialias = true })
                {
                    canvas.DrawCircle(center.X, center.Y + 6, 2f, dotPaint);
                }
                break;

            case MessageBoxType.Error:
                // Circle with "X"
                canvas.DrawCircle(center, r, paint);
                float xSize = 5f;
                canvas.DrawLine(center.X - xSize, center.Y - xSize, center.X + xSize, center.Y + xSize, paint);
                canvas.DrawLine(center.X + xSize, center.Y - xSize, center.X - xSize, center.Y + xSize, paint);
                break;
        }
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        float sc = FUIRenderer.CanvasScaleFactor;
        var pt = new SKPoint(e.X / sc, e.Y / sc);
        int newHovered = -1;

        for (int i = 0; i < _buttonBounds.Length; i++)
        {
            if (_buttonBounds[i].Contains(pt))
            {
                newHovered = i;
                break;
            }
        }

        if (newHovered != _hoveredButton)
        {
            _hoveredButton = newHovered;
            _canvas.Invalidate();
        }

        // Update cursor
        Cursor = newHovered >= 0 ? Cursors.Hand : Cursors.Default;

        // Handle dragging
        if (_isDragging)
        {
            var screenPos = PointToScreen(e.Location);
            Location = new Point(screenPos.X - _dragStart.X, screenPos.Y - _dragStart.Y);
        }
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            // Check if clicking on title bar for dragging
            float sc = FUIRenderer.CanvasScaleFactor;
            if (e.Y / sc < 36 && _hoveredButton < 0)
            {
                _isDragging = true;
                _dragStart = e.Location;
            }
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        _isDragging = false;

        if (e.Button == MouseButtons.Left && _hoveredButton >= 0)
        {
            _result = _hoveredButton;
            Close();
        }
    }

    private void OnMouseLeave(object? sender, EventArgs e)
    {
        if (_hoveredButton >= 0)
        {
            _hoveredButton = -1;
            _canvas.Invalidate();
        }
    }

    /// <summary>
    /// Show an information message with OK button
    /// </summary>
    public static void ShowInfo(IWin32Window? owner, string message, string title = "Information")
    {
        using var dialog = new FUIMessageBox(message, title, MessageBoxType.Information, new[] { "OK" });
        dialog.ShowDialog(owner);
    }

    /// <summary>
    /// Show an error message with OK button
    /// </summary>
    public static void ShowError(IWin32Window? owner, string message, string title = "Error")
    {
        using var dialog = new FUIMessageBox(message, title, MessageBoxType.Error, new[] { "OK" });
        dialog.ShowDialog(owner);
    }

    /// <summary>
    /// Show a warning message with OK button
    /// </summary>
    public static void ShowWarning(IWin32Window? owner, string message, string title = "Warning")
    {
        using var dialog = new FUIMessageBox(message, title, MessageBoxType.Warning, new[] { "OK" });
        dialog.ShowDialog(owner);
    }

    /// <summary>
    /// Show a Yes/No question dialog
    /// </summary>
    /// <returns>True if Yes was clicked, false otherwise</returns>
    public static bool ShowQuestion(IWin32Window? owner, string message, string title = "Confirm")
    {
        using var dialog = new FUIMessageBox(message, title, MessageBoxType.Question, new[] { "Yes", "No" });
        dialog.ShowDialog(owner);
        return dialog._result == 0;
    }

    /// <summary>
    /// Show a destructive confirmation dialog with a framed detail section.
    /// Primary button uses danger (red) accent.
    /// </summary>
    /// <returns>True if the confirm button was clicked, false otherwise</returns>
    public static bool ShowDestructiveConfirm(IWin32Window? owner, string message, string title,
        string confirmLabel, string[]? detailLines = null)
    {
        using var dialog = new FUIMessageBox(message, title, MessageBoxType.Warning,
            new[] { confirmLabel, "Cancel" }, detailLines, FUIColors.Danger);
        dialog.ShowDialog(owner);
        return dialog._result == 0;
    }

    /// <summary>
    /// Show a custom dialog with specified buttons
    /// </summary>
    /// <returns>Index of the clicked button (0-based), or -1 if closed without clicking</returns>
    public static int Show(IWin32Window? owner, string message, string title, MessageBoxType type, params string[] buttons)
    {
        using var dialog = new FUIMessageBox(message, title, type, buttons);
        dialog.ShowDialog(owner);
        return dialog._result;
    }
}
