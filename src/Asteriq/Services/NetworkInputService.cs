using System.Net;
using System.Net.Sockets;
using Asteriq.Models;
using Asteriq.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Asteriq.Services;

/// <summary>
/// Manages TCP-based raw input forwarding between two Asteriq instances.
///
/// Protocol framing:
///   [4] uint32 magic = 0x41535459 ("ASTY")
///   [1] byte   MessageType
///   [4] uint32 payloadLength
///   [N] payload bytes
///
/// MessageType values: see <see cref="MsgType"/>.
/// </summary>
public sealed class NetworkInputService : INetworkInputService
{
    // ── Packet constants ─────────────────────────────────────────────────────
    private const uint Magic = 0x41535459u; // "ASTY"

    private static class MsgType
    {
        public const byte Input = 0;
        public const byte Acquire = 1;
        public const byte Release = 2;
        public const byte Ping = 3;
        public const byte Pong = 4;
        public const byte Hello = 5;       // source→receiver: {nameLen,name,port[2],codeLen,code}
        public const byte HelloAccept = 6; // receiver→source: empty
        public const byte HelloReject = 7; // receiver→source: empty
    }

    // ── Services ─────────────────────────────────────────────────────────────
    private readonly IVJoyService _vjoy;
    private readonly ILogger<NetworkInputService> _logger;

    // ── State ────────────────────────────────────────────────────────────────
    private volatile NetworkInputMode _mode = NetworkInputMode.Local;
    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _listenerCts;
    private CancellationTokenSource? _receiveCts;
    private readonly object _sendLock = new();

    // ── Pairing ───────────────────────────────────────────────────────────────
    // Set when a receiver-side connection arrives; UI reads these to show PairingCodeDialog.
    private TaskCompletionSource<bool>? _pairingTcs; // true=accepted, false=rejected
    private string _pendingPeerName = "";
    private string _pendingCode = "";
    private TcpClient? _pendingClient;
    private NetworkStream? _pendingStream;

    // ── Events ───────────────────────────────────────────────────────────────
    public event EventHandler? ConnectionLost;

    /// <summary>
    /// Fired on the receiver side when a remote peer requests to connect.
    /// EventArgs: <see cref="PairingRequestEventArgs"/> — contains machine name and 6-digit code.
    /// UI should show the code, then call <see cref="AcceptPairingAsync"/> or <see cref="RejectPairing"/>.
    /// </summary>
    public event EventHandler<PairingRequestEventArgs>? PairingRequested;

    public NetworkInputMode Mode => _mode;
    public bool IsListening => _listener is not null;

