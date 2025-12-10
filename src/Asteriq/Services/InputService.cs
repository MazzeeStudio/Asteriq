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
    private HidDeviceService? _hidDeviceService;
    private List<HidDeviceService.HidDeviceInfo>? _hidDevicesCache;
    private readonly HashSet<string> _matchedHidDevicePaths = new();

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

        // Initialize HidSharp for axis type detection and unique device identification
        try
        {
            _hidDeviceService = new HidDeviceService();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"HidSharp initialization failed (axis types will be unknown): {ex.Message}");
            // Continue without HidSharp - we'll just not have axis type info
        }

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
        Guid sdlGuid = SDL.SDL_JoystickGetDeviceGUID(deviceIndex);

        // Need to open the joystick to get axis/button counts
        IntPtr joystick = SDL.SDL_JoystickOpen(deviceIndex);
        if (joystick == IntPtr.Zero)
            return null;

        // Get the instance ID for matching with DirectInput
        var instanceId = SDL.SDL_JoystickInstanceID(joystick);

        var info = new PhysicalDeviceInfo
        {
            DeviceIndex = deviceIndex,
            Name = name,
            InstanceGuid = sdlGuid,
            AxisCount = SDL.SDL_JoystickNumAxes(joystick),
            ButtonCount = SDL.SDL_JoystickNumButtons(joystick),
            HatCount = SDL.SDL_JoystickNumHats(joystick),
            IsVirtual = IsVirtualDevice(name)
        };

        // Try to get axis type information from DirectInput
        PopulateAxisTypes(info);

        // Keep joystick open for polling
        _openJoysticks[deviceIndex] = joystick;
        _deviceInfo[deviceIndex] = info;

        return info;
    }

    /// <summary>
    /// Populate axis type information from HidSharp
    /// </summary>
    private void PopulateAxisTypes(PhysicalDeviceInfo info)
    {
        if (_hidDeviceService == null)
        {
            LogAxisTypes($"HidDeviceService is null for {info.Name}");
            return;
        }

        try
        {
            // Cache HID devices on first call (to handle multiple identical devices)
            if (_hidDevicesCache == null)
            {
                _hidDevicesCache = _hidDeviceService.EnumerateDevices();
                LogAxisTypes($"Enumerated {_hidDevicesCache.Count} HID devices:");
                foreach (var d in _hidDevicesCache)
                {
                    LogAxisTypes($"  - {d.ProductName}: {d.Axes.Count} axes, Path=...{d.DevicePath.Substring(Math.Max(0, d.DevicePath.Length - 40))}");
                    foreach (var axis in d.Axes)
                    {
                        LogAxisTypes($"      Axis {axis.Index}: {axis.Type}");
                    }
                }
            }

            // Find matching device by name, but exclude already-matched devices
            // This handles the case of multiple identical devices (e.g., two Alpha Primes)
            // The DevicePath is unique per physical device instance
            LogAxisTypes($"Looking for match for SDL device: '{info.Name}'");
            LogAxisTypes($"  Already matched paths: {_matchedHidDevicePaths.Count}");

            var matchingDevice = _hidDevicesCache.FirstOrDefault(d =>
                !_matchedHidDevicePaths.Contains(d.DevicePath) &&
                DeviceNamesMatch(d.ProductName, info.Name));

            if (matchingDevice != null)
            {
                // Mark this HID device as matched so it won't be reused
                _matchedHidDevicePaths.Add(matchingDevice.DevicePath);

                // Copy axis info directly (HidDeviceService already uses our AxisInfo type)
                info.AxisInfos = matchingDevice.Axes;

                // Store the unique device path for future reference
                info.HidDevicePath = matchingDevice.DevicePath;

                LogAxisTypes($"Matched {info.Name} to HID device with {info.AxisInfos.Count} axes");
                foreach (var axis in info.AxisInfos)
                {
                    LogAxisTypes($"  Axis {axis.Index}: {axis.Type} -> vJoy index {axis.ToVJoyAxisIndex()}");
                }
            }
            else
            {
                LogAxisTypes($"No match found for '{info.Name}'");
            }
        }
        catch (Exception ex)
        {
            LogAxisTypes($"Failed to get axis types for {info.Name}: {ex.Message}");
        }
    }

    private static void LogAxisTypes(string message)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Asteriq", "axis_types.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
    }

    /// <summary>
    /// Check if two device names match, accounting for naming differences between HidSharp and SDL2.
    /// SDL2 sometimes expands abbreviations (e.g., "L-" -> "LEFT ", "R-" -> "RIGHT ").
    /// </summary>
    private static bool DeviceNamesMatch(string hidName, string sdlName)
    {
        // Exact match
        if (hidName.Equals(sdlName, StringComparison.OrdinalIgnoreCase))
            return true;

        // Normalize names for comparison
        var normalizedHid = NormalizeDeviceName(hidName);
        var normalizedSdl = NormalizeDeviceName(sdlName);

        return normalizedHid.Equals(normalizedSdl, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalize device name by expanding common abbreviations and removing extra spaces.
    /// </summary>
    private static string NormalizeDeviceName(string name)
    {
        var normalized = name.Trim();

        // Expand common Virpil/VKB abbreviations that SDL2 expands
        // "L-" at start -> "LEFT "
        // "R-" at start -> "RIGHT "
        if (normalized.StartsWith("L-", StringComparison.OrdinalIgnoreCase))
            normalized = "LEFT " + normalized.Substring(2);
        else if (normalized.StartsWith("R-", StringComparison.OrdinalIgnoreCase))
            normalized = "RIGHT " + normalized.Substring(2);

        // Remove extra whitespace
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ");

        return normalized;
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

        _hidDeviceService = null;
        _hidDevicesCache = null;
        _matchedHidDevicePaths.Clear();

        if (_isInitialized)
        {
            SDL.SDL_Quit();
            _isInitialized = false;
        }
    }
}
