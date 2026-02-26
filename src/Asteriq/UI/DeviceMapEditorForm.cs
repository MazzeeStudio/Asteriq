#if DEBUG
using System.Text.Json;
using System.Text.Json.Serialization;
using Asteriq.Models;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Svg.Skia;

namespace Asteriq.UI;

/// <summary>
/// Visual editor for creating and editing device control maps.
/// Only available in DEBUG builds with --map-editor flag.
/// </summary>
public class DeviceMapEditorForm : Form
{
    // Canvas and rendering
    private SKControl _canvas = null!;
    private System.Windows.Forms.Timer _renderTimer = null!;
    private float _pulsePhase = 0f;

    // SVG handling
    private SKSvg? _currentSvg;
    private float _svgScale = 1f;
    private SKPoint _svgOffset;
    private float _svgScaledWidth; // For mirror coordinate conversion
    private string _imagesDir = "";
    private List<string> _availableSvgFiles = new();

    // Current device map being edited
    private DeviceMap _deviceMap = new();
    private string _currentJsonPath = "";
    private bool _hasUnsavedChanges = false;

    // Selection and interaction state
    private string? _selectedControlKey;
    private enum DragMode { None, Anchor, Label, ShelfEnd, Segment }
    private DragMode _dragMode = DragMode.None;
    private int _dragSegmentIndex = -1; // Which segment is being dragged (0+ = segment index)

    // Mouse position
    private SKPoint _mousePos;
    private SKPoint _mouseViewBox;

    // UI Layout regions
    private SKRect _svgPanelBounds;
    private SKRect _propertiesPanelBounds;
    private SKRect _controlsListBounds;
    private SKRect _toolbarBounds;
    private SKRect _statusBarBounds;

    // Toolbar controls
    private SKRect _svgDropdownBounds;
    private SKRect _jsonTextBoxBounds;
    private SKRect _newButtonBounds;
    private SKRect _loadButtonBounds;
    private bool _svgDropdownOpen = false;
    private List<SKRect> _svgDropdownItemBounds = new();

    // Properties panel controls
    private readonly string[] _controlTypes = { "Button", "Axis", "Hat", "Toggle", "Slider", "Encoder", "Ministick" };
    private readonly string[] _shelfSides = { "left", "right" };

    // Controls list
    private List<SKRect> _controlListItemBounds = new();
    private SKRect _addControlButtonBounds;
    private SKRect _deleteControlButtonBounds;
    private float _controlsListScroll = 0;

    // Save button
    private SKRect _saveButtonBounds;

    // Mirror checkbox
    private SKRect _mirrorCheckboxBounds;
    private bool _mirrorCheckboxHovered;

    // Lead line editing buttons
    private SKRect _shelfSideButtonBounds;
    private SKRect _shelfLengthMinusBounds;
    private SKRect _shelfLengthPlusBounds;
    private SKRect _addSegmentButtonBounds;
    private SKRect _removeSegmentButtonBounds;
    private List<(SKRect angleMinus, SKRect anglePlus, SKRect lengthMinus, SKRect lengthPlus)> _segmentButtonBounds = new();

    // Lead line segment draggable handles (screen positions)
    private SKPoint _shelfEndHandle;  // End of shelf segment
    private List<SKPoint> _segmentEndHandles = new();  // End of each additional segment
    private const float HandleRadius = 8f;
    private const float HandleHitRadius = 12f;

    // Hover states
    private int _hoveredSvgDropdownItem = -1;
    private int _hoveredControlListItem = -1;
    private bool _saveButtonHovered = false;
    private bool _newButtonHovered = false;
    private bool _loadButtonHovered = false;
    private bool _addControlHovered = false;
    private bool _deleteControlHovered = false;

    // Text input state (simple focus tracking)
    private string _jsonFileName = "new_device.json";

    // Save status for footer display
    private string? _lastSaveMessage;
    private DateTime _lastSaveTime;

    // Window dragging
    private bool _isDragging = false;
    private Point _dragStart;
    private const int TitleBarHeight = 40;

    public DeviceMapEditorForm()
    {
        InitializeForm();
        InitializeCanvas();
        InitializeTimer();
        LoadAvailableSvgFiles();
    }

    private void InitializeForm()
    {
        Text = "Device Map Editor";
        float s = FUIRenderer.CanvasScaleFactor;
        Size = new Size((int)(1400 * s), (int)(900 * s));
        MinimumSize = new Size((int)(1000 * s), (int)(700 * s));
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        KeyPreview = true;
    }

    private void InitializeCanvas()
    {
        _canvas = new SKControl { Dock = DockStyle.Fill };
        _canvas.PaintSurface += OnPaintSurface;
        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseDown += OnMouseDown;
        _canvas.MouseUp += OnMouseUp;
        _canvas.MouseWheel += OnMouseWheel;
        Controls.Add(_canvas);
    }

    private void InitializeTimer()
    {
        _renderTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _renderTimer.Tick += (s, e) =>
        {
            _pulsePhase += 0.05f;
            _canvas.Invalidate();
        };
        _renderTimer.Start();
    }

    private void LoadAvailableSvgFiles()
    {
        _imagesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Devices");
        if (Directory.Exists(_imagesDir))
        {
            _availableSvgFiles = Directory.GetFiles(_imagesDir, "*.svg")
                .Select(Path.GetFileName)
                .Where(f => f is not null)
                .Cast<string>()
                .ToList();
        }

        // Load first SVG if available
        if (_availableSvgFiles.Count > 0)
        {
            LoadSvgFile(_availableSvgFiles[0]);
            _deviceMap.SvgFile = _availableSvgFiles[0];
        }
    }

    private void LoadSvgFile(string fileName)
    {
        var path = Path.Combine(_imagesDir, fileName);
        if (File.Exists(path))
        {
            _currentSvg = new SKSvg();
            _currentSvg.Load(path);
        }
    }

    private void LoadJsonFile(string path)
    {
        var map = DeviceMap.Load(path);
        if (map is not null)
        {
            _deviceMap = map;
            _currentJsonPath = path;
            _jsonFileName = Path.GetFileName(path);
            _hasUnsavedChanges = false;
            _selectedControlKey = null;

            // Load corresponding SVG
            if (!string.IsNullOrEmpty(map.SvgFile))
            {
                LoadSvgFile(map.SvgFile);
            }

            // Debug: Show where file was loaded from
            System.Diagnostics.Debug.WriteLine($"Loaded from: {path}");
        }
        else
        {
            FUIMessageBox.ShowError(this, $"Failed to load file:\n{path}", "Load Error");
        }
    }

    private void SaveJsonFile()
    {
        try
        {
            string savePath;

            // If we have a current path from loading, use that directory
            // Otherwise show save dialog
            if (!string.IsNullOrEmpty(_currentJsonPath) && File.Exists(_currentJsonPath))
            {
                savePath = _currentJsonPath;
            }
            else
            {
                // Try to find source directory for Maps
                var sourceDir = FindSourceMapsDirectory();

                using var sfd = new SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    FileName = _jsonFileName,
                    InitialDirectory = sourceDir ?? Path.Combine(_imagesDir, "Maps")
                };

                if (sfd.ShowDialog() != DialogResult.OK)
                    return;

                savePath = sfd.FileName;
                _jsonFileName = Path.GetFileName(savePath);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(_deviceMap, options);
            File.WriteAllText(savePath, json);
            _currentJsonPath = savePath;
            _hasUnsavedChanges = false;
            _lastSaveMessage = $"Saved to {Path.GetFileName(savePath)}";
            _lastSaveTime = DateTime.Now;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            FUIMessageBox.ShowError(this, $"Failed to save: {ex.Message}", "Save Error");
        }
    }

