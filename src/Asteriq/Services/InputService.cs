using SDL2;
using Asteriq.Models;
using System.Collections.Concurrent;

namespace Asteriq.Services;

/// <summary>
/// Handles physical device input using SDL2
/// </summary>
public class InputService : IDisposable
{
    private readonly ConcurrentDictionary<int, IntPtr> _openJoysticks = new();
    private readonly ConcurrentDictionary<int, PhysicalDeviceInfo> _deviceInfo = new();
    private readonly ConcurrentDictionary<int, DeviceInputState> _lastState = new();
    private volatile bool _isPolling;
    private Task? _pollTask;
    private bool _isInitialized;

    /// <summary>
    /// When true, only fires InputReceived when state changes. Default: false (fire every poll)
    /// </summary>
    public bool OnlyFireOnChange { get; set; } = false;

    /// <summary>
    /// Fired when input state changes on any device
    /// </summary>
    public event EventHandler<DeviceInputState>? InputReceived;

    /// <summary>
    /// Fired when a device is connected
    /// </summary>
    public event EventHandler<PhysicalDeviceInfo>? DeviceConnected;

    /// <summary>
    /// Fired when a device is disconnected
    /// </summary>
    public event EventHandler<int>? DeviceDisconnected;

    /// <summary>
    /// Initialize SDL2 joystick subsystem
    /// </summary>
    public bool Initialize()
    {
        if (_isInitialized) return true;

        if (SDL.SDL_Init(SDL.SDL_INIT_JOYSTICK) < 0)
        {
            var error = SDL.SDL_GetError();
            Console.WriteLine($"SDL2 init failed: {error}");
            return false;
        }

        // Enable joystick events
        SDL.SDL_JoystickEventState(SDL.SDL_ENABLE);

        _isInitialized = true;
        return true;
    }

    /// <summary>
    /// Get list of currently connected devices
    /// </summary>
    public List<PhysicalDeviceInfo> EnumerateDevices()
    {
        if (!_isInitialized) return new List<PhysicalDeviceInfo>();

        var devices = new List<PhysicalDeviceInfo>();
        int numJoysticks = SDL.SDL_NumJoysticks();

        for (int i = 0; i < numJoysticks; i++)
        {
            var info = GetDeviceInfo(i);
            if (info != null)
                devices.Add(info);
        }

        return devices;
    }

    /// <summary>
    /// Get info about a specific device by index
    /// </summary>
    private PhysicalDeviceInfo? GetDeviceInfo(int deviceIndex)
    {
        string name = SDL.SDL_JoystickNameForIndex(deviceIndex) ?? $"Unknown Device {deviceIndex}";
        Guid guid = SDL.SDL_JoystickGetDeviceGUID(deviceIndex);

        // Need to open the joystick to get axis/button counts
        IntPtr joystick = SDL.SDL_JoystickOpen(deviceIndex);
        if (joystick == IntPtr.Zero)
            return null;

        var info = new PhysicalDeviceInfo
        {
            DeviceIndex = deviceIndex,
            Name = name,
            InstanceGuid = guid,
            AxisCount = SDL.SDL_JoystickNumAxes(joystick),
            ButtonCount = SDL.SDL_JoystickNumButtons(joystick),
            HatCount = SDL.SDL_JoystickNumHats(joystick),
            IsVirtual = IsVirtualDevice(name)
        };

        // Keep joystick open for polling
        _openJoysticks[deviceIndex] = joystick;
        _deviceInfo[deviceIndex] = info;

        return info;
    }

    /// <summary>
    /// Check if a device name indicates a virtual device (vJoy, vXBox, etc.)
    /// </summary>
    private static bool IsVirtualDevice(string name)
    {
        var upper = name.ToUpperInvariant();
        return upper.Contains("VJOY") ||
               upper.Contains("VXBOX") ||
               upper.Contains("VIGEM") ||
               upper.Contains("VIRTUAL") ||
               upper.Contains("FEEDER");
    }

    /// <summary>
    /// Start polling for input
    /// </summary>
    public void StartPolling(int pollRateHz = 500)
    {
        if (_isPolling) return;

        _isPolling = true;
        int delayMs = 1000 / pollRateHz;

        _pollTask = Task.Run(async () =>
        {
            while (_isPolling)
            {
                PollAllDevices();
                await Task.Delay(delayMs);
            }
        });
    }

    /// <summary>
    /// Stop polling for input
    /// </summary>
    public void StopPolling()
    {
        _isPolling = false;
        _pollTask?.Wait(1000);
    }

