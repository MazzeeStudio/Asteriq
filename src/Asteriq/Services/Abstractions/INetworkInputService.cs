using Asteriq.Models;

namespace Asteriq.Services.Abstractions;

/// <summary>
/// Network input mode — which machine is currently active.
/// </summary>
public enum NetworkInputMode
{
    /// <summary>Input is processed locally by MappingEngine.</summary>
    Local,

    /// <summary>
    /// Master mode: input runs through MappingEngine (capture, no local vJoy write),
    /// and the resulting vJoy snapshot is forwarded over TCP to the client.
    /// </summary>
    Remote,

    /// <summary>
    /// Client mode: this machine is receiving vJoy snapshots from a master
    /// and applying them to a local vJoy slot. MappingEngine is bypassed.
    /// </summary>
    Receiving
}

/// <summary>
/// Manages the TCP connection between master (sender) and client (receiver) machines.
/// Master sends post-mapped <see cref="VJoyOutputSnapshot"/> packets;
/// client applies them directly to its local vJoy slot.
/// </summary>
public interface INetworkInputService : IDisposable
{
    /// <summary>Current forwarding/receiving mode.</summary>
    NetworkInputMode Mode { get; }

    /// <summary>True when the TCP listener is running (client / receiver side).</summary>
    bool IsListening { get; }

    /// <summary>
    /// Number of INPUT packets received since the current connection was established.
    /// Zero on the master; increments on the client for every vJoy snapshot applied.
    /// Resets to zero on disconnect.
    /// </summary>
    int PacketsReceived { get; }

    /// <summary>
    /// Start the TCP listener so a remote master can connect.
    /// Called at startup when NetworkEnabled = true.
    /// </summary>
    Task StartListenerAsync(int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// Connect to a remote peer (client) and begin forwarding mode.
    /// Sends ACQUIRE packet so the receiver takes ownership of its vJoy slot.
    /// </summary>
    Task ConnectToAsync(NetworkPeer peer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect from the current peer and return to Local mode.
    /// Sends RELEASE packet before disconnecting.
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a post-mapped vJoy output snapshot to the remote client.
    /// Called on the background input thread — must be thread-safe and non-blocking.
    /// No-op when Mode != Remote or not connected.
    /// </summary>
    void SendVJoyState(VJoyOutputSnapshot snapshot);

    /// <summary>
    /// Send the given control profile to the connected client.
    /// The client will save and activate it automatically.
    /// No-op when not connected as master.
    /// </summary>
    void SendProfile(MappingProfile profile);

    /// <summary>
    /// Accept the pending trust request (called after the user approves in the trust dialog).
    /// </summary>
    void AcceptPairing();

    /// <summary>
    /// Reject the pending trust request (called after the user dismisses the trust dialog).
    /// </summary>
    void RejectPairing();

    /// <summary>
    /// Fired on the receive thread when a profile packet arrives from the master.
    /// Caller must marshal to the UI thread before touching UI state.
    /// </summary>
    event EventHandler<MappingProfile>? ProfileReceived;

    /// <summary>
    /// Fired on the background receive thread when the TCP connection is lost unexpectedly.
    /// Caller should BeginInvoke back to UI thread and call DisconnectAsync / update mode.
    /// </summary>
    event EventHandler? ConnectionLost;

    /// <summary>
    /// Fired on the client side when a connection from a master is successfully established
    /// (handshake complete, receive loop started). Includes the master's machine name.
    /// Caller should BeginInvoke back to UI thread to enter client mode.
    /// </summary>
    event EventHandler<string>? ClientConnected;

    /// <summary>
    /// Fired on the client side when an unrecognised master requests to connect.
    /// UI should show a one-time trust dialog and call AcceptPairing() or RejectPairing().
    /// </summary>
    event EventHandler<TrustRequestEventArgs>? TrustRequested;
}

/// <summary>
/// Event args for a trust request from an unknown master instance.
/// </summary>
public sealed class TrustRequestEventArgs : EventArgs
{
    /// <summary>Machine name of the connecting master.</summary>
    public string PeerName { get; }

    /// <summary>6-digit code sent by the master (to display for user verification).</summary>
    public string Code { get; }

    /// <summary>Remote IP address of the connecting master.</summary>
    public string IpAddress { get; }

    public TrustRequestEventArgs(string peerName, string code, string ipAddress)
    {
        PeerName  = peerName;
        Code      = code;
        IpAddress = ipAddress;
    }
}
