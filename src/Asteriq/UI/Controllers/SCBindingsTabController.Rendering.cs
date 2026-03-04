using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class SCBindingsTabController
{
    private void DrawBindingsTabContent(SKCanvas canvas, SKRect bounds, float pad, float contentTop, float contentBottom)
    {
        float frameInset = 5f;
        var contentBounds = new SKRect(pad, contentTop, bounds.Right - pad, contentBottom);

        // Two-panel layout: Left (bindings table) | Right (Installation + Device Order/Profiles stacked)
        float rightPanelWidth = Math.Min(500f, Math.Max(280f, contentBounds.Width * 0.24f));
        float gap = 10f;

        var leftBounds = new SKRect(contentBounds.Left, contentBounds.Top,
            contentBounds.Right - rightPanelWidth - gap, contentBounds.Bottom);
        var rightBounds = new SKRect(leftBounds.Right + gap, contentBounds.Top,
            contentBounds.Right, contentBounds.Bottom);

        // Right panel stacking order (top → bottom):
        //   1. SC Installation (compact: no title, just dropdown)
        //   2. Device Order and Control Profiles — mutually exclusive expand/collapse
        //   3. Column Actions (fixed 235px at bottom, when a vJoy column is selected)
        float verticalGap = 8f;
        float installationHeight = 90f;

        var installationBounds = new SKRect(rightBounds.Left, rightBounds.Top,
            rightBounds.Right, rightBounds.Top + installationHeight);

        const float CollapsedPanelH = 52f;
        float columnActionsHeight = 235f;
        float verticalGap2 = 8f;

        int vjoyDeviceCount = _ctx.VJoyDevices.Count(v => v.Exists);
        bool hasDeviceOrder = vjoyDeviceCount > 0;

        bool showColumnActions = _colImport.HighlightedColumn >= 0
            && _grid.Columns is not null
            && _colImport.HighlightedColumn < _grid.Columns.Count
            && _grid.Columns[_colImport.HighlightedColumn].IsJoystick
            && !_grid.Columns[_colImport.HighlightedColumn].IsPhysical
            && !_grid.Columns[_colImport.HighlightedColumn].IsReadOnly;

        float afterInstall = installationBounds.Bottom + verticalGap;
        float bottomAreaBottom = rightBounds.Bottom;
        SKRect columnActionsBounds = SKRect.Empty;
        if (showColumnActions)
        {
            columnActionsBounds = new SKRect(rightBounds.Left, bottomAreaBottom - columnActionsHeight,
                rightBounds.Right, bottomAreaBottom);
            bottomAreaBottom = columnActionsBounds.Top - verticalGap2;
        }

        // Accordion layout: Device Order always on top, Control Profiles always on bottom.
        // Only one is expanded at a time; the collapsed panel shows a fixed-height title bar.
        SKRect deviceOrderBounds = SKRect.Empty;
        SKRect controlProfilesBounds;
        if (hasDeviceOrder)
        {
            if (_deviceOrder.IsExpanded)
            {
                // Control Profiles: collapsed title bar anchored at the very bottom
                float cpTop = bottomAreaBottom - CollapsedPanelH;
                controlProfilesBounds = new SKRect(rightBounds.Left, cpTop, rightBounds.Right, bottomAreaBottom);
                // Device Order: fills from top down to just above Control Profiles
                float devH = Math.Max(CollapsedPanelH, cpTop - verticalGap - afterInstall);
                deviceOrderBounds = new SKRect(rightBounds.Left, afterInstall, rightBounds.Right, afterInstall + devH);
            }
            else
            {
                // Device Order: collapsed title bar at the top
                deviceOrderBounds = new SKRect(rightBounds.Left, afterInstall, rightBounds.Right, afterInstall + CollapsedPanelH);
                // Control Profiles: fills the rest
                controlProfilesBounds = new SKRect(rightBounds.Left, deviceOrderBounds.Bottom + verticalGap,
                    rightBounds.Right, bottomAreaBottom);
            }
        }
        else
        {
            controlProfilesBounds = new SKRect(rightBounds.Left, afterInstall, rightBounds.Right, bottomAreaBottom);
        }

        // LEFT PANEL
        DrawSCBindingsTablePanel(canvas, leftBounds, frameInset);

        // RIGHT 1 — SC Installation (no title, just dropdown)
        DrawSCInstallationPanelCompact(canvas, installationBounds, frameInset);

        // RIGHT 2 — Device Order (collapsible, collapsed by default)
        if (hasDeviceOrder)
            DrawDeviceOrderPanel(canvas, deviceOrderBounds, frameInset, _deviceOrder.IsExpanded);

        // RIGHT 3 — Control Profiles (expanded when Device Order is collapsed)
        bool controlProfilesExpanded = !hasDeviceOrder || !_deviceOrder.IsExpanded;
        DrawSCExportPanelCompact(canvas, controlProfilesBounds, frameInset,
            suppressActionInfo: showColumnActions, isExpanded: controlProfilesExpanded, isCollapsible: hasDeviceOrder);

        // RIGHT 4 — Column Actions (when a vJoy column is selected)
        if (showColumnActions)
            DrawColumnActionsPanel(canvas, columnActionsBounds, frameInset);

        // Draw dropdowns last (on top) so they render over all panels
        if (_profileMgmt.DropdownOpen && !_profileMgmt.DropdownListBounds.IsEmpty)
            DrawSCProfileDropdownList(canvas, _profileMgmt.DropdownListBounds);
        if (_scInstall.DropdownOpen && _scInstall.Installations.Count > 0)
            DrawSCInstallationDropdown(canvas);
        if (_searchFilter.FilterDropdownOpen && _searchFilter.ActionMaps.Count > 0)
            DrawSCActionMapFilterDropdown(canvas);
        if (showColumnActions && _colImport.ProfileDropdownOpen)
            DrawColImportProfileDropdown(canvas);
        if (showColumnActions && _colImport.ColumnDropdownOpen && _colImport.SourceColumns.Count > 0)
            DrawColImportColumnDropdown(canvas);
        if (_deviceOrder.IsExpanded && _deviceOrder.OpenRow >= 0)
            DrawDeviceOrderDropdown(canvas);
    }

    private void DrawSCInstallationPanelCompact(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        var m = FUIRenderer.DrawPanelChrome(canvas, bounds);
        float y = m.Y;
        FUIWidgets.DrawPanelTitle(canvas, m.LeftMargin, m.RightMargin, ref y, "GAME ENVIRONMENT");

        float selectorHeight = 32f;
        _scInstall.SelectorBounds = new SKRect(m.LeftMargin, y, m.RightMargin, y + selectorHeight);

        string installationText = _scInstall.Installations.Count > 0 && _scInstall.SelectedInstallation < _scInstall.Installations.Count
            ? _scInstall.Installations[_scInstall.SelectedInstallation].DisplayName
            : "No SC found";

        bool selectorHovered = _scInstall.SelectorBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawSelector(canvas, _scInstall.SelectorBounds, installationText, selectorHovered || _scInstall.DropdownOpen, _scInstall.Installations.Count > 0);
    }

    private void DrawSCInstallationDropdown(SKCanvas canvas)
    {
        float itemH = 28f;
        _scInstall.DropdownBounds = new SKRect(
            _scInstall.SelectorBounds.Left,
            _scInstall.SelectorBounds.Bottom + 2,
            _scInstall.SelectorBounds.Right,
            _scInstall.SelectorBounds.Bottom + 2 + Math.Min(_scInstall.Installations.Count * itemH + 8f, 200f));

        var items = _scInstall.Installations.Select(s => s.DisplayName).ToList();
        FUIWidgets.DrawDropdownPanel(canvas, _scInstall.DropdownBounds, items,
            _scInstall.SelectedInstallation, _scInstall.HoveredInstallation, itemH);
    }

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
            ? _scSchemaService.FilterJoystickActions(_scInstall.Actions).Count
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
        float gap = 10f;

        // Category filter dropdown on the right
        float filterX = rightMargin - filterWidth;
        _searchFilter.FilterBounds = new SKRect(filterX, y, rightMargin, y + filterRowHeight);
        string filterText = string.IsNullOrEmpty(_searchFilter.ActionMapFilter) ? "All Categories" : _searchFilter.ActionMapFilter;
        bool filterHovered = _searchFilter.FilterBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawSelector(canvas, _searchFilter.FilterBounds, filterText, filterHovered || _searchFilter.FilterDropdownOpen, _searchFilter.ActionMaps.Count > 0);

        // Search box on the left (max 280px wide)
        float maxSearchWidth = 280f;
        _searchFilter.SearchBoxBounds = new SKRect(leftMargin, y, leftMargin + maxSearchWidth, y + filterRowHeight);
        FUIWidgets.DrawSearchBox(canvas, _searchFilter.SearchBoxBounds, _searchFilter.SearchText, _searchFilter.SearchBoxFocused, _ctx.MousePosition);

        // "Bound only" checkbox
        float checkboxX = leftMargin + maxSearchWidth + gap;
        _searchFilter.ShowBoundOnlyBounds = new SKRect(checkboxX, y + (filterRowHeight - checkboxSize) / 2,
            checkboxX + checkboxSize, y + (filterRowHeight + checkboxSize) / 2);
        _searchFilter.ShowBoundOnlyHovered = _searchFilter.ShowBoundOnlyBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawSCCheckbox(canvas, _searchFilter.ShowBoundOnlyBounds, showBoundOnly, _searchFilter.ShowBoundOnlyHovered);

        float boundOnlyLabelX = checkboxX + checkboxSize + 6f;
        FUIRenderer.DrawText(canvas, "Bound only", new SKPoint(boundOnlyLabelX, y + filterRowHeight / 2 + 4),
            showBoundOnly ? FUIColors.Active : FUIColors.TextDim, 13f);

        // "Show JS ref" checkbox — 16px after "Bound only" label
        float boundOnlyLabelW = FUIRenderer.MeasureText("Bound only", 13f);
        float jsRefCheckboxX = boundOnlyLabelX + boundOnlyLabelW + 16f;
        bool showJSRef = !_ctx.AppSettings.SCBindingsShowPhysicalHeaders;
        _searchFilter.ShowJSRefBounds = new SKRect(jsRefCheckboxX, y + (filterRowHeight - checkboxSize) / 2,
            jsRefCheckboxX + checkboxSize, y + (filterRowHeight + checkboxSize) / 2);
        _searchFilter.ShowJSRefHovered = _searchFilter.ShowJSRefBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawSCCheckbox(canvas, _searchFilter.ShowJSRefBounds, showJSRef, _searchFilter.ShowJSRefHovered);
        float jsRefLabelX = jsRefCheckboxX + checkboxSize + 6f;
        FUIRenderer.DrawText(canvas, "Show JS ref", new SKPoint(jsRefLabelX, y + filterRowHeight / 2 + 4),
            showJSRef ? FUIColors.Active : FUIColors.TextDim, 13f);

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
        using var headerPaint = FUIRenderer.CreateFillPaint(FUIColors.Background2.WithAlpha(120));
        canvas.DrawRect(new SKRect(leftMargin - 5, y, rightMargin + 5, y + headerRowHeight), headerPaint);

        // Store column headers bounds for click detection
        _grid.ColumnHeadersBounds = new SKRect(deviceColsStart, y, deviceColsStart + visibleDeviceWidth, y + headerRowHeight);

        // Draw ACTION column header
        FUIRenderer.DrawText(canvas, "ACTION", new SKPoint(leftMargin + 18f, headerTextY), FUIColors.TextDim, 12f, true);

        // Draw separator after ACTION column
        using var actionSepPaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame.WithAlpha(80));
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

                // Highlight background if this column is selected (read-only columns cannot be highlighted)
                if (c == _colImport.HighlightedColumn && !col.IsReadOnly)
                {
                    using var highlightPaint = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(40));
                    canvas.DrawRect(new SKRect(colX, y, colX + colW, y + headerRowHeight), highlightPaint);
                }

                if (col.IsReadOnly)
                {
                    // Read-only column: dimmed header + "NO DEVICE" sub-label
                    float headerTextWidth = FUIRenderer.MeasureText(col.Header, 12f);
                    float centeredX = colX + (colW - headerTextWidth) / 2;
                    FUIRenderer.DrawText(canvas, col.Header, new SKPoint(centeredX, headerTextY - 5f), FUIColors.TextDim, 12f, true);
                    float subLabelWidth = FUIRenderer.MeasureText("NO DEVICE", 12f);
                    FUIRenderer.DrawText(canvas, "NO DEVICE", new SKPoint(colX + (colW - subLabelWidth) / 2, headerTextY + 5f), FUIColors.TextDim.WithAlpha(120), 12f);
                }
                else if (col.IsPhysical)
                {
                    // Physical device column: device name on top, "JS{N}" sub-label below
                    var headerColor = c == _colImport.HighlightedColumn ? FUIColors.Active : FUIColors.TextPrimary;
                    float headerTextWidth = FUIRenderer.MeasureText(col.Header, 10f);
                    float centeredX = colX + (colW - headerTextWidth) / 2;
                    FUIRenderer.DrawText(canvas, col.Header, new SKPoint(centeredX, headerTextY - 5f), headerColor, 10f, true);
                    string jsLabel = $"JS{col.SCInstance}";
                    float subLabelWidth = FUIRenderer.MeasureText(jsLabel, 10f);
                    FUIRenderer.DrawText(canvas, jsLabel, new SKPoint(colX + (colW - subLabelWidth) / 2, headerTextY + 5f), FUIColors.Active.WithAlpha(180), 10f);
                }
                else if (col.IsJoystick && _ctx.AppSettings.SCBindingsShowPhysicalHeaders)
                {
                    // Device mode: show physical device name only — no JS# (user can toggle to see that)
                    var headerColor = c == _colImport.HighlightedColumn ? FUIColors.Active : FUIColors.TextPrimary;
                    string? deviceName = GetPhysicalDeviceNameForVJoyColumn(col);
                    if (deviceName is not null)
                    {
                        string shortName = FUIWidgets.TruncateTextToWidth(deviceName, colW - 4f, 11f);
                        float nameTextWidth = FUIRenderer.MeasureText(shortName, 11f);
                        FUIRenderer.DrawText(canvas, shortName, new SKPoint(colX + (colW - nameTextWidth) / 2, headerTextY), headerColor, 11f, true);
                    }
                    else
                    {
                        // No physical device mapped — dim dash
                        float dashWidth = FUIRenderer.MeasureText("—", 12f);
                        FUIRenderer.DrawText(canvas, "—", new SKPoint(colX + (colW - dashWidth) / 2, headerTextY), FUIColors.TextDim.WithAlpha(80), 12f);
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
                using var sepPaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame.WithAlpha(50));
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
                        using var selPaint = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(60));
                        canvas.DrawRect(rowBounds, selPaint);
                    }
                    else if (isHovered)
                    {
                        using var hoverPaint = FUIRenderer.CreateFillPaint(FUIColors.Background2.WithAlpha(120));
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

                    // Draw action name with ellipsis if too long
                    float actionIndent = 18f;
                    string displayName = SCCategoryMapper.FormatActionName(action.ActionName);
                    float maxNameWidth = actionColWidth - actionIndent - 10f;
                    displayName = FUIWidgets.TruncateTextToWidth(displayName, maxNameWidth, 10f);
                    var nameColor = isSelected ? FUIColors.Active : FUIColors.TextPrimary;
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
                                using var listeningBgPaint = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(40));
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
                                    b.PhysicalDeviceId == col.PhysicalDevice!.HidDevicePath);
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
                                // Check for conflicts and cross-column action duplicates (joystick only)
                                if (col.IsJoystick && !isCellShared)
                                {
                                    isConflicting = _conflicts.ConflictingBindings.Contains(binding.Key)
                                        || _conflicts.NetworkConflictKeys.Contains(binding.Key);
                                    isDuplicateAction = _conflicts.DuplicateActionBindings.Contains(binding.Key);
                                }
                            }

                            // For shared cells with no primary binding on this column, synthesize from secondary input name
                            if (binding is null && isCellShared)
                            {
                                var (primaryVJoy, _, secondaryInputName) = _conflicts.SharedCells[sharedCellKey];
                                bindingComponents = SCBindingsRenderer.GetBindingComponents(secondaryInputName, new List<string>());
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
            float scrollbarWidth = 8f;  // Slightly wider for easier clicking
            float scrollbarX = rightMargin - scrollbarWidth + 10;
            float scrollbarHeight = _scBindingsListBounds.Height;
            float thumbHeight = Math.Max(30f, scrollbarHeight * (_scBindingsListBounds.Height / _scBindingsContentHeight));
            float maxVScroll = _scBindingsContentHeight - _scBindingsListBounds.Height;
            float thumbY = listTop + (maxVScroll > 0 ? (_scBindingsScrollOffset / maxVScroll) * (scrollbarHeight - thumbHeight) : 0);

            _scroll.VScrollBounds = new SKRect(scrollbarX, listTop, scrollbarX + scrollbarWidth, listTop + scrollbarHeight);
            _scroll.VThumbBounds = new SKRect(scrollbarX, thumbY, scrollbarX + scrollbarWidth, thumbY + thumbHeight);

            bool vScrollHovered = _scroll.VScrollBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y) || _scroll.IsDraggingVScroll;

            using var trackPaint = FUIRenderer.CreateFillPaint(FUIColors.Background2.WithAlpha(vScrollHovered ? (byte)120 : (byte)80));
            canvas.DrawRoundRect(_scroll.VScrollBounds, 4f, 4f, trackPaint);

            using var thumbPaint = FUIRenderer.CreateFillPaint(vScrollHovered ? FUIColors.Active : FUIColors.Frame.WithAlpha(180));
            canvas.DrawRoundRect(_scroll.VThumbBounds, 4f, 4f, thumbPaint);
        }

        // Horizontal scrollbar if needed
        _scroll.HScrollBounds = SKRect.Empty;
        _scroll.HThumbBounds = SKRect.Empty;
        if (needsHorizontalScroll)
        {
            float scrollbarHeight = 8f;  // Slightly taller for easier clicking
            float scrollbarY = listBottom + 5f;
            float scrollbarWidth = visibleDeviceWidth;
            float thumbWidth = Math.Max(30f, scrollbarWidth * (visibleDeviceWidth / totalDeviceColsWidth));
            float maxHScroll = totalDeviceColsWidth - visibleDeviceWidth;
            float thumbX = deviceColsStart + (maxHScroll > 0 ? (_grid.HorizontalScroll / maxHScroll) * (scrollbarWidth - thumbWidth) : 0);

            _scroll.HScrollBounds = new SKRect(deviceColsStart, scrollbarY, deviceColsStart + scrollbarWidth, scrollbarY + scrollbarHeight);
            _scroll.HThumbBounds = new SKRect(thumbX, scrollbarY, thumbX + thumbWidth, scrollbarY + scrollbarHeight);

            bool hScrollHovered = _scroll.HScrollBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y) || _scroll.IsDraggingHScroll;

            using var trackPaint = FUIRenderer.CreateFillPaint(FUIColors.Background2.WithAlpha(hScrollHovered ? (byte)120 : (byte)80));
            canvas.DrawRoundRect(_scroll.HScrollBounds, 4f, 4f, trackPaint);

            using var thumbPaint = FUIRenderer.CreateFillPaint(hScrollHovered ? FUIColors.Active : FUIColors.Frame.WithAlpha(180));
            canvas.DrawRoundRect(_scroll.HThumbBounds, 4f, 4f, thumbPaint);
        }
    }

    private void DrawSCExportPanelCompact(SKCanvas canvas, SKRect bounds, float frameInset,
        bool suppressActionInfo = false, bool isExpanded = true, bool isCollapsible = false)
    {
        float cornerLen = isExpanded ? 30f : Math.Min(16f, bounds.Height * 0.35f);
        var m = FUIRenderer.DrawPanelChrome(canvas, bounds, cornerLength: cornerLen);
        float y = m.Y;
        float leftMargin = m.LeftMargin;
        float rightMargin = m.RightMargin;
        float buttonGap = 6f;

        // Header bounds for click-to-expand handling (when part of the mutual-exclusive group)
        _deviceOrder.ControlProfilesHeaderBounds = isCollapsible
            ? new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Top + 52f)
            : SKRect.Empty;
        bool headerHovered = isCollapsible && !isExpanded
            && _deviceOrder.ControlProfilesHeaderBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

        FUIWidgets.DrawPanelTitle(canvas, leftMargin, rightMargin, ref y, "CONTROL PROFILES");

        // Expand/collapse indicator (only shown when part of the mutual-exclusive group)
        if (isCollapsible)
        {
            string indicator = isExpanded ? "-" : "+";
            float indW = FUIRenderer.MeasureText(indicator, 13f);
            FUIRenderer.DrawText(canvas, indicator, new SKPoint(rightMargin - indW, y - 18f),
                headerHovered ? FUIColors.TextBright : FUIColors.Active.WithAlpha(isExpanded ? (byte)100 : (byte)180),
                13f, true);
        }

        if (!isExpanded) return;

        // Control Profile dropdown (full width)
        float dropdownHeight = 32f;
        _profileMgmt.DropdownBounds = new SKRect(leftMargin, y, rightMargin, y + dropdownHeight);
        bool dropdownHovered = _profileMgmt.DropdownBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        string dropdownLabel = string.IsNullOrEmpty(_scExportProfile.ProfileName)
            ? "— No Profile Selected —"
            : _scProfileDirty ? $"{_scExportProfile.ProfileName}*" : _scExportProfile.ProfileName;
        SCBindingsRenderer.DrawSCProfileDropdown(canvas, _profileMgmt.DropdownBounds, dropdownLabel, dropdownHovered, _profileMgmt.DropdownOpen);

        // Pencil edit icon inside dropdown box (left of arrow), visible on hover when a profile is loaded
        bool hasProfile = !string.IsNullOrEmpty(_scExportProfile.ProfileName);
        if (hasProfile && dropdownHovered && !_profileMgmt.DropdownOpen)
        {
            float editSize = 20f;
            float editX = _profileMgmt.DropdownBounds.Right - 28f - editSize;
            float editY = _profileMgmt.DropdownBounds.MidY - editSize / 2f;
            _profileMgmt.ProfileEditBounds = new SKRect(editX, editY, editX + editSize, editY + editSize);
            _profileMgmt.ProfileEditHovered = _profileMgmt.ProfileEditBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

            // Draw pencil icon
            var iconColor = _profileMgmt.ProfileEditHovered ? FUIColors.Active : FUIColors.TextDim;
            float cx = _profileMgmt.ProfileEditBounds.MidX;
            float cy = _profileMgmt.ProfileEditBounds.MidY;
            using var penPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = iconColor,
                StrokeWidth = 1.2f,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round
            };
            // Pencil body (diagonal line)
            canvas.DrawLine(cx - 4f, cy + 4f, cx + 3f, cy - 3f, penPaint);
            // Pencil tip
            canvas.DrawLine(cx - 4f, cy + 4f, cx - 5f, cy + 5.5f, penPaint);
            // Pencil top
            canvas.DrawLine(cx + 3f, cy - 3f, cx + 5f, cy - 5f, penPaint);
        }
        else
        {
            _profileMgmt.ProfileEditBounds = SKRect.Empty;
            _profileMgmt.ProfileEditHovered = false;
        }

        y += dropdownHeight + 6f;

        // Buttons row: [Import]  [+ New]  [Save] — all text-width sized, right-aligned
        const float textBtnPad = 16f;
        float textBtnHeight = FUIRenderer.TouchTargetMinHeight;  // 24px minimum
        float saveBtnWidth = FUIRenderer.MeasureText("Save", 14f) + textBtnPad;
        float newBtnWidth = FUIRenderer.MeasureText("+ New", 14f) + textBtnPad;
        float importBtnWidth = FUIRenderer.MeasureText("Import", 14f) + textBtnPad;

        // Save button (rightmost)
        _profileMgmt.SaveProfileBounds = new SKRect(rightMargin - saveBtnWidth, y, rightMargin, y + textBtnHeight);
        _profileMgmt.SaveProfileHovered = _profileMgmt.SaveProfileBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIRenderer.DrawButton(canvas, _profileMgmt.SaveProfileBounds, "Save",
            _profileMgmt.SaveProfileHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

        // New button (left of Save)
        float newBtnX = rightMargin - saveBtnWidth - buttonGap - newBtnWidth;
        _profileMgmt.NewProfileBounds = new SKRect(newBtnX, y, newBtnX + newBtnWidth, y + textBtnHeight);
        _profileMgmt.NewProfileHovered = _profileMgmt.NewProfileBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIRenderer.DrawButton(canvas, _profileMgmt.NewProfileBounds, "+ New",
            _profileMgmt.NewProfileHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

        // Import button (left of + New)
        float importBtnX = newBtnX - buttonGap - importBtnWidth;
        _profileMgmt.ImportProfileBounds = new SKRect(importBtnX, y, importBtnX + importBtnWidth, y + textBtnHeight);
        _profileMgmt.ImportProfileHovered = _profileMgmt.ImportProfileBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIRenderer.DrawButton(canvas, _profileMgmt.ImportProfileBounds, "Import",
            _profileMgmt.ImportProfileHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

        y += textBtnHeight + 10f;

        // Compute profile dropdown list bounds so the draw-last pass can render it on top of all panels
        if (_profileMgmt.DropdownOpen)
        {
            int asteriqCount = _profileMgmt.ExportProfiles.Count;
            int scFileCount  = _scAvailableProfiles.Count;
            int remoteCount  = _ctx.RemoteControlProfiles.Count;
            // Accurate height: 8px padding + rows (24px each) + per-section header (16px for sec1, 26px for others: 4+6+16)
            const float sec1HeaderH = 16f;
            const float sepHeaderH  = 26f;
            float listHeight = 8f
                + (asteriqCount > 0 ? sec1HeaderH + asteriqCount * 24f : 0f)
                + (scFileCount > 0 ? sepHeaderH + scFileCount * 24f : 0f)
                + (remoteCount > 0 ? sepHeaderH + remoteCount * 24f : 0f);
            listHeight = Math.Min(Math.Max(listHeight, 36f), 280f);
            _profileMgmt.DropdownListBounds = new SKRect(leftMargin, _profileMgmt.DropdownBounds.Bottom + 2, rightMargin, _profileMgmt.DropdownBounds.Bottom + 2 + listHeight);
        }

        // Selected action info with ASSIGN/CLEAR buttons (hidden when column actions panel is active)
        if (!suppressActionInfo && _scSelectedActionIndex >= 0 && _scFilteredActions is not null && _scSelectedActionIndex < _scFilteredActions.Count)
        {
            var selectedAction = _scFilteredActions[_scSelectedActionIndex];
            float lineHeight = 15f;

            // Whitespace separator from profile controls above
            y += 12f;

            FUIRenderer.DrawText(canvas, "SELECTED ACTION", new SKPoint(leftMargin, y), FUIColors.Active, 12f, true);
            y += lineHeight;

            string actionDisplay = FUIWidgets.TruncateTextToWidth(selectedAction.ActionName, rightMargin - leftMargin - 10, 10f);
            FUIRenderer.DrawText(canvas, actionDisplay, new SKPoint(leftMargin, y), FUIColors.TextPrimary, 13f);
            y += lineHeight;

            FUIRenderer.DrawText(canvas, $"Type: {selectedAction.InputType}", new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
            y += lineHeight;

            // Detect if the selected cell is a shared cell
            bool isCellSharedInPanel = false;
            uint panelSharedPrimaryVJoy = 0;
            string panelSharedPrimaryInput = "";
            if (_cell.SelectedCell.colIndex >= 0 && _grid.Columns is not null && _cell.SelectedCell.colIndex < _grid.Columns.Count)
            {
                var panelCol = _grid.Columns[_cell.SelectedCell.colIndex];
                if (panelCol.IsJoystick && !panelCol.IsPhysical)
                {
                    string panelSharedKey = $"{selectedAction.Key}|{panelCol.VJoyDeviceId}";
                    if (_conflicts.SharedCells.TryGetValue(panelSharedKey, out var panelSharedInfo))
                    {
                        isCellSharedInPanel = true;
                        (panelSharedPrimaryVJoy, panelSharedPrimaryInput, _) = panelSharedInfo;
                    }
                }
            }

            if (isCellSharedInPanel)
            {
                int panelPrimaryInstance = _scExportProfile.GetSCInstance(panelSharedPrimaryVJoy);
                string panelPrimaryFormatted = SCBindingsRenderer.FormatInputName(panelSharedPrimaryInput);
                FUIRenderer.DrawText(canvas, $"Routed to JS{panelPrimaryInstance} / {panelPrimaryFormatted}",
                    new SKPoint(leftMargin, y), FUIColors.Primary.WithAlpha(200), 11f, true);
                y += lineHeight;
            }

            y += 6f;

            // Assign/Clear buttons
            float btnWidth = (rightMargin - leftMargin - 8) / 2;
            float btnHeight = 24f;

            _scAssignInputButtonBounds = new SKRect(leftMargin, y, leftMargin + btnWidth, y + btnHeight);
            _scAssignInputButtonHovered = _scAssignInputButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

            if (_scListening.IsListening)
            {
                // Show "listening" state when cell listener is active
                using var waitBgPaint = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(80));
                canvas.DrawRect(_scAssignInputButtonBounds, waitBgPaint);
                FUIRenderer.DrawTextCentered(canvas, "LISTENING...", _scAssignInputButtonBounds, FUIColors.Active, 12f);
            }
            else if (isCellSharedInPanel)
            {
                // Shared cell — ASSIGN is disabled; user must unshare first
                using var disabledAssignPaint = FUIRenderer.CreateFillPaint(FUIColors.Background2.WithAlpha(60));
                canvas.DrawRect(_scAssignInputButtonBounds, disabledAssignPaint);
                FUIRenderer.DrawTextCentered(canvas, "ASSIGN", _scAssignInputButtonBounds, FUIColors.TextDim.WithAlpha(100), 13f);
            }
            else
            {
                FUIRenderer.DrawButton(canvas, _scAssignInputButtonBounds, "ASSIGN",
                    _scAssignInputButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
            }

            _scClearBindingButtonBounds = new SKRect(leftMargin + btnWidth + 8, y, rightMargin, y + btnHeight);
            _scClearBindingButtonHovered = _scClearBindingButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

            if (isCellSharedInPanel)
            {
                // Shared cell — CLEAR becomes UNSHARE (always enabled)
                FUIRenderer.DrawButton(canvas, _scClearBindingButtonBounds, "UNSHARE",
                    _scClearBindingButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
            }
            else
            {
                // Check for existing binding on currently selected cell's column
                SCActionBinding? existingBinding = null;
                if (_cell.SelectedCell.colIndex >= 0 && _grid.Columns is not null && _cell.SelectedCell.colIndex < _grid.Columns.Count)
                {
                    var selCol = _grid.Columns[_cell.SelectedCell.colIndex];
                    if (selCol.IsPhysical)
                    {
                        existingBinding = _scExportProfile.Bindings.FirstOrDefault(b =>
                            b.ActionMap == selectedAction.ActionMap && b.ActionName == selectedAction.ActionName &&
                            b.DeviceType == SCDeviceType.Joystick &&
                            b.PhysicalDeviceId == selCol.PhysicalDevice!.HidDevicePath);
                    }
                    else if (selCol.IsJoystick)
                    {
                        existingBinding = _scExportProfile.Bindings.FirstOrDefault(b =>
                            b.ActionMap == selectedAction.ActionMap && b.ActionName == selectedAction.ActionName &&
                            b.DeviceType == SCDeviceType.Joystick &&
                            b.PhysicalDeviceId is null &&
                            _scExportProfile.GetSCInstance(b.VJoyDevice) == selCol.SCInstance);
                    }
                    else
                    {
                        existingBinding = _scExportProfile.GetBinding(selectedAction.ActionMap, selectedAction.ActionName);
                    }
                }
                else
                {
                    existingBinding = _scExportProfile.GetBinding(selectedAction.ActionMap, selectedAction.ActionName);
                }

                bool hasBinding = existingBinding is not null;

                if (hasBinding)
                {
                    FUIRenderer.DrawButton(canvas, _scClearBindingButtonBounds, "CLEAR",
                        _scClearBindingButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
                }
                else
                {
                    // Disabled clear button
                    using var disabledPaint = FUIRenderer.CreateFillPaint(FUIColors.Background2.WithAlpha(60));
                    canvas.DrawRect(_scClearBindingButtonBounds, disabledPaint);
                    FUIRenderer.DrawTextCentered(canvas, "CLEAR", _scClearBindingButtonBounds, FUIColors.TextDim.WithAlpha(100), 13f);
                }
            }

            y += btnHeight + 10f;

            // Conflict links panel — shown when the selected binding conflicts with other actions
            if (_conflicts.ConflictLinks.Count > 0)
            {
                y += 16f;
                DrawConflictLinksPanel(canvas, leftMargin, rightMargin, y, bounds.Bottom - frameInset - 100f);
            }
        }

        // Button group — built bottom-up so every button has a consistent anchor
        float buttonHeight = 32f;
        float smallBtnHeight = 24f;
        float smallBtnWidth = (rightMargin - leftMargin - FUIRenderer.SpaceSM) / 2;

        float exportBtnY  = bounds.Bottom - frameInset - FUIRenderer.SpaceLG - buttonHeight;
        float clearBtnY   = exportBtnY - FUIRenderer.SpaceSM - smallBtnHeight;
        float statusBannerY = clearBtnY - FUIRenderer.SpaceSM - 24f;

        // CLEAR ALL / RESET DFLTS
        _scClearAllButtonBounds = new SKRect(leftMargin, clearBtnY, leftMargin + smallBtnWidth, clearBtnY + smallBtnHeight);
        _scClearAllButtonHovered = _scClearAllButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        bool hasBoundActions = _scExportProfile.Bindings.Count > 0;
        FUIRenderer.DrawButton(canvas, _scClearAllButtonBounds, "CLEAR ALL",
            !hasBoundActions ? FUIRenderer.ButtonState.Disabled
            : (_scClearAllButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal),
            isDanger: true);

        _scResetDefaultsButtonBounds = new SKRect(leftMargin + smallBtnWidth + FUIRenderer.SpaceSM, clearBtnY, rightMargin, clearBtnY + smallBtnHeight);
        _scResetDefaultsButtonHovered = _scResetDefaultsButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIRenderer.DrawButton(canvas, _scResetDefaultsButtonBounds, "RESET DFLTS",
            _scResetDefaultsButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal,
            isDanger: true);

        // EXPORT TO SC — primary CTA, anchored to panel bottom
        _scExportButtonBounds = new SKRect(leftMargin, exportBtnY, rightMargin, exportBtnY + buttonHeight);
        _scExportButtonHovered = _scExportButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        bool canExport = _scInstall.Installations.Count > 0 && _conflicts.DuplicateActionBindings.Count == 0;
        FUIRenderer.DrawButton(canvas, _scExportButtonBounds, "EXPORT TO SC",
            !canExport ? FUIRenderer.ButtonState.Disabled : (_scExportButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal));

        // Status banner — appears above the button group so it is never clipped
        if (!string.IsNullOrEmpty(_scExportStatus))
        {
            var elapsed = DateTime.Now - _scExportStatusTime;
            if (elapsed.TotalSeconds < 10)
                DrawStatusBanner(canvas, new SKRect(leftMargin, statusBannerY, rightMargin, statusBannerY + 24f));
            else
                _scExportStatus = null;
        }
    }

    private void DrawSCImportDropdown(SKCanvas canvas, SKRect buttonBounds)
    {
        float itemHeight = 28f;
        float dropdownHeight = Math.Min(_scAvailableProfiles.Count * itemHeight + 8f, 200f);

        _scImportDropdownBounds = new SKRect(
            buttonBounds.Left,
            buttonBounds.Bottom + 2,
            buttonBounds.Right,
            buttonBounds.Bottom + 2 + dropdownHeight);

        // Shared FUI chrome
        FUIRenderer.DrawPanelShadow(canvas, _scImportDropdownBounds, 4f, 4f, 15f);
        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active.WithAlpha(30),
            StrokeWidth = 3f,
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(4f, 4f)
        };
        canvas.DrawRect(_scImportDropdownBounds, glowPaint);
        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Void);
        canvas.DrawRect(_scImportDropdownBounds, bgPaint);
        using var innerPaint = FUIRenderer.CreateFillPaint(FUIColors.Background0);
        canvas.DrawRect(_scImportDropdownBounds.Inset(2, 2), innerPaint);
        FUIRenderer.DrawLCornerFrame(canvas, _scImportDropdownBounds, FUIColors.Active.WithAlpha(180), 20f, 6f, 1.5f, true);

        // Items (custom layout: name left, date right)
        float y = _scImportDropdownBounds.Top + 4;
        float leftMargin = _scImportDropdownBounds.Left + 10;
        float rightMargin = _scImportDropdownBounds.Right - 8;
        for (int i = 0; i < _scAvailableProfiles.Count; i++)
        {
            var profile = _scAvailableProfiles[i];
            var itemBounds = new SKRect(_scImportDropdownBounds.Left + 2, y, _scImportDropdownBounds.Right - 2, y + itemHeight);
            bool isHovered = itemBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            if (isHovered)
            {
                _scHoveredImportProfile = i;
                using var hoverBg = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(40));
                canvas.DrawRect(itemBounds, hoverBg);
                using var accentBar = FUIRenderer.CreateFillPaint(FUIColors.Active);
                canvas.DrawRect(new SKRect(itemBounds.Left, itemBounds.Top + 2, itemBounds.Left + 2, itemBounds.Bottom - 2), accentBar);
            }
            var textColor = isHovered ? FUIColors.TextBright : FUIColors.TextPrimary;
            FUIRenderer.DrawText(canvas, profile.DisplayName, new SKPoint(leftMargin, y + itemHeight / 2 + 4), textColor, 13f);
            var dateText = profile.LastModified.ToString("MM/dd HH:mm");
            float dateWidth = FUIRenderer.MeasureText(dateText, 12f);
            FUIRenderer.DrawText(canvas, dateText, new SKPoint(rightMargin - dateWidth, y + itemHeight / 2 + 3), FUIColors.TextDim, 12f);
            y += itemHeight;
        }
    }

    private void DrawSCActionMapFilterDropdown(SKCanvas canvas)
    {
        float itemHeight = 24f;
        // items[0] = "All Categories", items[1..] = action map names
        var items = new List<string> { "All Categories" };
        items.AddRange(_searchFilter.ActionMaps);

        float totalContentHeight = items.Count * itemHeight + 4;
        float maxDropdownHeight = 300f;
        float dropdownHeight = Math.Min(totalContentHeight, maxDropdownHeight);
        bool needsScroll = totalContentHeight > maxDropdownHeight;
        float scrollbarWidth = needsScroll ? 8f : 0f;

        _searchFilter.FilterMaxScroll = Math.Max(0, totalContentHeight - dropdownHeight);
        _searchFilter.FilterScrollOffset = Math.Clamp(_searchFilter.FilterScrollOffset, 0, _searchFilter.FilterMaxScroll);

        _searchFilter.FilterDropdownBounds = new SKRect(
            _searchFilter.FilterBounds.Right - _searchFilter.FilterBounds.Width,
            _searchFilter.FilterBounds.Bottom + 2,
            _searchFilter.FilterBounds.Right,
            _searchFilter.FilterBounds.Bottom + 2 + dropdownHeight);

        // Map hover: _searchFilter.HoveredFilter == -1 means "All Categories" row OR nothing.
        // Disambiguate using mouse position against the first item's Y range.
        float firstItemTop = _searchFilter.FilterDropdownBounds.Top + 2 - _searchFilter.FilterScrollOffset;
        bool allCatHovered = _searchFilter.HoveredFilter == -1
            && _ctx.MousePosition.Y >= firstItemTop
            && _ctx.MousePosition.Y < firstItemTop + itemHeight;
        int hoveredIdx = allCatHovered ? 0
            : _searchFilter.HoveredFilter >= 0 ? _searchFilter.HoveredFilter + 1
            : -1;
        int selectedIdx = string.IsNullOrEmpty(_searchFilter.ActionMapFilter) ? 0
            : _searchFilter.ActionMaps.IndexOf(_searchFilter.ActionMapFilter) + 1;

        FUIWidgets.DrawDropdownPanel(canvas, _searchFilter.FilterDropdownBounds, items,
            selectedIdx, hoveredIdx, itemHeight, _searchFilter.FilterScrollOffset, scrollbarWidth);

        // Scrollbar (drawn on top of the panel)
        if (needsScroll)
        {
            float scrollTrackX = _searchFilter.FilterDropdownBounds.Right - scrollbarWidth - 2;
            float scrollTrackY = _searchFilter.FilterDropdownBounds.Top + 2;
            float scrollTrackHeight = dropdownHeight - 4;

            using var trackPaint = FUIRenderer.CreateFillPaint(FUIColors.Background2.WithAlpha(80));
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(scrollTrackX, scrollTrackY, scrollTrackX + scrollbarWidth, scrollTrackY + scrollTrackHeight), 2f), trackPaint);

            float thumbHeight = Math.Max(20f, scrollTrackHeight * (dropdownHeight / totalContentHeight));
            float thumbY = scrollTrackY + (_searchFilter.FilterScrollOffset / _searchFilter.FilterMaxScroll) * (scrollTrackHeight - thumbHeight);
            using var thumbPaint = FUIRenderer.CreateFillPaint(FUIColors.TextDim.WithAlpha(150));
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(scrollTrackX, thumbY, scrollTrackX + scrollbarWidth, thumbY + thumbHeight), 2f), thumbPaint);
        }
    }

    private void DrawSCDetailRow(SKCanvas canvas, float leftMargin, float rightMargin, ref float y, string label, string value)
    {
        float lineHeight = 18f;
        FUIRenderer.DrawText(canvas, label, new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        FUIRenderer.DrawText(canvas, value, new SKPoint(leftMargin + 120, y), FUIColors.TextDim, 13f);
        y += lineHeight;
    }

    private void DrawStatusBanner(SKCanvas canvas, SKRect bounds)
    {
        if (string.IsNullOrEmpty(_scExportStatus)) return;

        var color = _scStatusKind switch
        {
            SCStatusKind.Success => FUIColors.Success,
            SCStatusKind.Error   => FUIColors.Danger,
            SCStatusKind.Warning => FUIColors.Warning,
            _                    => FUIColors.TextDim,
        };

        using var bgPaint4 = FUIRenderer.CreateFillPaint(color.WithAlpha(25));
        canvas.DrawRoundRect(bounds, 2f, 2f, bgPaint4);

        using var accentPaint = FUIRenderer.CreateFillPaint(color.WithAlpha(180));
        canvas.DrawRect(new SKRect(bounds.Left, bounds.Top, bounds.Left + 3f, bounds.Bottom), accentPaint);

        FUIRenderer.DrawTextCentered(canvas, _scExportStatus, bounds, color, 13f);
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
        using var bgPaint5 = FUIRenderer.CreateFillPaint(FUIColors.Void);
        canvas.DrawRect(bounds, bgPaint5);

        // Inner background
        using var innerBgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background0);
        canvas.DrawRect(bounds.Inset(2, 2), innerBgPaint);

        // L-corner frame (FUI style)
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Active.WithAlpha(180), 20f, 6f, 1.5f, true);

        // Items
        float rowHeight = 24f;
        float y = bounds.Top + 4;
        _profileMgmt.HoveredProfileIndex = -1;
        _profileMgmt.DropdownDeleteProfileName = "";

        // Section 1: Asteriq saved profiles (all of them, including the active one)
        if (_profileMgmt.ExportProfiles.Count > 0)
        {
            FUIRenderer.DrawText(canvas, "SAVED PROFILES", new SKPoint(bounds.Left + 10, y + 9f), FUIColors.TextDim, 12f, true);
            y += 16f;
        }

        for (int i = 0; i < _profileMgmt.ExportProfiles.Count && y + rowHeight <= bounds.Bottom; i++)
        {
            var profile = _profileMgmt.ExportProfiles[i];
            bool isActive  = profile.ProfileName == _scExportProfile.ProfileName;
            var rowBounds  = new SKRect(bounds.Left + 4, y, bounds.Right - 4, y + rowHeight);
            bool isHovered = rowBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

            // Active profile: always show accent bar (even when not hovered)
            if (isActive)
            {
                using var activeAccentPaint = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(180));
                canvas.DrawRect(new SKRect(rowBounds.Left, rowBounds.Top + 2, rowBounds.Left + 2, rowBounds.Bottom - 2), activeAccentPaint);
            }

            // FUI hover style
            if (isHovered)
            {
                _profileMgmt.HoveredProfileIndex = i;
                using var hoverPaint = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(40));
                canvas.DrawRect(rowBounds, hoverPaint);
                if (!isActive)
                {
                    using var accentPaint = FUIRenderer.CreateFillPaint(FUIColors.Active);
                    canvas.DrawRect(new SKRect(rowBounds.Left, rowBounds.Top + 2, rowBounds.Left + 2, rowBounds.Bottom - 2), accentPaint);
                }

                // Delete (×) button — shown on hover for all profiles including active
                _profileMgmt.DropdownDeleteBounds = new SKRect(rowBounds.Right - 22, rowBounds.Top + 4, rowBounds.Right - 4, rowBounds.Bottom - 4);
                _profileMgmt.DropdownDeleteProfileName = profile.ProfileName;
                bool delHovered = _profileMgmt.DropdownDeleteBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
                FUIRenderer.DrawText(canvas, "×", new SKPoint(_profileMgmt.DropdownDeleteBounds.MidX - 3f, _profileMgmt.DropdownDeleteBounds.MidY + 4f),
                    delHovered ? FUIColors.TextBright : FUIColors.TextDim, 14f);
            }

            var textColor = isHovered ? FUIColors.TextBright : (isActive ? FUIColors.Active : FUIColors.TextPrimary);
            float maxTextWidth = rowBounds.Width - (isHovered ? 56f : 40f); // extra room for × on hover
            string displayName = FUIWidgets.TruncateTextToWidth(profile.ProfileName, maxTextWidth, 10f);
            FUIRenderer.DrawText(canvas, displayName, new SKPoint(rowBounds.Left + 10, rowBounds.MidY + 4f), textColor, 13f);

            // Binding count badge
            if (profile.BindingCount > 0)
            {
                string countStr = profile.BindingCount.ToString();
                float badgeX = rowBounds.Right - (isHovered ? 28f : 8f) - FUIRenderer.MeasureText(countStr, 12f);
                FUIRenderer.DrawText(canvas, countStr, new SKPoint(badgeX, rowBounds.MidY + 3f), FUIColors.TextDim, 12f);
            }

            y += rowHeight;
        }

        // Section 2: SC mapping files from mappings folder (if any)
        if (_scAvailableProfiles.Count > 0 && y + rowHeight <= bounds.Bottom)
        {
            // Separator line (FUI style)
            y += 4f;
            float sepY = y;
            using var sepPaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame);
            canvas.DrawLine(bounds.Left + 12, sepY, bounds.Right - 12, sepY, sepPaint);

            // Corner accents on separator
            using var accentLinePaint = FUIRenderer.CreateStrokePaint(FUIColors.Active.WithAlpha(120));
            canvas.DrawLine(bounds.Left + 8, sepY, bounds.Left + 12, sepY, accentLinePaint);
            canvas.DrawLine(bounds.Right - 12, sepY, bounds.Right - 8, sepY, accentLinePaint);

            y += 6f;

            // Section label: make it clear these are SC files to import from, not Asteriq profiles
            FUIRenderer.DrawText(canvas, "IMPORT FROM SC", new SKPoint(bounds.Left + 10, y + 9f), FUIColors.TextDim, 12f, true);
            y += 16f;

            // SC mapping files
            int scFileIndexOffset = _profileMgmt.ExportProfiles.Count + 1000; // Use offset to distinguish from Asteriq profiles
            for (int i = 0; i < _scAvailableProfiles.Count && y + rowHeight <= bounds.Bottom; i++)
            {
                var scFile = _scAvailableProfiles[i];
                var rowBounds = new SKRect(bounds.Left + 4, y, bounds.Right - 4, y + rowHeight);
                bool isHovered = rowBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

                if (isHovered)
                {
                    _profileMgmt.HoveredProfileIndex = scFileIndexOffset + i;
                    using var hoverPaint = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(40));
                    canvas.DrawRect(rowBounds, hoverPaint);
                    using var accentPaint = FUIRenderer.CreateFillPaint(FUIColors.Active);
                    canvas.DrawRect(new SKRect(rowBounds.Left, rowBounds.Top + 2, rowBounds.Left + 2, rowBounds.Bottom - 2), accentPaint);
                }

                var textColor = isHovered ? FUIColors.TextBright : FUIColors.TextPrimary;
                float maxTextWidth = rowBounds.Width - 20f;
                string displayName = scFile.DisplayName;
                displayName = FUIWidgets.TruncateTextToWidth(displayName, maxTextWidth, 10f);
                FUIRenderer.DrawText(canvas, displayName, new SKPoint(rowBounds.Left + 10, rowBounds.MidY + 4f), textColor, 13f);

                y += rowHeight;
            }
        }

        // Section 3: Remote SC control profiles received from TX master
        var remoteProfiles = _ctx.RemoteControlProfiles;
        if (remoteProfiles.Count > 0 && y + rowHeight <= bounds.Bottom)
        {
            // Separator (same style as Section 2)
            y += 4f;
            float sepY2 = y;
            using var sepPaint2 = FUIRenderer.CreateStrokePaint(FUIColors.Frame);
            canvas.DrawLine(bounds.Left + 12, sepY2, bounds.Right - 12, sepY2, sepPaint2);
            using var accentLinePaint2 = FUIRenderer.CreateStrokePaint(FUIColors.Active.WithAlpha(120));
            canvas.DrawLine(bounds.Left + 8, sepY2, bounds.Left + 12, sepY2, accentLinePaint2);
            canvas.DrawLine(bounds.Right - 12, sepY2, bounds.Right - 8, sepY2, accentLinePaint2);
            y += 6f;

            string masterLabel = string.IsNullOrEmpty(_ctx.RemoteControlProfilesMasterName)
                ? "FROM MASTER"
                : $"FROM {_ctx.RemoteControlProfilesMasterName.ToUpperInvariant()}";
            FUIRenderer.DrawText(canvas, masterLabel, new SKPoint(bounds.Left + 10, y + 9f), FUIColors.TextDim, 12f, true);
            y += 16f;

            int remoteIndexOffset = _profileMgmt.ExportProfiles.Count + 2000;
            for (int i = 0; i < remoteProfiles.Count && y + rowHeight <= bounds.Bottom; i++)
            {
                var (remoteName, _) = remoteProfiles[i];
                var rowBounds = new SKRect(bounds.Left + 4, y, bounds.Right - 4, y + rowHeight);
                bool isHovered = rowBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

                if (isHovered)
                {
                    _profileMgmt.HoveredProfileIndex = remoteIndexOffset + i;
                    using var hoverPaint = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(40));
                    canvas.DrawRect(rowBounds, hoverPaint);
                    using var accentPaint = FUIRenderer.CreateFillPaint(FUIColors.Active);
                    canvas.DrawRect(new SKRect(rowBounds.Left, rowBounds.Top + 2, rowBounds.Left + 2, rowBounds.Bottom - 2), accentPaint);
                }

                var textColor = isHovered ? FUIColors.TextBright : FUIColors.TextPrimary;
                string displayName = FUIWidgets.TruncateTextToWidth(remoteName, rowBounds.Width - 20f, 10f);
                FUIRenderer.DrawText(canvas, displayName, new SKPoint(rowBounds.Left + 10, rowBounds.MidY + 4f), textColor, 13f);

                y += rowHeight;
            }
        }
    }

    /// <summary>
    /// Returns the physical device name for a vJoy column, or null if not mapped.
    /// </summary>
    private string? GetPhysicalDeviceNameForVJoyColumn(SCGridColumn col)
    {
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return null;
        if (!profile.VJoyPrimaryDevices.TryGetValue(col.VJoyDeviceId, out var guid)) return null;

        var device = _ctx.Devices.Concat(_ctx.DisconnectedDevices)
            .FirstOrDefault(d => d.InstanceGuid.ToString().Equals(guid, StringComparison.OrdinalIgnoreCase));
        return device?.Name;
    }

    private void DrawConflictLinksPanel(SKCanvas canvas, float left, float right, float top, float maxBottom)
    {
        // Available height — cap at 4 rows so we don't blow the layout on large conflict lists
        const float rowH = 22f;
        const float padV = 8f;
        const float padH = 10f;
        const float headerH = 20f;

        int maxVisible = (int)Math.Max(1, Math.Min(_conflicts.ConflictLinks.Count, (maxBottom - top - headerH - padV * 2) / rowH));
        float boxH = padV + headerH + maxVisible * rowH + padV;

        if (top + boxH > maxBottom)
            return; // not enough room — skip

        var boxBounds = new SKRect(left, top, right, top + boxH);

        // Amber glow background + border
        using var glowFilter = SKImageFilter.CreateBlur(6f, 6f);
        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Warning.WithAlpha(60),
            StrokeWidth = 3f,
            IsAntialias = true,
            ImageFilter = glowFilter
        };
        canvas.DrawRoundRect(boxBounds, 4, 4, glowPaint);

        FUIRenderer.DrawRoundedPanel(canvas, boxBounds, FUIColors.Warning.WithAlpha(20), FUIColors.Warning.WithAlpha(160), 4f);

        // Header
        float y = top + padV;
        FUIRenderer.DrawText(canvas, "ALSO BOUND TO THIS INPUT:", new SKPoint(left + padH, y + 13f), FUIColors.Warning, 10f, true);
        y += headerH;

        // Resize bounds list for click detection
        while (_conflicts.ConflictLinkBounds.Count < _conflicts.ConflictLinks.Count)
            _conflicts.ConflictLinkBounds.Add(SKRect.Empty);
        for (int i = 0; i < _conflicts.ConflictLinkBounds.Count; i++)
            _conflicts.ConflictLinkBounds[i] = SKRect.Empty;

        // Link rows
        for (int i = 0; i < maxVisible; i++)
        {
            var (map, name) = _conflicts.ConflictLinks[i];
            string category = SCCategoryMapper.GetCategoryNameForAction(map, name);
            string formatted = SCCategoryMapper.FormatActionName(name);
            string label = $"{category} > {formatted}";

            var rowBounds = new SKRect(left + padH / 2, y, right - padH / 2, y + rowH);
            _conflicts.ConflictLinkBounds[i] = rowBounds;

            bool hovered = i == _conflicts.ConflictLinkHovered;
            if (hovered)
            {
                using var hoverPaint = FUIRenderer.CreateFillPaint(FUIColors.Warning.WithAlpha(40));
                canvas.DrawRoundRect(rowBounds, 2, 2, hoverPaint);
            }

            string truncated = FUIWidgets.TruncateTextToWidth(label, right - left - padH * 2, 10f);
            byte linkAlpha = hovered ? (byte)255 : (byte)180;
            FUIRenderer.DrawText(canvas, truncated, new SKPoint(left + padH, y + rowH / 2 + 4f), FUIColors.Warning.WithAlpha(linkAlpha), 10f);

            y += rowH;
        }

        // "and N more" if truncated
        if (_conflicts.ConflictLinks.Count > maxVisible)
        {
            int remaining = _conflicts.ConflictLinks.Count - maxVisible;
            FUIRenderer.DrawText(canvas, $"and {remaining} more…", new SKPoint(left + padH, y + 12f),
                FUIColors.TextDim.WithAlpha(120), 10f);
        }
    }

    private void DrawColumnActionsPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        if (_grid.Columns is null || _colImport.HighlightedColumn < 0 || _colImport.HighlightedColumn >= _grid.Columns.Count)
            return;

        var col = _grid.Columns[_colImport.HighlightedColumn];

        var m = FUIRenderer.DrawPanelChrome(canvas, bounds);
        float y = m.Y;
        float leftMargin = m.LeftMargin;
        float rightMargin = m.RightMargin;
        FUIWidgets.DrawPanelTitle(canvas, leftMargin, rightMargin, ref y, "COLUMN ACTIONS", withDivider: true);

        // Column label + device name
        string colLabel = $"JS{col.SCInstance}";
        FUIRenderer.DrawText(canvas, colLabel, new SKPoint(leftMargin, y), FUIColors.Active, 13f, true);
        string? deviceName = GetPhysicalDeviceNameForVJoyColumn(col);
        if (deviceName is not null)
        {
            FUIRenderer.DrawText(canvas, " — ", new SKPoint(leftMargin + 26f, y), FUIColors.TextDim, 13f);
            string shortName = FUIWidgets.TruncateTextToWidth(deviceName, rightMargin - leftMargin - 48f, 10f);
            FUIRenderer.DrawText(canvas, shortName, new SKPoint(leftMargin + 44f, y), FUIColors.TextPrimary, 12f);
        }
        y += 17f;

        // Binding count for this column
        int bindingCount = _scExportProfile.Bindings.Count(b =>
            b.DeviceType == SCDeviceType.Joystick &&
            b.PhysicalDeviceId is null &&
            _scExportProfile.GetSCInstance(b.VJoyDevice) == col.SCInstance);
        string countStr = bindingCount == 0 ? "No bindings" : $"{bindingCount} binding{(bindingCount == 1 ? "" : "s")}";
        FUIRenderer.DrawText(canvas, countStr, new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
        y += 20f;

        // IMPORT FROM section
        FUIRenderer.DrawText(canvas, "IMPORT FROM", new SKPoint(leftMargin, y), FUIColors.TextDim, 11f, true);
        y += 14f;

        // Source profile selector — shows saved Asteriq profiles + SC XML files
        float selectorH = 28f;
        var (savedProfiles, xmlFiles) = GetColImportSources();
        int totalSources = savedProfiles.Count + xmlFiles.Count;
        bool hasProfiles = totalSources > 0;
        string profileSelectorLabel = _colImport.ProfileIndex >= 0 && _colImport.ProfileIndex < totalSources
            ? GetColImportSourceLabel(_colImport.ProfileIndex, savedProfiles, xmlFiles)
            : (hasProfiles ? "Select profile…" : "No other profiles");
        _colImport.ProfileSelectorBounds = new SKRect(leftMargin, y, rightMargin, y + selectorH);
        bool profileSelectorHovered = _colImport.ProfileSelectorBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawSelector(canvas, _colImport.ProfileSelectorBounds, profileSelectorLabel,
            profileSelectorHovered || _colImport.ProfileDropdownOpen, hasProfiles);
        y += selectorH + 4f;

        // Source column selector
        bool hasSourceColumns = _colImport.SourceColumns.Count > 0;
        string columnSelectorLabel = _colImport.ColumnIndex >= 0 && _colImport.ColumnIndex < _colImport.SourceColumns.Count
            ? _colImport.SourceColumns[_colImport.ColumnIndex].Label
            : (_colImport.ProfileIndex >= 0 && !hasSourceColumns ? "No columns found" : "Select column…");
        _colImport.ColumnSelectorBounds = new SKRect(leftMargin, y, rightMargin, y + selectorH);
        bool columnSelectorHovered = _colImport.ColumnSelectorBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawSelector(canvas, _colImport.ColumnSelectorBounds, columnSelectorLabel,
            columnSelectorHovered || _colImport.ColumnDropdownOpen, hasSourceColumns);

        // Action buttons anchored to panel bottom
        float btnH = 28f;
        float btnW = (rightMargin - leftMargin - 8f) / 2f;
        float btnY = bounds.Bottom - frameInset - FUIRenderer.SpaceLG - btnH;

        bool canImport = _colImport.ProfileIndex >= 0 && _colImport.ColumnIndex >= 0;
        _colImport.ImportButtonBounds = new SKRect(leftMargin, btnY, leftMargin + btnW, btnY + btnH);
        _colImport.ImportButtonHovered = _colImport.ImportButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        if (canImport)
        {
            FUIRenderer.DrawButton(canvas, _colImport.ImportButtonBounds, "IMPORT",
                _colImport.ImportButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
        }
        else
        {
            using var disabledPaint = FUIRenderer.CreateFillPaint(FUIColors.Background2.WithAlpha(60));
            canvas.DrawRect(_colImport.ImportButtonBounds, disabledPaint);
            FUIRenderer.DrawTextCentered(canvas, "IMPORT", _colImport.ImportButtonBounds, FUIColors.TextDim.WithAlpha(100), 12f);
        }

        _colImport.ClearColumnBounds = new SKRect(leftMargin + btnW + 8f, btnY, rightMargin, btnY + btnH);
        _colImport.ClearColumnHovered = _colImport.ClearColumnBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        if (bindingCount > 0)
        {
            FUIRenderer.DrawButton(canvas, _colImport.ClearColumnBounds, "CLEAR COL",
                _colImport.ClearColumnHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal,
                isDanger: true);
        }
        else
        {
            using var disabledPaint = FUIRenderer.CreateFillPaint(FUIColors.Background2.WithAlpha(60));
            canvas.DrawRect(_colImport.ClearColumnBounds, disabledPaint);
            FUIRenderer.DrawTextCentered(canvas, "CLEAR COL", _colImport.ClearColumnBounds, FUIColors.TextDim.WithAlpha(100), 12f);
        }
    }

    private void DrawColImportProfileDropdown(SKCanvas canvas)
    {
        var (savedProfiles, xmlFiles) = GetColImportSources();
        int totalSources = savedProfiles.Count + xmlFiles.Count;
        if (totalSources == 0) return;

        float itemH = 28f;
        float dropdownH = Math.Min(totalSources * itemH + 8f, 200f);
        _colImport.ProfileDropdownBounds = new SKRect(
            _colImport.ProfileSelectorBounds.Left,
            _colImport.ProfileSelectorBounds.Top - 2 - dropdownH,
            _colImport.ProfileSelectorBounds.Right,
            _colImport.ProfileSelectorBounds.Top - 2);

        var items = savedProfiles.Select(p => p.ProfileName)
            .Concat(xmlFiles.Select(f => $"[SC] {f.DisplayName}"))
            .ToList();
        FUIWidgets.DrawDropdownPanel(canvas, _colImport.ProfileDropdownBounds, items,
            _colImport.ProfileIndex, _colImport.ProfileHoveredIndex, itemH);
    }

    /// <summary>
    /// Returns the two separate source collections for the Import From Profile picker.
    /// Asteriq saved profiles first (excluding the active one), then SC XML mapping files.
    /// </summary>
    private (List<SCExportProfileInfo> Saved, List<SCMappingFile> Xml) GetColImportSources()
    {
        var saved = _profileMgmt.ExportProfiles.Where(p => p.ProfileName != _scExportProfile.ProfileName).ToList();
        return (saved, _scAvailableProfiles);
    }

    private string GetColImportSourceLabel(int index, List<SCExportProfileInfo> saved, List<SCMappingFile> xml)
    {
        if (index < saved.Count)
            return saved[index].ProfileName;
        int xmlIdx = index - saved.Count;
        return xmlIdx < xml.Count ? $"[SC] {xml[xmlIdx].DisplayName}" : "?";
    }

    private void DrawColImportColumnDropdown(SKCanvas canvas)
    {
        if (_colImport.SourceColumns.Count == 0) return;

        float itemH = 28f;
        float dropdownH = Math.Min(_colImport.SourceColumns.Count * itemH + 8f, 200f);
        _colImport.ColumnDropdownBounds = new SKRect(
            _colImport.ColumnSelectorBounds.Left,
            _colImport.ColumnSelectorBounds.Top - 2 - dropdownH,
            _colImport.ColumnSelectorBounds.Right,
            _colImport.ColumnSelectorBounds.Top - 2);

        var items = _colImport.SourceColumns.Select(c => c.Label).ToList();
        FUIWidgets.DrawDropdownPanel(canvas, _colImport.ColumnDropdownBounds, items,
            _colImport.ColumnIndex, _colImport.ColumnHoveredIndex, itemH);
    }

    private void DrawDeviceOrderPanel(SKCanvas canvas, SKRect bounds, float frameInset, bool isExpanded)
    {
        var existingSlots = _ctx.VJoyDevices.Where(v => v.Exists).OrderBy(v => v.Id).ToList();
        if (existingSlots.Count == 0) return;

        float cornerLen = isExpanded ? 30f : Math.Min(16f, bounds.Height * 0.35f);
        var m = FUIRenderer.DrawPanelChrome(canvas, bounds, cornerLength: cornerLen);
        float y = m.Y;
        float leftMargin = m.LeftMargin;
        float rightMargin = m.RightMargin;

        // Header bounds for click-to-expand handling
        _deviceOrder.HeaderBounds = new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Top + 52f);
        bool headerHovered = !isExpanded && _deviceOrder.HeaderBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

        FUIWidgets.DrawPanelTitle(canvas, leftMargin, rightMargin, ref y, "DEVICE ORDER");

        // Expand/collapse indicator
        string indicator = isExpanded ? "-" : "+";
        float indW = FUIRenderer.MeasureText(indicator, 13f);
        FUIRenderer.DrawText(canvas, indicator, new SKPoint(rightMargin - indW, y - 18f),
            headerHovered ? FUIColors.TextBright : FUIColors.Active.WithAlpha(isExpanded ? (byte)100 : (byte)180),
            13f, true);

        if (!isExpanded) return;

        y += 4f;

        // Resize selector bounds array if needed
        if (_deviceOrder.SelectorBounds.Length != existingSlots.Count)
            _deviceOrder.SelectorBounds = new SKRect[existingSlots.Count];

        // Build reverse mapping: SC instance → vJoy slot ID
        var vjoyForSCInst = new Dictionary<int, uint>();
        foreach (var slot in existingSlots)
        {
            int inst = _scExportProfile.GetSCInstance(slot.Id);
            if (!vjoyForSCInst.ContainsKey(inst))
                vjoyForSCInst[inst] = slot.Id;
        }

        float selectorH = 24f;
        float labelW = 32f;   // "JS1 " label
        float selectorW = rightMargin - leftMargin - labelW - 6f;

        for (int row = 0; row < existingSlots.Count; row++)
        {
            int scInst = row + 1;
            vjoyForSCInst.TryGetValue(scInst, out uint vjoySlotId);

            // JS label
            FUIRenderer.DrawText(canvas, $"JS{scInst}", new SKPoint(leftMargin, y + selectorH / 2f + 4f),
                FUIColors.Active, 12f, true);

            // Selector
            var selectorBounds = new SKRect(leftMargin + labelW, y, leftMargin + labelW + selectorW, y + selectorH);
            _deviceOrder.SelectorBounds[row] = selectorBounds;

            bool isOpen = _deviceOrder.OpenRow == row;
            bool isHovered = selectorBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y) || isOpen;
            bool deviceMode = _ctx.AppSettings.SCBindingsShowPhysicalHeaders;
            string? assignedDevName = vjoySlotId > 0 ? GetPhysicalDeviceNameForVJoyId(vjoySlotId) : null;
            string selectorLabel = vjoySlotId > 0
                ? (deviceMode && assignedDevName is not null
                    ? FUIWidgets.TruncateTextToWidth(assignedDevName, selectorW - 20f, 11f)
                    : $"vJoy {vjoySlotId}")
                : "—";
            FUIWidgets.DrawSelector(canvas, selectorBounds, selectorLabel, isHovered, existingSlots.Count > 1);

            // In JS-ref mode, show the device name as a dim hint after the selector
            if (!deviceMode && assignedDevName is not null)
            {
                float nameX = selectorBounds.Right + 6f;
                float maxNameW = rightMargin - nameX;
                if (maxNameW > 10f)
                {
                    string truncated = FUIWidgets.TruncateTextToWidth(assignedDevName, maxNameW, 10f);
                    FUIRenderer.DrawText(canvas, truncated,
                        new SKPoint(nameX, y + selectorH / 2f + 4f),
                        FUIColors.TextDim, 10f);
                }
            }

            y += selectorH + 4f;
        }

        // Auto-detect button anchored near the bottom
        float btnH = 26f;
        float btnY = bounds.Bottom - frameInset - FUIRenderer.SpaceLG - btnH;
        _deviceOrder.AutoDetectBounds = new SKRect(leftMargin, btnY, rightMargin, btnY + btnH);
        _deviceOrder.AutoDetectHovered = _deviceOrder.AutoDetectBounds.Contains(
            _ctx.MousePosition.X, _ctx.MousePosition.Y);

        bool canAutoDetect = _directInputService is not null;
        if (canAutoDetect)
        {
            FUIRenderer.DrawButton(canvas, _deviceOrder.AutoDetectBounds, "AUTO-DETECT",
                _deviceOrder.AutoDetectHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
        }
        else
        {
            using var disabledPaint = FUIRenderer.CreateFillPaint(FUIColors.Background2.WithAlpha(60));
            canvas.DrawRect(_deviceOrder.AutoDetectBounds, disabledPaint);
            FUIRenderer.DrawTextCentered(canvas, "AUTO-DETECT", _deviceOrder.AutoDetectBounds,
                FUIColors.TextDim.WithAlpha(100), 12f);
        }
    }

    private void DrawDeviceOrderDropdown(SKCanvas canvas)
    {
        if (_deviceOrder.OpenRow < 0) return;

        var existingSlots = _ctx.VJoyDevices.Where(v => v.Exists).OrderBy(v => v.Id).ToList();
        if (existingSlots.Count == 0 || _deviceOrder.OpenRow >= _deviceOrder.SelectorBounds.Length) return;

        // Determine currently selected vJoy slot for this row's SC instance
        int scInst = _deviceOrder.OpenRow + 1;
        var vjoyForSCInst = new Dictionary<int, uint>();
        foreach (var slot in existingSlots)
        {
            int inst = _scExportProfile.GetSCInstance(slot.Id);
            if (!vjoyForSCInst.ContainsKey(inst))
                vjoyForSCInst[inst] = slot.Id;
        }
        vjoyForSCInst.TryGetValue(scInst, out uint selectedVJoyId);
        int selectedIdx = existingSlots.FindIndex(s => s.Id == selectedVJoyId);

        float itemH = 28f;
        var selectorBounds = _deviceOrder.SelectorBounds[_deviceOrder.OpenRow];
        _deviceOrder.DropdownBounds = new SKRect(
            selectorBounds.Left,
            selectorBounds.Bottom + 2,
            selectorBounds.Right,
            selectorBounds.Bottom + 2 + existingSlots.Count * itemH + 8f);

        bool deviceMode = _ctx.AppSettings.SCBindingsShowPhysicalHeaders;
        var items = existingSlots.Select(s =>
        {
            if (deviceMode)
            {
                string? name = GetPhysicalDeviceNameForVJoyId(s.Id);
                return name ?? $"vJoy {s.Id}";
            }
            return $"vJoy {s.Id}";
        }).ToList();
        FUIWidgets.DrawDropdownPanel(canvas, _deviceOrder.DropdownBounds, items,
            selectedIdx, _deviceOrder.HoveredIndex, itemH);
    }

    private string? GetPhysicalDeviceNameForVJoyId(uint vjoySlotId)
    {
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return null;
        if (!profile.VJoyPrimaryDevices.TryGetValue(vjoySlotId, out var guid)) return null;

        var device = _ctx.Devices.Concat(_ctx.DisconnectedDevices)
            .FirstOrDefault(d => d.InstanceGuid.ToString().Equals(guid, StringComparison.OrdinalIgnoreCase));
        return device?.Name;
    }
}
