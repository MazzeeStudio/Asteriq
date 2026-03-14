using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Asteriq.UI;

public partial class MainForm : Form
{
    // Input forwarding state (physical → vJoy)
    private bool _isForwarding = false;

    // Network forwarding state
    private volatile NetworkInputMode _networkMode = NetworkInputMode.Local;
    private bool _isNetworkConnecting;    // true while master-side handshake is in-flight
    private bool _lastSwitchButtonState = false;
    private long _lastSwitchButtonTick = 0;
    private const int SwitchDebounceMs = 400;

    // NetworkVJoyService wrapper — provides capture-mode forwarding; always the same object as _vjoyService
    private NetworkVJoyService _networkVjoy = null!;

    // Client mode — true when this machine is receiving vJoy state from a master
    private bool _isClientConnected;
    private string _connectedMasterName = "";

    // Master heartbeat — sends current snapshot at 20 Hz when in Remote mode
    // even if no physical input events are generated (e.g. stick at rest).
    private CancellationTokenSource? _heartbeatCts;

    #region Networking

    private void InitializeNetworking()
    {
        if (_networkDiscovery is NullNetworkDiscoveryService ||
            _networkInput is NullNetworkInputService) return;

        var machineName = string.IsNullOrEmpty(_appSettings.NetworkMachineName)
            ? Environment.MachineName
            : _appSettings.NetworkMachineName;
        var port = _appSettings.NetworkListenPort;

        if (_networkDiscovery is NetworkDiscoveryService nds)
            nds.Configure(machineName, port);

        _networkInput.ConnectionLost      += OnNetworkConnectionLost;
        _networkInput.ClientConnected     += OnClientConnected;
        _networkInput.TrustRequested      += OnTrustRequested;
        _networkInput.ProfileListReceived += OnProfileListReceived;

        _ = _networkDiscovery.StartAsync();
        _ = _networkInput.StartListenerAsync(port);

        MarkDirty();
    }

    private void ShutdownNetworking()
    {
        _networkInput.ConnectionLost      -= OnNetworkConnectionLost;
        _networkInput.ClientConnected     -= OnClientConnected;
        _networkInput.TrustRequested      -= OnTrustRequested;
        _networkInput.ProfileListReceived -= OnProfileListReceived;

        _ = _networkDiscovery.StopAsync();
        _ = _networkInput.DisconnectAsync();
    }

    /// <summary>
    /// Start forwarding (thin wrapper for tray menu - actual logic in DevicesTabController).
    /// </summary>
    private void StartForwarding()
    {
        SyncTabContext();
        // Reuse the same logic as the controller by invoking the button path
        if (!_isForwarding)
        {
            var profile = _profileManager.ActiveProfile;
            if (profile is null) return;
            if (profile.AxisMappings.Count == 0 && profile.ButtonMappings.Count == 0 && profile.HatMappings.Count == 0) return;
            _mappingEngine.LoadProfile(profile);
            if (!_vjoyService.IsInitialized) return;
            if (!_mappingEngine.Start()) return;
            _isForwarding = true;
            _trayIcon.SetActive(true);
        }
    }

    /// <summary>
    /// Stop forwarding (thin wrapper for tray menu - actual logic in DevicesTabController).
    /// </summary>
    private void StopForwarding()
    {
        if (!_isForwarding) return;
        _mappingEngine.Stop();
        _isForwarding = false;
        _trayIcon.SetActive(false);
    }

