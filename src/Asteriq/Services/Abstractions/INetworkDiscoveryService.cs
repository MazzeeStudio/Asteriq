using Asteriq.Models;

namespace Asteriq.Services.Abstractions;

/// <summary>
/// Discovers remote Asteriq instances on the LAN via UDP broadcast.
/// </summary>
public interface INetworkDiscoveryService
{
    /// <summary>Start broadcasting presence and listening for peers.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stop broadcasting and listening. Clears the peer list.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>All currently-known (non-stale) peers indexed by IP address string.</summary>
    IReadOnlyDictionary<string, NetworkPeer> KnownPeers { get; }

    /// <summary>Fired whenever the peer list changes (peer added, updated, or pruned).</summary>
    event EventHandler? PeersChanged;
}
