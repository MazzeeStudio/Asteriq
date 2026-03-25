using System.Text.RegularExpressions;
using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public class DevicesTabController : ITabController
{
    private readonly TabContext _ctx;

    // Cached regex for parsing vJoy slot numbers from device names
    private static readonly Regex s_vjoySlotNumberRegex = new(@"\d+", RegexOptions.Compiled);

    // Device list hover/selection (local to this tab)
    private int _hoveredDevice = -1;

    // D1 filter state (persisted across sessions)
    private bool _showHiddenDevices;
    private SKRect _showHiddenCheckboxBounds;

    // Layout state cached from Draw — reused by mouse handlers to avoid duplicating layout math
    private float _cachedContentTop;
    private float _cachedLeftPanelWidth;
    private float _cachedSideTabPad;

    // State groups
    private readonly DeviceCategoryState _devCat = new();
    private readonly DeviceDragState _drag = new();
    private readonly DeviceActionsState _actions = new();
    private readonly ForwardingButtonsState _forwarding = new();
    private readonly SilhouettePickerState _silhouette = new();
    private readonly SvgClickState _svgClick = new();

    /// <summary>
    /// The active device category (0 = physical, 1 = virtual/vJoy).
    /// </summary>
    public int DeviceCategory => _devCat.Active;

    /// <summary>
    /// True if a device drag-to-reorder is in progress (checked by MainForm for mouse dispatch).
    /// </summary>
    public bool IsDraggingDevice => _drag.IsDragging;

    /// <summary>
    /// True if a drag has been initiated (mouse down on device item, but may not have exceeded threshold yet).
    /// </summary>
    public bool HasPendingDrag => _drag.DeviceIndex >= 0;

    public DevicesTabController(TabContext ctx)
    {
        _ctx = ctx;
        _showHiddenDevices = ctx.AppSettings.DevicesIncludeHidden;
    }

    public void Draw(SKCanvas canvas, SKRect bounds, float padLeft, float contentTop, float contentBottom)
    {
        // Ensure selected device is not hidden (can happen on first load after prior session hid a device)
        if (_devCat.Active == 0 && _ctx.SelectedDevice >= 0 && _ctx.SelectedDevice < _ctx.Devices.Count
            && !_showHiddenDevices
            && _ctx.AppSettings.IsDeviceHidden(_ctx.Devices[_ctx.SelectedDevice].InstanceGuid.ToString()))
        {
            EnsureVisibleDeviceSelected();
        }

        // Calculate responsive panel widths (same logic as MainForm.DrawStructureLayer)
        float sideTabPad = FUIRenderer.SpaceSM;
        float pad = FUIRenderer.SpaceXL;
        float contentWidth = bounds.Width - sideTabPad - pad;
        var layout = FUIRenderer.CalculateLayout(contentWidth, minLeftPanel: 240f, minRightPanel: 340f, maxSidePanel: 500f);

        float leftPanelWidth = layout.LeftPanelWidth;

        // Cache for mouse handlers — avoids duplicating layout math
        _cachedContentTop = contentTop;
        _cachedLeftPanelWidth = leftPanelWidth;
        _cachedSideTabPad = sideTabPad;
        float gap = layout.Gutter;
        float centerStart = sideTabPad + leftPanelWidth + gap;
        float centerEnd = layout.ShowRightPanel
            ? bounds.Right - pad - layout.RightPanelWidth - gap
            : bounds.Right - pad;

        // Left panel: Device List
        var deviceListBounds = new SKRect(sideTabPad, contentTop, sideTabPad + leftPanelWidth, contentBottom);
        DrawDeviceListPanel(canvas, deviceListBounds);

        // Center panel: Device Details
        var detailsBounds = new SKRect(centerStart, contentTop, centerEnd, contentBottom);
        DrawDeviceDetailsPanel(canvas, detailsBounds);

        // Right panel: S1 pinned to bottom at natural height; D3 fills above
        if (layout.ShowRightPanel)
        {
            float rightPanelX = bounds.Right - pad - layout.RightPanelWidth;
            float panelGap = FUIRenderer.SpaceSM;
            float rightEdge = bounds.Right - pad;

            // S1 natural height: title + 3 items + button + padding (24px items, 12f font)
            const float s1FrameInset = 5f;
            const float s1TitleH = 32f;
            const float s1ItemH = 24f;
            const float s1ItemCount = 3f;
            float s1Height = s1FrameInset * 2
                + s1TitleH
                + FUIRenderer.PanelPadding
                + s1ItemCount * s1ItemH
                + (s1ItemCount - 1) * FUIRenderer.SpaceSM
                + FUIRenderer.SpaceLG
                + 36f
                + FUIRenderer.SpaceLG
                + s1FrameInset;

            var statusBounds = new SKRect(rightPanelX, contentBottom - s1Height, rightEdge, contentBottom);
            var deviceActionsBounds = new SKRect(rightPanelX, contentTop, rightEdge, statusBounds.Top - panelGap);
            DrawDeviceActionsPanel(canvas, deviceActionsBounds);
            DrawStatusPanel(canvas, statusBounds);
        }
    }

    public void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        if (HandleCategoryTabClick()) return;
        if (HandleDeviceActionClick(e)) return;
        if (HandleHiddenCheckboxClick(e)) return;
        if (HandleSilhouetteClick(e)) return;
        if (HandleVJoyActionClick(e)) return;
        if (HandleForwardingClick(e)) return;
        if (HandleDeviceListClick(e)) return;
        HandleSvgClick(e);
    }

    private bool HandleCategoryTabClick()
    {
        if (_devCat.Hovered < 0) return false;
        _devCat.Active = _devCat.Hovered;
        _ctx.SelectedDevice = -1;
        _ctx.CurrentInputState = null;
        _ctx.SelectFirstDeviceInCategory?.Invoke();
        return true;
    }

    private bool HandleDeviceActionClick(MouseEventArgs e)
    {
        if (_actions.Map1to1Bounds.HitTest(e.X, e.Y))
        { _ctx.CreateOneToOneMappings?.Invoke(); return true; }

        if (_actions.ClearMappingsBounds.HitTest(e.X, e.Y))
        { _ctx.ClearDeviceMappings?.Invoke(); return true; }

        if (_actions.RemoveDeviceBounds.HitTest(e.X, e.Y))
        { _ctx.RemoveDisconnectedDevice?.Invoke(); return true; }

        if (_actions.HideFromViewBounds.HitTest(e.X, e.Y))
        { ToggleHideFromView(); return true; }

        return false;
    }

    private bool HandleHiddenCheckboxClick(MouseEventArgs e)
    {
        if (_showHiddenCheckboxBounds.IsEmpty || !_showHiddenCheckboxBounds.Contains(e.X, e.Y))
            return false;
        _showHiddenDevices = !_showHiddenDevices;
        _ctx.AppSettings.DevicesIncludeHidden = _showHiddenDevices;
        EnsureVisibleDeviceSelected();
        _ctx.MarkDirty();
        return true;
    }

    private bool HandleSilhouetteClick(MouseEventArgs e)
    {
        var (_, hasPrev, hasNext) = GetSilhouettePickerState();
        if (_silhouette.PrevBounds.HitTest(e.X, e.Y) && hasPrev) { StepSilhouette(-1); return true; }
        if (_silhouette.NextBounds.HitTest(e.X, e.Y) && hasNext) { StepSilhouette(+1); return true; }
        return false;
    }

    private bool HandleVJoyActionClick(MouseEventArgs e)
    {
        if (_silhouette.SyncVJoyBounds.HitTest(e.X, e.Y))
        {
            uint vjoyId = GetSelectedVJoyId();
            if (vjoyId > 0) SyncVJoyToPhysical(vjoyId);
            return true;
        }

        if (_silhouette.RemoveVJoyBounds.HitTest(e.X, e.Y))
        {
            uint vjoyId = GetSelectedVJoyId();
            if (vjoyId > 0) RemoveVJoyDevice(vjoyId);
            return true;
        }

        return false;
    }

    private bool HandleForwardingClick(MouseEventArgs e)
    {
        if (_forwarding.StartBounds.HitTest(e.X, e.Y))
        { StartForwarding(); return true; }

        if (_forwarding.StopBounds.HitTest(e.X, e.Y))
        { StopForwarding(); return true; }

        return false;
    }

    private bool HandleDeviceListClick(MouseEventArgs e)
    {
        if (!IsValidDeviceIndex(_hoveredDevice)) return false;

        SelectDevice(_hoveredDevice);

        // Drag/drop reordering disabled — reassigning vJoy IDs via drag
        // can cause capability mismatches (e.g., 60-button device → 32-button vJoy).
        // Use MAP 1:1 TO VJOY + Match Physical for safe device assignment.

        return true;
    }

    private void HandleSvgClick(MouseEventArgs e)
    {
        if (_ctx.HoveredControlId is { } controlId)
        {
            bool isDoubleClick = _svgClick.LastControlId == controlId &&
                                 Environment.TickCount64 - _svgClick.LastClickTicks < SystemInformation.DoubleClickTime;

            if (isDoubleClick)
            {
                _ctx.OpenMappingDialogForControl?.Invoke(controlId);
                _svgClick.LastControlId = null;
            }
            else
            {
                _ctx.SelectedControlId = controlId;
                _ctx.LeadLineProgress = 0f;
                _svgClick.LastControlId = controlId;
                _svgClick.LastClickTicks = Environment.TickCount64;
            }
        }
        else if (_ctx.SilhouetteBounds.Contains(e.X, e.Y))
        {
            _ctx.SelectedControlId = null;
            _svgClick.LastControlId = null;
        }
    }

    private bool IsValidDeviceIndex(int index) => index >= 0 && index < _ctx.Devices.Count;

    private void SelectDevice(int index)
    {
        _ctx.SelectedDevice = index;
        _ctx.CurrentInputState = null;
        _ctx.LoadDeviceMapForDevice(_ctx.Devices[index]);
        _ctx.ActiveInputTracker.Clear();
    }

    public void OnMouseMove(MouseEventArgs e)
    {
        if (UpdateDragState(e)) return;
        _ctx.OwnerForm.Cursor = Cursors.Default;
        UpdateCategoryTabHover(e);
        UpdateDeviceListHover(e);
        UpdateActionButtonHover(e);
        UpdateForwardingHover(e);
        UpdateSilhouettePickerHover(e);
        UpdateSvgHover(e);
    }

    private bool UpdateDragState(MouseEventArgs e)
    {
        if (_devCat.Active != 0 || _drag.DeviceIndex < 0) return false;

        var currentPoint = new SKPoint(e.X, e.Y);
        float dragDistance = SKPoint.Distance(currentPoint, _drag.StartPoint);

        if (!_drag.IsDragging && dragDistance > 5)
        {
            _drag.IsDragging = true;
            _ctx.OwnerForm.Cursor = Cursors.SizeAll;
        }

        if (!_drag.IsDragging) return false;

        _drag.CurrentPoint = currentPoint;

        float dragItemHeight = 60f;
        float dragItemGap = FUIRenderer.ItemSpacing;
        float dragContentTop = _cachedContentTop + 32 + FUIRenderer.PanelPadding;

        var physicalDevices = _ctx.Devices.Where(d => !d.IsVirtual).ToList();
        float relativeY = e.Y - dragContentTop;
        int targetIndex = Math.Clamp((int)(relativeY / (dragItemHeight + dragItemGap)), 0, physicalDevices.Count);

        _drag.DropTargetIndex = targetIndex;
        _ctx.MarkDirty();
        return true;
    }

    private void UpdateCategoryTabHover(MouseEventArgs e)
    {
        _devCat.Hovered = -1;
        if (_devCat.D1Bounds.Contains(e.X, e.Y))
        {
            _devCat.Hovered = 0;
            _ctx.OwnerForm.Cursor = Cursors.Hand;
        }
        else if (_devCat.D2Bounds.Contains(e.X, e.Y))
        {
            _devCat.Hovered = 1;
            _ctx.OwnerForm.Cursor = Cursors.Hand;
        }
    }

    private void UpdateDeviceListHover(MouseEventArgs e)
    {
        float sideTabWidth = FUIRenderer.SideTabWidth;

        _hoveredDevice = -1;
        if (e.X >= _cachedSideTabPad + sideTabWidth && e.X <= _cachedSideTabPad + _cachedLeftPanelWidth)
        {
            float itemY = _cachedContentTop + 32 + FUIRenderer.PanelPadding;
            float itemHeight = 60f;
            float itemGap = FUIRenderer.ItemSpacing;

            var filteredDevices = _devCat.Active == 0
                ? _ctx.Devices.Where(d => !d.IsVirtual &&
                    (_showHiddenDevices || !_ctx.AppSettings.IsDeviceHidden(d.InstanceGuid.ToString()))).ToList()
                : _ctx.Devices.Where(d => d.IsVirtual).ToList();

            int filteredIndex = (int)((e.Y - itemY) / (itemHeight + itemGap));
            if (filteredIndex >= 0 && filteredIndex < filteredDevices.Count)
                _hoveredDevice = _ctx.Devices.IndexOf(filteredDevices[filteredIndex]);
        }

        if (_hoveredDevice >= 0)
            _ctx.OwnerForm.Cursor = Cursors.Hand;
    }

    private void UpdateActionButtonHover(MouseEventArgs e)
    {
        _actions.Map1to1Hovered = _actions.Map1to1Bounds.HitTest(e.X, e.Y);
        _actions.ClearMappingsHovered = _actions.ClearMappingsBounds.HitTest(e.X, e.Y);
        _actions.RemoveDeviceHovered = _actions.RemoveDeviceBounds.HitTest(e.X, e.Y);
        _actions.HideFromViewHovered = _actions.HideFromViewBounds.HitTest(e.X, e.Y);
        _silhouette.RemoveVJoyHovered = _silhouette.RemoveVJoyBounds.HitTest(e.X, e.Y);
        _silhouette.SyncVJoyHovered = _silhouette.SyncVJoyBounds.HitTest(e.X, e.Y);

        if (_actions.Map1to1Hovered || _actions.ClearMappingsHovered || _actions.RemoveDeviceHovered ||
            _actions.HideFromViewHovered || _silhouette.RemoveVJoyHovered || _silhouette.SyncVJoyHovered)
            _ctx.OwnerForm.Cursor = Cursors.Hand;

        if (_showHiddenCheckboxBounds.HitTest(e.X, e.Y))
            _ctx.OwnerForm.Cursor = Cursors.Hand;
    }

    private void UpdateForwardingHover(MouseEventArgs e)
    {
        _forwarding.StartHovered = _forwarding.StartBounds.HitTest(e.X, e.Y);
        _forwarding.StopHovered = _forwarding.StopBounds.HitTest(e.X, e.Y);

        if (_forwarding.StartHovered || _forwarding.StopHovered)
            _ctx.OwnerForm.Cursor = Cursors.Hand;
    }

    private void UpdateSilhouettePickerHover(MouseEventArgs e)
    {
        _silhouette.PrevHovered = false;
        _silhouette.NextHovered = false;
        var (_, hasPrev, hasNext) = GetSilhouettePickerState();

        if (_silhouette.PrevBounds.HitTest(e.X, e.Y) && hasPrev)
        {
            _silhouette.PrevHovered = true;
            _ctx.OwnerForm.Cursor = Cursors.Hand;
        }
        else if (_silhouette.NextBounds.HitTest(e.X, e.Y) && hasNext)
        {
            _silhouette.NextHovered = true;
            _ctx.OwnerForm.Cursor = Cursors.Hand;
        }
    }

    private void UpdateSvgHover(MouseEventArgs e)
    {
        if (_ctx.SilhouetteBounds.Contains(e.X, e.Y) && _ctx.JoystickSvg is not null)
        {
            var hitControlId = _ctx.HitTestSvg(new SKPoint(e.X, e.Y));
            if (hitControlId != _ctx.HoveredControlId)
            {
                _ctx.HoveredControlId = hitControlId;
                _ctx.OwnerForm.Cursor = hitControlId is not null ? Cursors.Hand : Cursors.Default;
            }
        }
        else if (_ctx.HoveredControlId is not null)
        {
            _ctx.HoveredControlId = null;
        }
    }

    public void OnMouseUp(MouseEventArgs e)
    {
        // Complete device drag-to-reorder
        if (_drag.IsDragging && _drag.DeviceIndex >= 0 && _drag.DeviceIndex < _ctx.Devices.Count)
        {
            var filteredDevices = _ctx.Devices.Where(d => !d.IsVirtual).ToList();

            var draggedDevice = _ctx.Devices[_drag.DeviceIndex];
            int sourceFilteredIndex = filteredDevices.IndexOf(draggedDevice);

            if (sourceFilteredIndex >= 0 && _drag.DropTargetIndex >= 0 && _drag.DropTargetIndex != sourceFilteredIndex)
            {
                int targetFilteredIndex = _drag.DropTargetIndex;
                if (targetFilteredIndex > sourceFilteredIndex)
                    targetFilteredIndex--;

                int targetActualIndex;
                if (targetFilteredIndex >= 0 && targetFilteredIndex < filteredDevices.Count)
                {
                    var targetDevice = filteredDevices[targetFilteredIndex];
                    targetActualIndex = _ctx.Devices.IndexOf(targetDevice);
                    if (_drag.DropTargetIndex > sourceFilteredIndex)
                        targetActualIndex++;
                }
                else
                {
                    targetActualIndex = _ctx.Devices.Count;
                    for (int i = _ctx.Devices.Count - 1; i >= 0; i--)
                    {
                        if (!_ctx.Devices[i].IsVirtual)
                        {
                            targetActualIndex = i + 1;
                            break;
                        }
                    }
                }

                int sourceActualIndex = _ctx.Devices.IndexOf(draggedDevice);
                _ctx.Devices.RemoveAt(sourceActualIndex);
                if (targetActualIndex > sourceActualIndex)
                    targetActualIndex--;
                _ctx.Devices.Insert(targetActualIndex, draggedDevice);

                _ctx.SelectedDevice = _ctx.Devices.IndexOf(draggedDevice);
                _ctx.SaveDeviceOrder?.Invoke();
            }

            _drag.IsDragging = false;
            _drag.DeviceIndex = -1;
            _drag.DropTargetIndex = -1;
            _ctx.OwnerForm.Cursor = Cursors.Default;
            _ctx.MarkDirty();
            return;
        }

        // Reset potential drag state even if we didn't actually drag
        if (_drag.DeviceIndex >= 0)
        {
            _drag.DeviceIndex = -1;
            _drag.DropTargetIndex = -1;
        }
    }

    public void OnMouseWheel(MouseEventArgs e) { }

    public bool ProcessCmdKey(ref Message msg, Keys keyData) => false;

    public void OnMouseLeave()
    {
        _hoveredDevice = -1;
        _devCat.Hovered = -1;
        _silhouette.PrevHovered = false;
        _silhouette.NextHovered = false;
        _silhouette.RemoveVJoyHovered = false;
        _silhouette.SyncVJoyHovered = false;
        _ctx.HoveredControlId = null;
    }

    public void OnTick() { }

    public void OnActivated()
    {
        if (_ctx.SelectedDevice < 0)
            _ctx.SelectFirstDeviceInCategory?.Invoke();
        // Ensure the selected device is visible (not hidden) — fixes stale
        // selection when a previously-selected device was hidden in a prior session.
        EnsureVisibleDeviceSelected();
    }

    public void OnDeactivated() { }

    #region Drawing

    private void DrawDeviceListPanel(SKCanvas canvas, SKRect bounds)
    {
        float pad = FUIRenderer.PanelPadding;
        float itemGap = FUIRenderer.ItemSpacing;
        float frameInset = 5f;

        float sideTabWidth = FUIRenderer.SideTabWidth;

        FUIRenderer.DrawPanelShadow(canvas, bounds, 3f, 3f, 10f);

        var contentBounds = new SKRect(bounds.Left + frameInset + sideTabWidth, bounds.Top + frameInset,
                                        bounds.Right - frameInset, bounds.Bottom - frameInset);
        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background1.WithAlpha(140));
        canvas.DrawRect(contentBounds, bgPaint);

        DrawDeviceCategorySideTabs(canvas, bounds.Left + frameInset, bounds.Top + frameInset,
            sideTabWidth, bounds.Height - frameInset * 2);

        var frameBounds = new SKRect(bounds.Left + sideTabWidth, bounds.Top, bounds.Right, bounds.Bottom);
        FUIRenderer.DrawLCornerFrame(canvas, frameBounds, FUIColors.Frame, 30f, 8f);

        float titleBarHeight = 32f;
        var titleBounds = new SKRect(contentBounds.Left, contentBounds.Top, contentBounds.Right, contentBounds.Top + titleBarHeight);
        string categoryCode = _devCat.Active == 0 ? "D1" : "D2";
        string categoryName = _devCat.Active == 0 ? "DEVICES" : "DEVICES";
        FUIRenderer.DrawPanelTitle(canvas, titleBounds, categoryCode, categoryName);

        // Complete pending hide after delay
        const long HideDelayMs = 500;
        if (_pendingHideGuid is not null)
        {
            long elapsedMs = Environment.TickCount64 - _pendingHideTicks;
            if (elapsedMs >= HideDelayMs)
            {
                _ctx.AppSettings.SetDeviceHidden(_pendingHideGuid, true);
                _pendingHideGuid = null;
                EnsureVisibleDeviceSelected();
            }
            else
            {
                _ctx.MarkDirty(); // keep rendering until delay completes
            }
        }

        var filteredDevices = _devCat.Active == 0
            ? _ctx.Devices.Where(d => !d.IsVirtual &&
                (_showHiddenDevices || !_ctx.AppSettings.IsDeviceHidden(d.InstanceGuid.ToString()))).ToList()
            : _ctx.Devices.Where(d => d.IsVirtual).ToList();

        float itemY = contentBounds.Top + titleBarHeight + pad;
        float itemHeight = 60f;

        if (filteredDevices.Count == 0)
        {
            string emptyMsg = _devCat.Active == 0
                ? "No physical devices detected"
                : "No virtual devices detected";
            string helpMsg = _devCat.Active == 0
                ? "Connect a joystick, throttle, or other input device"
                : "Install vJoy or start a virtual device";
            bool showClientHint = _devCat.Active == 0 && !_ctx.AppSettings.ClientOnlyMode;

            const float boxPad = 12f;
            const float lineH = 20f;
            float boxH = boxPad + lineH + lineH + (showClientHint ? lineH * 2 + lineH * 0.5f : 0) + boxPad;
            var boxRect = new SKRect(contentBounds.Left + pad, itemY + 4f,
                contentBounds.Right - pad, itemY + 4f + boxH);

            using (var fillPaint = FUIRenderer.CreateFillPaint(FUIColors.Warning.WithAlpha(22)))
                canvas.DrawRoundRect(boxRect, 4f, 4f, fillPaint);
            using (var borderPaint = FUIRenderer.CreateStrokePaint(FUIColors.Warning.WithAlpha(90)))
                canvas.DrawRoundRect(boxRect, 4f, 4f, borderPaint);

            float tx = boxRect.Left + boxPad;
            float ty = boxRect.Top + boxPad + lineH - 3f;
            FUIRenderer.DrawText(canvas, emptyMsg, new SKPoint(tx, ty), FUIColors.Warning, 14f);
            ty += lineH;
            FUIRenderer.DrawText(canvas, helpMsg, new SKPoint(tx, ty), FUIColors.TextPrimary, 12f);
            if (showClientHint)
            {
                ty += lineH * 1.5f;
                FUIRenderer.DrawText(canvas, "Using Asteriq as a network client only?", new SKPoint(tx, ty), FUIColors.TextPrimary, 12f);
                ty += lineH;
                FUIRenderer.DrawText(canvas, "Enable Client Mode in Settings.", new SKPoint(tx, ty), FUIColors.TextPrimary, 12f);
            }
        }
        else
        {
            _drag.ItemBounds.Clear();

            for (int i = 0; i < filteredDevices.Count && itemY + itemHeight < contentBounds.Bottom - 40; i++)
            {
                int actualIndex = _ctx.Devices.IndexOf(filteredDevices[i]);

                var itemBounds = new SKRect(contentBounds.Left + pad - 10, itemY,
                    contentBounds.Left + pad - 10 + contentBounds.Width - pad, itemY + itemHeight);
                _drag.ItemBounds.Add(itemBounds);

                if (_drag.IsDragging && actualIndex == _drag.DeviceIndex)
                {
                    itemY += itemHeight + itemGap;
                    continue;
                }

                if (_drag.IsDragging && i == _drag.DropTargetIndex)
                {
                    using var dropPaint = FUIRenderer.CreateStrokePaint(FUIColors.Active, 2f);
                    canvas.DrawLine(itemBounds.Left, itemY - 2, itemBounds.Right, itemY - 2, dropPaint);
                }

                string status = filteredDevices[i].IsConnected ? "ONLINE" : "DISCONNECTED";
                string assignment = filteredDevices[i].IsVirtual
                    ? GetPrimaryDeviceForVJoyDevice(filteredDevices[i])
                    : GetVJoyAssignmentForDevice(filteredDevices[i]);
                FUIWidgets.DrawDeviceListItem(canvas, contentBounds.Left + pad - 10, itemY, contentBounds.Width - pad,
                    filteredDevices[i].Name, status, actualIndex == _ctx.SelectedDevice, actualIndex == _hoveredDevice, assignment);
                itemY += itemHeight + itemGap;
            }

            if (_drag.IsDragging && _drag.DeviceIndex >= 0 && _drag.DeviceIndex < _ctx.Devices.Count)
            {
                var draggedDevice = _ctx.Devices[_drag.DeviceIndex];
                string status = draggedDevice.IsConnected ? "ONLINE" : "DISCONNECTED";
                string assignment = draggedDevice.IsVirtual
                    ? GetPrimaryDeviceForVJoyDevice(draggedDevice)
                    : GetVJoyAssignmentForDevice(draggedDevice);

                canvas.Save();
                canvas.Translate(_drag.CurrentPoint.X - _drag.StartPoint.X, _drag.CurrentPoint.Y - _drag.StartPoint.Y);
                using var ghostPaint = new SKPaint { Color = SKColors.White.WithAlpha(180) };
                FUIWidgets.DrawDeviceListItem(canvas, contentBounds.Left + pad - 10,
                    _drag.ItemBounds.Count > 0 ? _drag.ItemBounds[0].Top + (_drag.DeviceIndex * (itemHeight + itemGap)) : contentBounds.Top + 50,
                    contentBounds.Width - pad, draggedDevice.Name, status, true, false, assignment);
                canvas.Restore();
            }
        }

        // Bottom row: "Include hidden" checkbox (only when on physical devices tab)
        if (_devCat.Active == 0)
        {
            const float checkboxSize = 14f;
            const float checkboxH = checkboxSize;
            float rowCenterY = bounds.Bottom - pad - 10f;
            float cbX = contentBounds.Left + pad;
            float cbY = rowCenterY - checkboxH / 2f;
            _showHiddenCheckboxBounds = new SKRect(cbX, cbY, cbX + checkboxSize, cbY + checkboxH);
            bool cbHovered = _showHiddenCheckboxBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            FUIWidgets.DrawCheckboxWithLabel(canvas, _showHiddenCheckboxBounds, _showHiddenDevices, cbHovered, "Include hidden", 12f);
        }
        else
        {
            _showHiddenCheckboxBounds = SKRect.Empty;
        }
    }

    private void DrawDeviceCategorySideTabs(SKCanvas canvas, float x, float y, float width, float height)
    {
        float tabHeight = 80f;
        float tabGap = 4f;

        float totalTabsHeight = tabHeight * 2 + tabGap;
        float startY = y + height - totalTabsHeight - 10f;

        var d1Bounds = new SKRect(x, startY + tabHeight + tabGap, x + width, startY + tabHeight * 2 + tabGap);
        _devCat.D1Bounds = d1Bounds;
        FUIWidgets.DrawVerticalSideTab(canvas, d1Bounds, "DEVICES_01", _devCat.Active == 0, _devCat.Hovered == 0);

        var d2Bounds = new SKRect(x, startY, x + width, startY + tabHeight);
        _devCat.D2Bounds = d2Bounds;
        FUIWidgets.DrawVerticalSideTab(canvas, d2Bounds, "DEVICES_02", _devCat.Active == 1, _devCat.Hovered == 1);
    }

    private void DrawDeviceDetailsPanel(SKCanvas canvas, SKRect bounds)
    {
        float pad = FUIRenderer.PanelPadding;
        float frameInset = 5f;

        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background1.WithAlpha(100));
        canvas.DrawRect(new SKRect(bounds.Left + frameInset, bounds.Top + frameInset,
            bounds.Right - frameInset, bounds.Bottom - frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame, 30f, 8f);

        if (_ctx.Devices.Count == 0 || _ctx.SelectedDevice < 0 || _ctx.SelectedDevice >= _ctx.Devices.Count)
        {
            FUIRenderer.DrawText(canvas, "Select a device to view details",
                new SKPoint(bounds.Left + pad, bounds.Top + 50), FUIColors.TextDim, 14f);
            return;
        }

        float centerX = bounds.MidX;
        float centerY = bounds.MidY;

        if (_ctx.JoystickSvg?.Picture is not null)
        {
            float maxSize = 900f;
            float maxWidth = Math.Min(bounds.Width - 40, maxSize);
            float maxHeight = Math.Min(bounds.Height - 40, maxSize);

            float constrainedWidth = Math.Min(maxWidth, maxHeight);
            float constrainedHeight = constrainedWidth;
            _ctx.SilhouetteBounds = new SKRect(
                centerX - constrainedWidth / 2,
                centerY - constrainedHeight / 2,
                centerX + constrainedWidth / 2,
                centerY + constrainedHeight / 2
            );

            DrawDeviceSilhouette(canvas, _ctx.SilhouetteBounds);
        }
        else
        {
            _ctx.SilhouetteBounds = SKRect.Empty;
            FUIRenderer.DrawTextCentered(canvas, "Device Preview",
                new SKRect(bounds.Left, centerY - 20, bounds.Right, centerY + 20),
                FUIColors.TextDim, 14f);
        }

        DrawActiveInputLeadLines(canvas, bounds);
    }

    private void DrawActiveInputLeadLines(SKCanvas canvas, SKRect panelBounds)
    {
        if (_ctx.DeviceMap is null || _ctx.JoystickSvg?.Picture is null) return;

        var visibleInputs = _ctx.ActiveInputTracker.GetVisibleInputs();
        int inputIndex = 0;

        foreach (var input in visibleInputs)
        {
            var control = input.Control;
            if (control?.Anchor is null) continue;

            float opacity = input.GetOpacity(_ctx.ActiveInputTracker.FadeDelay, _ctx.ActiveInputTracker.FadeDuration);
            if (opacity < 0.01f) continue;

            SKPoint anchorScreen = ViewBoxToScreen(control.Anchor.X, control.Anchor.Y);

            float labelX, labelY;
            bool goesRight = true;

            if (control.LabelOffset is not null)
            {
                float labelVbX = control.Anchor.X + control.LabelOffset.X;
                float labelVbY = control.Anchor.Y + control.LabelOffset.Y;
                var labelScreen = ViewBoxToScreen(labelVbX, labelVbY);
                labelX = labelScreen.X;
                labelY = labelScreen.Y;
                bool offsetGoesRight = control.LabelOffset.X >= 0;
                goesRight = _ctx.SvgMirrored ? !offsetGoesRight : offsetGoesRight;
            }
            else
            {
                labelY = panelBounds.Top + 80 + (inputIndex * 55);
                if (labelY > panelBounds.Bottom - 60)
                {
                    labelY = panelBounds.Top + 80 + ((inputIndex % 8) * 55);
                }
                labelX = _ctx.SilhouetteBounds.Right + 20;
            }

            DeviceLeadLineRenderer.DrawInputLeadLine(canvas, anchorScreen, new SKPoint(labelX, labelY), goesRight, opacity, input, _ctx.SvgMirrored, _ctx.SvgScale);

            inputIndex++;
        }
    }

    private SKPoint ViewBoxToScreen(float viewBoxX, float viewBoxY)
    {
        float screenX = _ctx.SvgMirrored
            ? _ctx.SvgOffset.X + _ctx.SilhouetteSourceWidth * _ctx.SvgScale - viewBoxX * _ctx.SvgScale
            : _ctx.SvgOffset.X + viewBoxX * _ctx.SvgScale;

        float screenY = _ctx.SvgOffset.Y + viewBoxY * _ctx.SvgScale;
        return new SKPoint(screenX, screenY);
    }

    private void DrawDeviceSilhouette(SKCanvas canvas, SKRect bounds)
    {
        bool mirror = _ctx.DeviceMap?.Mirror ?? false;
        var activeSvg = _ctx.GetActiveSvg?.Invoke();
        if (activeSvg?.Picture is not null)
        {
            var t = FUIRenderer.DrawSvgInBounds(canvas, activeSvg, bounds, mirror);
            ApplySvgTransform(t, mirror);
            return;
        }

        var activeBitmap = _ctx.GetActiveBitmap?.Invoke();
        if (activeBitmap is not null)
        {
            float vbW = _ctx.DeviceMap?.ViewBox?.X ?? activeBitmap.Width;
            float vbH = _ctx.DeviceMap?.ViewBox?.Y ?? activeBitmap.Height;
            var t = FUIRenderer.DrawBitmapInBounds(canvas, activeBitmap, vbW, vbH, bounds, mirror);
            ApplySvgTransform(t, mirror);
            return;
        }

        FUIWidgets.DrawJoystickOutlineFallback(canvas, bounds);
    }

    private void ApplySvgTransform(FUIRenderer.SvgTransform t, bool mirror)
    {
        _ctx.SvgScale = t.Scale;
        _ctx.SvgOffset = t.Offset;
        _ctx.SvgMirrored = mirror;
        _ctx.SilhouetteSourceWidth = t.SourceWidth;
    }

    private void DrawDeviceActionsPanel(SKCanvas canvas, SKRect bounds)
    {
        float pad = FUIRenderer.PanelPadding;
        float frameInset = 5f;

        FUIRenderer.DrawPanelShadow(canvas, bounds, 3f, 3f, 10f);

        var contentBounds = new SKRect(bounds.Left + frameInset, bounds.Top + frameInset,
                                        bounds.Right - frameInset, bounds.Bottom - frameInset);
        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background1.WithAlpha(140));
        canvas.DrawRect(contentBounds, bgPaint);

        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame, 30f, 8f);

        float titleBarHeight = 32f;
        var titleBounds = new SKRect(contentBounds.Left, contentBounds.Top, contentBounds.Right, contentBounds.Top + titleBarHeight);
        FUIRenderer.DrawPanelTitle(canvas, titleBounds, "D3", "DEVICE ACTIONS");

        float y = contentBounds.Top + titleBarHeight + pad;
        float buttonHeight = 32f;
        float buttonGap = 8f;

        bool hasPhysicalDevice = _devCat.Active == 0 && _ctx.SelectedDevice >= 0 &&
                                  _ctx.SelectedDevice < _ctx.Devices.Count && !_ctx.Devices[_ctx.SelectedDevice].IsVirtual;
        bool hasVirtualDevice = _devCat.Active == 1 && _ctx.SelectedDevice >= 0 &&
                                 _ctx.SelectedDevice < _ctx.Devices.Count && _ctx.Devices[_ctx.SelectedDevice].IsVirtual;

        if (hasPhysicalDevice)
        {
            var device = _ctx.Devices[_ctx.SelectedDevice];
            bool isDisconnected = !device.IsConnected;

            FUIRenderer.DrawText(canvas, isDisconnected ? "DISCONNECTED DEVICE" : "SELECTED DEVICE",
                new SKPoint(contentBounds.Left + pad, y), isDisconnected ? FUIColors.Danger : FUIColors.TextDim, 12f);
            y += 16f;

            string shortName = device.Name.Length > 22 ? string.Concat(device.Name.AsSpan(0, 20), "...") : device.Name;
            FUIRenderer.DrawText(canvas, shortName, new SKPoint(contentBounds.Left + pad, y),
                isDisconnected ? FUIColors.TextDim : FUIColors.TextPrimary, 14f);
            y += 20f;

            // Capabilities + VID:PID on same line
            string capsText = $"{device.AxisCount} axes  {device.ButtonCount} btns  {device.HatCount} hats";
            var (vid, pid) = Services.DeviceMatchingService.ExtractVidPidFromSdlGuid(device.InstanceGuid);
            string vidPidText = vid > 0 ? $"{vid:X4}:{pid:X4}" : "";

            FUIRenderer.DrawText(canvas, capsText, new SKPoint(contentBounds.Left + pad, y), FUIColors.TextDim, 12f);
            if (!string.IsNullOrEmpty(vidPidText))
            {
                float vidPidWidth = FUIRenderer.MeasureText(vidPidText, 12f);
                FUIRenderer.DrawText(canvas, vidPidText,
                    new SKPoint(contentBounds.Right - pad - vidPidWidth, y), FUIColors.Active, 12f);
            }
            y += 24f;

            float buttonWidth = contentBounds.Width - pad * 2;

            if (isDisconnected)
            {
                _actions.Map1to1Bounds = SKRect.Empty;

                _actions.ClearMappingsBounds = new SKRect(contentBounds.Left + pad, y, contentBounds.Left + pad + buttonWidth, y + buttonHeight);
                var clearState = _actions.ClearMappingsHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
                FUIRenderer.DrawButton(canvas, _actions.ClearMappingsBounds, "CLEAR MAPPINGS", clearState, isDanger: true);
                y += buttonHeight + buttonGap;

                _actions.RemoveDeviceBounds = new SKRect(contentBounds.Left + pad, y, contentBounds.Left + pad + buttonWidth, y + buttonHeight);
                var removeState = _actions.RemoveDeviceHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
                FUIRenderer.DrawButton(canvas, _actions.RemoveDeviceBounds, "REMOVE DEVICE", removeState, isDanger: true);
            }
            else
            {
                _actions.RemoveDeviceBounds = SKRect.Empty;

                if (_ctx.VJoyService.IsInitialized)
                {
                    _actions.Map1to1Bounds = new SKRect(contentBounds.Left + pad, y, contentBounds.Left + pad + buttonWidth, y + buttonHeight);
                    var mapState = _actions.Map1to1Hovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
                    FUIRenderer.DrawButton(canvas, _actions.Map1to1Bounds, "MAP 1:1 TO VJOY", mapState);
                    y += buttonHeight + buttonGap;

                    _actions.ClearMappingsBounds = new SKRect(contentBounds.Left + pad, y, contentBounds.Left + pad + buttonWidth, y + buttonHeight);
                    var clearState2 = _actions.ClearMappingsHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
                    FUIRenderer.DrawButton(canvas, _actions.ClearMappingsBounds, "CLEAR MAPPINGS", clearState2, isDanger: true);
                    y += buttonHeight + buttonGap;
                }
                else
                {
                    _actions.Map1to1Bounds = SKRect.Empty;
                    _actions.ClearMappingsBounds = SKRect.Empty;
                }

                // Hide Device — Asteriq-level UI preference, bottom-anchored (separate from HidHide driver hiding)
                {
                    bool isHiddenFromView = _ctx.AppSettings.IsDeviceHidden(device.InstanceGuid.ToString())
                        || _pendingHideGuid == device.InstanceGuid.ToString();
                    const float cbSize = 14f;
                    float rowCenterY = contentBounds.Bottom - pad - cbSize / 2f;
                    float cbX = contentBounds.Right - pad - cbSize;
                    float cbY = rowCenterY - cbSize / 2f;
                    _actions.HideFromViewBounds = new SKRect(cbX, cbY, cbX + cbSize, cbY + cbSize);
                    bool cbHovered = _actions.HideFromViewBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
                    FUIWidgets.DrawCheckboxWithLabelLeft(canvas, _actions.HideFromViewBounds, isHiddenFromView, cbHovered, "Hide Device", 12f);
                }
            }
        }
        else if (hasVirtualDevice)
        {
            _actions.Map1to1Bounds = SKRect.Empty;
            _actions.ClearMappingsBounds = SKRect.Empty;
            _actions.RemoveDeviceBounds = SKRect.Empty;
            _actions.HideFromViewBounds = SKRect.Empty;
            _silhouette.RemoveVJoyBounds = SKRect.Empty;

            uint vjoyId = GetSelectedVJoyId();
            var vjoyInfo = vjoyId > 0 ? _ctx.VJoyDevices.FirstOrDefault(v => v.Id == vjoyId) : null;

            // Show vJoy slot header
            FUIRenderer.DrawText(canvas, "VIRTUAL DEVICE",
                new SKPoint(contentBounds.Left + pad, y), FUIColors.TextDim, 12f);
            y += 16f;

            string vjoyName = vjoyId > 0 ? $"vJoy Slot {vjoyId}" : _ctx.Devices[_ctx.SelectedDevice].Name;
            FUIRenderer.DrawText(canvas, vjoyName, new SKPoint(contentBounds.Left + pad, y), FUIColors.TextPrimary, 14f);
            y += 20f;

            if (vjoyInfo is not null)
            {
                int axisCount = new[] { vjoyInfo.HasAxisX, vjoyInfo.HasAxisY, vjoyInfo.HasAxisZ,
                    vjoyInfo.HasAxisRX, vjoyInfo.HasAxisRY, vjoyInfo.HasAxisRZ,
                    vjoyInfo.HasSlider0, vjoyInfo.HasSlider1 }.Count(b => b);
                int hatCount = vjoyInfo.DiscPovCount + vjoyInfo.ContPovCount;
                FUIRenderer.DrawText(canvas, $"{axisCount} axes  {vjoyInfo.ButtonCount} btns  {hatCount} hats",
                    new SKPoint(contentBounds.Left + pad, y), FUIColors.TextDim, 12f);
                y += 20f;
            }

            // Show primary mapped physical device
            y += 6f;
            FUIRenderer.DrawText(canvas, "MAPPED DEVICE",
                new SKPoint(contentBounds.Left + pad, y), FUIColors.TextDim, 12f);
            y += 16f;

            var primaryDevice = GetPrimaryPhysicalDevice(vjoyId);
            if (primaryDevice is not null)
            {
                string devName = primaryDevice.Name.Length > 22 ? primaryDevice.Name[..20] + "..." : primaryDevice.Name;
                FUIRenderer.DrawText(canvas, devName, new SKPoint(contentBounds.Left + pad, y),
                    primaryDevice.IsConnected ? FUIColors.TextPrimary : FUIColors.TextDim, 14f);
                y += 20f;
                string primaryCapsText = $"{primaryDevice.AxisCount} axes  {primaryDevice.ButtonCount} btns  {primaryDevice.HatCount} hats";
                var (pVid, pPid) = Services.DeviceMatchingService.ExtractVidPidFromSdlGuid(primaryDevice.InstanceGuid);
                string primaryVidPid = pVid > 0 ? $"{pVid:X4}:{pPid:X4}" : "";

                FUIRenderer.DrawText(canvas, primaryCapsText, new SKPoint(contentBounds.Left + pad, y), FUIColors.TextDim, 12f);
                if (!string.IsNullOrEmpty(primaryVidPid))
                {
                    float pVidPidWidth = FUIRenderer.MeasureText(primaryVidPid, 12f);
                    FUIRenderer.DrawText(canvas, primaryVidPid,
                        new SKPoint(contentBounds.Right - pad - pVidPidWidth, y), FUIColors.Active, 12f);
                }
                y += 20f;
            }
            else
            {
                FUIRenderer.DrawText(canvas, "None", new SKPoint(contentBounds.Left + pad, y), FUIColors.TextDisabled, 14f);
                y += 20f;
            }

            // Silhouette picker
            y += 6f;
            FUIRenderer.DrawText(canvas, "VISUAL IDENTITY",
                new SKPoint(contentBounds.Left + pad, y), FUIColors.TextDim, 12f);
            y += 16f;

            float arrowButtonSize = 24f;
            float leftMargin = contentBounds.Left + pad;
            float rightMargin = contentBounds.Right - pad;

            var (silhouetteLabel, hasPrev, hasNext) = GetSilhouettePickerState();

            _silhouette.PrevBounds = new SKRect(leftMargin, y, leftMargin + arrowButtonSize, y + arrowButtonSize);
            FUIWidgets.DrawArrowButton(canvas, _silhouette.PrevBounds, "<", _silhouette.PrevHovered, hasPrev);

            var labelBounds = new SKRect(leftMargin + arrowButtonSize, y, rightMargin - arrowButtonSize, y + arrowButtonSize);
            FUIRenderer.DrawTextCentered(canvas, silhouetteLabel, labelBounds, FUIColors.TextBright, 13f);

            _silhouette.NextBounds = new SKRect(rightMargin - arrowButtonSize, y, rightMargin, y + arrowButtonSize);
            FUIWidgets.DrawArrowButton(canvas, _silhouette.NextBounds, ">", _silhouette.NextHovered, hasNext);
            y += arrowButtonSize + buttonGap * 2;

            // Sync to physical button — shown when a physical device is mapped
            if (primaryDevice is not null)
            {
                float syncWidth = contentBounds.Width - pad * 2;
                _silhouette.SyncVJoyBounds = new SKRect(contentBounds.Left + pad, y, contentBounds.Left + pad + syncWidth, y + buttonHeight);
                var syncState = _silhouette.SyncVJoyHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
                FUIRenderer.DrawButton(canvas, _silhouette.SyncVJoyBounds, "MATCH PHYSICAL", syncState);
                y += buttonHeight + buttonGap;
            }
            else
            {
                _silhouette.SyncVJoyBounds = SKRect.Empty;
            }

            // Remove vJoy device button
            float removeWidth = contentBounds.Width - pad * 2;
            _silhouette.RemoveVJoyBounds = new SKRect(contentBounds.Left + pad, y, contentBounds.Left + pad + removeWidth, y + buttonHeight);
            var removeVJoyState = _silhouette.RemoveVJoyHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
            FUIRenderer.DrawButton(canvas, _silhouette.RemoveVJoyBounds, "REMOVE VJOY DEVICE", removeVJoyState, isDanger: true);
        }
        else
        {
            _actions.Map1to1Bounds = SKRect.Empty;
            _actions.ClearMappingsBounds = SKRect.Empty;
            _actions.RemoveDeviceBounds = SKRect.Empty;
            _actions.HideFromViewBounds = SKRect.Empty;
            _silhouette.RemoveVJoyBounds = SKRect.Empty;
            _silhouette.SyncVJoyBounds = SKRect.Empty;
            _silhouette.PrevBounds = SKRect.Empty;
            _silhouette.NextBounds = SKRect.Empty;

            FUIRenderer.DrawText(canvas, "Select a device",
                new SKPoint(contentBounds.Left + pad, y + 20), FUIColors.TextDim, 13f);
            FUIRenderer.DrawText(canvas, "to see available actions",
                new SKPoint(contentBounds.Left + pad, y + 36), FUIColors.TextDim, 13f);
        }
    }

    private void DrawStatusPanel(SKCanvas canvas, SKRect bounds)
    {
        float pad = FUIRenderer.PanelPadding;
        float itemGap = FUIRenderer.SpaceSM;
        float frameInset = 5f;

        FUIRenderer.DrawPanelShadow(canvas, bounds, 3f, 3f, 10f);

        var contentBounds = new SKRect(bounds.Left + frameInset, bounds.Top + frameInset,
                                        bounds.Right - frameInset, bounds.Bottom - frameInset);
        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background1.WithAlpha(140));
        canvas.DrawRect(contentBounds, bgPaint);

        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame, 30f, 8f);

        float titleBarHeight = 32f;
        var titleBounds = new SKRect(contentBounds.Left, contentBounds.Top, contentBounds.Right, contentBounds.Top + titleBarHeight);
        FUIRenderer.DrawPanelTitle(canvas, titleBounds, "S1", "STATUS");

        float statusItemHeight = 24f;
        float itemY = contentBounds.Top + titleBarHeight + pad;

        bool vjoyActive = _ctx.VJoyService.IsInitialized;
        FUIWidgets.DrawStatusItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2, "VJOY DRIVER",
            vjoyActive ? "ACTIVE" : "NOT FOUND", vjoyActive ? FUIColors.Active : FUIColors.Danger, fontSize: 12f);
        itemY += statusItemHeight + itemGap;

        FUIWidgets.DrawStatusItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2, "FORWARDING",
            _ctx.IsForwarding ? "RUNNING" : "STOPPED", FUIColors.InteractiveColor(_ctx.IsForwarding), fontSize: 12f);
        itemY += statusItemHeight + itemGap;

        int pollHz = _ctx.InputService.PollRateHz;
        string pollRateText = pollHz > 0 ? $"{pollHz} HZ" : "—";
        FUIWidgets.DrawStatusItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2, "POLL RATE", pollRateText, FUIColors.TextPrimary, fontSize: 12f);

        float buttonHeight = 36f;
        float buttonWidth = contentBounds.Width - pad * 2;
        float buttonY = bounds.Bottom - frameInset - FUIRenderer.SpaceLG - buttonHeight;

        if (_ctx.IsForwarding)
        {
            _forwarding.StopBounds = new SKRect(contentBounds.Left + pad, buttonY,
                contentBounds.Left + pad + buttonWidth, buttonY + buttonHeight);
            _forwarding.StartBounds = SKRect.Empty;
            FUIRenderer.DrawButton(canvas, _forwarding.StopBounds, "STOP FORWARDING",
                FUIRenderer.ButtonState.Active, isDanger: true);
        }
        else
        {
            _forwarding.StartBounds = new SKRect(contentBounds.Left + pad, buttonY,
                contentBounds.Left + pad + buttonWidth, buttonY + buttonHeight);
            _forwarding.StopBounds = SKRect.Empty;
            FUIRenderer.DrawButton(canvas, _forwarding.StartBounds, "START FORWARDING",
                _forwarding.StartHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
        }
    }

    #endregion

    #region Forwarding

    private void StartForwarding()
    {
        if (_ctx.IsForwarding) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null)
        {
            FUIMessageBox.ShowWarning(_ctx.OwnerForm,
                "No active configuration found.\n\nTo create mappings:\n1. Select a physical device\n2. Click 'MAP 1:1 TO VJOY'",
                "Cannot Start Forwarding");
            return;
        }

        if (profile.AxisMappings.Count == 0 && profile.ButtonMappings.Count == 0 && profile.HatMappings.Count == 0)
        {
            FUIMessageBox.ShowWarning(_ctx.OwnerForm,
                $"Configuration '{profile.Name}' has no mappings.\n\nTo create mappings:\n1. Select a physical device\n2. Click 'MAP 1:1 TO VJOY'",
                "Cannot Start Forwarding");
            return;
        }

        _ctx.MappingEngine.LoadProfile(profile);

        if (!_ctx.VJoyService.IsInitialized)
        {
            FUIMessageBox.ShowError(_ctx.OwnerForm,
                "vJoy driver is not initialized.\n\nPlease ensure vJoy is installed correctly.",
                "Cannot Start Forwarding");
            return;
        }

        var requiredDevices = profile.AxisMappings
            .Select(m => m.Output.VJoyDevice)
            .Concat(profile.ButtonMappings.Select(m => m.Output.VJoyDevice))
            .Concat(profile.HatMappings.Select(m => m.Output.VJoyDevice))
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        foreach (var deviceId in requiredDevices)
        {
            var info = _ctx.VJoyService.GetDeviceInfo(deviceId);
            if (!info.Exists)
            {
                FUIMessageBox.ShowError(_ctx.OwnerForm,
                    $"vJoy device {deviceId} does not exist.\n\nPlease configure vJoy device {deviceId} using 'Configure vJoy'.",
                    "Cannot Start Forwarding");
                return;
            }
        }

        if (!_ctx.MappingEngine.Start())
        {
            int ourPid = Environment.ProcessId;
            var statusMessages = requiredDevices
                .Select(id => {
                    var info = _ctx.VJoyService.GetDeviceInfo(id);
                    int ownerPid = VJoy.VJoyInterop.GetOwnerPid(id);
                    return $"vJoy {id}: {info.Status} (Owner PID: {ownerPid}, Our PID: {ourPid})";
                })
                .ToList();

            FUIMessageBox.ShowError(_ctx.OwnerForm,
                $"Failed to acquire vJoy device(s).\n\nDevice status:\n{string.Join("\n", statusMessages)}\n\nIf Owner PID matches Our PID, try restarting the app.\nIf different, another app owns the device.",
                "Cannot Start Forwarding");
            return;
        }

        _ctx.IsForwarding = true;
        _ctx.TrayIcon.SetActive(true);
        _ctx.UpdateTrayMenu?.Invoke();
        System.Diagnostics.Debug.WriteLine($"Started forwarding with profile: {profile.Name}");
    }

    private void StopForwarding()
    {
        if (!_ctx.IsForwarding) return;

        _ctx.MappingEngine.Stop();
        _ctx.IsForwarding = false;
        _ctx.TrayIcon.SetActive(false);
        _ctx.UpdateTrayMenu?.Invoke();
        System.Diagnostics.Debug.WriteLine("Stopped forwarding");
    }

    #endregion

    #region vJoy Device Management

    // Pending hide — guid of device waiting to be hidden after visual delay
    private string? _pendingHideGuid;
    private long _pendingHideTicks;

    private void ToggleHideFromView()
    {
        if (_ctx.SelectedDevice < 0 || _ctx.SelectedDevice >= _ctx.Devices.Count) return;
        var device = _ctx.Devices[_ctx.SelectedDevice];
        string guid = device.InstanceGuid.ToString();
        bool currentlyHidden = _ctx.AppSettings.IsDeviceHidden(guid);

        if (currentlyHidden)
        {
            _ctx.AppSettings.SetDeviceHidden(guid, false);
            _pendingHideGuid = null;
        }
        else
        {
            // Mark pending — device stays visible in both D1 and D3 until delay elapses
            _pendingHideGuid = guid;
            _pendingHideTicks = Environment.TickCount64;
        }
        _ctx.MarkDirty();
    }

    /// <summary>
    /// Ensures a visible physical device is selected whenever the visible list is non-empty.
    /// Clears selection only when the visible list is genuinely empty.
    /// </summary>
    private void EnsureVisibleDeviceSelected()
    {
        var visible = _ctx.Devices
            .Where(d => !d.IsVirtual &&
                (_showHiddenDevices || !_ctx.AppSettings.IsDeviceHidden(d.InstanceGuid.ToString())))
            .ToList();

        if (visible.Count == 0)
        {
            _ctx.SelectedDevice = -1;
            return;
        }

        // If current selection is still in the visible list, keep it
        if (_ctx.SelectedDevice >= 0 && _ctx.SelectedDevice < _ctx.Devices.Count)
        {
            var current = _ctx.Devices[_ctx.SelectedDevice];
            if (visible.Contains(current)) return;
        }

        // Otherwise pick the first visible device
        var first = visible[0];
        _ctx.SelectedDevice = _ctx.Devices.IndexOf(first);
        _ctx.LoadDeviceMapForDevice(first);
    }

    /// <summary>
    /// Toggles HidHide driver hiding for the given physical device.
    /// Called from both the Devices tab and the Settings → HidHide panel via TabContext callback.
    /// When hiding: enables cloaking, ensures Asteriq can see devices, whitelists SC executables.
    /// </summary>
    public void ToggleHidHideForDevicePublic(PhysicalDeviceInfo device) => ToggleHidHideForDevice(device);

    private void ToggleHidHideForDevice(PhysicalDeviceInfo device)
    {
        if (_ctx.HidHide is null || _ctx.DeviceMatching is null) return;
        var (vid, pid) = DeviceMatchingService.ExtractVidPidFromSdlGuid(device.InstanceGuid);
        var vidPidPattern = vid > 0 ? $"VID_{vid:X4}&PID_{pid:X4}" : null;

        // Determine current state directly from the service (no cached state needed)
        var hiddenPaths = _ctx.HidHide.GetHiddenDevices();
        bool isCurrentlyHidden = vidPidPattern is not null &&
            hiddenPaths.Any(p => p.Contains(vidPidPattern, StringComparison.OrdinalIgnoreCase));

        if (isCurrentlyHidden)
        {
            var toUnhide = vidPidPattern is not null
                ? hiddenPaths.Where(p => p.Contains(vidPidPattern, StringComparison.OrdinalIgnoreCase)).ToList()
                : new List<string>();
            foreach (var path in toUnhide)
                _ctx.HidHide.UnhideDevice(path);
            UpdateSCWhitelist(isHiding: false);
        }
        else
        {
            var matches = _ctx.DeviceMatching.FindMatchingHidDevices(device);
            if (matches.Count == 0) return;
            foreach (var match in matches)
                _ctx.HidHide.HideDevice(match.DeviceInstancePath);
            if (!_ctx.HidHide.IsCloakingEnabled())
                _ctx.HidHide.EnableCloaking();
            _ctx.HidHide.EnsureSelfCanSeeDevices();
            UpdateSCWhitelist(isHiding: true);
        }

        _ctx.MarkDirty();
    }

    /// <summary>
    /// Updates the HidHide whitelist for all detected SC game executables based on the current mode.
    ///
    /// Normal mode  — whitelisted apps CAN see hidden devices.
    ///   Hiding:   REMOVE SC from whitelist so it cannot see the hidden physical device.
    ///   Unhiding: no-op (SC was never whitelisted; devices visible to all again).
    ///
    /// Inverse mode — whitelisted apps are BLOCKED from hidden devices.
    ///   Hiding:   ADD SC to whitelist so it is blocked from the hidden physical device.
    ///   Unhiding: REMOVE SC from whitelist (no longer need to block it).
    /// </summary>
    private void UpdateSCWhitelist(bool isHiding)
    {
        if (_ctx.HidHide is null || _ctx.SCInstallation is null) return;

        // On unhide: only modify the SC whitelist if no hidden devices remain.
        // If other joysticks are still hidden, the SC restriction must stay in place.
        if (!isHiding && _ctx.HidHide.GetHiddenDevices().Count > 0) return;

        bool isInverse = _ctx.HidHide.IsInverseMode();

        foreach (var installation in _ctx.SCInstallation.Installations)
        {
            var exePath = Path.Combine(installation.InstallPath, "Bin64", "StarCitizen.exe");
            if (!File.Exists(exePath)) continue;

            if (isInverse)
            {
                // Inverse: whitelist = blocked. Add on hide so SC can't see device; remove on unhide.
                if (isHiding) _ctx.HidHide.WhitelistApp(exePath);
                else _ctx.HidHide.UnwhitelistApp(exePath);
            }
            else
            {
                // Normal: whitelist = allowed. Remove SC so it can't bypass the hide.
                // On unhide there's nothing to undo — SC was never in the whitelist.
                if (isHiding) _ctx.HidHide.UnwhitelistApp(exePath);
            }
        }
    }

    /// <summary>
    /// Removes a vJoy device using vJoyConfig.exe CLI, then refreshes the device lists.
    /// </summary>
    private void RemoveVJoyDevice(uint vjoyId)
    {
        // Prevent removal of the last vJoy device — vJoy requires at least one device
        int existingCount = _ctx.VJoyDevices.Count(v => v.Exists);
        if (existingCount <= 1)
        {
            FUIMessageBox.ShowError(_ctx.OwnerForm,
                "Cannot remove the last vJoy device.\nvJoy requires at least one device to be configured.",
                "Remove Blocked");
            return;
        }

        // Build per-device mapping breakdown for the warning dialog
        var profile = _ctx.ProfileManager.ActiveProfile;
        int mappingCount = 0;
        string[]? detailLines = null;

        if (profile is not null)
        {
            // Count total affected mappings
            mappingCount =
                profile.AxisMappings.Count(m => m.Output.VJoyDevice == vjoyId) +
                profile.ButtonMappings.Count(m => m.Output.VJoyDevice == vjoyId) +
                profile.HatMappings.Count(m => m.Output.VJoyDevice == vjoyId) +
                profile.AxisToButtonMappings.Count(m => m.Output.VJoyDevice == vjoyId) +
                profile.ButtonToAxisMappings.Count(m => m.Output.VJoyDevice == vjoyId);

            if (mappingCount > 0)
            {
                // Tally how many affected mappings reference each physical device
                var deviceCounts = new Dictionary<string, int>();
                void Tally(IEnumerable<string> deviceIds)
                {
                    foreach (var id in deviceIds)
                    {
                        deviceCounts.TryGetValue(id, out int c);
                        deviceCounts[id] = c + 1;
                    }
                }

                foreach (var m in profile.AxisMappings.Where(m => m.Output.VJoyDevice == vjoyId))
                    Tally(m.Inputs.Select(i => i.DeviceId).Distinct());
                foreach (var m in profile.ButtonMappings.Where(m => m.Output.VJoyDevice == vjoyId))
                    Tally(m.Inputs.Select(i => i.DeviceId).Distinct());
                foreach (var m in profile.HatMappings.Where(m => m.Output.VJoyDevice == vjoyId))
                    Tally(m.Inputs.Select(i => i.DeviceId).Distinct());
                foreach (var m in profile.AxisToButtonMappings.Where(m => m.Output.VJoyDevice == vjoyId))
                    Tally(m.Inputs.Select(i => i.DeviceId).Distinct());
                foreach (var m in profile.ButtonToAxisMappings.Where(m => m.Output.VJoyDevice == vjoyId))
                    Tally(m.Inputs.Select(i => i.DeviceId).Distinct());

                var deviceLookup = _ctx.Devices.Concat(_ctx.DisconnectedDevices)
                    .DistinctBy(d => d.InstanceGuid)
                    .ToDictionary(d => d.InstanceGuid.ToString(), d => d.Name);

                detailLines = deviceCounts
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv =>
                    {
                        string name = deviceLookup.TryGetValue(kv.Key, out var n) ? n : "Unknown device";
                        int c = kv.Value;
                        return $"{name}  \u2014  {c} mapping{(c == 1 ? "" : "s")}";
                    })
                    .ToArray();
            }
        }

        string message = mappingCount > 0
            ? $"Remove vJoy Slot {vjoyId}?\n\nThis will delete the virtual device from the driver.\n" +
              $"{mappingCount} mapping{(mappingCount == 1 ? "" : "s")} targeting this device will also be removed.\n\n" +
              "An admin prompt will appear."
            : $"Remove vJoy Slot {vjoyId}?\n\nThis will delete the virtual device from the driver.\n\nAn admin prompt will appear.";

        bool confirmed = FUIMessageBox.ShowDestructiveConfirm(_ctx.OwnerForm, message, "Remove vJoy Device", "Remove", detailLines);
        if (!confirmed) return;

        string? configPath = DriverSetupManager.GetVJoyConfigPath();
        if (configPath is null)
        {
            FUIMessageBox.ShowWarning(_ctx.OwnerForm,
                "vJoyConfig.exe was not found in your vJoy installation.",
                "vJoy Not Found");
            return;
        }

        // Clean up mappings before deleting the device so stale references never persist
        if (profile is not null && mappingCount > 0)
        {
            profile.AxisMappings.RemoveAll(m => m.Output.VJoyDevice == vjoyId);
            profile.ButtonMappings.RemoveAll(m => m.Output.VJoyDevice == vjoyId);
            profile.HatMappings.RemoveAll(m => m.Output.VJoyDevice == vjoyId);
            profile.AxisToButtonMappings.RemoveAll(m => m.Output.VJoyDevice == vjoyId);
            profile.ButtonToAxisMappings.RemoveAll(m => m.Output.VJoyDevice == vjoyId);
            _ctx.ProfileManager.SaveActiveProfile();
            _ctx.OnMappingsChanged();
        }

        // Release the device if our process has it acquired (prevents driver refusing deletion)
        _ctx.VJoyService.ReleaseDevice(vjoyId);

        int exitCode;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = configPath,
                Arguments = $"-d {vjoyId}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit(10000);
            exitCode = process?.ExitCode ?? -1;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return; // User cancelled UAC — mappings already cleaned, that's fine
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            FUIMessageBox.ShowError(_ctx.OwnerForm,
                $"Failed to remove vJoy device:\n{ex.Message}",
                "Removal Failed");
            return;
        }

        if (exitCode != 0)
        {
            FUIMessageBox.ShowError(_ctx.OwnerForm,
                $"vJoyConfig.exe reported failure (exit code {exitCode}).\n\n" +
                "The device may still exist. Try using the vJoy Configuration tool directly.",
                "Removal Failed");
            return;
        }

        // Update vJoy list — do NOT call RefreshDevices() here because SDL2 still reports
        // the device until the OS sends a device-removed notification
        _ctx.RefreshVJoyDevices?.Invoke();
        _ctx.SelectedDevice = -1;
        _ctx.MarkDirty();
    }

    /// <summary>
    /// Reconfigure a vJoy device to match its paired physical device's capabilities.
    /// Deletes and recreates the vJoy slot via vJoyConfig.exe (requires UAC).
    /// Preserves all existing mappings by keeping the same slot ID.
    /// </summary>
    private void SyncVJoyToPhysical(uint vjoyId)
    {
        var physical = GetPrimaryPhysicalDevice(vjoyId);
        if (physical is null) return;

        string? configPath = DriverSetupManager.GetVJoyConfigPath();
        if (configPath is null)
        {
            FUIMessageBox.ShowWarning(_ctx.OwnerForm,
                "vJoyConfig.exe was not found in your vJoy installation.\n\n" +
                "Please ensure vJoy is installed correctly.",
                "vJoy Not Found");
            return;
        }

        string[] axisNames = { "X", "Y", "Z", "RX", "RY", "RZ", "SL0", "SL1" };
        int axisCount = Math.Min(physical.AxisCount, axisNames.Length);
        int buttonCount = Math.Max(physical.ButtonCount, 1);
        int povCount = physical.HatCount;

        string message = $"Match vJoy Slot {vjoyId} to {physical.Name}?\n\n" +
                         $"Axes: {axisCount}\n" +
                         $"Buttons: {buttonCount}\n" +
                         $"Hats: {povCount}\n\n" +
                         "Existing mappings will be preserved.\nAn admin prompt will appear.";

        int result = FUIMessageBox.Show(_ctx.OwnerForm, message, "Match Physical Device",
            FUIMessageBox.MessageBoxType.Question, "Match", "Cancel");
        if (result != 0) return;

        // Release the device before reconfiguring
        _ctx.VJoyService.ReleaseDevice(vjoyId);

        // Build vJoyConfig args: delete the slot then recreate with new capabilities
        // vJoyConfig.exe supports applying a new config over an existing slot with -f (force)
        string args = $"{vjoyId} -f";
        if (axisCount > 0)
            args += $" -a {string.Join(" ", axisNames.Take(axisCount))}";
        if (buttonCount > 0)
            args += $" -b {buttonCount}";
        if (povCount > 0)
            args += $" -p {povCount}";

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = configPath,
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };

            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit(15000);

            int exitCode = process?.ExitCode ?? -1;
            if (exitCode != 0)
            {
                FUIMessageBox.ShowError(_ctx.OwnerForm,
                    $"vJoyConfig.exe reported failure (exit code {exitCode}).\n\n" +
                    "The device configuration may not have changed.",
                    "Sync Failed");
                return;
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return; // User cancelled UAC
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            FUIMessageBox.ShowError(_ctx.OwnerForm,
                $"Failed to reconfigure vJoy device:\n{ex.Message}",
                "Sync Failed");
            return;
        }

        // Re-enumerate and refresh
        _ctx.RefreshVJoyDevices?.Invoke();
        _ctx.RefreshDevices?.Invoke();
        _ctx.MarkDirty();
    }

    #endregion

    #region Silhouette Picker

    /// <summary>
    /// Gets the vJoy slot ID for the currently selected virtual device.
    /// Uses VJoyDevices list by position first (most reliable); falls back to name regex.
    /// </summary>
    private uint GetSelectedVJoyId()
    {
        if (_devCat.Active != 1 || _ctx.SelectedDevice < 0 || _ctx.SelectedDevice >= _ctx.Devices.Count)
            return 0;
        var device = _ctx.Devices[_ctx.SelectedDevice];
        if (!device.IsVirtual) return 0;

        // Primary: match by index within virtual devices to VJoyDevices list
        var virtualDevices = _ctx.Devices.Where(d => d.IsVirtual).ToList();
        int virtualIndex = virtualDevices.IndexOf(device);
        if (virtualIndex >= 0 && virtualIndex < _ctx.VJoyDevices.Count)
            return _ctx.VJoyDevices[virtualIndex].Id;

        // Fallback: parse slot number from device name (e.g. "vJoy Device 1" → 1)
        var match = s_vjoySlotNumberRegex.Match(device.Name);
        return match.Success && uint.TryParse(match.Value, out uint id) ? id : 0;
    }

    /// <summary>
    /// Gets the primary physical device mapped to the currently selected vJoy slot.
    /// Returns null if nothing is mapped.
    /// </summary>
    private PhysicalDeviceInfo? GetPrimaryPhysicalDevice(uint vjoyId)
    {
        if (vjoyId == 0) return null;
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return null;
        var primaryGuid = profile.GetPrimaryDeviceForVJoy(vjoyId);
        if (string.IsNullOrEmpty(primaryGuid)) return null;
        return _ctx.Devices.FirstOrDefault(d =>
            !d.IsVirtual && d.InstanceGuid.ToString().Equals(primaryGuid, StringComparison.OrdinalIgnoreCase));
    }

    private (string Label, bool HasPrev, bool HasNext) GetSilhouettePickerState()
    {
        uint vjoyId = GetSelectedVJoyId();
        var maps = _ctx.AvailableDeviceMaps;

        string? currentKey = vjoyId > 0 ? _ctx.AppSettings.GetVJoySilhouetteOverride(vjoyId) : null;

        int currentIndex = string.IsNullOrEmpty(currentKey)
            ? 0
            : maps.FindIndex(m => m.Key == currentKey) + 1;
        if (currentIndex < 0) currentIndex = 0;

        string label = currentIndex == 0
            ? $"Auto ({_ctx.DeviceMap?.Device ?? "none"})"
            : maps[currentIndex - 1].DisplayName;

        bool canNav = vjoyId > 0;
        int total = maps.Count + 1;
        return (label, canNav && currentIndex > 0, canNav && currentIndex < total - 1);
    }

    private void StepSilhouette(int delta)
    {
        uint vjoyId = GetSelectedVJoyId();
        if (vjoyId == 0) return;

        var currentKey = _ctx.AppSettings.GetVJoySilhouetteOverride(vjoyId);
        var maps = _ctx.AvailableDeviceMaps;

        int currentIndex = string.IsNullOrEmpty(currentKey)
            ? 0
            : maps.FindIndex(m => m.Key == currentKey) + 1;
        if (currentIndex < 0) currentIndex = 0;

        int newIndex = Math.Clamp(currentIndex + delta, 0, maps.Count);
        string? newKey = newIndex == 0 ? null : maps[newIndex - 1].Key;

        _ctx.AppSettings.SetVJoySilhouetteOverride(vjoyId, newKey);
        _ctx.LoadDeviceMapForDevice(_ctx.Devices[_ctx.SelectedDevice]);
        _ctx.UpdateMappingsPrimaryDeviceMap();
        _ctx.InvalidateCanvas();
    }

    #endregion

    #region Helpers

    private string GetVJoyAssignmentForDevice(PhysicalDeviceInfo device)
    {
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return "";

        string deviceId = device.InstanceGuid.ToString();

        var vjoyIds = new HashSet<uint>();

        foreach (var mapping in profile.AxisMappings)
        {
            if (mapping.Inputs.Any(i => i.DeviceId == deviceId) && mapping.Output.VJoyDevice > 0)
                vjoyIds.Add(mapping.Output.VJoyDevice);
        }
        foreach (var mapping in profile.ButtonMappings)
        {
            if (mapping.Inputs.Any(i => i.DeviceId == deviceId) && mapping.Output.VJoyDevice > 0)
                vjoyIds.Add(mapping.Output.VJoyDevice);
        }
        foreach (var mapping in profile.HatMappings)
        {
            if (mapping.Inputs.Any(i => i.DeviceId == deviceId) && mapping.Output.VJoyDevice > 0)
                vjoyIds.Add(mapping.Output.VJoyDevice);
        }

        if (vjoyIds.Count == 0) return "";
        if (vjoyIds.Count == 1) return $"VJOY:{vjoyIds.First()}";

        // Find which vJoy slot considers this device its primary (most mappings)
        uint primaryVJoy = 0;
        foreach (var (vjoyId, guid) in profile.VJoyPrimaryDevices)
        {
            if (guid.Equals(deviceId, StringComparison.OrdinalIgnoreCase) && vjoyIds.Contains(vjoyId))
            {
                primaryVJoy = vjoyId;
                break;
            }
        }

        // Primary first, then remaining sorted
        var ordered = primaryVJoy > 0
            ? vjoyIds.Where(id => id == primaryVJoy)
                .Concat(vjoyIds.Where(id => id != primaryVJoy).OrderBy(x => x))
            : vjoyIds.OrderBy(x => x);
        return $"VJOY:{string.Join(",", ordered)}";
    }

    private string GetPrimaryDeviceForVJoyDevice(PhysicalDeviceInfo vjoyDevice)
    {
        if (!vjoyDevice.IsVirtual) return "";

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return "";

        uint vjoyId = 0;
        var match = s_vjoySlotNumberRegex.Match(vjoyDevice.Name);
        if (match.Success && uint.TryParse(match.Value, out uint parsedId))
        {
            vjoyId = parsedId;
        }

        if (vjoyId == 0) return "";

        var primaryGuid = profile.GetPrimaryDeviceForVJoy(vjoyId);
        if (string.IsNullOrEmpty(primaryGuid)) return "";

        var primaryDevice = _ctx.Devices.FirstOrDefault(d =>
            !d.IsVirtual && d.InstanceGuid.ToString().Equals(primaryGuid, StringComparison.OrdinalIgnoreCase));

        if (primaryDevice is null) return "";

        string name = primaryDevice.Name;
        if (name.Length > 20)
            name = string.Concat(name.AsSpan(0, 17), "...");

        return $"\u2192 {name}";
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    // State classes — each groups logically-related fields for one sub-feature.
    // All fields are public so caller code can access them via the instance.
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class DeviceCategoryState
    {
        public int Active;
        public int Hovered = -1;
        public SKRect D1Bounds;
        public SKRect D2Bounds;
    }

    private sealed class DeviceDragState
    {
        public bool IsDragging;
        public int DeviceIndex = -1;
        public int DropTargetIndex = -1;
        public SKPoint StartPoint;
        public SKPoint CurrentPoint;
        public List<SKRect> ItemBounds = new();
    }

    private sealed class DeviceActionsState
    {
        public SKRect Map1to1Bounds;
        public bool Map1to1Hovered;
        public SKRect ClearMappingsBounds;
        public bool ClearMappingsHovered;
        public SKRect RemoveDeviceBounds;
        public bool RemoveDeviceHovered;
        public SKRect HideFromViewBounds;
        public bool HideFromViewHovered;
    }

    private sealed class ForwardingButtonsState
    {
        public SKRect StartBounds;
        public SKRect StopBounds;
        public bool StartHovered;
        public bool StopHovered;
    }

    private sealed class SilhouettePickerState
    {
        public SKRect PrevBounds;
        public SKRect NextBounds;
        public bool PrevHovered;
        public bool NextHovered;
        public SKRect RemoveVJoyBounds;
        public bool RemoveVJoyHovered;
        public SKRect SyncVJoyBounds;
        public bool SyncVJoyHovered;
    }

    private sealed class SvgClickState
    {
        public long LastClickTicks;
        public string? LastControlId;
    }
}
