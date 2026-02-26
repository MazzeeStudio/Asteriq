using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Asteriq.UI;

/// <summary>
/// FUI-styled selection dialog for choosing from a list of items.
/// Matches the dark sci-fi aesthetic of the main application.
/// </summary>
public class FUISelectionDialog : Form
{
    private readonly string _title;
    private readonly string _description;
    private readonly List<SelectionItem> _items;
    private readonly SKControl _canvas;
    private int _hoveredItem = -1;
    private int _selectedItem = 0;
    private int _hoveredButton = -1;
    private readonly string[] _buttonLabels;
    private SKRect[] _buttonBounds = Array.Empty<SKRect>();
    private SKRect[] _itemBounds = Array.Empty<SKRect>();
    private int _result = -1;
    private int _scrollOffset = 0;
    private int _maxVisibleItems = 6;

    // Dragging support
    private bool _isDragging;
    private Point _dragStart;

    // Layout constants
    private const float TitleBarHeight = 36f;
    private const float DescriptionHeight = 50f;
    private const float ItemHeight = 28f;
    private const float ButtonAreaHeight = 50f;
    private const float ContentPadding = 16f;

    public class SelectionItem
    {
        public string Text { get; set; } = string.Empty;
        public string? Status { get; set; }
        public bool IsAction { get; set; } // For items like "+ Configure new device..."
        public object? Tag { get; set; }
    }

