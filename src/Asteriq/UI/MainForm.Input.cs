using Asteriq.Services;
using Asteriq.UI.Controllers;
using SkiaSharp;

namespace Asteriq.UI;

public partial class MainForm
{
    private static MouseEventArgs ScaleMouseEvent(MouseEventArgs e)
    {
        float s = FUIRenderer.CanvasScaleFactor;
        if (s == 1.0f) return e;
        return new MouseEventArgs(e.Button, e.Clicks, (int)(e.X / s), (int)(e.Y / s), e.Delta);
    }

    private void OnCanvasMouseMove(object? sender, MouseEventArgs e)
    {
        var se = ScaleMouseEvent(e);
        // Store mouse position in scaled canvas coordinates for hit-testing in draw pass
        _mousePosition = se.Location;
        // Default cursor each frame; individual handlers override as needed
        Cursor = Cursors.Default;

        // Handle SC Bindings scrollbar dragging (delegated to SC Bindings controller)
        if (_scBindingsController.IsDraggingVScroll || _scBindingsController.IsDraggingHScroll)
        {
            SyncTabContext();
            _scBindingsController.OnMouseMove(se);
            SyncFromTabContext();
            return;
        }

        // Handle background slider dragging (delegated to Settings controller)
        if (_settingsController.IsDraggingSlider)
        {
            SyncTabContext();
            _settingsController.OnMouseMove(se);
            SyncFromTabContext();
            Cursor = Cursors.SizeWE;
            return;
        }

        // Handle device list drag-to-reorder (delegated to Devices controller)
        if (_activeTab == 0 && _devicesController.HasPendingDrag)
        {
            SyncTabContext();
            _devicesController.OnMouseMove(se);
            SyncFromTabContext();
            if (_devicesController.IsDraggingDevice)
                return;
        }

        // Mappings tab hover handling (delegated to controller)
        if (_activeTab == 1)
        {
            // Handle dragging that needs priority dispatch
            if (_mappingsController.IsDraggingCurve || _mappingsController.IsDraggingDeadzone ||
                _mappingsController.IsDraggingDuration)
            {
                SyncTabContext();
                _mappingsController.OnMouseMove(se);
                SyncFromTabContext();
                return;
            }

            SyncTabContext();
            _mappingsController.OnMouseMove(se);
            SyncFromTabContext();
        }

        // Profile dropdown hover detection
        if (_profileDropdownOpen && _profileDropdownBounds.Contains(se.X, se.Y))
        {
            const float itemHeight = 28f;   // must match DrawProfileDropdown
            const float padding = 8f;
            const float separatorGap = 4f;  // itemY += 4 after the separator line

            float relY = se.Y - _profileDropdownBounds.Top - padding;
            if (relY < 0)
            {
                _hoveredProfileIndex = -1;
            }
            else if (relY < _profiles.Count * itemHeight)
            {
                _hoveredProfileIndex = (int)(relY / itemHeight);
            }
            else
            {
                float actionsRelY = relY - _profiles.Count * itemHeight - separatorGap;
                _hoveredProfileIndex = actionsRelY >= 0
                    ? _profiles.Count + (int)(actionsRelY / itemHeight)
                    : -1;
            }
            Cursor = Cursors.Hand;
            return;
        }
        else
        {
            _hoveredProfileIndex = -1;
        }

        // SC Bindings tab hover detection (delegated to SC Bindings controller)
        if (_activeTab == 2)
        {
            SyncTabContext();
            _scBindingsController.OnMouseMove(se);
            SyncFromTabContext();
        }

        // Update cursor based on hit test (for resize feedback) - uses unscaled logical coords
        int hitResult = HitTest(e.Location);
        switch (hitResult)
        {
            case HTLEFT:
            case HTRIGHT:
                Cursor = Cursors.SizeWE;
                break;
            case HTTOP:
            case HTBOTTOM:
                Cursor = Cursors.SizeNS;
                break;
            case HTTOPLEFT:
            case HTBOTTOMRIGHT:
                Cursor = Cursors.SizeNWSE;
                break;
            case HTTOPRIGHT:
            case HTBOTTOMLEFT:
                Cursor = Cursors.SizeNESW;
                break;
            default:
                break;
        }

        // Devices tab hover handling (delegated to controller)
        if (_activeTab == 0)
        {
            SyncTabContext();
            _devicesController.OnMouseMove(se);
            SyncFromTabContext();
        }

        // Settings tab hover handling (delegated to controller)
        if (_activeTab == 3)
        {
            SyncTabContext();
            _settingsController.OnMouseMove(se);
            SyncFromTabContext();
        }

        // (Mapping category tab hover detection moved to MappingsTabController.OnMouseMove)

        // Profile selector cursor (when dropdown is closed)
        if (!_profileDropdownOpen && !_profileSelectorBounds.IsEmpty && _profileSelectorBounds.Contains(se.X, se.Y))
            Cursor = Cursors.Hand;

        // Window controls hover - all coords in scaled canvas space
        float pad = FUIRenderer.SpaceLG;
        float btnSize = FUIRenderer.TouchTargetCompact;
        float btnGap = FUIRenderer.SpaceSM;
        float btnTotalWidth = btnSize * 3 + btnGap * 2;
        float windowControlsX = ClientSize.Width / FUIRenderer.CanvasScaleFactor - pad - btnTotalWidth;
        float titleBarY = FUIRenderer.TitleBarPadding;
        if (se.Y >= titleBarY + 12 && se.Y <= titleBarY + FUIRenderer.TitleBarHeightExpanded)
        {
            float relX = se.X - windowControlsX;

            if (relX >= 0 && relX < btnSize) _hoveredWindowControl = 0;
            else if (relX >= btnSize + btnGap && relX < btnSize * 2 + btnGap) _hoveredWindowControl = 1;
            else if (relX >= (btnSize + btnGap) * 2 && relX < btnSize * 3 + btnGap * 2) _hoveredWindowControl = 2;
            else _hoveredWindowControl = -1;

            // Tab bar cursor (tabs are in the title bar area, right of _tabsStartX)
            if (_hoveredWindowControl < 0 && _tabsStartX > 0 && se.X >= _tabsStartX)
                Cursor = Cursors.Hand;
        }
        else
        {
            _hoveredWindowControl = -1;
        }

        if (_hoveredWindowControl >= 0)
            Cursor = Cursors.Hand;

    }

