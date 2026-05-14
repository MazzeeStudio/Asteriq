using System.Text.RegularExpressions;
using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class DevicesTabController
{
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
            y += buttonHeight + 8f;

            // "Manual configuration" pencil link
            const float configFontSize = 12f;
            const string configLabel = "Manual configuration";
            float configTextW = FUIRenderer.MeasureText(configLabel, configFontSize);
            float pencilGap = 14f;
            float configTotalW = pencilGap + configTextW;
            float configX = contentBounds.Right - pad - configTotalW;
            _silhouette.ConfigVJoyBounds = new SKRect(configX - 4f, y, contentBounds.Right - pad + 4f, y + 18f);
            _silhouette.ConfigVJoyHovered = _silhouette.ConfigVJoyBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            var configColor = _silhouette.ConfigVJoyHovered ? FUIColors.TextBright : FUIColors.TextDim;

            // Pencil icon
            float pcx = configX + 6f;
            float pcy = y + 7f;
            using (var penPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = configColor,
                StrokeWidth = 1.2f,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round
            })
            {
                canvas.DrawLine(pcx - 4f, pcy + 4f, pcx + 3f, pcy - 3f, penPaint);
                canvas.DrawLine(pcx - 4f, pcy + 4f, pcx - 5f, pcy + 5.5f, penPaint);
                canvas.DrawLine(pcx + 3f, pcy - 3f, pcx + 5f, pcy - 5f, penPaint);
            }
            FUIRenderer.DrawText(canvas, configLabel, new SKPoint(configX + pencilGap, y + 11f), configColor, configFontSize);
        }
        else
        {
            _actions.Map1to1Bounds = SKRect.Empty;
            _actions.ClearMappingsBounds = SKRect.Empty;
            _actions.RemoveDeviceBounds = SKRect.Empty;
            _actions.HideFromViewBounds = SKRect.Empty;
            _silhouette.RemoveVJoyBounds = SKRect.Empty;
            _silhouette.SyncVJoyBounds = SKRect.Empty;
            _silhouette.ConfigVJoyBounds = SKRect.Empty;
            _silhouette.PrevBounds = SKRect.Empty;
            _silhouette.NextBounds = SKRect.Empty;

            FUIRenderer.DrawText(canvas, "Select a device",
                new SKPoint(contentBounds.Left + pad, y + 20), FUIColors.TextDim, 13f);
            FUIRenderer.DrawText(canvas, "to see available actions",
                new SKPoint(contentBounds.Left + pad, y + 36), FUIColors.TextDim, 13f);
        }
    }

}