    private FUISelectionDialog(string title, string description, List<SelectionItem> items, string[] buttonLabels)
    {
        _title = title;
        _description = description;
        _items = items;
        _buttonLabels = buttonLabels;

        // Form setup
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(6, 8, 10);
        ShowInTaskbar = false;
        KeyPreview = true;

        // Calculate size
        float width = 420f;
        float listHeight = Math.Min(_items.Count, _maxVisibleItems) * ItemHeight + 8f;
        float height = TitleBarHeight + DescriptionHeight + listHeight + ButtonAreaHeight + ContentPadding * 2;
        float s = FUIRenderer.CanvasScaleFactor;
        ClientSize = new Size((int)(width * s), (int)(height * s));

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
        _canvas.MouseWheel += OnMouseWheel;
        Controls.Add(_canvas);

        // Handle keyboard
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape:
                _result = -1;
                Close();
                break;
            case Keys.Enter:
                _result = _selectedItem;
                Close();
                break;
            case Keys.Up:
                if (_selectedItem > 0)
                {
                    _selectedItem--;
                    EnsureItemVisible(_selectedItem);
                    _canvas.Invalidate();
                }
                e.Handled = true;
                break;
            case Keys.Down:
                if (_selectedItem < _items.Count - 1)
                {
                    _selectedItem++;
                    EnsureItemVisible(_selectedItem);
                    _canvas.Invalidate();
                }
                e.Handled = true;
                break;
        }
    }

    private void EnsureItemVisible(int index)
    {
        if (index < _scrollOffset)
            _scrollOffset = index;
        else if (index >= _scrollOffset + _maxVisibleItems)
            _scrollOffset = index - _maxVisibleItems + 1;
    }

    private void OnMouseWheel(object? sender, MouseEventArgs e)
    {
        int maxScroll = Math.Max(0, _items.Count - _maxVisibleItems);
        if (e.Delta > 0)
            _scrollOffset = Math.Max(0, _scrollOffset - 1);
        else
            _scrollOffset = Math.Min(maxScroll, _scrollOffset + 1);
        _canvas.Invalidate();
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
        var titleBarBounds = new SKRect(bounds.Left + 2, bounds.Top + 2, bounds.Right - 2, bounds.Top + TitleBarHeight);
        using (var titleBgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background2
        })
        {
            canvas.DrawRect(titleBarBounds, titleBgPaint);
        }

        // Draw title bar separator
        using (var sepPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame,
            StrokeWidth = 1f
        })
        {
            canvas.DrawLine(titleBarBounds.Left, titleBarBounds.Bottom, titleBarBounds.Right, titleBarBounds.Bottom, sepPaint);
        }

        // Draw title
        FUIRenderer.DrawText(canvas, _title.ToUpperInvariant(), new SKPoint(ContentPadding, titleBarBounds.MidY + 5),
            FUIColors.TextBright, 13f, false);

        // Draw description
        float descY = titleBarBounds.Bottom + 16;
        var descLines = _description.Split('\n');
        foreach (var line in descLines)
        {
            FUIRenderer.DrawText(canvas, line, new SKPoint(ContentPadding, descY), FUIColors.TextPrimary, 14f);
            descY += 16f;
        }

        // Draw list area background
        float listTop = titleBarBounds.Bottom + DescriptionHeight;
        float listHeight = Math.Min(_items.Count, _maxVisibleItems) * ItemHeight + 8f;
        var listBounds = new SKRect(ContentPadding, listTop, bounds.Width - ContentPadding, listTop + listHeight);

        using (var listBgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1
        })
        {
            canvas.DrawRect(listBounds, listBgPaint);
        }

        // Draw list border
        using (var listBorderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame,
            StrokeWidth = 1f
        })
        {
            canvas.DrawRect(listBounds, listBorderPaint);
        }

        // Draw items
        _itemBounds = new SKRect[_items.Count];
        float itemY = listTop + 4f;

        for (int i = _scrollOffset; i < Math.Min(_scrollOffset + _maxVisibleItems, _items.Count); i++)
        {
            var item = _items[i];
            var itemBounds = new SKRect(listBounds.Left + 2, itemY, listBounds.Right - 2, itemY + ItemHeight);
            _itemBounds[i] = itemBounds;

            // Draw selection/hover background
            if (i == _selectedItem)
            {
                using var selPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = FUIColors.Active.WithAlpha(80)
                };
                canvas.DrawRect(itemBounds, selPaint);
            }
            else if (i == _hoveredItem)
            {
                using var hoverPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = FUIColors.Background2
                };
                canvas.DrawRect(itemBounds, hoverPaint);
            }

            // Draw item text
            var textColor = item.IsAction ? FUIColors.Active : FUIColors.TextPrimary;
            if (i == _selectedItem)
                textColor = FUIColors.TextBright;

            string displayText = item.Text;
            if (!string.IsNullOrEmpty(item.Status))
            {
                displayText += $" {item.Status}";
            }

            FUIRenderer.DrawText(canvas, displayText, new SKPoint(itemBounds.Left + 8, itemBounds.MidY + 4),
                textColor, 14f, false);

            // Draw status with different color
            if (!string.IsNullOrEmpty(item.Status))
            {
                using var textPaint = FUIRenderer.CreateTextPaint(textColor, 14f);
                float baseWidth = textPaint.MeasureText(item.Text + " ");

                var statusColor = item.Status.Contains("OK") ? FUIColors.Active :
                                  item.Status.Contains("partial") ? FUIColors.Warning : textColor;

                FUIRenderer.DrawText(canvas, item.Status, new SKPoint(itemBounds.Left + 8 + baseWidth, itemBounds.MidY + 4),
                    statusColor, 14f, false);
            }

            itemY += ItemHeight;
        }

        // Draw scroll indicators if needed
        if (_items.Count > _maxVisibleItems)
        {
            using var scrollPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = FUIColors.TextDim,
                IsAntialias = true
            };

            if (_scrollOffset > 0)
            {
                // Up arrow
                var upPath = new SKPath();
                float cx = listBounds.Right - 12;
                float cy = listBounds.Top + 10;
                upPath.MoveTo(cx, cy - 4);
                upPath.LineTo(cx + 5, cy + 2);
                upPath.LineTo(cx - 5, cy + 2);
                upPath.Close();
                canvas.DrawPath(upPath, scrollPaint);
            }

            if (_scrollOffset + _maxVisibleItems < _items.Count)
            {
                // Down arrow
                var downPath = new SKPath();
                float cx = listBounds.Right - 12;
                float cy = listBounds.Bottom - 10;
                downPath.MoveTo(cx, cy + 4);
                downPath.LineTo(cx + 5, cy - 2);
                downPath.LineTo(cx - 5, cy - 2);
                downPath.Close();
                canvas.DrawPath(downPath, scrollPaint);
            }
        }

        // Draw buttons
        float buttonWidth = 80f;
        float buttonHeight = 32f;
        float buttonSpacing = 12f;
        float totalButtonsWidth = _buttonLabels.Length * buttonWidth + (_buttonLabels.Length - 1) * buttonSpacing;
        float buttonStartX = (bounds.Width - totalButtonsWidth) / 2;
        float buttonY = bounds.Bottom - 44;

        _buttonBounds = new SKRect[_buttonLabels.Length];
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
            SKColor? accent = i == 0 ? FUIColors.Active : null;
            FUIRenderer.DrawButton(canvas, btnBounds, _buttonLabels[i].ToUpperInvariant(), state, accent);
        }

        // Draw L-corner decorations
        FUIRenderer.DrawLCornerFrame(canvas, bounds.Inset(-4, -4), FUIColors.Frame.WithAlpha(100), 20f, 6f, 1f);
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        float s = FUIRenderer.CanvasScaleFactor;
        var pt = new SKPoint(e.X / s, e.Y / s);
        int newHoveredItem = -1;
        int newHoveredButton = -1;

        // Check items
        for (int i = 0; i < _itemBounds.Length; i++)
        {
            if (_itemBounds[i].Contains(pt))
            {
                newHoveredItem = i;
                break;
            }
        }

        // Check buttons
        for (int i = 0; i < _buttonBounds.Length; i++)
        {
            if (_buttonBounds[i].Contains(pt))
            {
                newHoveredButton = i;
                break;
            }
        }

        if (newHoveredItem != _hoveredItem || newHoveredButton != _hoveredButton)
        {
            _hoveredItem = newHoveredItem;
            _hoveredButton = newHoveredButton;
            _canvas.Invalidate();
        }

        Cursor = (newHoveredItem >= 0 || newHoveredButton >= 0) ? Cursors.Hand : Cursors.Default;

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
            float s = FUIRenderer.CanvasScaleFactor;
            // Check if clicking on title bar for dragging
            if (e.Y / s < TitleBarHeight && _hoveredItem < 0 && _hoveredButton < 0)
            {
                _isDragging = true;
                _dragStart = e.Location;
            }
            else if (_hoveredItem >= 0)
            {
                _selectedItem = _hoveredItem;
                _canvas.Invalidate();
            }
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        _isDragging = false;

        if (e.Button == MouseButtons.Left)
        {
            if (_hoveredButton == 0) // OK/Select button
            {
                _result = _selectedItem;
                Close();
            }
            else if (_hoveredButton == 1) // Cancel button
            {
                _result = -1;
                Close();
            }
            else if (_hoveredItem >= 0 && e.Clicks == 2) // Double-click on item
            {
                _result = _selectedItem;
                Close();
            }
        }
    }

    private void OnMouseLeave(object? sender, EventArgs e)
    {
        if (_hoveredItem >= 0 || _hoveredButton >= 0)
        {
            _hoveredItem = -1;
            _hoveredButton = -1;
            _canvas.Invalidate();
        }
    }

    /// <summary>
    /// Show a selection dialog and return the selected index, or -1 if cancelled.
    /// </summary>
    public static int Show(IWin32Window? owner, string title, string description, List<SelectionItem> items,
        string okButtonText = "Select", string cancelButtonText = "Cancel")
    {
        using var dialog = new FUISelectionDialog(title, description, items, new[] { okButtonText, cancelButtonText });
        dialog.ShowDialog(owner);
        return dialog._result;
    }

    /// <summary>
    /// Show a selection dialog and return the selected item's Tag, or null if cancelled.
    /// </summary>
    public static T? ShowAndGetTag<T>(IWin32Window? owner, string title, string description, List<SelectionItem> items,
        string okButtonText = "Select", string cancelButtonText = "Cancel") where T : class
    {
        int result = Show(owner, title, description, items, okButtonText, cancelButtonText);
        if (result >= 0 && result < items.Count)
            return items[result].Tag as T;
        return null;
    }
}
