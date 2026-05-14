using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class SCBindingsTabController
{
    private void HandleBindingsTabClick(SKPoint point)
    {
        // Scrollbar click handling - start dragging
        if (_scroll.VScrollBounds.Contains(point.X, point.Y))
        {
            _scroll.IsDraggingVScroll = true;
            _scroll.DragStartY = point.Y;
            _scroll.DragStartOffset = _scBindingsScrollOffset;
            return;
        }

        if (_scroll.HScrollBounds.Contains(point.X, point.Y))
        {
            _scroll.IsDraggingHScroll = true;
            _scroll.DragStartX = point.X;
            _scroll.DragStartOffset = _grid.HorizontalScroll;
            return;
        }

        // "Show JS ref" checkbox
        if (_searchFilter.ShowJSRefBounds.HitTest(point))
        {
            _ctx.AppSettings.SCBindingsShowPhysicalHeaders = !_ctx.AppSettings.SCBindingsShowPhysicalHeaders;
            _ctx.MarkDirty();
            return;
        }

        // Column actions panel is only active when a vJoy (non-physical, non-readonly joystick) column is highlighted
        bool showColumnActions = IsColumnActionsVisible();

        // Column actions panel interactions â€” all guarded so stale bounds never intercept other panel clicks.
        // Skip when CP is spotlighted (column actions body is collapsed; body bounds are stale)
        // or when the CP dropdown overlay is open (its list visually covers column actions).
        if (showColumnActions && !_cpPanel.IsExpanded && !_profileMgmt.DropdownOpen)
        {
            // Profile dropdown â€” close on outside click
            if (_colImport.ProfileDropdownOpen)
            {
                if (_colImport.ProfileDropdownBounds.HitTest(point))
                {
                    var (savedProfiles, xmlFiles) = GetColImportSources();
                    int totalSources = savedProfiles.Count + xmlFiles.Count;
                    float itemH = 28f;
                    int idx = (int)((point.Y - _colImport.ProfileDropdownBounds.Top) / itemH);
                    if (idx >= 0 && idx < totalSources && idx != _colImport.ProfileIndex)
                    {
                        _colImport.ProfileIndex = idx;
                        LoadColImportSourceColumns();
                    }
                    _colImport.ProfileDropdownOpen = false;
                    _ctx.MarkDirty();
                    return;
                }
                else
                {
                    _colImport.ProfileDropdownOpen = false;
                    _ctx.MarkDirty();
                    return;
                }
            }

            // Column dropdown â€” close on outside click
            if (_colImport.ColumnDropdownOpen)
            {
                if (_colImport.ColumnDropdownBounds.HitTest(point))
                {
                    float itemH = 28f;
                    int idx = (int)((point.Y - _colImport.ColumnDropdownBounds.Top) / itemH);
                    if (idx >= 0 && idx < _colImport.SourceColumns.Count)
                        _colImport.ColumnIndex = idx;
                    _colImport.ColumnDropdownOpen = false;
                    _ctx.MarkDirty();
                    return;
                }
                else
                {
                    _colImport.ColumnDropdownOpen = false;
                    _ctx.MarkDirty();
                    return;
                }
            }

            if (_colImport.ClearColumnBounds.HitTest(point))
            {
                ClearColumnBindings();
                return;
            }

            if (_colImport.ImportButtonBounds.HitTest(point))
            {
                ExecuteImportFromProfile();
                return;
            }

            if (_colImport.ProfileSelectorBounds.HitTest(point))
            {
                var (savedProfiles, xmlFiles) = GetColImportSources();
                if (savedProfiles.Count + xmlFiles.Count > 0)
                {
                    _colImport.ProfileDropdownOpen = !_colImport.ProfileDropdownOpen;
                    _colImport.ColumnDropdownOpen = false;
                    _ctx.MarkDirty();
                }
                return;
            }

            if (_colImport.ColumnSelectorBounds.HitTest(point))
            {
                if (_colImport.SourceColumns.Count > 0)
                {
                    _colImport.ColumnDropdownOpen = !_colImport.ColumnDropdownOpen;
                    _colImport.ProfileDropdownOpen = false;
                    _ctx.MarkDirty();
                }
                return;
            }
        }

        // Column header click - toggle column highlight
        // Only vJoy (non-physical joystick) columns are selectable; mouse/keyboard columns are display-only.
        // Guard: skip if any dropdown is open (they render over the column header area)
        bool anyDropdownOpen = _scInstall.DropdownOpen || _searchFilter.FilterDropdownOpen || _profileMgmt.DropdownOpen;
        if (!anyDropdownOpen && _grid.ColumnHeadersBounds.Contains(point.X, point.Y))
        {
            int clickedCol = GetClickedColumnIndex(point.X);
            if (clickedCol >= 0
                && _grid.Columns is not null
                && _grid.Columns[clickedCol].IsJoystick
                && !_grid.Columns[clickedCol].IsPhysical)
            {
                // Clicking a column header always deselects any selected cell
                _cell.SelectedCell = (-1, -1);
                _scListening.IsListening = false;
                _conflicts.ConflictLinks.Clear();
                _conflicts.ConflictLinkBounds.Clear();

                if (_colImport.HighlightedColumn == clickedCol)
                {
                    DeselectColumn();
                    _cpPanel.IsExpanded = true;
                }
                else
                {
                    _colImport.HighlightedColumn = clickedCol;
                    // Reset import state for the newly selected column
                    _colImport.ProfileIndex = -1;
                    _colImport.ColumnIndex = -1;
                    _colImport.LoadedProfile = null;
                    _colImport.SourceColumns.Clear();
                    _colImport.ProfileDropdownOpen = false;
                    _colImport.ColumnDropdownOpen = false;
                    _cpPanel.IsExpanded = false; // Auto-expand Column Actions
                    _ctx.MarkDirty();
                }
                return;
            }
        }

        // SC Installation dropdown handling (close when clicking outside)
        if (_scInstall.DropdownOpen)
        {
            if (_scInstall.DropdownBounds.Contains(point))
            {
                // Click on dropdown item
                if (_scInstall.HoveredInstallation >= 0 && _scInstall.HoveredInstallation < _scInstall.Installations.Count
                    && _scInstall.HoveredInstallation != _scInstall.SelectedInstallation)
                {
                    _scInstall.SelectedInstallation = _scInstall.HoveredInstallation;
                    LoadSCSchema(_scInstall.Installations[_scInstall.SelectedInstallation], autoLoadProfileForEnvironment: true);
                    _ctx.AppSettings.PreferredSCEnvironment = _scInstall.Installations[_scInstall.SelectedInstallation].Environment;
                }
                _scInstall.DropdownOpen = false;
                return;
            }
            else
            {
                // Click outside - close dropdown
                _scInstall.DropdownOpen = false;
                return;
            }
        }

        // Action map filter dropdown handling
        if (_searchFilter.FilterDropdownOpen)
        {
            if (_searchFilter.FilterDropdownBounds.Contains(point))
            {
                // Calculate which item was clicked, accounting for scroll offset
                float itemHeight = 24f;
                float relativeY = point.Y - _searchFilter.FilterDropdownBounds.Top - 2 + _searchFilter.FilterScrollOffset;
                int clickedIndex = (int)(relativeY / itemHeight) - 1; // -1 because first item is "All Categories"

                if (clickedIndex < 0)
                {
                    // "All Categories" clicked
                    _searchFilter.ActionMapFilter = "";
                }
                else if (clickedIndex < _searchFilter.ActionMaps.Count)
                {
                    _searchFilter.ActionMapFilter = _searchFilter.ActionMaps[clickedIndex];
                }
                RefreshFilteredActions();
                _searchFilter.FilterDropdownOpen = false;
                _searchFilter.FilterScrollOffset = 0; // Reset scroll when closing
                return;
            }
            else
            {
                _searchFilter.FilterDropdownOpen = false;
                _searchFilter.FilterScrollOffset = 0; // Reset scroll when closing
                return;
            }
        }

        // SC Export profile dropdown handling
        if (_profileMgmt.DropdownOpen)
        {
            if (_profileMgmt.DropdownListBounds.Contains(point))
            {
                // Delete button takes priority over row click
                if (!string.IsNullOrEmpty(_profileMgmt.DropdownDeleteProfileName) &&
                    _profileMgmt.DropdownDeleteBounds.Contains(point))
                {
                    var nameToDelete = _profileMgmt.DropdownDeleteProfileName;
                    int deleteResult = FUIMessageBox.Show(_ctx.OwnerForm,
                        $"Delete control profile '{nameToDelete}'?",
                        "Delete Profile", FUIMessageBox.MessageBoxType.Question, "Delete", "Cancel");
                    if (deleteResult == 0)
                    {
                        _scExportProfileService?.DeleteProfile(nameToDelete);
                        // If the deleted profile was active, clear the active profile name
                        if (_scExportProfile.ProfileName == nameToDelete)
                            _scExportProfile.ProfileName = "";
                        RefreshSCExportProfiles();
                        _ctx.InvalidateCanvas();
                    }
                    _profileMgmt.DropdownOpen = false;
                    return;
                }

                // SC control profile delete takes the same priority as the saved-profile delete
                if (!string.IsNullOrEmpty(_profileMgmt.DropdownDeleteSCFilePath) &&
                    _profileMgmt.DropdownDeleteBounds.Contains(point))
                {
                    var pathToDelete = _profileMgmt.DropdownDeleteSCFilePath;
                    var nameToShow = _profileMgmt.DropdownDeleteSCDisplayName;
                    int deleteResult = FUIMessageBox.Show(_ctx.OwnerForm,
                        $"Delete SC control profile '{nameToShow}'?",
                        "Delete SC Control Profile", FUIMessageBox.MessageBoxType.Question, "Delete", "Cancel");
                    if (deleteResult == 0)
                    {
                        try
                        {
                            if (File.Exists(pathToDelete))
                                File.Delete(pathToDelete);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            System.Diagnostics.Debug.WriteLine($"[SCBindings] Failed to delete SC control profile '{pathToDelete}': {ex.Message}");
                            SetStatus($"Delete failed: {ex.Message}", SCStatusKind.Error);
                        }

                        if (_scInstall.SelectedInstallation >= 0 && _scInstall.SelectedInstallation < _scInstall.Installations.Count)
                        {
                            try
                            {
                                _scAvailableProfiles = SCInstallationService.GetExistingProfiles(_scInstall.Installations[_scInstall.SelectedInstallation]);
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                            {
                                System.Diagnostics.Debug.WriteLine($"[SCBindings] Failed to refresh SC control profile list after delete: {ex.Message}");
                            }
                        }
                        _ctx.InvalidateCanvas();
                    }
                    _profileMgmt.DropdownOpen = false;
                    return;
                }

                // Click on dropdown item
                if (_profileMgmt.HoveredProfileIndex >= 0)
                {
                    int scFileIndexOffset    = _profileMgmt.ExportProfiles.Count + 1000;
                    int remoteIndexOffset    = _profileMgmt.ExportProfiles.Count + 2000;

                    if (_profileMgmt.HoveredProfileIndex >= remoteIndexOffset)
                    {
                        // Remote profile from TX master â€” write to temp file and import
                        int remoteIdx = _profileMgmt.HoveredProfileIndex - remoteIndexOffset;
                        var remotes = _ctx.RemoteControlProfiles;
                        if (remoteIdx >= 0 && remoteIdx < remotes.Count)
                            ApplyRemoteControlProfile(remotes[remoteIdx]);
                    }
                    else if (_profileMgmt.HoveredProfileIndex >= scFileIndexOffset)
                    {
                        // SC mapping file â€” import it
                        int scFileIndex = _profileMgmt.HoveredProfileIndex - scFileIndexOffset;
                        if (scFileIndex >= 0 && scFileIndex < _scAvailableProfiles.Count)
                            ImportSCProfile(_scAvailableProfiles[scFileIndex]);
                    }
                    else if (_profileMgmt.HoveredProfileIndex < _profileMgmt.ExportProfiles.Count)
                    {
                        // Asteriq saved profile â€” load it
                        LoadSCExportProfile(_profileMgmt.ExportProfiles[_profileMgmt.HoveredProfileIndex].ProfileName);
                    }
                }
                _profileMgmt.DropdownOpen = false;
                return;
            }
            else
            {
                // Click outside list - close dropdown
                _profileMgmt.DropdownOpen = false;
                // If the click was on the toggle button itself, stop here so it doesn't re-open below
                if (_profileMgmt.DropdownBounds.Contains(point))
                    return;
                // Otherwise allow the click to reach other handlers below
            }
        }

        // Determine if CP panel content is visible (not collapsed behind a contextual panel).
        // Row-only selection also creates a contextual panel (Binding Definition).
        bool hasRowSelection = !showColumnActions
            && _cell.SelectedCell.actionIndex >= 0
            && _scFilteredActions is not null && _cell.SelectedCell.actionIndex < _scFilteredActions.Count;
        bool hasContextualPanel = showColumnActions || hasRowSelection;
        bool cpContentVisible = !hasContextualPanel || _cpPanel.IsExpanded;

        // CP panel content handlers â€” must run BEFORE header click, since header bounds overlap content
        if (cpContentVisible)
        {
            // Profile edit icon click (inside dropdown box)
            if (_profileMgmt.ProfileEditBounds != SKRect.Empty && _profileMgmt.ProfileEditBounds.Contains(point))
            {
                EditSCProfileName();
                return;
            }

            // SC Export profile dropdown toggle click
            if (_profileMgmt.DropdownBounds.Contains(point))
            {
                _profileMgmt.DropdownOpen = !_profileMgmt.DropdownOpen;
                _scInstall.DropdownOpen = false;
                _searchFilter.FilterDropdownOpen = false;
                _searchFilter.SearchBoxFocused = false;
                return;
            }

            // SC Export profile management buttons
            if (_profileMgmt.NewProfileBounds.Contains(point))
            {
                CreateNewSCExportProfile();
                return;
            }

            if (_profileMgmt.ImportProfileBounds.Contains(point))
            {
                BrowseAndImportSCConfig();
                return;
            }
        }

        // Panel header clicks â€” Control Profiles / contextual panel mutual-exclusive expand
        if (_cpPanel.HeaderBounds.HitTest(point))
        {
            // Toggle Control Profiles expand â€” contextual panel stays visible but collapses
            _cpPanel.IsExpanded = !_cpPanel.IsExpanded;
            _ctx.MarkDirty();
            return;
        }
        if (_colImport.HeaderBounds.HitTest(point) && showColumnActions)
        {
            _cpPanel.IsExpanded = !_cpPanel.IsExpanded;
            _ctx.MarkDirty();
            return;
        }
        if (_bdPanel.HeaderBounds.HitTest(point))
        {
            // BD header â€” bring Binding Definition to the spotlight. If it's already the
            // spotlight panel (CP collapsed + BD expanded), toggle back to CP-expanded.
            if (!_cpPanel.IsExpanded && _bdPanel.IsExpanded)
                _cpPanel.IsExpanded = true;
            else
            {
                _cpPanel.IsExpanded = false;
                _bdPanel.IsExpanded = true;
            }
            _ctx.MarkDirty();
            return;
        }
        if (_cellDetails.HeaderBounds.HitTest(point))
        {
            // Cell Details header â€” bring Cell Details to the spotlight. If it's already the
            // spotlight panel (CP collapsed + BD collapsed), toggle back to CP-expanded.
            if (!_cpPanel.IsExpanded && !_bdPanel.IsExpanded)
                _cpPanel.IsExpanded = true;
            else
            {
                _cpPanel.IsExpanded = false;
                _bdPanel.IsExpanded = false;
            }
            _ctx.MarkDirty();
            return;
        }

        // Activation mode segmented control clicks (Cell Details panel)
        if (_cell.SelectedCell.actionIndex >= 0 && _cell.SelectedCell.colIndex >= 0
            && _scFilteredActions is not null && _cell.SelectedCell.actionIndex < _scFilteredActions.Count
            && !_cpPanel.IsExpanded)
        {
            var action = _scFilteredActions[_cell.SelectedCell.actionIndex];
            if (_grid.Columns is not null && _cell.SelectedCell.colIndex < _grid.Columns.Count)
            {
                var col = _grid.Columns[_cell.SelectedCell.colIndex];
                var binding = FindBindingForCell(action, col);
                if (binding is not null)
                {
                    for (int i = 0; i < _cellDetails.ActivationModeBounds.Length; i++)
                    {
                        if (_cellDetails.ActivationModeBounds[i].HitTest(point))
                        {
                            var newMode = (SCActivationMode)i;
                            if (binding.ActivationMode != newMode)
                            {
                                binding.ActivationMode = newMode;
                                if (!string.IsNullOrEmpty(_scExportProfile.ProfileName))
                                    _scExportProfileService.SaveProfile(_scExportProfile);
                                _ctx.MarkDirty();
                            }
                            return;
                        }
                    }
                }
            }
        }

        // SC Installation selector click (toggle dropdown)
        if (_scInstall.SelectorBounds.Contains(point) && _scInstall.Installations.Count > 0)
        {
            _scInstall.DropdownOpen = !_scInstall.DropdownOpen;
            _searchFilter.FilterDropdownOpen = false;
            _profileMgmt.DropdownOpen = false;
            return;
        }

        // Browse for SC install path
        if (_scInstall.BrowseBounds.HitTest(point))
        {
            BrowseForSCInstallPath();
            return;
        }

        // Action map filter selector click
        if (_searchFilter.FilterBounds.Contains(point) && _searchFilter.ActionMaps.Count > 0)
        {
            _searchFilter.FilterDropdownOpen = !_searchFilter.FilterDropdownOpen;
            _scInstall.DropdownOpen = false;
            _profileMgmt.DropdownOpen = false;
            _searchFilter.SearchBoxFocused = false;
            return;
        }

        // Button capture toggle click
        if (_searchFilter.ButtonCaptureBounds.Contains(point))
        {
            _searchFilter.ButtonCaptureActive = !_searchFilter.ButtonCaptureActive;
            if (_searchFilter.ButtonCaptureActive)
            {
                StartButtonCapture();
                _searchFilter.SearchBoxFocused = false;
            }
            else
            {
                StopButtonCapture();
            }
            _scInstall.DropdownOpen = false;
            _searchFilter.FilterDropdownOpen = false;
            _profileMgmt.DropdownOpen = false;
            _ctx.MarkDirty();
            return;
        }

        // Search box click
        if (_searchFilter.SearchBoxBounds.Contains(point))
        {
            // Check if clicking the X to clear
            if (!string.IsNullOrEmpty(_searchFilter.SearchText) && point.X > _searchFilter.SearchBoxBounds.Right - 24)
            {
                _searchFilter.SearchText = "";
                _searchFilter.CursorPos = 0;
                ClearSearchSelection();
                _searchFilter.ButtonCaptureTextActive = false;
                _searchFilter.CaptureDeviceHidPath = null;
                RefreshFilteredActions();
            }
            else
            {
                _searchFilter.ButtonCaptureTextActive = false;
                _searchFilter.CaptureDeviceHidPath = null;
                _searchFilter.SearchBoxFocused = true;

                bool isDoubleClick = Environment.TickCount64 - _searchFilter.LastSearchClickTicks < SystemInformation.DoubleClickTime;
                _searchFilter.LastSearchClickTicks = Environment.TickCount64;

                if (isDoubleClick && !string.IsNullOrEmpty(_searchFilter.SearchText))
                {
                    // Double-click: select all text
                    _searchFilter.SelectionStart = 0;
                    _searchFilter.SelectionEnd = _searchFilter.SearchText.Length;
                    _searchFilter.CursorPos = _searchFilter.SearchText.Length;
                    _searchFilter.SearchDragging = false;
                }
                else
                {
                    // Single click: position cursor and start drag selection
                    float contentX = _searchFilter.SearchBoxBounds.Left + 24f;
                    float clickOffset = point.X - contentX;
                    int pos = !string.IsNullOrEmpty(_searchFilter.SearchText)
                        ? HitTestSearchCursorPos(_searchFilter.SearchText, clickOffset, 13f)
                        : 0;
                    _searchFilter.CursorPos = pos;
                    _searchFilter.SelectionStart = pos;
                    _searchFilter.SelectionEnd = pos;
                    _searchFilter.SearchDragging = true;
                }
            }
            _scInstall.DropdownOpen = false;
            _searchFilter.FilterDropdownOpen = false;
            _profileMgmt.DropdownOpen = false;
            return;
        }
        else
        {
            // Click outside search box unfocuses it
            _searchFilter.SearchBoxFocused = false;
            ClearSearchSelection();
        }

        // Show Bound Only checkbox click
        if (_searchFilter.ShowBoundOnlyBounds.Contains(point))
        {
            _ctx.AppSettings.SCBindingsShowBoundOnly = !_ctx.AppSettings.SCBindingsShowBoundOnly;
            RefreshFilteredActions();
            return;
        }

        // Export / Clear All / Reset Defaults â€” inside CP panel, guard against stale bounds
        if (cpContentVisible)
        {
            if (_scExportButtonBounds.Contains(point))
            {
                if (_conflicts.DuplicateActionBindings.Count > 0)
                {
                    SetStatus("Resolve duplicate action bindings across joystick columns before exporting", SCStatusKind.Error);
                    return;
                }
                ExportToSC();
                return;
            }

            if (_scClearAllButtonBounds.Contains(point) && _scExportProfile.Bindings.Count > 0)
            {
                ClearAllBindings();
                return;
            }

            if (_scResetDefaultsButtonBounds.Contains(point))
            {
                ResetToDefaults();
                return;
            }
        }

        // Conflict link clicks â€” navigate to the conflicting action
        for (int ci = 0; ci < _conflicts.ConflictLinkBounds.Count; ci++)
        {
            if (_conflicts.ConflictLinkBounds[ci].HitTest(point))
            {
                if (_scFilteredActions is not null && ci < _conflicts.ConflictLinks.Count)
                {
                    var (linkMap, linkName) = _conflicts.ConflictLinks[ci];
                    int targetIdx = _scFilteredActions.FindIndex(a =>
                        a.ActionMap == linkMap && a.ActionName == linkName);

                    // If target isn't visible (filtered out by category), switch to its category
                    if (targetIdx < 0)
                    {
                        string targetCategory = SCCategoryMapper.GetCategoryNameForAction(linkMap, linkName);
                        _searchFilter.ActionMapFilter = targetCategory;
                        RefreshFilteredActions();
                        // Re-search in the now-updated filtered list
                        targetIdx = _scFilteredActions?.FindIndex(a =>
                            a.ActionMap == linkMap && a.ActionName == linkName) ?? -1;
                    }

                    if (targetIdx >= 0)
                        ScrollToAction(targetIdx);
                }
                return;
            }
        }

        // Assign input button â€” activates the listener on the selected cell (same as double-click)
        if (_scAssignInputButtonBounds.Contains(point) && _scSelectedActionIndex >= 0)
        {
            if (_cell.SelectedCell.actionIndex >= 0 && _cell.SelectedCell.colIndex >= 0 &&
                _grid.Columns is not null && _cell.SelectedCell.colIndex < _grid.Columns.Count)
            {
                var col = _grid.Columns[_cell.SelectedCell.colIndex];
                if (!col.IsReadOnly)
                {
                    // Block ASSIGN for shared cells â€” user must unshare first
                    if (col.IsJoystick && !col.IsPhysical && _scFilteredActions is not null
                        && _cell.SelectedCell.actionIndex < _scFilteredActions.Count)
                    {
                        var selectedActionForAssign = _scFilteredActions[_cell.SelectedCell.actionIndex];
                        string assignSharedKey = $"{selectedActionForAssign.Key}|{col.VJoyDeviceId}";
                        if (_conflicts.SharedCells.ContainsKey(assignSharedKey))
                            return;
                    }

                    _scListening.IsListening = true;
                    _scListening.StartTime = DateTime.Now;
                    _cell.ListeningColumn = col;

                    if (col.IsKeyboard)
                        ClearStaleKeyPresses();
                    if (col.IsMouse)
                        ClearStaleMousePresses();

                    System.Diagnostics.Debug.WriteLine($"[SCBindings] ASSIGN button: started listening on cell ({_cell.SelectedCell.actionIndex}, {_cell.SelectedCell.colIndex}) - {col.Header}");
                }
            }
            return;
        }

        // Clear binding button (also serves as UNSHARE for shared cells)
        if (_scClearBindingButtonBounds.Contains(point) && _scSelectedActionIndex >= 0 && _scFilteredActions is not null)
        {
            var selectedAction = _scFilteredActions[_scSelectedActionIndex];

            // If a cell is selected, clear the binding for that specific column
            if (_cell.SelectedCell.colIndex >= 0 && _grid.Columns is not null && _cell.SelectedCell.colIndex < _grid.Columns.Count)
            {
                var selCol = _grid.Columns[_cell.SelectedCell.colIndex];

                // For shared cells, show the unshare dialog instead of clearing a binding
                if (selCol.IsJoystick && !selCol.IsPhysical)
                {
                    string clearSharedKey = $"{selectedAction.Key}|{selCol.VJoyDeviceId}";
                    if (_conflicts.SharedCells.ContainsKey(clearSharedKey))
                    {
                        HandleSharedCellClick(selectedAction, selCol);
                        return;
                    }
                }

                if (selCol.IsPhysical)
                {
                    var binding = _scExportProfile.Bindings.FirstOrDefault(b =>
                        b.ActionMap == selectedAction.ActionMap && b.ActionName == selectedAction.ActionName &&
                        b.DeviceType == SCDeviceType.Joystick &&
                        b.PhysicalDeviceId == selCol.PhysicalDeviceKey);
                    if (binding is not null)
                        _scExportProfile.RemoveBinding(binding);
                }
                else if (selCol.IsJoystick)
                {
                    var binding = _scExportProfile.Bindings.FirstOrDefault(b =>
                        b.ActionMap == selectedAction.ActionMap && b.ActionName == selectedAction.ActionName &&
                        b.DeviceType == SCDeviceType.Joystick &&
                        b.PhysicalDeviceId is null &&
                        _scExportProfile.GetSCInstance(b.VJoyDevice) == selCol.SCInstance);
                    if (binding is not null)
                        _scExportProfile.RemoveBinding(binding);
                }
                else
                {
                    _scExportProfile.RemoveBinding(selectedAction.ActionMap, selectedAction.ActionName);
                }
            }
            else
            {
                _scExportProfile.RemoveBinding(selectedAction.ActionMap, selectedAction.ActionName);
            }

            _scExportProfileService?.SaveProfile(_scExportProfile);
            UpdateConflictingBindings();
            UpdateSharedCells();
            UpdateConflictLinks();
            _ctx.MarkDirty();

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
                    if (clickedCol >= 0 && _grid.Columns is not null && clickedCol < _grid.Columns.Count)
                    {
                        // Cell was clicked - enter listening mode
                        HandleCellClick(i, clickedCol);
                    }
                    else
                    {
                        // Action name area clicked â€” check for cross-column duplicates first
                        if (_scFilteredActions is not null && i < _scFilteredActions.Count)
                        {
                            var clickedAction = _scFilteredActions[i];
                            if (TryShowDuplicateResolveDialog(clickedAction))
                                return;
                        }

                        // Row-only selection: clicking the action name selects the row without
                        // a cell, opening the Binding Definition panel for the user to read about
                        // the action. Re-clicking the same row's name with BD already expanded
                        // toggles back to no-selection (matches the legacy "click name = back to CP"
                        // muscle memory).
                        bool sameRowAlreadyOpen =
                            _cell.SelectedCell.actionIndex == i && _cell.SelectedCell.colIndex < 0
                            && _bdPanel.IsExpanded;

                        _scListening.IsListening = false;
                        _conflicts.ConflictLinks.Clear();
                        _conflicts.ConflictLinkBounds.Clear();

                        if (sameRowAlreadyOpen)
                        {
                            _cell.SelectedCell = (-1, -1);
                            _bdPanel.IsExpanded = false;
                            if (_colImport.HighlightedColumn < 0)
                                _cpPanel.IsExpanded = true;
                        }
                        else
                        {
                            _cell.SelectedCell = (i, -1);
                            _bdPanel.IsExpanded = true;
                            _cpPanel.IsExpanded = false;
                        }
                    }
                    return;
                }

                currentY += rowHeight + rowGap;
            }

            // Click was in list area but not on a row - clear selection
            _cell.SelectedCell = (-1, -1);
            _scListening.IsListening = false;
            if (_colImport.HighlightedColumn < 0)
                _cpPanel.IsExpanded = true;
        }
    }

    private int GetClickedColumnIndex(float x)
    {
        if (_grid.Columns is null || x < _grid.DeviceColsStart || x > _grid.DeviceColsStart + _grid.VisibleDeviceWidth)
            return -1;

        float relativeX = x - _grid.DeviceColsStart + _grid.HorizontalScroll;

        // Walk through columns to find which one contains this X
        float cumX = 0f;
        for (int c = 0; c < _grid.Columns.Count; c++)
        {
            float colW = _grid.DeviceColWidths.TryGetValue(_grid.Columns[c].Id, out var w) ? w : _grid.DeviceColMinWidth;
            if (relativeX >= cumX && relativeX < cumX + colW)
                return c;
            cumX += colW;
        }

        return -1;
    }

    private int GetHoveredColumnIndex(float x)
    {
        if (_grid.Columns is null || x < _grid.DeviceColsStart || x > _grid.DeviceColsStart + _grid.VisibleDeviceWidth)
            return -1;

        float relativeX = x - _grid.DeviceColsStart + _grid.HorizontalScroll;

        float cumX = 0f;
        for (int c = 0; c < _grid.Columns.Count; c++)
        {
            float colW = _grid.DeviceColWidths.TryGetValue(_grid.Columns[c].Id, out var w) ? w : _grid.DeviceColMinWidth;
            if (relativeX >= cumX && relativeX < cumX + colW)
                return c;
            cumX += colW;
        }

        return -1;
    }

    private void DeselectColumn()
    {
        _colImport.HighlightedColumn = -1;
        _colImport.ProfileIndex = -1;
        _colImport.ColumnIndex = -1;
        _colImport.LoadedProfile = null;
        _colImport.SourceColumns.Clear();
        _colImport.ProfileDropdownOpen = false;
        _colImport.ColumnDropdownOpen = false;
        _ctx.MarkDirty();
    }

    /// <summary>
    /// Called when the user selects a source profile; loads that profile and builds the
    /// list of its vJoy columns available to import from.
    /// Supports both saved Asteriq profiles and SC XML mapping files.
    /// </summary>
}