    private void OnCanvasMouseDown(object? sender, MouseEventArgs e)
    {
        var se = ScaleMouseEvent(e);

        // Handle right-click
        if (e.Button == MouseButtons.Right)
        {
            // Right-click on SC Bindings tab (delegated to SC Bindings controller)
            if (_activeTab == 2)
            {
                SyncTabContext();
                _scBindingsController.OnMouseDown(se);
                SyncFromTabContext();
                return;
            }

            // Right-click on Mappings tab (delegated to controller)
            if (_activeTab == 1)
            {
                SyncTabContext();
                _mappingsController.OnMouseDown(se);
                SyncFromTabContext();
            }
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        // Profile dropdown clicks (must be handled first when dropdown is open)
        if (_profileDropdownOpen)
        {
            if (_profileDropdownBounds.Contains(se.X, se.Y))
            {
                // Click on dropdown item
                if (_hoveredProfileIndex >= 0 && _hoveredProfileIndex < _profiles.Count)
                {
                    // Select existing profile
                    _profileManager.ActivateProfile(_profiles[_hoveredProfileIndex].Id);
                    // Initialize primary devices for migration of old profiles
                    _profileManager.ActiveProfile?.UpdateAllPrimaryDevices();
                    UpdateMappingsPrimaryDeviceMap();
                    _profileDropdownOpen = false;
                    return;
                }
                else if (_hoveredProfileIndex == _profiles.Count)
                {
                    // "New Profile" clicked
                    CreateNewProfilePrompt();
                    _profileDropdownOpen = false;
                    return;
                }
                else if (_hoveredProfileIndex == _profiles.Count + 1)
                {
                    // "Import" clicked
                    ImportProfilePrompt();
                    _profileDropdownOpen = false;
                    return;
                }
                else if (_hoveredProfileIndex == _profiles.Count + 2)
                {
                    // "Export" clicked
                    ExportActiveProfile();
                    _profileDropdownOpen = false;
                    return;
                }
            }
            else
            {
                // Click outside dropdown - close it
                _profileDropdownOpen = false;
                return;
            }
        }

        // Profile selector click (toggle dropdown)
        if (_profileSelectorBounds.Contains(se.X, se.Y))
        {
            _profileDropdownOpen = !_profileDropdownOpen;
            if (_profileDropdownOpen)
            {
                RefreshProfileList();
            }
            return;
        }

        // Window controls
        if (_hoveredWindowControl >= 0)
        {
            switch (_hoveredWindowControl)
            {
                case 0:
                    WindowState = FormWindowState.Minimized;
                    break;
                case 1:
                    // Toggle manual maximize/restore
                    if (_isManuallyMaximized)
                        RestoreWindow();
                    else
                        MaximizeWindow();
                    break;
                case 2:
                    Close();
                    break;
            }
            return;
        }

        // Check for window dragging/resizing
        int hitResult = HitTest(e.Location);
        if (hitResult != HTCLIENT)
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, hitResult, 0);
            return;
        }

