using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

/// <summary>
/// SC Bindings tab controller - Star Citizen binding export/import.
/// </summary>
public partial class SCBindingsTabController : ITabController
{
    private readonly TabContext _ctx;

    // SC-specific services
    private readonly ISCInstallationService _scInstallationService;
    private readonly SCProfileCacheService _scProfileCacheService;
    private readonly SCSchemaService _scSchemaService;
    private readonly SCXmlExportService _scExportService;
    private readonly SCExportProfileService _scExportProfileService;

    // SC state
    private List<SCInstallation> _scInstallations = new();
    private int _selectedSCInstallation = 0;
    private SCExportProfile _scExportProfile = new();
    private List<SCAction>? _scActions;
    private string? _scExportStatus;
    private DateTime _scExportStatusTime;
    private SCStatusKind _scStatusKind = SCStatusKind.Info;

    private enum SCStatusKind { Info, Success, Warning, Error }

    // Async schema loading state
    private bool _scLoading = false;
    private string _scLoadingMessage = "";
    private int _schemaLoadVersion = 0;

    // Dirty tracking: true when the profile name has been edited but not yet saved
    private bool _scProfileDirty = false;

    // In-memory schema cache: avoids re-parsing XML when switching back to an already-loaded environment
    private static readonly Dictionary<string, List<SCAction>> s_schemaCache = new();

    // SC UI bounds
    private SKRect _scInstallationSelectorBounds;
    private bool _scInstallationDropdownOpen;
    private SKRect _scInstallationDropdownBounds;
    private int _hoveredSCInstallation = -1;
    private SKRect _scExportButtonBounds;
    private bool _scExportButtonHovered;
    private bool _scExportFilenameBoxFocused;
    private string _scExportFilename = "";
    private List<SCMappingFile> _scAvailableProfiles = new();
    private bool _scImportDropdownOpen;
    private SKRect _scImportDropdownBounds;
    private int _scHoveredImportProfile = -1;
    private SKRect _scClearAllButtonBounds;
    private bool _scClearAllButtonHovered;
    private SKRect _scResetDefaultsButtonBounds;
    private bool _scResetDefaultsButtonHovered;
    // SC table state
    private List<SCAction>? _scFilteredActions;
    private string _scActionMapFilter = "";
    private int _scSelectedActionIndex = -1;
    private int _scHoveredActionIndex = -1;
    private float _scBindingsScrollOffset = 0;
    private float _scBindingsContentHeight = 0;
    private SKRect _scBindingsListBounds;
    private List<SKRect> _scActionRowBounds = new();
    private SKRect _scActionMapFilterBounds;
    private bool _scActionMapFilterDropdownOpen;
    private SKRect _scActionMapFilterDropdownBounds;
    private int _scHoveredActionMapFilter = -1;
    private float _scActionMapFilterScrollOffset = 0;
    private float _scActionMapFilterMaxScroll = 0;
    private List<string> _scActionMaps = new();

    // SC grid column state
    private float _scGridActionColWidth = 300f;
    private float _scGridDeviceColMinWidth = 160f;
    private Dictionary<string, float> _scGridDeviceColWidths = new();
    private float _scGridHorizontalScroll = 0f;
    private float _scGridTotalWidth = 0f;
    private List<SCGridColumn>? _scGridColumns;
    private float _scDeviceColsStart = 0f;
    private float _scVisibleDeviceWidth = 0f;

    // SC cell interaction state
    private (int actionIndex, int colIndex) _scSelectedCell = (-1, -1);
    private (int actionIndex, int colIndex) _scHoveredCell = (-1, -1);
    private bool _scIsListeningForInput = false;
    private DateTime _scListeningStartTime;
    private DateTime _scLastCellClickTime;
    private const int SCListeningTimeoutMs = 5000;
    private SCGridColumn? _scListeningColumn;
    private HashSet<string> _scConflictingBindings = new();
    private int _scHighlightedColumn = -1;

    // Header toggle button (JS REF / DEVICE)
    private SKRect _scHeaderToggleButtonBounds;
    private bool _scHeaderToggleButtonHovered;