    /// <summary>
    /// Connect to a specific peer as master (called from Settings tab CONNECT button).
    /// Auto-starts the MappingEngine if needed so snapshots are populated.
    /// </summary>
    private async Task ConnectAsMasterAsync(NetworkPeer peer)
    {
        _logger.LogDebug("[ConnectMaster] Enter → {Peer} ({Ip}) | mode={Mode} connectedIp={ConnectedIp} connecting={Connecting}",
            peer.MachineName, peer.IpAddress, _networkMode, _tabContext.ConnectedPeerIp ?? "none", _isNetworkConnecting);

        if (_isNetworkConnecting)
        {
            _logger.LogDebug("[ConnectMaster] BLOCKED — connect already in progress");
            return;
        }

        // Set capture mode first so AcquireDevice in NetworkVJoyService skips real vJoy.
        // This lets MappingEngine.Start() succeed even when CASTRA has no vJoy configured.
        _networkVjoy.ForwardingMode = true;

        // Ensure MappingEngine is running so NetworkVJoyService captures output.
        if (!_isForwarding)
        {
            var profile = _profileManager.ActiveProfile;
            if (profile is not null)
            {
                _mappingEngine.LoadProfile(profile);
                if (_mappingEngine.Start())
                {
                    _isForwarding = true;
                    _trayIcon.SetActive(true);
                    BeginInvoke(UpdateTrayMenu);
                }
            }
        }

        _isNetworkConnecting = true;
        BeginInvoke(MarkDirty);   // disable CONNECT button immediately

        try
        {
            await _networkInput.ConnectToAsync(peer).ConfigureAwait(false);
            _networkMode = NetworkInputMode.Remote;
            _tabContext.NetworkMode = _networkMode;
            _tabContext.ConnectedPeerIp = peer.IpAddress;
            _logger.LogInformation("[ConnectMaster] SUCCESS → {Peer} ({Ip}) | mode={Mode}",
                peer.MachineName, peer.IpAddress, _networkMode);

            // Pre-initialise snapshots for every vJoy device in the active profile so the
            // encoder always sends correctly-sized packets from the very first input tick.
            PreInitializeAllNetworkSnapshots();
            StartNetworkHeartbeat();
            _tabContext.SendProfileListToClient?.Invoke();
            BeginInvoke(() => _trayIcon.ShowBalloonTip("Asteriq", $"Connected to {peer.MachineName}"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Connection failed — roll back capture mode
            _networkVjoy.ForwardingMode = false;
            _tabContext.ConnectedPeerIp = null;
            _logger.LogWarning("[ConnectMaster] FAILED → {Peer} ({Ip}): {Error}",
                peer.MachineName, peer.IpAddress, ex.Message);
            BeginInvoke(() => _trayIcon.ShowBalloonTip("Asteriq",
                $"Could not connect to {peer.MachineName}"));
        }
        finally
        {
            _isNetworkConnecting = false;
            _logger.LogDebug("[ConnectMaster] Exit | mode={Mode} connectedIp={ConnectedIp}",
                _networkMode, _tabContext.ConnectedPeerIp ?? "none");
        }
        BeginInvoke(MarkDirty);
    }

    private async Task SwitchToRemoteAsync()
    {
        // Pick first known peer for MVP (2-machine scenario).
        // Skip staleness check — TCP connect fails fast (<1s) if truly unreachable.
        var peer = _networkDiscovery.KnownPeers.Values.FirstOrDefault();
        if (peer is null)
        {
            BeginInvoke(() => _tabContext.MarkDirty()); // flash handled by status bar
            return;
        }

        // Set capture mode first (same as ConnectAsMasterAsync) so AcquireDevice skips vJoy.
        _networkVjoy.ForwardingMode = true;

        // Ensure MappingEngine is running so snapshots are populated from physical input.
        if (!_isForwarding)
        {
            var profile = _profileManager.ActiveProfile;
            if (profile is not null)
            {
                _mappingEngine.LoadProfile(profile);
                if (_mappingEngine.Start())
                {
                    _isForwarding = true;
                    _trayIcon.SetActive(true);
                    BeginInvoke(UpdateTrayMenu);
                }
            }
        }

        try
        {
            await _networkInput.ConnectToAsync(peer).ConfigureAwait(false);
            _networkMode = NetworkInputMode.Remote;
            _tabContext.NetworkMode = _networkMode;
            _tabContext.ConnectedPeerIp = peer.IpAddress;
            PreInitializeAllNetworkSnapshots();
            StartNetworkHeartbeat();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Connection refused or rejected — roll back
            _networkVjoy.ForwardingMode = false;
            System.Diagnostics.Debug.WriteLine($"[Network] SwitchToRemote failed: {ex.Message}");
        }
        BeginInvoke(MarkDirty);
    }

    private async Task SwitchToLocalAsync()
    {
        _logger.LogDebug("[Disconnect] SwitchToLocalAsync | mode={Mode} connectedIp={ConnectedIp}",
            _networkMode, _tabContext.ConnectedPeerIp ?? "none");
        StopNetworkHeartbeat();
        _networkVjoy.ForwardingMode = false;      // resume local vJoy writes
        await _networkInput.DisconnectAsync().ConfigureAwait(false);
        _networkMode = NetworkInputMode.Local;

        // If we were in client (receiving) mode, reset that state now.
        // ConnectionLost won't fire on a clean explicit disconnect so we reset here.
        if (_isClientConnected)
        {
            _isClientConnected = false;
            _connectedMasterName = "";
            _tabContext.IsClientConnected = false;
        }

        _tabContext.NetworkMode = _networkMode;
        _tabContext.ConnectedPeerIp = null;
        _tabContext.RemoteControlProfiles = new();
        _tabContext.RemoteControlProfilesMasterName = "";
        _logger.LogInformation("[Disconnect] Disconnected — mode=Local");
        BeginInvoke(MarkDirty);
    }

    /// <summary>
    /// Starts a 20 Hz background loop that sends the current vJoy snapshot to the client
    /// even when no physical input events are generated (stick at rest, etc.).
    /// Replaces any previously running heartbeat.
    /// </summary>
    private void StartNetworkHeartbeat()
    {
        StopNetworkHeartbeat();
        var cts = new CancellationTokenSource();
        _heartbeatCts = cts;
        _ = RunNetworkHeartbeatAsync(cts.Token);
    }

    private void StopNetworkHeartbeat()
    {
        _heartbeatCts?.Cancel();
        _heartbeatCts?.Dispose();
        _heartbeatCts = null;
    }

    private async Task RunNetworkHeartbeatAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(50, ct).ConfigureAwait(false); // 20 Hz

                if (_networkMode != NetworkInputMode.Remote || !_networkVjoy.ForwardingMode)
                    break;

                if (!_tabContext.SuppressForwarding)
                {
                    foreach (var snapshot in _networkVjoy.GetAllSnapshots())
                        _networkInput.SendVJoyState(snapshot);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[Network] Heartbeat error: {ex.Message}");
        }
    }

    private void OnNetworkConnectionLost(object? sender, EventArgs e)
    {
        _logger.LogWarning("[ConnLost] ConnectionLost event | mode={Mode} connectedIp={ConnectedIp}",
            _networkMode, _tabContext.ConnectedPeerIp ?? "none");
        StopNetworkHeartbeat();
        _networkVjoy.ForwardingMode = false;      // resume local vJoy writes on disconnect
        _networkMode = NetworkInputMode.Local;
        BeginInvoke(() => _trayIcon.ShowBalloonTip("Asteriq", "Network connection lost"));
        BeginInvoke(() =>
        {
            // If we were in client mode, unlock tabs
            if (_isClientConnected)
            {
                _isClientConnected = false;
                _connectedMasterName = "";
            }
            _tabContext.NetworkMode = _networkMode;
            _tabContext.IsClientConnected = _isClientConnected;
            _tabContext.ConnectedPeerIp = null;
            _tabContext.RemoteControlProfiles = new();
            _tabContext.RemoteControlProfilesMasterName = "";
            MarkDirty();
        });
    }

    private void OnTrustRequested(object? sender, TrustRequestEventArgs e)
    {
        BeginInvoke(() =>
        {
            using var dlg = new TrustRequestDialog(e.PeerName, e.Code);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _networkInput.AcceptPairing();
                _appSettings.TrustedMaster = new TrustedPeerConfig
                {
                    MachineName = e.PeerName,
                    Code        = e.Code,
                    LastIp      = e.IpAddress
                };
                EnterClientMode(e.PeerName);
            }
            else
            {
                _networkInput.RejectPairing();
            }
        });
    }

