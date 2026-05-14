using System.Reflection;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using Serilog;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public sealed partial class SettingsTabController
{
    private void DrawSystemSettingsSubPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        var m = FUIRenderer.DrawPanelChrome(canvas, bounds);
        float y = m.Y;
        float leftMargin = m.LeftMargin;
        float rightMargin = m.RightMargin;
        float contentWidth = m.ContentWidth;
        float sectionSpacing = 20f;
        float rowHeight = 24f;
        float minControlGap = 12f;

        FUIRenderer.DrawText(canvas, "SYSTEM", new SKPoint(leftMargin, y), FUIColors.TextBright, FUIRenderer.FontBody, true);
        y += 32f;

        // Auto-load setting
        float toggleWidth = 48f;
        float toggleHeight = 24f;
        float autoLoadLabelMaxWidth = contentWidth - toggleWidth - minControlGap;
        float autoLoadLabelY = y + (rowHeight - 11f) / 2 + 11f - 3;
        FUIRenderer.DrawTextTruncated(canvas, "Auto-load configuration", new SKPoint(leftMargin, autoLoadLabelY),
            autoLoadLabelMaxWidth, FUIColors.TextPrimary, 14f);
        float toggleY = y + (rowHeight - toggleHeight) / 2;
        _autoLoadToggleBounds = new SKRect(rightMargin - toggleWidth, toggleY, rightMargin, toggleY + toggleHeight);
        FUIWidgets.DrawToggleSwitch(canvas, _autoLoadToggleBounds, _autoLoadT.T, _ctx.MousePosition);
        y += rowHeight + sectionSpacing;

        // Close to Tray toggle
        float closeToTrayLabelMaxWidth = contentWidth - toggleWidth - minControlGap;
        float closeToTrayLabelY = y + (rowHeight - 11f) / 2 + 11f - 3;
        FUIRenderer.DrawTextTruncated(canvas, "Close to tray", new SKPoint(leftMargin, closeToTrayLabelY),
            closeToTrayLabelMaxWidth, FUIColors.TextPrimary, 14f);
        float closeToTrayToggleY = y + (rowHeight - toggleHeight) / 2;
        _closeToTrayToggleBounds = new SKRect(rightMargin - toggleWidth, closeToTrayToggleY, rightMargin, closeToTrayToggleY + toggleHeight);
        FUIWidgets.DrawToggleSwitch(canvas, _closeToTrayToggleBounds, _closeToTrayT.T, _ctx.MousePosition);
        y += rowHeight + sectionSpacing;

        // Client Only Mode toggle
        float clientOnlyLabelMaxWidth = contentWidth - toggleWidth - minControlGap;
        float clientOnlyLabelY = y + (rowHeight - 11f) / 2 + 11f - 3;
        FUIRenderer.DrawTextTruncated(canvas, "Client Only Mode", new SKPoint(leftMargin, clientOnlyLabelY),
            clientOnlyLabelMaxWidth, FUIColors.TextPrimary, 14f);
        float clientOnlyToggleY = y + (rowHeight - toggleHeight) / 2;
        _clientOnlyToggleBounds = new SKRect(rightMargin - toggleWidth, clientOnlyToggleY, rightMargin, clientOnlyToggleY + toggleHeight);
        FUIWidgets.DrawToggleSwitch(canvas, _clientOnlyToggleBounds, _clientOnlyT.T, _ctx.MousePosition);
        y += rowHeight + 4;

        // STARTUP section
        FUIWidgets.DrawSectionLabel(canvas, "STARTUP", leftMargin, ref y);

        // Launch on Windows startup
        float launchLabelMaxWidth = contentWidth - toggleWidth - minControlGap;
        float launchLabelY = y + (rowHeight - 11f) / 2 + 11f - 3;
        FUIRenderer.DrawTextTruncated(canvas, "Launch on Windows startup", new SKPoint(leftMargin, launchLabelY),
            launchLabelMaxWidth, FUIColors.TextPrimary, 14f);
        float launchToggleY = y + (rowHeight - toggleHeight) / 2;
        _launchOnStartToggleBounds = new SKRect(rightMargin - toggleWidth, launchToggleY, rightMargin, launchToggleY + toggleHeight);
        FUIWidgets.DrawToggleSwitch(canvas, _launchOnStartToggleBounds, _launchOnStartT.T, _ctx.MousePosition);
        y += rowHeight + sectionSpacing;

        // Start forwarding on startup
        float autoFwdLabelMaxWidth = contentWidth - toggleWidth - minControlGap;
        float autoFwdLabelY = y + (rowHeight - 11f) / 2 + 11f - 3;
        FUIRenderer.DrawTextTruncated(canvas, "Start forwarding on startup", new SKPoint(leftMargin, autoFwdLabelY),
            autoFwdLabelMaxWidth, FUIColors.TextPrimary, 14f);
        float autoFwdToggleY = y + (rowHeight - toggleHeight) / 2;
        _autoStartFwdToggleBounds = new SKRect(rightMargin - toggleWidth, autoFwdToggleY, rightMargin, autoFwdToggleY + toggleHeight);
        FUIWidgets.DrawToggleSwitch(canvas, _autoStartFwdToggleBounds, _autoStartFwdT.T, _ctx.MousePosition);
        y += rowHeight + sectionSpacing;

        // Open minimized
        float openMinLabelMaxWidth = contentWidth - toggleWidth - minControlGap;
        float openMinLabelY = y + (rowHeight - 11f) / 2 + 11f - 3;
        FUIRenderer.DrawTextTruncated(canvas, "Open minimised", new SKPoint(leftMargin, openMinLabelY),
            openMinLabelMaxWidth, FUIColors.TextPrimary, 14f);
        float openMinToggleY = y + (rowHeight - toggleHeight) / 2;
        _openMinimizedToggleBounds = new SKRect(rightMargin - toggleWidth, openMinToggleY, rightMargin, openMinToggleY + toggleHeight);
        FUIWidgets.DrawToggleSwitch(canvas, _openMinimizedToggleBounds, _openMinimizedT.T, _ctx.MousePosition);
        y += rowHeight + 4;

        // DRIVERS section
        FUIWidgets.DrawSectionLabel(canvas, "DRIVERS", leftMargin, ref y);

        var vjoyDevices = _ctx.VJoyService.EnumerateDevices();
        bool vjoyEnabled = vjoyDevices.Count > 0;
        var driverStatus = _ctx.DriverSetupManager.GetSetupStatus();

        float statusDotRadius = 4f;
        float statusLineHeight = 21f;
        float statusTextX = leftMargin + statusDotRadius * 2 + 8;

        // vJoy status row
        {
            string vjoyStatusStr = vjoyEnabled ? "Driver active" : "Not installed";
            var vjoyColor = vjoyEnabled ? FUIColors.Success : FUIColors.Danger;
            float dotY = y + (statusLineHeight / 2);
            float textY = y + (statusLineHeight / 2) + 4;

            using var vjoyDot = FUIRenderer.CreateFillPaint(vjoyColor);
            canvas.DrawCircle(leftMargin + statusDotRadius + 1, dotY, statusDotRadius, vjoyDot);
            FUIRenderer.DrawText(canvas, "vJoy", new SKPoint(statusTextX, textY), FUIColors.TextPrimary, 14f);
            float vjoyLabelW = FUIRenderer.MeasureText("vJoy", 14f);
            FUIRenderer.DrawText(canvas, vjoyStatusStr, new SKPoint(statusTextX + vjoyLabelW + 12f, textY),
                vjoyEnabled ? FUIColors.TextDim : FUIColors.Danger, 13f);
            y += statusLineHeight;
        }

        // HidHide status row + DRIVER SETUP button
        {
            string hidHideStatusStr = driverStatus.HidHideInstalled ? "Driver active" : "Not installed";
            var hidHideColor = driverStatus.HidHideInstalled ? FUIColors.Success : FUIColors.Warning;
            float dotY = y + (statusLineHeight / 2);
            float textY = y + (statusLineHeight / 2) + 4;

            using var hidHideDot = FUIRenderer.CreateFillPaint(hidHideColor);
            canvas.DrawCircle(leftMargin + statusDotRadius + 1, dotY, statusDotRadius, hidHideDot);
            FUIRenderer.DrawText(canvas, "HidHide", new SKPoint(statusTextX, textY), FUIColors.TextPrimary, 14f);
            float hidHideLabelW = FUIRenderer.MeasureText("HidHide", 14f);
            FUIRenderer.DrawText(canvas, hidHideStatusStr, new SKPoint(statusTextX + hidHideLabelW + 12f, textY),
                driverStatus.HidHideInstalled ? FUIColors.TextDim : FUIColors.Warning, 13f);

            // DRIVER SETUP button — right-aligned on the HidHide row
            float setupBtnWidth = 110f;
            float setupBtnHeight = 22f;
            float setupBtnY = y + (statusLineHeight - setupBtnHeight) / 2;
            _driverSetupButtonBounds = new SKRect(rightMargin - setupBtnWidth, setupBtnY, rightMargin, setupBtnY + setupBtnHeight);
            bool setupHovered = _driverSetupButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            FUIRenderer.DrawButton(canvas, _driverSetupButtonBounds, "DRIVER SETUP",
                setupHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
            y += statusLineHeight;
        }

        // Available devices line (only when vJoy is active)
        if (vjoyEnabled)
        {
            y += 4f; // extra spacing from the status dot rows
            FUIRenderer.DrawTextTruncated(canvas, $"Available devices: {vjoyDevices.Count}",
                new SKPoint(leftMargin, y + 12f), contentWidth, FUIColors.TextDim, 13f);
            y += rowHeight;
        }
        y += 4;

        // NETWORK enable toggle — only shown when vJoy is installed
        if (driverStatus.IsComplete)
        {
            FUIWidgets.DrawSectionLabel(canvas, "NETWORK", leftMargin, ref y);

            float netLabelMaxWidth = contentWidth - toggleWidth - minControlGap;
            float netLabelY = y + (rowHeight - 11f) / 2 + 11f - 3;
            FUIRenderer.DrawTextTruncated(canvas, "Enable network forwarding", new SKPoint(leftMargin, netLabelY),
                netLabelMaxWidth, FUIColors.TextPrimary, 14f);
            float netToggleY = y + (rowHeight - toggleHeight) / 2;
            _networkEnabledToggleBounds = new SKRect(rightMargin - toggleWidth, netToggleY, rightMargin, netToggleY + toggleHeight);
            FUIWidgets.DrawToggleSwitch(canvas, _networkEnabledToggleBounds, _networkEnabledT.T, _ctx.MousePosition);
            y += rowHeight + 4;
        }
        else
        {
            _networkEnabledToggleBounds = SKRect.Empty;
        }

        // VERSION & UPDATES section
        FUIWidgets.DrawSectionLabel(canvas, "VERSION & UPDATES", leftMargin, ref y);

        // "Check for updates automatically" toggle — first in this section
        float autoCheckLabelMaxWidth = contentWidth - toggleWidth - minControlGap;
        float autoCheckLabelY = y + (rowHeight - 11f) / 2 + 11f - 3;
        FUIRenderer.DrawTextTruncated(canvas, "Check for updates automatically", new SKPoint(leftMargin, autoCheckLabelY),
            autoCheckLabelMaxWidth, FUIColors.TextPrimary, 14f);
        float autoCheckToggleY = y + (rowHeight - toggleHeight) / 2;
        _checkUpdatesToggleBounds = new SKRect(rightMargin - toggleWidth, autoCheckToggleY, rightMargin, autoCheckToggleY + toggleHeight);
        FUIWidgets.DrawToggleSwitch(canvas, _checkUpdatesToggleBounds, _checkUpdatesT.T, _ctx.MousePosition);
        y += rowHeight + 8;

        // Update channel selector: Stable / Nightly
        float channelLabelMaxWidth = contentWidth - 160f - minControlGap;
        float channelLabelY = y + (rowHeight - 11f) / 2 + 11f - 3;
        FUIRenderer.DrawTextTruncated(canvas, "Update channel", new SKPoint(leftMargin, channelLabelY),
            channelLabelMaxWidth, FUIColors.TextPrimary, 14f);
        float segWidth = 160f;
        float segHeight = 22f;
        float segY = y + (rowHeight - segHeight) / 2;
        var segBounds = new SKRect(rightMargin - segWidth, segY, rightMargin, segY + segHeight);
        int channelIndex = _ctx.AppSettings.UpdateChannel == UpdateChannel.Nightly ? 1 : 0;
        int channelHovered = -1;
        if (segBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y))
        {
            float segEach = segBounds.Width / UpdateChannelLabels.Length;
            channelHovered = (int)((_ctx.MousePosition.X - segBounds.Left) / segEach);
        }
        _updateChannelSegBounds = FUIWidgets.DrawSegmentedControl(canvas, segBounds, UpdateChannelLabels,
            channelIndex, channelHovered);
        y += rowHeight + sectionSpacing;

        // Version row: "v0.8.289" left, [Check for updates] button right
        string currentVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        string versionStr = $"v{currentVersion}";

        var updateStatus = _ctx.UpdateService.Status;
        float checkBtnWidth = 150f;
        float checkBtnHeight = 28f;
        bool checkBtnEnabled = updateStatus is UpdateStatus.Unknown or UpdateStatus.UpToDate or UpdateStatus.Error;
        _checkButtonBounds = new SKRect(rightMargin - checkBtnWidth, y, rightMargin, y + checkBtnHeight);
        bool checkHovered = checkBtnEnabled && _checkButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        string checkLabel = updateStatus == UpdateStatus.Checking ? "CHECKING\u2026" : "CHECK FOR UPDATES";
        FUIRenderer.DrawButton(canvas, _checkButtonBounds, checkLabel,
            (!checkBtnEnabled || updateStatus == UpdateStatus.Checking) ? FUIRenderer.ButtonState.Disabled
            : checkHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

        // Version text — vertically aligned with the button
        float versionTextY = y + (checkBtnHeight - 11f) / 2 + 11f - 3;
        FUIRenderer.DrawText(canvas, versionStr, new SKPoint(leftMargin, versionTextY), FUIColors.TextPrimary, 14f);
        y += checkBtnHeight + 4f;

        // "Last checked" timestamp
        var lastChecked = _ctx.UpdateService.LastChecked;
        string lastCheckedStr = lastChecked.HasValue
            ? $"Last checked: {lastChecked.Value:dd/MM/yyyy HH:mm}"
            : "Last checked: never";
        FUIRenderer.DrawText(canvas, lastCheckedStr, new SKPoint(leftMargin, y + 10f), FUIColors.TextDim, 12f);
        y += 20f;

        // Status banner — contextual based on update status
        _downloadButtonBounds = SKRect.Empty;
        _applyButtonBounds = SKRect.Empty;

        float bannerHeight = 32f;
        float bannerRadius = 4f;
        string latest = _ctx.UpdateService.LatestVersion ?? "?";

        if (updateStatus != UpdateStatus.Unknown && updateStatus != UpdateStatus.Checking)
        {
            var bannerRect = new SKRect(leftMargin, y, rightMargin, y + bannerHeight);
            float dotRadius = 4f;
            float dotX = leftMargin + 12f;
            float dotY = y + bannerHeight / 2f;
            float textStartX = dotX + dotRadius + 8f;
            float textY = y + bannerHeight / 2f + 4f;

            switch (updateStatus)
            {
                case UpdateStatus.UpToDate:
                {
                    FUIRenderer.DrawRoundedPanel(canvas, bannerRect, FUIColors.Active.WithAlpha(25), FUIColors.Active.WithAlpha(50), bannerRadius);
                    using var dotPaint = FUIRenderer.CreateFillPaint(FUIColors.Active);
                    canvas.DrawCircle(dotX, dotY, dotRadius, dotPaint);
                    FUIRenderer.DrawText(canvas, "Asteriq is up to date", new SKPoint(textStartX, textY), FUIColors.TextPrimary, 13f);
                    break;
                }

                case UpdateStatus.UpdateAvailable:
                {
                    FUIRenderer.DrawRoundedPanel(canvas, bannerRect, FUIColors.Active.WithAlpha(25), FUIColors.Active.WithAlpha(50), bannerRadius);
                    using var dotPaint = FUIRenderer.CreateFillPaint(FUIColors.Active);
                    canvas.DrawCircle(dotX, dotY, dotRadius, dotPaint);
                    string availText = $"Update available: v{latest}";
                    FUIRenderer.DrawText(canvas, availText, new SKPoint(textStartX, textY), FUIColors.TextPrimary, 13f);

                    // Inline "Download update" button
                    float dlBtnWidth = FUIRenderer.MeasureText("DOWNLOAD", 13f) + 24f;
                    _downloadButtonBounds = new SKRect(rightMargin - dlBtnWidth - 6f, y + 4f, rightMargin - 6f, y + bannerHeight - 4f);
                    bool dlHovered = _downloadButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
                    FUIRenderer.DrawButton(canvas, _downloadButtonBounds, "DOWNLOAD",
                        dlHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
                    break;
                }

                case UpdateStatus.Downloading:
                {
                    int pct = _ctx.UpdateService.DownloadProgress;
                    // Progress bar fill as banner background
                    using var bannerBg = FUIRenderer.CreateFillPaint(FUIColors.ActiveGhost);
                    canvas.DrawRoundRect(bannerRect, bannerRadius, bannerRadius, bannerBg);
                    float fillWidth = bannerRect.Width * (pct / 100f);
                    var fillRect = new SKRect(bannerRect.Left, bannerRect.Top, bannerRect.Left + fillWidth, bannerRect.Bottom);
                    using var fillPaint = FUIRenderer.CreateFillPaint(FUIColors.SelectionBg);
                    canvas.Save();
                    using var bannerClip = new SKRoundRect(bannerRect, bannerRadius);
                    canvas.ClipRoundRect(bannerClip);
                    canvas.DrawRect(fillRect, fillPaint);
                    canvas.Restore();
                    using var bannerBorder = FUIRenderer.CreateStrokePaint(FUIColors.Active.WithAlpha(50));
                    canvas.DrawRoundRect(bannerRect, bannerRadius, bannerRadius, bannerBorder);
                    using var dotPaint = FUIRenderer.CreateFillPaint(FUIColors.Active);
                    canvas.DrawCircle(dotX, dotY, dotRadius, dotPaint);
                    FUIRenderer.DrawText(canvas, $"Downloading update\u2026 {pct}%", new SKPoint(textStartX, textY), FUIColors.TextPrimary, 13f);
                    break;
                }

                case UpdateStatus.ReadyToApply:
                {
                    FUIRenderer.DrawRoundedPanel(canvas, bannerRect, FUIColors.Active.WithAlpha(25), FUIColors.Active.WithAlpha(50), bannerRadius);
                    using var dotPaint = FUIRenderer.CreateFillPaint(FUIColors.Active);
                    canvas.DrawCircle(dotX, dotY, dotRadius, dotPaint);
                    FUIRenderer.DrawText(canvas, "Update ready", new SKPoint(textStartX, textY), FUIColors.TextPrimary, 13f);

                    // Inline "Apply update" button
                    float apBtnWidth = FUIRenderer.MeasureText("APPLY", 13f) + 24f;
                    _applyButtonBounds = new SKRect(rightMargin - apBtnWidth - 6f, y + 4f, rightMargin - 6f, y + bannerHeight - 4f);
                    bool apHovered = _applyButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
                    FUIRenderer.DrawButton(canvas, _applyButtonBounds, "APPLY",
                        apHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
                    break;
                }

                case UpdateStatus.Error:
                {
                    FUIRenderer.DrawRoundedPanel(canvas, bannerRect, FUIColors.Danger.WithAlpha(25), FUIColors.Danger.WithAlpha(50), bannerRadius);
                    using var dotPaint = FUIRenderer.CreateFillPaint(FUIColors.Danger);
                    canvas.DrawCircle(dotX, dotY, dotRadius, dotPaint);
                    FUIRenderer.DrawText(canvas, "Check failed", new SKPoint(textStartX, textY), FUIColors.TextPrimary, 13f);
                    break;
                }
            }
            y += bannerHeight + 8f;
        }
    }

}