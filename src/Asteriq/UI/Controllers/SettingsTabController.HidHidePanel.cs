using System.Reflection;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using Serilog;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class SettingsTabController
{
    private void DrawHidHideSettingsPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        bool headerHovered = new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Top + RightPanelCollapsedH)
            .Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        var m = FUIWidgets.DrawCollapsiblePanelHeader(canvas, bounds, "HIDHIDE", true, headerHovered, out var hdrBounds);
        _hidHidePanelHeaderBounds = hdrBounds;
        float y = m.Y;
        float leftMargin = m.LeftMargin;
        float rightMargin = m.RightMargin;
        const float toggleW = 48f;
        const float rowH = 26f;
        const float sectionGap = 10f;

        canvas.Save();
        canvas.ClipRect(bounds.Inset(2f, 2f));

        bool available = _ctx.HidHide?.IsAvailable() ?? false;

        // Kick off version check once per panel open
        if (!_hidHideVersionChecked && _ctx.HidHide is not null)
        {
            _hidHideVersionChecked = true;
            _hidHideInstalledVersion = _ctx.HidHide.GetInstalledVersion();
            _ = _ctx.HidHide.GetLatestReleaseAsync().ContinueWith(t =>
            {
                if (!t.IsFaulted && t.Result is not null)
                {
                    _hidHideLatestVersion = t.Result.Version;
                    _hidHideInstallerUrl  = t.Result.InstallerUrl;
                    _ctx.InvalidateCanvas();
                }
            }, TaskScheduler.Default);
        }

        if (!available)
        {
            _hidHideCloakingToggleBounds = SKRect.Empty;
            _hidHideInverseToggleBounds  = SKRect.Empty;
            FUIRenderer.DrawText(canvas, "HidHide not installed.",
                new SKPoint(leftMargin, y + rowH / 2f + 4f), FUIColors.Warning, 13f);
            y += rowH + sectionGap;
            // Show download button
            DrawHidHideDownloadButton(canvas, leftMargin, rightMargin, ref y, rowH,
                _hidHideLatestVersion is not null ? $"Download v{_hidHideLatestVersion}" : "Download HidHide");
            canvas.Restore();
            return;
        }

        // Version row
        if (_hidHideInstalledVersion is not null)
        {
            FUIRenderer.DrawText(canvas, $"v{_hidHideInstalledVersion}",
                new SKPoint(leftMargin, y + rowH / 2f + 4f), FUIColors.TextDim, 12f);
            y += rowH;

            bool updateAvailable = IsHidHideUpdateAvailable();
            if (updateAvailable)
            {
                DrawHidHideDownloadButton(canvas, leftMargin, rightMargin, ref y, rowH,
                    $"Update to v{_hidHideLatestVersion}");
                y += sectionGap;
            }
            else
            {
                _hidHideUpdateButtonBounds = SKRect.Empty;
                y += sectionGap;
            }
        }

        // CLOAKING toggle
        FUIRenderer.DrawTextTruncated(canvas, "Cloaking",
            new SKPoint(leftMargin, y + rowH / 2f + 4f), rightMargin - leftMargin - toggleW - 8f, FUIColors.TextPrimary, 13f);
        _hidHideCloakingToggleBounds = new SKRect(rightMargin - toggleW, y, rightMargin, y + rowH);
        FUIWidgets.DrawToggleSwitch(canvas, _hidHideCloakingToggleBounds, _cloakingT.T, _ctx.MousePosition);
        y += rowH + sectionGap;

        // INVERSE MODE toggle
        FUIRenderer.DrawTextTruncated(canvas, "Inverse mode",
            new SKPoint(leftMargin, y + rowH / 2f + 4f), rightMargin - leftMargin - toggleW - 8f, FUIColors.TextPrimary, 13f);
        _hidHideInverseToggleBounds = new SKRect(rightMargin - toggleW, y, rightMargin, y + rowH);
        FUIWidgets.DrawToggleSwitch(canvas, _hidHideInverseToggleBounds, _inverseT.T, _ctx.MousePosition);
        y += rowH + sectionGap;

        // Description of inverse mode
        var descText = _hidHideInverse
            ? "Whitelisted apps are blocked."
            : "Whitelisted apps can see devices.";
        FUIRenderer.DrawText(canvas, descText, new SKPoint(leftMargin, y + 4f),
            FUIColors.TextDim.WithAlpha(160), 11f);
        y += 20f + sectionGap;

        // Per-device HidHide toggle rows
        var physicalDevices = _ctx.Devices.Where(d => !d.IsVirtual && d.IsConnected).ToList();
        _hidHideDeviceToggleBounds.Clear();
        if (physicalDevices.Count > 0)
        {
            FUIRenderer.DrawText(canvas, "DEVICE HIDING", new SKPoint(leftMargin, y + 4f),
                FUIColors.TextDim, 11f);
            y += rowH;

            // Use the cached list populated by the panel-open background load. If still
            // null (first-time load in flight), every row renders as not-hidden until the
            // cache arrives — much better than spawning a CLI per paint frame.
            var hiddenPaths = _hidHideHiddenPaths ?? new List<string>();
            foreach (var device in physicalDevices)
            {
                var (vid, pid) = DeviceMatchingService.ExtractVidPidFromSdlGuid(device.InstanceGuid);
                var pattern = vid > 0 ? $"VID_{vid:X4}&PID_{pid:X4}" : null;
                bool isHidden = pattern is not null &&
                    hiddenPaths.Any(p => p.Contains(pattern, StringComparison.OrdinalIgnoreCase));

                string shortName = device.Name.Length > 22
                    ? string.Concat(device.Name.AsSpan(0, 20), "..") : device.Name;
                FUIRenderer.DrawTextTruncated(canvas, shortName,
                    new SKPoint(leftMargin, y + rowH / 2f + 4f),
                    rightMargin - leftMargin - toggleW - 8f, FUIColors.TextPrimary, 12f);

                var tBounds = new SKRect(rightMargin - toggleW, y, rightMargin, y + rowH);
                FUIWidgets.DrawToggleSwitch(canvas, tBounds, isHidden ? 1f : 0f, _ctx.MousePosition);
                _hidHideDeviceToggleBounds.Add((tBounds, device));
                y += rowH + 4f;
            }
        }

        // Transient HidHide status line (4 seconds after the operation)
        if (_hidHideStatusMessage is not null)
        {
            var elapsed = (DateTime.Now - _hidHideStatusTime).TotalSeconds;
            if (elapsed < 4.0)
            {
                y += 4f;
                FUIRenderer.DrawText(canvas, _hidHideStatusMessage,
                    new SKPoint(leftMargin, y + rowH / 2f + 4f), FUIColors.Danger, 11.5f);
            }
            else
            {
                _hidHideStatusMessage = null;
            }
        }

        canvas.Restore();
    }

    private void SetHidHideStatus(string message)
    {
        _hidHideStatusMessage = message;
        _hidHideStatusTime = DateTime.Now;
        // Schedule a repaint after the fade so the line disappears even with no further user input
        var uiScheduler = TaskScheduler.FromCurrentSynchronizationContext();
        _ = Task.Delay(4100).ContinueWith(_ => _ctx.InvalidateCanvas(), uiScheduler);
    }

    private void DrawHidHideDownloadButton(SKCanvas canvas, float leftMargin, float rightMargin,
        ref float y, float rowH, string idleLabel)
    {
        _hidHideUpdateButtonBounds = new SKRect(leftMargin, y, rightMargin, y + rowH);

        string label;
        FUIRenderer.ButtonState state;
        switch (_hidHideInstallPhase)
        {
            case HidHideInstallPhase.Downloading:
                label = $"Downloading... {_hidHideDownloadProgress}%";
                state = FUIRenderer.ButtonState.Disabled;
                break;
            case HidHideInstallPhase.Launching:
                label = "Installing...";
                state = FUIRenderer.ButtonState.Disabled;
                break;
            case HidHideInstallPhase.Error:
                label = "Download failed. Retry";
                state = _hidHideUpdateButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y)
                    ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
                break;
            default:
                label = idleLabel;
                state = _hidHideUpdateButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y)
                    ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
                break;
        }

        FUIRenderer.DrawButton(canvas, _hidHideUpdateButtonBounds, label, state);
        y += rowH;
    }

    private bool IsHidHideUpdateAvailable()
    {
        if (_hidHideInstalledVersion is null || _hidHideLatestVersion is null)
            return false;
        if (Version.TryParse(_hidHideInstalledVersion, out var installed) &&
            Version.TryParse(_hidHideLatestVersion, out var latest))
            return latest > installed;
        return false;
    }

    private void StartHidHideInstall()
    {
        string? url = _hidHideInstallerUrl;
        if (string.IsNullOrEmpty(url) || _ctx.HidHide is null)
            return;

        _hidHideInstallPhase = HidHideInstallPhase.Downloading;
        _hidHideDownloadProgress = 0;
        _ctx.InvalidateCanvas();

        _hidHideDownloadCts?.Cancel();
        _hidHideDownloadCts = new CancellationTokenSource();
        var ct = _hidHideDownloadCts.Token;

        var progress = new Progress<int>(pct =>
        {
            _hidHideDownloadProgress = pct;
            _ctx.InvalidateCanvas();
        });

        _ = _ctx.HidHide.DownloadInstallerAsync(url, progress, ct).ContinueWith(t =>
        {
            if (t.IsCanceled)
            {
                _hidHideInstallPhase = HidHideInstallPhase.Idle;
                _ctx.InvalidateCanvas();
                return;
            }

            string? installerPath = t.IsFaulted ? null : t.Result;
            if (installerPath is null)
            {
                _hidHideInstallPhase = HidHideInstallPhase.Error;
                _ctx.InvalidateCanvas();
                return;
            }

            _hidHideInstallPhase = HidHideInstallPhase.Launching;
            _ctx.InvalidateCanvas();

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true  // triggers UAC elevation if needed
            });
        }, TaskScheduler.Default);
    }

}