    private void OnClientConnected(object? sender, string masterName)
    {
        // Fired on the background receive thread — marshal to UI thread
        BeginInvoke(() => EnterClientMode(masterName));
    }

    private void OnProfileListReceived(object? sender, ProfileListReceivedEventArgs e)
    {
        // Arrives on background receive thread — marshal to UI
        BeginInvoke(() =>
        {
            _tabContext.RemoteControlProfiles = e.Profiles.ToList();
            _tabContext.RemoteControlProfilesMasterName = _connectedMasterName;
            _logger.LogInformation("Received {Count} control profile(s) from {Master}",
                e.Profiles.Count, _connectedMasterName);
            MarkDirty();
        });
    }

    private void EnterClientMode(string masterName)
    {
        _trayIcon.ShowBalloonTip("Asteriq", $"Receiving input from {masterName}");
        _isClientConnected   = true;
        _connectedMasterName = masterName;
        _networkMode         = NetworkInputMode.Receiving;
        _tabContext.NetworkMode       = _networkMode;
        _tabContext.IsClientConnected = _isClientConnected;

        // Refresh vJoy device list so the SC Keybindings tab shows JS columns.
        // This is necessary because the first render of the tab in client mode uses
        // _ctx.VJoyDevices which may not have been synced since startup.
        RefreshVJoyDevicesInternal();

        // Force to Settings tab (the only tab freely accessible in client mode)
        if (_activeTab < 2)
        {
            if (_activeTab == 1) _mappingsController.OnDeactivated();
            _activeTab = 3;
        }
        UpdateTrayMenu();
        MarkDirty();
    }

