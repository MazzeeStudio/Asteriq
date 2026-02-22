using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public class DevicesTabController : ITabController
{
    private readonly TabContext _ctx;

    // Device list hover/selection (local to this tab)
    private int _hoveredDevice = -1;

    // Device category tabs (D1 Physical, D2 Virtual)
    private int _deviceCategory = 0;
    private int _hoveredDeviceCategory = -1;
    private SKRect _deviceCategoryD1Bounds;
    private SKRect _deviceCategoryD2Bounds;

    // Device list drag-to-reorder state
    private bool _isDraggingDevice = false;
    private int _dragDeviceIndex = -1;
    private int _dragDropTargetIndex = -1;
    private SKPoint _dragStartPoint;
    private SKPoint _dragCurrentPoint;
    private List<SKRect> _deviceItemBounds = new();

    // Device Actions panel buttons
    private SKRect _map1to1ButtonBounds;
    private bool _map1to1ButtonHovered;
    private SKRect _clearMappingsButtonBounds;
    private bool _clearMappingsButtonHovered;
    private SKRect _removeDeviceButtonBounds;
    private bool _removeDeviceButtonHovered;

    // Forwarding buttons
    private SKRect _startForwardingButtonBounds;
    private SKRect _stopForwardingButtonBounds;
    private bool _startForwardingButtonHovered;
    private bool _stopForwardingButtonHovered;

    // SVG double-click detection
    private DateTime _lastSvgControlClick = DateTime.MinValue;
    private string? _lastClickedControlId;

    /// <summary>
    /// The active device category (0 = physical, 1 = virtual/vJoy).
    /// </summary>
    public int DeviceCategory => _deviceCategory;

    /// <summary>
    /// True if a device drag-to-reorder is in progress (checked by MainForm for mouse dispatch).
    /// </summary>
    public bool IsDraggingDevice => _isDraggingDevice;

    /// <summary>
    /// True if a drag has been initiated (mouse down on device item, but may not have exceeded threshold yet).
    /// </summary>
    public bool HasPendingDrag => _dragDeviceIndex >= 0;

    public DevicesTabController(TabContext ctx)
    {
        _ctx = ctx;
    }

    public void Draw(SKCanvas canvas, SKRect bounds, float padLeft, float contentTop, float contentBottom)
    {
        // Calculate responsive panel widths (same logic as MainForm.DrawStructureLayer)
        float sideTabPad = FUIRenderer.SpaceSM;
        float pad = FUIRenderer.SpaceXL;
        float contentWidth = bounds.Width - sideTabPad - pad;
        var layout = FUIRenderer.CalculateLayout(contentWidth, minLeftPanel: 360f, minRightPanel: 280f);

        float leftPanelWidth = layout.LeftPanelWidth;
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

        // Right panel: Split into Device Actions (top) and Status (bottom)
        if (layout.ShowRightPanel)
        {
            float rightPanelX = bounds.Right - pad - layout.RightPanelWidth;
            float rightPanelMid = contentTop + (contentBottom - contentTop) / 2f;
            float panelGap = FUIRenderer.SpaceSM;

            var deviceActionsBounds = new SKRect(rightPanelX, contentTop, bounds.Right - pad, rightPanelMid - panelGap / 2f);
            DrawDeviceActionsPanel(canvas, deviceActionsBounds);

            var statusBounds = new SKRect(rightPanelX, rightPanelMid + panelGap / 2f, bounds.Right - pad, contentBottom);
            DrawStatusPanel(canvas, statusBounds);
        }
    }

    public void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        // Device category tab clicks (D1 Physical / D2 Virtual)
        if (_hoveredDeviceCategory >= 0)
        {
            _deviceCategory = _hoveredDeviceCategory;
            _ctx.SelectedDevice = -1;
            _ctx.CurrentInputState = null;
            _ctx.SelectFirstDeviceInCategory?.Invoke();
            return;
        }

        // Device list clicks - initiate potential drag on physical devices
        if (_deviceCategory == 0 && _hoveredDevice >= 0 && _hoveredDevice < _ctx.Devices.Count)
        {
            _ctx.SelectedDevice = _hoveredDevice;
            _ctx.CurrentInputState = null;
            _ctx.LoadDeviceMapForDevice(_ctx.Devices[_ctx.SelectedDevice]);
            _ctx.ActiveInputTracker.Clear();

            // Start potential drag
            _dragDeviceIndex = _hoveredDevice;
            _dragStartPoint = new SKPoint(e.X, e.Y);
            _dragCurrentPoint = _dragStartPoint;
        }
        else if (_hoveredDevice >= 0 && _hoveredDevice < _ctx.Devices.Count)
        {
            // Non-draggable device selection (virtual devices tab)
            _ctx.SelectedDevice = _hoveredDevice;
            _ctx.CurrentInputState = null;
            _ctx.LoadDeviceMapForDevice(_ctx.Devices[_ctx.SelectedDevice]);
            _ctx.ActiveInputTracker.Clear();
        }

        // Device action button clicks
        if (_map1to1ButtonHovered && !_map1to1ButtonBounds.IsEmpty)
        {
            _ctx.CreateOneToOneMappings?.Invoke();
            return;
        }
        if (_clearMappingsButtonHovered && !_clearMappingsButtonBounds.IsEmpty)
        {
            _ctx.ClearDeviceMappings?.Invoke();
            return;
        }
        if (_removeDeviceButtonHovered && !_removeDeviceButtonBounds.IsEmpty)
        {
            _ctx.RemoveDisconnectedDevice?.Invoke();
            return;
        }

        // Forwarding button clicks
        if (_startForwardingButtonHovered && !_startForwardingButtonBounds.IsEmpty)
        {
            StartForwarding();
            return;
        }
        if (_stopForwardingButtonHovered && !_stopForwardingButtonBounds.IsEmpty)
        {
            StopForwarding();
            return;
        }

        // SVG control clicks
        if (_ctx.HoveredControlId is not null)
        {
            bool isDoubleClick = _lastClickedControlId == _ctx.HoveredControlId &&
                                 (DateTime.Now - _lastSvgControlClick).TotalMilliseconds < 500;

            if (isDoubleClick)
            {
                _ctx.OpenMappingDialogForControl?.Invoke(_ctx.HoveredControlId);
                _lastClickedControlId = null;
            }
            else
            {
                _ctx.SelectedControlId = _ctx.HoveredControlId;
                _ctx.LeadLineProgress = 0f;
                _lastClickedControlId = _ctx.HoveredControlId;
                _lastSvgControlClick = DateTime.Now;
            }
        }
        else if (_ctx.SilhouetteBounds.Contains(e.X, e.Y))
        {
            _ctx.SelectedControlId = null;
            _lastClickedControlId = null;
        }
    }

    public void OnMouseMove(MouseEventArgs e)
    {
        // Handle device list drag-to-reorder (physical devices only)
        if (_deviceCategory == 0 && _dragDeviceIndex >= 0)
        {
            var currentPoint = new SKPoint(e.X, e.Y);
            float dragDistance = SKPoint.Distance(currentPoint, _dragStartPoint);

            if (!_isDraggingDevice && dragDistance > 5)
            {
                _isDraggingDevice = true;
                _ctx.OwnerForm.Cursor = Cursors.SizeAll;
            }

            if (_isDraggingDevice)
            {
                _dragCurrentPoint = currentPoint;

                float dragItemHeight = 60f;
                float dragItemGap = FUIRenderer.ItemSpacing;
                float dragContentTop = 90 + 32 + FUIRenderer.PanelPadding;

                var physicalDevices = _ctx.Devices.Where(d => !d.IsVirtual).ToList();

                float relativeY = e.Y - dragContentTop;
                int targetIndex = (int)(relativeY / (dragItemHeight + dragItemGap));
                targetIndex = Math.Clamp(targetIndex, 0, physicalDevices.Count);

                _dragDropTargetIndex = targetIndex;
                _ctx.MarkDirty();
                return;
            }
        }

        // Device category tabs hover detection
        _hoveredDeviceCategory = -1;
        if (_deviceCategoryD1Bounds.Contains(e.X, e.Y))
        {
            _hoveredDeviceCategory = 0;
            _ctx.OwnerForm.Cursor = Cursors.Hand;
        }
        else if (_deviceCategoryD2Bounds.Contains(e.X, e.Y))
        {
            _hoveredDeviceCategory = 1;
            _ctx.OwnerForm.Cursor = Cursors.Hand;
        }

        // Device list hover detection - use same responsive layout as Draw
        float sideTabPad = FUIRenderer.SpaceSM;
        float contentPad = FUIRenderer.SpaceXL;
        float contentTop = 88;
        float contentWidth = _ctx.OwnerForm.ClientSize.Width - sideTabPad - contentPad;
        var layout = FUIRenderer.CalculateLayout(contentWidth, minLeftPanel: 360f, minRightPanel: 280f);
        float leftPanelWidth = layout.LeftPanelWidth;
        float sideTabWidth = 28f;

        if (e.X >= sideTabPad + sideTabWidth && e.X <= sideTabPad + leftPanelWidth)
        {
            float itemY = contentTop + 32 + FUIRenderer.PanelPadding;
            float itemHeight = 60f;
            float itemGap = FUIRenderer.ItemSpacing;

            var filteredDevices = _deviceCategory == 0
                ? _ctx.Devices.Where(d => !d.IsVirtual).ToList()
                : _ctx.Devices.Where(d => d.IsVirtual).ToList();

            int filteredIndex = (int)((e.Y - itemY) / (itemHeight + itemGap));
            if (filteredIndex >= 0 && filteredIndex < filteredDevices.Count)
            {
                _hoveredDevice = _ctx.Devices.IndexOf(filteredDevices[filteredIndex]);
            }
            else
            {
                _hoveredDevice = -1;
            }
        }
        else
        {
            _hoveredDevice = -1;
        }

        // Device action button hover detection
        _map1to1ButtonHovered = !_map1to1ButtonBounds.IsEmpty && _map1to1ButtonBounds.Contains(e.X, e.Y);
        _clearMappingsButtonHovered = !_clearMappingsButtonBounds.IsEmpty && _clearMappingsButtonBounds.Contains(e.X, e.Y);
        _removeDeviceButtonHovered = !_removeDeviceButtonBounds.IsEmpty && _removeDeviceButtonBounds.Contains(e.X, e.Y);

        // Forwarding button hover detection
        _startForwardingButtonHovered = !_startForwardingButtonBounds.IsEmpty && _startForwardingButtonBounds.Contains(e.X, e.Y);
        _stopForwardingButtonHovered = !_stopForwardingButtonBounds.IsEmpty && _stopForwardingButtonBounds.Contains(e.X, e.Y);

        // SVG silhouette hover detection
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
        if (_isDraggingDevice && _dragDeviceIndex >= 0 && _dragDeviceIndex < _ctx.Devices.Count)
        {
            var filteredDevices = _ctx.Devices.Where(d => !d.IsVirtual).ToList();

            var draggedDevice = _ctx.Devices[_dragDeviceIndex];
            int sourceFilteredIndex = filteredDevices.IndexOf(draggedDevice);

            if (sourceFilteredIndex >= 0 && _dragDropTargetIndex >= 0 && _dragDropTargetIndex != sourceFilteredIndex)
            {
                int targetFilteredIndex = _dragDropTargetIndex;
                if (targetFilteredIndex > sourceFilteredIndex)
                    targetFilteredIndex--;

                int targetActualIndex;
                if (targetFilteredIndex >= 0 && targetFilteredIndex < filteredDevices.Count)
                {
                    var targetDevice = filteredDevices[targetFilteredIndex];
                    targetActualIndex = _ctx.Devices.IndexOf(targetDevice);
                    if (_dragDropTargetIndex > sourceFilteredIndex)
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

            _isDraggingDevice = false;
            _dragDeviceIndex = -1;
            _dragDropTargetIndex = -1;
            _ctx.OwnerForm.Cursor = Cursors.Default;
            _ctx.MarkDirty();
            return;
        }

        // Reset potential drag state even if we didn't actually drag
        if (_dragDeviceIndex >= 0)
        {
            _dragDeviceIndex = -1;
            _dragDropTargetIndex = -1;
        }
    }

    public void OnMouseWheel(MouseEventArgs e) { }

    public bool ProcessCmdKey(ref Message msg, Keys keyData) => false;

    public void OnMouseLeave()
    {
        _hoveredDevice = -1;
        _hoveredDeviceCategory = -1;
        _ctx.HoveredControlId = null;
    }

    public void OnTick() { }

    public void OnActivated() { }

    public void OnDeactivated() { }

    #region Drawing

    private void DrawDeviceListPanel(SKCanvas canvas, SKRect bounds)
    {
        float pad = FUIRenderer.PanelPadding;
        float itemGap = FUIRenderer.ItemSpacing;
        float frameInset = 5f;

        float sideTabWidth = 28f;

        FUIRenderer.DrawPanelShadow(canvas, bounds, 3f, 3f, 10f);

        var contentBounds = new SKRect(bounds.Left + frameInset + sideTabWidth, bounds.Top + frameInset,
                                        bounds.Right - frameInset, bounds.Bottom - frameInset);
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(140),
            IsAntialias = true
        };
        canvas.DrawRect(contentBounds, bgPaint);

        DrawDeviceCategorySideTabs(canvas, bounds.Left + frameInset, bounds.Top + frameInset,
            sideTabWidth, bounds.Height - frameInset * 2);

        bool panelHovered = _hoveredDevice >= 0;
        var frameBounds = new SKRect(bounds.Left + sideTabWidth, bounds.Top, bounds.Right, bounds.Bottom);
        FUIRenderer.DrawLCornerFrame(canvas, frameBounds,
            panelHovered ? FUIColors.FrameBright : FUIColors.Frame, 40f, 10f, 1.5f, panelHovered);

        float titleBarHeight = 32f;
        var titleBounds = new SKRect(contentBounds.Left, contentBounds.Top, contentBounds.Right, contentBounds.Top + titleBarHeight);
        string categoryCode = _deviceCategory == 0 ? "D1" : "D2";
        string categoryName = _deviceCategory == 0 ? "DEVICES" : "DEVICES";
        FUIRenderer.DrawPanelTitle(canvas, titleBounds, categoryCode, categoryName);

        var filteredDevices = _deviceCategory == 0
            ? _ctx.Devices.Where(d => !d.IsVirtual).ToList()
            : _ctx.Devices.Where(d => d.IsVirtual).ToList();

        float itemY = contentBounds.Top + titleBarHeight + pad;
        float itemHeight = 60f;

        if (filteredDevices.Count == 0)
        {
            string emptyMsg = _deviceCategory == 0
                ? "No physical devices detected"
                : "No virtual devices detected";
            string helpMsg = _deviceCategory == 0
                ? "Connect a joystick or gamepad"
                : "Install vJoy or start a virtual device";
            FUIRenderer.DrawText(canvas, emptyMsg,
                new SKPoint(contentBounds.Left + pad, itemY + 20), FUIColors.TextDim, 12f);
            FUIRenderer.DrawText(canvas, helpMsg,
                new SKPoint(contentBounds.Left + pad, itemY + 38), FUIColors.TextDisabled, 10f);
        }
        else
        {
            _deviceItemBounds.Clear();

            for (int i = 0; i < filteredDevices.Count && itemY + itemHeight < contentBounds.Bottom - 40; i++)
            {
                int actualIndex = _ctx.Devices.IndexOf(filteredDevices[i]);

                var itemBounds = new SKRect(contentBounds.Left + pad - 10, itemY,
                    contentBounds.Left + pad - 10 + contentBounds.Width - pad, itemY + itemHeight);
                _deviceItemBounds.Add(itemBounds);

                if (_isDraggingDevice && actualIndex == _dragDeviceIndex)
                {
                    itemY += itemHeight + itemGap;
                    continue;
                }

                if (_isDraggingDevice && i == _dragDropTargetIndex)
                {
                    using var dropPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Stroke,
                        Color = FUIColors.Active,
                        StrokeWidth = 2f,
                        IsAntialias = true
                    };
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

            if (_isDraggingDevice && _dragDeviceIndex >= 0 && _dragDeviceIndex < _ctx.Devices.Count)
            {
                var draggedDevice = _ctx.Devices[_dragDeviceIndex];
                string status = draggedDevice.IsConnected ? "ONLINE" : "DISCONNECTED";
                string assignment = draggedDevice.IsVirtual
                    ? GetPrimaryDeviceForVJoyDevice(draggedDevice)
                    : GetVJoyAssignmentForDevice(draggedDevice);

                canvas.Save();
                canvas.Translate(_dragCurrentPoint.X - _dragStartPoint.X, _dragCurrentPoint.Y - _dragStartPoint.Y);
                using var ghostPaint = new SKPaint { Color = SKColors.White.WithAlpha(180) };
                FUIWidgets.DrawDeviceListItem(canvas, contentBounds.Left + pad - 10,
                    _deviceItemBounds.Count > 0 ? _deviceItemBounds[0].Top + (_dragDeviceIndex * (itemHeight + itemGap)) : contentBounds.Top + 50,
                    contentBounds.Width - pad, draggedDevice.Name, status, true, false, assignment);
                canvas.Restore();
            }
        }

        float promptY = bounds.Bottom - pad - 20;
        FUIRenderer.DrawText(canvas, "+ SCAN FOR DEVICES",
            new SKPoint(contentBounds.Left + pad, promptY), FUIColors.TextDim, 12f);

        using var bracketPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.FrameDim,
            StrokeWidth = 1f
        };
        canvas.DrawLine(contentBounds.Left + pad - 20, promptY - 10, contentBounds.Left + pad - 20, promptY + 5, bracketPaint);
        canvas.DrawLine(contentBounds.Left + pad - 20, promptY - 10, contentBounds.Left + pad - 8, promptY - 10, bracketPaint);
    }

    private void DrawDeviceCategorySideTabs(SKCanvas canvas, float x, float y, float width, float height)
    {
        float tabHeight = 80f;
        float tabGap = 4f;

        float totalTabsHeight = tabHeight * 2 + tabGap;
        float startY = y + height - totalTabsHeight - 10f;

        var d1Bounds = new SKRect(x, startY + tabHeight + tabGap, x + width, startY + tabHeight * 2 + tabGap);
        _deviceCategoryD1Bounds = d1Bounds;
        FUIWidgets.DrawVerticalSideTab(canvas, d1Bounds, "DEVICES_01", _deviceCategory == 0, _hoveredDeviceCategory == 0);

        var d2Bounds = new SKRect(x, startY, x + width, startY + tabHeight);
        _deviceCategoryD2Bounds = d2Bounds;
        FUIWidgets.DrawVerticalSideTab(canvas, d2Bounds, "DEVICES_02", _deviceCategory == 1, _hoveredDeviceCategory == 1);
    }

    private void DrawDeviceDetailsPanel(SKCanvas canvas, SKRect bounds)
    {
        float pad = FUIRenderer.PanelPadding;
        float frameInset = 5f;

        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(100),
            IsAntialias = true
        };
        canvas.DrawRect(new SKRect(bounds.Left + frameInset, bounds.Top + frameInset,
            bounds.Right - frameInset, bounds.Bottom - frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame.WithAlpha(150), 30f, 8f);

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
        if (_ctx.JoystickSvg?.Picture is null)
            return new SKPoint(viewBoxX, viewBoxY);

        float screenX, screenY;

        if (_ctx.SvgMirrored)
        {
            var svgBounds = _ctx.JoystickSvg.Picture.CullRect;
            float scaledWidth = svgBounds.Width * _ctx.SvgScale;
            screenX = _ctx.SvgOffset.X + scaledWidth - viewBoxX * _ctx.SvgScale;
        }
        else
        {
            screenX = _ctx.SvgOffset.X + viewBoxX * _ctx.SvgScale;
        }

        screenY = _ctx.SvgOffset.Y + viewBoxY * _ctx.SvgScale;
        return new SKPoint(screenX, screenY);
    }

    private void DrawDeviceSilhouette(SKCanvas canvas, SKRect bounds)
    {
        var activeSvg = _ctx.GetActiveSvg?.Invoke();
        if (activeSvg?.Picture is not null)
        {
            bool mirror = _ctx.DeviceMap?.Mirror ?? false;
            DrawSvgInBounds(canvas, activeSvg, bounds, mirror);
        }
        else
        {
            FUIWidgets.DrawJoystickOutlineFallback(canvas, bounds);
        }
    }

    private void DrawSvgInBounds(SKCanvas canvas, SKSvg svg, SKRect bounds, bool mirror = false)
    {
        if (svg.Picture is null) return;

        var svgBounds = svg.Picture.CullRect;
        if (svgBounds.Width <= 0 || svgBounds.Height <= 0) return;

        float scaleX = bounds.Width / svgBounds.Width;
        float scaleY = bounds.Height / svgBounds.Height;
        float scale = Math.Min(scaleX, scaleY) * 0.95f;

        float scaledWidth = svgBounds.Width * scale;
        float scaledHeight = svgBounds.Height * scale;

        float offsetX = bounds.Left + (bounds.Width - scaledWidth) / 2 - svgBounds.Left * scale;
        float offsetY = bounds.Top + (bounds.Height - scaledHeight) / 2 - svgBounds.Top * scale;

        _ctx.SvgScale = scale;
        _ctx.SvgOffset = new SKPoint(offsetX, offsetY);
        _ctx.SvgMirrored = mirror;

        canvas.Save();
        canvas.Translate(offsetX, offsetY);

        if (mirror)
        {
            canvas.Translate(scaledWidth, 0);
            canvas.Scale(-scale, scale);
        }
        else
        {
            canvas.Scale(scale);
        }

        canvas.DrawPicture(svg.Picture);
        canvas.Restore();
    }

    private void DrawDeviceActionsPanel(SKCanvas canvas, SKRect bounds)
    {
        float pad = FUIRenderer.PanelPadding;
        float frameInset = 5f;

        FUIRenderer.DrawPanelShadow(canvas, bounds, 3f, 3f, 10f);

        var contentBounds = new SKRect(bounds.Left + frameInset, bounds.Top + frameInset,
                                        bounds.Right - frameInset, bounds.Bottom - frameInset);
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(140),
            IsAntialias = true
        };
        canvas.DrawRect(contentBounds, bgPaint);

        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame, 35f, 10f);

        float titleBarHeight = 32f;
        var titleBounds = new SKRect(contentBounds.Left, contentBounds.Top, contentBounds.Right, contentBounds.Top + titleBarHeight);
        FUIRenderer.DrawPanelTitle(canvas, titleBounds, "D3", "DEVICE ACTIONS");

        float y = contentBounds.Top + titleBarHeight + pad;
        float buttonHeight = 32f;
        float buttonGap = 8f;

        bool hasPhysicalDevice = _deviceCategory == 0 && _ctx.SelectedDevice >= 0 &&
                                  _ctx.SelectedDevice < _ctx.Devices.Count && !_ctx.Devices[_ctx.SelectedDevice].IsVirtual;

        if (hasPhysicalDevice)
        {
            var device = _ctx.Devices[_ctx.SelectedDevice];
            bool isDisconnected = !device.IsConnected;

            FUIRenderer.DrawText(canvas, isDisconnected ? "DISCONNECTED DEVICE" : "SELECTED DEVICE",
                new SKPoint(contentBounds.Left + pad, y), isDisconnected ? FUIColors.Danger : FUIColors.TextDim, 9f);
            y += 16f;

            string shortName = device.Name.Length > 22 ? device.Name.Substring(0, 20) + "..." : device.Name;
            FUIRenderer.DrawText(canvas, shortName, new SKPoint(contentBounds.Left + pad, y),
                isDisconnected ? FUIColors.TextDim : FUIColors.TextPrimary, 11f);
            y += 20f;

            FUIRenderer.DrawText(canvas, $"{device.AxisCount} axes  {device.ButtonCount} btns  {device.HatCount} hats",
                new SKPoint(contentBounds.Left + pad, y), FUIColors.TextDim, 9f);
            y += 24f;

            float buttonWidth = contentBounds.Width - pad * 2;

            if (isDisconnected)
            {
                _map1to1ButtonBounds = SKRect.Empty;

                _clearMappingsButtonBounds = new SKRect(contentBounds.Left + pad, y, contentBounds.Left + pad + buttonWidth, y + buttonHeight);
                var clearState = _clearMappingsButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
                FUIRenderer.DrawButton(canvas, _clearMappingsButtonBounds, "CLEAR MAPPINGS", clearState, FUIColors.Danger);
                y += buttonHeight + buttonGap;

                _removeDeviceButtonBounds = new SKRect(contentBounds.Left + pad, y, contentBounds.Left + pad + buttonWidth, y + buttonHeight);
                var removeState = _removeDeviceButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
                FUIRenderer.DrawButton(canvas, _removeDeviceButtonBounds, "REMOVE DEVICE", removeState, FUIColors.Danger);
            }
            else
            {
                _removeDeviceButtonBounds = SKRect.Empty;

                _map1to1ButtonBounds = new SKRect(contentBounds.Left + pad, y, contentBounds.Left + pad + buttonWidth, y + buttonHeight);
                var mapState = _map1to1ButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
                FUIRenderer.DrawButton(canvas, _map1to1ButtonBounds, "MAP 1:1 TO VJOY", mapState);
                y += buttonHeight + buttonGap;

                _clearMappingsButtonBounds = new SKRect(contentBounds.Left + pad, y, contentBounds.Left + pad + buttonWidth, y + buttonHeight);
                var clearState2 = _clearMappingsButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
                FUIRenderer.DrawButton(canvas, _clearMappingsButtonBounds, "CLEAR MAPPINGS", clearState2, FUIColors.Danger);
            }
        }
        else
        {
            _map1to1ButtonBounds = SKRect.Empty;
            _clearMappingsButtonBounds = SKRect.Empty;
            _removeDeviceButtonBounds = SKRect.Empty;

            FUIRenderer.DrawText(canvas, "Select a physical device",
                new SKPoint(contentBounds.Left + pad, y + 20), FUIColors.TextDim, 10f);
            FUIRenderer.DrawText(canvas, "to see available actions",
                new SKPoint(contentBounds.Left + pad, y + 36), FUIColors.TextDim, 10f);
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
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(140),
            IsAntialias = true
        };
        canvas.DrawRect(contentBounds, bgPaint);

        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame, 35f, 10f);

        float titleBarHeight = 32f;
        var titleBounds = new SKRect(contentBounds.Left, contentBounds.Top, contentBounds.Right, contentBounds.Top + titleBarHeight);
        FUIRenderer.DrawPanelTitle(canvas, titleBounds, "S1", "STATUS");

        float statusItemHeight = 32f;
        float itemY = contentBounds.Top + titleBarHeight + pad;

        bool vjoyActive = _ctx.VJoyService.IsInitialized;
        FUIWidgets.DrawStatusItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2, "VJOY DRIVER",
            vjoyActive ? "ACTIVE" : "NOT FOUND", vjoyActive ? FUIColors.Active : FUIColors.Danger);
        itemY += statusItemHeight + itemGap;

        FUIWidgets.DrawStatusItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2, "FORWARDING",
            _ctx.IsForwarding ? "RUNNING" : "STOPPED", _ctx.IsForwarding ? FUIColors.Active : FUIColors.TextDim);
        itemY += statusItemHeight + itemGap;

        int pollHz = _ctx.InputService.PollRateHz;
        string pollRateText = pollHz > 0 ? $"{pollHz} HZ" : "—";
        FUIWidgets.DrawStatusItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2, "POLL RATE", pollRateText, FUIColors.TextPrimary);
        itemY += statusItemHeight + itemGap;

        string profileName = _ctx.MappingEngine.ActiveProfile?.Name ?? "NONE";
        FUIWidgets.DrawStatusItem(canvas, bounds.Left + pad, itemY, bounds.Width - pad * 2, "PROFILE", profileName.ToUpper(), FUIColors.TextPrimary);

        float buttonHeight = 36f;
        float buttonWidth = contentBounds.Width - pad * 2;
        float buttonY = contentBounds.Bottom - buttonHeight - pad;

        if (_ctx.IsForwarding)
        {
            _stopForwardingButtonBounds = new SKRect(contentBounds.Left + pad, buttonY,
                contentBounds.Left + pad + buttonWidth, buttonY + buttonHeight);
            _startForwardingButtonBounds = SKRect.Empty;
            FUIWidgets.DrawForwardingButton(canvas, _stopForwardingButtonBounds, "STOP FORWARDING",
                _stopForwardingButtonHovered, isStop: true);
        }
        else
        {
            _startForwardingButtonBounds = new SKRect(contentBounds.Left + pad, buttonY,
                contentBounds.Left + pad + buttonWidth, buttonY + buttonHeight);
            _stopForwardingButtonBounds = SKRect.Empty;
            FUIWidgets.DrawForwardingButton(canvas, _startForwardingButtonBounds, "START FORWARDING",
                _startForwardingButtonHovered, isStop: false);
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
                "No active profile found.\n\nTo create mappings:\n1. Select a physical device\n2. Click 'MAP 1:1 TO VJOY'",
                "Cannot Start Forwarding");
            return;
        }

        if (profile.AxisMappings.Count == 0 && profile.ButtonMappings.Count == 0 && profile.HatMappings.Count == 0)
        {
            FUIMessageBox.ShowWarning(_ctx.OwnerForm,
                $"Profile '{profile.Name}' has no mappings.\n\nTo create mappings:\n1. Select a physical device\n2. Click 'MAP 1:1 TO VJOY'",
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
        return $"VJOY:{string.Join(",", vjoyIds.OrderBy(x => x))}";
    }

    private string GetPrimaryDeviceForVJoyDevice(PhysicalDeviceInfo vjoyDevice)
    {
        if (!vjoyDevice.IsVirtual) return "";

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return "";

        uint vjoyId = 0;
        var match = System.Text.RegularExpressions.Regex.Match(vjoyDevice.Name, @"\d+");
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
            name = name.Substring(0, 17) + "...";

        return $"\u2192 {name}";
    }

    #endregion
}
