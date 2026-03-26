using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Asteriq.Models;
using Asteriq.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Asteriq.Services;

/// <summary>
/// Discovers remote Asteriq instances on the LAN via UDP broadcast.
/// Broadcasts own presence every 5 s and prunes peers not heard in 15 s.
/// </summary>
public sealed class NetworkDiscoveryService : INetworkDiscoveryService, IDisposable
{
    private const int BroadcastPort = 47191;
    private const string BroadcastPrefix = "ASTERIQ:v1:";
    private const int BroadcastIntervalMs = 5000;
    private const int PruneIntervalMs = 5000;

    private readonly ILogger<NetworkDiscoveryService> _logger;
    private readonly ConcurrentDictionary<string, NetworkPeer> _peers = new();
    private readonly HashSet<string> _ownIps = new();

    private CancellationTokenSource? _cts;
    private Task? _broadcastTask;
    private Task? _listenTask;
    private System.Threading.Timer? _pruneTimer;

    private string _machineName = Environment.MachineName;
    private int _tcpPort = BroadcastPort;
    private NetworkRole _role = NetworkRole.None;

    public event EventHandler? PeersChanged;

    public IReadOnlyDictionary<string, NetworkPeer> KnownPeers => _peers;

    public NetworkDiscoveryService(ILogger<NetworkDiscoveryService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        DetectOwnIps();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        // Only Client (RX) machines broadcast their availability.
        // Master (TX) machines only listen — they don't need to advertise.
        if (_role == NetworkRole.Client)
            _broadcastTask = RunBroadcastAsync(ct);

        _listenTask = RunListenAsync(ct);
        _pruneTimer = new System.Threading.Timer(_ => PruneStale(), null, PruneIntervalMs, PruneIntervalMs);

        _logger.LogInformation("NetworkDiscovery started (machine={Machine}, port={Port}, role={Role}, broadcasting={Broadcast})",
            _machineName, _tcpPort, _role, _role == NetworkRole.Client);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts is null) return;

        await _cts.CancelAsync().ConfigureAwait(false);

        try
        {
            if (_broadcastTask is not null) await _broadcastTask.ConfigureAwait(false);
            if (_listenTask is not null) await _listenTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "NetworkDiscovery stop exception (expected on cancel)");
        }

        _pruneTimer?.Dispose();
        _pruneTimer = null;
        _cts.Dispose();
        _cts = null;
        _peers.Clear();

        _logger.LogInformation("NetworkDiscovery stopped");
    }

    /// <summary>
    /// Update the advertised machine name and TCP port before calling StartAsync.
    /// </summary>
    public void Configure(string machineName, int tcpPort, NetworkRole role = NetworkRole.None)
    {
        _machineName = string.IsNullOrWhiteSpace(machineName) ? Environment.MachineName : machineName;
        _tcpPort = tcpPort;
        _role = role;
    }

    private async Task RunBroadcastAsync(CancellationToken ct)
    {
        using var udp = new UdpClient();
        udp.EnableBroadcast = true;

        var endpoint = new IPEndPoint(IPAddress.Broadcast, BroadcastPort);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var msg = Encoding.UTF8.GetBytes($"{BroadcastPrefix}{_machineName}:{_tcpPort}:{_role}");
                await udp.SendAsync(msg, msg.Length, endpoint).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "UDP broadcast error");
            }

            try
            {
                await Task.Delay(BroadcastIntervalMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunListenAsync(CancellationToken ct)
    {
        using var udp = new UdpClient(BroadcastPort);
        udp.EnableBroadcast = true;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(ct).ConfigureAwait(false);
                var ip = result.RemoteEndPoint.Address.ToString();

                if (_ownIps.Contains(ip)) continue;

                var text = Encoding.UTF8.GetString(result.Buffer);
                if (!text.StartsWith(BroadcastPrefix, StringComparison.Ordinal)) continue;

                var parts = text[BroadcastPrefix.Length..].Split(':');
                if (parts.Length < 2) continue;

                // Format: name:port[:role]  — role is optional for backwards compatibility
                var role = NetworkRole.None;
                if (parts.Length >= 3 && Enum.TryParse<NetworkRole>(parts[^1], out var parsedRole))
                {
                    if (!int.TryParse(parts[^2], out int port2)) continue;
                    var name2 = string.Join(":", parts[..^2]);
                    var peer2 = _peers.AddOrUpdate(ip,
                        _ => new NetworkPeer { MachineName = name2, IpAddress = ip, TcpPort = port2, Role = parsedRole, LastSeen = DateTime.UtcNow },
                        (_, existing) =>
                        {
                            existing.MachineName = name2;
                            existing.TcpPort = port2;
                            existing.Role = parsedRole;
                            existing.LastSeen = DateTime.UtcNow;
                            return existing;
                        });
                    _logger.LogDebug("Peer discovered: {Machine} @ {Ip}:{Port} role={Role}", peer2.MachineName, ip, port2, parsedRole);
                    PeersChanged?.Invoke(this, EventArgs.Empty);
                    continue;
                }

                if (!int.TryParse(parts[^1], out int port)) continue;
                var name = string.Join(":", parts[..^1]);

                var peer = _peers.AddOrUpdate(ip,
                    _ => new NetworkPeer { MachineName = name, IpAddress = ip, TcpPort = port, Role = role, LastSeen = DateTime.UtcNow },
                    (_, existing) =>
                    {
                        existing.MachineName = name;
                        existing.TcpPort = port;
                        existing.Role = role;
                        existing.LastSeen = DateTime.UtcNow;
                        return existing;
                    });

                _logger.LogDebug("Peer discovered: {Machine} @ {Ip}:{Port}", peer.MachineName, ip, port);
                PeersChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "UDP listen error");
            }
        }
    }

    private void PruneStale()
    {
        bool changed = false;
        foreach (var kv in _peers)
        {
            if (kv.Value.IsStale)
            {
                _peers.TryRemove(kv.Key, out _);
                _logger.LogDebug("Peer pruned (stale): {Machine}", kv.Value.MachineName);
                changed = true;
            }
        }
        if (changed) PeersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DetectOwnIps()
    {
        try
        {
            _ownIps.Add("127.0.0.1");
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var addr in host.AddressList)
                _ownIps.Add(addr.ToString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not enumerate own IP addresses");
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _pruneTimer?.Dispose();
    }
}
