using Asteriq.Models;

namespace Asteriq.Services.Abstractions;

/// <summary>
/// Interface for handling physical device input using SDL2 or DirectInput
/// </summary>
public interface IInputService : IDisposable
{
    /// <summary>
    /// When true, only fires InputReceived when state changes. Default: false (fire every poll)
    /// </summary>
    bool OnlyFireOnChange { get; set; }

    /// <summary>
    /// Input backend to use for reading device state. Default: DirectInput (more reliable)
    /// </summary>
    InputPollingBackend InputBackend { get; set; }

    /// <summary>
    /// Fired when input state changes on any device
    /// </summary>
    event EventHandler<DeviceInputState>? InputReceived;

    /// <summary>
    /// Fired when a device is connected
    /// </summary>
    event EventHandler<PhysicalDeviceInfo>? DeviceConnected;

    /// <summary>
    /// Fired when a device is disconnected
    /// </summary>
    event EventHandler<int>? DeviceDisconnected;

    /// <summary>
    /// Initialize SDL2 joystick subsystem and DirectInput
    /// </summary>
    bool Initialize();

    /// <summary>
    /// Get list of currently connected devices
    /// </summary>
    List<PhysicalDeviceInfo> EnumerateDevices();

    /// <summary>
    /// Get the current input state for a device by its device index.
    /// Returns null if the device is not found or not opened.
    /// </summary>
    DeviceInputState? GetDeviceState(int deviceIndex);

    /// <summary>
    /// Start polling for input
    /// </summary>
    void StartPolling(int pollRateHz = 500);

    /// <summary>
    /// Stop polling for input
    /// </summary>
    void StopPolling();

    /// <summary>
    /// Stop polling for input asynchronously
    /// </summary>
    Task StopPollingAsync(CancellationToken ct = default);
}
