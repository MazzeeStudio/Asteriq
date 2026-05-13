using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class SCBindingsTabController
{
    private void HandleCellClick(int actionIndex, int colIndex)
    {
        if (_grid.Columns is null || colIndex < 0 || colIndex >= _grid.Columns.Count)
            return;
        if (_scFilteredActions is null || actionIndex < 0 || actionIndex >= _scFilteredActions.Count)
            return;

        var col = _grid.Columns[colIndex];

        // If already listening, cancel
        if (_scListening.IsListening)
        {
            _scListening.IsListening = false;
            _cell.ListeningColumn = null;
        }

        // Read-only columns (no backing device): allow selection but block listening
        if (col.IsReadOnly)
        {
            if (_colImport.HighlightedColumn >= 0)
                DeselectColumn();
            _cell.SelectedCell = (actionIndex, colIndex);
            _cell.LastCellClickTicks = Environment.TickCount64;
            _cpPanel.IsExpanded = false; // Auto-expand Cell Details
            _bdPanel.IsExpanded = false; // Cell-click intent is "configure" â€” collapse Definition panel
            UpdateConflictLinks();
            System.Diagnostics.Debug.WriteLine($"[SCBindings] Selected read-only cell ({actionIndex}, {colIndex}) - {col.Header}");
            return;
        }

        // Check for double-click on the same cell (within 400ms)
        bool isDoubleClick = _cell.SelectedCell == (actionIndex, colIndex) &&
                            Environment.TickCount64 - _cell.LastCellClickTicks < SystemInformation.DoubleClickTime;

        // Shared cells: select normally but never enter listen mode â€” use CLEAR/right-click to unshare
        if (col.IsJoystick && !col.IsPhysical)
        {
            var action = _scFilteredActions[actionIndex];
            string sharedKey = $"{action.Key}|{col.VJoyDeviceId}";
            if (_conflicts.SharedCells.ContainsKey(sharedKey))
            {
                if (_colImport.HighlightedColumn >= 0)
                    DeselectColumn();
                _cell.SelectedCell = (actionIndex, colIndex);
                _cell.LastCellClickTicks = Environment.TickCount64;
                _cpPanel.IsExpanded = false; // Auto-expand Cell Details
            _bdPanel.IsExpanded = false; // Cell-click intent is "configure" â€” collapse Definition panel
                UpdateConflictLinks();
                return;
            }
        }

        // Clicking a cell always deselects any highlighted column
        if (_colImport.HighlightedColumn >= 0)
            DeselectColumn();

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
            // Single click â€” check if this row has a cross-column duplicate to resolve
            if (col.IsJoystick && !col.IsPhysical && _scFilteredActions is not null)
            {
                var action = _scFilteredActions[actionIndex];
                if (TryShowDuplicateResolveDialog(action))
                    return;
            }

            // Single click: just select the cell
            _cell.SelectedCell = (actionIndex, colIndex);
            _cell.LastCellClickTicks = Environment.TickCount64;
            _conflicts.HighlightActionIndex = -1;
            _cpPanel.IsExpanded = false; // Auto-expand Cell Details
            _bdPanel.IsExpanded = false; // Cell-click intent is "configure" â€” collapse Definition panel
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
                SetStatus($"Shared: JS{dupInst} â†’ JS{baseInst}");
                _ctx.MarkDirty();
                return true;
        }

        // Cancel: no change was made â€” let the caller proceed with normal cell selection
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
                b.PhysicalDeviceId == col.PhysicalDeviceKey);
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

}