    public NetworkInputService(IVJoyService vjoy, ILogger<NetworkInputService> logger)
    {
        _vjoy = vjoy ?? throw new ArgumentNullException(nameof(vjoy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── Listener (receiver side) ─────────────────────────────────────────────

    public async Task StartListenerAsync(int port, CancellationToken cancellationToken = default)
    {
        if (_listener is not null) return;

        _listenerCts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();

        _logger.LogInformation("NetworkInput listener started on port {Port}", port);
        _ = RunAcceptLoopAsync(_listenerCts.Token);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task RunAcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                client.NoDelay = true;
                _logger.LogInformation("Incoming connection from {Remote}", client.Client.RemoteEndPoint);
                _ = HandleIncomingHandshakeAsync(client, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Accept loop error");
            }
        }
    }

    private async Task HandleIncomingHandshakeAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            var stream = client.GetStream();
            var (type, payload) = await ReadPacketAsync(stream, ct).ConfigureAwait(false);
            if (type != MsgType.Hello)
            {
                _logger.LogWarning("Expected Hello, got {Type}", type);
                client.Dispose();
                return;
            }

            var (peerName, _, code) = DecodeHello(payload);
            _logger.LogInformation("Hello from {Peer} with code {Code}", peerName, code);

            _pendingPeerName = peerName;
            _pendingCode = code;
            _pendingClient = client;
            _pendingStream = stream;
            _pairingTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            PairingRequested?.Invoke(this, new PairingRequestEventArgs(peerName, code));

            // Wait for UI response (accept/reject)
            bool accepted;
            try
            {
                accepted = await _pairingTcs.Task.WaitAsync(TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Pairing confirmation timed out");
                accepted = false;
            }

            if (!accepted)
            {
                await WritePacketAsync(stream, MsgType.HelloReject, [], ct).ConfigureAwait(false);
                client.Dispose();
                _logger.LogInformation("Pairing rejected for {Peer}", peerName);
                return;
            }

            await WritePacketAsync(stream, MsgType.HelloAccept, [], ct).ConfigureAwait(false);
            _client = client;
            _stream = stream;

            _logger.LogInformation("Pairing accepted, starting receive loop for {Peer}", peerName);
            _ = RunReceiveLoopAsync(stream, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Handshake error");
            client.Dispose();
        }
    }

    /// <summary>Call from UI after showing PairingCodeDialog to accept the connection.</summary>
    public void AcceptPairing() => _pairingTcs?.TrySetResult(true);

    /// <summary>Call from UI after showing PairingCodeDialog to reject the connection.</summary>
    public void RejectPairing() => _pairingTcs?.TrySetResult(false);

    // ── Connect (source side) ─────────────────────────────────────────────────

    public async Task ConnectToAsync(NetworkPeer peer, CancellationToken cancellationToken = default)
    {
        await DisconnectAsync(cancellationToken).ConfigureAwait(false);

        var code = GeneratePairingCode();
        _logger.LogInformation("Connecting to {Peer} @ {Ip}:{Port} with code {Code}",
            peer.MachineName, peer.IpAddress, peer.TcpPort, code);

        var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(peer.IpAddress, peer.TcpPort, cancellationToken).ConfigureAwait(false);

        var stream = client.GetStream();

        // Send HELLO with pairing code
        var helloPayload = EncodeHello(Environment.MachineName, (ushort)peer.TcpPort, code);
        await WritePacketAsync(stream, MsgType.Hello, helloPayload, cancellationToken).ConfigureAwait(false);

        // Notify UI to display code — source side shows the same code it sent
        PairingRequested?.Invoke(this, new PairingRequestEventArgs(peer.MachineName, code, isSource: true));

        // Wait for accept/reject from receiver
        var (responseType, _) = await ReadPacketAsync(stream, cancellationToken).ConfigureAwait(false);
        if (responseType != MsgType.HelloAccept)
        {
            _logger.LogWarning("Connection to {Peer} rejected", peer.MachineName);
            client.Dispose();
            throw new InvalidOperationException($"Connection to {peer.MachineName} was rejected.");
        }

        _client = client;
        _stream = stream;
        _mode = NetworkInputMode.Remote;

        // Send ACQUIRE so receiver grabs vJoy
        await WritePacketAsync(stream, MsgType.Acquire, [], cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Connected to {Peer}, mode=Remote", peer.MachineName);

        // Start ping keepalive
        _receiveCts = new CancellationTokenSource();
        _ = RunPingAsync(stream, _receiveCts.Token);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_stream is not null)
        {
            try
            {
                await WritePacketAsync(_stream, MsgType.Release, [], cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Release send error (ignored)");
            }
        }

        _receiveCts?.Cancel();
        _receiveCts?.Dispose();
        _receiveCts = null;

        _stream?.Dispose();
        _stream = null;
        _client?.Dispose();
        _client = null;
        _mode = NetworkInputMode.Local;

        _logger.LogInformation("NetworkInput disconnected, mode=Local");
    }

    // ── Send (source, hot path) ───────────────────────────────────────────────

    public void SendInputState(DeviceInputState state, byte deviceSlot)
    {
        if (_mode != NetworkInputMode.Remote || _stream is null) return;

        lock (_sendLock)
        {
            try
            {
                // Write synchronously inside the lock — stream is buffered, NoDelay flushes quickly
                WriteInputPacketSync(_stream, state, deviceSlot);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "SendInputState failed — connection lost");
                _mode = NetworkInputMode.Local;
                ConnectionLost?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    // ── Receive loop (receiver side) ─────────────────────────────────────────

    private async Task RunReceiveLoopAsync(NetworkStream stream, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var (type, payload) = await ReadPacketAsync(stream, ct).ConfigureAwait(false);
                switch (type)
                {
                    case MsgType.Acquire:
                        _vjoy.AcquireDevice(1);
                        break;

                    case MsgType.Release:
                        _vjoy.ResetDevice(1);
                        _vjoy.ReleaseDevice(1);
                        _logger.LogInformation("Remote released vJoy slot 1");
                        break;

                    case MsgType.Input:
                        var (_, inputState) = DecodeInputPayload(payload);
                        ApplyToVJoy(inputState, deviceId: 1);
                        break;

                    case MsgType.Ping:
                        await WritePacketAsync(stream, MsgType.Pong, [], ct).ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Receive loop error — connection lost");
            ConnectionLost?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _vjoy.ResetDevice(1);
            _vjoy.ReleaseDevice(1);
        }
    }

    // ── Ping keepalive (source side) ─────────────────────────────────────────

    private async Task RunPingAsync(NetworkStream stream, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(3000, ct).ConfigureAwait(false);
                await WritePacketAsync(stream, MsgType.Ping, [], ct).ConfigureAwait(false);
                // Don't wait for pong — if connection is dead, SendInputState will catch it
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ping failed — connection lost");
            _mode = NetworkInputMode.Local;
            ConnectionLost?.Invoke(this, EventArgs.Empty);
        }
    }

    // ── Codec ─────────────────────────────────────────────────────────────────

    /// <summary>Encode an INPUT payload from DeviceInputState.</summary>
    public static byte[] EncodeInputPayload(DeviceInputState state, byte deviceSlot)
    {
        int axisCount = Math.Min(state.Axes.Length, 255);
        int buttonCount = Math.Min(state.Buttons.Length, 65535);
        int hatCount = Math.Min(state.Hats.Length, 255);
        int buttonByteCount = (buttonCount + 7) / 8;

        int size = 1 + 1 + axisCount * 4 + 2 + buttonByteCount + 1 + hatCount * 4;
        using var ms = new System.IO.MemoryStream(size);
        using var w = new System.IO.BinaryWriter(ms);

        w.Write(deviceSlot);
        w.Write((byte)axisCount);
        for (int i = 0; i < axisCount; i++) w.Write(state.Axes[i]);

        w.Write((ushort)buttonCount);
        var buttonBytes = new byte[buttonByteCount];
        for (int i = 0; i < buttonCount; i++)
        {
            if (state.Buttons[i])
                buttonBytes[i / 8] |= (byte)(1 << (i % 8));
        }
        w.Write(buttonBytes);

        w.Write((byte)hatCount);
        for (int i = 0; i < hatCount; i++) w.Write(state.Hats[i]);

        return ms.ToArray();
    }

    /// <summary>Decode an INPUT payload to DeviceInputState (DeviceIndex = deviceSlot).</summary>
    public static (byte DeviceSlot, DeviceInputState State) DecodeInputPayload(byte[] payload)
    {
        using var ms = new System.IO.MemoryStream(payload);
        using var r = new System.IO.BinaryReader(ms);

        byte deviceSlot = r.ReadByte();
        int axisCount = r.ReadByte();
        var axes = new float[axisCount];
        for (int i = 0; i < axisCount; i++) axes[i] = r.ReadSingle();

        int buttonCount = r.ReadUInt16();
        int buttonByteCount = (buttonCount + 7) / 8;
        var buttonBytes = r.ReadBytes(buttonByteCount);
        var buttons = new bool[buttonCount];
        for (int i = 0; i < buttonCount; i++)
            buttons[i] = (buttonBytes[i / 8] & (1 << (i % 8))) != 0;

        int hatCount = r.ReadByte();
        var hats = new int[hatCount];
        for (int i = 0; i < hatCount; i++) hats[i] = r.ReadInt32();

        var state = new DeviceInputState
        {
            DeviceIndex = deviceSlot,
            Axes = axes,
            Buttons = buttons,
            Hats = hats,
            Timestamp = DateTime.UtcNow
        };
        return (deviceSlot, state);
    }

    private static byte[] EncodeHello(string machineName, ushort tcpPort, string code)
    {
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(machineName);
        var codeBytes = System.Text.Encoding.UTF8.GetBytes(code);
        using var ms = new System.IO.MemoryStream();
        using var w = new System.IO.BinaryWriter(ms);
        w.Write((byte)nameBytes.Length);
        w.Write(nameBytes);
        w.Write(tcpPort);
        w.Write((byte)codeBytes.Length);
        w.Write(codeBytes);
        return ms.ToArray();
    }

    private static (string Name, ushort Port, string Code) DecodeHello(byte[] payload)
    {
        using var ms = new System.IO.MemoryStream(payload);
        using var r = new System.IO.BinaryReader(ms);
        int nameLen = r.ReadByte();
        var name = System.Text.Encoding.UTF8.GetString(r.ReadBytes(nameLen));
        ushort port = r.ReadUInt16();
        int codeLen = r.ReadByte();
        var code = System.Text.Encoding.UTF8.GetString(r.ReadBytes(codeLen));
        return (name, port, code);
    }

    private void ApplyToVJoy(DeviceInputState state, uint deviceId)
    {
        for (int i = 0; i < state.Axes.Length && i < 8; i++)
            _vjoy.SetAxis(deviceId, VJoyAxisHelper.IndexToHidUsage(i), state.Axes[i]);

        for (int i = 0; i < state.Buttons.Length; i++)
            _vjoy.SetButton(deviceId, i + 1, state.Buttons[i]); // vJoy buttons are 1-indexed

        for (int i = 0; i < state.Hats.Length; i++)
            _vjoy.SetContinuousPov(deviceId, (uint)i, state.Hats[i]);
    }

    // ── Packet I/O ────────────────────────────────────────────────────────────

    private static async Task<(byte Type, byte[] Payload)> ReadPacketAsync(
        NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[9]; // 4 magic + 1 type + 4 length
        await ReadExactAsync(stream, header, 9, ct).ConfigureAwait(false);

        uint magic = BitConverter.ToUInt32(header, 0);
        if (magic != Magic)
            throw new InvalidDataException($"Bad magic: 0x{magic:X8}");

        byte type = header[4];
        uint length = BitConverter.ToUInt32(header, 5);
        if (length > 65536)
            throw new InvalidDataException($"Payload too large: {length}");

        var payload = new byte[length];
        if (length > 0)
            await ReadExactAsync(stream, payload, (int)length, ct).ConfigureAwait(false);

        return (type, payload);
    }

    private static async Task WritePacketAsync(
        NetworkStream stream, byte type, byte[] payload, CancellationToken ct)
    {
        var header = new byte[9];
        BitConverter.TryWriteBytes(header.AsSpan(0), Magic);
        header[4] = type;
        BitConverter.TryWriteBytes(header.AsSpan(5), (uint)payload.Length);

        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        if (payload.Length > 0)
            await stream.WriteAsync(payload, ct).ConfigureAwait(false);
    }

    private void WriteInputPacketSync(NetworkStream stream, DeviceInputState state, byte deviceSlot)
    {
        var payload = EncodeInputPayload(state, deviceSlot);
        var header = new byte[9];
        BitConverter.TryWriteBytes(header.AsSpan(0), Magic);
        header[4] = MsgType.Input;
        BitConverter.TryWriteBytes(header.AsSpan(5), (uint)payload.Length);
        stream.Write(header);
        stream.Write(payload);
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken ct)
    {
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("Connection closed");
            offset += read;
        }
    }

    private static string GeneratePairingCode()
    {
        var rng = Random.Shared;
        return $"{rng.Next(0, 10)}{rng.Next(0, 10)}{rng.Next(0, 10)}{rng.Next(0, 10)}{rng.Next(0, 10)}{rng.Next(0, 10)}";
    }

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
        _listenerCts?.Cancel();
        _listenerCts?.Dispose();
        _listener?.Stop();
    }
}

/// <summary>
/// Event args for a pairing request from a remote Asteriq instance.
/// </summary>
public sealed class PairingRequestEventArgs : EventArgs
{
    /// <summary>Machine name of the remote peer.</summary>
    public string PeerName { get; }

    /// <summary>6-digit pairing code to display.</summary>
    public string Code { get; }

    /// <summary>
    /// True when fired on the source (initiating) side — the code was generated locally.
    /// False when fired on the receiver side — the code was received from the remote.
    /// </summary>
    public bool IsSource { get; }

    public PairingRequestEventArgs(string peerName, string code, bool isSource = false)
    {
        PeerName = peerName;
        Code = code;
        IsSource = isSource;
    }
}
