using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Asteriq.UI;

/// <summary>
/// FUI-themed confirmation dialog with Yes/No style buttons.
/// </summary>
public class FUIConfirmDialog : Form
{
    private SKControl _canvas = null!;
    private readonly string _title;
    private readonly string _message;
    private readonly string _confirmText;
    private readonly string _cancelText;

    private SKRect _confirmButtonBounds;
    private SKRect _cancelButtonBounds;
    private int _hoveredButton = -1; // 0=confirm, 1=cancel

    public FUIConfirmDialog(string title, string message, string confirmText = "Yes", string cancelText = "No")
    {
        _title = title;
        _message = message;
        _confirmText = confirmText;
        _cancelText = cancelText;

        InitializeForm();
        InitializeCanvas();
    }

    private void InitializeForm()
    {
        Text = _title;
        Size = new Size(400, 220);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.Black;
        ShowInTaskbar = false;
        KeyPreview = true;
    }

    private void InitializeCanvas()
    {
        _canvas = new SKControl { Dock = DockStyle.Fill };
        _canvas.PaintSurface += OnPaintSurface;
        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseDown += OnMouseDown;
        Controls.Add(_canvas);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.No;
            Close();
            return true;
        }
        if (keyData == Keys.Enter)
        {
            DialogResult = DialogResult.Yes;
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var bounds = new SKRect(0, 0, e.Info.Width, e.Info.Height);

        // Background
        canvas.Clear(FUIColors.Background1);

        // Border frame
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame,
            StrokeWidth = 2f
        };
        canvas.DrawRect(bounds, framePaint);

        // Corner accents
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Primary, 20f, 6f);

        // Title
        float titleY = 24;
        var titleBounds = new SKRect(0, titleY, bounds.Width, titleY + 20);
        FUIRenderer.DrawTextCentered(canvas, _title.ToUpperInvariant(), titleBounds, FUIColors.Active, 14f, true);

        // Separator line under title
        float sepY = 50;
        using var sepPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame.WithAlpha(100),
            StrokeWidth = 1f
        };
        canvas.DrawLine(20, sepY, bounds.Right - 20, sepY, sepPaint);

        // Message text (multi-line support)
        float messageY = 65;
        float lineHeight = 18f;
        var lines = _message.Split('\n');
        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                var lineBounds = new SKRect(0, messageY, bounds.Width, messageY + lineHeight);
                FUIRenderer.DrawTextCentered(canvas, line, lineBounds, FUIColors.TextPrimary, 11f);
            }
            messageY += lineHeight;
        }

        // Buttons
        float buttonWidth = 100f;
        float buttonHeight = 32f;
        float buttonGap = 20f;
        float buttonsY = bounds.Bottom - 55;
        float totalButtonsWidth = buttonWidth * 2 + buttonGap;
        float buttonsStartX = bounds.MidX - totalButtonsWidth / 2;

        // Cancel button (left)
        _cancelButtonBounds = new SKRect(buttonsStartX, buttonsY, buttonsStartX + buttonWidth, buttonsY + buttonHeight);
        DrawButton(canvas, _cancelButtonBounds, _cancelText, _hoveredButton == 1, false);

        // Confirm button (right)
        _confirmButtonBounds = new SKRect(buttonsStartX + buttonWidth + buttonGap, buttonsY,
            buttonsStartX + buttonWidth * 2 + buttonGap, buttonsY + buttonHeight);
        DrawButton(canvas, _confirmButtonBounds, _confirmText, _hoveredButton == 0, true);
    }

    private void DrawButton(SKCanvas canvas, SKRect bounds, string text, bool hovered, bool isPrimary)
    {
        var bgColor = isPrimary
            ? (hovered ? FUIColors.Active.WithAlpha(80) : FUIColors.Active.WithAlpha(40))
            : (hovered ? FUIColors.Primary.WithAlpha(40) : FUIColors.Background2);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRoundRect(bounds, 4, 4, bgPaint);

        var frameColor = isPrimary
            ? FUIColors.Active
            : (hovered ? FUIColors.Primary : FUIColors.Frame);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = frameColor,
            StrokeWidth = hovered ? 2f : 1f
        };
        canvas.DrawRoundRect(bounds, 4, 4, framePaint);

        var textColor = isPrimary ? FUIColors.Active : (hovered ? FUIColors.TextPrimary : FUIColors.TextDim);
        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 11f);
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        var pt = new SKPoint(e.X, e.Y);
        int newHovered = -1;

        if (_confirmButtonBounds.Contains(pt))
            newHovered = 0;
        else if (_cancelButtonBounds.Contains(pt))
            newHovered = 1;

        if (newHovered != _hoveredButton)
        {
            _hoveredButton = newHovered;
            _canvas.Invalidate();
        }
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        var pt = new SKPoint(e.X, e.Y);

        if (_confirmButtonBounds.Contains(pt))
        {
            DialogResult = DialogResult.Yes;
            Close();
        }
        else if (_cancelButtonBounds.Contains(pt))
        {
            DialogResult = DialogResult.No;
            Close();
        }
    }
}
