using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Asteriq.UI;

/// <summary>
/// Result of the binding conflict dialog
/// </summary>
public enum BindingConflictResult
{
    Cancel,         // User cancelled - don't apply the binding
    ApplyAnyway,    // Apply binding even with conflicts
    ReplaceAll      // Remove conflicting bindings and apply new one
}

/// <summary>
/// FUI-styled dialog shown when a binding would conflict with existing bindings.
/// Allows user to Cancel, Apply Anyway, or Replace existing bindings.
/// </summary>
public class BindingConflictDialog : Form
{
    private readonly List<SCActionBinding> _conflicts;
    private readonly string _newActionName;
    private readonly string _inputDisplayName;
    private readonly string _deviceName;
    private SKControl _canvas = null!;

    private SKRect _cancelButtonBounds;
    private SKRect _applyButtonBounds;
    private SKRect _replaceButtonBounds;
    private int _hoveredButton = -1; // 0=Cancel, 1=Apply, 2=Replace

    // Dragging support
    private bool _isDragging;
    private Point _dragStart;

    public BindingConflictResult Result { get; private set; } = BindingConflictResult.Cancel;

    public BindingConflictDialog(
        List<SCActionBinding> conflicts,
        string newActionName,
        string inputDisplayName,
        string deviceName)
    {
        _conflicts = conflicts;
        _newActionName = newActionName;
        _inputDisplayName = inputDisplayName;
        _deviceName = deviceName;

        InitializeComponent();
    }

