using Asteriq.Models;

namespace Asteriq.Services.Abstractions;

/// <summary>
/// Network input mode — which machine is currently active.
/// </summary>
public enum NetworkInputMode
{
    /// <summary>Input is processed locally by MappingEngine.</summary>
    Local,
    /// <summary>Input is forwarded over TCP to the remote peer's vJoy.</summary>
    Remote
}

/// <summary>
/// Manages the TCP connection between source (sender) and receiver machines.
/// Source sends raw DeviceInputState packets; receiver applies them directly to vJoy.
/// </summary>
public interface INetworkInputService : IDisposable
{
    /// <summary>Current forwarding mode.</summary>
    NetworkInputMode Mode { get; }

    /// <summary>True when the TCP listener is running (receiver side).</summary>
    bool IsListening { get; }

    /// <summary>
    /// Start the TCP listener so remote peers can connect.
    /// Called at startup when NetworkEnabled = true.
    /// </summary>
    Task StartListenerAsync(int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// Connect to a remote peer and begin forwarding mode.
    /// Sends ACQUIRE packet so the receiver takes ownership of its vJoy slot.
    /// </summary>
    Task ConnectToAsync(NetworkPeer peer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect from the current peer and return to Local mode.
    /// Sends RELEASE packet before disconnecting.
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a raw device input state to the remote peer.
    /// Called on the background input thread — must be thread-safe and non-blocking.
    /// No-op when Mode == Local or not connected.
    /// </summary>
    void SendInputState(DeviceInputState state, byte deviceSlot);

    /// <summary>
    /// Fired on the background receive thread when the TCP connection is lost unexpectedly.
    /// Caller should BeginInvoke back to UI thread and call SwitchToLocal.
    /// </summary>
    event EventHandler? ConnectionLost;
}
