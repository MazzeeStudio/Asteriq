using Asteriq.Models;
using Asteriq.Services.Abstractions;

namespace Asteriq.Services;

/// <summary>
/// No-op discovery service used when networking is disabled or unavailable.
/// </summary>
internal sealed class NullNetworkDiscoveryService : INetworkDiscoveryService
{
    private static readonly Dictionary<string, NetworkPeer> s_empty = new();
    public IReadOnlyDictionary<string, NetworkPeer> KnownPeers => s_empty;
    public event EventHandler? PeersChanged { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// No-op input service used when networking is disabled or unavailable.
/// </summary>
internal sealed class NullNetworkInputService : INetworkInputService
{
    public NetworkInputMode Mode => NetworkInputMode.Local;
    public bool IsListening => false;
    public int PacketsReceived => 0;
    public event EventHandler? ConnectionLost { add { } remove { } }
    public event EventHandler<string>? ClientConnected { add { } remove { } }
    public event EventHandler<TrustRequestEventArgs>? TrustRequested { add { } remove { } }
    public event EventHandler<ProfileListReceivedEventArgs>? ProfileListReceived { add { } remove { } }
    public event EventHandler<VJoyConfigReceivedEventArgs>? VJoyConfigReceived { add { } remove { } }
    public Task StartListenerAsync(int port, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ConnectToAsync(NetworkPeer peer, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void SendVJoyState(VJoyOutputSnapshot snapshot) { }
    public void SendVJoyConfig(VJoyDeviceInfo deviceInfo) { }
    public void SendProfileList(IReadOnlyList<(string Name, byte[] XmlBytes)> profiles) { }
    public void AcceptPairing() { }
    public void RejectPairing() { }
    public void Dispose() { }
}
