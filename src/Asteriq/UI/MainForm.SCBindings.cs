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
            _scInstallationService = new SCInstallationService();
            _scProfileCacheService = new SCProfileCacheService();
            _scSchemaService = new SCSchemaService();
            _scExportService = new SCXmlExportService();
            _scExportProfileService = new SCExportProfileService();

            RefreshSCInstallations();
            RefreshSCExportProfiles();

            // Try to load the last used SC export profile
            var lastProfileName = _profileService.LastSCExportProfile;
            SCExportProfile? loadedProfile = null;

            if (!string.IsNullOrEmpty(lastProfileName) && _profileService.AutoLoadLastSCExportProfile)
            {
                loadedProfile = _scExportProfileService.LoadProfile(lastProfileName);
            }

            if (loadedProfile != null)
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

            System.Diagnostics.Debug.WriteLine($"[MainForm] SC bindings initialized, {_scInstallations.Count} installations found");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] SC bindings init failed: {ex.Message}");
        }
    }

    private void RefreshSCExportProfiles()
    {
        if (_scExportProfileService == null) return;
        _scExportProfiles = _scExportProfileService.ListProfiles();
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
        if (_scInstallationService == null) return;

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
        if (_scProfileCacheService == null || _scSchemaService == null) return;

        try
        {
            var profile = _scProfileCacheService.GetOrExtractProfile(installation);
            if (profile != null)
            {
                _scActions = _scSchemaService.ParseActions(profile);
                _scExportProfile.TargetEnvironment = installation.Environment;
                _scExportProfile.TargetBuildId = installation.BuildId;

                // Build action maps list and filter to joystick-relevant actions
                _scActionMaps = _scActions
                    .Select(a => a.ActionMap)
                    .Distinct()
                    .OrderBy(m => m)
                    .ToList();

                // Default: show joystick-relevant actions only
                RefreshFilteredActions();

                System.Diagnostics.Debug.WriteLine($"[MainForm] Loaded {_scActions.Count} SC actions from {installation.Environment}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] Failed to load SC schema: {ex.Message}");
            _scActions = null;
            _scFilteredActions = null;
            _scActionMaps.Clear();
        }
    }

    private void RefreshFilteredActions()
    {
        if (_scActions == null || _scSchemaService == null)
        {
            _scFilteredActions = null;
            return;
        }

        // Start with joystick-relevant actions
        var actions = _scSchemaService.FilterJoystickActions(_scActions);

        // Apply action map filter if set
        if (!string.IsNullOrEmpty(_scActionMapFilter))
        {
            actions = actions.Where(a => a.ActionMap == _scActionMapFilter).ToList();
        }

        // Apply search filter if set
        if (!string.IsNullOrEmpty(_scSearchText))
        {
            var searchLower = _scSearchText.ToLowerInvariant();
            actions = actions.Where(a =>
                a.ActionName.ToLowerInvariant().Contains(searchLower) ||
                a.ActionMap.ToLowerInvariant().Contains(searchLower) ||
                FormatActionMapName(a.ActionMap).ToLowerInvariant().Contains(searchLower)
            ).ToList();
        }

        // Apply "show bound only" filter if enabled
        if (_scShowBoundOnly)
        {
            actions = actions.Where(a =>
                _scExportProfile.GetBinding(a.ActionMap, a.ActionName) != null
            ).ToList();
        }

        _scFilteredActions = actions.OrderBy(a => a.ActionMap).ThenBy(a => a.ActionName).ToList();
        _scBindingsScrollOffset = 0;  // Reset scroll when filter changes
        _scSelectedActionIndex = -1;  // Clear selection
    }

    #endregion

    #region SC Bindings Tab Drawing

    private void DrawBindingsTabContent(SKCanvas canvas, SKRect bounds, float pad, float contentTop, float contentBottom)
    {
        float frameInset = 5f;
        var contentBounds = new SKRect(pad, contentTop, bounds.Right - pad, contentBottom);

        // Three-panel layout: Left (installation) | Center (bindings table) | Right (export)
        float leftPanelWidth = 280f;
        float rightPanelWidth = 260f;
        float gap = 10f;
        float centerPanelWidth = contentBounds.Width - leftPanelWidth - rightPanelWidth - gap * 2;

        var leftBounds = new SKRect(contentBounds.Left, contentBounds.Top,
            contentBounds.Left + leftPanelWidth, contentBounds.Bottom);
        var centerBounds = new SKRect(leftBounds.Right + gap, contentBounds.Top,
            leftBounds.Right + gap + centerPanelWidth, contentBounds.Bottom);
        var rightBounds = new SKRect(centerBounds.Right + gap, contentBounds.Top,
            contentBounds.Right, contentBounds.Bottom);

        // LEFT PANEL - SC Installation (condensed)
        DrawSCInstallationPanelCompact(canvas, leftBounds, frameInset);

        // CENTER PANEL - SC Action Bindings Table
        DrawSCBindingsTablePanel(canvas, centerBounds, frameInset);

        // RIGHT PANEL - vJoy Mapping & Export
        DrawSCExportPanelCompact(canvas, rightBounds, frameInset);

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
        float rightMargin = bounds.Right - frameInset - 15;
        float lineHeight = FUIRenderer.ScaleLineHeight(20f);

        // Title
        FUIRenderer.DrawText(canvas, "STAR CITIZEN INSTALLATION", new SKPoint(leftMargin, y), FUIColors.TextBright, 14f, true);
        y += FUIRenderer.ScaleLineHeight(35f);

        // Installation selector
        FUIRenderer.DrawText(canvas, "INSTALLATION", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += lineHeight;

        float selectorHeight = 28f;
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
            if (_scActions != null)
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
        y += 15f;
        float buttonWidth = 120f;
        float buttonHeight = 28f;
        _scRefreshButtonBounds = new SKRect(leftMargin, y, leftMargin + buttonWidth, y + buttonHeight);
        _scRefreshButtonHovered = _scRefreshButtonBounds.Contains(_mousePosition.X, _mousePosition.Y);
        FUIRenderer.DrawButton(canvas, _scRefreshButtonBounds, "REFRESH",
            _scRefreshButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

        // Export Profile Name section
        y += buttonHeight + 30f;
        FUIRenderer.DrawText(canvas, "EXPORT PROFILE", new SKPoint(leftMargin, y), FUIColors.TextBright, 14f, true);
        y += FUIRenderer.ScaleLineHeight(30f);

        FUIRenderer.DrawText(canvas, "PROFILE NAME", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += lineHeight;

        float nameFieldHeight = 28f;
        _scProfileNameBounds = new SKRect(leftMargin, y, rightMargin, y + nameFieldHeight);
        _scProfileNameHovered = _scProfileNameBounds.Contains(_mousePosition.X, _mousePosition.Y);
        DrawTextFieldReadOnly(canvas, _scProfileNameBounds, _scExportProfile.ProfileName, _scProfileNameHovered);
        y += nameFieldHeight + 10f;

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
        float rightMargin = bounds.Right - frameInset - 15;
        float lineHeight = FUIRenderer.ScaleLineHeight(20f);

        // Title
        FUIRenderer.DrawText(canvas, "VJOY TO SC MAPPING", new SKPoint(leftMargin, y), FUIColors.TextBright, 14f, true);
        y += FUIRenderer.ScaleLineHeight(35f);

        // Description
        FUIRenderer.DrawText(canvas, "Map vJoy devices to Star Citizen joystick instances (js1, js2, etc.)",
            new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += lineHeight + 10f;

        // vJoy mappings
        _scVJoyMappingBounds.Clear();
        float rowHeight = 32f;
        float rowGap = 6f;

        // Get available vJoy devices
        var availableVJoy = _vjoyDevices.Where(v => v.Exists).ToList();

        if (availableVJoy.Count == 0)
        {
            FUIRenderer.DrawText(canvas, "No vJoy devices available.", new SKPoint(leftMargin, y), FUIColors.TextDim, 11f);
            y += lineHeight;
            FUIRenderer.DrawText(canvas, "Configure vJoy devices to map them to SC.", new SKPoint(leftMargin, y), FUIColors.TextDim, 11f);
            y += lineHeight * 2;
        }
        else
        {
            foreach (var vjoy in availableVJoy.Take(8)) // Limit to 8 devices
            {
                var rowBounds = new SKRect(leftMargin, y, rightMargin, y + rowHeight);
                _scVJoyMappingBounds.Add(rowBounds);

                bool isHovered = rowBounds.Contains(_mousePosition.X, _mousePosition.Y);
                int scInstance = _scExportProfile.GetSCInstance(vjoy.Id);

                DrawVJoyMappingRow(canvas, rowBounds, vjoy.Id, scInstance, isHovered);
                y += rowHeight + rowGap;
            }
        }

        // Export section
        y += 20f;
        FUIRenderer.DrawText(canvas, "EXPORT", new SKPoint(leftMargin, y), FUIColors.TextBright, 14f, true);
        y += FUIRenderer.ScaleLineHeight(30f);

        // Export path preview
        if (_scInstallations.Count > 0 && _selectedSCInstallation < _scInstallations.Count && _scExportService != null)
        {
            var installation = _scInstallations[_selectedSCInstallation];
            string exportPath = _scExportService.GetExportPath(_scExportProfile, installation);

            FUIRenderer.DrawText(canvas, "EXPORT PATH", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
            y += lineHeight;
            FUIRenderer.DrawText(canvas, TruncatePath(exportPath, 55), new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
            y += lineHeight + 15f;
        }

        // Export button
        float buttonWidth = 200f;
        float buttonHeight = 36f;
        float buttonX = leftMargin + (rightMargin - leftMargin - buttonWidth) / 2;
        _scExportButtonBounds = new SKRect(buttonX, y, buttonX + buttonWidth, y + buttonHeight);
        _scExportButtonHovered = _scExportButtonBounds.Contains(_mousePosition.X, _mousePosition.Y);

        bool canExport = _scInstallations.Count > 0 && _scExportProfile.VJoyToSCInstance.Count > 0;
        DrawExportButton(canvas, _scExportButtonBounds, "EXPORT TO SC", _scExportButtonHovered, canExport);
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
        float dropdownHeight = Math.Min(_scInstallations.Count * itemHeight + 4, 200f);

        _scInstallationDropdownBounds = new SKRect(
            _scInstallationSelectorBounds.Left,
            _scInstallationSelectorBounds.Bottom + 2,
            _scInstallationSelectorBounds.Right,
            _scInstallationSelectorBounds.Bottom + 2 + dropdownHeight);

        // Shadow
        FUIRenderer.DrawPanelShadow(canvas, _scInstallationDropdownBounds, 2f, 2f, 8f);

        // Background
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1.WithAlpha(240), IsAntialias = true };
        canvas.DrawRect(_scInstallationDropdownBounds, bgPaint);

        // Border
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRect(_scInstallationDropdownBounds, borderPaint);

        // Items
        float y = _scInstallationDropdownBounds.Top + 2;
        for (int i = 0; i < _scInstallations.Count; i++)
        {
            var itemBounds = new SKRect(_scInstallationDropdownBounds.Left + 2, y,
                _scInstallationDropdownBounds.Right - 2, y + itemHeight);

            bool isHovered = i == _hoveredSCInstallation;
            bool isSelected = i == _selectedSCInstallation;

            if (isHovered || isSelected)
            {
                var highlightColor = isSelected ? FUIColors.Active.WithAlpha(60) : FUIColors.Background2.WithAlpha(150);
                using var highlightPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = highlightColor, IsAntialias = true };
                canvas.DrawRect(itemBounds, highlightPaint);
            }

            var textColor = isSelected ? FUIColors.Active : (isHovered ? FUIColors.TextBright : FUIColors.TextPrimary);
            FUIRenderer.DrawText(canvas, _scInstallations[i].DisplayName,
                new SKPoint(itemBounds.Left + 10, itemBounds.MidY + 4), textColor, 11f);

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
        float lineHeight = FUIRenderer.ScaleLineHeight(18f);

        // Title
        FUIRenderer.DrawText(canvas, "SC INSTALLATION", new SKPoint(leftMargin, y), FUIColors.TextBright, 12f, true);
        y += FUIRenderer.ScaleLineHeight(28f);

        // Installation selector
        float selectorHeight = 26f;
        _scInstallationSelectorBounds = new SKRect(leftMargin, y, rightMargin, y + selectorHeight);

        string installationText = _scInstallations.Count > 0 && _selectedSCInstallation < _scInstallations.Count
            ? _scInstallations[_selectedSCInstallation].DisplayName
            : "No SC found";

        bool selectorHovered = _scInstallationSelectorBounds.Contains(_mousePosition.X, _mousePosition.Y);
        DrawSelector(canvas, _scInstallationSelectorBounds, installationText, selectorHovered || _scInstallationDropdownOpen, _scInstallations.Count > 0);
        y += selectorHeight + 8f;

        // Brief details
        if (_scInstallations.Count > 0 && _selectedSCInstallation < _scInstallations.Count)
        {
            var installation = _scInstallations[_selectedSCInstallation];
            FUIRenderer.DrawText(canvas, installation.Environment, new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
            y += lineHeight;

            if (_scActions != null)
            {
                var joystickActions = _scSchemaService?.FilterJoystickActions(_scActions);
                FUIRenderer.DrawText(canvas, $"{joystickActions?.Count ?? 0} bindable actions", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
                y += lineHeight;
            }
        }

        // Refresh button (compact)
        y = bounds.Bottom - frameInset - 35f;
        float buttonWidth = 80f;
        float buttonHeight = 24f;
        _scRefreshButtonBounds = new SKRect(leftMargin, y, leftMargin + buttonWidth, y + buttonHeight);
        _scRefreshButtonHovered = _scRefreshButtonBounds.Contains(_mousePosition.X, _mousePosition.Y);
        FUIRenderer.DrawButton(canvas, _scRefreshButtonBounds, "REFRESH",
            _scRefreshButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
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
        float rightMargin = bounds.Right - frameInset - 15;
        float lineHeight = FUIRenderer.ScaleLineHeight(18f);

        // Title
        FUIRenderer.DrawText(canvas, "SC ACTIONS", new SKPoint(leftMargin, y), FUIColors.TextBright, 12f, true);

        // Category filter dropdown (right side of title)
        float filterWidth = 160f;
        float filterHeight = 24f;
        float filterX = rightMargin - filterWidth;
        _scActionMapFilterBounds = new SKRect(filterX, y - 4f, rightMargin, y - 4f + filterHeight);

        string filterText = string.IsNullOrEmpty(_scActionMapFilter) ? "All Categories" : FormatActionMapName(_scActionMapFilter);
        bool filterHovered = _scActionMapFilterBounds.Contains(_mousePosition.X, _mousePosition.Y);
        DrawSelector(canvas, _scActionMapFilterBounds, filterText, filterHovered || _scActionMapFilterDropdownOpen, _scActionMaps.Count > 0);

        y += FUIRenderer.ScaleLineHeight(28f);

        // Search box and "Show Bound Only" checkbox row
        float searchWidth = bounds.Width * 0.35f;
        float searchHeight = 24f;
        _scSearchBoxBounds = new SKRect(leftMargin, y, leftMargin + searchWidth, y + searchHeight);
        DrawSearchBox(canvas, _scSearchBoxBounds, _scSearchText, _scSearchBoxFocused);

        // "Show Bound Only" checkbox (to the right of search)
        float checkboxSize = 16f;
        float checkboxX = _scSearchBoxBounds.Right + 15f;
        _scShowBoundOnlyBounds = new SKRect(checkboxX, y + (searchHeight - checkboxSize) / 2,
            checkboxX + checkboxSize, y + (searchHeight + checkboxSize) / 2);
        _scShowBoundOnlyHovered = _scShowBoundOnlyBounds.Contains(_mousePosition.X, _mousePosition.Y);
        DrawSCCheckbox(canvas, _scShowBoundOnlyBounds, _scShowBoundOnly, _scShowBoundOnlyHovered);
        FUIRenderer.DrawText(canvas, "Bound Only", new SKPoint(checkboxX + checkboxSize + 6, y + searchHeight / 2 + 4),
            _scShowBoundOnly ? FUIColors.Active : FUIColors.TextDim, 10f);

        // Action count
        int actionCount = _scFilteredActions?.Count ?? 0;
        int boundCount = _scFilteredActions?.Count(a => _scExportProfile.GetBinding(a.ActionMap, a.ActionName) != null) ?? 0;
        string countText = $"{actionCount} actions, {boundCount} bound";
        FUIRenderer.DrawText(canvas, countText, new SKPoint(rightMargin - 120, y + searchHeight / 2 + 4), FUIColors.TextDim, 9f);

        y += searchHeight + 12f;

        // Get dynamic columns and cache them for mouse handling
        var columns = GetSCGridColumns();
        _scGridColumns = columns;

        // Column layout - calculate widths based on available space
        float totalWidth = rightMargin - leftMargin;
        float deviceColWidth = _scGridDeviceColWidth;
        float totalDeviceColsWidth = columns.Count * deviceColWidth;

        // Action column gets remaining space after device columns, with minimum width
        float actionColWidth = Math.Max(_scGridActionColWidth, totalWidth - totalDeviceColsWidth - 15f);
        // But cap it so we don't squeeze device columns too much
        actionColWidth = Math.Min(actionColWidth, totalWidth * 0.45f);

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
        float headerRowHeight = 22f;
        float headerTextY = y + headerRowHeight / 2 + 4f;  // Vertically centered

        // Table header background
        using var headerPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2.WithAlpha(120), IsAntialias = true };
        canvas.DrawRect(new SKRect(leftMargin - 5, y, rightMargin + 5, y + headerRowHeight), headerPaint);

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
            float colX = deviceColsStart + c * deviceColWidth - _scGridHorizontalScroll;
            if (colX + deviceColWidth > deviceColsStart && colX < deviceColsStart + visibleDeviceWidth)
            {
                var col = columns[c];
                var headerColor = col.IsKeyboard ? FUIColors.Primary :
                                  col.IsMouse ? FUIColors.Warning :
                                  FUIColors.Success;

                // Center the header text in the column
                float headerTextWidth = FUIRenderer.MeasureText(col.Header, 9f);
                float centeredX = colX + (deviceColWidth - headerTextWidth) / 2;
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
        float rowHeight = 24f;
        float rowGap = 2f;
        float scrollY = listTop - _scBindingsScrollOffset;

        _scCategoryHeaderBounds.Clear();

        if (_scFilteredActions == null || _scFilteredActions.Count == 0)
        {
            FUIRenderer.DrawText(canvas, _scActions == null ? "Loading actions..." : "No actions match filter",
                new SKPoint(leftMargin, scrollY + 20f), FUIColors.TextDim, 11f);
        }
        else
        {
            string? lastActionMap = null;
            float categoryHeaderHeight = 28f;

            for (int i = 0; i < _scFilteredActions.Count; i++)
            {
                var action = _scFilteredActions[i];

                // Category header when action map changes
                if (action.ActionMap != lastActionMap)
                {
                    lastActionMap = action.ActionMap;
                    bool isCollapsed = _scCollapsedCategories.Contains(action.ActionMap);

                    // Store header bounds for click detection
                    var headerBounds = new SKRect(leftMargin - 5, scrollY, rightMargin + 5, scrollY + categoryHeaderHeight - 2);
                    _scCategoryHeaderBounds[action.ActionMap] = headerBounds;

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

                        // Category name
                        string categoryName = FormatActionMapName(action.ActionMap);
                        int categoryActionCount = _scFilteredActions.Count(a => a.ActionMap == action.ActionMap);
                        int categoryBoundCount = _scFilteredActions.Count(a =>
                            a.ActionMap == action.ActionMap &&
                            _scExportProfile.GetBinding(a.ActionMap, a.ActionName) != null);

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
                        // Skip to next category
                        while (i < _scFilteredActions.Count - 1 &&
                               _scFilteredActions[i + 1].ActionMap == action.ActionMap)
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

                    // Row background
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

                    float textY = scrollY + rowHeight / 2 + 4;

                    // Draw action name with ellipsis if too long
                    float actionIndent = 18f;
                    string displayName = action.ActionName;
                    float maxNameWidth = actionColWidth - actionIndent - 10f;
                    displayName = TruncateTextToWidth(displayName, maxNameWidth, 10f);
                    var nameColor = isSelected ? FUIColors.Active : FUIColors.TextPrimary;
                    FUIRenderer.DrawText(canvas, displayName, new SKPoint(leftMargin + actionIndent, textY), nameColor, 10f);

                    // Draw device column cells (clipped)
                    canvas.Save();
                    canvas.ClipRect(new SKRect(deviceColsStart, scrollY, deviceColsStart + visibleDeviceWidth, scrollY + rowHeight));

                    for (int c = 0; c < columns.Count; c++)
                    {
                        float colX = deviceColsStart + c * deviceColWidth - _scGridHorizontalScroll;
                        if (colX + deviceColWidth > deviceColsStart && colX < deviceColsStart + visibleDeviceWidth)
                        {
                            var col = columns[c];
                            var cellBounds = new SKRect(colX, scrollY, colX + deviceColWidth, scrollY + rowHeight);

                            // Check cell state
                            bool isCellHovered = _scHoveredCell == (i, c);
                            bool isCellSelected = _scSelectedCell == (i, c);
                            bool isCellListening = _scIsListeningForInput && _scSelectedCell == (i, c);

                            // Draw cell background for hover/selection/listening states
                            if (isCellListening)
                            {
                                // Listening state - pulsing warning color
                                float pulse = (float)(0.5 + 0.3 * Math.Sin((DateTime.Now - _scListeningStartTime).TotalMilliseconds / 200.0));
                                using var listeningPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Warning.WithAlpha((byte)(80 * pulse)), IsAntialias = true };
                                canvas.DrawRect(cellBounds, listeningPaint);
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

                            string? bindingText = null;
                            SKColor textColor = FUIColors.TextDim;
                            bool isDefault = false;

                            // Check for user binding (from export profile) for joystick columns
                            if (col.IsJoystick)
                            {
                                var userBinding = _scExportProfile.GetBinding(action.ActionMap, action.ActionName);
                                if (userBinding != null && _scExportProfile.GetSCInstance(userBinding.VJoyDevice) == col.SCInstance)
                                {
                                    bindingText = FormatBindingForCell(userBinding.InputName, userBinding.Modifiers);
                                    textColor = FUIColors.Success;
                                }
                            }

                            // Check for default binding (from SC's defaultProfile.xml)
                            if (bindingText == null && action.DefaultBindings.Count > 0)
                            {
                                var defaultBinding = action.DefaultBindings.FirstOrDefault(b =>
                                    b.DevicePrefix.StartsWith(col.DevicePrefix, StringComparison.OrdinalIgnoreCase) ||
                                    (col.IsKeyboard && b.DevicePrefix.StartsWith("kb", StringComparison.OrdinalIgnoreCase)) ||
                                    (col.IsMouse && b.DevicePrefix.StartsWith("mo", StringComparison.OrdinalIgnoreCase)));

                                if (defaultBinding != null)
                                {
                                    bindingText = FormatBindingForCell(defaultBinding.Input, defaultBinding.Modifiers);
                                    textColor = col.IsKeyboard ? FUIColors.Primary.WithAlpha(180) :
                                               col.IsMouse ? FUIColors.Warning.WithAlpha(180) :
                                               FUIColors.TextDim;
                                    isDefault = true;
                                }
                            }

                            // Draw cell content
                            if (isCellListening)
                            {
                                // Show "Press..." text when listening
                                FUIRenderer.DrawText(canvas, "Press...", new SKPoint(colX + 5, textY), FUIColors.Warning, 9f);
                            }
                            else if (!string.IsNullOrEmpty(bindingText))
                            {
                                // Draw keycap-style badge for binding
                                DrawBindingBadge(canvas, colX + 3, textY - 10, deviceColWidth - 6, bindingText, textColor, isDefault);
                            }
                            else
                            {
                                // Draw empty indicator
                                FUIRenderer.DrawText(canvas, "—", new SKPoint(colX + deviceColWidth / 2 - 4, textY), FUIColors.TextDim.WithAlpha(100), 10f);
                            }

                            // Draw column separator
                            using var sepPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame.WithAlpha(40), StrokeWidth = 1 };
                            canvas.DrawLine(colX, scrollY, colX, scrollY + rowHeight, sepPaint);

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
        if (_scBindingsContentHeight > _scBindingsListBounds.Height)
        {
            float scrollbarWidth = 6f;
            float scrollbarX = rightMargin - scrollbarWidth + 10;
            float scrollbarHeight = _scBindingsListBounds.Height;
            float thumbHeight = Math.Max(30f, scrollbarHeight * (_scBindingsListBounds.Height / _scBindingsContentHeight));
            float thumbY = listTop + (_scBindingsScrollOffset / (_scBindingsContentHeight - _scBindingsListBounds.Height)) * (scrollbarHeight - thumbHeight);

            using var trackPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2.WithAlpha(80), IsAntialias = true };
            canvas.DrawRoundRect(new SKRect(scrollbarX, listTop, scrollbarX + scrollbarWidth, listTop + scrollbarHeight), 3f, 3f, trackPaint);

            using var thumbPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Frame.WithAlpha(180), IsAntialias = true };
            canvas.DrawRoundRect(new SKRect(scrollbarX, thumbY, scrollbarX + scrollbarWidth, thumbY + thumbHeight), 3f, 3f, thumbPaint);
        }

        // Horizontal scrollbar if needed
        if (needsHorizontalScroll)
        {
            float scrollbarHeight = 6f;
            float scrollbarY = listBottom + 5f;
            float scrollbarWidth = visibleDeviceWidth;
            float thumbWidth = Math.Max(30f, scrollbarWidth * (visibleDeviceWidth / totalDeviceColsWidth));
            float maxHScroll = totalDeviceColsWidth - visibleDeviceWidth;
            float thumbX = deviceColsStart + (_scGridHorizontalScroll / maxHScroll) * (scrollbarWidth - thumbWidth);

            using var trackPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2.WithAlpha(80), IsAntialias = true };
            canvas.DrawRoundRect(new SKRect(deviceColsStart, scrollbarY, deviceColsStart + scrollbarWidth, scrollbarY + scrollbarHeight), 3f, 3f, trackPaint);

            using var thumbPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Frame.WithAlpha(180), IsAntialias = true };
            canvas.DrawRoundRect(new SKRect(thumbX, scrollbarY, thumbX + thumbWidth, scrollbarY + scrollbarHeight), 3f, 3f, thumbPaint);
        }
    }

    /// <summary>
    /// Formats a binding input for display in a grid cell (with modifiers)
    /// </summary>
    private string FormatBindingForCell(string input, List<string>? modifiers)
    {
        if (modifiers == null || modifiers.Count == 0)
            return FormatInputName(input);

        var modStr = string.Join("+", modifiers
            .Select(m => FormatModifierName(m))
            .Where(m => !string.IsNullOrEmpty(m)));

        if (string.IsNullOrEmpty(modStr))
            return FormatInputName(input);

        return $"{modStr}+{FormatInputName(input)}";
    }

    /// <summary>
    /// Formats a modifier name for display (e.g., "lshift" -> "SHFT", "lctrl" -> "CTRL")
    /// </summary>
    private string FormatModifierName(string modifier)
    {
        if (string.IsNullOrEmpty(modifier))
            return "";

        // Remove left/right prefix and uppercase
        var cleaned = modifier
            .Replace("lshift", "SHFT")
            .Replace("rshift", "SHFT")
            .Replace("lctrl", "CTRL")
            .Replace("rctrl", "CTRL")
            .Replace("lalt", "ALT")
            .Replace("ralt", "ALT");

        // If no known replacement matched, try generic cleanup
        if (cleaned == modifier)
        {
            cleaned = modifier.TrimStart('l', 'r').ToUpper();
            if (cleaned.Length > 4)
                cleaned = cleaned.Substring(0, 4);
        }

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
    private void DrawBindingBadge(SKCanvas canvas, float x, float y, float maxWidth, string text, SKColor color, bool isDefault)
    {
        // Measure text to determine badge width
        float fontSize = 9f;
        float textWidth = FUIRenderer.MeasureText(text, fontSize);
        float badgeWidth = Math.Min(maxWidth - 2, textWidth + 10);
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

        // Badge text (truncate if needed)
        string displayText = text;
        if (textWidth > badgeWidth - 8)
        {
            displayText = TruncateTextToWidth(text, badgeWidth - 12, fontSize);
        }
        FUIRenderer.DrawText(canvas, displayText, new SKPoint(x + 5, y + badgeHeight / 2 + 3), color, fontSize);
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
        float lineHeight = FUIRenderer.ScaleLineHeight(18f);

        // Title
        FUIRenderer.DrawText(canvas, "EXPORT", new SKPoint(leftMargin, y), FUIColors.TextBright, 12f, true);
        y += FUIRenderer.ScaleLineHeight(28f);

        // Profile selector with dropdown and management buttons
        FUIRenderer.DrawText(canvas, "PROFILE", new SKPoint(leftMargin, y), FUIColors.TextDim, 9f);
        y += lineHeight - 4f;

        float dropdownHeight = 24f;
        float profileBtnWidth = 24f;
        float buttonGap = 3f;
        float dropdownRight = rightMargin - (profileBtnWidth * 3 + buttonGap * 3);

        // Profile dropdown
        _scProfileDropdownBounds = new SKRect(leftMargin, y, dropdownRight, y + dropdownHeight);
        bool dropdownHovered = _scProfileDropdownBounds.Contains(_mousePosition.X, _mousePosition.Y);
        DrawSCProfileDropdown(canvas, _scProfileDropdownBounds, _scExportProfile.ProfileName, dropdownHovered, _scProfileDropdownOpen);

        // Save button (disk icon)
        float btnX = dropdownRight + buttonGap;
        _scSaveProfileButtonBounds = new SKRect(btnX, y, btnX + profileBtnWidth, y + dropdownHeight);
        _scSaveProfileButtonHovered = _scSaveProfileButtonBounds.Contains(_mousePosition.X, _mousePosition.Y);
        DrawSCProfileButton(canvas, _scSaveProfileButtonBounds, "💾", _scSaveProfileButtonHovered, "Save");
        btnX += profileBtnWidth + buttonGap;

        // New button (plus icon)
        _scNewProfileButtonBounds = new SKRect(btnX, y, btnX + profileBtnWidth, y + dropdownHeight);
        _scNewProfileButtonHovered = _scNewProfileButtonBounds.Contains(_mousePosition.X, _mousePosition.Y);
        DrawSCProfileButton(canvas, _scNewProfileButtonBounds, "+", _scNewProfileButtonHovered, "New");
        btnX += profileBtnWidth + buttonGap;

        // Delete button (X icon)
        _scDeleteProfileButtonBounds = new SKRect(btnX, y, btnX + profileBtnWidth, y + dropdownHeight);
        _scDeleteProfileButtonHovered = _scDeleteProfileButtonBounds.Contains(_mousePosition.X, _mousePosition.Y);
        bool canDelete = _scExportProfiles.Count > 0;
        DrawSCProfileButton(canvas, _scDeleteProfileButtonBounds, "×", _scDeleteProfileButtonHovered && canDelete, "Delete", !canDelete);

        y += dropdownHeight + 10f;

        // Draw profile dropdown list if open
        if (_scProfileDropdownOpen && _scExportProfiles.Count > 0)
        {
            float listHeight = Math.Min(_scExportProfiles.Count * 24f, 120f);
            _scProfileDropdownListBounds = new SKRect(leftMargin, y - 10f, dropdownRight, y - 10f + listHeight);
            DrawSCProfileDropdownList(canvas, _scProfileDropdownListBounds);
        }

        // vJoy mappings (compact)
        FUIRenderer.DrawText(canvas, "VJOY → SC MAPPING", new SKPoint(leftMargin, y), FUIColors.TextDim, 9f);
        y += lineHeight;

        _scVJoyMappingBounds.Clear();
        float rowHeight = 24f;
        var availableVJoy = _vjoyDevices.Where(v => v.Exists).Take(4).ToList();

        if (availableVJoy.Count == 0)
        {
            FUIRenderer.DrawText(canvas, "No vJoy devices", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
            y += lineHeight;
        }
        else
        {
            foreach (var vjoy in availableVJoy)
            {
                var rowBounds = new SKRect(leftMargin, y, rightMargin, y + rowHeight);
                _scVJoyMappingBounds.Add(rowBounds);

                bool isHovered = rowBounds.Contains(_mousePosition.X, _mousePosition.Y);
                int scInstance = _scExportProfile.GetSCInstance(vjoy.Id);

                DrawVJoyMappingRowCompact(canvas, rowBounds, vjoy.Id, scInstance, isHovered);
                y += rowHeight + 2f;
            }
        }

        // Binding count
        y += 10f;
        int bindingCount = _scExportProfile.Bindings.Count;
        FUIRenderer.DrawText(canvas, $"Bindings: {bindingCount}", new SKPoint(leftMargin, y), bindingCount > 0 ? FUIColors.Success : FUIColors.TextDim, 10f);
        y += lineHeight + 10f;

        // Selected action info
        if (_scSelectedActionIndex >= 0 && _scFilteredActions != null && _scSelectedActionIndex < _scFilteredActions.Count)
        {
            var selectedAction = _scFilteredActions[_scSelectedActionIndex];

            using var selBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(30), IsAntialias = true };
            canvas.DrawRect(new SKRect(leftMargin - 5, y - 2, rightMargin + 5, y + 55f), selBgPaint);

            FUIRenderer.DrawText(canvas, "SELECTED ACTION", new SKPoint(leftMargin, y), FUIColors.Active, 9f, true);
            y += lineHeight - 2f;

            string actionDisplay = selectedAction.ActionName;
            if (actionDisplay.Length > 28) actionDisplay = actionDisplay.Substring(0, 25) + "...";
            FUIRenderer.DrawText(canvas, actionDisplay, new SKPoint(leftMargin, y), FUIColors.TextPrimary, 10f);
            y += lineHeight - 4f;

            FUIRenderer.DrawText(canvas, $"Type: {selectedAction.InputType}", new SKPoint(leftMargin, y), FUIColors.TextDim, 9f);
            y += lineHeight + 8f;

            // Assign/Clear buttons
            float btnWidth = (rightMargin - leftMargin - 5) / 2;
            float btnHeight = 24f;

            _scAssignInputButtonBounds = new SKRect(leftMargin, y, leftMargin + btnWidth, y + btnHeight);
            _scAssignInputButtonHovered = _scAssignInputButtonBounds.Contains(_mousePosition.X, _mousePosition.Y);

            var existingBinding = _scExportProfile.GetBinding(selectedAction.ActionMap, selectedAction.ActionName);

            if (_scAssigningInput)
            {
                // Show "waiting for input" state
                using var waitBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Warning.WithAlpha(80), IsAntialias = true };
                canvas.DrawRect(_scAssignInputButtonBounds, waitBgPaint);
                FUIRenderer.DrawTextCentered(canvas, "PRESS INPUT...", _scAssignInputButtonBounds, FUIColors.Warning, 9f);
            }
            else
            {
                FUIRenderer.DrawButton(canvas, _scAssignInputButtonBounds, "ASSIGN",
                    _scAssignInputButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
            }

            _scClearBindingButtonBounds = new SKRect(leftMargin + btnWidth + 5, y, rightMargin, y + btnHeight);
            _scClearBindingButtonHovered = _scClearBindingButtonBounds.Contains(_mousePosition.X, _mousePosition.Y);
            bool hasBinding = existingBinding != null;

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

        // Export button at bottom
        y = bounds.Bottom - frameInset - 50f;
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

    private void DrawSCActionMapFilterDropdown(SKCanvas canvas)
    {
        float itemHeight = 24f;
        float dropdownWidth = _scActionMapFilterBounds.Width + 60f;
        int itemCount = _scActionMaps.Count + 1; // +1 for "All Categories"
        float dropdownHeight = Math.Min(itemCount * itemHeight + 4, 300f);

        _scActionMapFilterDropdownBounds = new SKRect(
            _scActionMapFilterBounds.Right - dropdownWidth,
            _scActionMapFilterBounds.Bottom + 2,
            _scActionMapFilterBounds.Right,
            _scActionMapFilterBounds.Bottom + 2 + dropdownHeight);

        // Shadow
        FUIRenderer.DrawPanelShadow(canvas, _scActionMapFilterDropdownBounds, 2f, 2f, 8f);

        // Background
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1.WithAlpha(245), IsAntialias = true };
        canvas.DrawRect(_scActionMapFilterDropdownBounds, bgPaint);

        // Border
        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRect(_scActionMapFilterDropdownBounds, borderPaint);

        // Clip for scrolling
        canvas.Save();
        canvas.ClipRect(_scActionMapFilterDropdownBounds);

        // Items
        float y = _scActionMapFilterDropdownBounds.Top + 2;

        // "All Categories" option
        var allItemBounds = new SKRect(_scActionMapFilterDropdownBounds.Left + 2, y,
            _scActionMapFilterDropdownBounds.Right - 2, y + itemHeight);

        bool allHovered = _scHoveredActionMapFilter == -1 && allItemBounds.Contains(_mousePosition.X, _mousePosition.Y);
        bool allSelected = string.IsNullOrEmpty(_scActionMapFilter);

        if (allHovered || allSelected)
        {
            var highlightColor = allSelected ? FUIColors.Active.WithAlpha(50) : FUIColors.Background2.WithAlpha(120);
            using var highlightPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = highlightColor, IsAntialias = true };
            canvas.DrawRect(allItemBounds, highlightPaint);
        }

        var allTextColor = allSelected ? FUIColors.Active : (allHovered ? FUIColors.TextBright : FUIColors.TextPrimary);
        FUIRenderer.DrawText(canvas, "All Categories",
            new SKPoint(allItemBounds.Left + 8, allItemBounds.MidY + 4), allTextColor, 10f);
        y += itemHeight;

        // Category items
        for (int i = 0; i < _scActionMaps.Count; i++)
        {
            var itemBounds = new SKRect(_scActionMapFilterDropdownBounds.Left + 2, y,
                _scActionMapFilterDropdownBounds.Right - 2, y + itemHeight);

            bool isHovered = i == _scHoveredActionMapFilter;
            bool isSelected = _scActionMapFilter == _scActionMaps[i];

            if (isHovered || isSelected)
            {
                var highlightColor = isSelected ? FUIColors.Active.WithAlpha(50) : FUIColors.Background2.WithAlpha(120);
                using var highlightPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = highlightColor, IsAntialias = true };
                canvas.DrawRect(itemBounds, highlightPaint);
            }

            var textColor = isSelected ? FUIColors.Active : (isHovered ? FUIColors.TextBright : FUIColors.TextPrimary);
            FUIRenderer.DrawText(canvas, FormatActionMapName(_scActionMaps[i]),
                new SKPoint(itemBounds.Left + 8, itemBounds.MidY + 4), textColor, 10f);

            y += itemHeight;
        }

        canvas.Restore();
    }

    private static string FormatActionMapName(string actionMap)
    {
        // Convert "spaceship_movement" to "Spaceship Movement"
        if (string.IsNullOrEmpty(actionMap)) return actionMap;

        return string.Join(" ", actionMap
            .Split('_')
            .Select(word => char.ToUpper(word[0]) + word.Substring(1).ToLower()));
    }

    #endregion

    #region SC Bindings Click Handling

    private void HandleBindingsTabClick(SKPoint point)
    {
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
                // Calculate which item was clicked
                float itemHeight = 24f;
                float relativeY = point.Y - _scActionMapFilterDropdownBounds.Top - 2;
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
                return;
            }
            else
            {
                _scActionMapFilterDropdownOpen = false;
                return;
            }
        }

        // SC Export profile dropdown handling
        if (_scProfileDropdownOpen)
        {
            if (_scProfileDropdownListBounds.Contains(point))
            {
                // Click on dropdown item
                if (_scHoveredProfileIndex >= 0 && _scHoveredProfileIndex < _scExportProfiles.Count)
                {
                    LoadSCExportProfile(_scExportProfiles[_scHoveredProfileIndex].ProfileName);
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
            if (!string.IsNullOrEmpty(_scSearchText) && point.X > _scSearchBoxBounds.Right - 25)
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

        // Assign input button
        if (_scAssignInputButtonBounds.Contains(point) && _scSelectedActionIndex >= 0)
        {
            AssignSCBinding();
            return;
        }

        // Clear binding button
        if (_scClearBindingButtonBounds.Contains(point) && _scSelectedActionIndex >= 0 && _scFilteredActions != null)
        {
            var selectedAction = _scFilteredActions[_scSelectedActionIndex];
            _scExportProfile.RemoveBinding(selectedAction.ActionMap, selectedAction.ActionName);
            _scAssigningInput = false;
            return;
        }

        // vJoy mapping row clicks (cycle through js1-js8)
        for (int i = 0; i < _scVJoyMappingBounds.Count; i++)
        {
            if (_scVJoyMappingBounds[i].Contains(point))
            {
                var availableVJoy = _vjoyDevices.Where(v => v.Exists).Take(4).ToList();
                if (i < availableVJoy.Count)
                {
                    var vjoyId = availableVJoy[i].Id;
                    int currentInstance = _scExportProfile.GetSCInstance(vjoyId);
                    int newInstance = (currentInstance % 8) + 1; // Cycle 1-8
                    _scExportProfile.SetSCInstance(vjoyId, newInstance);
                }
                return;
            }
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
        if (_scBindingsListBounds.Contains(point) && _scFilteredActions != null)
        {
            // Find which row was clicked accounting for scroll offset and collapsed categories
            float rowHeight = 24f;
            float rowGap = 2f;
            float categoryHeaderHeight = 28f;
            float relativeY = point.Y - _scBindingsListBounds.Top + _scBindingsScrollOffset;

            string? lastActionMap = null;
            float currentY = 0;

            for (int i = 0; i < _scFilteredActions.Count; i++)
            {
                var action = _scFilteredActions[i];

                // Account for category header
                if (action.ActionMap != lastActionMap)
                {
                    lastActionMap = action.ActionMap;
                    currentY += categoryHeaderHeight;

                    // If category is collapsed, skip all its actions
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
                    _scSelectedActionIndex = i;

                    // Check if click was in a device column cell
                    int clickedCol = GetClickedColumnIndex(point.X);
                    if (clickedCol >= 0 && _scGridColumns != null && clickedCol < _scGridColumns.Count)
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
        if (_scGridColumns == null || x < _scDeviceColsStart || x > _scDeviceColsStart + _scVisibleDeviceWidth)
            return -1;

        float relativeX = x - _scDeviceColsStart + _scGridHorizontalScroll;
        int colIndex = (int)(relativeX / _scGridDeviceColWidth);

        if (colIndex >= 0 && colIndex < _scGridColumns.Count)
            return colIndex;

        return -1;
    }

    /// <summary>
    /// Handles a click on a binding cell - enters listening mode for input
    /// </summary>
    private void HandleCellClick(int actionIndex, int colIndex)
    {
        if (_scGridColumns == null || colIndex < 0 || colIndex >= _scGridColumns.Count)
            return;

        var col = _scGridColumns[colIndex];

        // If already listening for this cell, cancel
        if (_scIsListeningForInput && _scSelectedCell == (actionIndex, colIndex))
        {
            _scIsListeningForInput = false;
            _scSelectedCell = (-1, -1);
            _scListeningColumn = null;
            return;
        }

        // Start listening for input
        _scSelectedCell = (actionIndex, colIndex);
        _scIsListeningForInput = true;
        _scListeningStartTime = DateTime.Now;
        _scListeningColumn = col;

        System.Diagnostics.Debug.WriteLine($"[SCBindings] Started listening for input on cell ({actionIndex}, {colIndex}) - {col.Header}");
    }

    #endregion

    #region SC Export and Dialogs

    private void ExportToSC()
    {
        if (_scExportService == null || _scInstallations.Count == 0)
        {
            _scExportStatus = "No SC installation available";
            _scExportStatusTime = DateTime.Now;
            return;
        }

        if (_scExportProfile.VJoyToSCInstance.Count == 0)
        {
            _scExportStatus = "No vJoy mappings configured";
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

            // Export
            string exportPath = _scExportService.ExportToInstallation(_scExportProfile, installation);
            _scExportStatus = $"Success! Exported to {Path.GetFileName(exportPath)}";
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
            Left = 15,
            Top = 15,
            Width = 280,
            ForeColor = Color.FromArgb(180, 190, 210)
        };

        var textBox = new TextBox
        {
            Text = _scExportProfile.ProfileName,
            Left = 15,
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
        if (_scSelectedActionIndex < 0 || _scFilteredActions == null || _scSelectedActionIndex >= _scFilteredActions.Count)
            return;

        var action = _scFilteredActions[_scSelectedActionIndex];
        var availableVJoy = _vjoyDevices.Where(v => v.Exists).ToList();

        if (availableVJoy.Count == 0)
        {
            MessageBox.Show("No vJoy devices available. Please configure vJoy.", "No vJoy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new Form
        {
            Text = $"Assign: {action.ActionName}",
            Width = 350,
            Height = 240,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(20, 22, 30)
        };

        var vjoyLabel = new Label
        {
            Text = "vJoy Device:",
            Left = 15,
            Top = 15,
            Width = 100,
            ForeColor = Color.FromArgb(180, 190, 210)
        };

        var vjoyCombo = new ComboBox
        {
            Left = 15,
            Top = 35,
            Width = 300,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(30, 35, 45),
            ForeColor = Color.White
        };
        foreach (var vjoy in availableVJoy)
        {
            vjoyCombo.Items.Add($"vJoy {vjoy.Id}");
        }
        vjoyCombo.SelectedIndex = 0;

        var inputLabel = new Label
        {
            Text = action.InputType == SCInputType.Axis ? "Axis:" : "Button:",
            Left = 15,
            Top = 70,
            Width = 100,
            ForeColor = Color.FromArgb(180, 190, 210)
        };

        var inputCombo = new ComboBox
        {
            Left = 15,
            Top = 90,
            Width = 300,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(30, 35, 45),
            ForeColor = Color.White
        };

        if (action.InputType == SCInputType.Axis)
        {
            inputCombo.Items.AddRange(new[] { "x", "y", "z", "rx", "ry", "rz", "slider1", "slider2" });
        }
        else
        {
            for (int i = 1; i <= 32; i++)
            {
                inputCombo.Items.Add($"button{i}");
            }
        }
        inputCombo.SelectedIndex = 0;

        var invertCheck = new CheckBox
        {
            Text = "Inverted",
            Left = 15,
            Top = 125,
            Width = 100,
            ForeColor = Color.FromArgb(180, 190, 210),
            Visible = action.InputType == SCInputType.Axis
        };

        var okButton = new Button
        {
            Text = "Assign",
            Left = 150,
            Top = 160,
            Width = 80,
            DialogResult = DialogResult.OK,
            BackColor = Color.FromArgb(40, 100, 70),
            ForeColor = Color.White
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            Left = 235,
            Top = 160,
            Width = 80,
            DialogResult = DialogResult.Cancel,
            BackColor = Color.FromArgb(60, 50, 50),
            ForeColor = Color.White
        };

        dialog.Controls.AddRange(new Control[] { vjoyLabel, vjoyCombo, inputLabel, inputCombo, invertCheck, okButton, cancelButton });
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var vjoyId = availableVJoy[vjoyCombo.SelectedIndex].Id;
            var inputName = inputCombo.SelectedItem?.ToString() ?? "button1";

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

            var binding = new SCActionBinding
            {
                ActionMap = action.ActionMap,
                ActionName = action.ActionName,
                VJoyDevice = vjoyId,
                InputName = inputName,
                InputType = action.InputType,
                Inverted = invertCheck.Checked
            };

            _scExportProfile.SetBinding(action.ActionMap, action.ActionName, binding);

            // Ensure this vJoy device has an SC instance mapping
            if (!_scExportProfile.VJoyToSCInstance.ContainsKey(vjoyId))
            {
                _scExportProfile.SetSCInstance(vjoyId, (int)vjoyId);
            }

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
            Height = 220 + Math.Min(conflicts.Count * 15, 60),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(30, 25, 25)
        };

        var msgLabel = new Label
        {
            Text = message,
            Left = 15,
            Top = 15,
            Width = 380,
            Height = 100 + Math.Min(conflicts.Count * 15, 60),
            ForeColor = Color.FromArgb(200, 180, 170)
        };

        int buttonY = msgLabel.Bottom + 15;

        var replaceButton = new Button
        {
            Text = "Replace Existing",
            Left = 15,
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

        // Cursor when focused
        if (focused)
        {
            float cursorX = textX + (string.IsNullOrEmpty(text) ? 0 : MeasureTextWidth(text, 10f));
            if ((DateTime.Now.Millisecond / 500) % 2 == 0)
            {
                using var cursorPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active, StrokeWidth = 1f };
                canvas.DrawLine(cursorX, bounds.Top + 5, cursorX, bounds.Bottom - 5, cursorPaint);
            }
        }
    }

    private static float MeasureTextWidth(string text, float fontSize)
    {
        using var paint = new SKPaint { TextSize = fontSize, IsAntialias = true };
        return paint.MeasureText(text);
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

    private void DrawSCProfileDropdownList(SKCanvas canvas, SKRect bounds)
    {
        // Background with shadow
        using var shadowPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(0, 0, 0, 100), IsAntialias = true };
        var shadowBounds = new SKRect(bounds.Left + 2, bounds.Top + 2, bounds.Right + 2, bounds.Bottom + 2);
        canvas.DrawRoundRect(shadowBounds, 3f, 3f, shadowPaint);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1.WithAlpha(240), IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, bgPaint);

        using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawRoundRect(bounds, 3f, 3f, borderPaint);

        // Items
        float rowHeight = 24f;
        float y = bounds.Top;
        _scHoveredProfileIndex = -1;

        for (int i = 0; i < _scExportProfiles.Count && y + rowHeight <= bounds.Bottom; i++)
        {
            var profile = _scExportProfiles[i];
            var rowBounds = new SKRect(bounds.Left + 2, y, bounds.Right - 2, y + rowHeight);
            bool isHovered = rowBounds.Contains(_mousePosition.X, _mousePosition.Y);
            bool isCurrent = profile.ProfileName == _scExportProfile.ProfileName;

            if (isHovered)
            {
                _scHoveredProfileIndex = i;
                using var hoverPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(60), IsAntialias = true };
                canvas.DrawRoundRect(rowBounds, 2f, 2f, hoverPaint);
            }
            else if (isCurrent)
            {
                using var currentPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(30), IsAntialias = true };
                canvas.DrawRoundRect(rowBounds, 2f, 2f, currentPaint);
            }

            var textColor = isCurrent ? FUIColors.Active : (isHovered ? FUIColors.TextBright : FUIColors.TextPrimary);
            string displayName = profile.ProfileName.Length > 18 ? profile.ProfileName.Substring(0, 15) + "..." : profile.ProfileName;
            FUIRenderer.DrawText(canvas, displayName, new SKPoint(rowBounds.Left + 6, rowBounds.MidY + 4f), textColor, 10f);

            // Binding count badge
            if (profile.BindingCount > 0)
            {
                string countStr = profile.BindingCount.ToString();
                float badgeX = rowBounds.Right - 8 - MeasureTextWidth(countStr, 8f);
                FUIRenderer.DrawText(canvas, countStr, new SKPoint(badgeX, rowBounds.MidY + 3f), FUIColors.TextDim, 8f);
            }

            y += rowHeight;
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
        if (_scExportProfileService == null) return;

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
            Left = 15,
            Top = 15,
            Width = 280,
            ForeColor = Color.FromArgb(180, 190, 210)
        };

        var textBox = new TextBox
        {
            Text = "New Profile",
            Left = 15,
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
            if (_scExportProfileService != null && _scExportProfileService.ProfileExists(newName))
            {
                MessageBox.Show($"A profile named '{newName}' already exists.", "Profile Exists",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        if (_scExportProfileService == null || _scExportProfiles.Count == 0) return;

        var result = MessageBox.Show(
            $"Delete SC export profile '{_scExportProfile.ProfileName}'?\n\nThis cannot be undone.",
            "Delete Profile",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result == DialogResult.Yes)
        {
            var deletedName = _scExportProfile.ProfileName;
            _scExportProfileService.DeleteProfile(deletedName);
            RefreshSCExportProfiles();

            // Load another profile or create default
            if (_scExportProfiles.Count > 0)
            {
                var nextProfile = _scExportProfileService.LoadProfile(_scExportProfiles[0].ProfileName);
                if (nextProfile != null)
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
        if (_scExportProfileService == null) return;

        var profile = _scExportProfileService.LoadProfile(profileName);
        if (profile != null)
        {
            _scExportProfile = profile;
            _profileService.LastSCExportProfile = profileName;
            _scProfileDropdownOpen = false;
            _scExportStatus = $"Loaded profile '{profileName}'";
            _scExportStatusTime = DateTime.Now;
        }
    }

    #endregion
}
