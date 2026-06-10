using System.Reflection;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using Serilog;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public sealed partial class SettingsTabController
{
    private void ApplyMasterVJoyConfig()
    {
        if (_matchingMasterInProgress) return;

        string? configPath = Services.DriverSetupManager.GetVJoyConfigPath();
        if (configPath is null)
        {
            FUIMessageBox.ShowWarning(_ctx.OwnerForm,
                "vJoyConfig.exe was not found.\nPlease ensure vJoy is installed correctly.",
                "vJoy Not Found");
            return;
        }

        // Collect all commands that need to run, releasing devices up front
        var commands = new List<string>();
        foreach (var (deviceId, masterCfg) in _ctx.MasterVJoyConfigs)
        {
            var local = _ctx.VJoyDevices.FirstOrDefault(v => v.Id == deviceId);
            if (local is not null && VJoyConfigMatches(local, masterCfg))
                continue; // already matches

            _ctx.VJoyService.ReleaseDevice(deviceId);

            string args = $"{deviceId} -f";
            if (masterCfg.AxisFlags.Count > 0)
                args += $" -a {string.Join(" ", masterCfg.AxisFlags)}";
            if (masterCfg.ButtonCount > 0)
                args += $" -b {masterCfg.ButtonCount}";
            if (masterCfg.PovCount > 0)
                args += $" -p {masterCfg.PovCount}";

            commands.Add($"\"{configPath}\" {args}");
        }

        if (commands.Count == 0) return; // everything already matches

        _matchingMasterInProgress = true;
        _ctx.MarkDirty();

        // Write a temp batch script so all devices are configured under one UAC prompt
        string batPath = Path.Combine(Path.GetTempPath(), $"asteriq_vjoy_sync_{Environment.ProcessId}.bat");
        File.WriteAllText(batPath, string.Join(Environment.NewLine, commands) + Environment.NewLine);

        Task.Run(() =>
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{batPath}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };

                using var process = System.Diagnostics.Process.Start(psi);
                process?.WaitForExit(30000);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // User cancelled UAC — no error needed
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
            {
                _ctx.OwnerForm.BeginInvoke(() =>
                    FUIMessageBox.ShowError(_ctx.OwnerForm,
                        $"Failed to reconfigure vJoy devices:\n{ex.Message}",
                        "Configuration Failed"));
            }
            finally
            {
                try { File.Delete(batPath); } catch (IOException) { /* best effort cleanup */ }
                _ctx.OwnerForm.BeginInvoke(() =>
                {
                    _matchingMasterInProgress = false;
                    _ctx.RefreshVJoyDevices?.Invoke();
                    _ctx.InvalidateCanvas();
                });
            }
        });
    }

    private static bool VJoyConfigMatches(VJoyDeviceInfo local, VJoyConfigReceivedEventArgs master)
    {
        if (local.ButtonCount != master.ButtonCount) return false;
        if ((local.DiscPovCount + local.ContPovCount) != master.PovCount) return false;
        if (local.HasAxisX != master.AxisFlags.Contains("X")) return false;
        if (local.HasAxisY != master.AxisFlags.Contains("Y")) return false;
        if (local.HasAxisZ != master.AxisFlags.Contains("Z")) return false;
        if (local.HasAxisRX != master.AxisFlags.Contains("RX")) return false;
        if (local.HasAxisRY != master.AxisFlags.Contains("RY")) return false;
        if (local.HasAxisRZ != master.AxisFlags.Contains("RZ")) return false;
        if (local.HasSlider0 != master.AxisFlags.Contains("SL0")) return false;
        if (local.HasSlider1 != master.AxisFlags.Contains("SL1")) return false;
        return true;
    }

    private void DrawNetworkSettingsPanel(SKCanvas canvas, SKRect bounds)
    {
        bool headerHovered = new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Top + RightPanelCollapsedH)
            .Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        var m = FUIWidgets.DrawCollapsiblePanelHeader(canvas, bounds, "NETWORK", true, headerHovered, out var hdrBounds);
        _networkPanelHeaderBounds = hdrBounds;
        float y = m.Y;
        float leftMargin = m.LeftMargin;
        float rightMargin = m.RightMargin;
        float contentWidth = m.ContentWidth;
        const float rowH = 26f;
        const float btnH = 24f;
        const float toggleW = 48f;
        const float sectionGap = 8f;

        canvas.Save();
        canvas.ClipRect(bounds.Inset(2f, 2f));

        // Machine name + port
        string machineName = string.IsNullOrEmpty(_ctx.AppSettings.NetworkMachineName)
            ? Environment.MachineName
            : _ctx.AppSettings.NetworkMachineName;
        FUIRenderer.DrawTextTruncated(canvas, $"Machine: {machineName}  ·  Port: {_ctx.AppSettings.NetworkListenPort}",
            new SKPoint(leftMargin, y + rowH / 2f + 4f), contentWidth, FUIColors.TextDim, 13f);
        y += rowH + sectionGap;

        // Role selector  [TX]  [RX]
        float roleBtnW = 60f;
        float roleBtnGap = 6f;
        var currentRole = _ctx.AppSettings.NetworkRole;
        FUIRenderer.DrawTextTruncated(canvas, "Role", new SKPoint(leftMargin, y + btnH / 2f + 4f),
            FUIRenderer.MeasureText("Role", 13f) + 4f, FUIColors.TextDim, 13f);
        float rolePairX = rightMargin - (roleBtnW * 2 + roleBtnGap);
        for (int ri = 0; ri < 2; ri++)
        {
            float rx = rolePairX + ri * (roleBtnW + roleBtnGap);
            _netRoleButtonBounds[ri] = new SKRect(rx, y, rx + roleBtnW, y + btnH);
            bool isActive = (ri == 0 && currentRole == NetworkRole.Master)
                         || (ri == 1 && currentRole == NetworkRole.Client);
            bool hov = _netRoleButtonBounds[ri].Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            FUIWidgets.DrawToggleButton(canvas, _netRoleButtonBounds[ri], new[] { "TX", "RX" }[ri], isActive, hov, 13f);
        }
        y += btnH + sectionGap * 1.5f;

        // ── Master-specific UI ─────────────────────────────────────────
        if (currentRole == NetworkRole.Master)
        {
            string code = _ctx.AppSettings.NetworkMasterCode;
            FUIRenderer.DrawTextTruncated(canvas, $"Your code:  {code}",
                new SKPoint(leftMargin, y + btnH / 2f + 4f), contentWidth - 110f, FUIColors.TextBright, 13f);
            _netRegenerateBounds = new SKRect(rightMargin - 100f, y, rightMargin, y + btnH);
            bool regenHov = _netRegenerateBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            FUIRenderer.DrawButton(canvas, _netRegenerateBounds, "REGENERATE",
                regenHov ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
            y += btnH + sectionGap;

            // Only Client (RX) machines broadcast, so TX only discovers clients.
            // No role filtering needed — if you see a peer, it's connectable.
            var peers = _ctx.NetworkDiscovery?.KnownPeers.Values.ToList() ?? [];
            var netMode = _ctx.NetworkInput?.Mode ?? NetworkInputMode.Local;
            bool isConnecting = _ctx.IsNetworkConnecting;
            string? connectedIp = _ctx.ConnectedPeerIp;
            _peerActionBounds.Clear();

            if (peers.Count > 0)
            {
                for (int pi = 0; pi < peers.Count; pi++)
                {
                    var p = peers[pi];
                    bool thisConnected = netMode == NetworkInputMode.Remote && connectedIp == p.IpAddress;
                    var pColor = thisConnected ? FUIColors.Active
                               : p.IsStale    ? FUIColors.TextDim : FUIColors.TextPrimary;
                    string peerLabel = $"{p.MachineName}  {p.IpAddress}";

                    float tglY = y + (rowH - btnH) / 2f;
                    var toggleRect = new SKRect(rightMargin - toggleW, tglY, rightMargin, tglY + btnH);
                    _peerActionBounds.Add((toggleRect, p.IpAddress));

                    bool rowHov = toggleRect.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y)
                        || new SKRect(leftMargin, y, rightMargin - toggleW - 6f, y + rowH)
                            .Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
                    if (rowHov)
                    {
                        using var hpaint = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(12));
                        canvas.DrawRect(new SKRect(leftMargin, y, rightMargin, y + rowH), hpaint);
                    }
                    FUIRenderer.DrawTextTruncated(canvas, peerLabel, new SKPoint(leftMargin, y + rowH / 2f + 4f),
                        contentWidth - toggleW - 8f, pColor, 13f);
                    float knobT = _peerToggleT.TryGetValue(p.IpAddress, out float t) ? t : (thisConnected ? 1f : 0f);
                    FUIWidgets.DrawToggleSwitch(canvas, toggleRect, knobT, _ctx.MousePosition);
                    y += rowH + 8f;
                }
            }
            else
            {
                FUIRenderer.DrawTextTruncated(canvas, "No clients discovered yet",
                    new SKPoint(leftMargin, y + rowH / 2f + 4f), contentWidth, FUIColors.TextDim, 13f);
                y += rowH;
            }

            bool isConnected = netMode == NetworkInputMode.Remote;
            string statusText = isConnecting ? "Connecting..." : isConnected ? "Connected, sending" : "Not connected";
            var statusColor = isConnected ? FUIColors.Active : isConnecting ? FUIColors.Warning : FUIColors.TextDim;
            y += sectionGap;
            FUIRenderer.DrawTextTruncated(canvas, $"Status:  {statusText}",
                new SKPoint(leftMargin, y + rowH / 2f + 4f), contentWidth, statusColor, 13f);

        }
        // ── Client-specific UI ─────────────────────────────────────────
        else if (currentRole == NetworkRole.Client)
        {
            var trusted = _ctx.AppSettings.TrustedMaster;
            var netMode2 = _ctx.NetworkInput?.Mode ?? NetworkInputMode.Local;
            bool isReceiving = netMode2 == NetworkInputMode.Receiving;

            if (trusted is not null)
            {
                FUIRenderer.DrawTextTruncated(canvas, $"Trusted:  {trusted.MachineName}",
                    new SKPoint(leftMargin, y + btnH / 2f + 4f), contentWidth - 100f, FUIColors.TextPrimary, 13f);
                _netForgetBounds = new SKRect(rightMargin - 90f, y, rightMargin, y + btnH);
                bool forgetHov = _netForgetBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
                FUIRenderer.DrawButton(canvas, _netForgetBounds, "FORGET",
                    forgetHov ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal, isDanger: true);
                y += btnH + sectionGap;
            }
            else
            {
                FUIRenderer.DrawTextTruncated(canvas, "Waiting for trusted master",
                    new SKPoint(leftMargin, y + rowH / 2f + 4f), contentWidth, FUIColors.TextDim, 13f);
                y += rowH + sectionGap;
            }

            _netDisconnectBounds = new SKRect(rightMargin - 110f, y, rightMargin, y + btnH);
            bool discHov = isReceiving && _netDisconnectBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            FUIRenderer.DrawButton(canvas, _netDisconnectBounds, "DISCONNECT",
                !isReceiving ? FUIRenderer.ButtonState.Disabled :
                discHov ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal, isDanger: true);
            y += btnH + sectionGap;

            bool isListening = _ctx.NetworkInput?.IsListening ?? false;
            string statusText2 = isReceiving  ? $"Connected to {trusted?.MachineName ?? "master"}"
                               : isListening  ? $"Waiting for master (listening on port {_ctx.AppSettings.NetworkListenPort})"
                               : $"NOT LISTENING: port {_ctx.AppSettings.NetworkListenPort} unavailable, restart Asteriq";
            var statusColor2 = isReceiving ? FUIColors.Active
                             : isListening ? FUIColors.TextDim : FUIColors.Warning;
            FUIRenderer.DrawTextTruncated(canvas, $"Status:  {statusText2}",
                new SKPoint(leftMargin, y + rowH / 2f + 4f), contentWidth, statusColor2, 13f);
            y += rowH;

            // vJoy mismatch warning — show when master has sent config and local doesn't match
            _netMatchMasterBounds = SKRect.Empty;
            if (_ctx.MasterVJoyConfigs.Count > 0)
            {
                bool hasMismatch = false;
                foreach (var (masterId, masterCfg) in _ctx.MasterVJoyConfigs)
                {
                    var local = _ctx.VJoyDevices.FirstOrDefault(v => v.Id == masterId);
                    if (local is null || !VJoyConfigMatches(local, masterCfg))
                    {
                        hasMismatch = true;
                        break;
                    }
                }

                if (hasMismatch)
                {
                    y += 4f;
                    if (_matchingMasterInProgress)
                    {
                        // Animated dots: cycle 1–5 dots based on time
                        int dotCount = (int)(Environment.TickCount64 / 400 % 5) + 1;
                        string dots = new string('.', dotCount);
                        FUIRenderer.DrawTextTruncated(canvas, $"Syncing vJoy config{dots}",
                            new SKPoint(leftMargin, y + btnH / 2f + 4f), contentWidth, FUIColors.TextDim, 13f);
                        _ctx.MarkDirty(); // keep animating
                    }
                    else
                    {
                        FUIRenderer.DrawTextTruncated(canvas, "vJoy config does not match master",
                            new SKPoint(leftMargin, y + btnH / 2f + 4f), contentWidth - 140f, FUIColors.Warning, 13f);
                        float matchBtnW = 130f;
                        _netMatchMasterBounds = new SKRect(rightMargin - matchBtnW, y, rightMargin, y + btnH);
                        bool matchHov = _netMatchMasterBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
                        FUIRenderer.DrawButton(canvas, _netMatchMasterBounds, "MATCH MASTER",
                            matchHov ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
                    }
                }
            }
        }
        else
        {
            if (_ctx.NetworkDiscovery is not null)
            {
                int peerCount = _ctx.NetworkDiscovery.KnownPeers.Count;
                var peerColor = FUIColors.InteractiveColor(peerCount > 0);
                FUIRenderer.DrawTextTruncated(canvas, $"Peers visible: {peerCount}",
                    new SKPoint(leftMargin, y + rowH / 2f + 4f), contentWidth, peerColor, 13f);
            }
        }

        canvas.Restore();
    }

}