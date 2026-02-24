using System.Xml;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

/// <summary>
/// SC Bindings tab controller - Star Citizen binding export/import.
/// </summary>
public class SCBindingsTabController : ITabController
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
    private SKRect _scExportFilenameBoxBounds;
    private bool _scExportFilenameBoxFocused;
    private string _scExportFilename = "";
    private SKRect _scImportButtonBounds;
    private bool _scImportButtonHovered;
    private List<SCMappingFile> _scAvailableProfiles = new();
    private bool _scImportDropdownOpen;
    private SKRect _scImportDropdownBounds;
    private int _scHoveredImportProfile = -1;
    private SKRect _scRefreshButtonBounds;
    private bool _scRefreshButtonHovered;
    private SKRect _scClearAllButtonBounds;
    private bool _scClearAllButtonHovered;
    private SKRect _scResetDefaultsButtonBounds;
    private bool _scResetDefaultsButtonHovered;
    private SKRect _scProfileNameBounds;
    private bool _scProfileNameHovered;
    private List<SKRect> _scVJoyMappingBounds = new();
    private int _hoveredVJoyMapping = -1;

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

    // SC binding assignment state
    private bool _scAssigningInput = false;
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

    #region ITabController

    public void Draw(SKCanvas canvas, SKRect bounds, float padLeft, float contentTop, float contentBottom)
    {
        DrawBindingsTabContent(canvas, bounds, padLeft, contentTop, contentBottom);
    }

    public void OnMouseDown(MouseEventArgs e)
    {
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
        _hoveredVJoyMapping = -1;
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

            string? lastActionMap = null;
            float currentY = 0;

            for (int i = 0; i < _scFilteredActions.Count; i++)
            {
                var action = _scFilteredActions[i];

                if (action.ActionMap != lastActionMap)
                {
                    lastActionMap = action.ActionMap;
                    currentY += categoryHeaderHeight;

                    if (_scCollapsedCategories.Contains(action.ActionMap))
                    {
                        while (i < _scFilteredActions.Count - 1 &&
                               _scFilteredActions[i + 1].ActionMap == action.ActionMap)
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

        // vJoy mapping rows
        for (int i = 0; i < _scVJoyMappingBounds.Count; i++)
        {
            if (_scVJoyMappingBounds[i].Contains(e.X, e.Y))
            {
                _hoveredVJoyMapping = i;
                _ctx.OwnerForm.Cursor = Cursors.Hand;
                break;
            }
        }

        // Buttons and selectors
        if (_scRefreshButtonBounds.Contains(e.X, e.Y) ||
            _scExportButtonBounds.Contains(e.X, e.Y) ||
            _scInstallationSelectorBounds.Contains(e.X, e.Y) ||
            _scProfileNameBounds.Contains(e.X, e.Y) ||
            _scActionMapFilterBounds.Contains(e.X, e.Y) ||
            _scAssignInputButtonBounds.Contains(e.X, e.Y) ||
            _scClearBindingButtonBounds.Contains(e.X, e.Y))
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
        }

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

    #endregion

    #region SC Bindings Initialization

    public void Initialize()
    {
        try
        {
            // SC services are now injected via constructor
            // _scInstallationService, _scProfileCacheService, _scSchemaService,
            // _scExportService, and _scExportProfileService are already assigned

            // Ensure vJoy devices are enumerated for SC Bindings columns
            if (_ctx.VJoyDevices.Count == 0 && _ctx.VJoyService is not null)
            {
                _ctx.VJoyDevices = _ctx.VJoyService.EnumerateDevices();
            }

            RefreshSCInstallations();
            RefreshSCExportProfiles();

            // Try to load the last used SC export profile for the current environment.
            // Skip "asteriq" - that was the old auto-generated default name, never user-chosen.
            var currentEnv = CurrentEnvironment;
            var lastProfileName = currentEnv is not null
                ? _ctx.AppSettings.GetLastSCExportProfileForEnvironment(currentEnv)
                : _ctx.AppSettings.LastSCExportProfile;

            if (lastProfileName == "asteriq")
                lastProfileName = null;

            SCExportProfile? loadedProfile = null;
            if (!string.IsNullOrEmpty(lastProfileName) && _ctx.AppSettings.AutoLoadLastSCExportProfile)
            {
                loadedProfile = _scExportProfileService.LoadProfile(lastProfileName);
            }

            if (loadedProfile is not null)
            {
                _scExportProfile = loadedProfile;
                System.Diagnostics.Debug.WriteLine($"[SCBindings] Loaded last SC export for {currentEnv}: {loadedProfile.ProfileName}");
            }
            else
            {
                // No last profile for this environment - load first non-legacy profile
                var firstNamed = _scExportProfiles.FirstOrDefault(p => p.ProfileName != "asteriq");
                if (firstNamed is not null)
                {
                    var first = _scExportProfileService?.LoadProfile(firstNamed.ProfileName);
                    if (first is not null)
                    {
                        _scExportProfile = first;
                        if (currentEnv is not null)
                            _ctx.AppSettings.SetLastSCExportProfileForEnvironment(currentEnv, first.ProfileName);
                    }
                }
                else
                {
                    // Only "asteriq" or no profiles exist - start blank so user names their own
                    _scExportProfile = new SCExportProfile();
                    foreach (var vjoy in _ctx.VJoyDevices.Where(v => v.Exists))
                    {
                        _scExportProfile.SetSCInstance(vjoy.Id, (int)vjoy.Id);
                    }
                }
            }

            // Initial conflict detection
            UpdateConflictingBindings();

            System.Diagnostics.Debug.WriteLine($"[MainForm] SC bindings initialized, {_scInstallations.Count} installations found");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] SC bindings init failed: {ex.Message}");
        }
    }

    private void RefreshSCExportProfiles()
    {
        if (_scExportProfileService is null) return;
        // Exclude profiles with empty names - these are save artifacts from unnamed profiles
        _scExportProfiles = _scExportProfileService.ListProfiles()
            .Where(p => !string.IsNullOrEmpty(p.ProfileName))
            .ToList();
    }

    /// <summary>
    /// Detects conflicting bindings where the same input is assigned to multiple actions.
    /// Stores conflict keys in _scConflictingBindings for rendering.
    /// </summary>
    private void UpdateConflictingBindings()
    {
        _scConflictingBindings.Clear();

        // Track which inputs are used and by which actions
        // Key: "js1_button5" or "kb1_w" etc., Value: list of action keys that use it
        var inputUsage = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // Check user bindings from export profile
        foreach (var binding in _scExportProfile.Bindings)
        {
            int scInstance = _scExportProfile.GetSCInstance(binding.VJoyDevice);
            string inputKey = $"js{scInstance}_{binding.InputName}";
            string actionKey = binding.Key;

            if (!inputUsage.TryGetValue(inputKey, out var actions))
            {
                actions = new List<string>();
                inputUsage[inputKey] = actions;
            }
            actions.Add(actionKey);
        }

        // Find inputs that are used by multiple actions
        foreach (var kvp in inputUsage)
        {
            if (kvp.Value.Count > 1)
            {
                // Mark all actions using this input as conflicting
                foreach (var actionKey in kvp.Value)
                {
                    _scConflictingBindings.Add(actionKey);
                }
            }
        }
    }

    /// <summary>
    /// Gets the list of device columns for the SC bindings grid.
    /// Returns: KB, Mouse, plus one column per configured vJoy device.
    /// </summary>
    private List<SCGridColumn> GetSCGridColumns()
    {
        var columns = new List<SCGridColumn>
        {
            new SCGridColumn { Id = "kb", Header = "KB", DevicePrefix = "kb1", IsKeyboard = true },
            new SCGridColumn { Id = "mouse", Header = "Mouse", DevicePrefix = "mo1", IsMouse = true }
        };

        // Add a column for each vJoy device that exists and is mapped in the export profile
        foreach (var vjoy in _ctx.VJoyDevices.Where(v => v.Exists))
        {
            int scInstance = _scExportProfile.GetSCInstance(vjoy.Id);
            columns.Add(new SCGridColumn
            {
                Id = $"js{scInstance}",
                Header = $"JS{scInstance}",
                DevicePrefix = $"js{scInstance}",
                VJoyDeviceId = vjoy.Id,
                SCInstance = scInstance,
                IsJoystick = true
            });
        }

        return columns;
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
    }

    /// <summary>
    /// Returns the environment string of the currently selected SC installation, or null if none.
    /// </summary>
    private string? CurrentEnvironment =>
        _scInstallations.Count > 0 && _selectedSCInstallation < _scInstallations.Count
            ? _scInstallations[_selectedSCInstallation].Environment
            : null;

    private void RefreshSCInstallations()
    {
        if (_scInstallationService is null) return;

        _scInstallations = _scInstallationService.Installations.ToList();

        // Restore preferred environment selection (e.g. LIVE, PTU)
        var preferredEnv = _ctx.AppSettings.PreferredSCEnvironment;
        if (!string.IsNullOrEmpty(preferredEnv))
        {
            int preferredIndex = _scInstallations.FindIndex(i =>
                string.Equals(i.Environment, preferredEnv, StringComparison.OrdinalIgnoreCase));
            _selectedSCInstallation = preferredIndex >= 0 ? preferredIndex : 0;
        }
        else if (_selectedSCInstallation >= _scInstallations.Count)
        {
            _selectedSCInstallation = 0;
        }

    }

    private void StartSchemaLoad()
    {
        if (_scInstallations.Count > 0 && _selectedSCInstallation < _scInstallations.Count)
            LoadSCSchema(_scInstallations[_selectedSCInstallation]);
    }

    private void LoadSCSchema(SCInstallation installation, bool autoLoadProfileForEnvironment = false)
    {
        if (_scProfileCacheService is null || _scSchemaService is null) return;

        int version = ++_schemaLoadVersion;
        _scLoading = true;
        _scLoadingMessage = "Loading...";
        _scActions = null;
        _scFilteredActions = null;
        _scActionMaps.Clear();
        _scAvailableProfiles = new();      // clear stale profiles from previous installation immediately
        _scProfileDropdownOpen = false;    // close dropdown so it doesn't show stale data
        _scImportDropdownOpen = false;
        _ctx.InvalidateCanvas();

        Task.Run(() =>
        {
            List<SCAction>? actions = null;
            List<string> actionMaps = new();
            List<SCMappingFile> availableProfiles = new();
            string? loadError = null;

            try
            {
                // Check in-memory schema cache first (avoids re-parsing XML on environment switch)
                var cacheKey = installation.GetCacheKey();
                if (s_schemaCache.TryGetValue(cacheKey, out var cachedActions))
                {
                    actions = cachedActions;
                    ReportProgress(version, $"Using cached schema for {installation.Environment}...");
                }
                else
                {
                    ReportProgress(version, "Checking cache...");
                    var profile = _scProfileCacheService.GetOrExtractProfile(installation,
                        msg => ReportProgress(version, msg));

                    if (profile is not null)
                    {
                        ReportProgress(version, "Parsing actions...");
                        actions = _scSchemaService.ParseActions(profile);
                        s_schemaCache[cacheKey] = actions;
                    }
                }

                if (actions is not null)
                {
                    ReportProgress(version, "Building categories...");
                    var joystickActions = _scSchemaService.FilterJoystickActions(actions);
                    actionMaps = SCCategoryMapper.GetSortedCategoriesFromActions(
                        joystickActions.Select(a => (a.ActionMap, a.ActionName))
                    ).ToList();

                    ReportProgress(version, "Loading profiles...");
                    availableProfiles = SCInstallationService.GetExistingProfiles(installation);
                }
            }
            catch (Exception ex) when (ex is XmlException or IOException or ArgumentException)
            {
                loadError = ex.Message;
                System.Diagnostics.Debug.WriteLine($"[SCBindings] Failed to load SC schema: {ex.Message}");
            }
            finally
            {
                // Guarantee _scLoading is cleared even if an unexpected exception escapes the catch above.
                // BeginInvoke queues onto the UI thread; if an uncaught exception later faults the task
                // it becomes an unobserved task exception rather than leaving the UI stuck at "Loading...".
                _ctx.OwnerForm.BeginInvoke(() =>
                {
                    if (version != _schemaLoadVersion) return; // A newer load was started; discard this result

                    _scLoading = false;
                    _scLoadingMessage = loadError is not null ? $"Load failed: {loadError}" : "";
                    _scActions = actions;
                    _scActionMaps = actionMaps;
                    _scAvailableProfiles = availableProfiles;

                    if (actions is not null)
                    {
                        // On an explicit installation switch, load the remembered profile for
                        // the new environment (or fall back to first available).
                        if (autoLoadProfileForEnvironment)
                            LoadProfileForEnvironment(installation.Environment);

                        _scExportProfile.TargetEnvironment = installation.Environment;
                        _scExportProfile.TargetBuildId = installation.BuildId;

                        // NOTE: We intentionally do NOT auto-apply defaults here.
                        // Defaults are only applied when user explicitly clicks "Reset Defaults".
                        RefreshFilteredActions();
                        CalculateDeviceColumnWidths();

                        System.Diagnostics.Debug.WriteLine($"[SCBindings] Loaded {actions.Count} SC actions from {installation.Environment}");
                    }

                    _ctx.InvalidateCanvas();
                });
            } // end finally
        });
    }

    /// <summary>
    /// Loads the last-remembered control profile for the given SC environment.
    /// Falls back to the first available profile, or a blank profile if none exist.
    /// Called on the UI thread from the schema load callback after an installation switch.
    /// </summary>
    private void LoadProfileForEnvironment(string environment)
    {
        var lastProfileName = _ctx.AppSettings.GetLastSCExportProfileForEnvironment(environment);

        SCExportProfile? profile = null;
        if (!string.IsNullOrEmpty(lastProfileName))
            profile = _scExportProfileService?.LoadProfile(lastProfileName);

        // No fallback to global profiles — each environment gets its own remembered profile
        // or starts blank. Never bleed a profile from another installation.
        if (profile is not null)
        {
            _scExportProfile = profile;
            System.Diagnostics.Debug.WriteLine($"[SCBindings] Switched to profile '{profile.ProfileName}' for {environment}");
        }
        else
        {
            _scExportProfile = new SCExportProfile();
            foreach (var vjoy in _ctx.VJoyDevices.Where(v => v.Exists))
                _scExportProfile.SetSCInstance(vjoy.Id, (int)vjoy.Id);
            System.Diagnostics.Debug.WriteLine($"[SCBindings] No remembered profile for {environment} — started blank");
        }

        _scProfileDirty = false;
        UpdateConflictingBindings();
    }

    private void ReportProgress(int version, string message)
    {
        _ctx.OwnerForm.BeginInvoke(() =>
        {
            if (version != _schemaLoadVersion) return;
            _scLoadingMessage = message;
            _ctx.InvalidateCanvas();
        });
    }

    /// <summary>
    /// Calculates dynamic column widths based on the longest binding text in each column.
    /// </summary>
    private void CalculateDeviceColumnWidths()
    {
        _scGridDeviceColWidths.Clear();

        if (_scActions is null) return;

        var columns = GetSCGridColumns();
        float padding = 12f; // Cell padding

        foreach (var col in columns)
        {
            float maxWidth = _scGridDeviceColMinWidth;

            // Determine device type for this column
            SCDeviceType? deviceType = col.IsKeyboard ? SCDeviceType.Keyboard :
                                        col.IsMouse ? SCDeviceType.Mouse :
                                        col.IsJoystick ? SCDeviceType.Joystick : null;

            foreach (var action in _scActions)
            {
                // Check profile bindings for this column
                if (deviceType.HasValue)
                {
                    SCActionBinding? binding = null;
                    if (col.IsJoystick)
                    {
                        binding = _scExportProfile.GetBinding(action.ActionMap, action.ActionName, SCDeviceType.Joystick);
                        // Check if this binding matches the current column's device
                        if (binding is not null && _scExportProfile.GetSCInstance(binding.VJoyDevice) != col.SCInstance)
                            binding = null;
                    }
                    else
                    {
                        binding = _scExportProfile.GetBinding(action.ActionMap, action.ActionName, deviceType.Value);
                    }

                    if (binding is not null && !string.IsNullOrEmpty(binding.InputName))
                    {
                        var components = GetBindingComponents(binding.InputName, binding.Modifiers);
                        float badgesWidth = MeasureMultiKeycapWidth(components, binding.InputType) + padding;
                        maxWidth = Math.Max(maxWidth, badgesWidth);
                    }
                }

                // Also check default bindings
                var defaultBinding = action.DefaultBindings
                    .FirstOrDefault(b => b.DevicePrefix.Equals(col.DevicePrefix, StringComparison.OrdinalIgnoreCase));
                if (defaultBinding is not null && !string.IsNullOrEmpty(defaultBinding.Input))
                {
                    var modifiers = defaultBinding.Modifiers?.Where(m => !string.IsNullOrEmpty(m)).ToList();
                    var components = GetBindingComponents(defaultBinding.Input, modifiers);
                    // Default bindings don't have input type info
                    float badgesWidth = MeasureMultiKeycapWidth(components, null) + padding;
                    maxWidth = Math.Max(maxWidth, badgesWidth);
                }
            }

            _scGridDeviceColWidths[col.Id] = maxWidth;
        }
    }

    /// <summary>
    /// Copies SC default bindings from parsed actions into the export profile.
    /// This follows SCVirtStick's model where the profile contains ALL bindings.
    /// </summary>
    private void ApplyDefaultBindingsToProfile()
    {
        if (_scActions is null) return;

        int kbCount = 0, moCount = 0, jsCount = 0;

        foreach (var action in _scActions)
        {
            foreach (var defaultBinding in action.DefaultBindings)
            {
                // Skip empty bindings (SC uses space for "no binding")
                if (string.IsNullOrWhiteSpace(defaultBinding.Input) || defaultBinding.Input.Trim() == "")
                    continue;

                SCDeviceType deviceType;
                if (defaultBinding.DevicePrefix.StartsWith("kb", StringComparison.OrdinalIgnoreCase))
                {
                    deviceType = SCDeviceType.Keyboard;
                    kbCount++;
                }
                else if (defaultBinding.DevicePrefix.StartsWith("mo", StringComparison.OrdinalIgnoreCase))
                {
                    deviceType = SCDeviceType.Mouse;
                    moCount++;
                }
                else if (defaultBinding.DevicePrefix.StartsWith("js", StringComparison.OrdinalIgnoreCase))
                {
                    deviceType = SCDeviceType.Joystick;
                    jsCount++;
                }
                else
                {
                    continue; // Skip gamepad and unknown
                }

                var binding = new SCActionBinding
                {
                    ActionMap = action.ActionMap,
                    ActionName = action.ActionName,
                    DeviceType = deviceType,
                    InputName = defaultBinding.Input,
                    InputType = InferInputTypeFromName(defaultBinding.Input),
                    Inverted = defaultBinding.Inverted,
                    ActivationMode = defaultBinding.ActivationMode,
                    Modifiers = defaultBinding.Modifiers.ToList()
                };

                // For joystick bindings, set VJoyDevice based on the js instance in prefix
                if (deviceType == SCDeviceType.Joystick)
                {
                    // Extract instance from "js1", "js2", etc.
                    if (defaultBinding.DevicePrefix.Length > 2 &&
                        uint.TryParse(defaultBinding.DevicePrefix.Substring(2), out var jsInstance))
                    {
                        binding.VJoyDevice = jsInstance;
                    }
                    else
                    {
                        binding.VJoyDevice = 1; // Default to js1
                    }
                }

                _scExportProfile.SetBinding(action.ActionMap, action.ActionName, binding);
            }
        }

        _scExportProfileService?.SaveProfile(_scExportProfile);
        System.Diagnostics.Debug.WriteLine($"[MainForm] Applied SC defaults to profile: {kbCount} KB, {moCount} Mouse, {jsCount} JS bindings");
    }

    private void RefreshFilteredActions()
    {
        if (_scActions is null || _scSchemaService is null)
        {
            _scFilteredActions = null;
            return;
        }

        // Start with joystick-relevant actions
        var actions = _scSchemaService.FilterJoystickActions(_scActions);

        // Apply action map filter if set (use category name for filtering)
        // Use GetCategoryNameForAction to respect action-level overrides (e.g., Emergency)
        if (!string.IsNullOrEmpty(_scActionMapFilter))
        {
            actions = actions.Where(a =>
                SCCategoryMapper.GetCategoryNameForAction(a.ActionMap, a.ActionName) == _scActionMapFilter).ToList();
        }

        // Apply search filter if set - search multiple fields like SCVirtStick
        if (!string.IsNullOrEmpty(_scSearchText))
        {
            var searchLower = _scSearchText.ToLowerInvariant();
            actions = actions.Where(a => ActionMatchesSearch(a, searchLower)).ToList();
        }

        // Apply "show bound only" filter if enabled
        if (_scShowBoundOnly)
        {
            actions = actions.Where(a =>
                _scExportProfile.GetBinding(a.ActionMap, a.ActionName) is not null ||
                a.DefaultBindings.Any()
            ).ToList();
        }

        // Sort by category order (like SCVirtStick), then by action name
        // IMPORTANT: Use GetSortOrderForAction to respect action-level overrides (e.g., Emergency)
        _scFilteredActions = actions
            .OrderBy(a => SCCategoryMapper.GetSortOrderForAction(a.ActionMap, a.ActionName))
            .ThenBy(a => SCCategoryMapper.GetCategoryNameForAction(a.ActionMap, a.ActionName))
            .ThenBy(a => a.ActionName)
            .ToList();

        _scBindingsScrollOffset = 0;  // Reset scroll when filter changes
        _scSelectedActionIndex = -1;  // Clear selection
    }

    /// <summary>
    /// Checks if an action matches the search filter (like SCVirtStick's ActionMatchesSearch)
    /// Searches: action name, display name, category, binding input names, modifiers
    /// </summary>
    private bool ActionMatchesSearch(SCAction action, string searchLower)
    {
        // Check action name (raw)
        if (action.ActionName.ToLowerInvariant().Contains(searchLower))
            return true;

        // Check formatted action name (display name)
        if (SCCategoryMapper.FormatActionName(action.ActionName).ToLowerInvariant().Contains(searchLower))
            return true;

        // Check category name
        if (SCCategoryMapper.GetCategoryName(action.ActionMap).ToLowerInvariant().Contains(searchLower))
            return true;

        // Check actionmap name (raw)
        if (action.ActionMap.ToLowerInvariant().Contains(searchLower))
            return true;

        // Check user binding input names
        var userBinding = _scExportProfile.GetBinding(action.ActionMap, action.ActionName);
        if (userBinding is not null)
        {
            if (userBinding.InputName.ToLowerInvariant().Contains(searchLower))
                return true;

            // Check modifiers
            foreach (var modifier in userBinding.Modifiers)
            {
                if (modifier.ToLowerInvariant().Contains(searchLower))
                    return true;
            }
        }

        // Check default binding input names
        foreach (var binding in action.DefaultBindings)
        {
            if (binding.Input.ToLowerInvariant().Contains(searchLower))
                return true;

            if (binding.FullInput.ToLowerInvariant().Contains(searchLower))
                return true;

            foreach (var modifier in binding.Modifiers)
            {
                if (modifier.ToLowerInvariant().Contains(searchLower))
                    return true;
            }
        }

        return false;
    }

    #endregion

    #region SC Bindings Tab Drawing

    private void DrawBindingsTabContent(SKCanvas canvas, SKRect bounds, float pad, float contentTop, float contentBottom)
    {
        float frameInset = 5f;
        var contentBounds = new SKRect(pad, contentTop, bounds.Right - pad, contentBottom);

        // Two-panel layout: Left (bindings table) | Right (Installation + Export stacked)
        // Table on left for more space, controls on right
        float rightPanelWidth = 280f;
        float gap = 10f;

        var leftBounds = new SKRect(contentBounds.Left, contentBounds.Top,
            contentBounds.Right - rightPanelWidth - gap, contentBounds.Bottom);
        var rightBounds = new SKRect(leftBounds.Right + gap, contentBounds.Top,
            contentBounds.Right, contentBounds.Bottom);

        // Split right panel vertically: SC Installation (top) | Export (bottom)
        float installationHeight = 150f; // Compact installation panel
        float verticalGap = 8f;

        var installationBounds = new SKRect(rightBounds.Left, rightBounds.Top,
            rightBounds.Right, rightBounds.Top + installationHeight);
        var exportBounds = new SKRect(rightBounds.Left, installationBounds.Bottom + verticalGap,
            rightBounds.Right, rightBounds.Bottom);

        // LEFT PANEL - SC Action Bindings Table (wider)
        DrawSCBindingsTablePanel(canvas, leftBounds, frameInset);

        // RIGHT TOP - SC Installation (condensed)
        DrawSCInstallationPanelCompact(canvas, installationBounds, frameInset);

        // RIGHT BOTTOM - Export panel
        DrawSCExportPanelCompact(canvas, exportBounds, frameInset);

        // Draw dropdowns last (on top)
        if (_scInstallationDropdownOpen && _scInstallations.Count > 0)
        {
            DrawSCInstallationDropdown(canvas);
        }
        if (_scActionMapFilterDropdownOpen && _scActionMaps.Count > 0)
        {
            DrawSCActionMapFilterDropdown(canvas);
        }
    }

    private void DrawSCInstallationPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(160),
            IsAntialias = true
        };
        canvas.DrawRect(bounds.Inset(frameInset, frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Primary, 30f, 8f);

        float cornerPadding = 20f;
        float y = bounds.Top + frameInset + cornerPadding;
        float leftMargin = bounds.Left + frameInset + cornerPadding;
        float rightMargin = bounds.Right - frameInset - 16;  // 4px aligned
        float lineHeight = FUIRenderer.ScaleLineHeight(20f);

        // Title
        FUIRenderer.DrawText(canvas, "STAR CITIZEN INSTALLATION", new SKPoint(leftMargin, y), FUIColors.TextBright, 14f, true);
        y += FUIRenderer.ScaleLineHeight(35f);

        // Installation selector
        FUIRenderer.DrawText(canvas, "INSTALLATION", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += lineHeight;

        float selectorHeight = 36f;
        _scInstallationSelectorBounds = new SKRect(leftMargin, y, rightMargin, y + selectorHeight);

        string installationText = _scInstallations.Count > 0 && _selectedSCInstallation < _scInstallations.Count
            ? _scInstallations[_selectedSCInstallation].DisplayName
            : "No SC installation found";

        bool selectorHovered = _scInstallationSelectorBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawSelector(canvas, _scInstallationSelectorBounds, installationText, selectorHovered || _scInstallationDropdownOpen, _scInstallations.Count > 0);
        y += selectorHeight + lineHeight;

        // Installation details
        if (_scInstallations.Count > 0 && _selectedSCInstallation < _scInstallations.Count)
        {
            var installation = _scInstallations[_selectedSCInstallation];

            FUIRenderer.DrawText(canvas, "DETAILS", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
            y += lineHeight;

            DrawSCDetailRow(canvas, leftMargin, rightMargin, ref y, "Environment", installation.Environment);
            DrawSCDetailRow(canvas, leftMargin, rightMargin, ref y, "BuildId", installation.BuildId ?? "Unknown");
            DrawSCDetailRow(canvas, leftMargin, rightMargin, ref y, "Path", TruncatePath(installation.InstallPath, 40));

            y += 10f;

            // Schema info
            if (_scActions is not null)
            {
                DrawSCDetailRow(canvas, leftMargin, rightMargin, ref y, "Actions", _scActions.Count.ToString());
                var joystickActions = _scSchemaService?.FilterJoystickActions(_scActions);
                DrawSCDetailRow(canvas, leftMargin, rightMargin, ref y, "Joystick Actions", joystickActions?.Count.ToString() ?? "0");
            }
            else
            {
                FUIRenderer.DrawText(canvas, "Schema not loaded", new SKPoint(leftMargin, y), FUIColors.Warning, 11f);
                y += lineHeight;
            }
        }
        else
        {
            y += 10f;
            FUIRenderer.DrawText(canvas, "Star Citizen not detected.", new SKPoint(leftMargin, y), FUIColors.TextDim, 11f);
            y += lineHeight;
            FUIRenderer.DrawText(canvas, "Install SC or check the installation path.", new SKPoint(leftMargin, y), FUIColors.TextDim, 11f);
            y += lineHeight * 2;
        }

        // Refresh button
        y += 16f;  // 4px aligned
        float buttonWidth = 120f;
        float buttonHeight = FUIRenderer.TouchTargetCompact;  // 32px
        _scRefreshButtonBounds = new SKRect(leftMargin, y, leftMargin + buttonWidth, y + buttonHeight);
        _scRefreshButtonHovered = _scRefreshButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIRenderer.DrawButton(canvas, _scRefreshButtonBounds, "REFRESH",
            _scRefreshButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

        // Export Profile Name section
        y += buttonHeight + 32f;  // 4px aligned
        FUIRenderer.DrawText(canvas, "EXPORT PROFILE", new SKPoint(leftMargin, y), FUIColors.TextBright, 14f, true);
        y += FUIRenderer.ScaleLineHeight(30f);

        FUIRenderer.DrawText(canvas, "PROFILE NAME", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += lineHeight;

        float nameFieldHeight = FUIRenderer.TouchTargetCompact;  // 32px for text inputs
        _scProfileNameBounds = new SKRect(leftMargin, y, rightMargin, y + nameFieldHeight);
        _scProfileNameHovered = _scProfileNameBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        string profileNameDisplay = string.IsNullOrEmpty(_scExportProfile.ProfileName)
            ? "— not saved —"
            : _scProfileDirty ? $"{_scExportProfile.ProfileName}*" : _scExportProfile.ProfileName;
        FUIWidgets.DrawTextFieldReadOnly(canvas, _scProfileNameBounds, profileNameDisplay, _scProfileNameHovered);
        y += nameFieldHeight + 12f;  // 4px aligned

        // Export filename preview
        FUIRenderer.DrawText(canvas, "FILENAME", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += lineHeight;
        string exportFilenameDisplay = string.IsNullOrEmpty(_scExportProfile.ProfileName) ? "— save export to generate filename —" : _scExportProfile.GetExportFileName();
        FUIRenderer.DrawText(canvas, exportFilenameDisplay, new SKPoint(leftMargin, y), FUIColors.TextDim, 11f);
    }

    private void DrawSCExportPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(160),
            IsAntialias = true
        };
        canvas.DrawRect(bounds.Inset(frameInset, frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame, 30f, 8f);

        float cornerPadding = 20f;
        float y = bounds.Top + frameInset + cornerPadding;
        float leftMargin = bounds.Left + frameInset + cornerPadding;
        float rightMargin = bounds.Right - frameInset - 16;  // 4px aligned
        float lineHeight = FUIRenderer.ScaleLineHeight(20f);

        // Title - Import Section
        FUIRenderer.DrawText(canvas, "IMPORT FROM SC", new SKPoint(leftMargin, y), FUIColors.TextBright, 14f, true);
        y += FUIRenderer.ScaleLineHeight(25f);

        // Clear mapping bounds since we removed the UI
        _scVJoyMappingBounds.Clear();

        // Fallback: populate available profiles synchronously if load completed but list is still empty
        if (!_scLoading && _scAvailableProfiles.Count == 0 &&
            _scInstallations.Count > 0 && _selectedSCInstallation < _scInstallations.Count)
        {
            _scAvailableProfiles = SCInstallationService.GetExistingProfiles(_scInstallations[_selectedSCInstallation]);
        }

        // Import button/selector
        float buttonWidth = 200f;
        float buttonHeight = 32f;
        float buttonX = leftMargin + (rightMargin - leftMargin - buttonWidth) / 2;

        _scImportButtonBounds = new SKRect(buttonX, y, buttonX + buttonWidth, y + buttonHeight);
        _scImportButtonHovered = _scImportButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

        string importText = _scAvailableProfiles.Count > 0
            ? $"Import ({_scAvailableProfiles.Count} profiles)"
            : "No profiles found";
        bool canImport = _scAvailableProfiles.Count > 0;

        DrawImportButton(canvas, _scImportButtonBounds, importText, _scImportButtonHovered || _scImportDropdownOpen, canImport);
        y += buttonHeight + 4f;  // 4px aligned

        // Draw import dropdown if open
        if (_scImportDropdownOpen && _scAvailableProfiles.Count > 0)
        {
            DrawSCImportDropdown(canvas, _scImportButtonBounds);
        }

        y += 20f;

        // Title - Export Section
        FUIRenderer.DrawText(canvas, "EXPORT TO SC", new SKPoint(leftMargin, y), FUIColors.TextBright, 14f, true);
        y += FUIRenderer.ScaleLineHeight(25f);

        // Filename input
        FUIRenderer.DrawText(canvas, "FILENAME", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += lineHeight - 2f;

        float filenameBoxHeight = FUIRenderer.TouchTargetCompact;  // 32px for text inputs
        _scExportFilenameBoxBounds = new SKRect(leftMargin, y, rightMargin, y + filenameBoxHeight);

        // Draw text input box
        var boxBorderColor = _scExportFilenameBoxFocused ? FUIColors.Active : FUIColors.Frame;
        using var boxBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2.WithAlpha(120), IsAntialias = true };
        using var boxBorderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = boxBorderColor, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRect(_scExportFilenameBoxBounds, boxBgPaint);
        canvas.DrawRect(_scExportFilenameBoxBounds, boxBorderPaint);

        // Display filename or placeholder
        string displayFilename = string.IsNullOrEmpty(_scExportFilename)
            ? _scExportProfile.GetExportFileName()
            : $"{_scExportFilename}.xml";
        var filenameColor = string.IsNullOrEmpty(_scExportFilename) ? FUIColors.TextDim : FUIColors.TextPrimary;
        FUIRenderer.DrawText(canvas, displayFilename, new SKPoint(leftMargin + 8, y + filenameBoxHeight / 2 + 4), filenameColor, 10f);

        // Show cursor when focused
        if (_scExportFilenameBoxFocused)
        {
            float cursorX = leftMargin + 8 + FUIRenderer.MeasureText(_scExportFilename, 10f);
            using var cursorPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.TextBright, IsAntialias = true };
            canvas.DrawRect(cursorX, y + 5, 1.5f, filenameBoxHeight - 10, cursorPaint);
        }

        y += filenameBoxHeight + 8f;

        // Export path preview (folder)
        if (_scInstallations.Count > 0 && _selectedSCInstallation < _scInstallations.Count && _scExportService is not null)
        {
            var installation = _scInstallations[_selectedSCInstallation];
            FUIRenderer.DrawText(canvas, "TO FOLDER", new SKPoint(leftMargin, y), FUIColors.TextDim, 9f);
            y += lineHeight - 4f;
            FUIRenderer.DrawText(canvas, TruncatePath(installation.MappingsPath, 50), new SKPoint(leftMargin, y), FUIColors.TextDim, 9f);
            y += lineHeight + 5f;
        }

        // Export button
        _scExportButtonBounds = new SKRect(buttonX, y, buttonX + buttonWidth, y + buttonHeight);
        _scExportButtonHovered = _scExportButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

        // Can export if: SC installation exists AND (no JS bindings OR has vJoy mappings)
        var hasJsBindings = _scExportProfile.Bindings.Any(b => b.DeviceType == SCDeviceType.Joystick);
        bool canExport = _scInstallations.Count > 0 && (!hasJsBindings || _scExportProfile.VJoyToSCInstance.Count > 0);
        DrawExportButton(canvas, _scExportButtonBounds, "EXPORT", _scExportButtonHovered, canExport);
        y += buttonHeight + 15f;

        // Status message
        if (!string.IsNullOrEmpty(_scExportStatus))
        {
            var elapsed = DateTime.Now - _scExportStatusTime;
            if (elapsed.TotalSeconds < 10)
            {
                var statusColor = _scExportStatus.Contains("Success") ? FUIColors.Success : FUIColors.Warning;
                FUIRenderer.DrawTextCentered(canvas, _scExportStatus,
                    new SKRect(leftMargin, y, rightMargin, y + 20f), statusColor, 11f);
            }
            else
            {
                _scExportStatus = null;
            }
        }
    }

    private void DrawSCDetailRow(SKCanvas canvas, float leftMargin, float rightMargin, ref float y, string label, string value)
    {
        float lineHeight = FUIRenderer.ScaleLineHeight(18f);
        FUIRenderer.DrawText(canvas, label, new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        FUIRenderer.DrawText(canvas, value, new SKPoint(leftMargin + 120, y), FUIColors.TextDim, 10f);
        y += lineHeight;
    }

    private void DrawSCInstallationDropdown(SKCanvas canvas)
    {
        float itemHeight = 28f;
        float dropdownWidth = _scInstallationSelectorBounds.Width;
        float dropdownHeight = Math.Min(_scInstallations.Count * itemHeight + 8, 200f);

        _scInstallationDropdownBounds = new SKRect(
            _scInstallationSelectorBounds.Left,
            _scInstallationSelectorBounds.Bottom + 2,
            _scInstallationSelectorBounds.Right,
            _scInstallationSelectorBounds.Bottom + 2 + dropdownHeight);

        // Drop shadow with glow effect (FUI style)
        FUIRenderer.DrawPanelShadow(canvas, _scInstallationDropdownBounds, 4f, 4f, 15f);

        // Outer glow (subtle)
        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active.WithAlpha(30),
            StrokeWidth = 3f,
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(4f, 4f)
        };
        canvas.DrawRect(_scInstallationDropdownBounds, glowPaint);

        // Solid opaque background
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Void, IsAntialias = true };
        canvas.DrawRect(_scInstallationDropdownBounds, bgPaint);

        // Inner background
        using var innerBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background0, IsAntialias = true };
        canvas.DrawRect(_scInstallationDropdownBounds.Inset(2, 2), innerBgPaint);

        // L-corner frame (FUI style)
        FUIRenderer.DrawLCornerFrame(canvas, _scInstallationDropdownBounds, FUIColors.Active.WithAlpha(180), 20f, 6f, 1.5f, true);

        // Items
        float y = _scInstallationDropdownBounds.Top + 4;
        for (int i = 0; i < _scInstallations.Count; i++)
        {
            var itemBounds = new SKRect(_scInstallationDropdownBounds.Left + 4, y,
                _scInstallationDropdownBounds.Right - 4, y + itemHeight);

            bool isHovered = i == _hoveredSCInstallation;
            bool isSelected = i == _selectedSCInstallation;

            // FUI hover/selected style with accent bar
            if (isHovered)
            {
                using var hoverPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(40), IsAntialias = true };
                canvas.DrawRect(itemBounds, hoverPaint);
                // Left accent bar
                using var accentPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active, IsAntialias = true };
                canvas.DrawRect(new SKRect(itemBounds.Left, itemBounds.Top + 2, itemBounds.Left + 2, itemBounds.Bottom - 2), accentPaint);
            }
            else if (isSelected)
            {
                using var selectPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(60), IsAntialias = true };
                canvas.DrawRect(new SKRect(itemBounds.Left, itemBounds.Top + 2, itemBounds.Left + 2, itemBounds.Bottom - 2), selectPaint);
            }

            var textColor = isSelected ? FUIColors.Active : (isHovered ? FUIColors.TextBright : FUIColors.TextPrimary);
            FUIRenderer.DrawText(canvas, _scInstallations[i].DisplayName,
                new SKPoint(itemBounds.Left + 12, itemBounds.MidY + 4), textColor, 11f);

            y += itemHeight;
        }
    }

    private void DrawSCInstallationPanelCompact(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(160),
            IsAntialias = true
        };
        canvas.DrawRect(bounds.Inset(frameInset, frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Primary, 30f, 8f);

        float cornerPadding = 15f;
        float y = bounds.Top + frameInset + cornerPadding;
        float leftMargin = bounds.Left + frameInset + cornerPadding;
        float rightMargin = bounds.Right - frameInset - 10;

        // Title
        FUIRenderer.DrawText(canvas, "SC INSTALLATION", new SKPoint(leftMargin, y), FUIColors.TextBright, 11f, true);
        y += 18f;

        // Installation selector
        float selectorHeight = 32f;
        _scInstallationSelectorBounds = new SKRect(leftMargin, y, rightMargin, y + selectorHeight);

        string installationText = _scInstallations.Count > 0 && _selectedSCInstallation < _scInstallations.Count
            ? _scInstallations[_selectedSCInstallation].DisplayName
            : "No SC found";

        bool selectorHovered = _scInstallationSelectorBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawSelector(canvas, _scInstallationSelectorBounds, installationText, selectorHovered || _scInstallationDropdownOpen, _scInstallations.Count > 0);
    }

    private void DrawSCBindingsTablePanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(160),
            IsAntialias = true
        };
        canvas.DrawRect(bounds.Inset(frameInset, frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame, 30f, 8f);

        float cornerPadding = 15f;
        float y = bounds.Top + frameInset + cornerPadding;
        float leftMargin = bounds.Left + frameInset + cornerPadding;
        float rightMargin = bounds.Right - frameInset - 16;  // 4px aligned
        float lineHeight = FUIRenderer.ScaleLineHeight(18f);

        // Title row with action count
        FUIRenderer.DrawText(canvas, "SC ACTIONS", new SKPoint(leftMargin, y), FUIColors.TextBright, 12f, true);

        // Action count on right of title - show "N of T" when filtered
        int actionCount = _scFilteredActions?.Count ?? 0;
        int boundCount = _scFilteredActions?.Count(a => _scExportProfile.GetBinding(a.ActionMap, a.ActionName) is not null) ?? 0;
        bool isFiltered = !string.IsNullOrEmpty(_scActionMapFilter) || !string.IsNullOrEmpty(_scSearchText) || _scShowBoundOnly;
        int totalCount = _scSchemaService is not null && _scActions is not null
            ? _scSchemaService.FilterJoystickActions(_scActions).Count
            : actionCount;
        string countText = isFiltered
            ? $"{actionCount} of {totalCount}, {boundCount} bound"
            : $"{actionCount} actions, {boundCount} bound";
        float countTextWidth = FUIRenderer.MeasureText(countText, 9f);
        FUIRenderer.DrawText(canvas, countText, new SKPoint(rightMargin - countTextWidth, y), FUIColors.TextDim, 9f);

        y += FUIRenderer.ScaleLineHeight(28f);

        // Filter row: [search...] [☐] Bound only    [All Categories ▼]
        float filterRowHeight = 32f;
        float checkboxSize = 16f;
        float filterWidth = 220f;  // Width for category selector
        float gap = 10f;

        // Category filter dropdown on the right
        float filterX = rightMargin - filterWidth;
        _scActionMapFilterBounds = new SKRect(filterX, y, rightMargin, y + filterRowHeight);
        string filterText = string.IsNullOrEmpty(_scActionMapFilter) ? "All Categories" : FormatActionMapName(_scActionMapFilter);
        bool filterHovered = _scActionMapFilterBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawSelector(canvas, _scActionMapFilterBounds, filterText, filterHovered || _scActionMapFilterDropdownOpen, _scActionMaps.Count > 0);

        // Search box on the left (max 280px wide)
        float maxSearchWidth = 280f;
        _scSearchBoxBounds = new SKRect(leftMargin, y, leftMargin + maxSearchWidth, y + filterRowHeight);
        DrawSearchBox(canvas, _scSearchBoxBounds, _scSearchText, _scSearchBoxFocused);

        // Checkbox after search box
        float checkboxX = leftMargin + maxSearchWidth + gap;
        _scShowBoundOnlyBounds = new SKRect(checkboxX, y + (filterRowHeight - checkboxSize) / 2,
            checkboxX + checkboxSize, y + (filterRowHeight + checkboxSize) / 2);
        _scShowBoundOnlyHovered = _scShowBoundOnlyBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        DrawSCCheckbox(canvas, _scShowBoundOnlyBounds, _scShowBoundOnly, _scShowBoundOnlyHovered);

        // "Bound only" label after checkbox
        float labelX = checkboxX + checkboxSize + 6f;
        FUIRenderer.DrawText(canvas, "Bound only", new SKPoint(labelX, y + filterRowHeight / 2 + 4),
            _scShowBoundOnly ? FUIColors.Active : FUIColors.TextDim, 10f);

        y += filterRowHeight + 12f;

        // Get dynamic columns and cache them for mouse handling
        var columns = GetSCGridColumns();
        _scGridColumns = columns;

        // Column layout - fixed action column, device columns have dynamic widths
        float totalWidth = rightMargin - leftMargin;

        // Calculate column widths and X positions
        var colWidths = new float[columns.Count];
        var colXPositions = new float[columns.Count];
        float cumX = 0f;
        for (int c = 0; c < columns.Count; c++)
        {
            colWidths[c] = _scGridDeviceColWidths.TryGetValue(columns[c].Id, out var w) ? w : _scGridDeviceColMinWidth;
            colXPositions[c] = cumX;
            cumX += colWidths[c];
        }
        float totalDeviceColsWidth = cumX;

        // Action column is fixed width
        float actionColWidth = _scGridActionColWidth;

        float availableWidth = totalWidth - actionColWidth - 10f;

        // Calculate if horizontal scrolling is needed
        bool needsHorizontalScroll = totalDeviceColsWidth > availableWidth;
        float visibleDeviceWidth = needsHorizontalScroll ? availableWidth : totalDeviceColsWidth;
        _scGridTotalWidth = totalDeviceColsWidth;
        _scVisibleDeviceWidth = visibleDeviceWidth;

        // Clamp horizontal scroll
        if (needsHorizontalScroll)
        {
            float maxHScroll = totalDeviceColsWidth - visibleDeviceWidth;
            _scGridHorizontalScroll = Math.Clamp(_scGridHorizontalScroll, 0, maxHScroll);
        }
        else
        {
            _scGridHorizontalScroll = 0;
        }

        float deviceColsStart = leftMargin + actionColWidth + 5f;
        _scDeviceColsStart = deviceColsStart;

        // Table header row
        float headerRowHeight = FUIRenderer.TouchTargetMinHeight;  // 24px minimum
        float headerTextY = y + headerRowHeight / 2 + 4f;  // Vertically centered

        // Table header background
        using var headerPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2.WithAlpha(120), IsAntialias = true };
        canvas.DrawRect(new SKRect(leftMargin - 5, y, rightMargin + 5, y + headerRowHeight), headerPaint);

        // Store column headers bounds for click detection
        _scColumnHeadersBounds = new SKRect(deviceColsStart, y, deviceColsStart + visibleDeviceWidth, y + headerRowHeight);

        // Draw ACTION column header
        FUIRenderer.DrawText(canvas, "ACTION", new SKPoint(leftMargin + 18f, headerTextY), FUIColors.TextDim, 9f, true);

        // Draw separator after ACTION column
        using var actionSepPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame.WithAlpha(80), StrokeWidth = 1 };
        canvas.DrawLine(deviceColsStart - 3, y, deviceColsStart - 3, y + headerRowHeight, actionSepPaint);

        // Clip device columns to available area
        canvas.Save();
        var deviceColsClipRect = new SKRect(deviceColsStart, y, deviceColsStart + visibleDeviceWidth, bounds.Bottom);
        canvas.ClipRect(deviceColsClipRect);

        // Draw device column headers
        for (int c = 0; c < columns.Count; c++)
        {
            float colW = colWidths[c];
            float colX = deviceColsStart + colXPositions[c] - _scGridHorizontalScroll;
            if (colX + colW > deviceColsStart && colX < deviceColsStart + visibleDeviceWidth)
            {
                var col = columns[c];

                // Highlight background if this column is selected
                if (c == _scHighlightedColumn)
                {
                    using var highlightPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(40), IsAntialias = true };
                    canvas.DrawRect(new SKRect(colX, y, colX + colW, y + headerRowHeight), highlightPaint);
                }

                // Use consistent theme colors for all column headers
                var headerColor = c == _scHighlightedColumn ? FUIColors.Active :
                                  col.IsJoystick ? FUIColors.Active : FUIColors.TextPrimary;

                // Center the header text in the column
                float headerTextWidth = FUIRenderer.MeasureText(col.Header, 9f);
                float centeredX = colX + (colW - headerTextWidth) / 2;
                FUIRenderer.DrawText(canvas, col.Header, new SKPoint(centeredX, headerTextY), headerColor, 9f, true);

                // Draw column separator on left edge
                using var sepPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame.WithAlpha(50), StrokeWidth = 1 };
                canvas.DrawLine(colX, y, colX, y + headerRowHeight, sepPaint);
            }
        }
        canvas.Restore();

        y += headerRowHeight + 2f;

        // Scrollable action list
        float listTop = y;
        float listBottom = bounds.Bottom - frameInset - (needsHorizontalScroll ? 20f : 15f);
        _scBindingsListBounds = new SKRect(leftMargin - 5, listTop, rightMargin + 5, listBottom);

        // Clip to list area
        canvas.Save();
        canvas.ClipRect(_scBindingsListBounds);

        _scActionRowBounds.Clear();
        float rowHeight = 28f;
        float rowGap = 2f;
        float scrollY = listTop - _scBindingsScrollOffset;

        _scCategoryHeaderBounds.Clear();

        if (_scFilteredActions is null || _scFilteredActions.Count == 0)
        {
            string emptyMsg = _scLoading ? _scLoadingMessage
                : _scActions is null ? "No SC installation found"
                : "No actions match filter";
            FUIRenderer.DrawText(canvas, emptyMsg,
                new SKPoint(leftMargin, scrollY + 20f), FUIColors.TextDim, 11f);
        }
        else
        {
            string? lastActionMap = null;
            float categoryHeaderHeight = 28f;

            for (int i = 0; i < _scFilteredActions.Count; i++)
            {
                var action = _scFilteredActions[i];

                // Use GetCategoryNameForAction to respect action-level overrides (e.g., Emergency)
                string categoryName = SCCategoryMapper.GetCategoryNameForAction(action.ActionMap, action.ActionName);

                // Category header when category changes
                if (categoryName != lastActionMap)
                {
                    lastActionMap = categoryName;
                    bool isCollapsed = _scCollapsedCategories.Contains(categoryName);

                    // Store header bounds for click detection
                    var headerBounds = new SKRect(leftMargin - 5, scrollY, rightMargin + 5, scrollY + categoryHeaderHeight - 2);
                    _scCategoryHeaderBounds[categoryName] = headerBounds;

                    // Draw category header (always visible)
                    if (scrollY >= listTop - categoryHeaderHeight && scrollY < listBottom)
                    {
                        bool headerHovered = headerBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

                        // Background
                        var bgColor = headerHovered ? FUIColors.Primary.WithAlpha(50) : FUIColors.Primary.WithAlpha(30);
                        using var groupBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
                        canvas.DrawRect(headerBounds, groupBgPaint);

                        // Collapse/expand indicator
                        float indicatorX = leftMargin + 2;
                        float indicatorY = scrollY + categoryHeaderHeight / 2;
                        DrawCollapseIndicator(canvas, indicatorX, indicatorY, isCollapsed, headerHovered);

                        // Count actions in this category (same display name)
                        int categoryActionCount = _scFilteredActions.Count(a =>
                            SCCategoryMapper.GetCategoryNameForAction(a.ActionMap, a.ActionName) == categoryName);
                        int categoryBoundCount = _scFilteredActions.Count(a =>
                            SCCategoryMapper.GetCategoryNameForAction(a.ActionMap, a.ActionName) == categoryName &&
                            _scExportProfile.GetBinding(a.ActionMap, a.ActionName) is not null);

                        FUIRenderer.DrawText(canvas, categoryName,
                            new SKPoint(leftMargin + 18, scrollY + categoryHeaderHeight / 2 + 4),
                            headerHovered ? FUIColors.TextBright : FUIColors.Primary, 10f, true);

                        // Action count
                        string countStr = categoryBoundCount > 0
                            ? $"({categoryBoundCount}/{categoryActionCount})"
                            : $"({categoryActionCount})";
                        FUIRenderer.DrawText(canvas, countStr,
                            new SKPoint(leftMargin + actionColWidth - 60, scrollY + categoryHeaderHeight / 2 + 4),
                            FUIColors.TextDim, 9f);
                    }
                    scrollY += categoryHeaderHeight;

                    // If collapsed, skip all actions in this category
                    if (isCollapsed)
                    {
                        // Skip to next category (by display name, using action-aware lookup)
                        while (i < _scFilteredActions.Count - 1 &&
                               SCCategoryMapper.GetCategoryNameForAction(_scFilteredActions[i + 1].ActionMap, _scFilteredActions[i + 1].ActionName) == categoryName)
                        {
                            i++;
                        }
                        continue;
                    }
                }

                var rowBounds = new SKRect(leftMargin - 5, scrollY, rightMargin + 5, scrollY + rowHeight);
                _scActionRowBounds.Add(rowBounds);

                // Only draw if visible
                if (scrollY >= listTop - rowHeight && scrollY < listBottom)
                {
                    bool isHovered = i == _scHoveredActionIndex;
                    bool isSelected = i == _scSelectedActionIndex;
                    bool isEvenRow = i % 2 == 0;

                    // Row background - alternating colors with selection/hover states
                    if (isSelected)
                    {
                        using var selPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(60), IsAntialias = true };
                        canvas.DrawRect(rowBounds, selPaint);
                    }
                    else if (isHovered)
                    {
                        using var hoverPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2.WithAlpha(120), IsAntialias = true };
                        canvas.DrawRect(rowBounds, hoverPaint);
                    }
                    else if (isEvenRow)
                    {
                        // Subtle alternating row background
                        using var altPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2.WithAlpha(40), IsAntialias = true };
                        canvas.DrawRect(rowBounds, altPaint);
                    }

                    float textY = scrollY + rowHeight / 2 + 4;

                    // Draw action name with ellipsis if too long
                    float actionIndent = 18f;
                    string displayName = SCCategoryMapper.FormatActionName(action.ActionName);
                    float maxNameWidth = actionColWidth - actionIndent - 10f;
                    displayName = TruncateTextToWidth(displayName, maxNameWidth, 10f);
                    var nameColor = isSelected ? FUIColors.Active : FUIColors.TextPrimary;
                    FUIRenderer.DrawText(canvas, displayName, new SKPoint(leftMargin + actionIndent, textY), nameColor, 10f);

                    // Draw device column cells (clipped)
                    canvas.Save();
                    canvas.ClipRect(new SKRect(deviceColsStart, scrollY, deviceColsStart + visibleDeviceWidth, scrollY + rowHeight));

                    for (int c = 0; c < columns.Count; c++)
                    {
                        float colW = colWidths[c];
                        float colX = deviceColsStart + colXPositions[c] - _scGridHorizontalScroll;
                        if (colX + colW > deviceColsStart && colX < deviceColsStart + visibleDeviceWidth)
                        {
                            var col = columns[c];
                            var cellBounds = new SKRect(colX, scrollY, colX + colW, scrollY + rowHeight);

                            // Check cell state
                            bool isCellHovered = _scHoveredCell == (i, c);
                            bool isCellSelected = _scSelectedCell == (i, c);
                            bool isCellListening = _scIsListeningForInput && _scSelectedCell == (i, c);
                            bool isColumnHighlighted = c == _scHighlightedColumn;

                            // Draw column highlight background
                            if (isColumnHighlighted && !isCellSelected && !isCellListening)
                            {
                                using var colHighlightPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(20), IsAntialias = true };
                                canvas.DrawRect(cellBounds, colHighlightPaint);
                            }

                            // Draw cell background for hover/selection/listening states
                            if (isCellListening)
                            {
                                // Listening state - use Active color to match theme
                                using var listeningBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(40), IsAntialias = true };
                                canvas.DrawRect(cellBounds, listeningBgPaint);

                                // Draw countdown progress bar at bottom of cell
                                float elapsed = (float)(DateTime.Now - _scListeningStartTime).TotalMilliseconds;
                                float progress = Math.Max(0, 1.0f - elapsed / SCListeningTimeoutMs);
                                float barHeight = 3f;
                                float barWidth = (cellBounds.Width - 4) * progress;
                                var progressBounds = new SKRect(cellBounds.Left + 2, cellBounds.Bottom - barHeight - 2,
                                                                cellBounds.Left + 2 + barWidth, cellBounds.Bottom - 2);
                                using var progressPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active, IsAntialias = true };
                                canvas.DrawRoundRect(progressBounds, 1.5f, 1.5f, progressPaint);

                                // Pulsing border
                                float pulse = (float)(0.6 + 0.4 * Math.Sin((DateTime.Now - _scListeningStartTime).TotalMilliseconds / 150.0));
                                using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active.WithAlpha((byte)(200 * pulse)), StrokeWidth = 2f, IsAntialias = true };
                                canvas.DrawRect(cellBounds.Inset(1, 1), borderPaint);
                            }
                            else if (isCellSelected)
                            {
                                using var selectedPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(50), IsAntialias = true };
                                canvas.DrawRect(cellBounds, selectedPaint);
                            }
                            else if (isCellHovered)
                            {
                                using var hoverPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Primary.WithAlpha(30), IsAntialias = true };
                                canvas.DrawRect(cellBounds, hoverPaint);
                            }

                            List<string>? bindingComponents = null;
                            SKColor textColor = FUIColors.TextPrimary;
                            SCInputType? inputType = null;
                            bool isConflicting = false;

                            // All bindings now come from the profile (SCVirtStick model)
                            // No separate "defaults" - profile contains everything
                            SCActionBinding? binding = null;

                            if (col.IsJoystick)
                            {
                                binding = _scExportProfile.GetBinding(action.ActionMap, action.ActionName, SCDeviceType.Joystick);
                                // Check if this binding matches the current column's device
                                if (binding is not null && _scExportProfile.GetSCInstance(binding.VJoyDevice) != col.SCInstance)
                                    binding = null;
                            }
                            else if (col.IsKeyboard)
                            {
                                binding = _scExportProfile.GetBinding(action.ActionMap, action.ActionName, SCDeviceType.Keyboard);
                            }
                            else if (col.IsMouse)
                            {
                                binding = _scExportProfile.GetBinding(action.ActionMap, action.ActionName, SCDeviceType.Mouse);
                            }

                            if (binding is not null)
                            {
                                bindingComponents = GetBindingComponents(binding.InputName, binding.Modifiers);
                                inputType = binding.InputType;
                                // Check for conflicts (joystick only)
                                if (col.IsJoystick)
                                {
                                    isConflicting = _scConflictingBindings.Contains(binding.Key);
                                }
                            }

                            // Draw cell content
                            if (isCellListening)
                            {
                                // Show "PRESS INPUT" text when listening, centered, using theme Active color
                                string listeningText = "PRESS INPUT";
                                float listeningFontSize = 9f;
                                float listeningTextWidth = FUIRenderer.MeasureText(listeningText, listeningFontSize);
                                float listeningTextX = colX + (colW - listeningTextWidth) / 2;
                                FUIRenderer.DrawText(canvas, listeningText, new SKPoint(listeningTextX, textY - 2), FUIColors.Active, listeningFontSize, true);
                            }
                            else if (bindingComponents is not null && bindingComponents.Count > 0)
                            {
                                // Draw multiple keycap badges for binding (one per key component)
                                DrawMultiKeycapBinding(canvas, cellBounds, bindingComponents, textColor, col.IsJoystick ? inputType : null);

                                // Draw conflict warning indicator
                                if (isConflicting)
                                {
                                    DrawConflictIndicator(canvas, colX + colW - 12, cellBounds.MidY - 4);
                                }
                            }
                            else
                            {
                                // Draw empty indicator, centered
                                FUIRenderer.DrawText(canvas, "—", new SKPoint(colX + colW / 2 - 4, textY), FUIColors.TextDim.WithAlpha(100), 11f);
                            }

                            // Draw column separator
                            using var sepPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame.WithAlpha(40), StrokeWidth = 1 };
                            canvas.DrawLine(colX, scrollY, colX, scrollY + rowHeight, sepPaint);

                            // Conflict indicator is drawn via DrawConflictIndicator - no background tint needed

                            // Draw selection border for selected cell
                            if (isCellSelected && !isCellListening)
                            {
                                using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active, StrokeWidth = 1.5f, IsAntialias = true };
                                canvas.DrawRect(cellBounds.Inset(1, 1), borderPaint);
                            }
                        }
                    }
                    canvas.Restore();
                }

                scrollY += rowHeight + rowGap;
            }

            _scBindingsContentHeight = scrollY - listTop + _scBindingsScrollOffset;
        }

        canvas.Restore();

        // Vertical scrollbar if needed
        _scVScrollbarBounds = SKRect.Empty;
        _scVScrollThumbBounds = SKRect.Empty;
        if (_scBindingsContentHeight > _scBindingsListBounds.Height)
        {
            float scrollbarWidth = 8f;  // Slightly wider for easier clicking
            float scrollbarX = rightMargin - scrollbarWidth + 10;
            float scrollbarHeight = _scBindingsListBounds.Height;
            float thumbHeight = Math.Max(30f, scrollbarHeight * (_scBindingsListBounds.Height / _scBindingsContentHeight));
            float maxVScroll = _scBindingsContentHeight - _scBindingsListBounds.Height;
            float thumbY = listTop + (maxVScroll > 0 ? (_scBindingsScrollOffset / maxVScroll) * (scrollbarHeight - thumbHeight) : 0);

            _scVScrollbarBounds = new SKRect(scrollbarX, listTop, scrollbarX + scrollbarWidth, listTop + scrollbarHeight);
            _scVScrollThumbBounds = new SKRect(scrollbarX, thumbY, scrollbarX + scrollbarWidth, thumbY + thumbHeight);

            bool vScrollHovered = _scVScrollbarBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y) || _scIsDraggingVScroll;

            using var trackPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2.WithAlpha(vScrollHovered ? (byte)120 : (byte)80), IsAntialias = true };
            canvas.DrawRoundRect(_scVScrollbarBounds, 4f, 4f, trackPaint);

            using var thumbPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = vScrollHovered ? FUIColors.Active : FUIColors.Frame.WithAlpha(180), IsAntialias = true };
            canvas.DrawRoundRect(_scVScrollThumbBounds, 4f, 4f, thumbPaint);
        }

        // Horizontal scrollbar if needed
        _scHScrollbarBounds = SKRect.Empty;
        _scHScrollThumbBounds = SKRect.Empty;
        if (needsHorizontalScroll)
        {
            float scrollbarHeight = 8f;  // Slightly taller for easier clicking
            float scrollbarY = listBottom + 5f;
            float scrollbarWidth = visibleDeviceWidth;
            float thumbWidth = Math.Max(30f, scrollbarWidth * (visibleDeviceWidth / totalDeviceColsWidth));
            float maxHScroll = totalDeviceColsWidth - visibleDeviceWidth;
            float thumbX = deviceColsStart + (maxHScroll > 0 ? (_scGridHorizontalScroll / maxHScroll) * (scrollbarWidth - thumbWidth) : 0);

            _scHScrollbarBounds = new SKRect(deviceColsStart, scrollbarY, deviceColsStart + scrollbarWidth, scrollbarY + scrollbarHeight);
            _scHScrollThumbBounds = new SKRect(thumbX, scrollbarY, thumbX + thumbWidth, scrollbarY + scrollbarHeight);

            bool hScrollHovered = _scHScrollbarBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y) || _scIsDraggingHScroll;

            using var trackPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2.WithAlpha(hScrollHovered ? (byte)120 : (byte)80), IsAntialias = true };
            canvas.DrawRoundRect(_scHScrollbarBounds, 4f, 4f, trackPaint);

            using var thumbPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = hScrollHovered ? FUIColors.Active : FUIColors.Frame.WithAlpha(180), IsAntialias = true };
            canvas.DrawRoundRect(_scHScrollThumbBounds, 4f, 4f, thumbPaint);
        }
    }

    /// <summary>
    /// Formats a binding input for display in a grid cell (with modifiers)
    /// Uses a more readable separator between modifiers and the main key
    /// </summary>
    private string FormatBindingForCell(string input, List<string>? modifiers)
    {
        // For single string display (tooltips, width calculation, etc.)
        var components = GetBindingComponents(input, modifiers);
        return string.Join(" + ", components);
    }

    /// <summary>
    /// Gets the individual key components for a binding (modifiers + main key)
    /// Each component will be rendered as a separate keycap badge
    /// </summary>
    private List<string> GetBindingComponents(string input, List<string>? modifiers)
    {
        var components = new List<string>();

        if (modifiers is not null)
        {
            foreach (var mod in modifiers)
            {
                var formatted = FormatModifierName(mod);
                if (!string.IsNullOrEmpty(formatted))
                    components.Add(formatted);
            }
        }

        components.Add(FormatInputName(input));
        return components;
    }

    /// <summary>
    /// Formats a modifier name for display (e.g., "lshift" -> "SHFT", "lctrl" -> "CTRL")
    /// </summary>
    private string FormatModifierName(string modifier)
    {
        if (string.IsNullOrEmpty(modifier))
            return "";

        var lower = modifier.ToLowerInvariant();

        // Map common modifiers to short display names
        if (lower.Contains("shift")) return "SHFT";
        if (lower.Contains("ctrl") || lower.Contains("control")) return "CTRL";
        if (lower.Contains("alt")) return "ALT";

        // Generic cleanup for unknown modifiers
        var cleaned = lower.TrimStart('l', 'r').ToUpperInvariant();
        if (cleaned.Length > 4)
            cleaned = cleaned.Substring(0, 4);

        return cleaned;
    }

    /// <summary>
    /// Formats an input name for display (e.g., "button1" -> "Btn1", "x" -> "X")
    /// </summary>
    private string FormatInputName(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Handle button inputs
        if (input.StartsWith("button", StringComparison.OrdinalIgnoreCase))
        {
            var num = input.Substring(6);
            return $"Btn{num}";
        }

        // Handle mouse wheel inputs (mwheel_up, mwheel_down)
        if (input.StartsWith("mwheel_", StringComparison.OrdinalIgnoreCase))
        {
            var dir = input.Substring(7);
            return dir.ToLower() switch
            {
                "up" => "WhlUp",
                "down" => "WhlDn",
                _ => $"Whl{char.ToUpper(dir[0])}"
            };
        }

        // Handle mouse axis inputs (maxis_x, maxis_y)
        if (input.StartsWith("maxis_", StringComparison.OrdinalIgnoreCase))
        {
            var axis = input.Substring(6).ToUpper();
            return $"M{axis}";
        }

        // Handle mouse button inputs (mouse1, mouse2, etc.)
        if (input.StartsWith("mouse", StringComparison.OrdinalIgnoreCase))
        {
            var num = input.Substring(5);
            return $"M{num}";
        }

        // Handle single letter axis inputs (x, y, z, etc.)
        if (input.Length == 1)
            return input.ToUpper();

        // Handle hat inputs (hat1_up -> H1UP)
        if (input.StartsWith("hat", StringComparison.OrdinalIgnoreCase))
        {
            return input.ToUpper().Replace("HAT", "H").Replace("_", "");
        }

        // Handle rotational axes (rx, ry, rz -> RX, RY, RZ)
        if (input.Length == 2 && input[0] == 'r' && char.IsLetter(input[1]))
        {
            return input.ToUpper();
        }

        // Handle slider inputs
        if (input.StartsWith("slider", StringComparison.OrdinalIgnoreCase))
        {
            var num = input.Substring(6);
            return $"Sl{num}";
        }

        // Default: capitalize and truncate if too long
        var result = char.ToUpper(input[0]) + (input.Length > 1 ? input.Substring(1) : "");
        if (result.Length > 8)
            result = result.Substring(0, 8);
        return result;
    }

    /// <summary>
    /// Draws a keycap-style badge for a binding
    /// </summary>
    private void DrawBindingBadge(SKCanvas canvas, float x, float y, float maxWidth, string text, SKColor color, bool isDefault, SCInputType? inputType = null)
        => SCBindingsRenderer.DrawBindingBadge(canvas, x, y, maxWidth, text, color, isDefault, inputType);

    private void DrawBindingBadgeCentered(SKCanvas canvas, SKRect cellBounds, string text, SKColor color, bool isDefault, SCInputType? inputType = null)
        => SCBindingsRenderer.DrawBindingBadgeCentered(canvas, cellBounds, text, color, isDefault, inputType);

    private void DrawMultiKeycapBinding(SKCanvas canvas, SKRect cellBounds, List<string> components, SKColor color, SCInputType? inputType)
        => SCBindingsRenderer.DrawMultiKeycapBinding(canvas, cellBounds, components, color, inputType);

    private float MeasureMultiKeycapWidth(List<string> components, SCInputType? inputType)
        => SCBindingsRenderer.MeasureMultiKeycapWidth(components, inputType);

    private void DrawInputTypeIndicator(SKCanvas canvas, float x, float centerY, SCInputType inputType, SKColor color)
        => SCBindingsRenderer.DrawInputTypeIndicator(canvas, x, centerY, inputType, color);

    private void DrawConflictIndicator(SKCanvas canvas, float x, float y)
        => SCBindingsRenderer.DrawConflictIndicator(canvas, x, y);

    /// <summary>
    /// Detects the input type from an input name (for joystick bindings)
    /// </summary>
    private static SCInputType DetectInputTypeFromName(string inputName)
    {
        if (string.IsNullOrEmpty(inputName))
            return SCInputType.Button;

        var lower = inputName.ToLowerInvariant();

        // Hat/POV inputs
        if (lower.Contains("hat") || lower.Contains("pov"))
            return SCInputType.Hat;

        // Axis inputs (x, y, z, rx, ry, rz, slider, throttle, etc.)
        if (lower is "x" or "y" or "z" or "rx" or "ry" or "rz" or "rotx" or "roty" or "rotz")
            return SCInputType.Axis;

        if (lower.StartsWith("slider") || lower.StartsWith("throttle"))
            return SCInputType.Axis;

        // Default to button
        return SCInputType.Button;
    }

    private string TruncateTextToWidth(string text, float maxWidth, float fontSize)
        => FUIWidgets.TruncateTextToWidth(text, maxWidth, fontSize);

    private void DrawSCExportPanelCompact(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(160),
            IsAntialias = true
        };
        canvas.DrawRect(bounds.Inset(frameInset, frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame, 30f, 8f);

        float cornerPadding = 15f;
        float y = bounds.Top + frameInset + cornerPadding;
        float leftMargin = bounds.Left + frameInset + cornerPadding;
        float rightMargin = bounds.Right - frameInset - 10;
        float lineHeight = FUIRenderer.ScaleLineHeight(15f);
        float buttonGap = 6f;

        // Title
        FUIRenderer.DrawText(canvas, "CONTROL PROFILES", new SKPoint(leftMargin, y), FUIColors.TextBright, 11f, true);
        y += 18f;

        // Control Profile dropdown (full width)
        float dropdownHeight = 32f;
        _scProfileDropdownBounds = new SKRect(leftMargin, y, rightMargin, y + dropdownHeight);
        bool dropdownHovered = _scProfileDropdownBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        string dropdownLabel = string.IsNullOrEmpty(_scExportProfile.ProfileName)
            ? "— No Profile Selected —"
            : _scProfileDirty ? $"{_scExportProfile.ProfileName}*" : _scExportProfile.ProfileName;
        DrawSCProfileDropdownWide(canvas, _scProfileDropdownBounds, dropdownLabel, dropdownHovered, _scProfileDropdownOpen);
        y += dropdownHeight + 6f;

        // Buttons row: + New, Save (aligned right)
        float textBtnWidth = 52f;  // 4px aligned
        float textBtnHeight = FUIRenderer.TouchTargetMinHeight;  // 24px minimum

        // Save button (rightmost)
        _scSaveProfileButtonBounds = new SKRect(rightMargin - textBtnWidth, y, rightMargin, y + textBtnHeight);
        _scSaveProfileButtonHovered = _scSaveProfileButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        DrawTextButton(canvas, _scSaveProfileButtonBounds, "Save", _scSaveProfileButtonHovered);

        // New button (left of Save)
        float newBtnX = rightMargin - textBtnWidth * 2 - buttonGap;
        _scNewProfileButtonBounds = new SKRect(newBtnX, y, newBtnX + textBtnWidth, y + textBtnHeight);
        _scNewProfileButtonHovered = _scNewProfileButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        DrawTextButton(canvas, _scNewProfileButtonBounds, "+ New", _scNewProfileButtonHovered);

        y += textBtnHeight + 10f;

        // Draw profile dropdown list if open (shows both Asteriq profiles and SC mapping files)
        if (_scProfileDropdownOpen)
        {
            // Active profile is shown in the header, not in the list
            int asteriqCount = _scExportProfiles.Count(p => p.ProfileName != _scExportProfile.ProfileName);
            int scFileCount = _scAvailableProfiles.Count;
            int totalItems = asteriqCount + (scFileCount > 0 ? scFileCount + 1 : 0); // +1 for separator
            float listHeight = Math.Min(totalItems * 24f + (scFileCount > 0 ? 16f : 0f) + 8f, 240f); // +16 for "IMPORT FROM SC" label
            _scProfileDropdownListBounds = new SKRect(leftMargin, _scProfileDropdownBounds.Bottom + 2, rightMargin, _scProfileDropdownBounds.Bottom + 2 + listHeight);
            DrawSCProfileDropdownList(canvas, _scProfileDropdownListBounds);
        }

        // Selected action info
        if (_scSelectedActionIndex >= 0 && _scFilteredActions is not null && _scSelectedActionIndex < _scFilteredActions.Count)
        {
            var selectedAction = _scFilteredActions[_scSelectedActionIndex];

            FUIRenderer.DrawText(canvas, "SELECTED ACTION", new SKPoint(leftMargin, y), FUIColors.Active, 9f, true);
            y += lineHeight;

            string actionDisplay = TruncateTextToWidth(selectedAction.ActionName, rightMargin - leftMargin - 10, 10f);
            FUIRenderer.DrawText(canvas, actionDisplay, new SKPoint(leftMargin, y), FUIColors.TextPrimary, 10f);
            y += lineHeight;

            FUIRenderer.DrawText(canvas, $"Type: {selectedAction.InputType}", new SKPoint(leftMargin, y), FUIColors.TextDim, 9f);
            y += lineHeight + 6f;

            // Assign/Clear buttons
            float btnWidth = (rightMargin - leftMargin - 8) / 2;
            float btnHeight = 24f;

            _scAssignInputButtonBounds = new SKRect(leftMargin, y, leftMargin + btnWidth, y + btnHeight);
            _scAssignInputButtonHovered = _scAssignInputButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

            var existingBinding = _scExportProfile.GetBinding(selectedAction.ActionMap, selectedAction.ActionName);

            if (_scAssigningInput)
            {
                // Show "waiting for input" state - use Active color to match theme
                using var waitBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(80), IsAntialias = true };
                canvas.DrawRect(_scAssignInputButtonBounds, waitBgPaint);
                FUIRenderer.DrawTextCentered(canvas, "PRESS INPUT...", _scAssignInputButtonBounds, FUIColors.Active, 9f);
            }
            else
            {
                FUIRenderer.DrawButton(canvas, _scAssignInputButtonBounds, "ASSIGN",
                    _scAssignInputButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
            }

            _scClearBindingButtonBounds = new SKRect(leftMargin + btnWidth + 8, y, rightMargin, y + btnHeight);
            _scClearBindingButtonHovered = _scClearBindingButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            bool hasBinding = existingBinding is not null;

            if (hasBinding)
            {
                FUIRenderer.DrawButton(canvas, _scClearBindingButtonBounds, "CLEAR",
                    _scClearBindingButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
            }
            else
            {
                // Disabled clear button
                using var disabledPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2.WithAlpha(60), IsAntialias = true };
                canvas.DrawRect(_scClearBindingButtonBounds, disabledPaint);
                FUIRenderer.DrawTextCentered(canvas, "CLEAR", _scClearBindingButtonBounds, FUIColors.TextDim.WithAlpha(100), 10f);
            }

            y += btnHeight + 10f;
        }

        // Clear All / Reset Defaults buttons
        y = bounds.Bottom - frameInset - 95f;
        float smallBtnWidth = (rightMargin - leftMargin - 5) / 2;
        float smallBtnHeight = 24f;

        _scClearAllButtonBounds = new SKRect(leftMargin, y, leftMargin + smallBtnWidth, y + smallBtnHeight);
        _scClearAllButtonHovered = _scClearAllButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        bool hasBoundActions = _scExportProfile.Bindings.Count > 0;
        if (hasBoundActions)
        {
            FUIRenderer.DrawButton(canvas, _scClearAllButtonBounds, "CLEAR ALL",
                _scClearAllButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
        }
        else
        {
            using var disabledPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2.WithAlpha(60), IsAntialias = true };
            canvas.DrawRect(_scClearAllButtonBounds, disabledPaint);
            FUIRenderer.DrawTextCentered(canvas, "CLEAR ALL", _scClearAllButtonBounds, FUIColors.TextDim.WithAlpha(100), 9f);
        }

        _scResetDefaultsButtonBounds = new SKRect(leftMargin + smallBtnWidth + 5, y, rightMargin, y + smallBtnHeight);
        _scResetDefaultsButtonHovered = _scResetDefaultsButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIRenderer.DrawButton(canvas, _scResetDefaultsButtonBounds, "RESET DFLTS",
            _scResetDefaultsButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

        y += smallBtnHeight + 8f;

        // Export button at bottom
        float buttonWidth = rightMargin - leftMargin;
        float buttonHeight = 32f;
        _scExportButtonBounds = new SKRect(leftMargin, y, rightMargin, y + buttonHeight);
        _scExportButtonHovered = _scExportButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

        bool canExport = _scInstallations.Count > 0;
        DrawExportButton(canvas, _scExportButtonBounds, "EXPORT TO SC", _scExportButtonHovered, canExport);
        y += buttonHeight + 5f;

        // Status message
        if (!string.IsNullOrEmpty(_scExportStatus))
        {
            var elapsed = DateTime.Now - _scExportStatusTime;
            if (elapsed.TotalSeconds < 10)
            {
                var statusColor = _scExportStatus.Contains("Success") ? FUIColors.Success : FUIColors.Warning;
                FUIRenderer.DrawTextCentered(canvas, _scExportStatus,
                    new SKRect(leftMargin, y, rightMargin, y + 16f), statusColor, 9f);
            }
            else
            {
                _scExportStatus = null;
            }
        }
    }

    private void DrawVJoyMappingRow(SKCanvas canvas, SKRect bounds, uint vjoyId, int scInstance, bool isHovered)
        => SCBindingsRenderer.DrawVJoyMappingRow(canvas, bounds, vjoyId, scInstance, isHovered);

    private void DrawVJoyMappingRowCompact(SKCanvas canvas, SKRect bounds, uint vjoyId, int scInstance, bool isHovered)
        => SCBindingsRenderer.DrawVJoyMappingRowCompact(canvas, bounds, vjoyId, scInstance, isHovered);

    private void DrawExportButton(SKCanvas canvas, SKRect bounds, string text, bool isHovered, bool isEnabled)
        => FUIWidgets.DrawExportButton(canvas, bounds, text, isHovered, isEnabled);

    private void DrawImportButton(SKCanvas canvas, SKRect bounds, string text, bool isHovered, bool isEnabled)
        => FUIWidgets.DrawImportButton(canvas, bounds, text, isHovered, isEnabled);

    private void DrawSCImportDropdown(SKCanvas canvas, SKRect buttonBounds)
    {
        float itemHeight = 28f;
        float dropdownWidth = buttonBounds.Width;
        int itemCount = _scAvailableProfiles.Count;
        float totalContentHeight = itemCount * itemHeight + 4;
        float maxDropdownHeight = 200f;
        float dropdownHeight = Math.Min(totalContentHeight, maxDropdownHeight);

        _scImportDropdownBounds = new SKRect(
            buttonBounds.Left,
            buttonBounds.Bottom + 2,
            buttonBounds.Right,
            buttonBounds.Bottom + 2 + dropdownHeight);

        // Shadow
        FUIRenderer.DrawPanelShadow(canvas, _scImportDropdownBounds, 2f, 2f, 8f);

        // Background
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1.WithAlpha(245), IsAntialias = true };
        canvas.DrawRect(_scImportDropdownBounds, bgPaint);

        // Border
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Primary.WithAlpha(150), StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRect(_scImportDropdownBounds, borderPaint);

        // Items
        float y = _scImportDropdownBounds.Top + 2;
        float leftMargin = _scImportDropdownBounds.Left + 8;
        float rightMargin = _scImportDropdownBounds.Right - 8;

        for (int i = 0; i < _scAvailableProfiles.Count; i++)
        {
            var profile = _scAvailableProfiles[i];
            var itemBounds = new SKRect(_scImportDropdownBounds.Left, y, _scImportDropdownBounds.Right, y + itemHeight);

            bool isHovered = itemBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            if (isHovered)
                _scHoveredImportProfile = i;

            if (isHovered)
            {
                using var hoverPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Primary.WithAlpha(60), IsAntialias = true };
                canvas.DrawRect(itemBounds, hoverPaint);
            }

            var textColor = isHovered ? FUIColors.TextBright : FUIColors.TextPrimary;
            FUIRenderer.DrawText(canvas, profile.DisplayName, new SKPoint(leftMargin, y + itemHeight / 2 + 4), textColor, 11f);

            // Show file date on right
            var dateText = profile.LastModified.ToString("MM/dd HH:mm");
            float dateWidth = FUIRenderer.MeasureText(dateText, 9f);
            FUIRenderer.DrawText(canvas, dateText, new SKPoint(rightMargin - dateWidth, y + itemHeight / 2 + 3), FUIColors.TextDim, 9f);

            y += itemHeight;
        }
    }

    private void DrawSCActionMapFilterDropdown(SKCanvas canvas)
    {
        float itemHeight = 24f;
        float dropdownWidth = _scActionMapFilterBounds.Width;  // Match selector width exactly
        int itemCount = _scActionMaps.Count + 1; // +1 for "All Categories"
        float totalContentHeight = itemCount * itemHeight + 4;
        float maxDropdownHeight = 300f;
        float dropdownHeight = Math.Min(totalContentHeight, maxDropdownHeight);
        bool needsScroll = totalContentHeight > maxDropdownHeight;
        float scrollbarWidth = needsScroll ? 8f : 0f;

        // Calculate max scroll
        _scActionMapFilterMaxScroll = Math.Max(0, totalContentHeight - dropdownHeight);

        // Clamp scroll offset
        _scActionMapFilterScrollOffset = Math.Max(0, Math.Min(_scActionMapFilterScrollOffset, _scActionMapFilterMaxScroll));

        _scActionMapFilterDropdownBounds = new SKRect(
            _scActionMapFilterBounds.Right - dropdownWidth,
            _scActionMapFilterBounds.Bottom + 2,
            _scActionMapFilterBounds.Right,
            _scActionMapFilterBounds.Bottom + 2 + dropdownHeight);

        // Drop shadow with glow effect (FUI style)
        FUIRenderer.DrawPanelShadow(canvas, _scActionMapFilterDropdownBounds, 4f, 4f, 15f);

        // Outer glow (subtle)
        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active.WithAlpha(30),
            StrokeWidth = 3f,
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(4f, 4f)
        };
        canvas.DrawRect(_scActionMapFilterDropdownBounds, glowPaint);

        // Solid opaque background
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Void, IsAntialias = true };
        canvas.DrawRect(_scActionMapFilterDropdownBounds, bgPaint);

        // Inner background
        using var innerBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background0, IsAntialias = true };
        canvas.DrawRect(_scActionMapFilterDropdownBounds.Inset(2, 2), innerBgPaint);

        // L-corner frame (FUI style)
        FUIRenderer.DrawLCornerFrame(canvas, _scActionMapFilterDropdownBounds, FUIColors.Active.WithAlpha(180), 20f, 6f, 1.5f, true);

        // Clip for scrolling
        canvas.Save();
        canvas.ClipRect(_scActionMapFilterDropdownBounds);

        // Items - apply scroll offset
        float y = _scActionMapFilterDropdownBounds.Top + 2 - _scActionMapFilterScrollOffset;

        // "All Categories" option
        var allItemBounds = new SKRect(_scActionMapFilterDropdownBounds.Left + 2, y,
            _scActionMapFilterDropdownBounds.Right - 2 - scrollbarWidth, y + itemHeight);

        bool allHovered = _scHoveredActionMapFilter == -1 && allItemBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        bool allSelected = string.IsNullOrEmpty(_scActionMapFilter);

        // Only draw if visible
        if (allItemBounds.Bottom > _scActionMapFilterDropdownBounds.Top && allItemBounds.Top < _scActionMapFilterDropdownBounds.Bottom)
        {
            // FUI hover/selected style with accent bar
            if (allHovered)
            {
                using var hoverPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(40), IsAntialias = true };
                canvas.DrawRect(allItemBounds, hoverPaint);
                // Left accent bar
                using var accentPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active, IsAntialias = true };
                canvas.DrawRect(new SKRect(allItemBounds.Left, allItemBounds.Top + 2, allItemBounds.Left + 2, allItemBounds.Bottom - 2), accentPaint);
            }
            else if (allSelected)
            {
                using var selectPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(60), IsAntialias = true };
                canvas.DrawRect(new SKRect(allItemBounds.Left, allItemBounds.Top + 2, allItemBounds.Left + 2, allItemBounds.Bottom - 2), selectPaint);
            }

            var allTextColor = allSelected ? FUIColors.Active : (allHovered ? FUIColors.TextBright : FUIColors.TextPrimary);
            FUIRenderer.DrawText(canvas, "All Categories",
                new SKPoint(allItemBounds.Left + 10, allItemBounds.MidY + 4), allTextColor, 10f);
        }
        y += itemHeight;

        // Category items
        for (int i = 0; i < _scActionMaps.Count; i++)
        {
            var itemBounds = new SKRect(_scActionMapFilterDropdownBounds.Left + 2, y,
                _scActionMapFilterDropdownBounds.Right - 2 - scrollbarWidth, y + itemHeight);

            // Only draw if visible
            if (itemBounds.Bottom > _scActionMapFilterDropdownBounds.Top && itemBounds.Top < _scActionMapFilterDropdownBounds.Bottom)
            {
                bool isHovered = i == _scHoveredActionMapFilter;
                bool isSelected = _scActionMapFilter == _scActionMaps[i];

                // FUI hover/selected style with accent bar
                if (isHovered)
                {
                    using var hoverPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(40), IsAntialias = true };
                    canvas.DrawRect(itemBounds, hoverPaint);
                    // Left accent bar
                    using var accentPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active, IsAntialias = true };
                    canvas.DrawRect(new SKRect(itemBounds.Left, itemBounds.Top + 2, itemBounds.Left + 2, itemBounds.Bottom - 2), accentPaint);
                }
                else if (isSelected)
                {
                    using var selectPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(60), IsAntialias = true };
                    canvas.DrawRect(new SKRect(itemBounds.Left, itemBounds.Top + 2, itemBounds.Left + 2, itemBounds.Bottom - 2), selectPaint);
                }

                var textColor = isSelected ? FUIColors.Active : (isHovered ? FUIColors.TextBright : FUIColors.TextPrimary);
                FUIRenderer.DrawText(canvas, FormatActionMapName(_scActionMaps[i]),
                    new SKPoint(itemBounds.Left + 10, itemBounds.MidY + 4), textColor, 10f);
            }

            y += itemHeight;
        }

        canvas.Restore();

        // Draw scrollbar if needed
        if (needsScroll)
        {
            float scrollTrackX = _scActionMapFilterDropdownBounds.Right - scrollbarWidth - 2;
            float scrollTrackY = _scActionMapFilterDropdownBounds.Top + 2;
            float scrollTrackHeight = dropdownHeight - 4;

            // Scrollbar track
            using var trackPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2.WithAlpha(80), IsAntialias = true };
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(scrollTrackX, scrollTrackY, scrollTrackX + scrollbarWidth, scrollTrackY + scrollTrackHeight), 2f), trackPaint);

            // Scrollbar thumb
            float thumbHeight = Math.Max(20f, scrollTrackHeight * (dropdownHeight / totalContentHeight));
            float thumbY = scrollTrackY + (_scActionMapFilterScrollOffset / _scActionMapFilterMaxScroll) * (scrollTrackHeight - thumbHeight);

            using var thumbPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.TextDim.WithAlpha(150), IsAntialias = true };
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(scrollTrackX, thumbY, scrollTrackX + scrollbarWidth, thumbY + thumbHeight), 2f), thumbPaint);
        }
    }

    private static string FormatActionMapName(string categoryName)
    {
        // _scActionMaps already contains formatted category names, just return as-is
        return categoryName;
    }

    #endregion

    #region SC Bindings Click Handling

    private void HandleBindingsTabClick(SKPoint point)
    {
        // Scrollbar click handling - start dragging
        if (_scVScrollbarBounds.Contains(point.X, point.Y))
        {
            _scIsDraggingVScroll = true;
            _scScrollDragStartY = point.Y;
            _scScrollDragStartOffset = _scBindingsScrollOffset;
            return;
        }

        if (_scHScrollbarBounds.Contains(point.X, point.Y))
        {
            _scIsDraggingHScroll = true;
            _scScrollDragStartX = point.X;
            _scScrollDragStartOffset = _scGridHorizontalScroll;
            return;
        }

        // Column header click - toggle column highlight
        if (_scColumnHeadersBounds.Contains(point.X, point.Y))
        {
            int clickedCol = GetClickedColumnIndex(point.X);
            if (clickedCol >= 0)
            {
                // Toggle highlight: if same column clicked, unhighlight; otherwise highlight new column
                _scHighlightedColumn = (_scHighlightedColumn == clickedCol) ? -1 : clickedCol;
                return;
            }
        }

        // SC Installation dropdown handling (close when clicking outside)
        if (_scInstallationDropdownOpen)
        {
            if (_scInstallationDropdownBounds.Contains(point))
            {
                // Click on dropdown item
                if (_hoveredSCInstallation >= 0 && _hoveredSCInstallation < _scInstallations.Count
                    && _hoveredSCInstallation != _selectedSCInstallation)
                {
                    if (_scProfileDirty)
                    {
                        using var dialog = new FUIConfirmDialog(
                            "Unsaved Changes",
                            $"Profile '{_scExportProfile.ProfileName}' has an unsaved name change.\n\nSwitch installation and discard changes?",
                            "Discard & Switch", "Cancel");
                        if (dialog.ShowDialog(_ctx.OwnerForm) != DialogResult.Yes)
                        {
                            _scInstallationDropdownOpen = false;
                            return;
                        }
                    }

                    _selectedSCInstallation = _hoveredSCInstallation;
                    _scProfileDirty = false;
                    LoadSCSchema(_scInstallations[_selectedSCInstallation], autoLoadProfileForEnvironment: true);
                    _ctx.AppSettings.PreferredSCEnvironment = _scInstallations[_selectedSCInstallation].Environment;
                }
                _scInstallationDropdownOpen = false;
                return;
            }
            else
            {
                // Click outside - close dropdown
                _scInstallationDropdownOpen = false;
                return;
            }
        }

        // Action map filter dropdown handling
        if (_scActionMapFilterDropdownOpen)
        {
            if (_scActionMapFilterDropdownBounds.Contains(point))
            {
                // Calculate which item was clicked, accounting for scroll offset
                float itemHeight = 24f;
                float relativeY = point.Y - _scActionMapFilterDropdownBounds.Top - 2 + _scActionMapFilterScrollOffset;
                int clickedIndex = (int)(relativeY / itemHeight) - 1; // -1 because first item is "All Categories"

                if (clickedIndex < 0)
                {
                    // "All Categories" clicked
                    _scActionMapFilter = "";
                }
                else if (clickedIndex < _scActionMaps.Count)
                {
                    _scActionMapFilter = _scActionMaps[clickedIndex];
                }
                RefreshFilteredActions();
                _scActionMapFilterDropdownOpen = false;
                _scActionMapFilterScrollOffset = 0; // Reset scroll when closing
                return;
            }
            else
            {
                _scActionMapFilterDropdownOpen = false;
                _scActionMapFilterScrollOffset = 0; // Reset scroll when closing
                return;
            }
        }

        // SC Export profile dropdown handling
        if (_scProfileDropdownOpen)
        {
            if (_scProfileDropdownListBounds.Contains(point))
            {
                // Delete button takes priority over row click
                if (!string.IsNullOrEmpty(_scDropdownDeleteProfileName) &&
                    _scDropdownDeleteButtonBounds.Contains(point))
                {
                    var nameToDelete = _scDropdownDeleteProfileName;
                    var confirmed = FUIMessageBox.ShowQuestion(_ctx.OwnerForm,
                        $"Delete control profile '{nameToDelete}'?",
                        "Delete Profile");
                    if (confirmed)
                    {
                        _scExportProfileService?.DeleteProfile(nameToDelete);
                        RefreshSCExportProfiles();
                        _ctx.InvalidateCanvas();
                    }
                    _scProfileDropdownOpen = false;
                    return;
                }

                // Click on dropdown item
                if (_scHoveredProfileIndex >= 0)
                {
                    // SC files use offset: _scExportProfiles.Count + 1000 + i
                    int scFileIndexOffset = _scExportProfiles.Count + 1000;
                    if (_scHoveredProfileIndex >= scFileIndexOffset)
                    {
                        // SC mapping file - import it
                        int scFileIndex = _scHoveredProfileIndex - scFileIndexOffset;
                        if (scFileIndex >= 0 && scFileIndex < _scAvailableProfiles.Count)
                        {
                            ImportSCProfile(_scAvailableProfiles[scFileIndex]);
                        }
                    }
                    else if (_scHoveredProfileIndex < _scExportProfiles.Count)
                    {
                        // Asteriq profile - load it
                        LoadSCExportProfile(_scExportProfiles[_scHoveredProfileIndex].ProfileName);
                    }
                }
                _scProfileDropdownOpen = false;
                return;
            }
            else
            {
                // Click outside - close dropdown
                _scProfileDropdownOpen = false;
                // Don't return - allow other clicks to process
            }
        }

        // SC Import dropdown handling
        if (_scImportDropdownOpen)
        {
            if (_scImportDropdownBounds.Contains(point))
            {
                // Calculate which item was clicked based on Y position
                float itemHeight = 28f;
                float relativeY = point.Y - _scImportDropdownBounds.Top - 2;
                int clickedIndex = (int)(relativeY / itemHeight);

                if (clickedIndex >= 0 && clickedIndex < _scAvailableProfiles.Count)
                {
                    ImportSCProfile(_scAvailableProfiles[clickedIndex]);
                }
                _scImportDropdownOpen = false;
                return;
            }
            else
            {
                // Click outside - close dropdown
                _scImportDropdownOpen = false;
                // Don't return - allow other clicks to process
            }
        }

        // SC Installation selector click (toggle dropdown)
        if (_scInstallationSelectorBounds.Contains(point) && _scInstallations.Count > 0)
        {
            _scInstallationDropdownOpen = !_scInstallationDropdownOpen;
            _scActionMapFilterDropdownOpen = false;
            _scProfileDropdownOpen = false;
            return;
        }

        // Action map filter selector click
        if (_scActionMapFilterBounds.Contains(point) && _scActionMaps.Count > 0)
        {
            _scActionMapFilterDropdownOpen = !_scActionMapFilterDropdownOpen;
            _scInstallationDropdownOpen = false;
            _scProfileDropdownOpen = false;
            _scSearchBoxFocused = false;
            return;
        }

        // SC Export profile dropdown toggle click
        if (_scProfileDropdownBounds.Contains(point))
        {
            _scProfileDropdownOpen = !_scProfileDropdownOpen;
            _scInstallationDropdownOpen = false;
            _scActionMapFilterDropdownOpen = false;
            _scSearchBoxFocused = false;
            return;
        }

        // SC Export profile management buttons
        if (_scSaveProfileButtonBounds.Contains(point))
        {
            SaveSCExportProfile();
            return;
        }

        if (_scNewProfileButtonBounds.Contains(point))
        {
            CreateNewSCExportProfile();
            return;
        }

        if (_scDeleteProfileButtonBounds.Contains(point) && _scExportProfiles.Count > 0)
        {
            DeleteSCExportProfile();
            return;
        }

        // Search box click
        if (_scSearchBoxBounds.Contains(point))
        {
            // Check if clicking the X to clear
            if (!string.IsNullOrEmpty(_scSearchText) && point.X > _scSearchBoxBounds.Right - 24)
            {
                _scSearchText = "";
                RefreshFilteredActions();
            }
            else
            {
                _scSearchBoxFocused = true;
            }
            _scInstallationDropdownOpen = false;
            _scActionMapFilterDropdownOpen = false;
            _scProfileDropdownOpen = false;
            return;
        }
        else
        {
            // Click outside search box unfocuses it
            _scSearchBoxFocused = false;
        }

        // Show Bound Only checkbox click
        if (_scShowBoundOnlyBounds.Contains(point))
        {
            _scShowBoundOnly = !_scShowBoundOnly;
            RefreshFilteredActions();
            return;
        }

        // Refresh button
        if (_scRefreshButtonBounds.Contains(point))
        {
            RefreshSCInstallations();
            StartSchemaLoad();
            _scExportStatus = "Installations refreshed";
            _scExportStatusTime = DateTime.Now;
            return;
        }

        // Export button
        if (_scExportButtonBounds.Contains(point))
        {
            ExportToSC();
            return;
        }

        // Import button - toggle dropdown
        if (_scImportButtonBounds.Contains(point) && _scAvailableProfiles.Count > 0)
        {
            _scImportDropdownOpen = !_scImportDropdownOpen;
            _scInstallationDropdownOpen = false;
            _scActionMapFilterDropdownOpen = false;
            _scProfileDropdownOpen = false;
            return;
        }

        // Export filename box click
        if (_scExportFilenameBoxBounds.Contains(point))
        {
            _scExportFilenameBoxFocused = true;
            _scSearchBoxFocused = false;
            _scInstallationDropdownOpen = false;
            _scActionMapFilterDropdownOpen = false;
            _scProfileDropdownOpen = false;
            _scImportDropdownOpen = false;
            return;
        }
        else if (_scExportFilenameBoxFocused && !_scExportFilenameBoxBounds.Contains(point))
        {
            _scExportFilenameBoxFocused = false;
        }

        // Clear All bindings button
        if (_scClearAllButtonBounds.Contains(point) && _scExportProfile.Bindings.Count > 0)
        {
            ClearAllBindings();
            return;
        }

        // Reset Defaults button
        if (_scResetDefaultsButtonBounds.Contains(point))
        {
            ResetToDefaults();
            return;
        }

        // Assign input button
        if (_scAssignInputButtonBounds.Contains(point) && _scSelectedActionIndex >= 0)
        {
            AssignSCBinding();
            return;
        }

        // Clear binding button
        if (_scClearBindingButtonBounds.Contains(point) && _scSelectedActionIndex >= 0 && _scFilteredActions is not null)
        {
            var selectedAction = _scFilteredActions[_scSelectedActionIndex];
            _scExportProfile.RemoveBinding(selectedAction.ActionMap, selectedAction.ActionName);
            _scExportProfileService?.SaveProfile(_scExportProfile);
            UpdateConflictingBindings();
            _scAssigningInput = false;
            return;
        }

        // Profile name click (could open edit dialog in future)
        if (_scProfileNameBounds.Contains(point))
        {
            EditSCProfileName();
            return;
        }

        // Category header clicks (expand/collapse)
        foreach (var kvp in _scCategoryHeaderBounds)
        {
            if (kvp.Value.Contains(point))
            {
                if (_scCollapsedCategories.Contains(kvp.Key))
                {
                    _scCollapsedCategories.Remove(kvp.Key);
                }
                else
                {
                    _scCollapsedCategories.Add(kvp.Key);
                }
                return;
            }
        }

        // Action row and cell clicks
        if (_scBindingsListBounds.Contains(point) && _scFilteredActions is not null)
        {
            // Find which row was clicked accounting for scroll offset and collapsed categories
            float rowHeight = 28f;
            float rowGap = 2f;
            float categoryHeaderHeight = 28f;
            float relativeY = point.Y - _scBindingsListBounds.Top + _scBindingsScrollOffset;

            string? lastCategoryName = null;
            float currentY = 0;

            for (int i = 0; i < _scFilteredActions.Count; i++)
            {
                var action = _scFilteredActions[i];
                string categoryName = SCCategoryMapper.GetCategoryNameForAction(action.ActionMap, action.ActionName);

                // Account for category header
                if (categoryName != lastCategoryName)
                {
                    lastCategoryName = categoryName;
                    currentY += categoryHeaderHeight;

                    // If category is collapsed, skip all its actions
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
                    _scSelectedActionIndex = i;

                    // Check if click was in a device column cell
                    int clickedCol = GetClickedColumnIndex(point.X);
                    if (clickedCol >= 0 && _scGridColumns is not null && clickedCol < _scGridColumns.Count)
                    {
                        // Cell was clicked - enter listening mode
                        HandleCellClick(i, clickedCol);
                    }
                    else
                    {
                        // Action name area clicked - just select the row
                        _scSelectedCell = (-1, -1);
                        _scIsListeningForInput = false;
                    }
                    return;
                }

                currentY += rowHeight + rowGap;
            }

            // Click was in list area but not on a row - clear selection
            _scSelectedCell = (-1, -1);
            _scIsListeningForInput = false;
        }
    }

    /// <summary>
    /// Gets the column index at the given X coordinate, or -1 if not in a device column
    /// </summary>
    private int GetClickedColumnIndex(float x)
    {
        if (_scGridColumns is null || x < _scDeviceColsStart || x > _scDeviceColsStart + _scVisibleDeviceWidth)
            return -1;

        float relativeX = x - _scDeviceColsStart + _scGridHorizontalScroll;

        // Walk through columns to find which one contains this X
        float cumX = 0f;
        for (int c = 0; c < _scGridColumns.Count; c++)
        {
            float colW = _scGridDeviceColWidths.TryGetValue(_scGridColumns[c].Id, out var w) ? w : _scGridDeviceColMinWidth;
            if (relativeX >= cumX && relativeX < cumX + colW)
                return c;
            cumX += colW;
        }

        return -1;
    }

    /// <summary>
    /// Handles a click on a binding cell - selects on single click, activates listening on double-click
    /// </summary>
    private void HandleCellClick(int actionIndex, int colIndex)
    {
        if (_scGridColumns is null || colIndex < 0 || colIndex >= _scGridColumns.Count)
            return;

        var col = _scGridColumns[colIndex];

        // If already listening, cancel
        if (_scIsListeningForInput)
        {
            _scIsListeningForInput = false;
            _scListeningColumn = null;
        }

        // Check for double-click on the same cell (within 400ms)
        bool isDoubleClick = _scSelectedCell == (actionIndex, colIndex) &&
                            (DateTime.Now - _scLastCellClickTime).TotalMilliseconds < 400;

        if (isDoubleClick)
        {
            // Double-click: enter listening mode
            _scIsListeningForInput = true;
            _scListeningStartTime = DateTime.Now;
            _scListeningColumn = col;

            // Clear stale key presses from search box input before detecting
            if (col.IsKeyboard)
            {
                ClearStaleKeyPresses();
            }

            System.Diagnostics.Debug.WriteLine($"[SCBindings] Started listening for input on cell ({actionIndex}, {colIndex}) - {col.Header}");
        }
        else
        {
            // Single click: just select the cell
            _scSelectedCell = (actionIndex, colIndex);
            _scLastCellClickTime = DateTime.Now;
            System.Diagnostics.Debug.WriteLine($"[SCBindings] Selected cell ({actionIndex}, {colIndex}) - {col.Header}");
        }
    }

    /// <summary>
    /// Handles right-click on a binding cell - clears the binding
    /// </summary>
    private void HandleCellRightClick(int actionIndex, int colIndex)
    {
        if (_scGridColumns is null || colIndex < 0 || colIndex >= _scGridColumns.Count)
            return;
        if (_scFilteredActions is null || actionIndex < 0 || actionIndex >= _scFilteredActions.Count)
            return;

        var col = _scGridColumns[colIndex];
        var action = _scFilteredActions[actionIndex];

        // Cancel listening if active
        if (_scIsListeningForInput)
        {
            CancelSCInputListening();
        }

        // Clear binding for this action on this column's device
        if (col.IsJoystick)
        {
            var userBinding = _scExportProfile.GetBinding(action.ActionMap, action.ActionName, SCDeviceType.Joystick);
            if (userBinding is not null && _scExportProfile.GetSCInstance(userBinding.VJoyDevice) == col.SCInstance)
            {
                _scExportProfile.RemoveBinding(action.ActionMap, action.ActionName, SCDeviceType.Joystick);
                _scExportProfileService?.SaveProfile(_scExportProfile);
                UpdateConflictingBindings();
                System.Diagnostics.Debug.WriteLine($"[SCBindings] Cleared JS binding for {action.ActionName} on {col.Header}");
            }
        }
        else if (col.Header == "KB")
        {
            // Clear user keyboard binding
            var userBinding = _scExportProfile.GetBinding(action.ActionMap, action.ActionName, SCDeviceType.Keyboard);
            if (userBinding is not null)
            {
                _scExportProfile.RemoveBinding(action.ActionMap, action.ActionName, SCDeviceType.Keyboard);
                _scExportProfileService?.SaveProfile(_scExportProfile);
                System.Diagnostics.Debug.WriteLine($"[SCBindings] Cleared KB binding for {action.ActionName}");
            }
        }
        else if (col.Header == "Mouse")
        {
            // Clear user mouse binding
            var userBinding = _scExportProfile.GetBinding(action.ActionMap, action.ActionName, SCDeviceType.Mouse);
            if (userBinding is not null)
            {
                _scExportProfile.RemoveBinding(action.ActionMap, action.ActionName, SCDeviceType.Mouse);
                _scExportProfileService?.SaveProfile(_scExportProfile);
                System.Diagnostics.Debug.WriteLine($"[SCBindings] Cleared Mouse binding for {action.ActionName}");
            }
        }
    }

    /// <summary>
    /// Handles right-click on the bindings tab - finds the cell and clears its binding
    /// </summary>
    private void HandleBindingsTabRightClick(SKPoint point)
    {
        // Check if click is in the bindings list area
        if (!_scBindingsListBounds.Contains(point) || _scFilteredActions is null)
            return;

        // Find which row was clicked accounting for scroll offset and collapsed categories
        float rowHeight = 28f;  // Updated row height
        float rowGap = 2f;
        float categoryHeaderHeight = 28f;
        float relativeY = point.Y - _scBindingsListBounds.Top + _scBindingsScrollOffset;

        string? lastCategoryName = null;
        float currentY = 0;

        for (int i = 0; i < _scFilteredActions.Count; i++)
        {
            var action = _scFilteredActions[i];
            string categoryName = SCCategoryMapper.GetCategoryNameForAction(action.ActionMap, action.ActionName);

            // Account for category header
            if (categoryName != lastCategoryName)
            {
                lastCategoryName = categoryName;
                currentY += categoryHeaderHeight;

                // If category is collapsed, skip all its actions
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
                // Check if right-click was in a device column cell
                int clickedCol = GetClickedColumnIndex(point.X);
                if (clickedCol >= 0 && _scGridColumns is not null && clickedCol < _scGridColumns.Count)
                {
                    HandleCellRightClick(i, clickedCol);
                }
                return;
            }

            currentY += rowHeight + rowGap;
        }
    }

    /// <summary>
    /// Called from the render timer to check for input during listening mode
    /// </summary>
    private void CheckSCBindingInput()
    {
        if (!_scIsListeningForInput || _scListeningColumn is null || _scFilteredActions is null)
            return;

        // Check for timeout
        if ((DateTime.Now - _scListeningStartTime).TotalMilliseconds > SCListeningTimeoutMs)
        {
            CancelSCInputListening();
            return;
        }

        // Check for Escape to cancel
        if (IsKeyHeld(0x1B)) // VK_ESCAPE
        {
            CancelSCInputListening();
            return;
        }

        var (actionIndex, colIndex) = _scSelectedCell;
        if (actionIndex < 0 || actionIndex >= _scFilteredActions.Count)
            return;

        var action = _scFilteredActions[actionIndex];
        var col = _scListeningColumn;

        // Detect input based on column type
        if (col.IsKeyboard)
        {
            var detectedKey = DetectKeyboardInput();
            if (detectedKey is not null)
            {
                AssignKeyboardBinding(action, detectedKey.Value.key, detectedKey.Value.modifiers);
                CancelSCInputListening();
            }
        }
        else if (col.IsMouse)
        {
            var detectedMouse = DetectMouseInput();
            if (detectedMouse is not null)
            {
                AssignMouseBinding(action, detectedMouse);
                CancelSCInputListening();
            }
        }
        else if (col.IsJoystick)
        {
            // Joystick input detection will use physical→vJoy mapping lookup
            var detectedJoystick = DetectJoystickInput(col);
            if (detectedJoystick is not null)
            {
                AssignJoystickBinding(action, col, detectedJoystick);
                CancelSCInputListening();
            }
        }
    }

    private void CancelSCInputListening()
    {
        _scIsListeningForInput = false;
        _scListeningColumn = null;
        ResetJoystickDetectionState(); // Reset all joystick detection state
        System.Diagnostics.Debug.WriteLine("[SCBindings] Input listening cancelled");
    }

    /// <summary>
    /// Detects keyboard input, returning the key and modifiers if a non-modifier key is pressed
    /// </summary>
    private (Keys key, List<string> modifiers)? DetectKeyboardInput()
    {
        // Collect held modifiers
        var modifiers = new List<string>();
        if (IsKeyHeld(0xA0) || IsKeyHeld(0xA1)) // VK_LSHIFT, VK_RSHIFT
        {
            modifiers.Add(IsKeyHeld(0xA1) ? "rshift" : "lshift");
        }
        if (IsKeyHeld(0xA2) || IsKeyHeld(0xA3)) // VK_LCONTROL, VK_RCONTROL
        {
            modifiers.Add(IsKeyHeld(0xA3) ? "rctrl" : "lctrl");
        }
        if (IsKeyHeld(0xA4) || IsKeyHeld(0xA5)) // VK_LMENU, VK_RMENU (Alt)
        {
            modifiers.Add(IsKeyHeld(0xA5) ? "ralt" : "lalt");
        }

        // Check for regular keys (A-Z)
        for (int vk = 0x41; vk <= 0x5A; vk++) // A-Z
        {
            if (IsKeyPressed(vk))
            {
                return ((Keys)vk, modifiers);
            }
        }

        // Check number keys (0-9)
        for (int vk = 0x30; vk <= 0x39; vk++)
        {
            if (IsKeyPressed(vk))
            {
                return ((Keys)vk, modifiers);
            }
        }

        // Check function keys (F1-F12)
        for (int vk = 0x70; vk <= 0x7B; vk++) // VK_F1 - VK_F12
        {
            if (IsKeyPressed(vk))
            {
                return ((Keys)vk, modifiers);
            }
        }

        // Check common keys
        int[] commonKeys = { 0x20, 0x0D, 0x08, 0x09, 0x2E, 0x2D, 0x24, 0x23, 0x21, 0x22, // Space, Enter, Backspace, Tab, Delete, Insert, Home, End, PgUp, PgDn
                            0x25, 0x26, 0x27, 0x28, // Arrow keys
                            0xC0, 0xBD, 0xBB, 0xDB, 0xDD, 0xDC, 0xBA, 0xDE, 0xBC, 0xBE, 0xBF }; // Symbol keys

        foreach (var vk in commonKeys)
        {
            if (IsKeyPressed(vk))
            {
                return ((Keys)vk, modifiers);
            }
        }

        return null;
    }

    /// <summary>
    /// Detects mouse button input
    /// </summary>
    private string? DetectMouseInput()
    {
        // Check mouse buttons (excluding primary which is used for clicking)
        if (IsKeyPressed(0x02)) return "mouse2"; // VK_RBUTTON
        if (IsKeyPressed(0x04)) return "mouse3"; // VK_MBUTTON
        if (IsKeyPressed(0x05)) return "mouse4"; // VK_XBUTTON1
        if (IsKeyPressed(0x06)) return "mouse5"; // VK_XBUTTON2

        // Mouse wheel detection would need WM_MOUSEWHEEL messages which we don't have here
        // For now, mouse wheel bindings need to be entered differently

        return null;
    }

    // State tracking for SDL2-based joystick input detection
    private Dictionary<Guid, float[]>? _scAxisBaseline;    // Baseline axis values
    private Dictionary<Guid, bool[]>? _scButtonBaseline;   // Baseline button values
    private Dictionary<Guid, int[]>? _scHatBaseline;       // Baseline hat values
    private int _scBaselineFrames = 0;                     // Frames since baseline capture

    /// <summary>
    /// Detects joystick input using SDL2 with axis TYPE info for proper slider detection.
    ///
    /// Design: SC Bindings detection accepts input from ANY physical joystick and assigns it
    /// to the clicked column's vJoy device. Uses AxisInfo to properly identify sliders.
    /// </summary>
    private string? DetectJoystickInput(SCGridColumn col)
    {
        const float AxisThreshold = 0.15f; // 15% threshold like SCVirtStick/Gremlin

        // Initialize on first call - capture baseline from SDL2
        if (_scAxisBaseline is null)
        {
            _scAxisBaseline = new Dictionary<Guid, float[]>();
            _scButtonBaseline = new Dictionary<Guid, bool[]>();
            _scHatBaseline = new Dictionary<Guid, int[]>();
            _scBaselineFrames = 0;

            // Capture baseline from current SDL2 state
            for (int idx = 0; idx < _ctx.Devices.Count; idx++)
            {
                var device = _ctx.Devices[idx];
                if (device.IsVirtual || !device.IsConnected) continue;

                var state = _ctx.InputService.GetDeviceState(idx);
                if (state is not null)
                {
                    _scAxisBaseline[device.InstanceGuid] = (float[])state.Axes.Clone();
                    _scButtonBaseline[device.InstanceGuid] = (bool[])state.Buttons.Clone();
                    _scHatBaseline[device.InstanceGuid] = (int[])state.Hats.Clone();
                }
            }

            System.Diagnostics.Debug.WriteLine($"[SCBindings] Initialized SDL2 input detection for {_scAxisBaseline.Count} devices");
            return null; // First frame - just capture baseline
        }

        _scBaselineFrames++;

        // Skip first few frames to let baseline stabilize
        if (_scBaselineFrames < 3)
            return null;

        // Check each physical device for input changes
        for (int idx = 0; idx < _ctx.Devices.Count; idx++)
        {
            var device = _ctx.Devices[idx];
            if (device.IsVirtual || !device.IsConnected) continue;

            var state = _ctx.InputService.GetDeviceState(idx);
            if (state is null) continue;

            _scAxisBaseline.TryGetValue(device.InstanceGuid, out var baselineAxes);
            _scButtonBaseline!.TryGetValue(device.InstanceGuid, out var baselineButtons);
            _scHatBaseline!.TryGetValue(device.InstanceGuid, out var baselineHats);

            // Check for button presses - immediately return on first press
            for (int i = 0; i < state.Buttons.Length; i++)
            {
                bool wasPressed = baselineButtons is not null && i < baselineButtons.Length && baselineButtons[i];
                bool isPressed = state.Buttons[i];

                if (isPressed && !wasPressed)
                {
                    System.Diagnostics.Debug.WriteLine($"[SCBindings] SDL2 detected button {i + 1} on {device.Name}");
                    ResetJoystickDetectionState();
                    return $"button{i + 1}";
                }
            }

            // Check for axis movement - look up vJoy output axis from mapping profile
            for (int i = 0; i < state.Axes.Length; i++)
            {
                float baselineValue = baselineAxes is not null && i < baselineAxes.Length ? baselineAxes[i] : 0f;
                float currValue = state.Axes[i];
                float deflection = Math.Abs(currValue - baselineValue);

                if (deflection > AxisThreshold)
                {
                    // Look up the vJoy output axis from the mapping profile
                    // This ensures SC Bindings exports match where the data actually goes
                    string axisName = GetVJoyAxisNameFromMapping(device, i, col);
                    System.Diagnostics.Debug.WriteLine($"[SCBindings] SDL2 detected axis {i} -> vJoy {axisName} on {device.Name}, deflection: {deflection:F2}");
                    ResetJoystickDetectionState();
                    return axisName;
                }
            }

            // Check for hat movement
            for (int i = 0; i < state.Hats.Length; i++)
            {
                int baselineHat = baselineHats is not null && i < baselineHats.Length ? baselineHats[i] : -1;
                int currHat = state.Hats[i];

                // Hat changed from centered to a direction
                if (currHat >= 0 && baselineHat < 0)
                {
                    string hatDir = GetHatDirection(HatAngleToDiscrete(currHat));
                    System.Diagnostics.Debug.WriteLine($"[SCBindings] SDL2 detected hat {i + 1} {hatDir} on {device.Name}");
                    ResetJoystickDetectionState();
                    return $"hat{i + 1}_{hatDir}";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Resets joystick detection state
    /// </summary>
    private void ResetJoystickDetectionState()
    {
        _scAxisBaseline = null;
        _scButtonBaseline = null;
        _scHatBaseline = null;
        _scBaselineFrames = 0;
    }

    /// <summary>
    /// Convert hat angle (0-359 degrees) to discrete direction (0=up, 1=right, 2=down, 3=left)
    /// </summary>
    private static int HatAngleToDiscrete(int angle)
    {
        if (angle < 0) return -1; // Centered
        // Normalize to 0-359
        angle = ((angle % 360) + 360) % 360;
        // 315-44 = up, 45-134 = right, 135-224 = down, 225-314 = left
        if (angle >= 315 || angle < 45) return 0;   // Up
        if (angle >= 45 && angle < 135) return 1;   // Right
        if (angle >= 135 && angle < 225) return 2;  // Down
        return 3; // Left
    }

    /// <summary>
    /// Converts axis binding name back to axis index
    /// </summary>
    private static int GetAxisIndexFromBinding(string binding)
    {
        return binding.ToLowerInvariant() switch
        {
            "x" => 0,
            "y" => 1,
            "z" => 2,
            "rx" => 3,
            "ry" => 4,
            "rz" => 5,
            "slider1" => 6,
            "slider2" => 7,
            _ when binding.StartsWith("axis") && int.TryParse(binding.Substring(4), out int idx) => idx,
            _ => -1
        };
    }

    /// <summary>
    /// Gets the vJoy output axis name from the mapping profile.
    /// This ensures SC Bindings export matches where the data actually goes in vJoy.
    /// </summary>
    private string GetVJoyAxisNameFromMapping(PhysicalDeviceInfo device, int physicalAxisIndex, SCGridColumn col)
    {
        // Look up the mapping in the active profile
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is not null)
        {
            // Try to find a mapping for this physical input using GUID
            var deviceId = device.InstanceGuid.ToString();
            var output = profile.GetVJoyOutputForPhysicalInput(deviceId, InputType.Axis, physicalAxisIndex);

            if (output is not null && output.Type == OutputType.VJoyAxis)
            {
                // Found a mapping - return the vJoy output axis name
                string axisName = VJoyAxisIndexToSCName(output.Index);
                System.Diagnostics.Debug.WriteLine($"[SCBindings] Found mapping: {device.Name} axis {physicalAxisIndex} -> vJoy axis {output.Index} ({axisName})");
                return axisName;
            }
        }

        // No mapping found - fall back to sequential index-based naming
        // This assumes physical axis N maps to vJoy axis N
        System.Diagnostics.Debug.WriteLine($"[SCBindings] No mapping found for {device.Name} axis {physicalAxisIndex}, using index-based fallback");
        return VJoyAxisIndexToSCName(physicalAxisIndex);
    }

    /// <summary>
    /// Convert vJoy axis index to SC axis name.
    /// vJoy: 0=X, 1=Y, 2=Z, 3=RX, 4=RY, 5=RZ, 6=Slider1, 7=Slider2
    /// SC uses: x, y, z, rx, ry, rz, slider1, slider2
    /// </summary>
    private static string VJoyAxisIndexToSCName(int vjoyAxisIndex)
    {
        return vjoyAxisIndex switch
        {
            0 => "x",
            1 => "y",
            2 => "z",
            3 => "rx",
            4 => "ry",
            5 => "rz",
            6 => "slider1",
            7 => "slider2",
            _ => $"axis{vjoyAxisIndex}"
        };
    }

    /// <summary>
    /// Gets the SC axis name using HID axis type info from the device.
    /// This properly identifies sliders by their HID usage ID rather than SDL index.
    /// NOTE: This is kept for reference but GetVJoyAxisNameFromMapping should be used
    /// for SC Bindings to ensure export matches where data actually goes.
    /// </summary>
    private static string GetSCAxisNameFromDevice(int axisIndex, PhysicalDeviceInfo? device)
    {
        // Use HID axis type info if available (this properly detects sliders)
        if (device is not null && device.AxisInfos.Count > 0)
        {
            var axisInfo = device.AxisInfos.FirstOrDefault(a => a.Index == axisIndex);
            if (axisInfo is not null)
            {
                return axisInfo.Type switch
                {
                    AxisType.X => "x",
                    AxisType.Y => "y",
                    AxisType.Z => "z",
                    AxisType.RX => "rx",
                    AxisType.RY => "ry",
                    AxisType.RZ => "rz",
                    AxisType.Slider => GetSliderName(axisIndex, device),
                    _ => $"axis{axisIndex}"
                };
            }
        }

        // Fallback to index-based mapping if no HID info
        return axisIndex switch
        {
            0 => "x",
            1 => "y",
            2 => "z",
            3 => "rx",
            4 => "ry",
            5 => "rz",
            6 => "slider1",
            7 => "slider2",
            _ => $"axis{axisIndex}"
        };
    }

    /// <summary>
    /// Gets the slider name (slider1 or slider2) based on which slider this is on the device
    /// </summary>
    private static string GetSliderName(int axisIndex, PhysicalDeviceInfo device)
    {
        // Count how many sliders come before this one
        int sliderNumber = 1;
        foreach (var axis in device.AxisInfos.OrderBy(a => a.Index))
        {
            if (axis.Index == axisIndex)
                break;
            if (axis.Type == AxisType.Slider)
                sliderNumber++;
        }
        return $"slider{sliderNumber}";
    }

    private static string GetSCAxisName(int axisIndex)
    {
        return axisIndex switch
        {
            0 => "x",
            1 => "y",
            2 => "z",
            3 => "rx",
            4 => "ry",
            5 => "rz",
            6 => "slider1",
            7 => "slider2",
            _ => $"axis{axisIndex}"
        };
    }

    private static string GetHatDirection(int dir)
    {
        return dir switch
        {
            0 => "up",
            1 => "right",
            2 => "down",
            3 => "left",
            _ => "up"
        };
    }

    /// <summary>
    /// Infers the SC input type from the input name string.
    /// Matches SCVirtStick's InferJoystickInputType logic.
    /// </summary>
    private static SCInputType InferInputTypeFromName(string inputName)
    {
        if (string.IsNullOrEmpty(inputName))
            return SCInputType.Button;

        var lower = inputName.ToLowerInvariant();

        // Button inputs (check first since it's most common)
        if (lower.StartsWith("button"))
            return SCInputType.Button;

        // Hat/POV inputs
        if (lower.StartsWith("hat"))
            return SCInputType.Hat;

        // Known axis names (matches SCVirtStick exactly)
        if (lower is "x" or "y" or "z" or "rx" or "ry" or "rz" or
            "slider1" or "slider2" or "throttle" or "rotz" or "rotx" or "roty")
            return SCInputType.Axis;

        // Slider axes (any slider*)
        if (lower.StartsWith("slider"))
            return SCInputType.Axis;

        // Fallback axis names (axis0, axis1, etc.)
        if (lower.StartsWith("axis"))
            return SCInputType.Axis;

        // Default to button (same as SCVirtStick)
        return SCInputType.Button;
    }

    private void AssignKeyboardBinding(SCAction action, Keys key, List<string> modifiers)
    {
        // Store as SC-format keyboard binding
        string inputName = KeyToSCInput(key);
        System.Diagnostics.Debug.WriteLine($"[SCBindings] Assigning KB binding: {action.ActionName} = {string.Join("+", modifiers)}+{inputName}");

        // Store in the export profile (persisted and clearable)
        var binding = new SCActionBinding
        {
            ActionMap = action.ActionMap,
            ActionName = action.ActionName,
            DeviceType = SCDeviceType.Keyboard,
            InputName = inputName,
            InputType = SCInputType.Button,
            Modifiers = modifiers
        };

        _scExportProfile.SetBinding(action.ActionMap, action.ActionName, binding);
        _scExportProfileService?.SaveProfile(_scExportProfile);

        _scExportStatus = $"Bound {action.ActionName} to kb1_{inputName}";
        _scExportStatusTime = DateTime.Now;
    }

    private void AssignMouseBinding(SCAction action, string inputName)
    {
        System.Diagnostics.Debug.WriteLine($"[SCBindings] Assigning Mouse binding: {action.ActionName} = {inputName}");

        // Store in the export profile (persisted and clearable)
        var binding = new SCActionBinding
        {
            ActionMap = action.ActionMap,
            ActionName = action.ActionName,
            DeviceType = SCDeviceType.Mouse,
            InputName = inputName,
            InputType = SCInputType.Button
        };

        _scExportProfile.SetBinding(action.ActionMap, action.ActionName, binding);
        _scExportProfileService?.SaveProfile(_scExportProfile);

        _scExportStatus = $"Bound {action.ActionName} to mo1_{inputName}";
        _scExportStatusTime = DateTime.Now;
    }

    private void AssignJoystickBinding(SCAction action, SCGridColumn col, string inputName)
    {
        System.Diagnostics.Debug.WriteLine($"[SCBindings] Assigning JS binding: {action.ActionName} = js{col.SCInstance}_{inputName}");

        // Check for conflicting bindings (same input already used by another action)
        var conflicts = _scExportProfile.GetConflictingBindings(
            col.VJoyDeviceId,
            inputName,
            action.ActionMap,
            action.ActionName);

        if (conflicts.Count > 0)
        {
            // Show conflict dialog
            string actionDisplayName = SCCategoryMapper.FormatActionName(action.ActionName);
            string inputDisplayName = FormatInputName(inputName);
            string deviceName = $"JS{col.SCInstance}";

            var dialog = new BindingConflictDialog(conflicts, actionDisplayName, inputDisplayName, deviceName);
            dialog.ShowDialog(_ctx.OwnerForm);

            switch (dialog.Result)
            {
                case BindingConflictResult.Cancel:
                    // User cancelled - don't apply the binding
                    System.Diagnostics.Debug.WriteLine($"[SCBindings] Binding cancelled for {action.ActionName}");
                    return;

                case BindingConflictResult.ReplaceAll:
                    // Remove all conflicting bindings first
                    foreach (var conflict in conflicts)
                    {
                        _scExportProfile.RemoveBinding(conflict.ActionMap, conflict.ActionName);
                        System.Diagnostics.Debug.WriteLine($"[SCBindings] Removed conflicting binding: {conflict.ActionName}");
                    }
                    break;

                case BindingConflictResult.ApplyAnyway:
                    // Just apply, keep the conflicts (user accepts duplicates)
                    System.Diagnostics.Debug.WriteLine($"[SCBindings] Applying binding with {conflicts.Count} conflict(s)");
                    break;
            }
        }

        // Determine input type from input name
        var inputType = InferInputTypeFromName(inputName);

        // Set the binding (removes any existing binding for this action first)
        _scExportProfile.SetBinding(action.ActionMap, action.ActionName, new SCActionBinding
        {
            ActionMap = action.ActionMap,
            ActionName = action.ActionName,
            DeviceType = SCDeviceType.Joystick,
            VJoyDevice = col.VJoyDeviceId,
            InputName = inputName,
            InputType = inputType
        });

        // Ensure this vJoy device has an SC instance mapping (required for export)
        if (!_scExportProfile.VJoyToSCInstance.ContainsKey(col.VJoyDeviceId))
        {
            _scExportProfile.SetSCInstance(col.VJoyDeviceId, col.SCInstance);
            System.Diagnostics.Debug.WriteLine($"[SCBindings] Set vJoy{col.VJoyDeviceId} -> js{col.SCInstance} mapping");
        }

        // Save the profile and update conflict detection
        _scExportProfileService?.SaveProfile(_scExportProfile);
        UpdateConflictingBindings();

        _scExportStatus = $"Bound {action.ActionName} to js{col.SCInstance}_{inputName}";
        _scExportStatusTime = DateTime.Now;
    }

    private static string KeyToSCInput(Keys key)
    {
        return key switch
        {
            >= Keys.A and <= Keys.Z => key.ToString().ToLower(),
            >= Keys.D0 and <= Keys.D9 => ((int)key - (int)Keys.D0).ToString(),
            >= Keys.NumPad0 and <= Keys.NumPad9 => $"np_{(int)key - (int)Keys.NumPad0}",
            >= Keys.F1 and <= Keys.F12 => key.ToString().ToLower(),
            Keys.Space => "space",
            Keys.Enter => "enter",
            Keys.Escape => "escape",
            Keys.Back => "backspace",
            Keys.Tab => "tab",
            Keys.Delete => "delete",
            Keys.Insert => "insert",
            Keys.Home => "home",
            Keys.End => "end",
            Keys.PageUp => "pgup",
            Keys.PageDown => "pgdn",
            Keys.Left => "left",
            Keys.Right => "right",
            Keys.Up => "up",
            Keys.Down => "down",
            Keys.OemMinus => "minus",
            Keys.Oemplus => "equals",
            Keys.OemOpenBrackets => "lbracket",
            Keys.OemCloseBrackets => "rbracket",
            Keys.OemBackslash or Keys.Oem5 => "backslash",
            Keys.OemSemicolon or Keys.Oem1 => "semicolon",
            Keys.OemQuotes or Keys.Oem7 => "apostrophe",
            Keys.Oemcomma => "comma",
            Keys.OemPeriod => "period",
            Keys.OemQuestion or Keys.Oem2 => "slash",
            Keys.Oemtilde or Keys.Oem3 => "grave",
            _ => key.ToString().ToLower()
        };
    }

    /// <summary>
    /// Checks if a key was just pressed (transition from up to down)
    /// </summary>
    private static bool IsKeyPressed(int vk)
    {
        // GetAsyncKeyState returns high bit set if key is down
        // and low bit set if key was pressed since last call
        short state = GetAsyncKeyState(vk);
        return (state & 0x0001) != 0; // Check "was pressed" bit
    }

    /// <summary>
    /// Clears stale "was pressed" bits for all monitored keys.
    /// This prevents keys typed in search box from being detected as bindings.
    /// </summary>
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

    #endregion

    #region SC Export and Dialogs

    private void ExportToSC()
    {
        if (_scExportService is null || _scInstallations.Count == 0)
        {
            _scExportStatus = "No SC installation available";
            _scExportStatusTime = DateTime.Now;
            return;
        }

        // Only require vJoy mappings if there are joystick bindings
        var hasJoystickBindings = _scExportProfile.Bindings.Any(b => b.DeviceType == SCDeviceType.Joystick);
        if (hasJoystickBindings && _scExportProfile.VJoyToSCInstance.Count == 0)
        {
            _scExportStatus = "No vJoy mappings configured for joystick bindings";
            _scExportStatusTime = DateTime.Now;
            return;
        }

        try
        {
            var installation = _scInstallations[_selectedSCInstallation];

            // Validate profile
            var validation = _scExportService.Validate(_scExportProfile);
            if (!validation.IsValid)
            {
                _scExportStatus = $"Validation failed: {validation.Errors.FirstOrDefault()}";
                _scExportStatusTime = DateTime.Now;
                return;
            }

            // Export - use custom filename if provided, otherwise auto-generate
            string filename = string.IsNullOrEmpty(_scExportFilename)
                ? _scExportProfile.GetExportFileName()
                : $"{_scExportFilename}.xml";

            // Ensure mappings directory exists
            SCInstallationService.EnsureMappingsDirectory(installation);

            string exportPath = Path.Combine(installation.MappingsPath, filename);
            _scExportService.ExportToFile(_scExportProfile, exportPath);

            // Refresh available profiles list after export
            _scAvailableProfiles = SCInstallationService.GetExistingProfiles(installation);

            _scExportStatus = $"Success! Exported to {filename}";
            _scExportStatusTime = DateTime.Now;

            System.Diagnostics.Debug.WriteLine($"[MainForm] Exported SC profile to: {exportPath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _scExportStatus = $"Export failed: {ex.Message}";
            _scExportStatusTime = DateTime.Now;
            System.Diagnostics.Debug.WriteLine($"[MainForm] SC export failed: {ex}");
        }
    }

    private void ClearAllBindings()
    {
        using var dialog = new FUIConfirmDialog(
            "Clear All Bindings",
            $"Clear all {_scExportProfile.Bindings.Count} binding(s) from profile '{_scExportProfile.ProfileName}'?\n\nThis will remove ALL bindings.\nUse 'Reset to Defaults' to restore SC default bindings.",
            "Clear", "Cancel");

        if (dialog.ShowDialog(_ctx.OwnerForm) == DialogResult.Yes)
        {
            int count = _scExportProfile.Bindings.Count;
            _scExportProfile.ClearBindings();
            _scExportProfileService?.SaveProfile(_scExportProfile);
            UpdateConflictingBindings();

            _scExportStatus = $"Cleared {count} binding(s)";
            _scExportStatusTime = DateTime.Now;

            System.Diagnostics.Debug.WriteLine($"[MainForm] Cleared all SC bindings");
        }
    }

    private void ResetToDefaults()
    {
        using var dialog = new FUIConfirmDialog(
            "Reset to Defaults",
            "Reset all bindings to default values from\nStar Citizen's defaultProfile.xml?\n\nThis will clear your custom bindings and reload the defaults.",
            "Reset", "Cancel");

        if (dialog.ShowDialog(_ctx.OwnerForm) == DialogResult.Yes)
        {
            // Clear existing bindings
            _scExportProfile.ClearBindings();

            // Reload schema from currently selected installation
            if (_scInstallations.Count > 0 && _selectedSCInstallation < _scInstallations.Count)
            {
                LoadSCSchema(_scInstallations[_selectedSCInstallation]);
            }

            // Apply default bindings from the loaded schema
            ApplyDefaultBindingsToProfile();

            UpdateConflictingBindings();

            _scExportStatus = "Reset to defaults";
            _scExportStatusTime = DateTime.Now;

            System.Diagnostics.Debug.WriteLine($"[MainForm] Reset SC bindings to defaults");
        }
    }

    private void EditSCProfileName()
    {
        var name = FUIInputDialog.Show(_ctx.OwnerForm, "Control Profile Name", "Profile Name:",
            _scExportProfile.ProfileName);
        if (name is not null)
        {
            _scExportProfile.ProfileName = name;
            _scProfileDirty = true;
            _ctx.InvalidateCanvas();
        }
    }

    private void AssignSCBinding()
    {
        if (_scSelectedActionIndex < 0 || _scFilteredActions is null || _scSelectedActionIndex >= _scFilteredActions.Count)
            return;

        var action = _scFilteredActions[_scSelectedActionIndex];
        var availableVJoy = _ctx.VJoyDevices.Where(v => v.Exists).ToList();

        if (availableVJoy.Count == 0)
        {
            using var warningDialog = new FUIConfirmDialog(
                "No vJoy",
                "No vJoy devices available.\nPlease configure vJoy.",
                "OK", "Cancel");
            warningDialog.ShowDialog(_ctx.OwnerForm);
            return;
        }

        using var dialog = new SCAssignmentDialog(action, availableVJoy);

        if (dialog.ShowDialog(_ctx.OwnerForm) == DialogResult.OK)
        {
            var vjoyId = dialog.SelectedVJoyId;
            var inputName = dialog.SelectedInputName;

            // Check for conflicts - is this input already bound to another action?
            var conflicts = FindSCBindingConflicts(vjoyId, inputName, action.ActionMap, action.ActionName);

            if (conflicts.Count > 0)
            {
                string actionDisplayName = SCCategoryMapper.FormatActionName(action.ActionName);
                string inputDisplayName = FormatInputName(inputName);
                string deviceName = $"JS{_scExportProfile.GetSCInstance(vjoyId)}";
                using var conflictDialog = new BindingConflictDialog(conflicts, actionDisplayName, inputDisplayName, deviceName);
                conflictDialog.ShowDialog(_ctx.OwnerForm);

                if (conflictDialog.Result == BindingConflictResult.Cancel)
                {
                    _scAssigningInput = false;
                    return;
                }
                else if (conflictDialog.Result == BindingConflictResult.ReplaceAll)
                {
                    // Remove all conflicting bindings
                    foreach (var conflict in conflicts)
                    {
                        _scExportProfile.RemoveBinding(conflict.ActionMap, conflict.ActionName);
                    }
                }
                // else ApplyAnyway - just add the binding, allow duplicate
            }

            // Infer input type from the selected input name (not from action's expected type)
            var inputType = InferInputTypeFromName(inputName);

            var binding = new SCActionBinding
            {
                ActionMap = action.ActionMap,
                ActionName = action.ActionName,
                DeviceType = SCDeviceType.Joystick,
                VJoyDevice = vjoyId,
                InputName = inputName,
                InputType = inputType,
                Inverted = dialog.IsInverted
            };

            _scExportProfile.SetBinding(action.ActionMap, action.ActionName, binding);

            // Ensure this vJoy device has an SC instance mapping
            if (!_scExportProfile.VJoyToSCInstance.ContainsKey(vjoyId))
            {
                _scExportProfile.SetSCInstance(vjoyId, (int)vjoyId);
            }

            // Save the profile and update conflict detection
            _scExportProfileService?.SaveProfile(_scExportProfile);
            UpdateConflictingBindings();

            _scExportStatus = $"Bound {action.ActionName} to js{_scExportProfile.GetSCInstance(vjoyId)}_{inputName}";
            _scExportStatusTime = DateTime.Now;
        }

        _scAssigningInput = false;
    }

    private List<SCActionBinding> FindSCBindingConflicts(uint vjoyId, string inputName, string excludeActionMap, string excludeActionName)
    {
        var conflicts = new List<SCActionBinding>();

        foreach (var binding in _scExportProfile.Bindings)
        {
            // Skip the current action being assigned
            if (binding.ActionMap == excludeActionMap && binding.ActionName == excludeActionName)
                continue;

            // Check if same vJoy device and input
            if (binding.VJoyDevice == vjoyId &&
                string.Equals(binding.InputName, inputName, StringComparison.OrdinalIgnoreCase))
            {
                conflicts.Add(binding);
            }
        }

        return conflicts;
    }

    #endregion

    #region Utility Methods

    private static string TruncatePath(string path, int maxLength)
    {
        if (string.IsNullOrEmpty(path) || path.Length <= maxLength)
            return path;

        // Try to truncate by removing middle part
        int start = maxLength / 3;
        int end = maxLength - start - 3;  // 3 for "..."
        return path.Substring(0, start) + "..." + path.Substring(path.Length - end);
    }

    private void DrawSearchBox(SKCanvas canvas, SKRect bounds, string text, bool focused)
        => FUIWidgets.DrawSearchBox(canvas, bounds, text, focused, _ctx.MousePosition);

    private void DrawCollapseIndicator(SKCanvas canvas, float x, float y, bool isCollapsed, bool isHovered)
        => FUIWidgets.DrawCollapseIndicator(canvas, x, y, isCollapsed, isHovered);

    private void DrawSCCheckbox(SKCanvas canvas, SKRect bounds, bool isChecked, bool isHovered)
        => FUIWidgets.DrawSCCheckbox(canvas, bounds, isChecked, isHovered);

    private void DrawSCProfileDropdown(SKCanvas canvas, SKRect bounds, string text, bool hovered, bool open)
        => SCBindingsRenderer.DrawSCProfileDropdown(canvas, bounds, text, hovered, open);

    private void DrawSCProfileDropdownWide(SKCanvas canvas, SKRect bounds, string text, bool hovered, bool open)
        => SCBindingsRenderer.DrawSCProfileDropdownWide(canvas, bounds, text, hovered, open);

    private void DrawProfileRefreshButton(SKCanvas canvas, SKRect bounds, bool hovered)
        => FUIWidgets.DrawProfileRefreshButton(canvas, bounds, hovered);

    private void DrawTextButton(SKCanvas canvas, SKRect bounds, string text, bool hovered, bool disabled = false)
        => FUIWidgets.DrawTextButton(canvas, bounds, text, hovered, disabled);

    private void DrawSCProfileDropdownList(SKCanvas canvas, SKRect bounds)
    {
        // Drop shadow with glow effect (FUI style)
        FUIRenderer.DrawPanelShadow(canvas, bounds, 4f, 4f, 15f);

        // Outer glow (subtle)
        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active.WithAlpha(30),
            StrokeWidth = 3f,
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(4f, 4f)
        };
        canvas.DrawRect(bounds, glowPaint);

        // Solid opaque background
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Void, IsAntialias = true };
        canvas.DrawRect(bounds, bgPaint);

        // Inner background
        using var innerBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background0, IsAntialias = true };
        canvas.DrawRect(bounds.Inset(2, 2), innerBgPaint);

        // L-corner frame (FUI style)
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Active.WithAlpha(180), 20f, 6f, 1.5f, true);

        // Items
        float rowHeight = 24f;
        float y = bounds.Top + 4;
        _scHoveredProfileIndex = -1;
        _scDropdownDeleteProfileName = "";

        // Section 1: Asteriq profiles — active profile is shown in the header, skip it here
        for (int i = 0; i < _scExportProfiles.Count && y + rowHeight <= bounds.Bottom; i++)
        {
            var profile = _scExportProfiles[i];
            if (profile.ProfileName == _scExportProfile.ProfileName) continue;

            var rowBounds = new SKRect(bounds.Left + 4, y, bounds.Right - 4, y + rowHeight);
            bool isHovered = rowBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

            // FUI hover style with accent bar
            if (isHovered)
            {
                _scHoveredProfileIndex = i;
                using var hoverPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(40), IsAntialias = true };
                canvas.DrawRect(rowBounds, hoverPaint);
                using var accentPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active, IsAntialias = true };
                canvas.DrawRect(new SKRect(rowBounds.Left, rowBounds.Top + 2, rowBounds.Left + 2, rowBounds.Bottom - 2), accentPaint);

                // Delete (×) button — only shown on hover
                _scDropdownDeleteButtonBounds = new SKRect(rowBounds.Right - 22, rowBounds.Top + 4, rowBounds.Right - 4, rowBounds.Bottom - 4);
                _scDropdownDeleteProfileName = profile.ProfileName;
                bool delHovered = _scDropdownDeleteButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
                FUIRenderer.DrawText(canvas, "×", new SKPoint(_scDropdownDeleteButtonBounds.MidX - 3f, _scDropdownDeleteButtonBounds.MidY + 4f),
                    delHovered ? FUIColors.TextBright : FUIColors.TextDim, 11f);
            }

            var textColor = isHovered ? FUIColors.TextBright : FUIColors.TextPrimary;
            float maxTextWidth = rowBounds.Width - (isHovered ? 56f : 40f); // extra room for × on hover
            string displayName = TruncateTextToWidth(profile.ProfileName, maxTextWidth, 10f);
            FUIRenderer.DrawText(canvas, displayName, new SKPoint(rowBounds.Left + 10, rowBounds.MidY + 4f), textColor, 10f);

            // Binding count badge
            if (profile.BindingCount > 0)
            {
                string countStr = profile.BindingCount.ToString();
                float badgeX = rowBounds.Right - (isHovered ? 28f : 8f) - FUIRenderer.MeasureText(countStr, 8f);
                FUIRenderer.DrawText(canvas, countStr, new SKPoint(badgeX, rowBounds.MidY + 3f), FUIColors.TextDim, 8f);
            }

            y += rowHeight;
        }

        // Section 2: SC mapping files from mappings folder (if any)
        if (_scAvailableProfiles.Count > 0 && y + rowHeight <= bounds.Bottom)
        {
            // Separator line (FUI style)
            y += 4f;
            float sepY = y;
            using var sepPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f, IsAntialias = true };
            canvas.DrawLine(bounds.Left + 12, sepY, bounds.Right - 12, sepY, sepPaint);

            // Corner accents on separator
            using var accentLinePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active.WithAlpha(120), StrokeWidth = 1f, IsAntialias = true };
            canvas.DrawLine(bounds.Left + 8, sepY, bounds.Left + 12, sepY, accentLinePaint);
            canvas.DrawLine(bounds.Right - 12, sepY, bounds.Right - 8, sepY, accentLinePaint);

            y += 6f;

            // Section label: make it clear these are SC files to import from, not Asteriq profiles
            FUIRenderer.DrawText(canvas, "IMPORT FROM SC", new SKPoint(bounds.Left + 10, y + 9f), FUIColors.TextDim, 8f, true);
            y += 16f;

            // SC mapping files
            int scFileIndexOffset = _scExportProfiles.Count + 1000; // Use offset to distinguish from Asteriq profiles
            for (int i = 0; i < _scAvailableProfiles.Count && y + rowHeight <= bounds.Bottom; i++)
            {
                var scFile = _scAvailableProfiles[i];
                var rowBounds = new SKRect(bounds.Left + 4, y, bounds.Right - 4, y + rowHeight);
                bool isHovered = rowBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

                if (isHovered)
                {
                    _scHoveredProfileIndex = scFileIndexOffset + i;
                    using var hoverPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(40), IsAntialias = true };
                    canvas.DrawRect(rowBounds, hoverPaint);
                    using var accentPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active, IsAntialias = true };
                    canvas.DrawRect(new SKRect(rowBounds.Left, rowBounds.Top + 2, rowBounds.Left + 2, rowBounds.Bottom - 2), accentPaint);
                }

                var textColor = isHovered ? FUIColors.TextBright : FUIColors.TextPrimary;
                float maxTextWidth = rowBounds.Width - 20f;
                string displayName = scFile.DisplayName;
                displayName = TruncateTextToWidth(displayName, maxTextWidth, 10f);
                FUIRenderer.DrawText(canvas, displayName, new SKPoint(rowBounds.Left + 10, rowBounds.MidY + 4f), textColor, 10f);

                y += rowHeight;
            }
        }
    }

    private void DrawSCProfileButton(SKCanvas canvas, SKRect bounds, string icon, bool hovered, string tooltip, bool disabled = false)
        => SCBindingsRenderer.DrawSCProfileButton(canvas, bounds, icon, hovered, tooltip, disabled);

    #endregion

    #region SC Profile Management

    private void SaveSCExportProfile()
    {
        if (_scExportProfileService is null) return;

        // If there's no name yet, prompt the user (same as Create New)
        if (string.IsNullOrEmpty(_scExportProfile.ProfileName))
        {
            CreateNewSCExportProfile();
            return;
        }

        _scExportProfileService.SaveProfile(_scExportProfile);
        _scProfileDirty = false;
        if (CurrentEnvironment is not null)
            _ctx.AppSettings.SetLastSCExportProfileForEnvironment(CurrentEnvironment, _scExportProfile.ProfileName);
        _ctx.AppSettings.LastSCExportProfile = _scExportProfile.ProfileName;
        RefreshSCExportProfiles();

        _scExportStatus = $"Profile '{_scExportProfile.ProfileName}' saved";
        _scExportStatusTime = DateTime.Now;
    }

    private void CreateNewSCExportProfile()
    {
        var newName = FUIInputDialog.Show(_ctx.OwnerForm, "New Control Profile", "Profile Name:",
            "New Profile", "Create");
        if (newName is not null)
        {

            // Check for duplicate name
            if (_scExportProfileService is not null && _scExportProfileService.ProfileExists(newName))
            {
                using var existsDialog = new FUIConfirmDialog(
                    "Profile Exists",
                    $"A profile named '{newName}' already exists.",
                    "OK", "Cancel");
                existsDialog.ShowDialog(_ctx.OwnerForm);
                return;
            }

            // Create new profile
            _scExportProfile = new SCExportProfile
            {
                ProfileName = newName,
                TargetEnvironment = _scExportProfile.TargetEnvironment,
                TargetBuildId = _scExportProfile.TargetBuildId
            };

            // Set up default vJoy mappings
            foreach (var vjoy in _ctx.VJoyDevices.Where(v => v.Exists))
            {
                _scExportProfile.SetSCInstance(vjoy.Id, (int)vjoy.Id);
            }

            SaveSCExportProfile();
            _scExportStatus = $"Created profile '{newName}'";
            _scExportStatusTime = DateTime.Now;
        }
    }

    private void DeleteSCExportProfile()
    {
        if (_scExportProfileService is null || _scExportProfiles.Count == 0) return;

        using var confirmDialog = new FUIConfirmDialog(
            "Delete Profile",
            $"Delete control profile '{_scExportProfile.ProfileName}'?\n\nThis cannot be undone.",
            "Delete", "Cancel");

        if (confirmDialog.ShowDialog(_ctx.OwnerForm) == DialogResult.Yes)
        {
            var deletedName = _scExportProfile.ProfileName;
            _scExportProfileService.DeleteProfile(deletedName);
            RefreshSCExportProfiles();

            // Load another profile or create default
            if (_scExportProfiles.Count > 0)
            {
                var nextProfile = _scExportProfileService.LoadProfile(_scExportProfiles[0].ProfileName);
                if (nextProfile is not null)
                {
                    _scExportProfile = nextProfile;
                    _scProfileDirty = false;
                    if (CurrentEnvironment is not null)
                        _ctx.AppSettings.SetLastSCExportProfileForEnvironment(CurrentEnvironment, nextProfile.ProfileName);
                    _ctx.AppSettings.LastSCExportProfile = nextProfile.ProfileName;
                }
            }
            else
            {
                // All profiles deleted - reset to blank unnamed state
                if (CurrentEnvironment is not null)
                    _ctx.AppSettings.SetLastSCExportProfileForEnvironment(CurrentEnvironment, null);
                _ctx.AppSettings.LastSCExportProfile = null;
                _scProfileDirty = false;
                _scExportProfile = new SCExportProfile();
                foreach (var vjoy in _ctx.VJoyDevices.Where(v => v.Exists))
                {
                    _scExportProfile.SetSCInstance(vjoy.Id, (int)vjoy.Id);
                }
            }

            _scExportStatus = $"Deleted profile '{deletedName}'";
            _scExportStatusTime = DateTime.Now;
        }
    }

    private void LoadSCExportProfile(string profileName)
    {
        if (_scExportProfileService is null) return;

        var profile = _scExportProfileService.LoadProfile(profileName);
        if (profile is not null)
        {
            _scExportProfile = profile;
            _scProfileDirty = false;
            if (CurrentEnvironment is not null)
                _ctx.AppSettings.SetLastSCExportProfileForEnvironment(CurrentEnvironment, profileName);
            _ctx.AppSettings.LastSCExportProfile = profileName;
            _scProfileDropdownOpen = false;
            _scExportStatus = $"Loaded profile '{profileName}'";
            _scExportStatusTime = DateTime.Now;
            _ctx.InvalidateCanvas();
        }
    }

    private void ImportSCProfile(SCMappingFile mappingFile)
    {
        if (_scExportService is null) return;

        // Warn before overwriting an existing named profile's bindings
        string oldProfileName = _scExportProfile.ProfileName;
        if (!string.IsNullOrEmpty(oldProfileName) && _scExportProfile.Bindings.Count > 0)
        {
            var result = FUIMessageBox.ShowQuestion(_ctx.OwnerForm,
                $"Profile '{oldProfileName}' has {_scExportProfile.Bindings.Count} existing binding(s).\n\n" +
                "Import will replace all current bindings. Continue?",
                "Replace Bindings");

            if (!result)
                return;
        }

        // Always adopt the SC file's name so the dropdown reflects what was imported
        _scExportProfile.ProfileName = mappingFile.DisplayName;

        // Import the profile
        var importResult = _scExportService.ImportFromFile(mappingFile.FilePath);

        if (!importResult.Success)
        {
            _scExportStatus = $"Import failed: {importResult.Error}";
            _scExportStatusTime = DateTime.Now;
            return;
        }

        // Log import stats for debugging
        var kbCount = importResult.Bindings.Count(b => b.DeviceType == SCDeviceType.Keyboard);
        var moCount = importResult.Bindings.Count(b => b.DeviceType == SCDeviceType.Mouse);
        var jsCount = importResult.Bindings.Count(b => b.DeviceType == SCDeviceType.Joystick);
        System.Diagnostics.Debug.WriteLine($"[SCBindings] Import parsed: {kbCount} KB, {moCount} Mouse, {jsCount} Joystick bindings");


        // Clear existing bindings and add imported ones
        _scExportProfile.ClearBindings();
        foreach (var binding in importResult.Bindings)
        {
            _scExportProfile.SetBinding(binding.ActionMap, binding.ActionName, binding);
        }

        // Update VJoy to SC instance mappings from imported joystick bindings
        var usedInstances = importResult.Bindings
            .Where(b => b.DeviceType == SCDeviceType.Joystick)
            .Select(b => b.VJoyDevice)
            .Distinct();
        foreach (var instance in usedInstances)
        {
            _scExportProfile.SetSCInstance(instance, (int)instance);
        }

        // Save the profile under the new name; delete old file if the name changed
        _scExportProfileService?.SaveProfile(_scExportProfile);
        if (!string.IsNullOrEmpty(oldProfileName) && oldProfileName != _scExportProfile.ProfileName)
            _scExportProfileService?.DeleteProfile(oldProfileName);
        _scProfileDirty = false;
        if (CurrentEnvironment is not null)
            _ctx.AppSettings.SetLastSCExportProfileForEnvironment(CurrentEnvironment, _scExportProfile.ProfileName);
        _ctx.AppSettings.LastSCExportProfile = _scExportProfile.ProfileName;
        RefreshSCExportProfiles();

        // Update conflicts
        UpdateConflictingBindings();

        // Log final profile stats
        var finalKb = _scExportProfile.Bindings.Count(b => b.DeviceType == SCDeviceType.Keyboard);
        var finalMo = _scExportProfile.Bindings.Count(b => b.DeviceType == SCDeviceType.Mouse);
        var finalJs = _scExportProfile.Bindings.Count(b => b.DeviceType == SCDeviceType.Joystick);
        System.Diagnostics.Debug.WriteLine($"[SCBindings] Profile after save: {finalKb} KB, {finalMo} Mouse, {finalJs} Joystick bindings");

        _scExportStatus = $"Imported {importResult.Bindings.Count} bindings ({jsCount} JS, {kbCount} KB, {moCount} Mouse)";
        _scExportStatusTime = DateTime.Now;
        _ctx.InvalidateCanvas();

        System.Diagnostics.Debug.WriteLine($"[SCBindings] Imported {importResult.Bindings.Count} bindings from {mappingFile.FilePath}");
    }

    #endregion

    #region Keyboard & Column Helpers

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

    private bool HandleSearchBoxKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;

        if (key == Keys.Escape)
        {
            _scSearchBoxFocused = false;
            return true;
        }

        if (key == Keys.Back)
        {
            if (_scSearchText.Length > 0)
            {
                _scSearchText = _scSearchText.Substring(0, _scSearchText.Length - 1);
                RefreshFilteredActions();
            }
            return true;
        }

        if (key == Keys.Delete)
        {
            _scSearchText = "";
            RefreshFilteredActions();
            return true;
        }

        char c = KeyToChar(key, (keyData & Keys.Shift) == Keys.Shift);
        if (c != '\0' && _scSearchText.Length < 50)
        {
            _scSearchText += c;
            RefreshFilteredActions();
            return true;
        }

        return false;
    }

    private bool HandleExportFilenameBoxKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;

        if (key == Keys.Escape || key == Keys.Enter)
        {
            _scExportFilenameBoxFocused = false;
            return true;
        }

        if (key == Keys.Back)
        {
            if (_scExportFilename.Length > 0)
            {
                _scExportFilename = _scExportFilename.Substring(0, _scExportFilename.Length - 1);
            }
            return true;
        }

        if (key == Keys.Delete)
        {
            _scExportFilename = "";
            return true;
        }

        char c = KeyToFilenameChar(key, (keyData & Keys.Shift) == Keys.Shift);
        if (c != '\0' && _scExportFilename.Length < 50)
        {
            _scExportFilename += c;
            return true;
        }

        return false;
    }

    private static char KeyToFilenameChar(Keys key, bool shift)
    {
        if (key >= Keys.A && key <= Keys.Z)
        {
            char c = (char)('a' + (key - Keys.A));
            return shift ? char.ToUpper(c) : c;
        }

        if (key >= Keys.D0 && key <= Keys.D9)
        {
            return (char)('0' + (key - Keys.D0));
        }

        return key switch
        {
            Keys.OemMinus => shift ? '_' : '-',
            Keys.Oemplus => '=',
            Keys.OemPeriod => '.',
            _ => '\0'
        };
    }

    private static char KeyToChar(Keys key, bool shift)
    {
        if (key >= Keys.A && key <= Keys.Z)
        {
            char c = (char)('a' + (key - Keys.A));
            return shift ? char.ToUpper(c) : c;
        }

        if (key >= Keys.D0 && key <= Keys.D9)
        {
            return (char)('0' + (key - Keys.D0));
        }

        return key switch
        {
            Keys.Space => ' ',
            Keys.OemMinus => shift ? '_' : '-',
            Keys.Oemplus => shift ? '+' : '=',
            Keys.OemPeriod => '.',
            Keys.Oemcomma => ',',
            _ => '\0'
        };
    }

    private int GetHoveredColumnIndex(float x)
    {
        if (_scGridColumns is null || x < _scDeviceColsStart || x > _scDeviceColsStart + _scVisibleDeviceWidth)
            return -1;

        float relativeX = x - _scDeviceColsStart + _scGridHorizontalScroll;

        float cumX = 0f;
        for (int c = 0; c < _scGridColumns.Count; c++)
        {
            float colW = _scGridDeviceColWidths.TryGetValue(_scGridColumns[c].Id, out var w) ? w : _scGridDeviceColMinWidth;
            if (relativeX >= cumX && relativeX < cumX + colW)
                return c;
            cumX += colW;
        }

        return -1;
    }

    #endregion
}