        // Devices tab click handling (delegated to controller)
        if (_activeTab == 0)
        {
            SyncTabContext();
            _devicesController.OnMouseDown(se);
            SyncFromTabContext();
        }

        // Mapping category tab clicks (delegated to controller)
        // (handled within MappingsTabController.OnMouseDown)

        // Tab clicks - must match positions calculated in DrawTitleBar exactly.
        // All coords in scaled canvas space (se already divided by userScale).
        float pad = FUIRenderer.SpaceLG;
        float btnTotalWidth = FUIRenderer.TouchTargetCompact * 3 + FUIRenderer.SpaceSM * 2;
        float windowControlsX = ClientSize.Width / FUIRenderer.CanvasScaleFactor - pad - btnTotalWidth;
        float tabWindowGap = FUIRenderer.Space2XL;
        float tabGap = 16f;

        using var tabMeasurePaint = FUIRenderer.CreateTextPaint(FUIColors.TextDim, 16f);
        var visibleTabs = GetVisibleTabIndices();
        float[] tabWidths = new float[_tabNames.Length];    // keyed by semantic index
        float totalTabsWidth = 0;
        for (int vi = 0; vi < visibleTabs.Length; vi++)
        {
            int i = visibleTabs[vi];
            tabWidths[i] = tabMeasurePaint.MeasureText(_tabNames[i]);
            totalTabsWidth += tabWidths[i];
            if (vi < visibleTabs.Length - 1) totalTabsWidth += tabGap;
        }
        float tabStartX = windowControlsX - tabWindowGap - totalTabsWidth;
        float tabY = 16;

        if (se.Y >= tabY + 20 && se.Y <= tabY + 50)
        {
            float tabX = tabStartX;
            for (int vi = 0; vi < visibleTabs.Length; vi++)
            {
                int i = visibleTabs[vi];
                float tabHitWidth = tabWidths[i] + (vi < visibleTabs.Length - 1 ? tabGap / 2 : 0);
                if (se.X >= tabX && se.X < tabX + tabHitWidth)
                {
                    if (_activeTab != i)
                    {
                        if (_activeTab == 1) _mappingsController.OnDeactivated();
                        if (i == 0) _devicesController.OnActivated();
                        if (i == 1) _mappingsController.OnActivated();
                        if (i == 2) _scBindingsController.OnActivated();
                    }
                    _activeTab = i;
                    break;
                }
                tabX += tabWidths[i] + tabGap;
            }
        }

