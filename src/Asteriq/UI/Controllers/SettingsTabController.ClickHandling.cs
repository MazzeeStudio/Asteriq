using System.Reflection;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using Serilog;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class SettingsTabController
{
    private void HandleSettingsTabClick(SKPoint pt)
    {
        // Profile name edit icon
        if (_profileNameEditBounds != SKRect.Empty && _profileNameEditBounds.Contains(pt))
        {
            RenameActiveProfile();
            return;
        }

        // Profile name box click (whole box)
        if (_profileNameBounds.Contains(pt) && _ctx.ProfileManager.ActiveProfile is not null)
        {
            RenameActiveProfile();
            return;
        }

        // Profile action buttons
        if (_newProfileButtonBounds.Contains(pt))
        {
            _ctx.CreateNewProfilePrompt?.Invoke();
            return;
        }
        if (_duplicateProfileButtonBounds.Contains(pt) && _ctx.ProfileManager.ActiveProfile is not null)
        {
            _ctx.DuplicateActiveProfile?.Invoke();
            return;
        }
        if (_importProfileButtonBounds.Contains(pt))
        {
            _ctx.ImportProfile?.Invoke();
            return;
        }
        if (_exportProfileButtonBounds.Contains(pt) && _ctx.ProfileManager.ActiveProfile is not null)
        {
            _ctx.ExportActiveProfile?.Invoke();
            return;
        }
        if (_deleteProfileButtonBounds != default && _deleteProfileButtonBounds.Contains(pt) &&
            _ctx.ProfileManager.ActiveProfile is not null)
        {
            _ctx.DeleteActiveProfile?.Invoke();
            return;
        }

        // Theme button clicks
        FUITheme[] themes = {
            FUITheme.Midnight, FUITheme.Matrix, FUITheme.Amber, FUITheme.Ice,
            FUITheme.Drake, FUITheme.Aegis, FUITheme.Anvil, FUITheme.Argo,
            FUITheme.Crusader, FUITheme.Origin, FUITheme.MISC, FUITheme.RSI
        };
        for (int i = 0; i < _themeButtonBounds.Length && i < themes.Length; i++)
        {
            if (_themeButtonBounds[i].Contains(pt))
            {
                FUIColors.SetTheme(themes[i]);
                _ctx.ThemeService.Theme = themes[i];
                _ctx.BackgroundDirty = true;
                _ctx.TrayIcon.RefreshThemeColors();
                _ctx.InvalidateCanvas();
                return;
            }
        }

        // Auto-load toggle
        if (_autoLoadToggleBounds.Contains(pt))
        {
            _ctx.AppSettings.AutoLoadLastProfile = !_ctx.AppSettings.AutoLoadLastProfile;
            _ctx.InvalidateCanvas();
            return;
        }

        // Close to tray toggle
        if (_closeToTrayToggleBounds.Contains(pt))
        {
            _ctx.AppSettings.CloseToTray = !_ctx.AppSettings.CloseToTray;
            _ctx.InvalidateCanvas();
            return;
        }

        // Client Only Mode toggle
        if (_clientOnlyToggleBounds.Contains(pt))
        {
            _ctx.AppSettings.ClientOnlyMode = !_ctx.AppSettings.ClientOnlyMode;
            _ctx.MarkDirty();
            return;
        }

        // Launch on Windows startup toggle
        if (_launchOnStartToggleBounds.Contains(pt))
        {
            bool next = !_ctx.AppSettings.LaunchOnWindowsStart;
            try
            {
                WindowsStartupService.SetEnabled(next);
                _ctx.AppSettings.LaunchOnWindowsStart = next;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException
                                    or UnauthorizedAccessException
                                    or IOException)
            {
                Log.Warning(ex, "Failed to update Windows startup registry entry");
            }
            _ctx.InvalidateCanvas();
            return;
        }

        // Start forwarding on startup toggle
        if (_autoStartFwdToggleBounds.Contains(pt))
        {
            _ctx.AppSettings.AutoStartForwarding = !_ctx.AppSettings.AutoStartForwarding;
            _ctx.InvalidateCanvas();
            return;
        }

        // Open minimized toggle
        if (_openMinimizedToggleBounds.Contains(pt))
        {
            _ctx.AppSettings.OpenMinimized = !_ctx.AppSettings.OpenMinimized;
            _ctx.InvalidateCanvas();
            return;
        }

        // Check for updates automatically toggle
        if (_checkUpdatesToggleBounds.Contains(pt))
        {
            _ctx.AppSettings.AutoCheckUpdates = !_ctx.AppSettings.AutoCheckUpdates;
            _ctx.InvalidateCanvas();
            return;
        }

        // Update channel segmented control
        for (int i = 0; i < _updateChannelSegBounds.Length; i++)
        {
            if (_updateChannelSegBounds[i].Contains(pt))
            {
                var channel = i == 1 ? UpdateChannel.Nightly : UpdateChannel.Stable;
                if (_ctx.AppSettings.UpdateChannel != channel)
                {
                    _ctx.AppSettings.UpdateChannel = channel;
                    _ctx.InvalidateCanvas();
                }
                return;
            }
        }

        // Network forwarding toggle
        if (_networkEnabledToggleBounds.Contains(pt))
        {
            _ctx.AppSettings.NetworkEnabled = !_ctx.AppSettings.NetworkEnabled;
            if (_ctx.AppSettings.NetworkEnabled)
            {
                _ctx.StartNetworking?.Invoke();
                // Switch right panel to NETWORK so user can configure immediately
                _settingsRightPanelActive = PanelNetwork;
                _ctx.AppSettings.SettingsRightPanel = PanelNetwork;
            }
            else
            {
                _ctx.ShutdownNetworking?.Invoke();
            }
            _ctx.InvalidateCanvas();
            return;
        }

        // Right panel accordion header clicks
        if (_visualPanelHeaderBounds.HitTest(pt))
        {
            _settingsRightPanelActive = PanelVisual;
            _ctx.AppSettings.SettingsRightPanel = PanelVisual;
            _ctx.InvalidateCanvas();
            return;
        }
        if (_networkPanelHeaderBounds.HitTest(pt))
        {
            _settingsRightPanelActive = PanelNetwork;
            _ctx.AppSettings.SettingsRightPanel = PanelNetwork;
            _ctx.InvalidateCanvas();
            return;
        }
        if (_hidHidePanelHeaderBounds.HitTest(pt))
        {
            _settingsRightPanelActive = PanelHidHide;
            _ctx.AppSettings.SettingsRightPanel = PanelHidHide;
            // State and version are loaded once per session in DrawRightPanel.
            // Don't force a reload here — each reload spawns blocking CLI calls and stalls the UI.
            _hidHideInstallPhase = HidHideInstallPhase.Idle;
            _ctx.InvalidateCanvas();
            return;
        }

        // HidHide panel clicks
        if (_settingsRightPanelActive == PanelHidHide)
        {
            if (_hidHideUpdateButtonBounds.HitTest(pt)
                && _hidHideInstallPhase is HidHideInstallPhase.Idle or HidHideInstallPhase.Error)
            {
                StartHidHideInstall();
                return;
            }
        }

        if (_settingsRightPanelActive == PanelHidHide && _ctx.HidHide?.IsAvailable() == true)
        {
            // All toggle handlers below flip local UI state immediately for snappy feedback
            // and run the blocking HidHideCLI command on a worker thread.
            var hidhide = _ctx.HidHide;

            if (_hidHideCloakingToggleBounds.HitTest(pt))
            {
                _hidHideCloaking = !_hidHideCloaking;
                bool enable = _hidHideCloaking;
                _ = Task.Run(() =>
                {
                    if (enable) hidhide.EnableCloaking();
                    else hidhide.DisableCloaking();
                });
                _ctx.InvalidateCanvas();
                return;
            }
            if (_hidHideInverseToggleBounds.HitTest(pt))
            {
                _hidHideInverse = !_hidHideInverse;
                bool enable = _hidHideInverse;
                _ = Task.Run(() =>
                {
                    if (enable) hidhide.EnableInverseMode();
                    else hidhide.DisableInverseMode();
                });
                _ctx.InvalidateCanvas();
                return;
            }

            // Per-device HidHide toggle rows
            foreach (var (bounds, device) in _hidHideDeviceToggleBounds)
            {
                if (bounds.Contains(pt))
                {
                    var localDevice = device;
                    var (vid, pid) = DeviceMatchingService.ExtractVidPidFromSdlGuid(localDevice.InstanceGuid);
                    var pattern = vid > 0 ? $"VID_{vid:X4}&PID_{pid:X4}" : null;
                    bool wasHidden = pattern is not null && _hidHideHiddenPaths is not null
                        && _hidHideHiddenPaths.Any(p => p.Contains(pattern, StringComparison.OrdinalIgnoreCase));
                    bool willHide = !wasHidden;

                    // Optimistic local update so the switch flips immediately.
                    // For hide we add the pattern as a sentinel — the Contains check below
                    // matches it just like a real path. The continuation overwrites the
                    // cache with the real result either way.
                    if (pattern is not null)
                    {
                        _hidHideHiddenPaths ??= new List<string>();
                        if (wasHidden)
                            _hidHideHiddenPaths.RemoveAll(p => p.Contains(pattern, StringComparison.OrdinalIgnoreCase));
                        else
                            _hidHideHiddenPaths.Add(pattern);
                    }

                    var uiScheduler = TaskScheduler.FromCurrentSynchronizationContext();
                    _ = Task.Run(() =>
                    {
                        _ctx.ToggleHidHideForDevice?.Invoke(localDevice);
                        return hidhide.GetHiddenDevices();
                    }).ContinueWith(t =>
                    {
                        if (t.IsCompletedSuccessfully)
                        {
                            _hidHideHiddenPaths = t.Result;
                            // Failure detection: if the real state diverges from what the user asked for,
                            // surface a status line. The cache assignment above already snapped the toggle back.
                            if (pattern is not null)
                            {
                                bool actuallyHidden = t.Result.Any(p => p.Contains(pattern, StringComparison.OrdinalIgnoreCase));
                                if (actuallyHidden != willHide)
                                {
                                    var verb = willHide ? "hide" : "unhide";
                                    SetHidHideStatus($"Failed to {verb} {localDevice.Name}");
                                }
                            }
                        }
                        else
                        {
                            SetHidHideStatus($"HidHide command failed for {localDevice.Name}");
                        }
                        _ctx.InvalidateCanvas();
                    }, uiScheduler);
                    _ctx.InvalidateCanvas();
                    return;
                }
            }
        }

        // Role selector buttons (only when networking is enabled)
        if (_ctx.AppSettings.NetworkEnabled)
        {
            var roleValues = new[] { NetworkRole.Master, NetworkRole.Client };
            for (int ri = 0; ri < _netRoleButtonBounds.Length; ri++)
            {
                if (_netRoleButtonBounds[ri].Contains(pt))
                {
                    _ctx.AppSettings.NetworkRole = roleValues[ri];
                    // Restart discovery — broadcast on/off depends on role (only Client broadcasts)
                    if (_ctx.NetworkDiscovery is NetworkDiscoveryService nds)
                    {
                        var name = string.IsNullOrEmpty(_ctx.AppSettings.NetworkMachineName)
                            ? Environment.MachineName : _ctx.AppSettings.NetworkMachineName;
                        nds.Configure(name, _ctx.AppSettings.NetworkListenPort, roleValues[ri]);
                        _ = nds.StartAsync();
                    }
                    _ctx.InvalidateCanvas();
                    return;
                }
            }

            // Master-specific buttons
            if (_ctx.AppSettings.NetworkRole == NetworkRole.Master)
            {
                if (_netRegenerateBounds.Contains(pt))
                {
                    // Clear the code — getter in ApplicationSettingsService auto-generates a new one
                    _ctx.AppSettings.NetworkMasterCode = "";
                    _ctx.InvalidateCanvas();
                    return;
                }

                // Per-peer connection toggles
                foreach (var (toggleRect, peerIp) in _peerActionBounds)
                {
                    if (!toggleRect.Contains(pt)) continue;

                    bool thisConnected = _ctx.NetworkMode == NetworkInputMode.Remote
                                     && _ctx.ConnectedPeerIp == peerIp;
                    if (thisConnected)
                    {
                        Log.Debug("[UI] Peer toggle OFF | peer={Ip}", peerIp);
                        _ = _ctx.NetworkDisconnectAsync?.Invoke();
                        _ctx.InvalidateCanvas();
                        return;
                    }
                    if (!_ctx.IsNetworkConnecting && _ctx.ConnectToPeerAsync is not null)
                    {
                        var peer = _ctx.NetworkDiscovery?.KnownPeers.Values
                            .FirstOrDefault(p => p.IpAddress == peerIp);
                        if (peer is not null)
                        {
                            Log.Debug("[UI] Peer toggle ON | peer={Ip} prevConnected={Prev}", peerIp, _ctx.ConnectedPeerIp ?? "none");
                            _connectingPeerIp = peerIp;
                            _ = _ctx.ConnectToPeerAsync(peer).ContinueWith(_ =>
                            {
                                // Clear connecting state on completion (success or failure)
                                // so the toggle doesn't stick in the "connecting" position.
                                _connectingPeerIp = null;
                                _ctx.InvalidateCanvas();
                            }, TaskScheduler.Default);
                        }
                        _ctx.InvalidateCanvas();
                        return;
                    }
                }

            }
            // Client-specific buttons
            else if (_ctx.AppSettings.NetworkRole == NetworkRole.Client)
            {
                if (_netForgetBounds.Contains(pt))
                {
                    _ctx.AppSettings.TrustedMaster = null;
                    _ctx.InvalidateCanvas();
                    return;
                }

                if (_netDisconnectBounds.Contains(pt) &&
                    _ctx.NetworkInput?.Mode == NetworkInputMode.Receiving)
                {
                    // Route through MainForm.SwitchToLocalAsync — clears client state
                    _ = _ctx.NetworkDisconnectAsync?.Invoke();
                    _ctx.InvalidateCanvas();
                    return;
                }

                // Match Master — apply master's vJoy config locally using proven vJoyConfig.exe flow
                if (_netMatchMasterBounds.Contains(pt))
                {
                    ApplyMasterVJoyConfig();
                    return;
                }
            }
        }


        // Background slider clicks (start drag)
        if (_bgGridSliderBounds.Contains(pt)) { _draggingBgSlider = "grid"; UpdateBgSliderFromPoint(pt.X); return; }
        if (_bgGlowSliderBounds.Contains(pt)) { _draggingBgSlider = "glow"; UpdateBgSliderFromPoint(pt.X); return; }
        if (_bgNoiseSliderBounds.Contains(pt)) { _draggingBgSlider = "noise"; UpdateBgSliderFromPoint(pt.X); return; }
        if (_bgScanlineSliderBounds.Contains(pt)) { _draggingBgSlider = "scanline"; UpdateBgSliderFromPoint(pt.X); return; }
        if (_bgVignetteSliderBounds.Contains(pt)) { _draggingBgSlider = "vignette"; UpdateBgSliderFromPoint(pt.X); return; }

        // Support panel clicks
        if (_bmacButtonBounds.Contains(pt))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://buymeacoffee.com/nerosilentr",
                UseShellExecute = true
            });
            return;
        }
        if (_referralCopyButtonBounds.Contains(pt))
        {
            Clipboard.SetText("STAR-RBDQ-Z4JG");
            _referralCopiedTicks = Environment.TickCount64;
            _ctx.MarkDirty();
            return;
        }
        if (_referralLinkButtonBounds.Contains(pt))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://www.robertsspaceindustries.com/enlist?referral=STAR-RBDQ-Z4JG",
                UseShellExecute = true
            });
            return;
        }

        // Driver setup button click
        if (_driverSetupButtonBounds.HitTest(pt))
        {
            _ctx.OpenDriverSetup?.Invoke();
            return;
        }

        // Check for updates button click
        if (_checkButtonBounds.HitTest(pt))
        {
            var status = _ctx.UpdateService.Status;
            if (status is UpdateStatus.Unknown or UpdateStatus.UpToDate or UpdateStatus.Error)
            {
                _ = _ctx.UpdateService.CheckAsync().ContinueWith(
                    _ => _ctx.OwnerForm.Invoke(_ctx.InvalidateCanvas),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                _ctx.InvalidateCanvas();
            }
            return;
        }

        // Download update button click (inside UpdateAvailable banner)
        if (_downloadButtonBounds.HitTest(pt))
        {
            _ = _ctx.UpdateService.DownloadAsync().ContinueWith(
                _ => _ctx.OwnerForm.Invoke(_ctx.InvalidateCanvas),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            _ctx.InvalidateCanvas();
            return;
        }

        // Apply update button click (inside ReadyToApply banner)
        if (_applyButtonBounds.HitTest(pt))
        {
            _ = _ctx.UpdateService.ApplyUpdateAsync();
            return;
        }
    }

}