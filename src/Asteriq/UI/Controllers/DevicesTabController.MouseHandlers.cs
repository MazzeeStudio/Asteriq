using System.Text.RegularExpressions;
using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class DevicesTabController
{
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

    private bool HandleAddVjoyClick(MouseEventArgs e)
    {
        if (_addVjoyBounds.IsEmpty || !_addVjoyBounds.Contains(e.X, e.Y))
            return false;
        AddVJoyDevice();
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

        if (_silhouette.ConfigVJoyBounds.HitTest(e.X, e.Y))
        {
            uint vjoyId = GetSelectedVJoyId();
            if (vjoyId > 0) ConfigureVJoyDevice(vjoyId);
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

    private bool UpdateDragState(MouseEventArgs e)
    {
        if (_devCat.Active != 0 || _drag.DeviceIndex < 0) return false;

        var currentPoint = new SKPoint(e.X, e.Y);
        float dragDistance = SKPoint.Distance(currentPoint, SKPoint.Empty);

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
        _silhouette.ConfigVJoyHovered = _silhouette.ConfigVJoyBounds.HitTest(e.X, e.Y);

        if (_actions.Map1to1Hovered || _actions.ClearMappingsHovered || _actions.RemoveDeviceHovered ||
            _actions.HideFromViewHovered || _silhouette.RemoveVJoyHovered || _silhouette.SyncVJoyHovered ||
            _silhouette.ConfigVJoyHovered)
            _ctx.OwnerForm.Cursor = Cursors.Hand;

        if (_showHiddenCheckboxBounds.HitTest(e.X, e.Y))
            _ctx.OwnerForm.Cursor = Cursors.Hand;

        if (_addVjoyBounds.HitTest(e.X, e.Y))
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

}