    /// <summary>
    /// Pre-initialises snapshots for every vJoy device referenced in the active profile.
    /// Falls back to device 1 if the profile has no mappings.
    /// </summary>
    private void PreInitializeAllNetworkSnapshots()
    {
        var profile = _profileManager.ActiveProfile;
        IEnumerable<uint> deviceIds = profile is null
            ? [1u]
            : profile.AxisMappings.Select(m => m.Output.VJoyDevice)
                .Concat(profile.ButtonMappings.Select(m => m.Output.VJoyDevice))
                .Concat(profile.HatMappings.Select(m => m.Output.VJoyDevice))
                .Where(id => id > 0)
                .Distinct();

        bool any = false;
        foreach (var id in deviceIds)
        {
            PreInitializeNetworkSnapshot(id);
            any = true;
        }
        if (!any) PreInitializeNetworkSnapshot(1u);
    }

    /// <summary>
    /// Pre-populates the NetworkVJoyService snapshot for <paramref name="deviceId"/> with
    /// the axis/button/hat capacities reported by the vJoy driver.  Falls back to safe
    /// maximums if the device info cannot be obtained.
    /// </summary>
    private void PreInitializeNetworkSnapshot(uint deviceId)
    {
        try
        {
            var info = _networkVjoy.GetDeviceInfo(deviceId);
            int axisCount = ComputeVJoyAxisCount(info);
            int hatCount  = Math.Max(info.ContPovCount, info.DiscPovCount);
            _networkVjoy.PreInitializeSnapshot(deviceId, axisCount, info.ButtonCount, hatCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[Network] GetDeviceInfo({deviceId}) failed: {ex.Message}; using maximum capacities");
            _networkVjoy.PreInitializeSnapshot(deviceId, 8, 128, 4);
        }
    }

    /// <summary>Returns the highest-indexed axis + 1 for the given vJoy device.</summary>
    private static int ComputeVJoyAxisCount(VJoyDeviceInfo info)
    {
        int max = -1;
        if (info.HasAxisX)   max = Math.Max(max, 0);
        if (info.HasAxisY)   max = Math.Max(max, 1);
        if (info.HasAxisZ)   max = Math.Max(max, 2);
        if (info.HasAxisRX)  max = Math.Max(max, 3);
        if (info.HasAxisRY)  max = Math.Max(max, 4);
        if (info.HasAxisRZ)  max = Math.Max(max, 5);
        if (info.HasSlider0) max = Math.Max(max, 6);
        if (info.HasSlider1) max = Math.Max(max, 7);
        return max + 1;
    }

    #endregion
}