    private void InitializeComponent()
    {
        // Form setup
        Text = "Binding Conflict";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(6, 8, 10);
        ShowInTaskbar = false;
        KeyPreview = true;

        // Calculate size based on content
        int conflictCount = Math.Min(_conflicts.Count, 5); // Show max 5 in list
        int height = 220 + conflictCount * 22;
        ClientSize = new Size(450, height);

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
                Result = BindingConflictResult.Cancel;
                Close();
            }
        };
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var bounds = new SKRect(0, 0, e.Info.Width, e.Info.Height);

        canvas.Clear(FUIColors.Background0);

        // Draw outer frame
        FUIRenderer.DrawFrame(canvas, bounds.Inset(-1, -1), FUIColors.Frame, FUIRenderer.ChamferSize);

        // Draw title bar background with warning tint
        var titleBarBounds = new SKRect(bounds.Left + 2, bounds.Top + 2, bounds.Right - 2, bounds.Top + 40);
        using var titleBgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Active.WithAlpha(40)
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

        // Draw warning icon
        DrawWarningIcon(canvas, new SKPoint(20, titleBarBounds.MidY), FUIColors.Active);

        // Draw title
        string titleText = $"{_inputDisplayName} IS ALREADY IN USE";
        FUIRenderer.DrawText(canvas, titleText, new SKPoint(44, titleBarBounds.MidY + 5),
            FUIColors.Active, 12f, true);

        // Description
        float y = titleBarBounds.Bottom + 20;
        string descText = $"The input \"{_inputDisplayName}\" on {_deviceName} is already bound to:";
        FUIRenderer.DrawText(canvas, descText, new SKPoint(20, y), FUIColors.TextPrimary, 11f);
        y += 28;

        // Conflict list background
        float listHeight = Math.Min(_conflicts.Count, 5) * 22 + 10;
        var listBounds = new SKRect(20, y, bounds.Right - 20, y + listHeight);
        using var listBgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background2
        };
        canvas.DrawRect(listBounds, listBgPaint);

        // Conflict list border
        using var listBorderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(listBounds, listBorderPaint);

        // Draw conflict items
        float itemY = y + 8;
        for (int i = 0; i < Math.Min(_conflicts.Count, 5); i++)
        {
            var conflict = _conflicts[i];
            string displayName = FormatActionName(conflict.ActionMap, conflict.ActionName);
            FUIRenderer.DrawText(canvas, $"  {displayName}", new SKPoint(25, itemY + 14), FUIColors.TextPrimary, 10f);
            itemY += 22;
        }

        if (_conflicts.Count > 5)
        {
            FUIRenderer.DrawText(canvas, $"  ... and {_conflicts.Count - 5} more",
                new SKPoint(25, itemY + 14), FUIColors.TextDim, 10f);
        }

        y += listHeight + 15;

        // New binding info
        string newBindingText = $"You are trying to bind: {_newActionName}";
        FUIRenderer.DrawText(canvas, newBindingText, new SKPoint(20, y), FUIColors.TextDim, 10f);

        // Button panel background
        float buttonPanelTop = bounds.Bottom - 55;
        using var buttonPanelPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background2
        };
        canvas.DrawRect(new SKRect(0, buttonPanelTop, bounds.Right, bounds.Bottom), buttonPanelPaint);

        // Draw separator above buttons
        canvas.DrawLine(0, buttonPanelTop, bounds.Right, buttonPanelTop, sepPaint);

        // Draw buttons
        float buttonY = buttonPanelTop + 12;
        float buttonHeight = 32;  // 4px aligned, TouchTargetCompact

        // Cancel button (left)
        _cancelButtonBounds = new SKRect(15, buttonY, 105, buttonY + buttonHeight);
        FUIRenderer.DrawButton(canvas, _cancelButtonBounds, "CANCEL",
            _hoveredButton == 0 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

        // Apply Anyway button (middle)
        _applyButtonBounds = new SKRect(175, buttonY, 290, buttonY + buttonHeight);
        DrawWarningButton(canvas, _applyButtonBounds, "APPLY ANYWAY", _hoveredButton == 1);

        // Replace button (right)
        _replaceButtonBounds = new SKRect(340, buttonY, 430, buttonY + buttonHeight);
        FUIRenderer.DrawButton(canvas, _replaceButtonBounds, "REPLACE",
            _hoveredButton == 2 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
    }

    private void DrawWarningIcon(SKCanvas canvas, SKPoint center, SKColor color)
    {
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = 2f,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round
        };

        // Triangle
        float size = 10f;
        var path = new SKPath();
        path.MoveTo(center.X, center.Y - size);
        path.LineTo(center.X - size, center.Y + size * 0.7f);
        path.LineTo(center.X + size, center.Y + size * 0.7f);
        path.Close();
        canvas.DrawPath(path, paint);

        // Exclamation mark
        paint.StrokeWidth = 2.5f;
        canvas.DrawLine(center.X, center.Y - 4, center.X, center.Y + 2, paint);
        canvas.DrawPoint(center.X, center.Y + 6, paint);
    }

    private void DrawWarningButton(SKCanvas canvas, SKRect bounds, string text, bool hovered)
    {
        // Warning-colored button for "Apply Anyway"
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = hovered ? FUIColors.Active.WithAlpha(60) : FUIColors.Active.WithAlpha(30),
            IsAntialias = true
        };
        canvas.DrawRect(bounds, bgPaint);

        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = hovered ? FUIColors.Active : FUIColors.Active.WithAlpha(150),
            StrokeWidth = 1f,
            IsAntialias = true
        };
        canvas.DrawRect(bounds, borderPaint);

        // Text centered
        float textWidth = FUIRenderer.MeasureText(text, 10f);
        float textX = bounds.MidX - textWidth / 2;
        FUIRenderer.DrawText(canvas, text, new SKPoint(textX, bounds.MidY + 4),
            hovered ? FUIColors.Active : FUIColors.Active.WithAlpha(200), 10f, true);
    }

    private static string FormatActionName(string actionMap, string actionName)
    {
        // Use the SCCategoryMapper if available, otherwise simple format
        string category = SCCategoryMapper.GetCategoryName(actionMap);
        string name = SCCategoryMapper.FormatActionName(actionName);
        return $"{category} > {name}";
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        int newHovered = -1;

        if (_cancelButtonBounds.Contains(e.X, e.Y))
            newHovered = 0;
        else if (_applyButtonBounds.Contains(e.X, e.Y))
            newHovered = 1;
        else if (_replaceButtonBounds.Contains(e.X, e.Y))
            newHovered = 2;

        if (newHovered != _hoveredButton)
        {
            _hoveredButton = newHovered;
            Cursor = newHovered >= 0 ? Cursors.Hand : Cursors.Default;
            _canvas.Invalidate();
        }

        // Handle dragging
        if (_isDragging)
        {
            var screen = PointToScreen(e.Location);
            Location = new Point(screen.X - _dragStart.X, screen.Y - _dragStart.Y);
        }
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        if (_cancelButtonBounds.Contains(e.X, e.Y))
        {
            Result = BindingConflictResult.Cancel;
            DialogResult = DialogResult.Cancel;
            Close();
        }
        else if (_applyButtonBounds.Contains(e.X, e.Y))
        {
            Result = BindingConflictResult.ApplyAnyway;
            DialogResult = DialogResult.OK;
            Close();
        }
        else if (_replaceButtonBounds.Contains(e.X, e.Y))
        {
            Result = BindingConflictResult.ReplaceAll;
            DialogResult = DialogResult.OK;
            Close();
        }
        else if (e.Y < 40) // Title bar area for dragging
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