        // Settings tab click handling
        if (_activeTab == 3)
        {
            SyncTabContext();
            _settingsController.OnMouseDown(se);
            SyncFromTabContext();
        }

        // Bindings (SC) tab click handling (delegated to SC Bindings controller)
        if (_activeTab == 2)
        {
            SyncTabContext();
            _scBindingsController.OnMouseDown(se);
            SyncFromTabContext();
            return;
        }

        // Mappings tab click handling (delegated to controller)
        if (_activeTab == 1)
        {
            SyncTabContext();
            _mappingsController.OnMouseDown(se);
            SyncFromTabContext();
        }

        // (SVG control clicks handled by DevicesTabController)
    }

    private void OnCanvasMouseLeave(object? sender, EventArgs e)
    {
        _hoveredWindowControl = -1;
        _hoveredControlId = null;
        _mappingsController.OnMouseLeave();
        _devicesController.OnMouseLeave();
        _scBindingsController.OnMouseLeave();
    }

    private void OnCanvasMouseUp(object? sender, MouseEventArgs e)
    {
        var se = ScaleMouseEvent(e);

        // Device drag-to-reorder release (delegated to Devices controller)
        if (_devicesController.HasPendingDrag || _devicesController.IsDraggingDevice)
        {
            SyncTabContext();
            _devicesController.OnMouseUp(se);
            SyncFromTabContext();
            if (_devicesController.IsDraggingDevice)
                return; // Was still dragging, now released
        }

        // Release SC Bindings scrollbar dragging (delegated to SC Bindings controller)
        if (_scBindingsController.IsDraggingVScroll || _scBindingsController.IsDraggingHScroll)
        {
            SyncTabContext();
            _scBindingsController.OnMouseUp(se);
            SyncFromTabContext();
        }

        // Release mapping drag operations (delegated to Mappings controller)
        if (_mappingsController.IsDraggingCurve || _mappingsController.IsDraggingDeadzone ||
            _mappingsController.IsDraggingDuration)
        {
            SyncTabContext();
            _mappingsController.OnMouseUp(se);
            SyncFromTabContext();
        }

        // Release background slider dragging (delegated to Settings controller)
        if (_settingsController.IsDraggingSlider)
        {
            SyncTabContext();
            _settingsController.OnMouseUp(se);
            SyncFromTabContext();
        }
    }

    private void OnCanvasMouseWheel(object? sender, MouseEventArgs e)
    {
        var se = ScaleMouseEvent(e);

        // Handle scroll on SC Bindings tab (delegated to SC Bindings controller)
        if (_activeTab == 2)
        {
            SyncTabContext();
            _scBindingsController.OnMouseWheel(se);
            SyncFromTabContext();
            return;
        }

        // Handle scroll on MAPPINGS tab (delegated to controller)
        if (_activeTab == 1)
        {
            SyncTabContext();
            _mappingsController.OnMouseWheel(se);
            SyncFromTabContext();
        }
    }

    // Returns semantic tab indices (0=DEVICES,1=MAPPINGS,2=BINDINGS,3=SETTINGS) that are
    // currently visible. MAPPINGS is hidden when vJoy is not available.
    private int[] GetVisibleTabIndices() =>
        _vjoyService.IsInitialized
            ? new[] { 0, 1, 2, 3 }
            : new[] { 0, 2, 3 };

    private string? HitTestSvg(SKPoint screenPoint)
    {
        if (_joystickSvg?.Picture is null || _controlBounds.Count == 0) return null;

        // Transform screen coordinates to SVG coordinates
        float svgX = (screenPoint.X - _svgOffset.X) / _svgScale;
        float svgY = (screenPoint.Y - _svgOffset.Y) / _svgScale;

        // Check each control's bounds
        foreach (var (controlId, bounds) in _controlBounds)
        {
            if (bounds.Contains(svgX, svgY))
            {
                return controlId;
            }
        }

        return null;
    }
}

