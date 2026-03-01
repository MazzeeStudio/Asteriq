using Asteriq.Models;
using Asteriq.Services.Abstractions;
using Asteriq.VJoy;

namespace Asteriq.Services;

/// <summary>
/// Wraps a real <see cref="IVJoyService"/> and intercepts axis/button/hat writes
/// when <see cref="ForwardingMode"/> is enabled.
///
/// In forwarding mode the intercepted values are stored in a per-device snapshot
/// (for transmission to the network client) but NOT written through to the underlying
/// vJoy hardware.  All other operations (Initialize, Acquire, Release, Reset, …)
/// always delegate to the inner service regardless of forwarding mode.
/// </summary>
public sealed class NetworkVJoyService : IVJoyService
{
    private readonly IVJoyService _inner;
    private readonly Dictionary<uint, VJoyOutputSnapshot> _snapshots = new();

    /// <summary>
    /// When true, SetAxis / SetButton / SetContinuousPov / SetDiscretePov
    /// update the internal snapshot but skip the real vJoy write.
    /// </summary>
    public bool ForwardingMode { get; set; }

    public NetworkVJoyService(IVJoyService inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    // ── IVJoyService passthrough members ─────────────────────────────────────

    public bool IsInitialized => _inner.IsInitialized;
    public event EventHandler<uint>? DeviceLost
    {
        add    => _inner.DeviceLost += value;
        remove => _inner.DeviceLost -= value;
    }

    public bool Initialize()             => _inner.Initialize();
    public List<VJoyDeviceInfo> EnumerateDevices() => _inner.EnumerateDevices();
    public VJoyDeviceInfo GetDeviceInfo(uint deviceId) => _inner.GetDeviceInfo(deviceId);
    public bool AcquireDevice(uint deviceId)    => _inner.AcquireDevice(deviceId);
    public void ReleaseDevice(uint deviceId)    => _inner.ReleaseDevice(deviceId);
    public bool ResetDevice(uint deviceId)
    {
        // Clear snapshot so stale state is not transmitted after a reset
        if (_snapshots.TryGetValue(deviceId, out var snap))
        {
            Array.Clear(snap.Axes);
            Array.Clear(snap.Buttons);
            Array.Clear(snap.Hats);
        }
        return _inner.ResetDevice(deviceId);
    }

    // ── Intercepted output members ────────────────────────────────────────────

    public bool SetAxis(uint deviceId, HID_USAGES axis, float value)
    {
        var snap = EnsureSnapshot(deviceId);
        int idx = VJoyAxisHelper.HidUsageToIndex(axis);
        snap.Axes[idx] = value;
        snap.AxisCount = Math.Max(snap.AxisCount, idx + 1);

        return ForwardingMode || _inner.SetAxis(deviceId, axis, value);
    }

    public bool SetButton(uint deviceId, int button, bool pressed)
    {
        // IVJoyService buttons are 1-indexed; snapshot is 0-indexed
        var snap = EnsureSnapshot(deviceId);
        int idx = button - 1;
        if ((uint)idx < (uint)snap.Buttons.Length)
        {
            snap.Buttons[idx] = pressed;
            snap.ButtonCount = Math.Max(snap.ButtonCount, idx + 1);
        }

        return ForwardingMode || _inner.SetButton(deviceId, button, pressed);
    }

    public bool SetContinuousPov(uint deviceId, uint povIndex, int angle)
    {
        var snap = EnsureSnapshot(deviceId);
        if (povIndex < (uint)snap.Hats.Length)
        {
            snap.Hats[povIndex] = angle;
            snap.HatCount = Math.Max(snap.HatCount, (int)povIndex + 1);
        }

        return ForwardingMode || _inner.SetContinuousPov(deviceId, povIndex, angle);
    }

    public bool SetDiscretePov(uint deviceId, uint povIndex, int direction)
    {
        // Convert discrete (0–3 / -1) to continuous degrees for the snapshot
        int angle = direction switch
        {
            0 => 0,
            1 => 9000,
            2 => 18000,
            3 => 27000,
            _ => -1
        };

        var snap = EnsureSnapshot(deviceId);
        if (povIndex < (uint)snap.Hats.Length)
        {
            snap.Hats[povIndex] = angle;
            snap.HatCount = Math.Max(snap.HatCount, (int)povIndex + 1);
        }

        return ForwardingMode || _inner.SetDiscretePov(deviceId, povIndex, direction);
    }

    // ── Snapshot access ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current snapshot for the given device, or null if no writes have occurred.
    /// </summary>
    public VJoyOutputSnapshot? GetSnapshot(uint deviceId)
        => _snapshots.TryGetValue(deviceId, out var s) ? s : null;

    public void Dispose() => _inner.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private VJoyOutputSnapshot EnsureSnapshot(uint deviceId)
    {
        if (!_snapshots.TryGetValue(deviceId, out var snap))
        {
            snap = new VJoyOutputSnapshot { DeviceId = deviceId };
            _snapshots[deviceId] = snap;
        }
        return snap;
    }
}
