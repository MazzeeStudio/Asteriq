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

        // Column header click - toggle column highlight
        // Guard: skip if any dropdown is open (they render over the column header area)
        bool anyDropdownOpen = _scInstallationDropdownOpen || _scActionMapFilterDropdownOpen || _scProfileDropdownOpen;
        if (!anyDropdownOpen && _scColumnHeadersBounds.Contains(point.X, point.Y))
        {
            int clickedCol = GetClickedColumnIndex(point.X);
            if (clickedCol >= 0 && (_scGridColumns is null || !_scGridColumns[clickedCol].IsReadOnly))
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
}
