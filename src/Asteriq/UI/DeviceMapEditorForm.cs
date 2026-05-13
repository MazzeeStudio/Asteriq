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
public partial class DeviceMapEditorForm : Form
{
    // Canvas and rendering
    // CA2213: SKControl is a WinForms child control â€” disposed automatically via Controls collection
#pragma warning disable CA2213
    private SKControl _canvas = null!;
#pragma warning restore CA2213
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

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            if (_hasUnsavedChanges)
            {
                int result = FUIMessageBox.Show(this, "You have unsaved changes. Discard and close?", "Unsaved Changes",
                    FUIMessageBox.MessageBoxType.Question, "Discard", "Cancel");
                if (result != 0)
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


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderTimer?.Dispose();
            _currentSvg?.Dispose();
        }
        base.Dispose(disposing);
    }
}
#endif