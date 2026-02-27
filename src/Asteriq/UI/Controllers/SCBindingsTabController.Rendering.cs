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

        // Two-panel layout: Left (bindings table) | Right (Installation + Export stacked)
        // Table on left for more space, controls on right
        float rightPanelWidth = Math.Min(500f, Math.Max(280f, contentBounds.Width * 0.24f));
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

        // RIGHT BOTTOM - Export panel always visible; Column Actions panel stacked below when a vJoy column is selected
        bool showColumnActions = _scHighlightedColumn >= 0
            && _scGridColumns is not null
            && _scHighlightedColumn < _scGridColumns.Count
            && _scGridColumns[_scHighlightedColumn].IsJoystick
            && !_scGridColumns[_scHighlightedColumn].IsPhysical
            && !_scGridColumns[_scHighlightedColumn].IsReadOnly;

        float columnActionsHeight = 235f;
        float verticalGap2 = 8f;

        SKRect controlProfilesBounds;
        SKRect columnActionsBounds = SKRect.Empty;
        if (showColumnActions)
        {
            controlProfilesBounds = new SKRect(exportBounds.Left, exportBounds.Top,
                exportBounds.Right, exportBounds.Bottom - columnActionsHeight - verticalGap2);
            columnActionsBounds = new SKRect(exportBounds.Left, controlProfilesBounds.Bottom + verticalGap2,
                exportBounds.Right, exportBounds.Bottom);
        }
        else
        {
            controlProfilesBounds = exportBounds;
        }

        DrawSCExportPanelCompact(canvas, controlProfilesBounds, frameInset, suppressActionInfo: showColumnActions);
        if (showColumnActions)
            DrawColumnActionsPanel(canvas, columnActionsBounds, frameInset);

        // Draw dropdowns last (on top) so they render over all panels
        if (_scProfileDropdownOpen && !_scProfileDropdownListBounds.IsEmpty)
            DrawSCProfileDropdownList(canvas, _scProfileDropdownListBounds);
        if (_scInstallationDropdownOpen && _scInstallations.Count > 0)
            DrawSCInstallationDropdown(canvas);
        if (_scActionMapFilterDropdownOpen && _scActionMaps.Count > 0)
            DrawSCActionMapFilterDropdown(canvas);
        if (showColumnActions && _scColImportProfileDropdownOpen)
            DrawColImportProfileDropdown(canvas);
        if (showColumnActions && _scColImportColumnDropdownOpen && _scColImportSourceColumns.Count > 0)
            DrawColImportColumnDropdown(canvas);
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
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame, 30f, 8f);

        float cornerPadding = 15f;
        float y = bounds.Top + frameInset + cornerPadding;
        float leftMargin = bounds.Left + frameInset + cornerPadding;
        float rightMargin = bounds.Right - frameInset - 10;

        FUIWidgets.DrawPanelTitle(canvas, leftMargin, rightMargin, ref y, "SC INSTALLATION");

        // Installation selector
        float selectorHeight = 32f;
        _scInstallationSelectorBounds = new SKRect(leftMargin, y, rightMargin, y + selectorHeight);

        string installationText = _scInstallations.Count > 0 && _selectedSCInstallation < _scInstallations.Count
            ? _scInstallations[_selectedSCInstallation].DisplayName
            : "No SC found";

        bool selectorHovered = _scInstallationSelectorBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawSelector(canvas, _scInstallationSelectorBounds, installationText, selectorHovered || _scInstallationDropdownOpen, _scInstallations.Count > 0);
    }

    private void DrawSCInstallationDropdown(SKCanvas canvas)
    {
        float itemH = 28f;
        _scInstallationDropdownBounds = new SKRect(
            _scInstallationSelectorBounds.Left,
            _scInstallationSelectorBounds.Bottom + 2,
            _scInstallationSelectorBounds.Right,
            _scInstallationSelectorBounds.Bottom + 2 + Math.Min(_scInstallations.Count * itemH + 8f, 200f));

        var items = _scInstallations.Select(s => s.DisplayName).ToList();
        FUIWidgets.DrawDropdownPanel(canvas, _scInstallationDropdownBounds, items,
            _selectedSCInstallation, _hoveredSCInstallation, itemH);
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
        // Title row with action count
        FUIRenderer.DrawText(canvas, "SC ACTIONS", new SKPoint(leftMargin, y), FUIColors.TextBright, 15f, true);

        // Action count on right of title
        int actionCount = _scFilteredActions?.Count ?? 0;
        int totalCount = _scSchemaService is not null && _scActions is not null
            ? _scSchemaService.FilterJoystickActions(_scActions).Count
            : actionCount;
        // Total bound is always against the full unfiltered list so it reflects the whole profile
        int totalBound = _scActions?.Count(a => _scExportProfile.GetBinding(a.ActionMap, a.ActionName) is not null) ?? 0;
        int boundCount = _scFilteredActions?.Count(a => _scExportProfile.GetBinding(a.ActionMap, a.ActionName) is not null) ?? 0;
        bool otherFilters = !string.IsNullOrEmpty(_scActionMapFilter) || !string.IsNullOrEmpty(_scSearchText);
        bool isFiltered = otherFilters || _scShowBoundOnly;

        string countText;
        if (!isFiltered)
            countText = $"{totalCount} actions, {totalBound} bound";
        else if (_scShowBoundOnly && !otherFilters)
            countText = $"{totalBound} of {totalCount} bound";       // "239 of 1113 bound"
        else if (_scShowBoundOnly)
            countText = $"{actionCount} of {totalBound} bound";       // "26 of 239 bound" (within current filter)
        else
            countText = $"{actionCount} of {totalCount}, {boundCount} bound"; // "55 of 1113, 26 bound"
        float countTextWidth = FUIRenderer.MeasureText(countText, 12f);
        FUIRenderer.DrawText(canvas, countText, new SKPoint(rightMargin - countTextWidth, y), FUIColors.TextDim, 12f);

        // Header toggle button (JS REF / DEVICE) — placed just left of count text
        float toggleBtnW = 58f;
        float toggleBtnH = 18f;
        float toggleBtnX = rightMargin - countTextWidth - 10f - toggleBtnW;
        float toggleBtnY = y - 9f;
        _scHeaderToggleButtonBounds = new SKRect(toggleBtnX, toggleBtnY, toggleBtnX + toggleBtnW, toggleBtnY + toggleBtnH);
        _scHeaderToggleButtonHovered = _scHeaderToggleButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        string toggleLabel = _ctx.AppSettings.SCBindingsShowPhysicalHeaders ? "DEVICE" : "JS REF";
        DrawTextButton(canvas, _scHeaderToggleButtonBounds, toggleLabel, _scHeaderToggleButtonHovered);

        y += 28f;

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
            _scShowBoundOnly ? FUIColors.Active : FUIColors.TextDim, 13f);

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
        FUIRenderer.DrawText(canvas, "ACTION", new SKPoint(leftMargin + 18f, headerTextY), FUIColors.TextDim, 12f, true);

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

                // Highlight background if this column is selected (read-only columns cannot be highlighted)
                if (c == _scHighlightedColumn && !col.IsReadOnly)
                {
                    using var highlightPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(40), IsAntialias = true };
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
                    var headerColor = c == _scHighlightedColumn ? FUIColors.Active : FUIColors.TextPrimary;
                    float headerTextWidth = FUIRenderer.MeasureText(col.Header, 10f);
                    float centeredX = colX + (colW - headerTextWidth) / 2;
                    FUIRenderer.DrawText(canvas, col.Header, new SKPoint(centeredX, headerTextY - 5f), headerColor, 10f, true);
                    string jsLabel = $"JS{col.SCInstance}";
                    float subLabelWidth = FUIRenderer.MeasureText(jsLabel, 10f);
                    FUIRenderer.DrawText(canvas, jsLabel, new SKPoint(colX + (colW - subLabelWidth) / 2, headerTextY + 5f), FUIColors.Active.WithAlpha(180), 10f);
                }
                else if (col.IsJoystick && _ctx.AppSettings.SCBindingsShowPhysicalHeaders)
                {
                    // Physical header mode: device name on top + JS{N} sub-label
                    var headerColor = c == _scHighlightedColumn ? FUIColors.Active : FUIColors.TextPrimary;
                    string? deviceName = GetPhysicalDeviceNameForVJoyColumn(col);
                    if (deviceName is not null)
                    {
                        string shortName = TruncateTextToWidth(deviceName, colW - 4f, 10f);
                        float nameTextWidth = FUIRenderer.MeasureText(shortName, 10f);
                        FUIRenderer.DrawText(canvas, shortName, new SKPoint(colX + (colW - nameTextWidth) / 2, headerTextY - 5f), headerColor, 10f, true);
                        string jsLabel = $"JS{col.SCInstance}";
                        float subLabelWidth = FUIRenderer.MeasureText(jsLabel, 10f);
                        FUIRenderer.DrawText(canvas, jsLabel, new SKPoint(colX + (colW - subLabelWidth) / 2, headerTextY + 5f), FUIColors.Active.WithAlpha(180), 10f);
                    }
                    else
                    {
                        // No physical device mapped — fall back to JS{N} single-label
                        float headerTextWidth = FUIRenderer.MeasureText(col.Header, 12f);
                        float centeredX = colX + (colW - headerTextWidth) / 2;
                        FUIRenderer.DrawText(canvas, col.Header, new SKPoint(centeredX, headerTextY), FUIColors.Active, 12f, true);
                    }
                }
                else
                {
                    // Use consistent theme colors for all column headers
                    var headerColor = c == _scHighlightedColumn ? FUIColors.Active :
                                      col.IsJoystick ? FUIColors.Active : FUIColors.TextPrimary;

                    // Center the header text in the column
                    float headerTextWidth = FUIRenderer.MeasureText(col.Header, 12f);
                    float centeredX = colX + (colW - headerTextWidth) / 2;
                    FUIRenderer.DrawText(canvas, col.Header, new SKPoint(centeredX, headerTextY), headerColor, 12f, true);
                }

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
                : _scActions is null && !string.IsNullOrEmpty(_scLoadingMessage) ? _scLoadingMessage
                : _scActions is null ? "No SC installation found"
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
                    FUIRenderer.DrawText(canvas, displayName, new SKPoint(leftMargin + actionIndent, textY), nameColor, 13f);

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
                                FUIRenderer.DrawText(canvas, "—", new SKPoint(colX + colW / 2 - 4, textY), FUIColors.TextDim.WithAlpha(100), 14f);
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

    private string FormatBindingForCell(string input, List<string>? modifiers)
    {
        // For single string display (tooltips, width calculation, etc.)
        var components = GetBindingComponents(input, modifiers);
        return string.Join(" + ", components);
    }

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

    private void DrawSCExportPanelCompact(SKCanvas canvas, SKRect bounds, float frameInset, bool suppressActionInfo = false)
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
        float buttonGap = 6f;

        FUIWidgets.DrawPanelTitle(canvas, leftMargin, rightMargin, ref y, "CONTROL PROFILES");

        // Control Profile dropdown (full width)
        float dropdownHeight = 32f;
        _scProfileDropdownBounds = new SKRect(leftMargin, y, rightMargin, y + dropdownHeight);
        bool dropdownHovered = _scProfileDropdownBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        string dropdownLabel = string.IsNullOrEmpty(_scExportProfile.ProfileName)
            ? "— No Profile Selected —"
            : _scProfileDirty ? $"{_scExportProfile.ProfileName}*" : _scExportProfile.ProfileName;
        DrawSCProfileDropdownWide(canvas, _scProfileDropdownBounds, dropdownLabel, dropdownHovered, _scProfileDropdownOpen);

        // Pencil edit icon inside dropdown box (left of arrow), visible on hover when a profile is loaded
        bool hasProfile = !string.IsNullOrEmpty(_scExportProfile.ProfileName);
        if (hasProfile && dropdownHovered && !_scProfileDropdownOpen)
        {
            float editSize = 20f;
            float editX = _scProfileDropdownBounds.Right - 28f - editSize;
            float editY = _scProfileDropdownBounds.MidY - editSize / 2f;
            _scProfileEditBounds = new SKRect(editX, editY, editX + editSize, editY + editSize);
            _scProfileEditHovered = _scProfileEditBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

            // Draw pencil icon
            var iconColor = _scProfileEditHovered ? FUIColors.Active : FUIColors.TextDim;
            float cx = _scProfileEditBounds.MidX;
            float cy = _scProfileEditBounds.MidY;
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
            _scProfileEditBounds = SKRect.Empty;
            _scProfileEditHovered = false;
        }

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

        // Compute profile dropdown list bounds so the draw-last pass can render it on top of all panels
        if (_scProfileDropdownOpen)
        {
            int asteriqCount = _scExportProfiles.Count(p => p.ProfileName != _scExportProfile.ProfileName);
            int scFileCount = _scAvailableProfiles.Count;
            int totalItems = asteriqCount + (scFileCount > 0 ? scFileCount + 1 : 0); // +1 for separator
            float listHeight = Math.Min(totalItems * 24f + (scFileCount > 0 ? 16f : 0f) + 8f, 240f);
            _scProfileDropdownListBounds = new SKRect(leftMargin, _scProfileDropdownBounds.Bottom + 2, rightMargin, _scProfileDropdownBounds.Bottom + 2 + listHeight);
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

            string actionDisplay = TruncateTextToWidth(selectedAction.ActionName, rightMargin - leftMargin - 10, 10f);
            FUIRenderer.DrawText(canvas, actionDisplay, new SKPoint(leftMargin, y), FUIColors.TextPrimary, 13f);
            y += lineHeight;

            FUIRenderer.DrawText(canvas, $"Type: {selectedAction.InputType}", new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
            y += lineHeight + 6f;

            // Assign/Clear buttons
            float btnWidth = (rightMargin - leftMargin - 8) / 2;
            float btnHeight = 24f;

            _scAssignInputButtonBounds = new SKRect(leftMargin, y, leftMargin + btnWidth, y + btnHeight);
            _scAssignInputButtonHovered = _scAssignInputButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

            if (_scIsListeningForInput)
            {
                // Show "listening" state when cell listener is active
                using var waitBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(80), IsAntialias = true };
                canvas.DrawRect(_scAssignInputButtonBounds, waitBgPaint);
                FUIRenderer.DrawTextCentered(canvas, "LISTENING...", _scAssignInputButtonBounds, FUIColors.Active, 12f);
            }
            else
            {
                FUIRenderer.DrawButton(canvas, _scAssignInputButtonBounds, "ASSIGN",
                    _scAssignInputButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
            }

            _scClearBindingButtonBounds = new SKRect(leftMargin + btnWidth + 8, y, rightMargin, y + btnHeight);
            _scClearBindingButtonHovered = _scClearBindingButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

            // Check for existing binding on currently selected cell's column
            SCActionBinding? existingBinding = null;
            if (_scSelectedCell.colIndex >= 0 && _scGridColumns is not null && _scSelectedCell.colIndex < _scGridColumns.Count)
            {
                var selCol = _scGridColumns[_scSelectedCell.colIndex];
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
                using var disabledPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2.WithAlpha(60), IsAntialias = true };
                canvas.DrawRect(_scClearBindingButtonBounds, disabledPaint);
                FUIRenderer.DrawTextCentered(canvas, "CLEAR", _scClearBindingButtonBounds, FUIColors.TextDim.WithAlpha(100), 13f);
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
            FUIRenderer.DrawTextCentered(canvas, "CLEAR ALL", _scClearAllButtonBounds, FUIColors.TextDim.WithAlpha(100), 12f);
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
                DrawStatusBanner(canvas, new SKRect(leftMargin, y, rightMargin, y + 24f));
            else
                _scExportStatus = null;
        }
    }

    private void DrawVJoyMappingRow(SKCanvas canvas, SKRect bounds, uint vjoyId, int scInstance, bool isHovered)
        => SCBindingsRenderer.DrawVJoyMappingRow(canvas, bounds, vjoyId, scInstance, isHovered);

    private void DrawVJoyMappingRowCompact(SKCanvas canvas, SKRect bounds, uint vjoyId, int scInstance, bool isHovered)
        => SCBindingsRenderer.DrawVJoyMappingRowCompact(canvas, bounds, vjoyId, scInstance, isHovered);

    private void DrawExportButton(SKCanvas canvas, SKRect bounds, string text, bool isHovered, bool isEnabled)
        => FUIWidgets.DrawExportButton(canvas, bounds, text, isHovered, isEnabled);

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
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Void, IsAntialias = true };
        canvas.DrawRect(_scImportDropdownBounds, bgPaint);
        using var innerPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background0, IsAntialias = true };
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
                using var hoverBg = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(40), IsAntialias = true };
                canvas.DrawRect(itemBounds, hoverBg);
                using var accentBar = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active, IsAntialias = true };
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
        items.AddRange(_scActionMaps.Select(FormatActionMapName));

        float totalContentHeight = items.Count * itemHeight + 4;
        float maxDropdownHeight = 300f;
        float dropdownHeight = Math.Min(totalContentHeight, maxDropdownHeight);
        bool needsScroll = totalContentHeight > maxDropdownHeight;
        float scrollbarWidth = needsScroll ? 8f : 0f;

        _scActionMapFilterMaxScroll = Math.Max(0, totalContentHeight - dropdownHeight);
        _scActionMapFilterScrollOffset = Math.Clamp(_scActionMapFilterScrollOffset, 0, _scActionMapFilterMaxScroll);

        _scActionMapFilterDropdownBounds = new SKRect(
            _scActionMapFilterBounds.Right - _scActionMapFilterBounds.Width,
            _scActionMapFilterBounds.Bottom + 2,
            _scActionMapFilterBounds.Right,
            _scActionMapFilterBounds.Bottom + 2 + dropdownHeight);

        // Map hover: _scHoveredActionMapFilter == -1 means "All Categories" row OR nothing.
        // Disambiguate using mouse position against the first item's Y range.
        float firstItemTop = _scActionMapFilterDropdownBounds.Top + 2 - _scActionMapFilterScrollOffset;
        bool allCatHovered = _scHoveredActionMapFilter == -1
            && _ctx.MousePosition.Y >= firstItemTop
            && _ctx.MousePosition.Y < firstItemTop + itemHeight;
        int hoveredIdx = allCatHovered ? 0
            : _scHoveredActionMapFilter >= 0 ? _scHoveredActionMapFilter + 1
            : -1;
        int selectedIdx = string.IsNullOrEmpty(_scActionMapFilter) ? 0
            : _scActionMaps.IndexOf(_scActionMapFilter) + 1;

        FUIWidgets.DrawDropdownPanel(canvas, _scActionMapFilterDropdownBounds, items,
            selectedIdx, hoveredIdx, itemHeight, _scActionMapFilterScrollOffset, scrollbarWidth);

        // Scrollbar (drawn on top of the panel)
        if (needsScroll)
        {
            float scrollTrackX = _scActionMapFilterDropdownBounds.Right - scrollbarWidth - 2;
            float scrollTrackY = _scActionMapFilterDropdownBounds.Top + 2;
            float scrollTrackHeight = dropdownHeight - 4;

            using var trackPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2.WithAlpha(80), IsAntialias = true };
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(scrollTrackX, scrollTrackY, scrollTrackX + scrollbarWidth, scrollTrackY + scrollTrackHeight), 2f), trackPaint);

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

        using var bgPaint4 = new SKPaint { Style = SKPaintStyle.Fill, Color = color.WithAlpha(25), IsAntialias = true };
        canvas.DrawRoundRect(bounds, 2f, 2f, bgPaint4);

        using var accentPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = color.WithAlpha(180) };
        canvas.DrawRect(new SKRect(bounds.Left, bounds.Top, bounds.Left + 3f, bounds.Bottom), accentPaint);

        FUIRenderer.DrawTextCentered(canvas, _scExportStatus, bounds, color, 13f);
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
        using var bgPaint5 = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Void, IsAntialias = true };
        canvas.DrawRect(bounds, bgPaint5);

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
                    delHovered ? FUIColors.TextBright : FUIColors.TextDim, 14f);
            }

            var textColor = isHovered ? FUIColors.TextBright : FUIColors.TextPrimary;
            float maxTextWidth = rowBounds.Width - (isHovered ? 56f : 40f); // extra room for × on hover
            string displayName = TruncateTextToWidth(profile.ProfileName, maxTextWidth, 10f);
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
            using var sepPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f, IsAntialias = true };
            canvas.DrawLine(bounds.Left + 12, sepY, bounds.Right - 12, sepY, sepPaint);

            // Corner accents on separator
            using var accentLinePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active.WithAlpha(120), StrokeWidth = 1f, IsAntialias = true };
            canvas.DrawLine(bounds.Left + 8, sepY, bounds.Left + 12, sepY, accentLinePaint);
            canvas.DrawLine(bounds.Right - 12, sepY, bounds.Right - 8, sepY, accentLinePaint);

            y += 6f;

            // Section label: make it clear these are SC files to import from, not Asteriq profiles
            FUIRenderer.DrawText(canvas, "IMPORT FROM SC", new SKPoint(bounds.Left + 10, y + 9f), FUIColors.TextDim, 12f, true);
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
                FUIRenderer.DrawText(canvas, displayName, new SKPoint(rowBounds.Left + 10, rowBounds.MidY + 4f), textColor, 13f);

                y += rowHeight;
            }
        }
    }

    private void DrawSCProfileButton(SKCanvas canvas, SKRect bounds, string icon, bool hovered, string tooltip, bool disabled = false)
        => SCBindingsRenderer.DrawSCProfileButton(canvas, bounds, icon, hovered, tooltip, disabled);

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

    private void DrawColumnActionsPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        if (_scGridColumns is null || _scHighlightedColumn < 0 || _scHighlightedColumn >= _scGridColumns.Count)
            return;

        var col = _scGridColumns[_scHighlightedColumn];

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

        FUIWidgets.DrawPanelTitle(canvas, leftMargin, rightMargin, ref y, "COLUMN ACTIONS", withDivider: true);

        // Column label + device name
        string colLabel = $"JS{col.SCInstance}";
        FUIRenderer.DrawText(canvas, colLabel, new SKPoint(leftMargin, y), FUIColors.Active, 13f, true);
        string? deviceName = GetPhysicalDeviceNameForVJoyColumn(col);
        if (deviceName is not null)
        {
            FUIRenderer.DrawText(canvas, " — ", new SKPoint(leftMargin + 26f, y), FUIColors.TextDim, 13f);
            string shortName = TruncateTextToWidth(deviceName, rightMargin - leftMargin - 48f, 10f);
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

        // Source profile selector
        float selectorH = 28f;
        var importableProfiles = _scExportProfiles.Where(p => p.ProfileName != _scExportProfile.ProfileName).ToList();
        bool hasProfiles = importableProfiles.Count > 0;
        string profileSelectorLabel = _scColImportProfileIndex >= 0 && _scColImportProfileIndex < importableProfiles.Count
            ? importableProfiles[_scColImportProfileIndex].ProfileName
            : (hasProfiles ? "Select profile…" : "No other profiles");
        _scColImportProfileSelectorBounds = new SKRect(leftMargin, y, rightMargin, y + selectorH);
        bool profileSelectorHovered = _scColImportProfileSelectorBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawSelector(canvas, _scColImportProfileSelectorBounds, profileSelectorLabel,
            profileSelectorHovered || _scColImportProfileDropdownOpen, hasProfiles);
        y += selectorH + 4f;

        // Source column selector
        bool hasSourceColumns = _scColImportSourceColumns.Count > 0;
        string columnSelectorLabel = _scColImportColumnIndex >= 0 && _scColImportColumnIndex < _scColImportSourceColumns.Count
            ? _scColImportSourceColumns[_scColImportColumnIndex].Label
            : (_scColImportProfileIndex >= 0 && !hasSourceColumns ? "No columns found" : "Select column…");
        _scColImportColumnSelectorBounds = new SKRect(leftMargin, y, rightMargin, y + selectorH);
        bool columnSelectorHovered = _scColImportColumnSelectorBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawSelector(canvas, _scColImportColumnSelectorBounds, columnSelectorLabel,
            columnSelectorHovered || _scColImportColumnDropdownOpen, hasSourceColumns);

        // Action buttons anchored to panel bottom
        float btnH = 28f;
        float btnW = (rightMargin - leftMargin - 8f) / 2f;
        float btnY = bounds.Bottom - frameInset - cornerPadding - btnH;

        bool canImport = _scColImportProfileIndex >= 0 && _scColImportColumnIndex >= 0;
        _scColImportButtonBounds = new SKRect(leftMargin, btnY, leftMargin + btnW, btnY + btnH);
        _scColImportButtonHovered = _scColImportButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        if (canImport)
        {
            FUIRenderer.DrawButton(canvas, _scColImportButtonBounds, "IMPORT",
                _scColImportButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
        }
        else
        {
            using var disabledPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2.WithAlpha(60), IsAntialias = true };
            canvas.DrawRect(_scColImportButtonBounds, disabledPaint);
            FUIRenderer.DrawTextCentered(canvas, "IMPORT", _scColImportButtonBounds, FUIColors.TextDim.WithAlpha(100), 12f);
        }

        _scDeselectButtonBounds = new SKRect(leftMargin + btnW + 8f, btnY, rightMargin, btnY + btnH);
        _scDeselectButtonHovered = _scDeselectButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIRenderer.DrawButton(canvas, _scDeselectButtonBounds, "DESELECT",
            _scDeselectButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
    }

    private void DrawColImportProfileDropdown(SKCanvas canvas)
    {
        var importableProfiles = _scExportProfiles.Where(p => p.ProfileName != _scExportProfile.ProfileName).ToList();
        if (importableProfiles.Count == 0) return;

        float itemH = 28f;
        _scColImportProfileDropdownBounds = new SKRect(
            _scColImportProfileSelectorBounds.Left,
            _scColImportProfileSelectorBounds.Bottom + 2,
            _scColImportProfileSelectorBounds.Right,
            _scColImportProfileSelectorBounds.Bottom + 2 + Math.Min(importableProfiles.Count * itemH + 8f, 200f));

        var items = importableProfiles.Select(p => p.ProfileName).ToList();
        FUIWidgets.DrawDropdownPanel(canvas, _scColImportProfileDropdownBounds, items,
            _scColImportProfileIndex, _scColImportProfileHoveredIndex, itemH);
    }

    private void DrawColImportColumnDropdown(SKCanvas canvas)
    {
        if (_scColImportSourceColumns.Count == 0) return;

        float itemH = 28f;
        _scColImportColumnDropdownBounds = new SKRect(
            _scColImportColumnSelectorBounds.Left,
            _scColImportColumnSelectorBounds.Bottom + 2,
            _scColImportColumnSelectorBounds.Right,
            _scColImportColumnSelectorBounds.Bottom + 2 + Math.Min(_scColImportSourceColumns.Count * itemH + 8f, 200f));

        var items = _scColImportSourceColumns.Select(c => c.Label).ToList();
        FUIWidgets.DrawDropdownPanel(canvas, _scColImportColumnDropdownBounds, items,
            _scColImportColumnIndex, _scColImportColumnHoveredIndex, itemH);
    }
}