    // Column actions panel — Import From Profile
    private int _scColImportProfileIndex = -1;
    private bool _scColImportProfileDropdownOpen;
    private SKRect _scColImportProfileSelectorBounds;
    private SKRect _scColImportProfileDropdownBounds;
    private int _scColImportProfileHoveredIndex = -1;
    private SCExportProfile? _scColImportLoadedProfile;
    private List<(string Label, uint VJoyDeviceId)> _scColImportSourceColumns = new();
    private int _scColImportColumnIndex = -1;
    private bool _scColImportColumnDropdownOpen;
    private SKRect _scColImportColumnSelectorBounds;
    private SKRect _scColImportColumnDropdownBounds;
    private int _scColImportColumnHoveredIndex = -1;
    private SKRect _scColImportButtonBounds;
    private bool _scColImportButtonHovered;
    private SKRect _scDeselectButtonBounds;
    private bool _scDeselectButtonHovered;

    // SC scrollbar state
    private bool _scIsDraggingVScroll = false;
    private bool _scIsDraggingHScroll = false;
    private float _scScrollDragStartY = 0;
    private float _scScrollDragStartX = 0;
    private float _scScrollDragStartOffset = 0;
    private SKRect _scVScrollbarBounds;
    private SKRect _scHScrollbarBounds;
    private SKRect _scVScrollThumbBounds;
    private SKRect _scHScrollThumbBounds;
    private SKRect _scColumnHeadersBounds;

    // SC search/filter state
    private string _scSearchText = "";
    private bool _scShowBoundOnly = false;
    private SKRect _scSearchBoxBounds;
    private bool _scSearchBoxFocused = false;
    private SKRect _scShowBoundOnlyBounds;
    private bool _scShowBoundOnlyHovered = false;

    // SC category collapse state
    private HashSet<string> _scCollapsedCategories = new();
    private Dictionary<string, SKRect> _scCategoryHeaderBounds = new();

    // SC binding assignment state (right-panel ASSIGN/CLEAR buttons)
    private SKRect _scAssignInputButtonBounds;
    private bool _scAssignInputButtonHovered;
    private SKRect _scClearBindingButtonBounds;
    private bool _scClearBindingButtonHovered;

    // SC export profile management
    private List<SCExportProfileInfo> _scExportProfiles = new();
    private SKRect _scProfileDropdownBounds;
    private bool _scProfileDropdownOpen;
    private SKRect _scProfileDropdownListBounds;
    private int _scHoveredProfileIndex = -1;
    private SKRect _scNewProfileButtonBounds;
    private bool _scNewProfileButtonHovered;
    private SKRect _scSaveProfileButtonBounds;
    private bool _scSaveProfileButtonHovered;
    private SKRect _scDeleteProfileButtonBounds = default;
    private SKRect _scProfileEditBounds;
    private bool _scProfileEditHovered;
    // Inline delete button that appears on hover in the profile dropdown list
    private SKRect _scDropdownDeleteButtonBounds;
    private string _scDropdownDeleteProfileName = "";

    // Public properties for MainForm mouse dispatch
    public bool IsDraggingVScroll => _scIsDraggingVScroll;
    public bool IsDraggingHScroll => _scIsDraggingHScroll;
    public bool IsSearchBoxFocused => _scSearchBoxFocused;
    public bool IsExportFilenameBoxFocused => _scExportFilenameBoxFocused;

    public SCBindingsTabController(
        TabContext ctx,
        ISCInstallationService scInstallationService,
        SCProfileCacheService scProfileCacheService,
        SCSchemaService scSchemaService,
        SCXmlExportService scExportService,
        SCExportProfileService scExportProfileService)
    {
        _ctx = ctx;
        _scInstallationService = scInstallationService;
        _scProfileCacheService = scProfileCacheService;
        _scSchemaService = scSchemaService;
        _scExportService = scExportService;
        _scExportProfileService = scExportProfileService;
    }

