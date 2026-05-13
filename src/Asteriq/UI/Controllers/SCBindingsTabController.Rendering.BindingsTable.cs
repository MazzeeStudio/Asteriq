using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class SCBindingsTabController
{
    private void DrawSCBindingsTablePanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        var m = FUIRenderer.DrawPanelChrome(canvas, bounds);
        float leftMargin = m.LeftMargin;
        float rightMargin = m.RightMargin;
        float y = m.Y;
        // Title row with action count
        FUIRenderer.DrawText(canvas, "SC ACTIONS", new SKPoint(leftMargin, y), FUIColors.TextBright, 15f, true);

        // Action count on right of title
        int actionCount = _scFilteredActions?.Count ?? 0;
        int totalCount = _scSchemaService is not null && _scInstall.Actions is not null
            ? SCSchemaService.FilterJoystickActions(_scInstall.Actions).Count
            : actionCount;
        // Total bound is always against the full unfiltered list so it reflects the whole profile
        int totalBound = _scInstall.Actions?.Count(a => _scExportProfile.GetBinding(a.ActionMap, a.ActionName) is not null) ?? 0;
        int boundCount = _scFilteredActions?.Count(a => _scExportProfile.GetBinding(a.ActionMap, a.ActionName) is not null) ?? 0;
        bool otherFilters = !string.IsNullOrEmpty(_searchFilter.ActionMapFilter) || !string.IsNullOrEmpty(_searchFilter.SearchText);
        bool showBoundOnly = _ctx.AppSettings.SCBindingsShowBoundOnly;
        bool isFiltered = otherFilters || showBoundOnly;

        string countText;
        if (!isFiltered)
            countText = $"{totalCount} actions, {totalBound} bound";
        else if (showBoundOnly && !otherFilters)
            countText = $"{totalBound} of {totalCount} bound";       // "239 of 1113 bound"
        else if (showBoundOnly)
            countText = $"{actionCount} of {totalBound} bound";       // "26 of 239 bound" (within current filter)
        else
            countText = $"{actionCount} of {totalCount}, {boundCount} bound"; // "55 of 1113, 26 bound"
        float countTextWidth = FUIRenderer.MeasureText(countText, 12f);
        FUIRenderer.DrawText(canvas, countText, new SKPoint(rightMargin - countTextWidth, y), FUIColors.TextDim, 12f);

        y += 28f;

        // Filter row: [search...] [☐ Bound only] [☐ Show JS ref]    [All Categories ▼]
        float filterRowHeight = 32f;
        float checkboxSize = 16f;
        float filterWidth = 220f;  // Width for category selector

        // Category filter dropdown on the right
        float filterX = rightMargin - filterWidth;
        _searchFilter.FilterBounds = new SKRect(filterX, y, rightMargin, y + filterRowHeight);
        string filterText = string.IsNullOrEmpty(_searchFilter.ActionMapFilter) ? "All Categories" : _searchFilter.ActionMapFilter;
        bool filterHovered = _searchFilter.FilterBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawSelector(canvas, _searchFilter.FilterBounds, filterText, filterHovered || _searchFilter.FilterDropdownOpen, _searchFilter.ActionMaps.Count > 0);

        // Search box on the left (narrowed to make room for capture toggle button)
        const float captureButtonW = 28f;
        const float captureButtonGap = 4f;
        float maxSearchWidth = 280f - captureButtonW - captureButtonGap;
        _searchFilter.SearchBoxBounds = new SKRect(leftMargin, y, leftMargin + maxSearchWidth, y + filterRowHeight);
        string searchPlaceholder = _searchFilter.ButtonCaptureActive ? "Press a button..." : "Search actions...";
        // When capture result is active, parse "rctrl+button13" → ["CTRL", "Btn13"] for badge display
        IReadOnlyList<string>? captureBadges = null;
        if (_searchFilter.ButtonCaptureTextActive && !string.IsNullOrEmpty(_searchFilter.SearchText))
        {
            var parts = _searchFilter.SearchText.Split('+');
            var badges = new List<string>();
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var fmt = SCBindingsRenderer.FormatModifierName(parts[i]);
                if (!string.IsNullOrEmpty(fmt)) badges.Add(fmt);
            }
            badges.Add(SCBindingsRenderer.FormatInputName(parts[^1]));
            captureBadges = badges;
        }
        FUIWidgets.DrawSearchBox(canvas, _searchFilter.SearchBoxBounds, _searchFilter.SearchText, _searchFilter.SearchBoxFocused, _ctx.MousePosition, searchPlaceholder,
            captureBadges: captureBadges,
            cursorPos: _searchFilter.CursorPos,
            selectionStart: _searchFilter.SelectionStart,
            selectionEnd: _searchFilter.SelectionEnd);

        // Button capture toggle button [🎮] — right of search box
        float capBtnX = _searchFilter.SearchBoxBounds.Right + captureButtonGap;
        _searchFilter.ButtonCaptureBounds = new SKRect(capBtnX, y, capBtnX + captureButtonW, y + filterRowHeight);
        _searchFilter.ButtonCaptureHovered = _searchFilter.ButtonCaptureBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        DrawButtonCaptureToggle(canvas, _searchFilter.ButtonCaptureBounds, _searchFilter.ButtonCaptureActive, _searchFilter.ButtonCaptureHovered);

        // "Bound only" checkbox — 16px gap after capture toggle button
        float checkboxX = _searchFilter.ButtonCaptureBounds.Right + 16f;
        _searchFilter.ShowBoundOnlyBounds = new SKRect(checkboxX, y + (filterRowHeight - checkboxSize) / 2,
            checkboxX + checkboxSize, y + (filterRowHeight + checkboxSize) / 2);
        _searchFilter.ShowBoundOnlyHovered = _searchFilter.ShowBoundOnlyBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawCheckboxWithLabel(canvas, _searchFilter.ShowBoundOnlyBounds, showBoundOnly,
            _searchFilter.ShowBoundOnlyHovered, "Bound only");

        // "Show JS ref" checkbox — hidden in client mode (JS ref is always the header in that context)
        float boundOnlyTotalW = checkboxSize + 7 + FUIRenderer.MeasureText("Bound only", 13f);
        bool isClientMode = _ctx.AppSettings.ClientOnlyMode;
        bool showJSRef = isClientMode || !_ctx.AppSettings.SCBindingsShowPhysicalHeaders;
        if (!isClientMode)
        {
            float jsRefCheckboxX = checkboxX + boundOnlyTotalW + 16f;
            _searchFilter.ShowJSRefBounds = new SKRect(jsRefCheckboxX, y + (filterRowHeight - checkboxSize) / 2,
                jsRefCheckboxX + checkboxSize, y + (filterRowHeight + checkboxSize) / 2);
            _searchFilter.ShowJSRefHovered = _searchFilter.ShowJSRefBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            FUIWidgets.DrawCheckboxWithLabel(canvas, _searchFilter.ShowJSRefBounds, showJSRef,
                _searchFilter.ShowJSRefHovered, "Show JS ref");
        }
        else
        {
            _searchFilter.ShowJSRefBounds = SKRect.Empty;
            _searchFilter.ShowJSRefHovered = false;
        }

        y += filterRowHeight + 12f;

        // Get dynamic columns and cache them for mouse handling
        var columns = GetSCGridColumns();
        _grid.Columns = columns;

        // Column layout - fixed action column, device columns have dynamic widths
        float totalWidth = rightMargin - leftMargin;

        // Calculate column widths and X positions
        var colWidths = new float[columns.Count];
        var colXPositions = new float[columns.Count];
        float cumX = 0f;
        for (int c = 0; c < columns.Count; c++)
        {
            colWidths[c] = _grid.DeviceColWidths.TryGetValue(columns[c].Id, out var w) ? w : _grid.DeviceColMinWidth;
            colXPositions[c] = cumX;
            cumX += colWidths[c];
        }
        float totalDeviceColsWidth = cumX;

        // Action column is fixed width
        float actionColWidth = _grid.ActionColWidth;

        float availableWidth = totalWidth - actionColWidth - 10f;

        // Calculate if horizontal scrolling is needed
        bool needsHorizontalScroll = totalDeviceColsWidth > availableWidth;
        float visibleDeviceWidth = needsHorizontalScroll ? availableWidth : totalDeviceColsWidth;
        _grid.TotalWidth = totalDeviceColsWidth;
        _grid.VisibleDeviceWidth = visibleDeviceWidth;

        // Clamp horizontal scroll
        if (needsHorizontalScroll)
        {
            float maxHScroll = totalDeviceColsWidth - visibleDeviceWidth;
            _grid.HorizontalScroll = Math.Clamp(_grid.HorizontalScroll, 0, maxHScroll);
        }
        else
        {
            _grid.HorizontalScroll = 0;
        }

        float deviceColsStart = leftMargin + actionColWidth + 5f;
        _grid.DeviceColsStart = deviceColsStart;

        // Table header row
        float headerRowHeight = FUIRenderer.TouchTargetMinHeight;  // 24px minimum
        float headerTextY = y + headerRowHeight / 2 + 4f;  // Vertically centered

        // Table header background
        using var headerPaint = FUIRenderer.CreateFillPaint(FUIColors.PanelBgDefault);
        canvas.DrawRect(new SKRect(leftMargin - 5, y, rightMargin + 5, y + headerRowHeight), headerPaint);

        // Store column headers bounds for click detection
        _grid.ColumnHeadersBounds = new SKRect(deviceColsStart, y, deviceColsStart + visibleDeviceWidth, y + headerRowHeight);

        // Draw ACTION column header
        FUIRenderer.DrawText(canvas, "ACTION", new SKPoint(leftMargin + 18f, headerTextY), FUIColors.TextDim, 12f, true);

        // Draw separator after ACTION column
        using var actionSepPaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame.WithAlpha(FUIColors.AlphaHoverStrong));
        canvas.DrawLine(deviceColsStart - 3, y, deviceColsStart - 3, y + headerRowHeight, actionSepPaint);

        // Clip device columns to available area
        canvas.Save();
        var deviceColsClipRect = new SKRect(deviceColsStart, y, deviceColsStart + visibleDeviceWidth, bounds.Bottom);
        canvas.ClipRect(deviceColsClipRect);

        // Draw device column headers
        for (int c = 0; c < columns.Count; c++)
        {
            float colW = colWidths[c];
            float colX = deviceColsStart + colXPositions[c] - _grid.HorizontalScroll;
            if (colX + colW > deviceColsStart && colX < deviceColsStart + visibleDeviceWidth)
            {
                var col = columns[c];

                // Highlight background if this column is selected
                if (c == _colImport.HighlightedColumn)
                {
                    using var highlightPaint = FUIRenderer.CreateFillPaint(FUIColors.SelectionBg);
                    canvas.DrawRect(new SKRect(colX, y, colX + colW, y + headerRowHeight), highlightPaint);
                }

                if (col.IsReadOnly)
                {
                    if (!isClientMode)
                    {
                        // Read-only column: dimmed header + "NO DEVICE" sub-label
                        float headerTextWidth = FUIRenderer.MeasureText(col.Header, 12f);
                        float centeredX = colX + (colW - headerTextWidth) / 2;
                        FUIRenderer.DrawText(canvas, col.Header, new SKPoint(centeredX, headerTextY - 5f), FUIColors.TextDim, 12f, true);
                        float subLabelWidth = FUIRenderer.MeasureText("NO DEVICE", 12f);
                        FUIRenderer.DrawText(canvas, "NO DEVICE", new SKPoint(colX + (colW - subLabelWidth) / 2, headerTextY + 5f), FUIColors.TextDimSubtle, 12f);
                    }
                    else
                    {
                        // In client mode show the JS reference cleanly without the "no device" warning
                        string jsLabel = $"JS{col.SCInstance}";
                        float subLabelWidth = FUIRenderer.MeasureText(jsLabel, 12f);
                        FUIRenderer.DrawText(canvas, jsLabel, new SKPoint(colX + (colW - subLabelWidth) / 2, headerTextY), FUIColors.ActiveStrong, 12f);
                    }
                }
                else if (col.IsPhysical)
                {
                    var headerColor = FUIColors.ContentColor(c == _colImport.HighlightedColumn);
                    if (showJSRef)
                    {
                        // JS ref mode: show "JS{N}" only
                        string jsLabel = $"JS{col.SCInstance}";
                        float jsLabelWidth = FUIRenderer.MeasureText(jsLabel, 12f);
                        FUIRenderer.DrawText(canvas, jsLabel, new SKPoint(colX + (colW - jsLabelWidth) / 2, headerTextY), headerColor, 12f, true);
                    }
                    else
                    {
                        // Device name mode: truncated name only
                        string shortName = FUIWidgets.TruncateTextToWidth(col.Header, colW - 4f, 11f);
                        float nameWidth = FUIRenderer.MeasureText(shortName, 11f);
                        FUIRenderer.DrawText(canvas, shortName, new SKPoint(colX + (colW - nameWidth) / 2, headerTextY), headerColor, 11f, true);
                    }
                }
                else if (col.IsJoystick && !showJSRef)
                {
                    // Device mode: show physical device name, or fall back to JS# if no device mapped
                    var headerColor = FUIColors.ContentColor(c == _colImport.HighlightedColumn);
                    string? deviceName = GetPhysicalDeviceNameForVJoyColumn(col);
                    if (deviceName is not null)
                    {
                        string shortName = FUIWidgets.TruncateTextToWidth(deviceName, colW - 4f, 11f);
                        float nameTextWidth = FUIRenderer.MeasureText(shortName, 11f);
                        FUIRenderer.DrawText(canvas, shortName, new SKPoint(colX + (colW - nameTextWidth) / 2, headerTextY), headerColor, 11f, true);
                    }
                    else
                    {
                        // No physical device mapped — show JS number so the header is never blank
                        float jsW = FUIRenderer.MeasureText(col.Header, 12f);
                        FUIRenderer.DrawText(canvas, col.Header, new SKPoint(colX + (colW - jsW) / 2, headerTextY), FUIColors.TextDim, 12f, true);
                    }
                }
                else
                {
                    // Use consistent theme colors for all column headers
                    var headerColor = c == _colImport.HighlightedColumn ? FUIColors.Active :
                                      col.IsJoystick ? FUIColors.Active : FUIColors.TextPrimary;

                    // Center the header text in the column
                    float headerTextWidth = FUIRenderer.MeasureText(col.Header, 12f);
                    float centeredX = colX + (colW - headerTextWidth) / 2;
                    FUIRenderer.DrawText(canvas, col.Header, new SKPoint(centeredX, headerTextY), headerColor, 12f, true);
                }

                // Draw column separator on left edge
                using var sepPaint = FUIRenderer.CreateStrokePaint(FUIColors.FrameSubtle);
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
            string emptyMsg = _scInstall.Loading ? _scInstall.LoadingMessage
                : _scInstall.Actions is null && !string.IsNullOrEmpty(_scInstall.LoadingMessage) ? _scInstall.LoadingMessage
                : _scInstall.Actions is null ? "No SC installation found"
                : "No actions match filter";
            FUIRenderer.DrawText(canvas, emptyMsg,
                new SKPoint(leftMargin, scrollY + 20f), FUIColors.TextDim, 14f);
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
                        using var groupBgPaint = FUIRenderer.CreateFillPaint(bgColor);
                        canvas.DrawRect(headerBounds, groupBgPaint);

                        // Collapse/expand indicator
                        float indicatorX = leftMargin + 2;
                        float indicatorY = scrollY + categoryHeaderHeight / 2;
                        FUIWidgets.DrawCollapseIndicator(canvas, indicatorX, indicatorY, isCollapsed, headerHovered);

                        // Count actions in this category (same display name)
                        int categoryActionCount = _scFilteredActions.Count(a =>
                            SCCategoryMapper.GetCategoryNameForAction(a.ActionMap, a.ActionName) == categoryName);
                        int categoryBoundCount = _scFilteredActions.Count(a =>
                            SCCategoryMapper.GetCategoryNameForAction(a.ActionMap, a.ActionName) == categoryName &&
                            _scExportProfile.GetBinding(a.ActionMap, a.ActionName) is not null);

                        FUIRenderer.DrawText(canvas, categoryName,
                            new SKPoint(leftMargin + 18, scrollY + categoryHeaderHeight / 2 + 4),
                            headerHovered ? FUIColors.TextBright : FUIColors.Primary, 13f, true);

                        // Action count
                        string countStr = categoryBoundCount > 0
                            ? $"({categoryBoundCount}/{categoryActionCount})"
                            : $"({categoryActionCount})";
                        FUIRenderer.DrawText(canvas, countStr,
                            new SKPoint(leftMargin + actionColWidth - 60, scrollY + categoryHeaderHeight / 2 + 4),
                            FUIColors.TextDim, 12f);
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
                    bool isConflictHighlight = i == _conflicts.HighlightActionIndex
                        && (DateTime.Now - _conflicts.HighlightStartTime).TotalSeconds < 1.5;

                    if (isSelected)
                    {
                        using var selPaint = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(FUIColors.AlphaGlow));
                        canvas.DrawRect(rowBounds, selPaint);
                    }
                    else if (isHovered)
                    {
                        using var hoverPaint = FUIRenderer.CreateFillPaint(FUIColors.PanelBgDefault);
                        canvas.DrawRect(rowBounds, hoverPaint);
                    }
                    else if (isEvenRow)
                    {
                        // Subtle alternating row background
                        using var altPaint = FUIRenderer.CreateFillPaint(FUIColors.Background2.WithAlpha(40));
                        canvas.DrawRect(rowBounds, altPaint);
                    }

                    // Primary highlight pulse when navigated to from a conflict link
                    if (isConflictHighlight)
                    {
                        float t = (float)(DateTime.Now - _conflicts.HighlightStartTime).TotalSeconds / 1.5f;
                        byte alpha = (byte)(Math.Max(0, 1f - t) * 120);
                        using var highlightPaint = FUIRenderer.CreateFillPaint(FUIColors.Primary.WithAlpha(alpha));
                        canvas.DrawRect(rowBounds, highlightPaint);
                        _ctx.MarkDirty(); // keep redrawing while animating
                    }

                    float textY = scrollY + rowHeight / 2 + 4;

                    // Draw action name with ellipsis if too long. Prefer SC's localised label
                    // when available; fall back to the mechanical formatter otherwise.
                    float actionIndent = 18f;
                    string displayName = action.DisplayLabel
                        ?? SCCategoryMapper.FormatActionName(action.ActionName);
                    float maxNameWidth = actionColWidth - actionIndent - 10f;
                    displayName = FUIWidgets.TruncateTextToWidth(displayName, maxNameWidth, 10f);
                    var nameColor = FUIColors.ContentColor(isSelected);
                    FUIRenderer.DrawText(canvas, displayName, new SKPoint(leftMargin + actionIndent, textY), nameColor, 13f);

                    // Draw device column cells (clipped)
                    canvas.Save();
                    canvas.ClipRect(new SKRect(deviceColsStart, scrollY, deviceColsStart + visibleDeviceWidth, scrollY + rowHeight));

                    for (int c = 0; c < columns.Count; c++)
                    {
                        float colW = colWidths[c];
                        float colX = deviceColsStart + colXPositions[c] - _grid.HorizontalScroll;
                        if (colX + colW > deviceColsStart && colX < deviceColsStart + visibleDeviceWidth)
                        {
                            var col = columns[c];
                            var cellBounds = new SKRect(colX, scrollY, colX + colW, scrollY + rowHeight);

                            // Check cell state
                            bool isCellHovered = _cell.HoveredCell == (i, c);
                            bool isCellSelected = _cell.SelectedCell == (i, c);
                            bool isCellListening = _scListening.IsListening && _cell.SelectedCell == (i, c);
                            bool isColumnHighlighted = c == _colImport.HighlightedColumn;

                            // Draw column highlight background
                            if (isColumnHighlighted && !isCellSelected && !isCellListening)
                            {
                                using var colHighlightPaint = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(20));
                                canvas.DrawRect(cellBounds, colHighlightPaint);
                            }

                            // Draw cell background for hover/selection/listening states
                            if (isCellListening)
                            {
                                // Listening state - use Active color to match theme
                                using var listeningBgPaint = FUIRenderer.CreateFillPaint(FUIColors.SelectionBg);
                                canvas.DrawRect(cellBounds, listeningBgPaint);

                                // Draw countdown progress bar at bottom of cell
                                float elapsed = (float)(DateTime.Now - _scListening.StartTime).TotalMilliseconds;
                                float progress = Math.Max(0, 1.0f - elapsed / SCListeningTimeoutMs);
                                float barHeight = 3f;
                                float barWidth = (cellBounds.Width - 4) * progress;
                                var progressBounds = new SKRect(cellBounds.Left + 2, cellBounds.Bottom - barHeight - 2,
                                                                cellBounds.Left + 2 + barWidth, cellBounds.Bottom - 2);
                                using var progressPaint = FUIRenderer.CreateFillPaint(FUIColors.Active);
                                canvas.DrawRoundRect(progressBounds, 1.5f, 1.5f, progressPaint);

                                // Pulsing border
                                float pulse = (float)(0.6 + 0.4 * Math.Sin((DateTime.Now - _scListening.StartTime).TotalMilliseconds / 150.0));
                                using var borderPaint = FUIRenderer.CreateStrokePaint(FUIColors.Active.WithAlpha((byte)(200 * pulse)), 2f);
                                canvas.DrawRect(cellBounds.Inset(1, 1), borderPaint);
                            }
                            else if (isCellSelected)
                            {
                                using var selectedPaint = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(50));
                                canvas.DrawRect(cellBounds, selectedPaint);
                            }
                            else if (isCellHovered)
                            {
                                using var hoverPaint = FUIRenderer.CreateFillPaint(FUIColors.Primary.WithAlpha(30));
                                canvas.DrawRect(cellBounds, hoverPaint);
                            }

                            // Check if this cell is shared (vJoy columns only)
                            string sharedCellKey = col.IsJoystick && !col.IsPhysical && !col.IsReadOnly
                                ? $"{action.Key}|{col.VJoyDeviceId}"
                                : string.Empty;
                            bool isCellShared = !string.IsNullOrEmpty(sharedCellKey)
                                && _conflicts.SharedCells.ContainsKey(sharedCellKey);

                            List<string>? bindingComponents = null;
                            SKColor textColor = FUIColors.TextPrimary;
                            SCInputType? inputType = null;
                            bool isConflicting = false;
                            bool isDuplicateAction = false;

                            // All bindings now come from the profile (SCVirtStick model)
                            // No separate "defaults" - profile contains everything
                            SCActionBinding? binding = null;

                            if (col.IsPhysical)
                            {
                                // Physical device column: match by PhysicalDeviceId
                                binding = _scExportProfile.Bindings.FirstOrDefault(b =>
                                    b.ActionMap == action.ActionMap && b.ActionName == action.ActionName &&
                                    b.DeviceType == SCDeviceType.Joystick &&
                                    b.PhysicalDeviceId == col.PhysicalDeviceKey);
                            }
                            else if (col.IsJoystick)
                            {
                                // vJoy column: match by VJoyDevice → SCInstance
                                binding = _scExportProfile.Bindings.FirstOrDefault(b =>
                                    b.ActionMap == action.ActionMap && b.ActionName == action.ActionName &&
                                    b.DeviceType == SCDeviceType.Joystick &&
                                    b.PhysicalDeviceId is null &&
                                    _scExportProfile.GetSCInstance(b.VJoyDevice) == col.SCInstance);
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
                                bindingComponents = SCBindingsRenderer.GetBindingComponents(binding.InputName, binding.Modifiers);
                                inputType = binding.InputType;
                                // Check for conflicts and cross-column action duplicates
                                if (!isCellShared)
                                {
                                    isConflicting = _conflicts.ConflictingBindings.Contains(binding.Key)
                                        || _conflicts.NetworkConflictKeys.Contains(binding.Key);
                                    if (col.IsJoystick)
                                        isDuplicateAction = _conflicts.DuplicateActionBindings.Contains(binding.Key);
                                }
                            }

                            // For shared cells with no primary binding on this column, synthesize from secondary input name.
                            // A share is a standalone input reference — it does not inherit the primary's
                            // modifiers. Modifiers on a shared device (throttle, button box) are impractical,
                            // so the secondary fires the action on its own without needing modifiers held.
                            if (binding is null && isCellShared)
                            {
                                var (primaryVJoy, _, secondaryInputName) = _conflicts.SharedCells[sharedCellKey];
                                bindingComponents = SCBindingsRenderer.GetBindingComponents(secondaryInputName, null);
                                inputType = InferInputTypeFromName(secondaryInputName);
                                textColor = FUIColors.Primary.WithAlpha(180);

                                // Propagate the conflict stripe from the primary binding this cell reroutes to
                                var primaryBinding = _scExportProfile.Bindings.FirstOrDefault(b =>
                                    b.ActionMap == action.ActionMap && b.ActionName == action.ActionName &&
                                    b.DeviceType == SCDeviceType.Joystick && b.PhysicalDeviceId is null &&
                                    b.VJoyDevice == primaryVJoy);
                                if (primaryBinding is not null)
                                    isConflicting = _conflicts.ConflictingBindings.Contains(primaryBinding.Key)
                                        || _conflicts.NetworkConflictKeys.Contains(primaryBinding.Key);
                            }

                            // Draw cell content
                            if (isCellListening)
                            {
                                // Show modifier hint if a modifier is pending or held
                                string listeningText = "PRESS INPUT";
                                if (col.IsJoystick && _scModifierKeys.Count > 0)
                                {
                                    // Priority 1: confirmed pending modifier (user already pressed modifier button)
                                    string? heldMod = _scListening.PendingModifiers?.FirstOrDefault()?.ToUpperInvariant();
                                    // Priority 2: modifier VK currently held
                                    heldMod ??= _scModifierKeys
                                        .Where(kv => IsKeyHeld(kv.Key))
                                        .Select(kv => kv.Value.ToUpperInvariant())
                                        .FirstOrDefault();
                                    if (heldMod is not null)
                                        listeningText = $"{heldMod}+PRESS";
                                }
                                float listeningFontSize = 9f;
                                float listeningTextWidth = FUIRenderer.MeasureText(listeningText, listeningFontSize);
                                float listeningTextX = colX + (colW - listeningTextWidth) / 2;
                                FUIRenderer.DrawText(canvas, listeningText, new SKPoint(listeningTextX, textY - 2), FUIColors.Active, listeningFontSize, true);
                            }
                            else if (bindingComponents is not null && bindingComponents.Count > 0)
                            {
                                // Draw multiple keycap badges for binding (one per key component)
                                SKColor badgeColor = isCellSelected ? FUIColors.TextBright : textColor;
                                SCBindingsRenderer.DrawMultiKeycapBinding(canvas, cellBounds, bindingComponents, badgeColor,
                                    col.IsJoystick ? inputType : null, isConflicting, isDuplicateAction, isCellShared);
                            }
                            else
                            {
                                // Draw empty indicator, centered
                                FUIRenderer.DrawText(canvas, "—", new SKPoint(colX + colW / 2 - 4, textY), FUIColors.TextDim.WithAlpha(100), 14f);
                            }

                            // Draw column separator
                            using var sepPaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame.WithAlpha(40));
                            canvas.DrawLine(colX, scrollY, colX, scrollY + rowHeight, sepPaint);

                            // Draw selection border for selected cell
                            if (isCellSelected && !isCellListening)
                            {
                                using var borderPaint = FUIRenderer.CreateStrokePaint(FUIColors.Active, 1.5f);
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
        _scroll.VScrollBounds = SKRect.Empty;
        _scroll.VThumbBounds = SKRect.Empty;
        if (_scBindingsContentHeight > _scBindingsListBounds.Height)
        {
            float scrollbarWidth = 8f;
            float scrollbarX = rightMargin - scrollbarWidth + 10;
            _scroll.VScrollBounds = new SKRect(scrollbarX, listTop, scrollbarX + scrollbarWidth, listTop + _scBindingsListBounds.Height);

            bool vScrollHovered = _scroll.VScrollBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y) || _scroll.IsDraggingVScroll;
            FUIWidgets.DrawScrollbar(canvas, _scroll.VScrollBounds, _scBindingsScrollOffset,
                _scBindingsContentHeight, _scBindingsListBounds.Height, vScrollHovered, out var vThumb);
            _scroll.VThumbBounds = vThumb;
        }

        // Horizontal scrollbar if needed
        _scroll.HScrollBounds = SKRect.Empty;
        _scroll.HThumbBounds = SKRect.Empty;
        if (needsHorizontalScroll)
        {
            float scrollbarHeight = 8f;
            float scrollbarY = listBottom + 5f;
            _scroll.HScrollBounds = new SKRect(deviceColsStart, scrollbarY, deviceColsStart + visibleDeviceWidth, scrollbarY + scrollbarHeight);

            bool hScrollHovered = _scroll.HScrollBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y) || _scroll.IsDraggingHScroll;
            FUIWidgets.DrawScrollbar(canvas, _scroll.HScrollBounds, _grid.HorizontalScroll,
                totalDeviceColsWidth, visibleDeviceWidth, hScrollHovered, out var hThumb, isHorizontal: true);
            _scroll.HThumbBounds = hThumb;
        }
    }

    private string? GetPhysicalDeviceNameForVJoyColumn(SCGridColumn col)
    {
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return null;
        if (!profile.VJoyPrimaryDevices.TryGetValue(col.VJoyDeviceId, out var guid)) return null;

        var device = _ctx.Devices.Concat(_ctx.DisconnectedDevices)
            .FirstOrDefault(d => d.InstanceGuid.ToString().Equals(guid, StringComparison.OrdinalIgnoreCase));
        return device?.Name;
    }

}