    private string? FindSourceMapsDirectory()
    {
        // Try to find the source directory by walking up from bin folder
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;

            var srcPath = Path.Combine(dir, "src", "Asteriq", "Images", "Devices", "Maps");
            if (Directory.Exists(srcPath))
                return srcPath;
        }
        return null;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            if (_hasUnsavedChanges)
            {
                var result = FUIMessageBox.ShowQuestion(this, "You have unsaved changes. Discard and close?", "Unsaved Changes");
                if (!result)
                    return true;
            }
            Close();
            return true;
        }

        if (keyData == (Keys.Control | Keys.S))
        {
            SaveJsonFile();
            return true;
        }

        if (keyData == Keys.Delete && _selectedControlKey is not null)
        {
            _deviceMap.Controls.Remove(_selectedControlKey);
            _selectedControlKey = null;
            _hasUnsavedChanges = true;
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    #region Painting

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        float scale = FUIRenderer.CanvasScaleFactor;
        canvas.Scale(scale);
        var bounds = new SKRect(0, 0, e.Info.Width / scale, e.Info.Height / scale);

        // Clear background
        canvas.Clear(FUIColors.Background0);

        // Calculate layout regions
        CalculateLayout(bounds);

        // Draw components
        DrawTitleBar(canvas, bounds);
        DrawToolbar(canvas);
        DrawSvgPanel(canvas);
        DrawPropertiesPanel(canvas);
        DrawControlsList(canvas);
        DrawStatusBar(canvas);

        // Draw SVG dropdown if open (on top of everything)
        if (_svgDropdownOpen)
        {
            DrawSvgDropdownMenu(canvas);
        }
    }

    private void CalculateLayout(SKRect bounds)
    {
        float titleHeight = TitleBarHeight;
        float toolbarHeight = 50;
        float statusHeight = 30;
        float rightPanelWidth = 320;

        _toolbarBounds = new SKRect(0, titleHeight, bounds.Right, titleHeight + toolbarHeight);
        _statusBarBounds = new SKRect(0, bounds.Bottom - statusHeight, bounds.Right, bounds.Bottom);

        float contentTop = _toolbarBounds.Bottom;
        float contentBottom = _statusBarBounds.Top;

        _propertiesPanelBounds = new SKRect(bounds.Right - rightPanelWidth, contentTop,
            bounds.Right, contentBottom * 0.6f);
        _controlsListBounds = new SKRect(bounds.Right - rightPanelWidth, _propertiesPanelBounds.Bottom,
            bounds.Right, contentBottom);
        _svgPanelBounds = new SKRect(0, contentTop, bounds.Right - rightPanelWidth, contentBottom);
    }

    private void DrawTitleBar(SKCanvas canvas, SKRect bounds)
    {
        var titleBounds = new SKRect(0, 0, bounds.Right, TitleBarHeight);

        // Background
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1 };
        canvas.DrawRect(titleBounds, bgPaint);

        // Title
        var title = "DEVICE MAP EDITOR" + (_hasUnsavedChanges ? " *" : "");
        FUIRenderer.DrawText(canvas, title, new SKPoint(20, 26), FUIColors.Active, 14f, true);

        // Close button
        var closeRect = new SKRect(bounds.Right - 40, 8, bounds.Right - 8, 32);
        FUIRenderer.DrawTextCentered(canvas, "X", closeRect, FUIColors.TextDim, 14f);
    }

    private void DrawToolbar(SKCanvas canvas)
    {
        // Background
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1.WithAlpha(200) };
        canvas.DrawRect(_toolbarBounds, bgPaint);

        float y = _toolbarBounds.Top + 10;
        float x = 20;

        // SVG dropdown
        FUIRenderer.DrawText(canvas, "SVG:", new SKPoint(x, y + 12), FUIColors.TextDim, 10f);
        x += 36;

        _svgDropdownBounds = new SKRect(x, y, x + 180, y + 30);
        DrawDropdown(canvas, _svgDropdownBounds, _deviceMap.SvgFile ?? "Select SVG...", _svgDropdownOpen);
        x += 200;

        // JSON filename
        FUIRenderer.DrawText(canvas, "JSON:", new SKPoint(x, y + 12), FUIColors.TextDim, 10f);
        x += 40;

        _jsonTextBoxBounds = new SKRect(x, y, x + 200, y + 30);
        DrawTextBox(canvas, _jsonTextBoxBounds, _jsonFileName);
        x += 220;

        // New button
        _newButtonBounds = new SKRect(x, y, x + 60, y + 30);
        DrawButton(canvas, _newButtonBounds, "NEW", _newButtonHovered);
        x += 70;

        // Load button
        _loadButtonBounds = new SKRect(x, y, x + 60, y + 30);
        DrawButton(canvas, _loadButtonBounds, "LOAD", _loadButtonHovered);
        x += 80;

        // Mirror checkbox
        FUIRenderer.DrawText(canvas, "Mirror (L):", new SKPoint(x, y + 12), FUIColors.TextDim, 10f);
        x += 65;
        _mirrorCheckboxBounds = new SKRect(x, y + 3, x + 24, y + 27);
        DrawCheckbox(canvas, _mirrorCheckboxBounds, _deviceMap.Mirror, _mirrorCheckboxHovered);
        x += 40;

        // Save button (right side)
        _saveButtonBounds = new SKRect(_toolbarBounds.Right - 100, y, _toolbarBounds.Right - 20, y + 30);
        DrawButton(canvas, _saveButtonBounds, "SAVE", _saveButtonHovered, true);
    }

    private void DrawCheckbox(SKCanvas canvas, SKRect bounds, bool isChecked, bool hovered)
    {
        var bgColor = hovered ? FUIColors.Primary.WithAlpha(40) : FUIColors.Background2;
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRoundRect(bounds, 4, 4, bgPaint);

        var frameColor = isChecked ? FUIColors.Active : (hovered ? FUIColors.Primary : FUIColors.Frame);
        using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = frameColor, StrokeWidth = hovered ? 2f : 1f };
        canvas.DrawRoundRect(bounds, 4, 4, framePaint);

        if (isChecked)
        {
            // Draw checkmark
            using var checkPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.Active,
                StrokeWidth = 2.5f,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round
            };
            float cx = bounds.MidX;
            float cy = bounds.MidY;
            canvas.DrawLine(cx - 5, cy, cx - 1, cy + 4, checkPaint);
            canvas.DrawLine(cx - 1, cy + 4, cx + 6, cy - 4, checkPaint);
        }
    }

    private void DrawSvgPanel(SKCanvas canvas)
    {
        // Background
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background0 };
        canvas.DrawRect(_svgPanelBounds, bgPaint);

        // Frame
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(_svgPanelBounds, framePaint);

        // Draw SVG
        if (_currentSvg?.Picture is not null)
        {
            var svgBounds = new SKRect(_svgPanelBounds.Left + 10, _svgPanelBounds.Top + 10,
                _svgPanelBounds.Right - 10, _svgPanelBounds.Bottom - 10);
            DrawSvgInBounds(canvas, _currentSvg, svgBounds);
        }
        else
        {
            FUIRenderer.DrawTextCentered(canvas, "No SVG loaded", _svgPanelBounds, FUIColors.TextDisabled, 14f);
        }

        // Draw control overlays
        DrawControlOverlays(canvas);
    }

    private void DrawSvgInBounds(SKCanvas canvas, SKSvg svg, SKRect bounds)
    {
        var svgBounds = svg.Picture!.CullRect;

        float scaleX = bounds.Width / svgBounds.Width;
        float scaleY = bounds.Height / svgBounds.Height;
        float scale = Math.Min(scaleX, scaleY) * 0.95f;

        float scaledWidth = svgBounds.Width * scale;
        float scaledHeight = svgBounds.Height * scale;
        float offsetX = bounds.Left + (bounds.Width - scaledWidth) / 2 - svgBounds.Left * scale;
        float offsetY = bounds.Top + (bounds.Height - scaledHeight) / 2 - svgBounds.Top * scale;

        _svgScale = scale;
        _svgOffset = new SKPoint(offsetX, offsetY);
        _svgScaledWidth = scaledWidth;

        canvas.Save();
        canvas.Translate(offsetX, offsetY);

        if (_deviceMap.Mirror)
        {
            // Flip horizontally: translate to right edge, then scale X by -1
            canvas.Translate(scaledWidth, 0);
            canvas.Scale(-scale, scale);
        }
        else
        {
            canvas.Scale(scale);
        }

        canvas.DrawPicture(svg.Picture);
        canvas.Restore();
    }

    private void DrawControlOverlays(SKCanvas canvas)
    {
        foreach (var kvp in _deviceMap.Controls)
        {
            var key = kvp.Key;
            var control = kvp.Value;

            if (control.Anchor is null) continue;

            bool isSelected = key == _selectedControlKey;
            var anchorScreen = ViewBoxToScreen(control.Anchor.X, control.Anchor.Y);

            // Draw anchor point
            float radius = isSelected ? 10f : 6f;
            using var anchorPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = isSelected ? FUIColors.Active : FUIColors.Primary.WithAlpha(180)
            };
            canvas.DrawCircle(anchorScreen, radius, anchorPaint);

            // Draw anchor outline
            using var outlinePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = isSelected ? FUIColors.Active : FUIColors.Frame,
                StrokeWidth = 2f
            };
            canvas.DrawCircle(anchorScreen, radius, outlinePaint);

            // Calculate label position (offset from anchor in viewbox coords)
            float labelX = control.Anchor.X + (control.LabelOffset?.X ?? 50);
            float labelY = control.Anchor.Y + (control.LabelOffset?.Y ?? 0);
            var labelScreen = ViewBoxToScreen(labelX, labelY);

            // Measure label text
            var labelText = control.Label ?? key;
            var labelBounds = MeasureText(labelText, 11f);
            float padding = 6f;
            float shelfLength = 10f;

            // Determine if anchor is to the left or right of the label
            bool anchorOnLeft = anchorScreen.X < labelScreen.X;

            // Calculate label box - text position is top-left of text
            var labelRect = new SKRect(
                labelScreen.X - padding,
                labelScreen.Y - padding,
                labelScreen.X + labelBounds.Width + padding,
                labelScreen.Y + labelBounds.Height + padding);

            // Shelf extends horizontally from the appropriate side of the label box
            SKPoint shelfStart, shelfEnd;
            if (anchorOnLeft)
            {
                // Shelf extends left from left edge of label box
                shelfStart = new SKPoint(labelRect.Left, labelRect.MidY);
                shelfEnd = new SKPoint(labelRect.Left - shelfLength, labelRect.MidY);
            }
            else
            {
                // Shelf extends right from right edge of label box
                shelfStart = new SKPoint(labelRect.Right, labelRect.MidY);
                shelfEnd = new SKPoint(labelRect.Right + shelfLength, labelRect.MidY);
            }

            // Draw lead line (connects to end of shelf)
            DrawLeadLine(canvas, anchorScreen, shelfEnd, control.LeadLine, isSelected);

            // Draw the shelf connector line
            using var shelfPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = isSelected ? FUIColors.Active : FUIColors.Primary.WithAlpha(150),
                StrokeWidth = isSelected ? 2f : 1f,
                IsAntialias = true
            };
            canvas.DrawLine(shelfStart, shelfEnd, shelfPaint);

            // Draw label background
            using var labelBgPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = isSelected ? FUIColors.Active.WithAlpha(40) : FUIColors.Background1.WithAlpha(200)
            };
            canvas.DrawRoundRect(labelRect, 3, 3, labelBgPaint);

            using var labelFramePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = isSelected ? FUIColors.Active : FUIColors.Frame,
                StrokeWidth = 1f
            };
            canvas.DrawRoundRect(labelRect, 3, 3, labelFramePaint);

            // Draw label text (baseline adjusted)
            using var textPaint = new SKPaint { TextSize = 11f, IsAntialias = true };
            var metrics = textPaint.FontMetrics;
            float textY = labelScreen.Y - metrics.Ascent; // Adjust for baseline
            FUIRenderer.DrawText(canvas, labelText, new SKPoint(labelScreen.X, textY),
                isSelected ? FUIColors.Active : FUIColors.TextPrimary, 11f);
        }
    }

    private void DrawLeadLine(SKCanvas canvas, SKPoint anchor, SKPoint label, LeadLineDefinition? leadLine, bool selected)
    {
        // Only track handle positions for the selected control
        if (selected)
        {
            _segmentEndHandles.Clear();
            _shelfEndHandle = default;
        }

        using var linePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = selected ? FUIColors.Active : FUIColors.Primary.WithAlpha(150),
            StrokeWidth = selected ? 2f : 1f,
            IsAntialias = true
        };

        if (leadLine is null)
        {
            // Simple straight line
            canvas.DrawLine(anchor, label, linePaint);
            return;
        }

        // Build path from lead line definition
        var path = new SKPath();
        path.MoveTo(anchor);

        // Shelf segment - when mirrored, screen direction is inverted
        bool goesRight = leadLine.ShelfSide == "right";
        // When mirrored, a "right" shelf in viewbox goes LEFT on screen
        bool screenGoesRight = _deviceMap.Mirror ? !goesRight : goesRight;
        float shelfEndX = anchor.X + (screenGoesRight ? 1 : -1) * leadLine.ShelfLength * _svgScale;
        var shelfEnd = new SKPoint(shelfEndX, anchor.Y);
        path.LineTo(shelfEnd);

        // Store shelf end handle position (only for selected)
        if (selected)
        {
            _shelfEndHandle = shelfEnd;
        }

        // Additional segments
        var currentPoint = shelfEnd;
        if (leadLine.Segments is not null)
        {
            foreach (var seg in leadLine.Segments)
            {
                float angleRad = seg.Angle * MathF.PI / 180f;
                float dx = MathF.Cos(angleRad) * seg.Length * _svgScale;
                float dy = -MathF.Sin(angleRad) * seg.Length * _svgScale; // Y is inverted

                if (!screenGoesRight) dx = -dx; // Mirror for left shelf (screen direction)

                currentPoint = new SKPoint(currentPoint.X + dx, currentPoint.Y + dy);
                path.LineTo(currentPoint);

                // Store segment end handle position (only for selected)
                if (selected)
                {
                    _segmentEndHandles.Add(currentPoint);
                }
            }
        }

        // Always draw final connector to label
        path.LineTo(label);

        canvas.DrawPath(path, linePaint);

        // Draw draggable handles only for selected control WITH segments
        // (without segments, the lead line is in "simple mode" - shelf + auto line to label)
        // The handles let users control segment angles/lengths; the final connector to label is automatic
        if (selected && leadLine.Segments is not null && leadLine.Segments.Count > 0)
        {
            DrawSegmentHandles(canvas, anchor, leadLine);
        }
    }

    private void DrawSegmentHandles(SKCanvas canvas, SKPoint anchor, LeadLineDefinition leadLine)
    {
        using var handleFill = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Active.WithAlpha(180),
            IsAntialias = true
        };

        using var handleStroke = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active,
            StrokeWidth = 2f,
            IsAntialias = true
        };

        using var handleHover = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Primary.WithAlpha(100),
            IsAntialias = true
        };

        // Draw shelf end handle
        bool shelfHovered = SKPoint.Distance(_shelfEndHandle, _mousePos) < HandleHitRadius;
        if (shelfHovered)
        {
            canvas.DrawCircle(_shelfEndHandle, HandleRadius + 3, handleHover);
        }
        canvas.DrawCircle(_shelfEndHandle, HandleRadius, handleFill);
        canvas.DrawCircle(_shelfEndHandle, HandleRadius, handleStroke);

        // Draw segment end handles
        for (int i = 0; i < _segmentEndHandles.Count; i++)
        {
            var handlePos = _segmentEndHandles[i];
            bool segHovered = SKPoint.Distance(handlePos, _mousePos) < HandleHitRadius;
            if (segHovered)
            {
                canvas.DrawCircle(handlePos, HandleRadius + 3, handleHover);
            }
            canvas.DrawCircle(handlePos, HandleRadius, handleFill);
            canvas.DrawCircle(handlePos, HandleRadius, handleStroke);

            // Draw segment index number
            FUIRenderer.DrawTextCentered(canvas, (i + 1).ToString(),
                new SKRect(handlePos.X - 10, handlePos.Y - 10, handlePos.X + 10, handlePos.Y + 10),
                FUIColors.Background0, 9f);
        }
    }

    private void DrawPropertiesPanel(SKCanvas canvas)
    {
        // Background
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1 };
        canvas.DrawRect(_propertiesPanelBounds, bgPaint);

        // Frame
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(_propertiesPanelBounds, framePaint);

        // Header
        float y = _propertiesPanelBounds.Top + 16;
        FUIRenderer.DrawText(canvas, "CONTROL PROPERTIES", new SKPoint(_propertiesPanelBounds.Left + 16, y),
            FUIColors.Active, 11f, true);
        y += 24;

        if (_selectedControlKey is null)
        {
            FUIRenderer.DrawText(canvas, "Select a control or click on SVG",
                new SKPoint(_propertiesPanelBounds.Left + 16, y), FUIColors.TextDisabled, 10f);
            FUIRenderer.DrawText(canvas, "to add a new control anchor.",
                new SKPoint(_propertiesPanelBounds.Left + 16, y + 16), FUIColors.TextDisabled, 10f);
            return;
        }

        if (!_deviceMap.Controls.TryGetValue(_selectedControlKey, out var control))
            return;

        float leftMargin = _propertiesPanelBounds.Left + 16;
        float rightMargin = _propertiesPanelBounds.Right - 16;
        float labelWidth = 70;

        // Control Key (read-only for now)
        DrawPropertyRow(canvas, ref y, "Key:", _selectedControlKey, leftMargin, labelWidth, rightMargin, true);

        // Label
        DrawPropertyRow(canvas, ref y, "Label:", control.Label, leftMargin, labelWidth, rightMargin, false);

        // Type
        DrawPropertyRow(canvas, ref y, "Type:", control.Type, leftMargin, labelWidth, rightMargin, false);

        // Bindings
        var bindingsText = control.Bindings is not null ? string.Join(", ", control.Bindings) : "";
        DrawPropertyRow(canvas, ref y, "Bindings:", bindingsText, leftMargin, labelWidth, rightMargin, false);

        y += 10;
        FUIRenderer.DrawText(canvas, "POSITION", new SKPoint(leftMargin, y), FUIColors.TextDim, 9f);
        y += 18;

        // Anchor
        var anchorText = control.Anchor is not null ? $"X: {control.Anchor.X:F0}  Y: {control.Anchor.Y:F0}" : "Not set";
        DrawPropertyRow(canvas, ref y, "Anchor:", anchorText, leftMargin, labelWidth, rightMargin, true);

        // Offset
        var offsetText = control.LabelOffset is not null ? $"X: {control.LabelOffset.X:F0}  Y: {control.LabelOffset.Y:F0}" : "0, 0";
        DrawPropertyRow(canvas, ref y, "Offset:", offsetText, leftMargin, labelWidth, rightMargin, true);

        y += 10;
        FUIRenderer.DrawText(canvas, "LEAD LINE", new SKPoint(leftMargin, y), FUIColors.TextDim, 9f);
        y += 18;

        var ll = control.LeadLine;

        // Shelf Side with toggle button
        FUIRenderer.DrawText(canvas, "Side:", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        _shelfSideButtonBounds = new SKRect(leftMargin + labelWidth, y - 12, leftMargin + labelWidth + 60, y + 8);
        DrawSmallButton(canvas, _shelfSideButtonBounds, ll?.ShelfSide ?? "right");
        y += 24;

        // Shelf Length with +/- buttons
        FUIRenderer.DrawText(canvas, "Shelf:", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        float btnX = leftMargin + labelWidth;
        _shelfLengthMinusBounds = new SKRect(btnX, y - 12, btnX + 24, y + 8);
        DrawSmallButton(canvas, _shelfLengthMinusBounds, "-");
        FUIRenderer.DrawText(canvas, (ll?.ShelfLength ?? 80).ToString("F0"), new SKPoint(btnX + 32, y), FUIColors.TextPrimary, 10f);
        _shelfLengthPlusBounds = new SKRect(btnX + 60, y - 12, btnX + 84, y + 8);
        DrawSmallButton(canvas, _shelfLengthPlusBounds, "+");
        y += 24;

        // Segments section
        FUIRenderer.DrawText(canvas, "Segments:", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        _addSegmentButtonBounds = new SKRect(rightMargin - 50, y - 12, rightMargin, y + 8);
        DrawSmallButton(canvas, _addSegmentButtonBounds, "+ Add");
        y += 22;

        _segmentButtonBounds.Clear();
        if (ll?.Segments is not null && ll.Segments.Count > 0)
        {
            for (int i = 0; i < ll.Segments.Count; i++)
            {
                var seg = ll.Segments[i];

                // Segment label
                FUIRenderer.DrawText(canvas, $"  {i + 1}:", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);

                // Angle controls
                float angX = leftMargin + 30;
                FUIRenderer.DrawText(canvas, "A:", new SKPoint(angX, y), FUIColors.TextDim, 9f);
                var angMinus = new SKRect(angX + 16, y - 10, angX + 36, y + 6);
                DrawSmallButton(canvas, angMinus, "-");
                FUIRenderer.DrawText(canvas, seg.Angle.ToString("F0"), new SKPoint(angX + 40, y), FUIColors.TextPrimary, 9f);
                var angPlus = new SKRect(angX + 65, y - 10, angX + 85, y + 6);
                DrawSmallButton(canvas, angPlus, "+");

                // Length controls
                float lenX = angX + 95;
                FUIRenderer.DrawText(canvas, "L:", new SKPoint(lenX, y), FUIColors.TextDim, 9f);
                var lenMinus = new SKRect(lenX + 16, y - 10, lenX + 36, y + 6);
                DrawSmallButton(canvas, lenMinus, "-");
                FUIRenderer.DrawText(canvas, seg.Length.ToString("F0"), new SKPoint(lenX + 40, y), FUIColors.TextPrimary, 9f);
                var lenPlus = new SKRect(lenX + 65, y - 10, lenX + 85, y + 6);
                DrawSmallButton(canvas, lenPlus, "+");

                _segmentButtonBounds.Add((angMinus, angPlus, lenMinus, lenPlus));
                y += 20;
            }

            // Remove segment button
            _removeSegmentButtonBounds = new SKRect(leftMargin + 30, y - 2, leftMargin + 100, y + 14);
            DrawSmallButton(canvas, _removeSegmentButtonBounds, "- Remove");
            y += 20;
        }
        else
        {
            FUIRenderer.DrawText(canvas, "  (none - adds straight line)", new SKPoint(leftMargin, y), FUIColors.TextDisabled, 9f);
            y += 20;
        }

        // Angle guide
        y += 5;
        FUIRenderer.DrawText(canvas, "Angles: 0=horiz, 90=up, -90=down", new SKPoint(leftMargin, y), FUIColors.TextDisabled, 8f);
        y += 12;
        FUIRenderer.DrawText(canvas, "        45=diag-up, -45=diag-down", new SKPoint(leftMargin, y), FUIColors.TextDisabled, 8f);
    }

    private void DrawSmallButton(SKCanvas canvas, SKRect bounds, string text)
    {
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        canvas.DrawRoundRect(bounds, 3, 3, bgPaint);

        using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
        canvas.DrawRoundRect(bounds, 3, 3, framePaint);

        FUIRenderer.DrawTextCentered(canvas, text, bounds, FUIColors.TextPrimary, 9f);
    }


    private void DrawPropertyRow(SKCanvas canvas, ref float y, string label, string value,
        float leftMargin, float labelWidth, float rightMargin, bool readOnly)
    {
        FUIRenderer.DrawText(canvas, label, new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);

        var valueBounds = new SKRect(leftMargin + labelWidth, y - 12, rightMargin, y + 8);

        if (!readOnly)
        {
            using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
            canvas.DrawRoundRect(valueBounds, 3, 3, bgPaint);

            using var framePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.Frame,
                StrokeWidth = 1f
            };
            canvas.DrawRoundRect(valueBounds, 3, 3, framePaint);
        }

        FUIRenderer.DrawText(canvas, value, new SKPoint(leftMargin + labelWidth + 4, y),
            readOnly ? FUIColors.TextPrimary : FUIColors.TextPrimary, 10f);

        y += 24;
    }

    private void DrawControlsList(SKCanvas canvas)
    {
        // Background
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1 };
        canvas.DrawRect(_controlsListBounds, bgPaint);

        // Frame
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(_controlsListBounds, framePaint);

        // Header
        float headerY = _controlsListBounds.Top + 16;
        FUIRenderer.DrawText(canvas, "CONTROLS", new SKPoint(_controlsListBounds.Left + 16, headerY),
            FUIColors.Active, 11f, true);

        // Content area (between header and buttons)
        float contentTop = _controlsListBounds.Top + 40;
        float contentBottom = _controlsListBounds.Bottom - 50;
        var contentBounds = new SKRect(_controlsListBounds.Left, contentTop, _controlsListBounds.Right, contentBottom);

        // Calculate total content height
        float itemHeight = 24;
        float itemGap = 2;
        float totalContentHeight = _deviceMap.Controls.Count * (itemHeight + itemGap);

        // Clamp scroll offset
        float maxScroll = Math.Max(0, totalContentHeight - contentBounds.Height);
        _controlsListScroll = Math.Clamp(_controlsListScroll, 0, maxScroll);

        // Clip to content area
        canvas.Save();
        canvas.ClipRect(contentBounds);

        // Control list items
        _controlListItemBounds.Clear();
        float y = contentTop - _controlsListScroll;
        int index = 0;

        foreach (var kvp in _deviceMap.Controls)
        {
            var itemBounds = new SKRect(_controlsListBounds.Left + 10, y,
                _controlsListBounds.Right - 10, y + itemHeight);
            _controlListItemBounds.Add(itemBounds);

            // Only draw if visible
            if (y + itemHeight > contentTop && y < contentBottom)
            {
                bool isSelected = kvp.Key == _selectedControlKey;
                bool isHovered = index == _hoveredControlListItem;

                if (isSelected || isHovered)
                {
                    using var hlPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Fill,
                        Color = isSelected ? FUIColors.Active.WithAlpha(40) : FUIColors.Primary.WithAlpha(20)
                    };
                    canvas.DrawRoundRect(itemBounds, 3, 3, hlPaint);
                }

                var indicator = isSelected ? "> " : "  ";
                FUIRenderer.DrawText(canvas, indicator + kvp.Key, new SKPoint(itemBounds.Left + 5, y + 14),
                    isSelected ? FUIColors.Active : FUIColors.TextPrimary, 10f);
            }

            y += itemHeight + itemGap;
            index++;
        }

        canvas.Restore();

        // Draw scroll indicator if needed
        if (totalContentHeight > contentBounds.Height)
        {
            float scrollbarHeight = (contentBounds.Height / totalContentHeight) * contentBounds.Height;
            float scrollbarY = contentTop + (_controlsListScroll / totalContentHeight) * contentBounds.Height;
            var scrollbarBounds = new SKRect(_controlsListBounds.Right - 6, scrollbarY,
                _controlsListBounds.Right - 2, scrollbarY + scrollbarHeight);

            using var scrollPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Frame.WithAlpha(100) };
            canvas.DrawRoundRect(scrollbarBounds, 2, 2, scrollPaint);
        }

        // Add/Delete buttons
        float btnY = _controlsListBounds.Bottom - 40;
        float btnWidth = 80;
        float btnGap = 12;
        float btnX = _controlsListBounds.Left + 16;

        _addControlButtonBounds = new SKRect(btnX, btnY, btnX + btnWidth, btnY + 26);
        DrawButton(canvas, _addControlButtonBounds, "+ ADD", _addControlHovered);

        _deleteControlButtonBounds = new SKRect(btnX + btnWidth + btnGap, btnY,
            btnX + btnWidth * 2 + btnGap, btnY + 26);
        DrawButton(canvas, _deleteControlButtonBounds, "DELETE", _deleteControlHovered);
    }

    private void DrawStatusBar(SKCanvas canvas)
    {
        // Background
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1 };
        canvas.DrawRect(_statusBarBounds, bgPaint);

        // Cursor position
        var cursorText = $"Cursor: ({_mouseViewBox.X:F0}, {_mouseViewBox.Y:F0})";
        FUIRenderer.DrawText(canvas, cursorText, new SKPoint(20, _statusBarBounds.Top + 18), FUIColors.TextDim, 10f);

        // ViewBox info
        var viewBoxText = $"ViewBox: {_deviceMap.ViewBox?.X ?? 2048}x{_deviceMap.ViewBox?.Y ?? 2048}";
        FUIRenderer.DrawText(canvas, viewBoxText, new SKPoint(220, _statusBarBounds.Top + 18), FUIColors.TextDim, 10f);

        // Control count
        var countText = $"Controls: {_deviceMap.Controls.Count}";
        FUIRenderer.DrawText(canvas, countText, new SKPoint(420, _statusBarBounds.Top + 18), FUIColors.TextDim, 10f);

        // Show save message for 3 seconds, otherwise show help hint
        if (_lastSaveMessage is not null && (DateTime.Now - _lastSaveTime).TotalSeconds < 3)
        {
            FUIRenderer.DrawText(canvas, _lastSaveMessage, new SKPoint(580, _statusBarBounds.Top + 18), FUIColors.Active, 10f);
        }
        else
        {
            // Help hint - context sensitive
            string helpText;
            if (_selectedControlKey is not null)
            {
                // Check if hovering over a segment handle
                bool hoveringSegment = false;
                for (int i = 0; i < _segmentEndHandles.Count; i++)
                {
                    if (SKPoint.Distance(_segmentEndHandles[i], _mousePos) < HandleHitRadius)
                    {
                        hoveringSegment = true;
                        break;
                    }
                }
                helpText = hoveringSegment
                    ? "Drag to move, Right-click to remove"
                    : "Shift+Click to reposition anchor";
            }
            else
            {
                helpText = "Click on SVG to add control";
            }
            FUIRenderer.DrawText(canvas, helpText, new SKPoint(580, _statusBarBounds.Top + 18), FUIColors.TextDisabled, 10f);
        }
    }

    private void DrawDropdown(SKCanvas canvas, SKRect bounds, string text, bool open)
    {
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = open ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2
        };
        canvas.DrawRoundRect(bounds, 4, 4, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = open ? FUIColors.Primary : FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRoundRect(bounds, 4, 4, framePaint);

        FUIRenderer.DrawText(canvas, text, new SKPoint(bounds.Left + 8, bounds.MidY + 4), FUIColors.TextPrimary, 10f);

        // Dropdown arrow
        var arrow = open ? "^" : "v";
        FUIRenderer.DrawText(canvas, arrow, new SKPoint(bounds.Right - 18, bounds.MidY + 4), FUIColors.TextDim, 10f);
    }

    private void DrawSvgDropdownMenu(SKCanvas canvas)
    {
        float itemHeight = 28;  // 4px aligned
        float menuHeight = _availableSvgFiles.Count * itemHeight + 10;
        var menuBounds = new SKRect(_svgDropdownBounds.Left, _svgDropdownBounds.Bottom + 2,
            _svgDropdownBounds.Right, _svgDropdownBounds.Bottom + 2 + menuHeight);

        // Background
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1 };
        canvas.DrawRoundRect(menuBounds, 4, 4, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Primary,
            StrokeWidth = 1f
        };
        canvas.DrawRoundRect(menuBounds, 4, 4, framePaint);

        _svgDropdownItemBounds.Clear();
        float y = menuBounds.Top + 5;

        for (int i = 0; i < _availableSvgFiles.Count; i++)
        {
            var itemBounds = new SKRect(menuBounds.Left + 5, y, menuBounds.Right - 5, y + itemHeight - 2);
            _svgDropdownItemBounds.Add(itemBounds);

            bool hovered = i == _hoveredSvgDropdownItem;
            if (hovered)
            {
                using var hlPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Primary.WithAlpha(40) };
                canvas.DrawRoundRect(itemBounds, 3, 3, hlPaint);
            }

            FUIRenderer.DrawText(canvas, _availableSvgFiles[i], new SKPoint(itemBounds.Left + 5, y + 16),
                hovered ? FUIColors.TextPrimary : FUIColors.TextDim, 10f);

            y += itemHeight;
        }
    }

    private void DrawTextBox(SKCanvas canvas, SKRect bounds, string text)
    {
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        canvas.DrawRoundRect(bounds, 4, 4, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRoundRect(bounds, 4, 4, framePaint);

        FUIRenderer.DrawText(canvas, text, new SKPoint(bounds.Left + 8, bounds.MidY + 4), FUIColors.TextPrimary, 10f);
    }

    private void DrawButton(SKCanvas canvas, SKRect bounds, string text, bool hovered, bool primary = false)
    {
        var bgColor = primary
            ? (hovered ? FUIColors.Active.WithAlpha(60) : FUIColors.Active.WithAlpha(30))
            : (hovered ? FUIColors.Primary.WithAlpha(40) : FUIColors.Background2);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRoundRect(bounds, 4, 4, bgPaint);

        var frameColor = primary ? FUIColors.Active : (hovered ? FUIColors.Primary : FUIColors.Frame);
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = frameColor,
            StrokeWidth = hovered ? 2f : 1f
        };
        canvas.DrawRoundRect(bounds, 4, 4, framePaint);

        var textColor = primary ? FUIColors.Active : (hovered ? FUIColors.TextPrimary : FUIColors.TextDim);
        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 10f);
    }

    private SKRect MeasureText(string text, float size)
    {
        // Use the same scaled size as FUIRenderer.DrawText
        float scaledSize = size;
        using var paint = new SKPaint { TextSize = scaledSize, IsAntialias = true };
        var width = paint.MeasureText(text);
        var metrics = paint.FontMetrics;
        var height = metrics.Descent - metrics.Ascent;
        return new SKRect(0, 0, width, height);
    }

    #endregion

    #region Coordinate Conversion

    private SKPoint ViewBoxToScreen(float viewBoxX, float viewBoxY)
    {
        float screenX, screenY;

        if (_deviceMap.Mirror)
        {
            // When mirrored, X is inverted: viewBox 0 -> right edge, viewBox max -> left edge
            screenX = _svgOffset.X + _svgScaledWidth - viewBoxX * _svgScale;
        }
        else
        {
            screenX = _svgOffset.X + viewBoxX * _svgScale;
        }

        screenY = _svgOffset.Y + viewBoxY * _svgScale;
        return new SKPoint(screenX, screenY);
    }

    private SKPoint ScreenToViewBox(float screenX, float screenY)
    {
        float viewBoxX, viewBoxY;

        if (_deviceMap.Mirror)
        {
            // When mirrored, invert X conversion
            viewBoxX = (_svgOffset.X + _svgScaledWidth - screenX) / _svgScale;
        }
        else
        {
            viewBoxX = (screenX - _svgOffset.X) / _svgScale;
        }

        viewBoxY = (screenY - _svgOffset.Y) / _svgScale;
        return new SKPoint(viewBoxX, viewBoxY);
    }

    #endregion

    #region Mouse Handling

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        float s = FUIRenderer.CanvasScaleFactor;
        float mx = e.X / s, my = e.Y / s;
        _mousePos = new SKPoint(mx, my);
        _mouseViewBox = ScreenToViewBox(mx, my);

        // Window dragging (use raw coords for PointToScreen)
        if (_isDragging)
        {
            var newLocation = PointToScreen(new Point(e.X - _dragStart.X, e.Y - _dragStart.Y));
            Location = newLocation;
            return;
        }

        // Dragging anchor, label, or segment handles
        if (_dragMode != DragMode.None && _selectedControlKey is not null)
        {
            if (_deviceMap.Controls.TryGetValue(_selectedControlKey, out var control))
            {
                if (_dragMode == DragMode.Anchor)
                {
                    control.Anchor ??= new Point2D();
                    control.Anchor.X = _mouseViewBox.X;
                    control.Anchor.Y = _mouseViewBox.Y;
                    _hasUnsavedChanges = true;
                }
                else if (_dragMode == DragMode.Label && control.Anchor is not null)
                {
                    control.LabelOffset ??= new Point2D();
                    control.LabelOffset.X = _mouseViewBox.X - control.Anchor.X;
                    control.LabelOffset.Y = _mouseViewBox.Y - control.Anchor.Y;
                    _hasUnsavedChanges = true;
                }
                else if (_dragMode == DragMode.ShelfEnd && control.Anchor is not null && control.LeadLine is not null)
                {
                    // Calculate new shelf length from mouse position relative to anchor
                    var anchorScreen = ViewBoxToScreen(control.Anchor.X, control.Anchor.Y);
                    float dx = _mousePos.X - anchorScreen.X;

                    // Shelf is horizontal, so we only care about X distance
                    // When mirrored, screen direction is inverted from viewbox direction
                    bool goesRight = control.LeadLine.ShelfSide == "right";
                    bool screenGoesRight = _deviceMap.Mirror ? !goesRight : goesRight;
                    float newLength = (screenGoesRight ? dx : -dx) / _svgScale;
                    control.LeadLine.ShelfLength = Math.Max(10, newLength);
                    _hasUnsavedChanges = true;
                }
                else if (_dragMode == DragMode.Segment && control.Anchor is not null &&
                         control.LeadLine?.Segments is not null && _dragSegmentIndex >= 0 &&
                         _dragSegmentIndex < control.LeadLine.Segments.Count)
                {
                    // Calculate the start point of this segment
                    var anchorScreen = ViewBoxToScreen(control.Anchor.X, control.Anchor.Y);
                    bool goesRight = control.LeadLine.ShelfSide == "right";
                    bool screenGoesRight = _deviceMap.Mirror ? !goesRight : goesRight;

                    // Start from shelf end
                    float shelfEndX = anchorScreen.X + (screenGoesRight ? 1 : -1) * control.LeadLine.ShelfLength * _svgScale;
                    var segmentStart = new SKPoint(shelfEndX, anchorScreen.Y);

                    // Walk through previous segments to find the start of the dragged segment
                    for (int i = 0; i < _dragSegmentIndex; i++)
                    {
                        var prevSeg = control.LeadLine.Segments[i];
                        float angleRad = prevSeg.Angle * MathF.PI / 180f;
                        float dx = MathF.Cos(angleRad) * prevSeg.Length * _svgScale;
                        float dy = -MathF.Sin(angleRad) * prevSeg.Length * _svgScale;
                        if (!screenGoesRight) dx = -dx;
                        segmentStart = new SKPoint(segmentStart.X + dx, segmentStart.Y + dy);
                    }

                    // Calculate new angle and length from segment start to mouse position
                    float deltaX = _mousePos.X - segmentStart.X;
                    float deltaY = _mousePos.Y - segmentStart.Y;

                    // Mirror X for left-side shelf (screen direction)
                    if (!screenGoesRight) deltaX = -deltaX;

                    // Calculate length (in screen units, then convert to viewbox)
                    float screenLength = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
                    float newLength = screenLength / _svgScale;

                    // Calculate angle (remember Y is inverted in screen coords)
                    // atan2 gives angle from positive X axis, counterclockwise
                    float newAngle = MathF.Atan2(-deltaY, deltaX) * 180f / MathF.PI;

                    var segment = control.LeadLine.Segments[_dragSegmentIndex];
                    segment.Length = Math.Max(10, newLength);
                    segment.Angle = MathF.Round(newAngle / 5) * 5; // Snap to 5-degree increments
                    _hasUnsavedChanges = true;
                }
            }
            return;
        }

        // Update hover states
        UpdateHoverStates(mx, my);
        _canvas.Invalidate();
    }

    private void UpdateHoverStates(float x, float y)
    {
        var pt = new SKPoint(x, y);

        // Toolbar buttons
        _saveButtonHovered = _saveButtonBounds.Contains(pt);
        _newButtonHovered = _newButtonBounds.Contains(pt);
        _loadButtonHovered = _loadButtonBounds.Contains(pt);
        _mirrorCheckboxHovered = _mirrorCheckboxBounds.Contains(pt);

        // Controls list
        _addControlHovered = _addControlButtonBounds.Contains(pt);
        _deleteControlHovered = _deleteControlButtonBounds.Contains(pt);

        _hoveredControlListItem = -1;
        for (int i = 0; i < _controlListItemBounds.Count; i++)
        {
            if (_controlListItemBounds[i].Contains(pt))
            {
                _hoveredControlListItem = i;
                break;
            }
        }

        // SVG dropdown items
        _hoveredSvgDropdownItem = -1;
        if (_svgDropdownOpen)
        {
            for (int i = 0; i < _svgDropdownItemBounds.Count; i++)
            {
                if (_svgDropdownItemBounds[i].Contains(pt))
                {
                    _hoveredSvgDropdownItem = i;
                    break;
                }
            }
        }
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        float s = FUIRenderer.CanvasScaleFactor;
        float mx = e.X / s, my = e.Y / s;
        var pt = new SKPoint(mx, my);

        // Title bar dragging (use raw coords for drag start, but canvas-space for hit-test)
        if (my < TitleBarHeight && mx < Width / s - 50)
        {
            _isDragging = true;
            _dragStart = e.Location;
            return;
        }

        // Close button
        if (my < TitleBarHeight && mx >= Width / s - 50)
        {
            Close();
            return;
        }

        // Right-click on segment handles to remove them
        if (e.Button == MouseButtons.Right && _svgPanelBounds.Contains(pt) &&
            _selectedControlKey is not null &&
            _deviceMap.Controls.TryGetValue(_selectedControlKey, out var control) &&
            control.LeadLine?.Segments is not null)
        {
            for (int i = 0; i < _segmentEndHandles.Count; i++)
            {
                float segDist = SKPoint.Distance(_segmentEndHandles[i], _mousePos);
                if (segDist < HandleHitRadius)
                {
                    // Remove this segment
                    control.LeadLine.Segments.RemoveAt(i);
                    if (control.LeadLine.Segments.Count == 0)
                        control.LeadLine.Segments = null;
                    _hasUnsavedChanges = true;
                    return;
                }
            }
        }

        // SVG dropdown
        if (_svgDropdownBounds.Contains(pt))
        {
            _svgDropdownOpen = !_svgDropdownOpen;
            return;
        }

        // SVG dropdown item selection
        if (_svgDropdownOpen)
        {
            for (int i = 0; i < _svgDropdownItemBounds.Count; i++)
            {
                if (_svgDropdownItemBounds[i].Contains(pt))
                {
                    _deviceMap.SvgFile = _availableSvgFiles[i];
                    LoadSvgFile(_availableSvgFiles[i]);
                    _hasUnsavedChanges = true;
                    _svgDropdownOpen = false;
                    return;
                }
            }
            _svgDropdownOpen = false;
            return;
        }

        // Toolbar buttons
        if (_saveButtonBounds.Contains(pt))
        {
            SaveJsonFile();
            return;
        }

        if (_newButtonBounds.Contains(pt))
        {
            _deviceMap = new DeviceMap { SvgFile = _deviceMap.SvgFile };
            _selectedControlKey = null;
            _hasUnsavedChanges = false;
            _jsonFileName = "new_device.json";
            return;
        }

        if (_loadButtonBounds.Contains(pt))
        {
            // Try to open from source directory, not bin output
            var sourceDir = FindSourceMapsDirectory();
            using var ofd = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                InitialDirectory = sourceDir ?? Path.Combine(_imagesDir, "Maps")
            };
            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                LoadJsonFile(ofd.FileName);
            }
            return;
        }

        if (_mirrorCheckboxBounds.Contains(pt))
        {
            _deviceMap.Mirror = !_deviceMap.Mirror;
            _hasUnsavedChanges = true;
            return;
        }

        // Controls list
        if (_addControlButtonBounds.Contains(pt))
        {
            AddNewControl();
            return;
        }

        if (_deleteControlButtonBounds.Contains(pt) && _selectedControlKey is not null)
        {
            _deviceMap.Controls.Remove(_selectedControlKey);
            _selectedControlKey = null;
            _hasUnsavedChanges = true;
            return;
        }

        for (int i = 0; i < _controlListItemBounds.Count; i++)
        {
            if (_controlListItemBounds[i].Contains(pt))
            {
                _selectedControlKey = _deviceMap.Controls.Keys.ElementAt(i);
                return;
            }
        }

        // Lead line editing buttons (only when a control is selected)
        if (_selectedControlKey is not null && _deviceMap.Controls.TryGetValue(_selectedControlKey, out var selectedControl))
        {
            if (HandleLeadLineButtonClick(pt, selectedControl))
                return;
        }

        // Click on SVG panel - set anchor or start drag
        if (_svgPanelBounds.Contains(pt))
        {
            HandleSvgPanelClick(e);
        }
    }

    private void HandleSvgPanelClick(MouseEventArgs e)
    {
        var viewBoxPos = _mouseViewBox;
        bool shiftHeld = (Control.ModifierKeys & Keys.Shift) != 0;

        // Shift+Click: Reposition anchor of selected control to click position
        if (shiftHeld && _selectedControlKey is not null)
        {
            if (_deviceMap.Controls.TryGetValue(_selectedControlKey, out var control))
            {
                control.Anchor ??= new Point2D();
                control.Anchor.X = viewBoxPos.X;
                control.Anchor.Y = viewBoxPos.Y;
                _hasUnsavedChanges = true;
            }
            return;
        }

        // First, check if clicking on lead line segment handles (only for selected control)
        if (_selectedControlKey is not null &&
            _deviceMap.Controls.TryGetValue(_selectedControlKey, out var selectedControl) &&
            selectedControl.LeadLine is not null)
        {
            // Check shelf end handle
            if (_shelfEndHandle != default)
            {
                float shelfDist = SKPoint.Distance(_shelfEndHandle, _mousePos);
                if (shelfDist < HandleHitRadius)
                {
                    _dragMode = DragMode.ShelfEnd;
                    _dragSegmentIndex = -1;
                    return;
                }
            }

            // Check segment end handles
            for (int i = 0; i < _segmentEndHandles.Count; i++)
            {
                float segDist = SKPoint.Distance(_segmentEndHandles[i], _mousePos);
                if (segDist < HandleHitRadius)
                {
                    _dragMode = DragMode.Segment;
                    _dragSegmentIndex = i;
                    return;
                }
            }
        }

        // Check if clicking on existing control anchor or label
        foreach (var kvp in _deviceMap.Controls)
        {
            var control = kvp.Value;
            if (control.Anchor is null) continue;

            var anchorScreen = ViewBoxToScreen(control.Anchor.X, control.Anchor.Y);
            float anchorDist = SKPoint.Distance(anchorScreen, _mousePos);

            if (anchorDist < 15)
            {
                _selectedControlKey = kvp.Key;
                _dragMode = DragMode.Anchor;
                return;
            }

            // Check label area
            float labelX = control.Anchor.X + (control.LabelOffset?.X ?? 50);
            float labelY = control.Anchor.Y + (control.LabelOffset?.Y ?? 0);
            var labelScreen = ViewBoxToScreen(labelX, labelY);
            float labelDist = SKPoint.Distance(labelScreen, _mousePos);

            if (labelDist < 30)
            {
                _selectedControlKey = kvp.Key;
                _dragMode = DragMode.Label;
                return;
            }
        }

        // If we have a selected control without anchor, set anchor
        if (_selectedControlKey is not null)
        {
            if (_deviceMap.Controls.TryGetValue(_selectedControlKey, out var control))
            {
                if (control.Anchor is null)
                {
                    control.Anchor = new Point2D();
                    control.Anchor.X = viewBoxPos.X;
                    control.Anchor.Y = viewBoxPos.Y;
                    _hasUnsavedChanges = true;
                }
            }
        }
        else
        {
            // Create new control at click position
            AddNewControl(viewBoxPos);
        }
    }

    private bool HandleLeadLineButtonClick(SKPoint pt, ControlDefinition control)
    {
        // Shelf side toggle
        if (_shelfSideButtonBounds.Contains(pt))
        {
            control.LeadLine ??= new LeadLineDefinition();
            control.LeadLine.ShelfSide = control.LeadLine.ShelfSide == "left" ? "right" : "left";
            _hasUnsavedChanges = true;
            return true;
        }

        // Shelf length -/+
        if (_shelfLengthMinusBounds.Contains(pt))
        {
            control.LeadLine ??= new LeadLineDefinition();
            control.LeadLine.ShelfLength = Math.Max(10, control.LeadLine.ShelfLength - 10);
            _hasUnsavedChanges = true;
            return true;
        }

        if (_shelfLengthPlusBounds.Contains(pt))
        {
            control.LeadLine ??= new LeadLineDefinition();
            control.LeadLine.ShelfLength += 10;
            _hasUnsavedChanges = true;
            return true;
        }

        // Add segment
        if (_addSegmentButtonBounds.Contains(pt))
        {
            control.LeadLine ??= new LeadLineDefinition();
            control.LeadLine.Segments ??= new List<LeadLineSegment>();
            control.LeadLine.Segments.Add(new LeadLineSegment { Angle = -45, Length = 80 });
            _hasUnsavedChanges = true;
            return true;
        }

        // Remove segment
        if (_removeSegmentButtonBounds.Contains(pt) && control.LeadLine?.Segments?.Count > 0)
        {
            control.LeadLine.Segments.RemoveAt(control.LeadLine.Segments.Count - 1);
            if (control.LeadLine.Segments.Count == 0)
                control.LeadLine.Segments = null;
            _hasUnsavedChanges = true;
            return true;
        }

        // Segment angle/length buttons
        for (int i = 0; i < _segmentButtonBounds.Count; i++)
        {
            var (angMinus, angPlus, lenMinus, lenPlus) = _segmentButtonBounds[i];
            var seg = control.LeadLine?.Segments?[i];
            if (seg is null) continue;

            if (angMinus.Contains(pt))
            {
                seg.Angle -= 15;
                _hasUnsavedChanges = true;
                return true;
            }
            if (angPlus.Contains(pt))
            {
                seg.Angle += 15;
                _hasUnsavedChanges = true;
                return true;
            }
            if (lenMinus.Contains(pt))
            {
                seg.Length = Math.Max(10, seg.Length - 10);
                _hasUnsavedChanges = true;
                return true;
            }
            if (lenPlus.Contains(pt))
            {
                seg.Length += 10;
                _hasUnsavedChanges = true;
                return true;
            }
        }

        return false;
    }

    private void AddNewControl(SKPoint? position = null)
    {
        int index = _deviceMap.Controls.Count + 1;
        string key = $"control_{index}";
        while (_deviceMap.Controls.ContainsKey(key))
        {
            index++;
            key = $"control_{index}";
        }

        var control = new ControlDefinition
        {
            Id = key,
            Type = "Button",
            Label = $"Control {index}",
            Bindings = new List<string> { $"button{index}" }
        };

        if (position.HasValue)
        {
            control.Anchor = new Point2D { X = position.Value.X, Y = position.Value.Y };
            control.LabelOffset = new Point2D { X = 50, Y = 0 };
        }

        _deviceMap.Controls[key] = control;
        _selectedControlKey = key;
        _hasUnsavedChanges = true;
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        _isDragging = false;
        _dragMode = DragMode.None;
        _dragSegmentIndex = -1;
    }

    private void OnMouseWheel(object? sender, MouseEventArgs e)
    {
        var pt = new SKPoint(e.X, e.Y);

        // Scroll controls list when mouse is over it
        if (_controlsListBounds.Contains(pt))
        {
            float scrollAmount = -e.Delta / 4f; // Adjust scroll speed
            _controlsListScroll += scrollAmount;
            _canvas.Invalidate();
        }
    }

    #endregion
}
#endif