    /// <summary>
    /// Represents a column in the SC bindings grid
    /// </summary>
    private class SCGridColumn
    {
        public string Id { get; set; } = "";
        public string Header { get; set; } = "";
        public string DevicePrefix { get; set; } = "";
        public uint VJoyDeviceId { get; set; }
        public int SCInstance { get; set; }
        public bool IsKeyboard { get; set; }
        public bool IsMouse { get; set; }
        public bool IsJoystick { get; set; }
        /// <summary>
        /// True when this JS column exists in the profile but has no backing vJoy device.
        /// The column renders stored bindings read-only; no new assignments can be made.
        /// </summary>
        public bool IsReadOnly { get; set; }
        /// <summary>
        /// Physical device backing this column (null for vJoy/KB/Mouse columns).
        /// </summary>
        public PhysicalDeviceInfo? PhysicalDevice { get; set; }
        /// <summary>
        /// True when this column represents a connected physical device (not vJoy).
        /// </summary>
        public bool IsPhysical => PhysicalDevice is not null;
    }

    /// <summary>
    /// Returns the environment string of the currently selected SC installation, or null if none.
    /// </summary>
    private string? CurrentEnvironment =>
        _scInstallations.Count > 0 && _selectedSCInstallation < _scInstallations.Count
            ? _scInstallations[_selectedSCInstallation].Environment
            : null;

    // Windows API for detecting held keys
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    // Virtual key codes for left/right modifiers
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;  // Left Alt
    private const int VK_RMENU = 0xA5;  // Right Alt

