using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Asteriq.UI;

/// <summary>
/// Dialog for managing Star Citizen game library paths.
/// Shows auto-detected and user-added library locations with add/remove controls.
/// </summary>
public class SCLibrariesDialog : FUIBaseDialog
{
    private readonly List<LibraryEntry> _entries;
    private readonly List<string> _removedPaths = new();
    private readonly List<string> _addedPaths = new();
    // CA2213: SKControl is a WinForms child control — disposed automatically via Controls collection
#pragma warning disable CA2213
    private readonly SKControl _canvas;
#pragma warning restore CA2213
    private int _hoveredRemove = -1;
    private int _hoveredButton = -1;
    private bool _changed;

    // Dragging support
    private bool _isDragging;
    private Point _dragStart;

    // Layout constants
    private const float TitleBarHeight = 36f;
    private const float DescriptionHeight = 36f;
    private const float ItemHeight = 32f;
    private const float ButtonAreaHeight = 50f;

    private const float RemoveButtonSize = 20f;

    private SKRect[] _itemBounds = Array.Empty<SKRect>();
    private SKRect[] _removeBounds = Array.Empty<SKRect>();
    private SKRect _addButtonBounds;
    private SKRect _closeButtonBounds;
    private bool _addHovered;

    public class LibraryEntry
    {
        public string Path { get; set; } = string.Empty;
        public bool IsAutoDetected { get; set; }
        public bool IsRemoved { get; set; }
    }

    public bool Changed => _changed;
    public List<string> RemovedPaths => _removedPaths;
    public List<string> AddedPaths => _addedPaths;

