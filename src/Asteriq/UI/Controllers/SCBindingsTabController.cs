using Asteriq.DirectInput;
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
    private readonly DirectInputService? _directInputService;

    // ── Grouped state objects ──────────────────────────────────────────────────
    private readonly SCInstallationState _scInstall = new();
    private readonly GridLayoutState _grid = new();
    private readonly CellInteractionState _cell = new();
    private readonly InputListeningState _scListening = new();
    private readonly ColumnImportState _colImport = new();
    private readonly ConflictState _conflicts = new();
    private readonly ControlProfilesPanelState _cpPanel = new();
    private readonly CellDetailsState _cellDetails = new();
    private readonly ProfileMgmtState _profileMgmt = new();
    private readonly SearchFilterState _searchFilter = new();
    private readonly ScrollState _scroll = new();

    // ── Flat fields (not grouped) ──────────────────────────────────────────────
    private SCExportProfile _scExportProfile = new();
    private string? _scExportStatus;
    private DateTime _scExportStatusTime;
    private SCStatusKind _scStatusKind = SCStatusKind.Info;

    private enum SCStatusKind { Info, Success, Warning, Error }

    // In-memory schema cache: avoids re-parsing XML when switching back to an already-loaded environment
    private static readonly Dictionary<string, List<SCAction>> s_schemaCache = new();

    // SC UI bounds (export panel)
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
    private int _scSelectedActionIndex = -1;
    private int _scHoveredActionIndex = -1;
    private float _scBindingsScrollOffset = 0;
    private float _scBindingsContentHeight = 0;
    private SKRect _scBindingsListBounds;
    private List<SKRect> _scActionRowBounds = new();

    // SC category collapse state
    private HashSet<string> _scCollapsedCategories = new();
    private Dictionary<string, SKRect> _scCategoryHeaderBounds = new();

    // SC binding assignment state (right-panel ASSIGN/CLEAR buttons)
    private SKRect _scAssignInputButtonBounds;
    private bool _scAssignInputButtonHovered;
    private SKRect _scClearBindingButtonBounds;
    private bool _scClearBindingButtonHovered;

    private const int SCListeningTimeoutMs = 5000;

    // Modifier keys detected from Mappings profile (vkCode → SC modifier name, e.g. {0xA3: "rctrl"})
    private Dictionary<int, string> _scModifierKeys = new();

    // (InstanceGuid, 0-based buttonIndex) pairs that are mapped to keyboard modifier keys.
    // Keyed by device instance so button31 on the left stick ≠ button31 on the right stick.
    private HashSet<(Guid DeviceGuid, int ButtonIndex)> _scModifierPhysicalButtons = new();

    // Maps (InstanceGuid, 0-based buttonIndex) → list of SC modifier names (e.g. ["rctrl"])
    private Dictionary<(Guid DeviceGuid, int ButtonIndex), List<string>> _scModifierButtonToModifiers = new();

    // ── Button capture — 500 Hz event subscription ───────────────────────────
    // Delegate reference kept for unsubscription; null when capture is inactive.
    private EventHandler<DeviceInputState>? _captureEventHandler;
    // GUID → HID path for physical devices, built on UI thread at subscription time.
    // Read-only on background thread. Null when capture is inactive.
    private Dictionary<Guid, string>? _captureGuidToHidPath;
    // Warmup/detection dicts — written and read exclusively on the background event-handler thread.
    private Dictionary<Guid, bool[]>? _capturePrevButtons;
    private Dictionary<Guid, int[]>? _capturePrevHats;
    // Mouse wheel pending for capture — set on UI thread via OnMouseWheel, read on bg thread.
    private volatile string? _captureMouseWheelPending;
    // Pending modifier name detected mid-capture (e.g. "rctrl"), set on bg thread.
    private string? _capturePendingModifierBg;
    // End of the warmup window. Events arriving before this time update baseline+previous.
    private DateTime _captureWarmupUntil;
    // Latest detected input candidate (updated on each new press; committed after debounce expires).
    private string? _captureCandidateInput;
    private string? _captureCandidatePath;
    private DateTime _captureCandidateDebounceUntil;
    // Interlocked flag: 1 = handler processes events, 0 = inactive/cancelled.
    private volatile int _captureHandlerActive;

    // Public properties for MainForm mouse dispatch
    public bool IsDraggingVScroll => _scroll.IsDraggingVScroll;
    public bool IsDraggingHScroll => _scroll.IsDraggingHScroll;
    public bool IsDraggingSearchSelection => _searchFilter.SearchDragging;
    public bool IsSearchBoxFocused => _searchFilter.SearchBoxFocused;
    public bool IsExportFilenameBoxFocused => _scExportFilenameBoxFocused;
    public SCExportProfile ActiveSCExportProfile => _scExportProfile;

    public SCBindingsTabController(
        TabContext ctx,
        ISCInstallationService scInstallationService,
        SCProfileCacheService scProfileCacheService,
        SCSchemaService scSchemaService,
        SCXmlExportService scExportService,
        SCExportProfileService scExportProfileService,
        DirectInputService? directInputService = null)
    {
        _ctx = ctx;
        _scInstallationService = scInstallationService;
        _scProfileCacheService = scProfileCacheService;
        _scSchemaService = scSchemaService;
        _scExportService = scExportService;
        _scExportProfileService = scExportProfileService;
        _directInputService = directInputService;

        _ctx.SendProfileListToClient = () =>
        {
            if (_ctx.NetworkInput is null) return;
            var list = new List<(string Name, byte[] XmlBytes)>();
            foreach (var info in _scExportProfileService.ListProfiles())
            {
                var profile = _scExportProfileService.LoadProfile(info.ProfileName);
                if (profile is null) continue;
                list.Add((info.ProfileName, SCXmlExportService.ExportToBytes(profile)));
            }
            _ctx.NetworkInput.SendProfileList(list);
        };
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
        /// Stable key for physical device (VID:PID or HID path). Used to persist bindings
        /// across sessions. Empty for vJoy/KB/Mouse columns.
        /// </summary>
        public string PhysicalDeviceKey { get; set; } = "";
        /// <summary>
        /// True when this column represents a connected physical device (not vJoy).
        /// </summary>
        public bool IsPhysical => PhysicalDevice is not null;
    }

    /// <summary>
    /// Returns the environment string of the currently selected SC installation, or null if none.
    /// </summary>
    private string? CurrentEnvironment =>
        _scInstall.Installations.Count > 0 && _scInstall.SelectedInstallation < _scInstall.Installations.Count
            ? _scInstall.Installations[_scInstall.SelectedInstallation].Environment
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
        if (_scListening.IsListening && _cell.ListeningColumn is { IsMouse: true })
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
        // Handle search box text selection drag
        if (_searchFilter.SearchDragging && !string.IsNullOrEmpty(_searchFilter.SearchText))
        {
            float contentX = _searchFilter.SearchBoxBounds.Left + 24f;
            float clickOffset = e.X - contentX;
            int pos = HitTestSearchCursorPos(_searchFilter.SearchText, clickOffset, 13f);
            _searchFilter.SelectionEnd = pos;
            _searchFilter.CursorPos = pos;
            _ctx.MarkDirty();
            return;
        }

        // Handle scrollbar dragging
        if (_scroll.IsDraggingVScroll)
        {
            float deltaY = e.Y - _scroll.DragStartY;
            float maxScroll = Math.Max(0, _scBindingsContentHeight - _scBindingsListBounds.Height);
            float trackHeight = _scroll.VScrollBounds.Height - _scroll.VThumbBounds.Height;
            if (trackHeight > 0 && maxScroll > 0)
            {
                float scrollDelta = (deltaY / trackHeight) * maxScroll;
                _scBindingsScrollOffset = Math.Clamp(_scroll.DragStartOffset + scrollDelta, 0, maxScroll);
            }
            _ctx.MarkDirty();
            return;
        }

        if (_scroll.IsDraggingHScroll)
        {
            float deltaX = e.X - _scroll.DragStartX;
            float maxHScroll = Math.Max(0, _grid.TotalWidth - _grid.VisibleDeviceWidth);
            float trackWidth = _scroll.HScrollBounds.Width - _scroll.HThumbBounds.Width;
            if (trackWidth > 0 && maxHScroll > 0)
            {
                float scrollDelta = (deltaX / trackWidth) * maxHScroll;
                _grid.HorizontalScroll = Math.Clamp(_scroll.DragStartOffset + scrollDelta, 0, maxHScroll);
            }
            _ctx.MarkDirty();
            return;
        }

        _ctx.OwnerForm.Cursor = Cursors.Default;

        // Installation dropdown hover
        if (_scInstall.DropdownOpen && _scInstall.DropdownBounds.Contains(e.X, e.Y))
        {
            float itemHeight = 28f;
            int itemIndex = (int)((e.Y - _scInstall.DropdownBounds.Top - 2) / itemHeight);
            _scInstall.HoveredInstallation = itemIndex >= 0 && itemIndex < _scInstall.Installations.Count ? itemIndex : -1;
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }
        else
        {
            _scInstall.HoveredInstallation = -1;
        }

        // Reset hover states
        _scHoveredActionIndex = -1;
        _searchFilter.HoveredFilter = -1;
        _cell.HoveredCell = (-1, -1);

        // Action map filter dropdown hover
        if (_searchFilter.FilterDropdownOpen && _searchFilter.FilterDropdownBounds.Contains(e.X, e.Y))
        {
            float itemHeight = 24f;
            float relativeY = e.Y - _searchFilter.FilterDropdownBounds.Top - 2 + _searchFilter.FilterScrollOffset;
            int itemIndex = (int)(relativeY / itemHeight) - 1;
            _searchFilter.HoveredFilter = itemIndex >= -1 && itemIndex < _searchFilter.ActionMaps.Count ? itemIndex : -1;
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
                        _cell.HoveredCell = (i, hoveredCol);
                    }

                    _ctx.OwnerForm.Cursor = Cursors.Hand;
                    break;
                }

                currentY += rowHeight + rowGap;
            }
        }

        // Text input fields - IBeam cursor, Hand over the × clear button
        if (_searchFilter.SearchBoxBounds.Contains(e.X, e.Y))
        {
            bool overClear = !string.IsNullOrEmpty(_searchFilter.SearchText)
                && e.X > _searchFilter.SearchBoxBounds.Right - 24;
            _ctx.OwnerForm.Cursor = overClear ? Cursors.Hand : Cursors.IBeam;
        }

        // Panel header hover (collapsed panel expand buttons)
        bool overCPHeader = !_cpPanel.IsExpanded
            && !_cpPanel.HeaderBounds.IsEmpty
            && _cpPanel.HeaderBounds.Contains(e.X, e.Y);
        bool overColActionsHeader = _cpPanel.IsExpanded
            && !_colImport.HeaderBounds.IsEmpty
            && _colImport.HeaderBounds.Contains(e.X, e.Y);
        bool overCellDetailsHeader = _cpPanel.IsExpanded
            && !_cellDetails.HeaderBounds.IsEmpty
            && _cellDetails.HeaderBounds.Contains(e.X, e.Y);
        if (overCPHeader || overColActionsHeader || overCellDetailsHeader)
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            _ctx.MarkDirty();
        }

        // Activation mode segment hover (Cell Details)
        {
            int prevHovered = _cellDetails.HoveredModeIndex;
            _cellDetails.HoveredModeIndex = -1;
            for (int i = 0; i < _cellDetails.ActivationModeBounds.Length; i++)
            {
                if (_cellDetails.ActivationModeBounds[i].HitTest(e.X, e.Y))
                {
                    _cellDetails.HoveredModeIndex = i;
                    _ctx.OwnerForm.Cursor = Cursors.Hand;
                    break;
                }
            }
            if (_cellDetails.HoveredModeIndex != prevHovered)
                _ctx.MarkDirty();
        }

        // Conflict link hover tracking
        {
            int prevHovered = _conflicts.ConflictLinkHovered;
            _conflicts.ConflictLinkHovered = -1;
            for (int ci = 0; ci < _conflicts.ConflictLinkBounds.Count; ci++)
            {
                if (_conflicts.ConflictLinkBounds[ci].HitTest(e.X, e.Y))
                {
                    _conflicts.ConflictLinkHovered = ci;
                    _ctx.OwnerForm.Cursor = Cursors.Hand;
                    break;
                }
            }
            if (_conflicts.ConflictLinkHovered != prevHovered)
                _ctx.MarkDirty();
        }

        // Buttons and selectors
        if (_scExportButtonBounds.Contains(e.X, e.Y) ||
            _scInstall.SelectorBounds.Contains(e.X, e.Y) ||
            _profileMgmt.DropdownBounds.Contains(e.X, e.Y) ||
            (_profileMgmt.ProfileEditBounds != SKRect.Empty && _profileMgmt.ProfileEditBounds.Contains(e.X, e.Y)) ||
            _profileMgmt.NewProfileBounds.Contains(e.X, e.Y) ||
            _searchFilter.FilterBounds.Contains(e.X, e.Y) ||
            _scAssignInputButtonBounds.Contains(e.X, e.Y) ||
            _scClearBindingButtonBounds.Contains(e.X, e.Y) ||
            _scClearAllButtonBounds.Contains(e.X, e.Y) ||
            _scResetDefaultsButtonBounds.Contains(e.X, e.Y) ||
            _searchFilter.ShowBoundOnlyBounds.Contains(e.X, e.Y) ||
            (_scroll.VScrollBounds.HitTest(e.X, e.Y)) ||
            (_scroll.HScrollBounds.HitTest(e.X, e.Y)) ||
            (_searchFilter.ShowJSRefBounds.HitTest(e.X, e.Y)) ||
            (_colImport.ImportButtonBounds.HitTest(e.X, e.Y)) ||
            (_colImport.ClearColumnBounds.HitTest(e.X, e.Y)) ||
            (_colImport.ProfileSelectorBounds.HitTest(e.X, e.Y)) ||
            (_colImport.ColumnSelectorBounds.HitTest(e.X, e.Y)) ||
            (_scInstall.BrowseBounds.HitTest(e.X, e.Y)))
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
        }

        _scInstall.BrowseHovered = _scInstall.BrowseBounds.HitTest(e.X, e.Y);

        bool showColumnActions = IsColumnActionsVisible();

        // Import profile dropdown hover
        if (showColumnActions && _colImport.ProfileDropdownOpen
            && !_colImport.ProfileDropdownBounds.IsEmpty
            && _colImport.ProfileDropdownBounds.Contains(e.X, e.Y))
        {
            float itemH = 28f;
            int idx = (int)((e.Y - _colImport.ProfileDropdownBounds.Top) / itemH);
            var (savedProfiles, xmlFiles) = GetColImportSources();
            int totalSources = savedProfiles.Count + xmlFiles.Count;
            int newHovered = idx >= 0 && idx < totalSources ? idx : -1;
            if (newHovered != _colImport.ProfileHoveredIndex)
            {
                _colImport.ProfileHoveredIndex = newHovered;
                _ctx.MarkDirty();
            }
            _ctx.OwnerForm.Cursor = Cursors.Hand;
        }
        else if (_colImport.ProfileHoveredIndex >= 0)
        {
            _colImport.ProfileHoveredIndex = -1;
            _ctx.MarkDirty();
        }

        // Import column dropdown hover
        if (showColumnActions && _colImport.ColumnDropdownOpen
            && !_colImport.ColumnDropdownBounds.IsEmpty
            && _colImport.ColumnDropdownBounds.Contains(e.X, e.Y))
        {
            float itemH = 28f;
            int idx = (int)((e.Y - _colImport.ColumnDropdownBounds.Top) / itemH);
            int newHovered = idx >= 0 && idx < _colImport.SourceColumns.Count ? idx : -1;
            if (newHovered != _colImport.ColumnHoveredIndex)
            {
                _colImport.ColumnHoveredIndex = newHovered;
                _ctx.MarkDirty();
            }
            _ctx.OwnerForm.Cursor = Cursors.Hand;
        }
        else if (_colImport.ColumnHoveredIndex >= 0)
        {
            _colImport.ColumnHoveredIndex = -1;
            _ctx.MarkDirty();
        }

        // Category collapse headers
        foreach (var headerBounds in _scCategoryHeaderBounds.Values)
        {
            if (headerBounds.Contains(e.X, e.Y)) { _ctx.OwnerForm.Cursor = Cursors.Hand; break; }
        }

        // Profile dropdown list items when open
        if (_profileMgmt.DropdownOpen && _profileMgmt.DropdownListBounds.Contains(e.X, e.Y))
            _ctx.OwnerForm.Cursor = Cursors.Hand;

        // Listening timeout
        if (_scListening.IsListening && (DateTime.Now - _scListening.StartTime).TotalMilliseconds > SCListeningTimeoutMs)
        {
            _scListening.IsListening = false;
            _cell.ListeningColumn = null;
        }
    }

    public void OnMouseUp(MouseEventArgs e)
    {
        if (_searchFilter.SearchDragging)
        {
            _searchFilter.SearchDragging = false;
            // If start == end, no text was actually selected — clear selection markers
            if (_searchFilter.SelectionStart == _searchFilter.SelectionEnd)
                ClearSearchSelection();
        }

        if (_scroll.IsDraggingVScroll || _scroll.IsDraggingHScroll)
        {
            _scroll.IsDraggingVScroll = false;
            _scroll.IsDraggingHScroll = false;
            _ctx.MarkDirty();
        }
    }

    public void OnMouseWheel(MouseEventArgs e)
    {
        // Mouse wheel listening — capture for mouse column binding or button capture
        if (_scListening.IsListening && _cell.ListeningColumn is { IsMouse: true } && e.Delta != 0)
        {
            _scListening.PendingMouseWheel = e.Delta > 0 ? "mwheel_up" : "mwheel_down";
            return;
        }
        if (_searchFilter.ButtonCaptureActive && e.Delta != 0)
        {
            _captureMouseWheelPending = e.Delta > 0 ? "mwheel_up" : "mwheel_down";
            return;
        }

        // Action map filter dropdown scroll
        if (_searchFilter.FilterDropdownOpen && _searchFilter.FilterDropdownBounds.Contains(e.X, e.Y))
        {
            float scrollAmount = -e.Delta / 4f;
            _searchFilter.FilterScrollOffset = Math.Clamp(_searchFilter.FilterScrollOffset + scrollAmount, 0, _searchFilter.FilterMaxScroll);
            _ctx.MarkDirty();
            return;
        }

        // SC bindings list scroll
        if (_scBindingsListBounds.Contains(e.X, e.Y))
        {
            float scrollAmount = -e.Delta / 4f;

            if (Control.ModifierKeys.HasFlag(Keys.Shift))
            {
                float maxHScroll = Math.Max(0, _grid.TotalWidth - _grid.VisibleDeviceWidth);
                if (maxHScroll > 0)
                {
                    _grid.HorizontalScroll = Math.Clamp(_grid.HorizontalScroll + scrollAmount, 0, maxHScroll);
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
        var key = keyData & Keys.KeyCode;
        if (_searchFilter.ButtonCaptureActive && key == Keys.Escape)
        {
            StopButtonCapture();
            _ctx.MarkDirty();
            return true;
        }
        if (_searchFilter.SearchBoxFocused)
            return HandleSearchBoxKey(keyData);
        if (_scExportFilenameBoxFocused)
            return HandleExportFilenameBoxKey(keyData);
        return false;
    }

    public void OnMouseLeave()
    {
        _scInstall.HoveredInstallation = -1;
        _scInstall.BrowseHovered = false;
        _scHoveredActionIndex = -1;
        _cell.HoveredCell = (-1, -1);
    }

    public void OnTick()
    {
        if (_scListening.IsListening)
            CheckSCBindingInput();
        if (_searchFilter.CaptureWaitingForRelease)
            CheckCaptureRelease();

        // Animate panel expand/collapse
        bool hasContextual = IsColumnActionsVisible()
            || (_cell.SelectedCell.actionIndex >= 0 && _cell.SelectedCell.colIndex >= 0);
        if (_cpPanel.Anim.Update(_cpPanel.IsExpanded, hasContextual))
            _ctx.MarkDirty();
    }

    public void OnActivated()
    {
        // Defer schema load to first tab activation so BeginInvoke runs with a valid form handle.
        if (_scInstall.Actions is null && !_scInstall.Loading)
            StartSchemaLoad();

        UpdateModifierKeys();
    }

    public void OnDeactivated() { }

    private void BrowseForSCInstallPath()
    {
        // When no installations exist, go straight to folder browse for quick setup
        if (_scInstall.Installations.Count == 0)
        {
            using var folderDialog = new FolderBrowserDialog
            {
                Description = "Select Star Citizen library folder (e.g. Roberts Space Industries)",
                ShowNewFolderButton = false,
                UseDescriptionForTitle = true
            };

            if (folderDialog.ShowDialog(_ctx.OwnerForm) != DialogResult.OK)
                return;

            _ctx.AppSettings.AddCustomSCSearchPath(folderDialog.SelectedPath);
            SyncCustomPathsAndRefresh();
            return;
        }

        // When installations exist, show the libraries management dialog
        var autoDetected = _scInstallationService.GetAutoDetectedLibraryPaths();
        var custom = _ctx.AppSettings.CustomSCSearchPaths;

        bool changed = SCLibrariesDialog.Show(_ctx.OwnerForm, autoDetected, custom,
            out var addedPaths, out var removedPaths);

        if (!changed)
            return;

        foreach (var path in addedPaths)
            _ctx.AppSettings.AddCustomSCSearchPath(path);
        foreach (var path in removedPaths)
            _ctx.AppSettings.RemoveCustomSCSearchPath(path);

        SyncCustomPathsAndRefresh();
    }

    private void SyncCustomPathsAndRefresh()
    {
        _scInstallationService.CustomSearchPaths = _ctx.AppSettings.CustomSCSearchPaths;
        _scInstallationService.Refresh();
        RefreshSCInstallations();

        if (_scInstall.Installations.Count > 0 && _scInstall.SelectedInstallation < _scInstall.Installations.Count)
            LoadSCSchema(_scInstall.Installations[_scInstall.SelectedInstallation], autoLoadProfileForEnvironment: true);

        _ctx.MarkDirty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // State classes — each groups logically-related fields for one sub-feature.
    // All fields are public so partial files can access them via the instance.
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class SCInstallationState
    {
        public List<SCInstallation> Installations = new();
        public int SelectedInstallation;
        public List<SCAction>? Actions;
        public bool Loading;
        public string LoadingMessage = "";
        public int SchemaLoadVersion;
        public SKRect SelectorBounds;
        public bool DropdownOpen;
        public SKRect DropdownBounds;
        public int HoveredInstallation = -1;
        public SKRect BrowseBounds;
        public bool BrowseHovered;
    }

    private sealed class GridLayoutState
    {
        public float ActionColWidth = 300f;
        public float DeviceColMinWidth = 160f;
        public Dictionary<string, float> DeviceColWidths = new();
        public float HorizontalScroll;
        public float TotalWidth;
        public List<SCGridColumn>? Columns;
        public float DeviceColsStart;
        public float VisibleDeviceWidth;
        public SKRect ColumnHeadersBounds;
    }

    private sealed class CellInteractionState
    {
        public (int actionIndex, int colIndex) SelectedCell = (-1, -1);
        public (int actionIndex, int colIndex) HoveredCell = (-1, -1);
        public long LastCellClickTicks;
        public SCGridColumn? ListeningColumn;
    }

    private sealed class InputListeningState
    {
        public bool IsListening;
        public DateTime StartTime;
        public List<string>? PendingModifiers;
        // Baseline fields (populated when listening starts)
        public Dictionary<Guid, float[]>? AxisBaseline;
        public Dictionary<Guid, bool[]>? ButtonBaseline;
        public Dictionary<Guid, int[]>? HatBaseline;
        public int BaselineFrames;
        // Mouse wheel detection — set by OnMouseWheel, consumed by DetectMouseInput
        public string? PendingMouseWheel;
    }

    private sealed class ColumnImportState
    {
        public int HighlightedColumn = -1;
        public SKRect HeaderBounds;
        public int ProfileIndex = -1;
        public bool ProfileDropdownOpen;
        public SKRect ProfileSelectorBounds;
        public SKRect ProfileDropdownBounds;
        public int ProfileHoveredIndex = -1;
        public SCExportProfile? LoadedProfile;
        public List<(string Label, uint VJoyDeviceId, string? PhysicalDeviceId)> SourceColumns = new();
        public int ColumnIndex = -1;
        public bool ColumnDropdownOpen;
        public SKRect ColumnSelectorBounds;
        public SKRect ColumnDropdownBounds;
        public int ColumnHoveredIndex = -1;
        public SKRect ImportButtonBounds;
        public bool ImportButtonHovered;
        public SKRect ClearColumnBounds;
        public bool ClearColumnHovered;
    }

    private sealed class ConflictState
    {
        public HashSet<string> ConflictingBindings = new();
        public HashSet<string> DuplicateActionBindings = new();
        public Dictionary<string, (uint PrimaryVJoyDevice, string PrimaryInputName, string SecondaryInputName)> SharedCells = new();
        public List<(string ActionMap, string ActionName)> ConflictLinks = new();
        public List<SKRect> ConflictLinkBounds = new();
        public int ConflictLinkHovered = -1;
        public int HighlightActionIndex = -1;
        public DateTime HighlightStartTime;
        public HashSet<string> NetworkConflictKeys = new();
    }

    private sealed class ControlProfilesPanelState
    {
        public bool IsExpanded = true;
        public FUIWidgets.PanelSplitAnimator Anim = new() { T = 1f };
        public SKRect HeaderBounds;
    }

    private sealed class CellDetailsState
    {
        public SKRect HeaderBounds;
        public SKRect[] ActivationModeBounds = new SKRect[5];
        public int HoveredModeIndex = -1;
    }

    private sealed class ProfileMgmtState
    {
        public List<SCExportProfileInfo> ExportProfiles = new();
        public SKRect DropdownBounds;
        public bool DropdownOpen;
        public SKRect DropdownListBounds;
        public int HoveredProfileIndex = -1;
        public SKRect NewProfileBounds;
        public bool NewProfileHovered;
        public SKRect ImportProfileBounds;
        public bool ImportProfileHovered;
        public SKRect ProfileEditBounds;
        public bool ProfileEditHovered;
        public SKRect DropdownDeleteBounds;
        public string DropdownDeleteProfileName = "";
    }

    private sealed class SearchFilterState
    {
        public string SearchText = "";
        public int CursorPos;
        public int SelectionStart = -1; // -1 = no selection
        public int SelectionEnd = -1;
        public bool SearchDragging;
        public long LastSearchClickTicks;
        public SKRect SearchBoxBounds;
        public bool SearchBoxFocused;
        public SKRect ShowBoundOnlyBounds;
        public bool ShowBoundOnlyHovered;
        public string ActionMapFilter = "";
        public SKRect FilterBounds;
        public bool FilterDropdownOpen;
        public SKRect FilterDropdownBounds;
        public int HoveredFilter = -1;
        public float FilterScrollOffset;
        public float FilterMaxScroll;
        public List<string> ActionMaps = new();
        public SKRect ShowJSRefBounds;
        public bool ShowJSRefHovered;

        // Button capture mode — press a physical button to search for it
        public bool ButtonCaptureActive;
        public bool ButtonCaptureHovered;
        public SKRect ButtonCaptureBounds;
        // HID path of the device whose button was captured — used to restrict search results
        public string? CaptureDeviceHidPath;
        // True when SearchText was filled by button capture — render as pill
        public bool ButtonCaptureTextActive;
        // Set after capture result is applied — keeps SuppressForwarding=true until the button is released
        public bool CaptureWaitingForRelease;
        // Raw input name (no modifier prefix) used to poll for release, e.g. "button13" or "hat1_up"
        public string? CaptureReleasePendingInput;
        // Tick counter for release-wait timeout (safety valve if device disconnects mid-capture)
        public int CaptureReleaseWaitTicks;
    }

    private sealed class ScrollState
    {
        public bool IsDraggingVScroll;
        public bool IsDraggingHScroll;
        public float DragStartY;
        public float DragStartX;
        public float DragStartOffset;
        public SKRect VScrollBounds;
        public SKRect HScrollBounds;
        public SKRect VThumbBounds;
        public SKRect HThumbBounds;
    }
}
