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

            RefreshSCInstallations();

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

            System.Diagnostics.Debug.WriteLine($"[MainForm] SC bindings initialized, {_scInstallations.Count} installations found");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] SC bindings init failed: {ex.Message}");
        }
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

        // Title and filter
        FUIRenderer.DrawText(canvas, "SC ACTIONS", new SKPoint(leftMargin, y), FUIColors.TextBright, 12f, true);

        // Filter dropdown (right side of title)
        float filterWidth = 180f;
        float filterHeight = 24f;
        float filterX = rightMargin - filterWidth;
        _scActionMapFilterBounds = new SKRect(filterX, y - 4f, rightMargin, y - 4f + filterHeight);

        string filterText = string.IsNullOrEmpty(_scActionMapFilter) ? "All Categories" : FormatActionMapName(_scActionMapFilter);
        bool filterHovered = _scActionMapFilterBounds.Contains(_mousePosition.X, _mousePosition.Y);
        DrawSelector(canvas, _scActionMapFilterBounds, filterText, filterHovered || _scActionMapFilterDropdownOpen, _scActionMaps.Count > 0);

        y += FUIRenderer.ScaleLineHeight(32f);

        // Table header
        float colAction = leftMargin;
        float colType = leftMargin + 250f;
        float colBinding = rightMargin - 120f;

        using var headerPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2.WithAlpha(100), IsAntialias = true };
        canvas.DrawRect(new SKRect(leftMargin - 5, y - 2, rightMargin + 5, y + lineHeight), headerPaint);

        FUIRenderer.DrawText(canvas, "ACTION", new SKPoint(colAction, y), FUIColors.TextDim, 9f, true);
        FUIRenderer.DrawText(canvas, "TYPE", new SKPoint(colType, y), FUIColors.TextDim, 9f, true);
        FUIRenderer.DrawText(canvas, "BINDING", new SKPoint(colBinding, y), FUIColors.TextDim, 9f, true);
        y += lineHeight + 4f;

        // Scrollable action list
        float listTop = y;
        float listBottom = bounds.Bottom - frameInset - 15f;
        _scBindingsListBounds = new SKRect(leftMargin - 5, listTop, rightMargin + 5, listBottom);

        // Clip to list area
        canvas.Save();
        canvas.ClipRect(_scBindingsListBounds);

        _scActionRowBounds.Clear();
        float rowHeight = 24f;
        float rowGap = 2f;
        float scrollY = listTop - _scBindingsScrollOffset;

        if (_scFilteredActions == null || _scFilteredActions.Count == 0)
        {
            FUIRenderer.DrawText(canvas, _scActions == null ? "Loading actions..." : "No actions match filter",
                new SKPoint(leftMargin, scrollY + 20f), FUIColors.TextDim, 11f);
        }
        else
        {
            string? lastActionMap = null;

            for (int i = 0; i < _scFilteredActions.Count; i++)
            {
                var action = _scFilteredActions[i];

                // Group header when action map changes
                if (action.ActionMap != lastActionMap)
                {
                    lastActionMap = action.ActionMap;

                    // Draw group header
                    if (scrollY >= listTop - rowHeight && scrollY < listBottom)
                    {
                        using var groupBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Primary.WithAlpha(30), IsAntialias = true };
                        canvas.DrawRect(new SKRect(leftMargin - 5, scrollY, rightMargin + 5, scrollY + rowHeight - 2), groupBgPaint);
                        FUIRenderer.DrawText(canvas, FormatActionMapName(action.ActionMap), new SKPoint(leftMargin, scrollY + rowHeight / 2 + 4), FUIColors.Primary, 10f, true);
                    }
                    scrollY += rowHeight;
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

                    // Action name (truncate if needed)
                    string displayName = action.ActionName;
                    if (displayName.Length > 35) displayName = displayName.Substring(0, 32) + "...";
                    var nameColor = isSelected ? FUIColors.Active : FUIColors.TextPrimary;
                    FUIRenderer.DrawText(canvas, displayName, new SKPoint(colAction, textY), nameColor, 10f);

                    // Input type
                    var typeColor = action.InputType == SCInputType.Axis ? FUIColors.Warning : FUIColors.TextDim;
                    FUIRenderer.DrawText(canvas, action.InputType.ToString(), new SKPoint(colType, textY), typeColor, 10f);

                    // Current binding
                    var existingBinding = _scExportProfile.GetBinding(action.ActionMap, action.ActionName);
                    if (existingBinding != null)
                    {
                        string bindingText = $"js{_scExportProfile.GetSCInstance(existingBinding.VJoyDevice)}_{existingBinding.InputName}";
                        FUIRenderer.DrawText(canvas, bindingText, new SKPoint(colBinding, textY), FUIColors.Success, 10f);
                    }
                    else
                    {
                        FUIRenderer.DrawText(canvas, "—", new SKPoint(colBinding, textY), FUIColors.TextDim, 10f);
                    }
                }

                scrollY += rowHeight + rowGap;
            }

            _scBindingsContentHeight = scrollY - listTop + _scBindingsScrollOffset;
        }

        canvas.Restore();

        // Scrollbar if needed
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

        // Profile name
        FUIRenderer.DrawText(canvas, "PROFILE NAME", new SKPoint(leftMargin, y), FUIColors.TextDim, 9f);
        y += lineHeight - 4f;

        float nameFieldHeight = 24f;
        _scProfileNameBounds = new SKRect(leftMargin, y, rightMargin, y + nameFieldHeight);
        _scProfileNameHovered = _scProfileNameBounds.Contains(_mousePosition.X, _mousePosition.Y);
        DrawTextFieldReadOnly(canvas, _scProfileNameBounds, _scExportProfile.ProfileName, _scProfileNameHovered);
        y += nameFieldHeight + 10f;

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

        // SC Installation selector click (toggle dropdown)
        if (_scInstallationSelectorBounds.Contains(point) && _scInstallations.Count > 0)
        {
            _scInstallationDropdownOpen = !_scInstallationDropdownOpen;
            _scActionMapFilterDropdownOpen = false;
            return;
        }

        // Action map filter selector click
        if (_scActionMapFilterBounds.Contains(point) && _scActionMaps.Count > 0)
        {
            _scActionMapFilterDropdownOpen = !_scActionMapFilterDropdownOpen;
            _scInstallationDropdownOpen = false;
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

        // Action row clicks (select action)
        if (_scBindingsListBounds.Contains(point) && _scFilteredActions != null)
        {
            // Find which row was clicked accounting for scroll offset
            float rowHeight = 24f;
            float rowGap = 2f;
            float relativeY = point.Y - _scBindingsListBounds.Top + _scBindingsScrollOffset;

            // We need to account for group headers
            string? lastActionMap = null;
            float currentY = 0;

            for (int i = 0; i < _scFilteredActions.Count; i++)
            {
                var action = _scFilteredActions[i];

                // Account for group header
                if (action.ActionMap != lastActionMap)
                {
                    lastActionMap = action.ActionMap;
                    currentY += rowHeight; // Group header height
                }

                float rowTop = currentY;
                float rowBottom = currentY + rowHeight;

                if (relativeY >= rowTop && relativeY < rowBottom)
                {
                    _scSelectedActionIndex = i;
                    _scAssigningInput = false; // Reset assignment mode when selecting new action
                    return;
                }

                currentY += rowHeight + rowGap;
            }
        }
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

    #endregion
}
