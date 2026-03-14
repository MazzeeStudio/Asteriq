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
        if (!_searchFilter.ShowJSRefBounds.IsEmpty && _searchFilter.ShowJSRefBounds.Contains(point))
        {
            _ctx.AppSettings.SCBindingsShowPhysicalHeaders = !_ctx.AppSettings.SCBindingsShowPhysicalHeaders;
            _ctx.MarkDirty();
            return;
        }

        // Column actions panel is only active when a vJoy (non-physical, non-readonly joystick) column is highlighted
        bool showColumnActions = _colImport.HighlightedColumn >= 0
            && _grid.Columns is not null
            && _colImport.HighlightedColumn < _grid.Columns.Count
            && _grid.Columns[_colImport.HighlightedColumn].IsJoystick
            && !_grid.Columns[_colImport.HighlightedColumn].IsPhysical
            && !_grid.Columns[_colImport.HighlightedColumn].IsReadOnly;

        // Column actions panel interactions — all guarded so stale bounds never intercept other panel clicks
        if (showColumnActions)
        {
            // Profile dropdown — close on outside click
            if (_colImport.ProfileDropdownOpen)
            {
                if (!_colImport.ProfileDropdownBounds.IsEmpty && _colImport.ProfileDropdownBounds.Contains(point))
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

            // Column dropdown — close on outside click
            if (_colImport.ColumnDropdownOpen)
            {
                if (!_colImport.ColumnDropdownBounds.IsEmpty && _colImport.ColumnDropdownBounds.Contains(point))
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

            if (!_colImport.ClearColumnBounds.IsEmpty && _colImport.ClearColumnBounds.Contains(point))
            {
                ClearColumnBindings();
                return;
            }

            if (!_colImport.ImportButtonBounds.IsEmpty && _colImport.ImportButtonBounds.Contains(point))
            {
                ExecuteImportFromProfile();
                return;
            }

            if (!_colImport.ProfileSelectorBounds.IsEmpty && _colImport.ProfileSelectorBounds.Contains(point))
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

            if (!_colImport.ColumnSelectorBounds.IsEmpty && _colImport.ColumnSelectorBounds.Contains(point))
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
                && !_grid.Columns[clickedCol].IsPhysical
                && !_grid.Columns[clickedCol].IsReadOnly)
            {
                if (_colImport.HighlightedColumn == clickedCol)
                {
                    DeselectColumn();
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
                    if (_scProfileDirty)
                    {
                        using var dialog = new FUIConfirmDialog(
                            "Unsaved Changes",
                            $"Profile '{_scExportProfile.ProfileName}' has an unsaved name change.\n\nSwitch installation and discard changes?",
                            "Discard & Switch", "Cancel");
                        if (dialog.ShowDialog(_ctx.OwnerForm) != DialogResult.Yes)
                        {
                            _scInstall.DropdownOpen = false;
                            return;
                        }
                    }

                    _scInstall.SelectedInstallation = _scInstall.HoveredInstallation;
                    _scProfileDirty = false;
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
                    var confirmed = FUIMessageBox.ShowQuestion(_ctx.OwnerForm,
                        $"Delete control profile '{nameToDelete}'?",
                        "Delete Profile");
                    if (confirmed)
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

                // Click on dropdown item
                if (_profileMgmt.HoveredProfileIndex >= 0)
                {
                    int scFileIndexOffset    = _profileMgmt.ExportProfiles.Count + 1000;
                    int remoteIndexOffset    = _profileMgmt.ExportProfiles.Count + 2000;

                    if (_profileMgmt.HoveredProfileIndex >= remoteIndexOffset)
                    {
                        // Remote profile from TX master — write to temp file and import
                        int remoteIdx = _profileMgmt.HoveredProfileIndex - remoteIndexOffset;
                        var remotes = _ctx.RemoteControlProfiles;
                        if (remoteIdx >= 0 && remoteIdx < remotes.Count)
                            ApplyRemoteControlProfile(remotes[remoteIdx]);
                    }
                    else if (_profileMgmt.HoveredProfileIndex >= scFileIndexOffset)
                    {
                        // SC mapping file — import it
                        int scFileIndex = _profileMgmt.HoveredProfileIndex - scFileIndexOffset;
                        if (scFileIndex >= 0 && scFileIndex < _scAvailableProfiles.Count)
                            ImportSCProfile(_scAvailableProfiles[scFileIndex]);
                    }
                    else if (_profileMgmt.HoveredProfileIndex < _profileMgmt.ExportProfiles.Count)
                    {
                        // Asteriq saved profile — load it
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

        // Device Order panel — dropdown open/close
        if (_deviceOrder.OpenRow >= 0)
        {
            if (!_deviceOrder.DropdownBounds.IsEmpty && _deviceOrder.DropdownBounds.Contains(point))
            {
                // Click inside open dropdown: select a vJoy slot for this row's SC instance
                float itemH = 28f;
                int idx = (int)((point.Y - _deviceOrder.DropdownBounds.Top) / itemH);
                var existingSlots = _ctx.VJoyDevices.Where(v => v.Exists).OrderBy(v => v.Id).ToList();
                if (idx >= 0 && idx < existingSlots.Count)
                    AssignDeviceOrderSlot(_deviceOrder.OpenRow + 1, existingSlots[idx].Id);
                _deviceOrder.OpenRow = -1;
                _deviceOrder.HoveredIndex = -1;
                _ctx.MarkDirty();
                return;
            }
            else
            {
                // Click outside dropdown — close it
                _deviceOrder.OpenRow = -1;
                _deviceOrder.HoveredIndex = -1;
                _ctx.MarkDirty();
                // If the click landed on a row selector, return now to prevent the
                // row selector loop below from immediately re-opening the dropdown
                for (int i = 0; i < _deviceOrder.SelectorBounds.Length; i++)
                {
                    if (!_deviceOrder.SelectorBounds[i].IsEmpty && _deviceOrder.SelectorBounds[i].Contains(point))
                        return;
                }
                // Otherwise fall through so other panel clicks still work
            }
        }

        // Panel header clicks — Device Order / Control Profiles mutual-exclusive expand
        if (!_deviceOrder.HeaderBounds.IsEmpty && _deviceOrder.HeaderBounds.Contains(point))
        {
            if (!_deviceOrder.IsExpanded)
            {
                _deviceOrder.IsExpanded = true;
                _deviceOrder.OpenRow = -1;
                _ctx.MarkDirty();
            }
            return;
        }
        if (!_deviceOrder.ControlProfilesHeaderBounds.IsEmpty && _deviceOrder.ControlProfilesHeaderBounds.Contains(point))
        {
            if (_deviceOrder.IsExpanded)
            {
                _deviceOrder.IsExpanded = false;
                _ctx.MarkDirty();
            }
            return;
        }

        // Device Order panel — auto-detect button (only when expanded)
        if (_deviceOrder.IsExpanded && !_deviceOrder.AutoDetectBounds.IsEmpty && _deviceOrder.AutoDetectBounds.Contains(point)
            && _directInputService is not null)
        {
            RunDeviceOrderAutoDetect();
            return;
        }

        // Device Order panel — row selector clicks (only when expanded)
        if (_deviceOrder.IsExpanded)
        for (int row = 0; row < _deviceOrder.SelectorBounds.Length; row++)
        {
            if (!_deviceOrder.SelectorBounds[row].IsEmpty && _deviceOrder.SelectorBounds[row].Contains(point))
            {
                int vjoyCount = _ctx.VJoyDevices.Count(v => v.Exists);
                if (vjoyCount > 1) // no point opening a single-item dropdown
                {
                    _deviceOrder.OpenRow = _deviceOrder.OpenRow == row ? -1 : row;
                    _deviceOrder.HoveredIndex = -1;
                    _ctx.MarkDirty();
                }
                return;
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

        // Action map filter selector click
        if (_searchFilter.FilterBounds.Contains(point) && _searchFilter.ActionMaps.Count > 0)
        {
            _searchFilter.FilterDropdownOpen = !_searchFilter.FilterDropdownOpen;
            _scInstall.DropdownOpen = false;
            _profileMgmt.DropdownOpen = false;
            _searchFilter.SearchBoxFocused = false;
            return;
        }

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
        if (_profileMgmt.SaveProfileBounds.Contains(point))
        {
            SaveSCExportProfile();
            return;
        }

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
                _searchFilter.ButtonCaptureTextActive = false;
                _searchFilter.CaptureDeviceHidPath = null;
                RefreshFilteredActions();
            }
            else
            {
                _searchFilter.ButtonCaptureTextActive = false;
                _searchFilter.CaptureDeviceHidPath = null;
                _searchFilter.SearchBoxFocused = true;
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
        }

        // Show Bound Only checkbox click
        if (_searchFilter.ShowBoundOnlyBounds.Contains(point))
        {
            _ctx.AppSettings.SCBindingsShowBoundOnly = !_ctx.AppSettings.SCBindingsShowBoundOnly;
            RefreshFilteredActions();
            return;
        }

        // Export button
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

        // Conflict link clicks — navigate to the conflicting action
        for (int ci = 0; ci < _conflicts.ConflictLinkBounds.Count; ci++)
        {
            if (!_conflicts.ConflictLinkBounds[ci].IsEmpty && _conflicts.ConflictLinkBounds[ci].Contains(point))
            {
                if (_scFilteredActions is not null && ci < _conflicts.ConflictLinks.Count)
                {
                    var (linkMap, linkName) = _conflicts.ConflictLinks[ci];
                    int targetIdx = _scFilteredActions.FindIndex(a =>
                        a.ActionMap == linkMap && a.ActionName == linkName);
                    if (targetIdx >= 0)
                        ScrollToAction(targetIdx);
                }
                return;
            }
        }

        // Assign input button — activates the listener on the selected cell (same as double-click)
        if (_scAssignInputButtonBounds.Contains(point) && _scSelectedActionIndex >= 0)
        {
            if (_cell.SelectedCell.actionIndex >= 0 && _cell.SelectedCell.colIndex >= 0 &&
                _grid.Columns is not null && _cell.SelectedCell.colIndex < _grid.Columns.Count)
            {
                var col = _grid.Columns[_cell.SelectedCell.colIndex];
                if (!col.IsReadOnly)
                {
                    // Block ASSIGN for shared cells — user must unshare first
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
                        b.PhysicalDeviceId == selCol.PhysicalDevice!.HidDevicePath);
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
                        // Action name area clicked — check for cross-column duplicates first
                        if (_scFilteredActions is not null && i < _scFilteredActions.Count)
                        {
                            var clickedAction = _scFilteredActions[i];
                            if (TryShowDuplicateResolveDialog(clickedAction))
                                return;
                        }
                        _cell.SelectedCell = (-1, -1);
                        _scListening.IsListening = false;
                    }
                    return;
                }

                currentY += rowHeight + rowGap;
            }

            // Click was in list area but not on a row - clear selection
            _cell.SelectedCell = (-1, -1);
            _scListening.IsListening = false;
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

    private void HandleCellClick(int actionIndex, int colIndex)
    {
        if (_grid.Columns is null || colIndex < 0 || colIndex >= _grid.Columns.Count)
            return;
        if (_scFilteredActions is null || actionIndex < 0 || actionIndex >= _scFilteredActions.Count)
            return;

        var col = _grid.Columns[colIndex];

        // Read-only columns display bindings but do not accept new assignments
        if (col.IsReadOnly)
            return;

        // If already listening, cancel
        if (_scListening.IsListening)
        {
            _scListening.IsListening = false;
            _cell.ListeningColumn = null;
        }

        // Check for double-click on the same cell (within 400ms)
        bool isDoubleClick = _cell.SelectedCell == (actionIndex, colIndex) &&
                            (DateTime.Now - _cell.LastCellClickTime).TotalMilliseconds < 400;

        // Shared cells: select normally but never enter listen mode — use CLEAR/right-click to unshare
        if (col.IsJoystick && !col.IsPhysical)
        {
            var action = _scFilteredActions[actionIndex];
            string sharedKey = $"{action.Key}|{col.VJoyDeviceId}";
            if (_conflicts.SharedCells.ContainsKey(sharedKey))
            {
                _cell.SelectedCell = (actionIndex, colIndex);
                _cell.LastCellClickTime = DateTime.Now;
                UpdateConflictLinks();
                return;
            }
        }

        if (isDoubleClick)
        {
            // Double-click: enter listening mode
            _scListening.IsListening = true;
            _scListening.StartTime = DateTime.Now;
            _cell.ListeningColumn = col;
            _conflicts.ConflictLinks.Clear();
            _conflicts.ConflictLinkBounds.Clear();

            // Clear stale presses before detecting
            if (col.IsKeyboard)
                ClearStaleKeyPresses();
            if (col.IsMouse)
                ClearStaleMousePresses();

            System.Diagnostics.Debug.WriteLine($"[SCBindings] Started listening for input on cell ({actionIndex}, {colIndex}) - {col.Header}");
        }
        else
        {
            // Single click — check if this row has a cross-column duplicate to resolve
            if (col.IsJoystick && !col.IsPhysical && _scFilteredActions is not null)
            {
                var action = _scFilteredActions[actionIndex];
                if (TryShowDuplicateResolveDialog(action))
                    return;
            }

            // Single click: just select the cell
            _cell.SelectedCell = (actionIndex, colIndex);
            _cell.LastCellClickTime = DateTime.Now;
            _conflicts.HighlightActionIndex = -1;
            UpdateConflictLinks();
            System.Diagnostics.Debug.WriteLine($"[SCBindings] Selected cell ({actionIndex}, {colIndex}) - {col.Header}");
        }
    }

    /// <summary>
    /// If the given action has a cross-column duplicate binding, opens the SCSharedBindingDialog
    /// so the user can REPLACE (clear the higher JS) or SHARE (reroute physical button).
    /// Returns true if the dialog was shown (caller should not proceed with normal selection).
    /// </summary>
    private bool TryShowDuplicateResolveDialog(SCAction action)
    {
        // Find the duplicate (higher-JS) binding for this action
        var dupBinding = _scExportProfile.Bindings.FirstOrDefault(b =>
            b.ActionMap == action.ActionMap && b.ActionName == action.ActionName &&
            b.DeviceType == SCDeviceType.Joystick && b.PhysicalDeviceId is null &&
            _conflicts.DuplicateActionBindings.Contains(b.Key));

        if (dupBinding is null) return false;

        // Find the base (lower-JS) binding
        var baseBinding = _scExportProfile.Bindings.FirstOrDefault(b =>
            b.ActionMap == action.ActionMap && b.ActionName == action.ActionName &&
            b.DeviceType == SCDeviceType.Joystick && b.PhysicalDeviceId is null &&
            b.VJoyDevice != dupBinding.VJoyDevice &&
            _scExportProfile.GetSCInstance(b.VJoyDevice) < _scExportProfile.GetSCInstance(dupBinding.VJoyDevice));

        if (baseBinding is null) return false;

        int baseInst = _scExportProfile.GetSCInstance(baseBinding.VJoyDevice);
        int dupInst  = _scExportProfile.GetSCInstance(dupBinding.VJoyDevice);

        using var dialog = new SCSharedBindingDialog(
            SCCategoryMapper.FormatActionName(action.ActionName),
            $"JS{baseInst}",
            SCBindingsRenderer.FormatInputName(baseBinding.InputName),
            $"JS{dupInst}",
            SCBindingsRenderer.FormatInputName(dupBinding.InputName));

        dialog.ShowDialog(_ctx.OwnerForm);

        switch (dialog.Result)
        {
            case SCSharedBindingResult.Replace:
                // Remove the higher-JS duplicate, keep the base
                _scExportProfile.RemoveBinding(dupBinding);
                _scExportProfile.Modified = DateTime.UtcNow;
                _scExportProfileService?.SaveProfile(_scExportProfile);
                UpdateConflictingBindings();
                UpdateSharedCells();
                SetStatus($"Cleared JS{dupInst} binding for {SCCategoryMapper.FormatActionName(action.ActionName)}");
                _ctx.MarkDirty();
                return true;

            case SCSharedBindingResult.Share:
                // Keep base binding, reroute the duplicate's physical button to the base slot
                PerformShare(baseBinding, dupBinding.VJoyDevice, dupBinding.InputName);
                _scExportProfile.RemoveBinding(dupBinding);
                _scExportProfile.Modified = DateTime.UtcNow;
                _scExportProfileService?.SaveProfile(_scExportProfile);
                UpdateSharedCells();
                UpdateConflictingBindings();
                SetStatus($"Shared: JS{dupInst} → JS{baseInst}");
                _ctx.MarkDirty();
                return true;
        }

        // Cancel: no change was made — let the caller proceed with normal cell selection
        // so the user can still see conflict links and binding details in the right panel.
        return false;
    }

    private void HandleCellRightClick(int actionIndex, int colIndex)
    {
        if (_grid.Columns is null || colIndex < 0 || colIndex >= _grid.Columns.Count)
            return;
        if (_scFilteredActions is null || actionIndex < 0 || actionIndex >= _scFilteredActions.Count)
            return;

        var col = _grid.Columns[colIndex];
        var action = _scFilteredActions[actionIndex];

        // Cancel listening if active
        if (_scListening.IsListening)
        {
            CancelSCInputListening();
        }

        // Clear binding for this action on this column's device
        if (col.IsPhysical)
        {
            // Physical device column: find the specific binding by PhysicalDeviceId
            var binding = _scExportProfile.Bindings.FirstOrDefault(b =>
                b.ActionMap == action.ActionMap && b.ActionName == action.ActionName &&
                b.DeviceType == SCDeviceType.Joystick &&
                b.PhysicalDeviceId == col.PhysicalDevice!.HidDevicePath);
            if (binding is not null)
            {
                _scExportProfile.RemoveBinding(binding);
                _scExportProfileService?.SaveProfile(_scExportProfile);
                UpdateConflictingBindings();
                UpdateSharedCells();
                UpdateConflictLinks();
                _ctx.MarkDirty();
                System.Diagnostics.Debug.WriteLine($"[SCBindings] Cleared physical JS binding for {action.ActionName} on {col.Header}");
            }
        }
        else if (col.IsJoystick)
        {
            // For shared cells, show the unshare dialog instead of clearing a binding
            if (!col.IsPhysical)
            {
                string rightClickSharedKey = $"{action.Key}|{col.VJoyDeviceId}";
                if (_conflicts.SharedCells.ContainsKey(rightClickSharedKey))
                {
                    HandleSharedCellClick(action, col);
                    return;
                }
            }

            // vJoy column: find binding matching this column's SCInstance
            var userBinding = _scExportProfile.Bindings.FirstOrDefault(b =>
                b.ActionMap == action.ActionMap && b.ActionName == action.ActionName &&
                b.DeviceType == SCDeviceType.Joystick &&
                b.PhysicalDeviceId is null &&
                _scExportProfile.GetSCInstance(b.VJoyDevice) == col.SCInstance);
            if (userBinding is not null)
            {
                _scExportProfile.RemoveBinding(userBinding);
                _scExportProfileService?.SaveProfile(_scExportProfile);
                UpdateConflictingBindings();
                UpdateSharedCells();
                UpdateConflictLinks();
                _ctx.MarkDirty();
                System.Diagnostics.Debug.WriteLine($"[SCBindings] Cleared vJoy JS binding for {action.ActionName} on {col.Header}");
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
                if (clickedCol >= 0 && _grid.Columns is not null && clickedCol < _grid.Columns.Count)
                {
                    HandleCellRightClick(i, clickedCol);
                }
                return;
            }

            currentY += rowHeight + rowGap;
        }
    }

    private bool HandleSearchBoxKey(Keys keyData)
    {
        var key = keyData & Keys.KeyCode;

        if (key == Keys.Escape)
        {
            _searchFilter.SearchBoxFocused = false;
            return true;
        }

        if (key == Keys.Back)
        {
            _searchFilter.ButtonCaptureTextActive = false;
            _searchFilter.CaptureDeviceHidPath = null;
            if (_searchFilter.SearchText.Length > 0)
            {
                _searchFilter.SearchText = _searchFilter.SearchText.Substring(0, _searchFilter.SearchText.Length - 1);
                RefreshFilteredActions();
            }
            return true;
        }

        if (key == Keys.Delete)
        {
            _searchFilter.SearchText = "";
            _searchFilter.ButtonCaptureTextActive = false;
            _searchFilter.CaptureDeviceHidPath = null;
            RefreshFilteredActions();
            return true;
        }

        char c = KeyToChar(key, (keyData & Keys.Shift) == Keys.Shift);
        if (c != '\0' && _searchFilter.SearchText.Length < 50)
        {
            _searchFilter.ButtonCaptureTextActive = false;
            _searchFilter.CaptureDeviceHidPath = null;
            _searchFilter.SearchText += c;
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
    private void LoadColImportSourceColumns()
    {
        _colImport.LoadedProfile = null;
        _colImport.SourceColumns.Clear();
        _colImport.ColumnIndex = -1;

        if (_colImport.ProfileIndex < 0) return;

        var (savedProfiles, xmlFiles) = GetColImportSources();
        int savedCount = savedProfiles.Count;

        if (_colImport.ProfileIndex < savedCount)
        {
            // Load saved Asteriq JSON profile
            _colImport.LoadedProfile = _scExportProfileService.LoadProfile(savedProfiles[_colImport.ProfileIndex].ProfileName);
        }
        else
        {
            // Load from SC XML mapping file
            int xmlIdx = _colImport.ProfileIndex - savedCount;
            if (xmlIdx < xmlFiles.Count)
            {
                var xmlFile = xmlFiles[xmlIdx];
                var importResult = SCXmlExportService.ImportFromFile(xmlFile.FilePath);
                if (importResult.Success)
                {
                    _colImport.LoadedProfile = new SCExportProfile { ProfileName = xmlFile.DisplayName };
                    foreach (var binding in importResult.Bindings)
                        _colImport.LoadedProfile.Bindings.Add(binding);
                    // SC XML bindings use the instance number directly as VJoyDevice
                    var usedInstances = importResult.Bindings
                        .Where(b => b.DeviceType == SCDeviceType.Joystick)
                        .Select(b => b.VJoyDevice)
                        .Distinct();
                    foreach (var inst in usedInstances)
                        _colImport.LoadedProfile.SetSCInstance(inst, (int)inst);
                }
            }
        }

        if (_colImport.LoadedProfile is null) return;

        var vJoyIds = _colImport.LoadedProfile.Bindings
            .Where(b => b.DeviceType == SCDeviceType.Joystick && b.PhysicalDeviceId is null)
            .Select(b => b.VJoyDevice)
            .Distinct()
            .OrderBy(id => _colImport.LoadedProfile.GetSCInstance(id))
            .ToList();

        _colImport.SourceColumns = vJoyIds
            .Select(id => ($"JS{_colImport.LoadedProfile.GetSCInstance(id)}", id))
            .ToList();

        if (_colImport.SourceColumns.Count == 1)
            _colImport.ColumnIndex = 0;
    }

    private void ExecuteImportFromProfile()
    {
        if (_grid.Columns is null || _colImport.HighlightedColumn < 0 || _colImport.HighlightedColumn >= _grid.Columns.Count)
            return;
        if (_colImport.LoadedProfile is null || _colImport.ColumnIndex < 0 || _colImport.ColumnIndex >= _colImport.SourceColumns.Count)
            return;

        var targetCol = _grid.Columns[_colImport.HighlightedColumn];
        var (sourceLabel, sourceVJoyId) = _colImport.SourceColumns[_colImport.ColumnIndex];
        string sourceName = _colImport.LoadedProfile.ProfileName;

        var sourceBindings = _colImport.LoadedProfile.Bindings
            .Where(b => b.DeviceType == SCDeviceType.Joystick && b.PhysicalDeviceId is null && b.VJoyDevice == sourceVJoyId)
            .ToList();

        if (sourceBindings.Count == 0)
        {
            DeselectColumn();
            return;
        }

        int existingCount = _scExportProfile.Bindings.Count(b =>
            b.DeviceType == SCDeviceType.Joystick &&
            b.PhysicalDeviceId is null &&
            _scExportProfile.GetSCInstance(b.VJoyDevice) == targetCol.SCInstance);

        string replaceNote = existingCount > 0
            ? $"\n\nThis will replace {existingCount} existing binding{(existingCount == 1 ? "" : "s")} on JS{targetCol.SCInstance}."
            : string.Empty;

        int btnCount = sourceBindings.Count(b => b.InputType == SCInputType.Button);
        int axisCount = sourceBindings.Count(b => b.InputType == SCInputType.Axis);
        int hatCount = sourceBindings.Count(b => b.InputType == SCInputType.Hat);
        var detailParts = new List<string>();
        if (btnCount > 0)  detailParts.Add($"  {btnCount} button binding{(btnCount == 1 ? "" : "s")}");
        if (axisCount > 0) detailParts.Add($"  {axisCount} axis binding{(axisCount == 1 ? "" : "s")}");
        if (hatCount > 0)  detailParts.Add($"  {hatCount} hat binding{(hatCount == 1 ? "" : "s")}");

        string message = $"Import {sourceBindings.Count} binding{(sourceBindings.Count == 1 ? "" : "s")} from '{sourceName}' ({sourceLabel}) into JS{targetCol.SCInstance}?{replaceNote}";
        bool confirmed = FUIMessageBox.ShowDestructiveConfirm(
            _ctx.OwnerForm,
            message,
            "Import Bindings",
            "IMPORT",
            detailParts.Count > 0 ? detailParts.ToArray() : null);

        if (!confirmed) return;

        // Replace: remove existing bindings on target column
        _scExportProfile.Bindings.RemoveAll(b =>
            b.DeviceType == SCDeviceType.Joystick &&
            b.PhysicalDeviceId is null &&
            _scExportProfile.GetSCInstance(b.VJoyDevice) == targetCol.SCInstance);

        // Copy source bindings, reassigned to target vJoy device
        foreach (var b in sourceBindings)
        {
            _scExportProfile.Bindings.Add(new SCActionBinding
            {
                ActionMap = b.ActionMap,
                ActionName = b.ActionName,
                DeviceType = b.DeviceType,
                VJoyDevice = targetCol.VJoyDeviceId,
                InputName = b.InputName,
                InputType = b.InputType,
                Inverted = b.Inverted,
                ActivationMode = b.ActivationMode,
                Modifiers = new List<string>(b.Modifiers)
            });
        }

        _scExportProfile.Modified = DateTime.UtcNow;
        _scExportProfileService.SaveProfile(_scExportProfile);
        UpdateConflictingBindings();
        DeselectColumn();
    }

}