using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;

namespace Asteriq.UI;

/// <summary>
/// MainForm partial - SC Bindings tab rendering and logic
/// </summary>
public partial class MainForm
{
    #region SC Bindings Initialization

    private void InitializeSCBindings()
    {
        try
        {
            // SC services are now injected via constructor
            // _scInstallationService, _scProfileCacheService, _scSchemaService,
            // _scExportService, and _scExportProfileService are already assigned

            // Ensure vJoy devices are enumerated for SC Bindings columns
            if (_vjoyDevices.Count == 0 && _vjoyService is not null)
            {
                _vjoyDevices = _vjoyService.EnumerateDevices();
            }

            RefreshSCInstallations();
            RefreshSCExportProfiles();

            // Try to load the last used SC export profile
            var lastProfileName = _profileService.LastSCExportProfile;
            SCExportProfile? loadedProfile = null;

            if (!string.IsNullOrEmpty(lastProfileName) && _profileService.AutoLoadLastSCExportProfile)
            {
                loadedProfile = _scExportProfileService.LoadProfile(lastProfileName);
            }

            if (loadedProfile is not null)
            {
                _scExportProfile = loadedProfile;
                System.Diagnostics.Debug.WriteLine($"[MainForm] Loaded last SC export profile: {loadedProfile.ProfileName}");
            }
            else
            {
                // Initialize export profile with default name
                _scExportProfile = new SCExportProfile
                {
                    ProfileName = "asteriq"
                };

                // Set up default vJoy mappings based on available vJoy devices
                foreach (var vjoy in _vjoyDevices.Where(v => v.Exists))
                {
                    _scExportProfile.SetSCInstance(vjoy.Id, (int)vjoy.Id);
                }
            }

            // Initial conflict detection
            UpdateConflictingBindings();

            System.Diagnostics.Debug.WriteLine($"[MainForm] SC bindings initialized, {_scInstallations.Count} installations found");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] SC bindings init failed: {ex.Message}");
        }
    }

    private void RefreshSCExportProfiles()
    {
        if (_scExportProfileService is null) return;
        _scExportProfiles = _scExportProfileService.ListProfiles();
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
        foreach (var vjoy in _vjoyDevices.Where(v => v.Exists))
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

    private void RefreshSCInstallations()
    {
        if (_scInstallationService is null) return;

        _scInstallations = _scInstallationService.Installations.ToList();

        // Select preferred installation if none selected
        if (_selectedSCInstallation >= _scInstallations.Count)
        {
            _selectedSCInstallation = 0;
        }

        // Load schema for selected installation
        if (_scInstallations.Count > 0 && _selectedSCInstallation < _scInstallations.Count)
        {
            LoadSCSchema(_scInstallations[_selectedSCInstallation]);
        }
    }

    private void LoadSCSchema(SCInstallation installation)
    {
        if (_scProfileCacheService is null || _scSchemaService is null) return;

        try
        {
            var profile = _scProfileCacheService.GetOrExtractProfile(installation);
            if (profile is not null)
            {
                _scActions = _scSchemaService.ParseActions(profile);
                _scExportProfile.TargetEnvironment = installation.Environment;
                _scExportProfile.TargetBuildId = installation.BuildId;

                // Build category list from joystick-relevant actions (includes action-level overrides like Emergency)
                var joystickActions = _scSchemaService.FilterJoystickActions(_scActions);
                _scActionMaps = SCCategoryMapper.GetSortedCategoriesFromActions(
                    joystickActions.Select(a => (a.ActionMap, a.ActionName))
                ).ToList();

                // NOTE: We intentionally do NOT auto-apply defaults here.
                // Defaults are only applied when user explicitly clicks "Reset Defaults".
                // This allows users to create completely blank profiles if desired.

                // Default: show joystick-relevant actions only
                RefreshFilteredActions();

                // Calculate dynamic column widths based on binding content
                CalculateDeviceColumnWidths();

                // Load available profiles from mappings folder for import
                _scAvailableProfiles = SCInstallationService.GetExistingProfiles(installation);

                System.Diagnostics.Debug.WriteLine($"[MainForm] Loaded {_scActions.Count} SC actions from {installation.Environment}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] Failed to load SC schema: {ex.Message}");
            _scActions = null;
            _scFilteredActions = null;
            _scActionMaps.Clear();
            _scAvailableProfiles.Clear();
        }
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

        bool selectorHovered = _scInstallationSelectorBounds.Contains(_mousePosition.X, _mousePosition.Y);
        DrawSelector(canvas, _scInstallationSelectorBounds, installationText, selectorHovered || _scInstallationDropdownOpen, _scInstallations.Count > 0);
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
        _scRefreshButtonHovered = _scRefreshButtonBounds.Contains(_mousePosition.X, _mousePosition.Y);
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
        _scProfileNameHovered = _scProfileNameBounds.Contains(_mousePosition.X, _mousePosition.Y);
        DrawTextFieldReadOnly(canvas, _scProfileNameBounds, _scExportProfile.ProfileName, _scProfileNameHovered);
        y += nameFieldHeight + 12f;  // 4px aligned

        // Export filename preview
        FUIRenderer.DrawText(canvas, "FILENAME", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += lineHeight;
        FUIRenderer.DrawText(canvas, _scExportProfile.GetExportFileName(), new SKPoint(leftMargin, y), FUIColors.TextDim, 11f);
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

        // Refresh available profiles when installation changes
        if (_scInstallations.Count > 0 && _selectedSCInstallation < _scInstallations.Count)
        {
            var installation = _scInstallations[_selectedSCInstallation];
            if (_scAvailableProfiles.Count == 0 || _scImportDropdownOpen)
            {
                _scAvailableProfiles = SCInstallationService.GetExistingProfiles(installation);
            }
        }

        // Import button/selector
        float buttonWidth = 200f;
        float buttonHeight = 32f;
        float buttonX = leftMargin + (rightMargin - leftMargin - buttonWidth) / 2;

        _scImportButtonBounds = new SKRect(buttonX, y, buttonX + buttonWidth, y + buttonHeight);
        _scImportButtonHovered = _scImportButtonBounds.Contains(_mousePosition.X, _mousePosition.Y);

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
        _scExportButtonHovered = _scExportButtonBounds.Contains(_mousePosition.X, _mousePosition.Y);

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

        bool selectorHovered = _scInstallationSelectorBounds.Contains(_mousePosition.X, _mousePosition.Y);
        DrawSelector(canvas, _scInstallationSelectorBounds, installationText, selectorHovered || _scInstallationDropdownOpen, _scInstallations.Count > 0);
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

        // Action count on right of title
        int actionCount = _scFilteredActions?.Count ?? 0;
        int boundCount = _scFilteredActions?.Count(a => _scExportProfile.GetBinding(a.ActionMap, a.ActionName) is not null) ?? 0;
        string countText = $"{actionCount} actions, {boundCount} bound";
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
        bool filterHovered = _scActionMapFilterBounds.Contains(_mousePosition.X, _mousePosition.Y);
        DrawSelector(canvas, _scActionMapFilterBounds, filterText, filterHovered || _scActionMapFilterDropdownOpen, _scActionMaps.Count > 0);

        // Search box on the left (max 280px wide)
        float maxSearchWidth = 280f;
        _scSearchBoxBounds = new SKRect(leftMargin, y, leftMargin + maxSearchWidth, y + filterRowHeight);
        DrawSearchBox(canvas, _scSearchBoxBounds, _scSearchText, _scSearchBoxFocused);

        // Checkbox after search box
        float checkboxX = leftMargin + maxSearchWidth + gap;
        _scShowBoundOnlyBounds = new SKRect(checkboxX, y + (filterRowHeight - checkboxSize) / 2,
            checkboxX + checkboxSize, y + (filterRowHeight + checkboxSize) / 2);
        _scShowBoundOnlyHovered = _scShowBoundOnlyBounds.Contains(_mousePosition.X, _mousePosition.Y);
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
            FUIRenderer.DrawText(canvas, _scActions is null ? "Loading actions..." : "No actions match filter",
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
                        bool headerHovered = headerBounds.Contains(_mousePosition.X, _mousePosition.Y);

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

            bool vScrollHovered = _scVScrollbarBounds.Contains(_mousePosition.X, _mousePosition.Y) || _scIsDraggingVScroll;

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

            bool hScrollHovered = _scHScrollbarBounds.Contains(_mousePosition.X, _mousePosition.Y) || _scIsDraggingHScroll;

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
    {
        // Measure text to determine badge width
        float fontSize = 9f;
        float textWidth = FUIRenderer.MeasureText(text, fontSize);

        // Add space for type indicator if provided
        float indicatorWidth = inputType.HasValue ? 14f : 0f;
        float badgeWidth = Math.Min(maxWidth - 2, textWidth + indicatorWidth + 10);
        float badgeHeight = 16f;

        var badgeBounds = new SKRect(x, y, x + badgeWidth, y + badgeHeight);

        // Badge background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = isDefault ? FUIColors.Background2.WithAlpha(180) : color.WithAlpha(40),
            IsAntialias = true
        };
        canvas.DrawRoundRect(badgeBounds, 3f, 3f, bgPaint);

        // Badge border
        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color.WithAlpha(isDefault ? (byte)100 : (byte)180),
            StrokeWidth = 1f,
            IsAntialias = true
        };
        canvas.DrawRoundRect(badgeBounds, 3f, 3f, borderPaint);

        float textX = x + 5;

        // Draw type indicator if provided
        if (inputType.HasValue)
        {
            DrawInputTypeIndicator(canvas, x + 4, y + badgeHeight / 2, inputType.Value, color);
            textX = x + indicatorWidth + 2;
        }

        // Badge text (truncate if needed)
        string displayText = text;
        float availableTextWidth = badgeWidth - (textX - x) - 5;
        if (textWidth > availableTextWidth)
        {
            displayText = TruncateTextToWidth(text, availableTextWidth - 4, fontSize);
        }
        FUIRenderer.DrawText(canvas, displayText, new SKPoint(textX, y + badgeHeight / 2 + 3), color, fontSize);
    }

    /// <summary>
    /// Draws a keycap-style badge for a binding, centered within the given cell bounds
    /// Uses larger font and centered positioning for better readability
    /// </summary>
    private void DrawBindingBadgeCentered(SKCanvas canvas, SKRect cellBounds, string text, SKColor color, bool isDefault, SCInputType? inputType = null)
    {
        // Use larger font for better readability
        float fontSize = 11f;
        float textWidth = FUIRenderer.MeasureText(text, fontSize);

        // Add space for type indicator if provided
        float indicatorWidth = inputType.HasValue ? 14f : 0f;
        float padding = 8f;
        float badgeWidth = Math.Min(cellBounds.Width - 6, textWidth + indicatorWidth + padding * 2);
        float badgeHeight = 20f;

        // Center the badge in the cell
        float badgeX = cellBounds.Left + (cellBounds.Width - badgeWidth) / 2;
        float badgeY = cellBounds.Top + (cellBounds.Height - badgeHeight) / 2;

        var badgeBounds = new SKRect(badgeX, badgeY, badgeX + badgeWidth, badgeY + badgeHeight);

        // Badge background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = isDefault ? FUIColors.Background2.WithAlpha(180) : color.WithAlpha(40),
            IsAntialias = true
        };
        canvas.DrawRoundRect(badgeBounds, 4f, 4f, bgPaint);

        // Badge border
        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color.WithAlpha(isDefault ? (byte)100 : (byte)180),
            StrokeWidth = 1f,
            IsAntialias = true
        };
        canvas.DrawRoundRect(badgeBounds, 4f, 4f, borderPaint);

        float textX = badgeX + padding;

        // Draw type indicator if provided
        if (inputType.HasValue)
        {
            DrawInputTypeIndicator(canvas, badgeX + 4, badgeY + badgeHeight / 2, inputType.Value, color);
            textX = badgeX + indicatorWidth + 4;
        }

        // Badge text (truncate if needed), vertically centered
        string displayText = text;
        float availableTextWidth = badgeWidth - (textX - badgeX) - padding;
        if (textWidth > availableTextWidth)
        {
            displayText = TruncateTextToWidth(text, availableTextWidth - 4, fontSize);
        }

        // Center text vertically in badge
        float textY = badgeY + badgeHeight / 2 + 4;
        FUIRenderer.DrawText(canvas, displayText, new SKPoint(textX, textY), color, fontSize);
    }

    /// <summary>
    /// Draws multiple keycap badges for a binding (one per modifier + main key)
    /// Each key component gets its own separate badge, centered within the cell
    /// </summary>
    private void DrawMultiKeycapBinding(SKCanvas canvas, SKRect cellBounds, List<string> components, SKColor color, SCInputType? inputType)
    {
        if (components.Count == 0) return;

        float fontSize = 10f;
        float badgeHeight = 18f;
        float badgePadding = 8f;  // Horizontal padding inside badge
        float gap = 3f;  // Gap between badges

        // Calculate total width needed
        float totalWidth = 0f;
        var badgeWidths = new float[components.Count];
        for (int i = 0; i < components.Count; i++)
        {
            float textWidth = FUIRenderer.MeasureText(components[i], fontSize);
            // Add indicator space only to the last (main key) badge
            float indicatorSpace = (i == components.Count - 1 && inputType.HasValue) ? 12f : 0f;
            badgeWidths[i] = textWidth + badgePadding * 2 + indicatorSpace;
            totalWidth += badgeWidths[i];
            if (i > 0) totalWidth += gap;
        }

        // Start position (centered)
        float startX = cellBounds.Left + (cellBounds.Width - totalWidth) / 2;
        float badgeY = cellBounds.Top + (cellBounds.Height - badgeHeight) / 2;
        float currentX = startX;

        for (int i = 0; i < components.Count; i++)
        {
            var comp = components[i];
            float badgeWidth = badgeWidths[i];
            bool isMainKey = i == components.Count - 1;

            var badgeBounds = new SKRect(currentX, badgeY, currentX + badgeWidth, badgeY + badgeHeight);

            // Badge background - modifiers slightly dimmer
            byte bgAlpha = isMainKey ? (byte)50 : (byte)35;
            using var bgPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = color.WithAlpha(bgAlpha),
                IsAntialias = true
            };
            canvas.DrawRoundRect(badgeBounds, 3f, 3f, bgPaint);

            // Badge border - modifiers slightly dimmer
            byte borderAlpha = isMainKey ? (byte)180 : (byte)120;
            using var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = color.WithAlpha(borderAlpha),
                StrokeWidth = 1f,
                IsAntialias = true
            };
            canvas.DrawRoundRect(badgeBounds, 3f, 3f, borderPaint);

            float textX = currentX + badgePadding;

            // Draw type indicator only on main key
            if (isMainKey && inputType.HasValue)
            {
                DrawInputTypeIndicator(canvas, currentX + 4, badgeY + badgeHeight / 2, inputType.Value, color);
                textX = currentX + 14f;
            }

            // Draw text
            float textY = badgeY + badgeHeight / 2 + 3.5f;
            var textColor = isMainKey ? color : color.WithAlpha(200);
            FUIRenderer.DrawText(canvas, comp, new SKPoint(textX, textY), textColor, fontSize);

            currentX += badgeWidth + gap;
        }
    }

    /// <summary>
    /// Calculates the total width needed to draw multiple keycap badges
    /// </summary>
    private float MeasureMultiKeycapWidth(List<string> components, SCInputType? inputType)
    {
        float fontSize = 10f;
        float badgePadding = 8f;
        float gap = 3f;

        float totalWidth = 0f;
        for (int i = 0; i < components.Count; i++)
        {
            float textWidth = FUIRenderer.MeasureText(components[i], fontSize);
            float indicatorSpace = (i == components.Count - 1 && inputType.HasValue) ? 12f : 0f;
            totalWidth += textWidth + badgePadding * 2 + indicatorSpace;
            if (i > 0) totalWidth += gap;
        }

        return totalWidth;
    }

    /// <summary>
    /// Draws a small type indicator icon for axis, button, or hat
    /// </summary>
    private void DrawInputTypeIndicator(SKCanvas canvas, float x, float centerY, SCInputType inputType, SKColor color)
    {
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color.WithAlpha(150),
            StrokeWidth = 1.2f,
            IsAntialias = true
        };

        switch (inputType)
        {
            case SCInputType.Axis:
                // Double-headed arrow for axis
                float arrowLen = 4f;
                canvas.DrawLine(x, centerY, x + arrowLen * 2, centerY, paint);
                // Left arrow head
                canvas.DrawLine(x, centerY, x + 2, centerY - 2, paint);
                canvas.DrawLine(x, centerY, x + 2, centerY + 2, paint);
                // Right arrow head
                canvas.DrawLine(x + arrowLen * 2, centerY, x + arrowLen * 2 - 2, centerY - 2, paint);
                canvas.DrawLine(x + arrowLen * 2, centerY, x + arrowLen * 2 - 2, centerY + 2, paint);
                break;

            case SCInputType.Button:
                // Small filled circle for button
                paint.Style = SKPaintStyle.Fill;
                canvas.DrawCircle(x + 4, centerY, 3f, paint);
                break;

            case SCInputType.Hat:
                // Diamond/cross for hat
                float hatSize = 3f;
                float cx = x + 4;
                canvas.DrawLine(cx, centerY - hatSize, cx, centerY + hatSize, paint);
                canvas.DrawLine(cx - hatSize, centerY, cx + hatSize, centerY, paint);
                break;
        }
    }

    /// <summary>
    /// Draws a small conflict warning indicator (exclamation mark in triangle)
    /// </summary>
    private void DrawConflictIndicator(SKCanvas canvas, float x, float y)
    {
        float size = 8f;

        // Draw triangle background - use Warning color since conflicts are valid
        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Warning,
            IsAntialias = true
        };

        var path = new SKPath();
        path.MoveTo(x + size / 2, y);
        path.LineTo(x + size, y + size);
        path.LineTo(x, y + size);
        path.Close();
        canvas.DrawPath(path, fillPaint);

        // Draw exclamation mark
        using var textPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.White,
            IsAntialias = true,
            TextSize = 7f
        };
        canvas.DrawText("!", x + size / 2 - 1.5f, y + size - 1.5f, textPaint);
    }

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

    /// <summary>
    /// Truncates text to fit within a specified width, adding ellipsis if needed
    /// </summary>
    private string TruncateTextToWidth(string text, float maxWidth, float fontSize)
    {
        float textWidth = FUIRenderer.MeasureText(text, fontSize);
        if (textWidth <= maxWidth)
            return text;

        // Binary search for best fit
        int low = 0;
        int high = text.Length;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            string testText = text.Substring(0, mid) + "...";
            if (FUIRenderer.MeasureText(testText, fontSize) <= maxWidth)
                low = mid;
            else
                high = mid - 1;
        }

        return low > 0 ? text.Substring(0, low) + "..." : "...";
    }

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
        FUIRenderer.DrawText(canvas, "PROFILES", new SKPoint(leftMargin, y), FUIColors.TextBright, 11f, true);
        y += 18f;

        // Profile dropdown (full width)
        float dropdownHeight = 32f;
        _scProfileDropdownBounds = new SKRect(leftMargin, y, rightMargin, y + dropdownHeight);
        bool dropdownHovered = _scProfileDropdownBounds.Contains(_mousePosition.X, _mousePosition.Y);
        DrawSCProfileDropdownWide(canvas, _scProfileDropdownBounds, _scExportProfile.ProfileName, dropdownHovered, _scProfileDropdownOpen);
        y += dropdownHeight + 6f;

        // Buttons row: + New, Save (aligned right)
        float textBtnWidth = 52f;  // 4px aligned
        float textBtnHeight = FUIRenderer.TouchTargetMinHeight;  // 24px minimum

        // Save button (rightmost)
        _scSaveProfileButtonBounds = new SKRect(rightMargin - textBtnWidth, y, rightMargin, y + textBtnHeight);
        _scSaveProfileButtonHovered = _scSaveProfileButtonBounds.Contains(_mousePosition.X, _mousePosition.Y);
        DrawTextButton(canvas, _scSaveProfileButtonBounds, "Save", _scSaveProfileButtonHovered);

        // New button (left of Save)
        float newBtnX = rightMargin - textBtnWidth * 2 - buttonGap;
        _scNewProfileButtonBounds = new SKRect(newBtnX, y, newBtnX + textBtnWidth, y + textBtnHeight);
        _scNewProfileButtonHovered = _scNewProfileButtonBounds.Contains(_mousePosition.X, _mousePosition.Y);
        DrawTextButton(canvas, _scNewProfileButtonBounds, "+ New", _scNewProfileButtonHovered);

        y += textBtnHeight + 10f;

        // Draw profile dropdown list if open (shows both Asteriq profiles and SC mapping files)
        if (_scProfileDropdownOpen)
        {
            int asteriqCount = _scExportProfiles.Count;
            int scFileCount = _scAvailableProfiles.Count;
            int totalItems = asteriqCount + (scFileCount > 0 ? scFileCount + 1 : 0); // +1 for separator
            float listHeight = Math.Min(totalItems * 24f + 8f, 200f);
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
            _scAssignInputButtonHovered = _scAssignInputButtonBounds.Contains(_mousePosition.X, _mousePosition.Y);

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
            _scClearBindingButtonHovered = _scClearBindingButtonBounds.Contains(_mousePosition.X, _mousePosition.Y);
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
        _scClearAllButtonHovered = _scClearAllButtonBounds.Contains(_mousePosition.X, _mousePosition.Y);
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
        _scResetDefaultsButtonHovered = _scResetDefaultsButtonBounds.Contains(_mousePosition.X, _mousePosition.Y);
        FUIRenderer.DrawButton(canvas, _scResetDefaultsButtonBounds, "RESET DFLTS",
            _scResetDefaultsButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

        y += smallBtnHeight + 8f;

        // Export button at bottom
        float buttonWidth = rightMargin - leftMargin;
        float buttonHeight = 32f;
        _scExportButtonBounds = new SKRect(leftMargin, y, rightMargin, y + buttonHeight);
        _scExportButtonHovered = _scExportButtonBounds.Contains(_mousePosition.X, _mousePosition.Y);

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
    {
        var bgColor = isHovered ? FUIColors.Background2.WithAlpha(150) : FUIColors.Background1.WithAlpha(80);
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        canvas.DrawRect(bounds, bgPaint);

        if (isHovered)
        {
            using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active.WithAlpha(150), StrokeWidth = 1f, IsAntialias = true };
            canvas.DrawRect(bounds, borderPaint);
        }

        float textY = bounds.MidY + 4;

        // vJoy label
        FUIRenderer.DrawText(canvas, $"vJoy {vjoyId}", new SKPoint(bounds.Left + 10, textY), FUIColors.TextPrimary, 11f);

        // Arrow
        FUIRenderer.DrawText(canvas, "→", new SKPoint(bounds.Left + 80, textY), FUIColors.TextDim, 11f);

        // SC instance
        var scColor = FUIColors.Active;
        FUIRenderer.DrawText(canvas, $"js{scInstance}", new SKPoint(bounds.Left + 110, textY), scColor, 11f, true);

        // Click hint
        if (isHovered)
        {
            FUIRenderer.DrawText(canvas, "click to change", new SKPoint(bounds.Right - 90, textY), FUIColors.TextDim, 9f);
        }
    }

    private void DrawVJoyMappingRowCompact(SKCanvas canvas, SKRect bounds, uint vjoyId, int scInstance, bool isHovered)
    {
        if (isHovered)
        {
            using var hoverPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2.WithAlpha(100), IsAntialias = true };
            canvas.DrawRect(bounds, hoverPaint);
        }

        float textY = bounds.MidY + 4;
        FUIRenderer.DrawText(canvas, $"vJoy {vjoyId}", new SKPoint(bounds.Left + 5, textY), FUIColors.TextPrimary, 10f);
        FUIRenderer.DrawText(canvas, "→", new SKPoint(bounds.Left + 60, textY), FUIColors.TextDim, 10f);
        FUIRenderer.DrawText(canvas, $"js{scInstance}", new SKPoint(bounds.Left + 80, textY), FUIColors.Active, 10f, true);
    }

    private void DrawExportButton(SKCanvas canvas, SKRect bounds, string text, bool isHovered, bool isEnabled)
    {
        var bgColor = isEnabled
            ? (isHovered ? FUIColors.Active.WithAlpha(180) : FUIColors.Active.WithAlpha(120))
            : FUIColors.Background2.WithAlpha(100);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        using var path = FUIRenderer.CreateFrame(bounds, 4f);
        canvas.DrawPath(path, bgPaint);

        var borderColor = isEnabled ? FUIColors.Active : FUIColors.Frame.WithAlpha(100);
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = borderColor, StrokeWidth = 1.5f, IsAntialias = true };
        canvas.DrawPath(path, borderPaint);

        var textColor = isEnabled ? FUIColors.TextBright : FUIColors.TextDim;
        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 12f);
    }

    private void DrawImportButton(SKCanvas canvas, SKRect bounds, string text, bool isHovered, bool isEnabled)
    {
        var bgColor = isEnabled
            ? (isHovered ? FUIColors.Primary.WithAlpha(150) : FUIColors.Primary.WithAlpha(80))
            : FUIColors.Background2.WithAlpha(100);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        using var path = FUIRenderer.CreateFrame(bounds, 4f);
        canvas.DrawPath(path, bgPaint);

        var borderColor = isEnabled ? FUIColors.Primary : FUIColors.Frame.WithAlpha(100);
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = borderColor, StrokeWidth = 1.5f, IsAntialias = true };
        canvas.DrawPath(path, borderPaint);

        var textColor = isEnabled ? FUIColors.TextPrimary : FUIColors.TextDim;
        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 11f);

        // Draw dropdown arrow
        if (isEnabled)
        {
            float arrowX = bounds.Right - 16;
            float arrowY = bounds.MidY;
            using var arrowPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = textColor, StrokeWidth = 1.5f, IsAntialias = true };
            canvas.DrawLine(arrowX - 4, arrowY - 2, arrowX, arrowY + 2, arrowPaint);
            canvas.DrawLine(arrowX, arrowY + 2, arrowX + 4, arrowY - 2, arrowPaint);
        }
    }

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

            bool isHovered = itemBounds.Contains(_mousePosition.X, _mousePosition.Y);
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

        bool allHovered = _scHoveredActionMapFilter == -1 && allItemBounds.Contains(_mousePosition.X, _mousePosition.Y);
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
                if (_hoveredSCInstallation >= 0 && _hoveredSCInstallation < _scInstallations.Count)
                {
                    _selectedSCInstallation = _hoveredSCInstallation;
                    LoadSCSchema(_scInstallations[_selectedSCInstallation]);
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
            for (int idx = 0; idx < _devices.Count; idx++)
            {
                var device = _devices[idx];
                if (device.IsVirtual || !device.IsConnected) continue;

                var state = _inputService.GetDeviceState(idx);
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
        for (int idx = 0; idx < _devices.Count; idx++)
        {
            var device = _devices[idx];
            if (device.IsVirtual || !device.IsConnected) continue;

            var state = _inputService.GetDeviceState(idx);
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
        var profile = _profileService.ActiveProfile;
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
            dialog.ShowDialog(this);

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
        catch (Exception ex)
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

        if (dialog.ShowDialog(this) == DialogResult.Yes)
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

        if (dialog.ShowDialog(this) == DialogResult.Yes)
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
        using var dialog = new Form
        {
            Text = "Export Profile Name",
            Width = 320,
            Height = 140,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(20, 22, 30)
        };

        var label = new Label
        {
            Text = "Profile Name:",
            Left = 16,
            Top = 16,
            Width = 280,
            ForeColor = Color.FromArgb(180, 190, 210)
        };

        var textBox = new TextBox
        {
            Text = _scExportProfile.ProfileName,
            Left = 16,
            Top = 40,
            Width = 280,
            BackColor = Color.FromArgb(30, 35, 45),
            ForeColor = Color.White
        };

        var okButton = new Button
        {
            Text = "OK",
            Left = 130,
            Top = 70,
            Width = 75,
            DialogResult = DialogResult.OK,
            BackColor = Color.FromArgb(40, 50, 70)
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            Left = 210,
            Top = 70,
            Width = 75,
            DialogResult = DialogResult.Cancel,
            BackColor = Color.FromArgb(40, 50, 70)
        };

        dialog.Controls.AddRange(new Control[] { label, textBox, okButton, cancelButton });
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(textBox.Text))
        {
            _scExportProfile.ProfileName = textBox.Text.Trim();
        }
    }

    private void AssignSCBinding()
    {
        if (_scSelectedActionIndex < 0 || _scFilteredActions is null || _scSelectedActionIndex >= _scFilteredActions.Count)
            return;

        var action = _scFilteredActions[_scSelectedActionIndex];
        var availableVJoy = _vjoyDevices.Where(v => v.Exists).ToList();

        if (availableVJoy.Count == 0)
        {
            using var warningDialog = new FUIConfirmDialog(
                "No vJoy",
                "No vJoy devices available.\nPlease configure vJoy.",
                "OK", "Cancel");
            warningDialog.ShowDialog(this);
            return;
        }

        using var dialog = new SCAssignmentDialog(action, availableVJoy);

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var vjoyId = dialog.SelectedVJoyId;
            var inputName = dialog.SelectedInputName;

            // Check for conflicts - is this input already bound to another action?
            var conflicts = FindSCBindingConflicts(vjoyId, inputName, action.ActionMap, action.ActionName);

            if (conflicts.Count > 0)
            {
                var conflictResult = ShowSCBindingConflictDialog(conflicts, vjoyId, inputName);

                if (conflictResult == SCConflictResolution.Cancel)
                {
                    _scAssigningInput = false;
                    return;
                }
                else if (conflictResult == SCConflictResolution.ReplaceAll)
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

    private enum SCConflictResolution { Cancel, ApplyAnyway, ReplaceAll }

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

    private SCConflictResolution ShowSCBindingConflictDialog(List<SCActionBinding> conflicts, uint vjoyId, string inputName)
    {
        string scInput = $"js{_scExportProfile.GetSCInstance(vjoyId)}_{inputName}";

        // Build conflict list message
        var conflictList = string.Join("\n",
            conflicts.Select(c => $"  • {FormatActionMapName(c.ActionMap)}: {c.ActionName}"));

        string message = $"The input '{scInput}' is already bound to:\n\n{conflictList}\n\n" +
                         "What would you like to do?";

        using var dialog = new Form
        {
            Text = "Binding Conflict",
            Width = 420,
            Height = 220 + Math.Min(conflicts.Count * 16, 64),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(30, 25, 25)
        };

        var msgLabel = new Label
        {
            Text = message,
            Left = 16,
            Top = 16,
            Width = 380,
            Height = 100 + Math.Min(conflicts.Count * 16, 64),
            ForeColor = Color.FromArgb(200, 180, 170)
        };

        int buttonY = msgLabel.Bottom + 16;

        var replaceButton = new Button
        {
            Text = "Replace Existing",
            Left = 16,
            Top = buttonY,
            Width = 120,
            Height = 30,
            BackColor = Color.FromArgb(100, 60, 40),
            ForeColor = Color.White
        };
        replaceButton.Click += (_, _) => { dialog.Tag = SCConflictResolution.ReplaceAll; dialog.Close(); };

        var allowButton = new Button
        {
            Text = "Allow Duplicate",
            Left = 145,
            Top = buttonY,
            Width = 120,
            Height = 30,
            BackColor = Color.FromArgb(60, 80, 60),
            ForeColor = Color.White
        };
        allowButton.Click += (_, _) => { dialog.Tag = SCConflictResolution.ApplyAnyway; dialog.Close(); };

        var cancelButton = new Button
        {
            Text = "Cancel",
            Left = 275,
            Top = buttonY,
            Width = 100,
            Height = 30,
            DialogResult = DialogResult.Cancel,
            BackColor = Color.FromArgb(50, 50, 55),
            ForeColor = Color.White
        };

        dialog.Controls.AddRange(new Control[] { msgLabel, replaceButton, allowButton, cancelButton });
        dialog.CancelButton = cancelButton;

        dialog.Tag = SCConflictResolution.Cancel;
        dialog.ShowDialog(this);

        return (SCConflictResolution)(dialog.Tag ?? SCConflictResolution.Cancel);
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
    {
        // Background
        var bgColor = focused ? FUIColors.Background2.WithAlpha(180) : FUIColors.Background2.WithAlpha(100);
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 4f, 4f, bgPaint);

        // Border
        var borderColor = focused ? FUIColors.Active : FUIColors.Frame;
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = borderColor, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 4f, 4f, borderPaint);

        // Search icon
        float iconX = bounds.Left + 8f;
        float iconY = bounds.MidY;
        using var iconPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.TextDim, StrokeWidth = 1.5f, IsAntialias = true };
        canvas.DrawCircle(iconX + 5, iconY - 1, 5f, iconPaint);
        canvas.DrawLine(iconX + 9, iconY + 3, iconX + 13, iconY + 7, iconPaint);

        // Text or placeholder
        float textX = bounds.Left + 24f;
        float textY = bounds.MidY + 4f;
        if (string.IsNullOrEmpty(text))
        {
            FUIRenderer.DrawText(canvas, "Search actions...", new SKPoint(textX, textY), FUIColors.TextDim, 10f);
        }
        else
        {
            FUIRenderer.DrawText(canvas, text, new SKPoint(textX, textY), FUIColors.TextPrimary, 10f);

            // Clear button (X)
            if (bounds.Contains(_mousePosition.X, _mousePosition.Y))
            {
                float clearX = bounds.Right - 18f;
                float clearY = bounds.MidY;
                using var clearPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.TextDim, StrokeWidth = 1.5f, IsAntialias = true };
                canvas.DrawLine(clearX - 4, clearY - 4, clearX + 4, clearY + 4, clearPaint);
                canvas.DrawLine(clearX + 4, clearY - 4, clearX - 4, clearY + 4, clearPaint);
            }
        }

        // Cursor when focused - position at end of text
        if (focused)
        {
            // Use FUIRenderer.MeasureText for consistent measurement with DrawText
            float cursorX = textX + (string.IsNullOrEmpty(text) ? 0 : FUIRenderer.MeasureText(text, 10f));
            if ((DateTime.Now.Millisecond / 500) % 2 == 0)
            {
                using var cursorPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active, StrokeWidth = 1f };
                canvas.DrawLine(cursorX, bounds.Top + 5, cursorX, bounds.Bottom - 5, cursorPaint);
            }
        }
    }

    private void DrawCollapseIndicator(SKCanvas canvas, float x, float y, bool isCollapsed, bool isHovered)
    {
        var color = isHovered ? FUIColors.TextBright : FUIColors.Primary;
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = color,
            IsAntialias = true
        };

        var path = new SKPath();
        if (isCollapsed)
        {
            // Right-pointing triangle (collapsed)
            path.MoveTo(x, y - 4);
            path.LineTo(x + 6, y);
            path.LineTo(x, y + 4);
        }
        else
        {
            // Down-pointing triangle (expanded)
            path.MoveTo(x - 2, y - 3);
            path.LineTo(x + 6, y - 3);
            path.LineTo(x + 2, y + 3);
        }
        path.Close();
        canvas.DrawPath(path, paint);
    }

    private void DrawSCCheckbox(SKCanvas canvas, SKRect bounds, bool isChecked, bool isHovered)
    {
        // Background
        var bgColor = isChecked ? FUIColors.Active.WithAlpha(60) : FUIColors.Background2.WithAlpha(100);
        if (isHovered) bgColor = bgColor.WithAlpha((byte)Math.Min(255, bgColor.Alpha + 40));
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, bgPaint);

        // Border
        var borderColor = isChecked ? FUIColors.Active : (isHovered ? FUIColors.FrameBright : FUIColors.Frame);
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = borderColor, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, borderPaint);

        // Checkmark
        if (isChecked)
        {
            using var checkPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active, StrokeWidth = 2f, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
            float cx = bounds.MidX;
            float cy = bounds.MidY;
            canvas.DrawLine(cx - 4, cy, cx - 1, cy + 3, checkPaint);
            canvas.DrawLine(cx - 1, cy + 3, cx + 4, cy - 3, checkPaint);
        }
    }

    private void DrawSCProfileDropdown(SKCanvas canvas, SKRect bounds, string text, bool hovered, bool open)
    {
        // Background
        var bgColor = open ? FUIColors.Active.WithAlpha(40) : (hovered ? FUIColors.Background2.WithAlpha(180) : FUIColors.Background2.WithAlpha(120));
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, bgPaint);

        // Border
        var borderColor = open ? FUIColors.Active : (hovered ? FUIColors.FrameBright : FUIColors.Frame);
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = borderColor, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, borderPaint);

        // Text
        string displayText = text.Length > 18 ? text.Substring(0, 15) + "..." : text;
        FUIRenderer.DrawText(canvas, displayText, new SKPoint(bounds.Left + 6, bounds.MidY + 4f), FUIColors.TextPrimary, 10f);

        // Dropdown arrow
        float arrowX = bounds.Right - 14f;
        float arrowY = bounds.MidY;
        using var arrowPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.TextDim, IsAntialias = true };
        using var arrowPath = new SKPath();
        arrowPath.MoveTo(arrowX - 4, arrowY - 2);
        arrowPath.LineTo(arrowX + 4, arrowY - 2);
        arrowPath.LineTo(arrowX, arrowY + 3);
        arrowPath.Close();
        canvas.DrawPath(arrowPath, arrowPaint);
    }

    private void DrawSCProfileDropdownWide(SKCanvas canvas, SKRect bounds, string text, bool hovered, bool open)
    {
        // Background
        var bgColor = open ? FUIColors.Active.WithAlpha(40) : (hovered ? FUIColors.Background2.WithAlpha(180) : FUIColors.Background2.WithAlpha(120));
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, bgPaint);

        // Border
        var borderColor = open ? FUIColors.Active : (hovered ? FUIColors.FrameBright : FUIColors.Frame);
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = borderColor, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, borderPaint);

        // Text - wider truncation
        float maxTextWidth = bounds.Width - 30f;  // Leave room for arrow
        string displayText = text;
        if (FUIRenderer.MeasureText(text, 10f) > maxTextWidth)
        {
            // Truncate to fit
            int len = text.Length;
            while (len > 0 && FUIRenderer.MeasureText(text.Substring(0, len) + "...", 10f) > maxTextWidth)
                len--;
            displayText = len > 0 ? text.Substring(0, len) + "..." : "...";
        }
        FUIRenderer.DrawText(canvas, displayText, new SKPoint(bounds.Left + 8, bounds.MidY + 4f), FUIColors.TextPrimary, 10f);

        // Dropdown arrow
        float arrowX = bounds.Right - 14f;
        float arrowY = bounds.MidY;
        using var arrowPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.TextDim, IsAntialias = true };
        using var arrowPath = new SKPath();
        arrowPath.MoveTo(arrowX - 4, arrowY - 2);
        arrowPath.LineTo(arrowX + 4, arrowY - 2);
        arrowPath.LineTo(arrowX, arrowY + 3);
        arrowPath.Close();
        canvas.DrawPath(arrowPath, arrowPaint);
    }

    private void DrawProfileRefreshButton(SKCanvas canvas, SKRect bounds, bool hovered)
    {
        var bgColor = hovered ? FUIColors.Active.WithAlpha(80) : FUIColors.Background2.WithAlpha(120);
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, bgPaint);

        var borderColor = hovered ? FUIColors.Active : FUIColors.Frame;
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = borderColor, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, borderPaint);

        // Refresh icon (circular arrow)
        float cx = bounds.MidX;
        float cy = bounds.MidY;
        float r = 5f;
        var iconColor = hovered ? FUIColors.TextBright : FUIColors.TextPrimary;
        using var iconPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = iconColor, StrokeWidth = 1.5f, IsAntialias = true, StrokeCap = SKStrokeCap.Round };

        using var arcPath = new SKPath();
        arcPath.AddArc(new SKRect(cx - r, cy - r, cx + r, cy + r), -45, 270);
        canvas.DrawPath(arcPath, iconPaint);

        // Arrow head
        using var arrowPath = new SKPath();
        arrowPath.MoveTo(cx + r - 1, cy - r + 2);
        arrowPath.LineTo(cx + r + 2, cy - r - 1);
        arrowPath.LineTo(cx + r + 1, cy - r + 3);
        canvas.DrawPath(arrowPath, iconPaint);
    }

    private void DrawTextButton(SKCanvas canvas, SKRect bounds, string text, bool hovered, bool disabled = false)
    {
        SKColor bgColor;
        if (disabled)
            bgColor = FUIColors.Background2.WithAlpha(60);
        else if (hovered)
            bgColor = FUIColors.Active.WithAlpha(80);
        else
            bgColor = FUIColors.Background2.WithAlpha(100);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, bgPaint);

        var borderColor = disabled ? FUIColors.Frame.WithAlpha(80) : (hovered ? FUIColors.Active : FUIColors.Frame);
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = borderColor, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, borderPaint);

        var textColor = disabled ? FUIColors.TextDim.WithAlpha(100) : (hovered ? FUIColors.TextBright : FUIColors.TextPrimary);
        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 9f);
    }

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

        // Section 1: Asteriq profiles
        for (int i = 0; i < _scExportProfiles.Count && y + rowHeight <= bounds.Bottom; i++)
        {
            var profile = _scExportProfiles[i];
            var rowBounds = new SKRect(bounds.Left + 4, y, bounds.Right - 4, y + rowHeight);
            bool isHovered = rowBounds.Contains(_mousePosition.X, _mousePosition.Y);
            bool isCurrent = profile.ProfileName == _scExportProfile.ProfileName;

            // FUI hover/selected style with accent bar
            if (isHovered)
            {
                _scHoveredProfileIndex = i;
                using var hoverPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(40), IsAntialias = true };
                canvas.DrawRect(rowBounds, hoverPaint);
                using var accentPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active, IsAntialias = true };
                canvas.DrawRect(new SKRect(rowBounds.Left, rowBounds.Top + 2, rowBounds.Left + 2, rowBounds.Bottom - 2), accentPaint);
            }
            else if (isCurrent)
            {
                using var selectPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(60), IsAntialias = true };
                canvas.DrawRect(new SKRect(rowBounds.Left, rowBounds.Top + 2, rowBounds.Left + 2, rowBounds.Bottom - 2), selectPaint);
            }

            var textColor = isCurrent ? FUIColors.Active : (isHovered ? FUIColors.TextBright : FUIColors.TextPrimary);
            float maxTextWidth = rowBounds.Width - 40f;
            string displayName = profile.ProfileName;
            displayName = TruncateTextToWidth(displayName, maxTextWidth, 10f);
            FUIRenderer.DrawText(canvas, displayName, new SKPoint(rowBounds.Left + 10, rowBounds.MidY + 4f), textColor, 10f);

            // Binding count badge
            if (profile.BindingCount > 0)
            {
                string countStr = profile.BindingCount.ToString();
                float badgeX = rowBounds.Right - 8 - FUIRenderer.MeasureText(countStr, 8f);
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

            // SC mapping files
            int scFileIndexOffset = _scExportProfiles.Count + 1000; // Use offset to distinguish from Asteriq profiles
            for (int i = 0; i < _scAvailableProfiles.Count && y + rowHeight <= bounds.Bottom; i++)
            {
                var scFile = _scAvailableProfiles[i];
                var rowBounds = new SKRect(bounds.Left + 4, y, bounds.Right - 4, y + rowHeight);
                bool isHovered = rowBounds.Contains(_mousePosition.X, _mousePosition.Y);

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
    {
        // Background
        SKColor bgColor;
        if (disabled)
            bgColor = FUIColors.Background2.WithAlpha(60);
        else if (hovered)
            bgColor = FUIColors.Active.WithAlpha(80);
        else
            bgColor = FUIColors.Background2.WithAlpha(120);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, bgPaint);

        // Border
        var borderColor = disabled ? FUIColors.Frame.WithAlpha(80) : (hovered ? FUIColors.Active : FUIColors.Frame);
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = borderColor, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, borderPaint);

        // Icon - use text-based icons for simplicity
        var iconColor = disabled ? FUIColors.TextDim.WithAlpha(100) : (hovered ? FUIColors.TextBright : FUIColors.TextPrimary);

        // Custom drawing for common icons
        if (icon == "💾" || tooltip == "Save")
        {
            // Disk icon
            float cx = bounds.MidX;
            float cy = bounds.MidY;
            using var iconPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = iconColor, StrokeWidth = 1.5f, IsAntialias = true };
            canvas.DrawRect(cx - 5, cy - 5, 10, 10, iconPaint);
            canvas.DrawLine(cx - 2, cy - 5, cx - 2, cy - 2, iconPaint);
            canvas.DrawLine(cx + 2, cy - 5, cx + 2, cy - 2, iconPaint);
        }
        else if (icon == "+")
        {
            // Plus icon
            float cx = bounds.MidX;
            float cy = bounds.MidY;
            using var iconPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = iconColor, StrokeWidth = 2f, IsAntialias = true };
            canvas.DrawLine(cx - 5, cy, cx + 5, cy, iconPaint);
            canvas.DrawLine(cx, cy - 5, cx, cy + 5, iconPaint);
        }
        else if (icon == "×")
        {
            // X icon
            float cx = bounds.MidX;
            float cy = bounds.MidY;
            using var iconPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = iconColor, StrokeWidth = 2f, IsAntialias = true };
            canvas.DrawLine(cx - 4, cy - 4, cx + 4, cy + 4, iconPaint);
            canvas.DrawLine(cx + 4, cy - 4, cx - 4, cy + 4, iconPaint);
        }
        else
        {
            // Fallback to text
            FUIRenderer.DrawTextCentered(canvas, icon, bounds, iconColor, 12f);
        }
    }

    #endregion

    #region SC Profile Management

    private void SaveSCExportProfile()
    {
        if (_scExportProfileService is null) return;

        _scExportProfileService.SaveProfile(_scExportProfile);
        _profileService.LastSCExportProfile = _scExportProfile.ProfileName;
        RefreshSCExportProfiles();

        _scExportStatus = $"Profile '{_scExportProfile.ProfileName}' saved";
        _scExportStatusTime = DateTime.Now;
    }

    private void CreateNewSCExportProfile()
    {
        using var dialog = new Form
        {
            Text = "New SC Export Profile",
            Width = 320,
            Height = 140,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(20, 22, 30)
        };

        var label = new Label
        {
            Text = "Profile Name:",
            Left = 16,
            Top = 16,
            Width = 280,
            ForeColor = Color.FromArgb(180, 190, 210)
        };

        var textBox = new TextBox
        {
            Text = "New Profile",
            Left = 16,
            Top = 40,
            Width = 280,
            BackColor = Color.FromArgb(30, 35, 45),
            ForeColor = Color.White
        };

        var okButton = new Button
        {
            Text = "Create",
            Left = 130,
            Top = 70,
            Width = 75,
            DialogResult = DialogResult.OK,
            BackColor = Color.FromArgb(40, 50, 70),
            ForeColor = Color.White
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            Left = 210,
            Top = 70,
            Width = 75,
            DialogResult = DialogResult.Cancel,
            BackColor = Color.FromArgb(40, 50, 70),
            ForeColor = Color.White
        };

        dialog.Controls.AddRange(new Control[] { label, textBox, okButton, cancelButton });
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(textBox.Text))
        {
            var newName = textBox.Text.Trim();

            // Check for duplicate name
            if (_scExportProfileService is not null && _scExportProfileService.ProfileExists(newName))
            {
                using var existsDialog = new FUIConfirmDialog(
                    "Profile Exists",
                    $"A profile named '{newName}' already exists.",
                    "OK", "Cancel");
                existsDialog.ShowDialog(this);
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
            foreach (var vjoy in _vjoyDevices.Where(v => v.Exists))
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
            $"Delete SC export profile '{_scExportProfile.ProfileName}'?\n\nThis cannot be undone.",
            "Delete", "Cancel");

        if (confirmDialog.ShowDialog(this) == DialogResult.Yes)
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
                    _profileService.LastSCExportProfile = nextProfile.ProfileName;
                }
            }
            else
            {
                // Create default profile
                _scExportProfile = new SCExportProfile { ProfileName = "asteriq" };
                foreach (var vjoy in _vjoyDevices.Where(v => v.Exists))
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
            _profileService.LastSCExportProfile = profileName;
            _scProfileDropdownOpen = false;
            _scExportStatus = $"Loaded profile '{profileName}'";
            _scExportStatusTime = DateTime.Now;
        }
    }

    private void ImportSCProfile(SCMappingFile mappingFile)
    {
        if (_scExportService is null) return;

        // Warn if there are existing bindings that would be replaced
        if (_scExportProfile.Bindings.Count > 0)
        {
            var result = FUIMessageBox.ShowQuestion(this,
                $"Profile '{_scExportProfile.ProfileName}' has {_scExportProfile.Bindings.Count} existing binding(s).\n\n" +
                "Import will replace all current bindings. Continue?",
                "Replace Bindings");

            if (!result)
                return;
        }

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

        // Save the profile
        _scExportProfileService?.SaveProfile(_scExportProfile);

        // Update conflicts
        UpdateConflictingBindings();

        // Log final profile stats
        var finalKb = _scExportProfile.Bindings.Count(b => b.DeviceType == SCDeviceType.Keyboard);
        var finalMo = _scExportProfile.Bindings.Count(b => b.DeviceType == SCDeviceType.Mouse);
        var finalJs = _scExportProfile.Bindings.Count(b => b.DeviceType == SCDeviceType.Joystick);
        System.Diagnostics.Debug.WriteLine($"[SCBindings] Profile after save: {finalKb} KB, {finalMo} Mouse, {finalJs} Joystick bindings");

        _scExportStatus = $"Imported {importResult.Bindings.Count} bindings ({jsCount} JS, {kbCount} KB, {moCount} Mouse)";
        _scExportStatusTime = DateTime.Now;

        System.Diagnostics.Debug.WriteLine($"[SCBindings] Imported {importResult.Bindings.Count} bindings from {mappingFile.FilePath}");
    }

    #endregion
}