    /// <summary>
    /// Poll all open joysticks and fire events
    /// </summary>
    private void PollAllDevices()
    {
        // Process SDL events (needed for hot-plug detection)
        SDL.SDL_JoystickUpdate();

        foreach (var kvp in _openJoysticks)
        {
            int deviceIndex = kvp.Key;
            IntPtr joystick = kvp.Value;

            if (SDL.SDL_JoystickGetAttached(joystick) == SDL.SDL_bool.SDL_FALSE)
            {
                // Device disconnected
                HandleDeviceDisconnected(deviceIndex);
                continue;
            }

            if (!_deviceInfo.TryGetValue(deviceIndex, out var info))
                continue;

            var state = ReadDeviceState(joystick, info);

            if (OnlyFireOnChange)
            {
                if (_lastState.TryGetValue(deviceIndex, out var last) && !HasStateChanged(last, state))
                    continue;

                _lastState[deviceIndex] = state;
            }

            InputReceived?.Invoke(this, state);
        }
    }

    /// <summary>
    /// Check if input state has changed (axes beyond threshold or any button change)
    /// </summary>
    private static bool HasStateChanged(DeviceInputState last, DeviceInputState current, float axisThreshold = 0.01f)
    {
        // Check buttons - any change triggers
        for (int i = 0; i < Math.Min(last.Buttons.Length, current.Buttons.Length); i++)
        {
            if (last.Buttons[i] != current.Buttons[i])
                return true;
        }

        // Check axes - only if change exceeds threshold
        for (int i = 0; i < Math.Min(last.Axes.Length, current.Axes.Length); i++)
        {
            if (Math.Abs(last.Axes[i] - current.Axes[i]) > axisThreshold)
                return true;
        }

        // Check hats
        for (int i = 0; i < Math.Min(last.Hats.Length, current.Hats.Length); i++)
        {
            if (last.Hats[i] != current.Hats[i])
                return true;
        }

        return false;
    }

    /// <summary>
    /// Read current state from a joystick
    /// </summary>
    private DeviceInputState ReadDeviceState(IntPtr joystick, PhysicalDeviceInfo info)
    {
        // Read axes (normalize from -32768..32767 to -1.0..1.0)
        var axes = new float[info.AxisCount];
        for (int i = 0; i < info.AxisCount; i++)
        {
            short raw = SDL.SDL_JoystickGetAxis(joystick, i);
            axes[i] = raw / 32767f;
        }

        // Read buttons
        var buttons = new bool[info.ButtonCount];
        for (int i = 0; i < info.ButtonCount; i++)
        {
            buttons[i] = SDL.SDL_JoystickGetButton(joystick, i) == 1;
        }

        // Read hats
        var hats = new int[info.HatCount];
        for (int i = 0; i < info.HatCount; i++)
        {
            byte hatState = SDL.SDL_JoystickGetHat(joystick, i);
            hats[i] = HatToAngle(hatState);
        }

        return new DeviceInputState
        {
            DeviceIndex = info.DeviceIndex,
            DeviceName = info.Name,
            InstanceGuid = info.InstanceGuid,
            Timestamp = DateTime.UtcNow,
            Axes = axes,
            Buttons = buttons,
            Hats = hats
        };
    }

    /// <summary>
    /// Convert SDL hat bitmask to angle in degrees (-1 for center)
    /// </summary>
    private static int HatToAngle(byte hatState)
    {
        return hatState switch
        {
            SDL.SDL_HAT_UP => 0,
            SDL.SDL_HAT_RIGHTUP => 45,
            SDL.SDL_HAT_RIGHT => 90,
            SDL.SDL_HAT_RIGHTDOWN => 135,
            SDL.SDL_HAT_DOWN => 180,
            SDL.SDL_HAT_LEFTDOWN => 225,
            SDL.SDL_HAT_LEFT => 270,
            SDL.SDL_HAT_LEFTUP => 315,
            _ => -1 // Centered
        };
    }

    private void HandleDeviceDisconnected(int deviceIndex)
    {
        if (_openJoysticks.TryRemove(deviceIndex, out var joystick))
        {
            SDL.SDL_JoystickClose(joystick);
        }
        _deviceInfo.TryRemove(deviceIndex, out _);

        DeviceDisconnected?.Invoke(this, deviceIndex);
    }

    public void Dispose()
    {
        StopPolling();

        foreach (var joystick in _openJoysticks.Values)
        {
            SDL.SDL_JoystickClose(joystick);
        }
        _openJoysticks.Clear();
        _deviceInfo.Clear();
        _lastState.Clear();

        if (_isInitialized)
        {
            SDL.SDL_Quit();
            _isInitialized = false;
        }
    }
}
