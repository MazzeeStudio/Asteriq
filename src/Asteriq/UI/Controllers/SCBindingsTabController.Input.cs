using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class SCBindingsTabController
{
    // State tracking for SDL2-based joystick input detection
    private Dictionary<Guid, float[]>? _scAxisBaseline;    // Baseline axis values
    private Dictionary<Guid, bool[]>? _scButtonBaseline;   // Baseline button values
    private Dictionary<Guid, int[]>? _scHatBaseline;       // Baseline hat values
    private int _scBaselineFrames = 0;                     // Frames since baseline capture

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

        // Header toggle button (JS REF / DEVICE)
        if (!_scHeaderToggleButtonBounds.IsEmpty && _scHeaderToggleButtonBounds.Contains(point))
        {
            _ctx.AppSettings.SCBindingsShowPhysicalHeaders = !_ctx.AppSettings.SCBindingsShowPhysicalHeaders;
            _ctx.MarkDirty();
            return;
        }

        // Column actions panel is only active when a vJoy (non-physical, non-readonly joystick) column is highlighted
        bool showColumnActions = _scHighlightedColumn >= 0
            && _scGridColumns is not null
            && _scHighlightedColumn < _scGridColumns.Count
            && _scGridColumns[_scHighlightedColumn].IsJoystick
            && !_scGridColumns[_scHighlightedColumn].IsPhysical
            && !_scGridColumns[_scHighlightedColumn].IsReadOnly;

        // Column actions panel interactions — all guarded so stale bounds never intercept other panel clicks
        if (showColumnActions)
        {
            // Profile dropdown — close on outside click
            if (_scColImportProfileDropdownOpen)
            {
                if (!_scColImportProfileDropdownBounds.IsEmpty && _scColImportProfileDropdownBounds.Contains(point))
                {
                    var (savedProfiles, xmlFiles) = GetColImportSources();
                    int totalSources = savedProfiles.Count + xmlFiles.Count;
                    float itemH = 28f;
                    int idx = (int)((point.Y - _scColImportProfileDropdownBounds.Top) / itemH);
                    if (idx >= 0 && idx < totalSources && idx != _scColImportProfileIndex)
                    {
                        _scColImportProfileIndex = idx;
                        LoadColImportSourceColumns();
                    }
                    _scColImportProfileDropdownOpen = false;
                    _ctx.MarkDirty();
                    return;
                }
                else
                {
                    _scColImportProfileDropdownOpen = false;
                    _ctx.MarkDirty();
                    // Allow click to fall through
                }
            }

            // Column dropdown — close on outside click
            if (_scColImportColumnDropdownOpen)
            {
                if (!_scColImportColumnDropdownBounds.IsEmpty && _scColImportColumnDropdownBounds.Contains(point))
                {
                    float itemH = 28f;
                    int idx = (int)((point.Y - _scColImportColumnDropdownBounds.Top) / itemH);
                    if (idx >= 0 && idx < _scColImportSourceColumns.Count)
                        _scColImportColumnIndex = idx;
                    _scColImportColumnDropdownOpen = false;
                    _ctx.MarkDirty();
                    return;
                }
                else
                {
                    _scColImportColumnDropdownOpen = false;
                    _ctx.MarkDirty();
                    // Allow click to fall through
                }
            }

            if (!_scDeselectButtonBounds.IsEmpty && _scDeselectButtonBounds.Contains(point))
            {
                DeselectColumn();
                return;
            }

            if (!_scColImportButtonBounds.IsEmpty && _scColImportButtonBounds.Contains(point))
            {
                ExecuteImportFromProfile();
                return;
            }

            if (!_scColImportProfileSelectorBounds.IsEmpty && _scColImportProfileSelectorBounds.Contains(point))
            {
                var (savedProfiles, xmlFiles) = GetColImportSources();
                if (savedProfiles.Count + xmlFiles.Count > 0)
                {
                    _scColImportProfileDropdownOpen = !_scColImportProfileDropdownOpen;
                    _scColImportColumnDropdownOpen = false;
                    _ctx.MarkDirty();
                }
                return;
            }

            if (!_scColImportColumnSelectorBounds.IsEmpty && _scColImportColumnSelectorBounds.Contains(point))
            {
                if (_scColImportSourceColumns.Count > 0)
                {
                    _scColImportColumnDropdownOpen = !_scColImportColumnDropdownOpen;
                    _scColImportProfileDropdownOpen = false;
                    _ctx.MarkDirty();
                }
                return;
            }
        }

        // Column header click - toggle column highlight
        // Only vJoy (non-physical joystick) columns are selectable; mouse/keyboard columns are display-only.
        // Guard: skip if any dropdown is open (they render over the column header area)
        bool anyDropdownOpen = _scInstallationDropdownOpen || _scActionMapFilterDropdownOpen || _scProfileDropdownOpen;
        if (!anyDropdownOpen && _scColumnHeadersBounds.Contains(point.X, point.Y))
        {
            int clickedCol = GetClickedColumnIndex(point.X);
            if (clickedCol >= 0
                && _scGridColumns is not null
                && _scGridColumns[clickedCol].IsJoystick
                && !_scGridColumns[clickedCol].IsPhysical
                && !_scGridColumns[clickedCol].IsReadOnly)
            {
                if (_scHighlightedColumn == clickedCol)
                {
                    DeselectColumn();
                }
                else
                {
                    _scHighlightedColumn = clickedCol;
                    // Reset import state for the newly selected column
                    _scColImportProfileIndex = -1;
                    _scColImportColumnIndex = -1;
                    _scColImportLoadedProfile = null;
                    _scColImportSourceColumns.Clear();
                    _scColImportProfileDropdownOpen = false;
                    _scColImportColumnDropdownOpen = false;
                    _ctx.MarkDirty();
                }
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
                // Click outside list - close dropdown
                _scProfileDropdownOpen = false;
                // If the click was on the toggle button itself, stop here so it doesn't re-open below
                if (_scProfileDropdownBounds.Contains(point))
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
        if (_scDeviceOrderOpenRow >= 0)
        {
            if (!_scDeviceOrderDropdownBounds.IsEmpty && _scDeviceOrderDropdownBounds.Contains(point))
            {
                // Click inside open dropdown: select a vJoy slot for this row's SC instance
                float itemH = 28f;
                int idx = (int)((point.Y - _scDeviceOrderDropdownBounds.Top) / itemH);
                var existingSlots = _ctx.VJoyDevices.Where(v => v.Exists).OrderBy(v => v.Id).ToList();
                if (idx >= 0 && idx < existingSlots.Count)
                    AssignDeviceOrderSlot(_scDeviceOrderOpenRow + 1, existingSlots[idx].Id);
                _scDeviceOrderOpenRow = -1;
                _scDeviceOrderHoveredIndex = -1;
                _ctx.MarkDirty();
                return;
            }
            else
            {
                // Click outside dropdown — close it
                _scDeviceOrderOpenRow = -1;
                _scDeviceOrderHoveredIndex = -1;
                _ctx.MarkDirty();
                // Fall through so other panel clicks still work
            }
        }

        // Device Order panel — auto-detect button
        if (!_scDeviceOrderAutoDetectBounds.IsEmpty && _scDeviceOrderAutoDetectBounds.Contains(point)
            && _directInputService is not null)
        {
            RunDeviceOrderAutoDetect();
            return;
        }

        // Device Order panel — row selector clicks
        for (int row = 0; row < _scDeviceOrderSelectorBounds.Length; row++)
        {
            if (!_scDeviceOrderSelectorBounds[row].IsEmpty && _scDeviceOrderSelectorBounds[row].Contains(point))
            {
                int vjoyCount = _ctx.VJoyDevices.Count(v => v.Exists);
                if (vjoyCount > 1) // no point opening a single-item dropdown
                {
                    _scDeviceOrderOpenRow = _scDeviceOrderOpenRow == row ? -1 : row;
                    _scDeviceOrderHoveredIndex = -1;
                    _ctx.MarkDirty();
                }
                return;
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

        // Profile edit icon click (inside dropdown box)
        if (_scProfileEditBounds != SKRect.Empty && _scProfileEditBounds.Contains(point))
        {
            EditSCProfileName();
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

        // Export button
        if (_scExportButtonBounds.Contains(point))
        {
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

        // Assign input button — activates the listener on the selected cell (same as double-click)
        if (_scAssignInputButtonBounds.Contains(point) && _scSelectedActionIndex >= 0)
        {
            if (_scSelectedCell.actionIndex >= 0 && _scSelectedCell.colIndex >= 0 &&
                _scGridColumns is not null && _scSelectedCell.colIndex < _scGridColumns.Count)
            {
                var col = _scGridColumns[_scSelectedCell.colIndex];
                if (!col.IsReadOnly)
                {
                    _scIsListeningForInput = true;
                    _scListeningStartTime = DateTime.Now;
                    _scListeningColumn = col;

                    if (col.IsKeyboard)
                        ClearStaleKeyPresses();
                    if (col.IsMouse)
                        ClearStaleMousePresses();

                    System.Diagnostics.Debug.WriteLine($"[SCBindings] ASSIGN button: started listening on cell ({_scSelectedCell.actionIndex}, {_scSelectedCell.colIndex}) - {col.Header}");
                }
            }
            return;
        }

        // Clear binding button
        if (_scClearBindingButtonBounds.Contains(point) && _scSelectedActionIndex >= 0 && _scFilteredActions is not null)
        {
            var selectedAction = _scFilteredActions[_scSelectedActionIndex];

            // If a cell is selected, clear the binding for that specific column
            if (_scSelectedCell.colIndex >= 0 && _scGridColumns is not null && _scSelectedCell.colIndex < _scGridColumns.Count)
            {
                var selCol = _scGridColumns[_scSelectedCell.colIndex];
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

    private void HandleCellClick(int actionIndex, int colIndex)
    {
        if (_scGridColumns is null || colIndex < 0 || colIndex >= _scGridColumns.Count)
            return;

        var col = _scGridColumns[colIndex];

        // Read-only columns display bindings but do not accept new assignments
        if (col.IsReadOnly)
            return;

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

            // Clear stale presses before detecting
            if (col.IsKeyboard)
                ClearStaleKeyPresses();
            if (col.IsMouse)
                ClearStaleMousePresses();

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
                System.Diagnostics.Debug.WriteLine($"[SCBindings] Cleared physical JS binding for {action.ActionName} on {col.Header}");
            }
        }
        else if (col.IsJoystick)
        {
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
                if (clickedCol >= 0 && _scGridColumns is not null && clickedCol < _scGridColumns.Count)
                {
                    HandleCellRightClick(i, clickedCol);
                }
                return;
            }

            currentY += rowHeight + rowGap;
        }
    }

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

    private string? DetectMouseInput()
    {
        if (IsKeyPressed(0x01)) return "mouse1"; // VK_LBUTTON
        if (IsKeyPressed(0x02)) return "mouse2"; // VK_RBUTTON
        if (IsKeyPressed(0x04)) return "mouse3"; // VK_MBUTTON
        if (IsKeyPressed(0x05)) return "mouse4"; // VK_XBUTTON1
        if (IsKeyPressed(0x06)) return "mouse5"; // VK_XBUTTON2

        // Mouse wheel detection would need WM_MOUSEWHEEL messages which we don't have here
        // For now, mouse wheel bindings need to be entered differently

        return null;
    }

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

                // For physical columns, only baseline the matching device
                if (col.IsPhysical && device.HidDevicePath != col.PhysicalDevice!.HidDevicePath) continue;

                var state = _ctx.InputService.GetDeviceState(idx);
                if (state is not null)
                {
                    _scAxisBaseline[device.InstanceGuid] = (float[])state.Axes.Clone();
                    _scButtonBaseline[device.InstanceGuid] = (bool[])state.Buttons.Clone();
                    _scHatBaseline[device.InstanceGuid] = (int[])state.Hats.Clone();
                }
            }

            System.Diagnostics.Debug.WriteLine($"[SCBindings] Initialized SDL2 input detection for {_scAxisBaseline.Count} devices (physical={col.IsPhysical})");
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

            // For physical columns, only listen to the matching device
            if (col.IsPhysical && device.HidDevicePath != col.PhysicalDevice!.HidDevicePath) continue;

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

            // Check for axis movement
            for (int i = 0; i < state.Axes.Length; i++)
            {
                float baselineValue = baselineAxes is not null && i < baselineAxes.Length ? baselineAxes[i] : 0f;
                float currValue = state.Axes[i];
                float deflection = Math.Abs(currValue - baselineValue);

                if (deflection > AxisThreshold)
                {
                    // For physical columns, use HID axis type info directly
                    // For vJoy columns, look up the vJoy output axis from the mapping profile
                    string axisName = col.IsPhysical
                        ? GetSCAxisNameFromDevice(i, device)
                        : GetVJoyAxisNameFromMapping(device, i, col);
                    System.Diagnostics.Debug.WriteLine($"[SCBindings] SDL2 detected axis {i} -> {axisName} on {device.Name}, deflection: {deflection:F2}");
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

    private void ResetJoystickDetectionState()
    {
        _scAxisBaseline = null;
        _scButtonBaseline = null;
        _scHatBaseline = null;
        _scBaselineFrames = 0;
    }

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

    private void DeselectColumn()
    {
        _scHighlightedColumn = -1;
        _scColImportProfileIndex = -1;
        _scColImportColumnIndex = -1;
        _scColImportLoadedProfile = null;
        _scColImportSourceColumns.Clear();
        _scColImportProfileDropdownOpen = false;
        _scColImportColumnDropdownOpen = false;
        _ctx.MarkDirty();
    }

    /// <summary>
    /// Called when the user selects a source profile; loads that profile and builds the
    /// list of its vJoy columns available to import from.
    /// Supports both saved Asteriq profiles and SC XML mapping files.
    /// </summary>
    private void LoadColImportSourceColumns()
    {
        _scColImportLoadedProfile = null;
        _scColImportSourceColumns.Clear();
        _scColImportColumnIndex = -1;

        if (_scColImportProfileIndex < 0) return;

        var (savedProfiles, xmlFiles) = GetColImportSources();
        int savedCount = savedProfiles.Count;

        if (_scColImportProfileIndex < savedCount)
        {
            // Load saved Asteriq JSON profile
            _scColImportLoadedProfile = _scExportProfileService.LoadProfile(savedProfiles[_scColImportProfileIndex].ProfileName);
        }
        else
        {
            // Load from SC XML mapping file
            int xmlIdx = _scColImportProfileIndex - savedCount;
            if (xmlIdx < xmlFiles.Count)
            {
                var xmlFile = xmlFiles[xmlIdx];
                var importResult = _scExportService.ImportFromFile(xmlFile.FilePath);
                if (importResult.Success)
                {
                    _scColImportLoadedProfile = new SCExportProfile { ProfileName = xmlFile.DisplayName };
                    foreach (var binding in importResult.Bindings)
                        _scColImportLoadedProfile.Bindings.Add(binding);
                    // SC XML bindings use the instance number directly as VJoyDevice
                    var usedInstances = importResult.Bindings
                        .Where(b => b.DeviceType == SCDeviceType.Joystick)
                        .Select(b => b.VJoyDevice)
                        .Distinct();
                    foreach (var inst in usedInstances)
                        _scColImportLoadedProfile.SetSCInstance(inst, (int)inst);
                }
            }
        }

        if (_scColImportLoadedProfile is null) return;

        var vJoyIds = _scColImportLoadedProfile.Bindings
            .Where(b => b.DeviceType == SCDeviceType.Joystick && b.PhysicalDeviceId is null)
            .Select(b => b.VJoyDevice)
            .Distinct()
            .OrderBy(id => _scColImportLoadedProfile.GetSCInstance(id))
            .ToList();

        _scColImportSourceColumns = vJoyIds
            .Select(id => ($"JS{_scColImportLoadedProfile.GetSCInstance(id)}", id))
            .ToList();

        if (_scColImportSourceColumns.Count == 1)
            _scColImportColumnIndex = 0;
    }

    private void ExecuteImportFromProfile()
    {
        if (_scGridColumns is null || _scHighlightedColumn < 0 || _scHighlightedColumn >= _scGridColumns.Count)
            return;
        if (_scColImportLoadedProfile is null || _scColImportColumnIndex < 0 || _scColImportColumnIndex >= _scColImportSourceColumns.Count)
            return;

        var targetCol = _scGridColumns[_scHighlightedColumn];
        var (sourceLabel, sourceVJoyId) = _scColImportSourceColumns[_scColImportColumnIndex];
        string sourceName = _scColImportLoadedProfile.ProfileName;

        var sourceBindings = _scColImportLoadedProfile.Bindings
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

    /// <summary>
    /// Assigns a vJoy slot to a specific SC joystick instance, swapping the displaced slot
    /// to take the previous instance of the selected slot.
    /// </summary>
    private void AssignDeviceOrderSlot(int scInst, uint newVJoySlotId)
    {
        var existingSlots = _ctx.VJoyDevices.Where(v => v.Exists).ToList();
        if (existingSlots.Count == 0) return;

        // Find the vJoy slot that currently owns scInst
        uint? prevSlotId = null;
        foreach (var slot in existingSlots)
        {
            if (_scExportProfile.GetSCInstance(slot.Id) == scInst)
            {
                prevSlotId = slot.Id;
                break;
            }
        }

        if (prevSlotId == newVJoySlotId) return; // No change

        // The displaced slot gets the SC instance that newVJoySlot currently has
        int newSlotCurrentInst = _scExportProfile.GetSCInstance(newVJoySlotId);

        _scExportProfile.SetSCInstance(newVJoySlotId, scInst);
        if (prevSlotId.HasValue)
            _scExportProfile.SetSCInstance(prevSlotId.Value, newSlotCurrentInst);

        SaveAndRefreshAfterDeviceOrderChange();
    }

    /// <summary>
    /// Runs DirectInput-based auto-detection and updates VJoyToSCInstance in the active profile.
    /// </summary>
    private void RunDeviceOrderAutoDetect()
    {
        if (_directInputService is null) return;

        try
        {
            var diDevices = _directInputService.EnumerateDevices();
            var vjoySlots = _ctx.VJoyDevices.Where(v => v.Exists);
            var mapping = VJoyDirectInputOrderService.DetectVJoyDiOrder(vjoySlots, diDevices);

            foreach (var (vjoyId, scInstance) in mapping)
                _scExportProfile.SetSCInstance(vjoyId, scInstance);

            SaveAndRefreshAfterDeviceOrderChange();
            SetStatus("Device order auto-detected");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            SetStatus("Auto-detect failed: DirectInput unavailable", SCStatusKind.Error);
        }
    }

    private void SaveAndRefreshAfterDeviceOrderChange()
    {
        if (!string.IsNullOrEmpty(_scExportProfile.ProfileName))
            _scExportProfileService.SaveProfile(_scExportProfile);

        UpdateConflictingBindings();
        _ctx.MarkDirty();
    }
}