    private SCLibrariesDialog(IWin32Window? owner, List<LibraryEntry> entries)
    {
        _entries = entries;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(6, 8, 10);
        ShowInTaskbar = false;
        KeyPreview = true;

        float width = 460f;
        float listHeight = Math.Max(_entries.Count, 1) * ItemHeight + 8f;
        float height = TitleBarHeight + DescriptionHeight + listHeight + ButtonAreaHeight * 2 + ContentPadding;
        float s = FUIRenderer.CanvasScaleFactor;
        ClientSize = new Size((int)(width * s), (int)(height * s));

        _canvas = new SKControl { Dock = DockStyle.Fill };
        _canvas.PaintSurface += OnPaintSurface;
        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseDown += OnMouseDown;
        _canvas.MouseUp += OnMouseUp;
        _canvas.MouseLeave += OnMouseLeave;
        Controls.Add(_canvas);

        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        float scale = FUIRenderer.CanvasScaleFactor;
        canvas.Scale(scale);
        var bounds = new SKRect(0, 0, e.Info.Width / scale, e.Info.Height / scale);

        canvas.Clear(FUIColors.Background0);

        // Outer frame
        FUIRenderer.DrawFrame(canvas, bounds.Inset(-1, -1), FUIColors.Frame, FUIRenderer.ChamferSize);

        // Title bar
        var titleBarBounds = new SKRect(bounds.Left + 2, bounds.Top + 2, bounds.Right - 2, bounds.Top + TitleBarHeight);
        using (var titleBgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background2))
            canvas.DrawRect(titleBarBounds, titleBgPaint);
        using (var sepPaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame))
            canvas.DrawLine(titleBarBounds.Left, titleBarBounds.Bottom, titleBarBounds.Right, titleBarBounds.Bottom, sepPaint);

        FUIRenderer.DrawText(canvas, "GAME LIBRARIES", new SKPoint(ContentPadding, titleBarBounds.MidY + 5),
            FUIColors.TextBright, 13f, false);

        // Description
        float descY = titleBarBounds.Bottom + 14;
        FUIRenderer.DrawText(canvas, "Asteriq searches these locations for game environments.",
            new SKPoint(ContentPadding, descY), FUIColors.TextDim, 12f);

        // List area
        float listTop = titleBarBounds.Bottom + DescriptionHeight;
        float listHeight = Math.Max(_entries.Count(e => !e.IsRemoved), 1) * ItemHeight + 8f;
        var listBounds = new SKRect(ContentPadding, listTop, bounds.Width - ContentPadding, listTop + listHeight);

        using (var listBgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background1))
            canvas.DrawRect(listBounds, listBgPaint);
        using (var listBorderPaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame))
            canvas.DrawRect(listBounds, listBorderPaint);

        // Draw entries
        var visibleEntries = _entries.Where(e => !e.IsRemoved).ToList();
        _itemBounds = new SKRect[visibleEntries.Count];
        _removeBounds = new SKRect[visibleEntries.Count];
        float itemY = listTop + 4f;

        for (int i = 0; i < visibleEntries.Count; i++)
        {
            var entry = visibleEntries[i];
            var itemBounds = new SKRect(listBounds.Left + 2, itemY, listBounds.Right - 2, itemY + ItemHeight);
            _itemBounds[i] = itemBounds;

            // Hover background
            if (_hoveredRemove == i)
            {
                using var hoverPaint = FUIRenderer.CreateFillPaint(FUIColors.Background2);
                canvas.DrawRect(itemBounds, hoverPaint);
            }

            // Path text
            float textMaxWidth = itemBounds.Width - 12f;
            if (!entry.IsAutoDetected)
                textMaxWidth -= RemoveButtonSize + 8f;

            FUIRenderer.DrawTextTruncated(canvas, entry.Path, new SKPoint(itemBounds.Left + 8, itemBounds.MidY + 4),
                textMaxWidth, FUIColors.TextPrimary, 12f);

            if (entry.IsAutoDetected)
            {
                // Auto label on right
                float autoWidth = FUIRenderer.MeasureText("auto", 10f);
                FUIRenderer.DrawText(canvas, "auto",
                    new SKPoint(itemBounds.Right - autoWidth - 8, itemBounds.MidY + 3),
                    FUIColors.TextDisabled, 10f);
                _removeBounds[i] = SKRect.Empty;
            }
            else
            {
                // X remove button
                float xCenter = itemBounds.Right - RemoveButtonSize / 2 - 6;
                float yCenter = itemBounds.MidY;
                var removeBounds = new SKRect(xCenter - RemoveButtonSize / 2, yCenter - RemoveButtonSize / 2,
                    xCenter + RemoveButtonSize / 2, yCenter + RemoveButtonSize / 2);
                _removeBounds[i] = removeBounds;

                var xColor = _hoveredRemove == i ? FUIColors.Danger : FUIColors.TextDim;
                using var xPaint = FUIRenderer.CreateStrokePaint(xColor, 1.5f);
                float pad = 5f;
                canvas.DrawLine(removeBounds.Left + pad, removeBounds.Top + pad,
                    removeBounds.Right - pad, removeBounds.Bottom - pad, xPaint);
                canvas.DrawLine(removeBounds.Right - pad, removeBounds.Top + pad,
                    removeBounds.Left + pad, removeBounds.Bottom - pad, xPaint);
            }

            itemY += ItemHeight;
        }

        if (visibleEntries.Count == 0)
        {
            FUIRenderer.DrawText(canvas, "No library locations configured",
                new SKPoint(listBounds.Left + 8, listBounds.MidY + 4), FUIColors.TextDisabled, 12f);
        }

        // "+ Add Path" text link (right-aligned below list)
        float addY = listBounds.Bottom + 8f;
        string addText = "+ Add Path";
        float addTextWidth = FUIRenderer.MeasureText(addText, 12f);
        float addTextX = bounds.Width - ContentPadding - addTextWidth;
        var addColor = _addHovered ? FUIColors.TextBright : FUIColors.TextDim;
        FUIRenderer.DrawText(canvas, addText, new SKPoint(addTextX, addY + 10f), addColor, 12f);
        _addButtonBounds = new SKRect(addTextX - 4f, addY, bounds.Width - ContentPadding + 4f, addY + 20f);

        // Close button (bottom-right)
        float closeWidth = 80f;
        float closeHeight = 32f;
        _closeButtonBounds = new SKRect(bounds.Right - ContentPadding - closeWidth, bounds.Bottom - 44,
            bounds.Right - ContentPadding, bounds.Bottom - 44 + closeHeight);
        var closeState = _hoveredButton == 0 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
        FUIRenderer.DrawButton(canvas, _closeButtonBounds, "CLOSE", closeState);

        // L-corner decorations
        FUIRenderer.DrawLCornerFrame(canvas, bounds.Inset(-4, -4), FUIColors.Frame.WithAlpha(100), 20f, 6f, 1f);
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        float s = FUIRenderer.CanvasScaleFactor;
        var pt = new SKPoint(e.X / s, e.Y / s);

        int newHoveredRemove = -1;
        int newHoveredButton = -1;
        bool newAddHovered = false;

        // Check remove buttons
        for (int i = 0; i < _removeBounds.Length; i++)
        {
            if (!_removeBounds[i].IsEmpty && _itemBounds[i].Contains(pt))
            {
                newHoveredRemove = i;
                break;
            }
        }

        // Check add button
        if (_addButtonBounds.Contains(pt))
            newAddHovered = true;

        // Check close button
        if (_closeButtonBounds.Contains(pt))
            newHoveredButton = 0;

        if (newHoveredRemove != _hoveredRemove || newHoveredButton != _hoveredButton || newAddHovered != _addHovered)
        {
            _hoveredRemove = newHoveredRemove;
            _hoveredButton = newHoveredButton;
            _addHovered = newAddHovered;
            _canvas.Invalidate();
        }

        Cursor = (newHoveredRemove >= 0 || newHoveredButton >= 0 || newAddHovered) ? Cursors.Hand : Cursors.Default;

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
            if (e.Y / s < TitleBarHeight && _hoveredRemove < 0 && _hoveredButton < 0 && !_addHovered)
            {
                _isDragging = true;
                _dragStart = e.Location;
            }
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        _isDragging = false;

        if (e.Button != MouseButtons.Left)
            return;

        // Close button
        if (_hoveredButton == 0)
        {
            Close();
            return;
        }

        // Add button
        if (_addHovered)
        {
            AddLibraryPath();
            return;
        }

        // Remove button
        if (_hoveredRemove >= 0)
        {
            var visibleEntries = _entries.Where(en => !en.IsRemoved).ToList();
            if (_hoveredRemove < visibleEntries.Count)
            {
                var entry = visibleEntries[_hoveredRemove];
                entry.IsRemoved = true;
                _removedPaths.Add(entry.Path);
                _changed = true;
                _hoveredRemove = -1;
                ResizeForEntries();
                _canvas.Invalidate();
            }
        }
    }

    private void OnMouseLeave(object? sender, EventArgs e)
    {
        _hoveredRemove = -1;
        _hoveredButton = -1;
        _addHovered = false;
        _canvas.Invalidate();
    }

    private void AddLibraryPath()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select a Star Citizen library folder (contains StarCitizen subfolder)",
            ShowNewFolderButton = false,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        string path = dialog.SelectedPath;

        // Check for duplicates
        if (_entries.Any(en => !en.IsRemoved && en.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            return;

        _entries.Add(new LibraryEntry { Path = path, IsAutoDetected = false });
        _addedPaths.Add(path);
        _changed = true;
        ResizeForEntries();
        _canvas.Invalidate();
    }

    private void ResizeForEntries()
    {
        int visibleCount = _entries.Count(en => !en.IsRemoved);
        float width = 460f;
        float listHeight = Math.Max(visibleCount, 1) * ItemHeight + 8f;
        float height = TitleBarHeight + DescriptionHeight + listHeight + ButtonAreaHeight * 2 + ContentPadding;
        float s = FUIRenderer.CanvasScaleFactor;
        ClientSize = new Size((int)(width * s), (int)(height * s));
    }

    /// <summary>
    /// Show the libraries dialog. Returns true if the user made changes.
    /// </summary>
    public static bool Show(IWin32Window? owner, List<string> autoDetectedPaths, List<string> customPaths,
        out List<string> addedPaths, out List<string> removedPaths)
    {
        var entries = new List<LibraryEntry>();

        foreach (var path in autoDetectedPaths)
            entries.Add(new LibraryEntry { Path = path, IsAutoDetected = true });
        foreach (var path in customPaths)
            entries.Add(new LibraryEntry { Path = path, IsAutoDetected = false });

        using var dialog = new SCLibrariesDialog(owner, entries);
        dialog.ShowDialog(owner);

        addedPaths = dialog._addedPaths;
        removedPaths = dialog._removedPaths;
        return dialog._changed;
    }
}
