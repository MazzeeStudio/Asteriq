using System.Net;
using System.Net.Sockets;
using Asteriq.Models;
using Asteriq.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Asteriq.Services;

/// <summary>
/// Manages TCP-based vJoy state forwarding between two Asteriq instances.
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
        public const byte Input       = 0;
        public const byte Acquire     = 1;
        public const byte Release     = 2;
        public const byte Ping        = 3;
        public const byte Pong        = 4;
        public const byte Hello       = 5; // master→client: {nameLen,name,port[2],codeLen,code}
        public const byte HelloAccept = 6; // client→master: empty
        public const byte HelloReject = 7; // client→master: empty
        public const byte ProfileList  = 8; // master→client: [count:4][nameLen:2][name][xmlLen:4][xml] × count
    }

    // ── Services ─────────────────────────────────────────────────────────────
    private readonly IVJoyService _vjoy;
    private readonly IApplicationSettingsService _settings;
    private readonly ILogger<NetworkInputService> _logger;

    // ── State ────────────────────────────────────────────────────────────────
    private volatile NetworkInputMode _mode = NetworkInputMode.Local;
    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _listenerCts;
    private CancellationTokenSource? _receiveCts;
    private readonly object _sendLock = new();

    // ── Trust handshake ───────────────────────────────────────────────────────
    private TaskCompletionSource<bool>? _pairingTcs;
    private TcpClient? _pendingClient;
    private NetworkStream? _pendingStream;

    // ── Client-side vJoy tracking ─────────────────────────────────────────────
    // Devices acquired on behalf of the master; may be more than just slot 1.
    private readonly HashSet<uint> _acquiredDevices = [];
    // Devices that failed acquisition — skip retry until next session to avoid spam.
    private readonly HashSet<uint> _failedDevices = [];

    // ── Events ───────────────────────────────────────────────────────────────
    public event EventHandler? ConnectionLost;
    public event EventHandler<string>? ClientConnected;
    public event EventHandler<TrustRequestEventArgs>? TrustRequested;
    public event EventHandler<ProfileListReceivedEventArgs>? ProfileListReceived;

    private int _packetsReceived;

    public NetworkInputMode Mode => _mode;
    public bool IsListening => _listener is not null;
    public int PacketsReceived => _packetsReceived;

    public NetworkInputService(
        IVJoyService vjoy,
        IApplicationSettingsService settings,
        ILogger<NetworkInputService> logger)
    {
        _vjoy     = vjoy     ?? throw new ArgumentNullException(nameof(vjoy));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger   = logger   ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── Listener (client / receiver side) ────────────────────────────────────

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
            catch (Exception ex) when (ex is not OperationCanceledException)
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
            var remoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "";

            var (type, payload) = await ReadPacketAsync(stream, ct).ConfigureAwait(false);
            if (type != MsgType.Hello)
            {
                _logger.LogWarning("Expected Hello, got {Type}", type);
                client.Dispose();
                return;
            }

            var (peerName, _, code) = DecodeHello(payload);
            _logger.LogInformation("Hello from {Peer} with code {Code}", peerName, code);

            // Check for auto-accept (known trusted master with matching code)
            var trusted = _settings.TrustedMaster;
            bool autoAccept = trusted is not null
                && trusted.MachineName == peerName
                && trusted.Code == code;

            _pendingClient = client;
            _pendingStream = stream;
            _pairingTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (autoAccept)
            {
                _logger.LogInformation("Auto-accepting trusted master {Peer}", peerName);
                _pairingTcs.TrySetResult(true);
            }
            else
            {
                TrustRequested?.Invoke(this,
                    new TrustRequestEventArgs(peerName, code, remoteIp));
            }

            // Wait for UI response (or immediate auto-accept above)
            bool accepted;
            try
            {
                accepted = await _pairingTcs.Task.WaitAsync(TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Trust confirmation timed out for {Peer}", peerName);
                accepted = false;
            }

            if (!accepted)
            {
                await WritePacketAsync(stream, MsgType.HelloReject, [], ct).ConfigureAwait(false);
                client.Dispose();
                _logger.LogInformation("Connection rejected for {Peer}", peerName);
                return;
            }

            await WritePacketAsync(stream, MsgType.HelloAccept, [], ct).ConfigureAwait(false);
            _client = client;
            _stream = stream;
            _mode   = NetworkInputMode.Receiving;

            _logger.LogInformation("Connection accepted, starting receive loop for {Peer}", peerName);
            ClientConnected?.Invoke(this, peerName);
            _receiveCts = new CancellationTokenSource();
            _ = RunReceiveLoopAsync(stream, _receiveCts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Handshake error");
            client.Dispose();
        }
    }

    /// <summary>Accept the pending trust request.</summary>
    public void AcceptPairing() => _pairingTcs?.TrySetResult(true);

    /// <summary>Reject the pending trust request.</summary>
    public void RejectPairing() => _pairingTcs?.TrySetResult(false);

    // ── Connect (master side) ─────────────────────────────────────────────────

    public async Task ConnectToAsync(NetworkPeer peer, CancellationToken cancellationToken = default)
    {
        await DisconnectAsync(cancellationToken).ConfigureAwait(false);

        // Master uses its own permanent code (never changes)
        var code = _settings.NetworkMasterCode;
        _logger.LogInformation("Connecting to {Peer} @ {Ip}:{Port} with code {Code}",
            peer.MachineName, peer.IpAddress, peer.TcpPort, code);

        var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(peer.IpAddress, peer.TcpPort, cancellationToken).ConfigureAwait(false);

        var stream = client.GetStream();

        // Send HELLO with our permanent master code
        var helloPayload = EncodeHello(Environment.MachineName, (ushort)peer.TcpPort, code);
        await WritePacketAsync(stream, MsgType.Hello, helloPayload, cancellationToken).ConfigureAwait(false);

        // Wait for accept/reject from client.  The client shows a trust dialog so we
        // allow up to 45 seconds — longer than the client's own 60 s timeout so Castra
        // gets a clean rejection rather than a raw socket error.
        using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshakeCts.CancelAfter(TimeSpan.FromSeconds(45));
        byte responseType;
        try
        {
            (responseType, _) = await ReadPacketAsync(stream, handshakeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException($"Connection to {peer.MachineName} timed out waiting for trust confirmation.");
        }
        if (responseType != MsgType.HelloAccept)
        {
            _logger.LogWarning("Connection to {Peer} rejected", peer.MachineName);
            client.Dispose();
            throw new InvalidOperationException($"Connection to {peer.MachineName} was rejected.");
        }

        _client = client;
        _stream = stream;
        _mode   = NetworkInputMode.Remote;

        // Send ACQUIRE so client grabs vJoy
        await WritePacketAsync(stream, MsgType.Acquire, [], cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Connected to {Peer}, mode=Remote", peer.MachineName);

        _receiveCts = new CancellationTokenSource();
        _ = RunPingAsync(stream, _receiveCts.Token);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_stream is not null)
        {
            try
            {
                lock (_sendLock)
                {
                    WritePacketSync(_stream, MsgType.Release, []);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
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
        _acquiredDevices.Clear();
        Interlocked.Exchange(ref _packetsReceived, 0);

        _logger.LogInformation("NetworkInput disconnected, mode=Local");
    }

    // ── Send (master, hot path) ───────────────────────────────────────────────

    public void SendVJoyState(VJoyOutputSnapshot snapshot)
    {
        if (_mode != NetworkInputMode.Remote || _stream is null) return;

        lock (_sendLock)
        {
            try
            {
                WriteVJoyPacketSync(_stream, snapshot);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "SendVJoyState failed — connection lost");
                _mode = NetworkInputMode.Local;
                ConnectionLost?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void SendProfileList(IReadOnlyList<(string Name, byte[] XmlBytes)> profiles)
    {
        if (_mode != NetworkInputMode.Remote || _stream is null) return;

        // Payload: [count:4][nameLen:2][name UTF-8][xmlLen:4][xml UTF-8] × count
        using var ms = new System.IO.MemoryStream();
        var countBytes = BitConverter.GetBytes((uint)profiles.Count);
        ms.Write(countBytes, 0, 4);
        foreach (var (name, xml) in profiles)
        {
            var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
            var nameLenBytes = BitConverter.GetBytes((ushort)Math.Min(nameBytes.Length, ushort.MaxValue));
            ms.Write(nameLenBytes, 0, 2);
            ms.Write(nameBytes, 0, Math.Min(nameBytes.Length, ushort.MaxValue));
            var xmlLenBytes = BitConverter.GetBytes((uint)xml.Length);
            ms.Write(xmlLenBytes, 0, 4);
            ms.Write(xml, 0, xml.Length);
        }
        var payload = ms.ToArray();

        lock (_sendLock)
        {
            try   { WritePacketSync(_stream, MsgType.ProfileList, payload); }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "SendProfileList failed — connection lost");
                _mode = NetworkInputMode.Local;
                ConnectionLost?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    // ── Receive loop (client side) ────────────────────────────────────────────

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
                        // Ensure vJoy is initialized — it may have been skipped at startup
                        // if the driver wasn't ready yet.
                        if (!_vjoy.IsInitialized)
                            _vjoy.Initialize();

                        // Acquire device 1 upfront; additional devices are acquired lazily
                        // the first time we receive a snapshot for them (see MsgType.Input below).
                        EnsureDeviceAcquired(1);
                        break;

                    case MsgType.Release:
                        ReleaseAllDevices();
                        _mode = NetworkInputMode.Local;
                        _logger.LogInformation("Master released all vJoy devices");
                        ConnectionLost?.Invoke(this, EventArgs.Empty);
                        return;  // master is closing the connection — exit cleanly

                    case MsgType.Input:
                        var snapshot = DecodeVJoyPayload(payload);
                        // Acquire the device lazily — the master may forward multiple vJoy
                        // devices (e.g. JS1, JS2, JS3) but only sends one Acquire packet.
                        EnsureDeviceAcquired(snapshot.DeviceId);
                        ApplySnapshot(snapshot, snapshot.DeviceId);
                        Interlocked.Increment(ref _packetsReceived);
                        break;

                    case MsgType.Ping:
                        await WritePacketAsync(stream, MsgType.Pong, [], ct).ConfigureAwait(false);
                        break;

                    case MsgType.ProfileList:
                        var profileList = DecodeProfileList(payload);
                        if (profileList.Count > 0)
                            ProfileListReceived?.Invoke(this, new ProfileListReceivedEventArgs(profileList));
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Receive loop error — connection lost");
            _mode = NetworkInputMode.Local;
            ConnectionLost?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            ReleaseAllDevices();
        }
    }

    private void EnsureDeviceAcquired(uint deviceId)
    {
        if (_acquiredDevices.Contains(deviceId)) return;
        if (_failedDevices.Contains(deviceId)) return;  // don't spam retries each session

        if (!_vjoy.IsInitialized)
            _vjoy.Initialize();

        if (_vjoy.AcquireDevice(deviceId))
        {
            _acquiredDevices.Add(deviceId);
            _logger.LogInformation("Acquired vJoy device {DeviceId}", deviceId);
        }
        else
        {
            _failedDevices.Add(deviceId);
            _logger.LogWarning("vJoy device {DeviceId} not available on this machine — skipping", deviceId);
        }
    }

    private void ReleaseAllDevices()
    {
        foreach (var deviceId in _acquiredDevices)
        {
            _vjoy.ResetDevice(deviceId);
            _vjoy.ReleaseDevice(deviceId);
        }
        _acquiredDevices.Clear();
        _failedDevices.Clear();        // reset so next session can retry (vJoy config may have changed)
    }

    private static List<(string Name, byte[] XmlBytes)> DecodeProfileList(byte[] payload)
    {
        var result = new List<(string, byte[])>();
        if (payload.Length < 4) return result;

        int pos = 0;
        uint count = BitConverter.ToUInt32(payload, pos); pos += 4;
        for (uint i = 0; i < count && pos + 6 <= payload.Length; i++)
        {
            ushort nameLen = BitConverter.ToUInt16(payload, pos); pos += 2;
            if (pos + nameLen > payload.Length) break;
            var name = System.Text.Encoding.UTF8.GetString(payload, pos, nameLen); pos += nameLen;
            if (pos + 4 > payload.Length) break;
            uint xmlLen = BitConverter.ToUInt32(payload, pos); pos += 4;
            if (pos + xmlLen > payload.Length) break;
            var xml = new byte[xmlLen];
            Array.Copy(payload, pos, xml, 0, (int)xmlLen); pos += (int)xmlLen;
            result.Add((name, xml));
        }
        return result;
    }

    // ── Ping keepalive (master side) ──────────────────────────────────────────

    private async Task RunPingAsync(NetworkStream stream, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(3000, ct).ConfigureAwait(false);
                // Write inside _sendLock — same lock used by SendVJoyState/heartbeat
                // to prevent concurrent writes corrupting the TCP frame.
                lock (_sendLock)
                {
                    WritePacketSync(stream, MsgType.Ping, []);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Ping failed — connection lost");
            _mode = NetworkInputMode.Local;
            ConnectionLost?.Invoke(this, EventArgs.Empty);
        }
    }

    private static void WritePacketSync(NetworkStream stream, byte type, byte[] payload)
    {
        var header = new byte[9];
        BitConverter.TryWriteBytes(header.AsSpan(0), Magic);
        header[4] = type;
        BitConverter.TryWriteBytes(header.AsSpan(5), (uint)payload.Length);
        stream.Write(header);
        if (payload.Length > 0)
            stream.Write(payload);
    }

    // ── Codec ─────────────────────────────────────────────────────────────────

    /// <summary>Encode a <see cref="VJoyOutputSnapshot"/> as an INPUT payload.</summary>
    public static byte[] EncodeVJoyPayload(VJoyOutputSnapshot snapshot)
    {
        int axisCount   = Math.Min(snapshot.AxisCount, 8);
        int buttonCount = Math.Min(snapshot.ButtonCount, 128);
        int hatCount    = Math.Min(snapshot.HatCount, 4);
        int buttonByteCount = (buttonCount + 7) / 8;

        int size = 4 + 1 + axisCount * 4 + 2 + buttonByteCount + 1 + hatCount * 4;
        using var ms = new System.IO.MemoryStream(size);
        using var w  = new System.IO.BinaryWriter(ms);

        w.Write(snapshot.DeviceId);
        w.Write((byte)axisCount);
        for (int i = 0; i < axisCount; i++) w.Write(snapshot.Axes[i]);

        w.Write((ushort)buttonCount);
        var buttonBytes = new byte[buttonByteCount];
        for (int i = 0; i < buttonCount; i++)
        {
            if (snapshot.Buttons[i])
                buttonBytes[i / 8] |= (byte)(1 << (i % 8));
        }
        w.Write(buttonBytes);

        w.Write((byte)hatCount);
        for (int i = 0; i < hatCount; i++) w.Write(snapshot.Hats[i]);

        return ms.ToArray();
    }

    /// <summary>Decode an INPUT payload back to a <see cref="VJoyOutputSnapshot"/>.</summary>
    public static VJoyOutputSnapshot DecodeVJoyPayload(byte[] payload)
    {
        using var ms = new System.IO.MemoryStream(payload);
        using var r  = new System.IO.BinaryReader(ms);

        uint deviceId  = r.ReadUInt32();
        int axisCount  = r.ReadByte();
        var axes       = new float[8];
        for (int i = 0; i < axisCount; i++) axes[i] = r.ReadSingle();

        int buttonCount     = r.ReadUInt16();
        int buttonByteCount = (buttonCount + 7) / 8;
        var buttonBytes     = r.ReadBytes(buttonByteCount);
        var buttons         = new bool[128];
        for (int i = 0; i < buttonCount; i++)
            buttons[i] = (buttonBytes[i / 8] & (1 << (i % 8))) != 0;

        int hatCount = r.ReadByte();
        var hats     = new int[4];
        for (int i = 0; i < hatCount; i++) hats[i] = r.ReadInt32();

        return new VJoyOutputSnapshot
        {
            DeviceId    = deviceId,
            Axes        = axes,
            Buttons     = buttons,
            Hats        = hats,
            AxisCount   = axisCount,
            ButtonCount = buttonCount,
            HatCount    = hatCount
        };
    }

    private static byte[] EncodeHello(string machineName, ushort tcpPort, string code)
    {
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(machineName);
        var codeBytes = System.Text.Encoding.UTF8.GetBytes(code);
        using var ms = new System.IO.MemoryStream();
        using var w  = new System.IO.BinaryWriter(ms);
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
        using var r  = new System.IO.BinaryReader(ms);
        int nameLen  = r.ReadByte();
        var name     = System.Text.Encoding.UTF8.GetString(r.ReadBytes(nameLen));
        ushort port  = r.ReadUInt16();
        int codeLen  = r.ReadByte();
        var code     = System.Text.Encoding.UTF8.GetString(r.ReadBytes(codeLen));
        return (name, port, code);
    }

    private void ApplySnapshot(VJoyOutputSnapshot snapshot, uint deviceId)
    {
        for (int i = 0; i < snapshot.AxisCount && i < 8; i++)
            _vjoy.SetAxis(deviceId, VJoyAxisHelper.IndexToHidUsage(i), snapshot.Axes[i]);

        for (int i = 0; i < snapshot.ButtonCount; i++)
            _vjoy.SetButton(deviceId, i + 1, snapshot.Buttons[i]);

        for (int i = 0; i < snapshot.HatCount; i++)
            _vjoy.SetContinuousPov(deviceId, (uint)i, snapshot.Hats[i]);
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

        byte type   = header[4];
        uint length = BitConverter.ToUInt32(header, 5);
        if (length > 4194304) // 4 MB — covers a full profile list (many SC XMLs)
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

    private void WriteVJoyPacketSync(NetworkStream stream, VJoyOutputSnapshot snapshot)
    {
        var payload = EncodeVJoyPayload(snapshot);
        var header  = new byte[9];
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

    public void Dispose()
    {
        DisconnectAsync().GetAwaiter().GetResult();
        _listenerCts?.Cancel();
        _listenerCts?.Dispose();
        _listener?.Stop();
    }
}
