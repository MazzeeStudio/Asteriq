using System.Text.RegularExpressions;
using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class DevicesTabController : ITabController
{
    private readonly TabContext _ctx;

    // Cached regex for parsing vJoy slot numbers from device names
    private static readonly Regex s_vjoySlotNumberRegex = new(@"\d+", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    // Device list hover/selection (local to this tab)
    private int _hoveredDevice = -1;

    // D1 filter state (persisted across sessions)
    private bool _showHiddenDevices;
    private SKRect _showHiddenCheckboxBounds;

    // D2 "Add vJoy device" link-button
    private SKRect _addVjoyBounds;

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

    // Pending hide — guid of device waiting to be hidden after visual delay
    private string? _pendingHideGuid;
    private long _pendingHideTicks;

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
        if (HandleAddVjoyClick(e)) return;
        if (HandleSilhouetteClick(e)) return;
        if (HandleVJoyActionClick(e)) return;
        if (HandleForwardingClick(e)) return;
        if (HandleDeviceListClick()) return;
        HandleSvgClick(e);
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


    public void ToggleHidHideForDevicePublic(PhysicalDeviceInfo device) => ToggleHidHideForDevice(device);

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
        public SKRect ConfigVJoyBounds;
        public bool ConfigVJoyHovered;
    }

    private sealed class SvgClickState
    {
        public long LastClickTicks;
        public string? LastControlId;
    }
}