    private static bool IsKeyHeld(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static bool IsKeyPressed(int vk)
    {
        // GetAsyncKeyState returns high bit set if key is down
        // and low bit set if key was pressed since last call
        short state = GetAsyncKeyState(vk);
        return (state & 0x0001) != 0; // Check "was pressed" bit
    }

    private static void ClearStaleKeyPresses()
    {
        // Clear A-Z
        for (int vk = 0x41; vk <= 0x5A; vk++)
            GetAsyncKeyState(vk);

        // Clear 0-9
        for (int vk = 0x30; vk <= 0x39; vk++)
            GetAsyncKeyState(vk);

        // Clear F1-F12
        for (int vk = 0x70; vk <= 0x7B; vk++)
            GetAsyncKeyState(vk);

        // Clear common keys
        int[] commonKeys = { 0x20, 0x0D, 0x08, 0x09, 0x2E, 0x2D, 0x24, 0x23, 0x21, 0x22,
                            0x25, 0x26, 0x27, 0x28,
                            0xC0, 0xBD, 0xBB, 0xDB, 0xDD, 0xDC, 0xBA, 0xDE, 0xBC, 0xBE, 0xBF };
        foreach (var vk in commonKeys)
            GetAsyncKeyState(vk);
    }

    private static void ClearStaleMousePresses()
    {
        GetAsyncKeyState(0x01); // VK_LBUTTON
        GetAsyncKeyState(0x02); // VK_RBUTTON
        GetAsyncKeyState(0x04); // VK_MBUTTON
        GetAsyncKeyState(0x05); // VK_XBUTTON1
        GetAsyncKeyState(0x06); // VK_XBUTTON2
    }

    public void Draw(SKCanvas canvas, SKRect bounds, float padLeft, float contentTop, float contentBottom)
    {
        DrawBindingsTabContent(canvas, bounds, padLeft, contentTop, contentBottom);
    }

    public void OnMouseDown(MouseEventArgs e)
    {
        // When listening for mouse input, let the detection tick handle the press
        if (_scIsListeningForInput && _scListeningColumn is { IsMouse: true })
            return;

        if (e.Button == MouseButtons.Right)
        {
            HandleBindingsTabRightClick(new SKPoint(e.X, e.Y));
            return;
        }
        if (e.Button == MouseButtons.Left)
        {
            HandleBindingsTabClick(new SKPoint(e.X, e.Y));
        }
    }

    public void OnMouseMove(MouseEventArgs e)
    {
        // Handle scrollbar dragging
        if (_scIsDraggingVScroll)
        {
            float deltaY = e.Y - _scScrollDragStartY;
            float maxScroll = Math.Max(0, _scBindingsContentHeight - _scBindingsListBounds.Height);
            float trackHeight = _scVScrollbarBounds.Height - _scVScrollThumbBounds.Height;
            if (trackHeight > 0 && maxScroll > 0)
            {
                float scrollDelta = (deltaY / trackHeight) * maxScroll;
                _scBindingsScrollOffset = Math.Clamp(_scScrollDragStartOffset + scrollDelta, 0, maxScroll);
            }
            _ctx.MarkDirty();
            return;
        }

        if (_scIsDraggingHScroll)
        {
            float deltaX = e.X - _scScrollDragStartX;
            float maxHScroll = Math.Max(0, _scGridTotalWidth - _scVisibleDeviceWidth);
            float trackWidth = _scHScrollbarBounds.Width - _scHScrollThumbBounds.Width;
            if (trackWidth > 0 && maxHScroll > 0)
            {
                float scrollDelta = (deltaX / trackWidth) * maxHScroll;
                _scGridHorizontalScroll = Math.Clamp(_scScrollDragStartOffset + scrollDelta, 0, maxHScroll);
            }
            _ctx.MarkDirty();
            return;
        }

        // Installation dropdown hover
        if (_scInstallationDropdownOpen && _scInstallationDropdownBounds.Contains(e.X, e.Y))
        {
            float itemHeight = 28f;
            int itemIndex = (int)((e.Y - _scInstallationDropdownBounds.Top - 2) / itemHeight);
            _hoveredSCInstallation = itemIndex >= 0 && itemIndex < _scInstallations.Count ? itemIndex : -1;
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }
        else
        {
            _hoveredSCInstallation = -1;
        }

        // Reset hover states
        _scHoveredActionIndex = -1;
        _scHoveredActionMapFilter = -1;
        _scHoveredCell = (-1, -1);

        // Action map filter dropdown hover
        if (_scActionMapFilterDropdownOpen && _scActionMapFilterDropdownBounds.Contains(e.X, e.Y))
        {
            float itemHeight = 24f;
            float relativeY = e.Y - _scActionMapFilterDropdownBounds.Top - 2 + _scActionMapFilterScrollOffset;
            int itemIndex = (int)(relativeY / itemHeight) - 1;
            _scHoveredActionMapFilter = itemIndex >= -1 && itemIndex < _scActionMaps.Count ? itemIndex : -1;
            _ctx.OwnerForm.Cursor = Cursors.Hand;
        }

        // Action row and cell hover
        if (_scBindingsListBounds.Contains(e.X, e.Y) && _scFilteredActions is not null)
        {
            float rowHeight = 28f;
            float rowGap = 2f;
            float categoryHeaderHeight = 28f;
            float relativeY = e.Y - _scBindingsListBounds.Top + _scBindingsScrollOffset;

            string? lastCategoryName = null;
            float currentY = 0;

            for (int i = 0; i < _scFilteredActions.Count; i++)
            {
                var action = _scFilteredActions[i];
                // Must use the same category mapping as the draw code so header heights stay in sync
                string categoryName = SCCategoryMapper.GetCategoryNameForAction(action.ActionMap, action.ActionName);

                if (categoryName != lastCategoryName)
                {
                    lastCategoryName = categoryName;
                    currentY += categoryHeaderHeight;

                    if (_scCollapsedCategories.Contains(categoryName))
                    {
                        while (i < _scFilteredActions.Count - 1 &&
                               SCCategoryMapper.GetCategoryNameForAction(_scFilteredActions[i + 1].ActionMap, _scFilteredActions[i + 1].ActionName) == categoryName)
                        {
                            i++;
                        }
                        continue;
                    }
                }

                float rowTop = currentY;
                float rowBottom = currentY + rowHeight;

                if (relativeY >= rowTop && relativeY < rowBottom)
                {
                    _scHoveredActionIndex = i;

                    int hoveredCol = GetHoveredColumnIndex(e.X);
                    if (hoveredCol >= 0)
                    {
                        _scHoveredCell = (i, hoveredCol);
                    }

                    _ctx.OwnerForm.Cursor = Cursors.Hand;
                    break;
                }

                currentY += rowHeight + rowGap;
            }
        }

        // Text input fields - IBeam cursor
        if (_scSearchBoxBounds.Contains(e.X, e.Y))
        {
            _ctx.OwnerForm.Cursor = Cursors.IBeam;
        }

        // Buttons and selectors
        if (_scExportButtonBounds.Contains(e.X, e.Y) ||
            _scInstallationSelectorBounds.Contains(e.X, e.Y) ||
            _scProfileDropdownBounds.Contains(e.X, e.Y) ||
            (_scProfileEditBounds != SKRect.Empty && _scProfileEditBounds.Contains(e.X, e.Y)) ||
            _scNewProfileButtonBounds.Contains(e.X, e.Y) ||
            _scSaveProfileButtonBounds.Contains(e.X, e.Y) ||
            (_scDeleteProfileButtonBounds != default && _scDeleteProfileButtonBounds.Contains(e.X, e.Y)) ||
            _scActionMapFilterBounds.Contains(e.X, e.Y) ||
            _scAssignInputButtonBounds.Contains(e.X, e.Y) ||
            _scClearBindingButtonBounds.Contains(e.X, e.Y) ||
            _scClearAllButtonBounds.Contains(e.X, e.Y) ||
            _scResetDefaultsButtonBounds.Contains(e.X, e.Y) ||
            _scShowBoundOnlyBounds.Contains(e.X, e.Y) ||
            (!_scVScrollbarBounds.IsEmpty && _scVScrollbarBounds.Contains(e.X, e.Y)) ||
            (!_scHScrollbarBounds.IsEmpty && _scHScrollbarBounds.Contains(e.X, e.Y)) ||
            (!_scHeaderToggleButtonBounds.IsEmpty && _scHeaderToggleButtonBounds.Contains(e.X, e.Y)) ||
            (!_scColImportButtonBounds.IsEmpty && _scColImportButtonBounds.Contains(e.X, e.Y)) ||
            (!_scDeselectButtonBounds.IsEmpty && _scDeselectButtonBounds.Contains(e.X, e.Y)) ||
            (!_scColImportProfileSelectorBounds.IsEmpty && _scColImportProfileSelectorBounds.Contains(e.X, e.Y)) ||
            (!_scColImportColumnSelectorBounds.IsEmpty && _scColImportColumnSelectorBounds.Contains(e.X, e.Y)))
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
        }

        bool showColumnActions = _scHighlightedColumn >= 0
            && _scGridColumns is not null
            && _scHighlightedColumn < _scGridColumns.Count
            && _scGridColumns[_scHighlightedColumn].IsJoystick
            && !_scGridColumns[_scHighlightedColumn].IsPhysical
            && !_scGridColumns[_scHighlightedColumn].IsReadOnly;

        // Import profile dropdown hover
        if (showColumnActions && _scColImportProfileDropdownOpen
            && !_scColImportProfileDropdownBounds.IsEmpty
            && _scColImportProfileDropdownBounds.Contains(e.X, e.Y))
        {
            float itemH = 28f;
            int idx = (int)((e.Y - _scColImportProfileDropdownBounds.Top) / itemH);
            var importable = _scExportProfiles.Where(p => p.ProfileName != _scExportProfile.ProfileName).ToList();
            int newHovered = idx >= 0 && idx < importable.Count ? idx : -1;
            if (newHovered != _scColImportProfileHoveredIndex)
            {
                _scColImportProfileHoveredIndex = newHovered;
                _ctx.MarkDirty();
            }
            _ctx.OwnerForm.Cursor = Cursors.Hand;
        }
        else if (_scColImportProfileHoveredIndex >= 0)
        {
            _scColImportProfileHoveredIndex = -1;
            _ctx.MarkDirty();
        }

        // Import column dropdown hover
        if (showColumnActions && _scColImportColumnDropdownOpen
            && !_scColImportColumnDropdownBounds.IsEmpty
            && _scColImportColumnDropdownBounds.Contains(e.X, e.Y))
        {
            float itemH = 28f;
            int idx = (int)((e.Y - _scColImportColumnDropdownBounds.Top) / itemH);
            int newHovered = idx >= 0 && idx < _scColImportSourceColumns.Count ? idx : -1;
            if (newHovered != _scColImportColumnHoveredIndex)
            {
                _scColImportColumnHoveredIndex = newHovered;
                _ctx.MarkDirty();
            }
            _ctx.OwnerForm.Cursor = Cursors.Hand;
        }
        else if (_scColImportColumnHoveredIndex >= 0)
        {
            _scColImportColumnHoveredIndex = -1;
            _ctx.MarkDirty();
        }

        // Category collapse headers
        foreach (var headerBounds in _scCategoryHeaderBounds.Values)
        {
            if (headerBounds.Contains(e.X, e.Y)) { _ctx.OwnerForm.Cursor = Cursors.Hand; break; }
        }

        // Profile dropdown list items when open
        if (_scProfileDropdownOpen && _scProfileDropdownListBounds.Contains(e.X, e.Y))
            _ctx.OwnerForm.Cursor = Cursors.Hand;

        // Listening timeout
        if (_scIsListeningForInput && (DateTime.Now - _scListeningStartTime).TotalMilliseconds > SCListeningTimeoutMs)
        {
            _scIsListeningForInput = false;
            _scListeningColumn = null;
        }
    }

