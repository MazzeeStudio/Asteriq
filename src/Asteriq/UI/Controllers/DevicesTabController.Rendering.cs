using System.Text.RegularExpressions;
using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class DevicesTabController
{
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
        string categoryName = _devCat.Active == 0 ? "PHYSICAL" : "VIRTUAL";
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
                canvas.Translate(_drag.CurrentPoint.X, _drag.CurrentPoint.Y);
                using var ghostPaint = new SKPaint { Color = SKColors.White.WithAlpha(180) };
                FUIWidgets.DrawDeviceListItem(canvas, contentBounds.Left + pad - 10,
                    _drag.ItemBounds.Count > 0 ? _drag.ItemBounds[0].Top + (_drag.DeviceIndex * (itemHeight + itemGap)) : contentBounds.Top + 50,
                    contentBounds.Width - pad, draggedDevice.Name, status, true, false, assignment);
                canvas.Restore();
            }
        }

        // Bottom row: "Include hidden" checkbox (D1) or "+ Add vJoy device" link (D2)
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
            _addVjoyBounds = SKRect.Empty;
        }
        else
        {
            _showHiddenCheckboxBounds = SKRect.Empty;

            if (_ctx.VJoyService.IsInitialized)
            {
                const float fontSize = 12f;
                const string label = "+ Add vJoy device";
                float textW = FUIRenderer.MeasureText(label, fontSize);
                float lx = contentBounds.Left + pad;
                float ly = bounds.Bottom - pad - 10f;
                _addVjoyBounds = new SKRect(lx, ly - 8f, lx + textW, ly + 8f);
                bool hovered = _addVjoyBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
                var color = hovered ? FUIColors.TextBright : FUIColors.TextDim;
                FUIRenderer.DrawText(canvas, label, new SKPoint(lx, ly + 3f), color, fontSize);
            }
            else
            {
                _addVjoyBounds = SKRect.Empty;
            }
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

}