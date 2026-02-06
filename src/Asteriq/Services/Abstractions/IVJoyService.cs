using Asteriq.Models;
using Asteriq.VJoy;

namespace Asteriq.Services.Abstractions;

/// <summary>
/// Interface for managing vJoy virtual devices
/// </summary>
public interface IVJoyService : IDisposable
{
    /// <summary>
    /// Whether vJoy has been successfully initialized
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Event fired when a device is lost and could not be re-acquired
    /// </summary>
    event EventHandler<uint>? DeviceLost;

    /// <summary>
    /// Initialize vJoy and check driver availability
    /// </summary>
    bool Initialize();

    /// <summary>
    /// Get information about all vJoy device slots (1-16)
    /// </summary>
    List<VJoyDeviceInfo> EnumerateDevices();

    /// <summary>
    /// Get information about a specific vJoy device
    /// </summary>
    VJoyDeviceInfo GetDeviceInfo(uint deviceId);

    /// <summary>
    /// Acquire exclusive access to a vJoy device
    /// </summary>
    bool AcquireDevice(uint deviceId);

    /// <summary>
    /// Release a vJoy device
    /// </summary>
    void ReleaseDevice(uint deviceId);

    /// <summary>
    /// Set axis value (normalized -1.0 to 1.0)
    /// </summary>
    bool SetAxis(uint deviceId, HID_USAGES axis, float value);

    /// <summary>
    /// Set button state (1-indexed)
    /// </summary>
    bool SetButton(uint deviceId, int button, bool pressed);

    /// <summary>
    /// Set discrete POV hat (0-3 = directions, -1 = neutral)
    /// </summary>
    bool SetDiscretePov(uint deviceId, uint povIndex, int direction);

    /// <summary>
    /// Set continuous POV hat (angle in degrees, -1 = neutral)
    /// </summary>
    bool SetContinuousPov(uint deviceId, uint povIndex, int angle);

    /// <summary>
    /// Reset all controls on a device to neutral
    /// </summary>
    bool ResetDevice(uint deviceId);
}