    public void OnMouseUp(MouseEventArgs e)
    {
        if (_scIsDraggingVScroll || _scIsDraggingHScroll)
        {
            _scIsDraggingVScroll = false;
            _scIsDraggingHScroll = false;
            _ctx.MarkDirty();
        }
    }

    public void OnMouseWheel(MouseEventArgs e)
    {
        // Action map filter dropdown scroll
        if (_scActionMapFilterDropdownOpen && _scActionMapFilterDropdownBounds.Contains(e.X, e.Y))
        {
            float scrollAmount = -e.Delta / 4f;
            _scActionMapFilterScrollOffset = Math.Clamp(_scActionMapFilterScrollOffset + scrollAmount, 0, _scActionMapFilterMaxScroll);
            _ctx.MarkDirty();
            return;
        }

        // SC bindings list scroll
        if (_scBindingsListBounds.Contains(e.X, e.Y))
        {
            float scrollAmount = -e.Delta / 4f;

            if (Control.ModifierKeys.HasFlag(Keys.Shift))
            {
                float maxHScroll = Math.Max(0, _scGridTotalWidth - _scVisibleDeviceWidth);
                if (maxHScroll > 0)
                {
                    _scGridHorizontalScroll = Math.Clamp(_scGridHorizontalScroll + scrollAmount, 0, maxHScroll);
                }
            }
            else
            {
                float maxScroll = Math.Max(0, _scBindingsContentHeight - _scBindingsListBounds.Height);
                _scBindingsScrollOffset = Math.Clamp(_scBindingsScrollOffset + scrollAmount, 0, maxScroll);
            }

            _ctx.MarkDirty();
        }
    }

    public bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_scSearchBoxFocused)
            return HandleSearchBoxKey(keyData);
        if (_scExportFilenameBoxFocused)
            return HandleExportFilenameBoxKey(keyData);
        return false;
    }

    public void OnMouseLeave()
    {
        _hoveredSCInstallation = -1;
        _scHoveredActionIndex = -1;
        _scHoveredCell = (-1, -1);
    }

    public void OnTick()
    {
        if (_scIsListeningForInput)
        {
            CheckSCBindingInput();
        }
    }

    public void OnActivated()
    {
        // Defer schema load to first tab activation so BeginInvoke runs with a valid form handle.
        if (_scActions is null && !_scLoading)
            StartSchemaLoad();
    }

    public void OnDeactivated() { }
}
