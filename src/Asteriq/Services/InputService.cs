using SDL2;
using Asteriq.Models;
using Asteriq.DirectInput;
using Asteriq.Services.Abstractions;
using System.Collections.Concurrent;

namespace Asteriq.Services;

/// <summary>
/// Input polling backend selection
/// </summary>
public enum InputPollingBackend
{
    /// <summary>Use SDL2 for input (simpler but less reliable for some devices)</summary>
    SDL2,
    /// <summary>Use DirectInput for input (more reliable for dual-role controls)</summary>
    DirectInput
}

/// <summary>
/// Handles physical device input using SDL2 or DirectInput
/// </summary>
public class InputService : IInputService
{
    // Keyed by SDL instance ID (stable) rather than device index (can shift)
    private readonly ConcurrentDictionary<int, IntPtr> _openJoysticks = new();
    private readonly ConcurrentDictionary<int, PhysicalDeviceInfo> _deviceInfo = new();
    private readonly ConcurrentDictionary<int, DeviceInputState> _lastState = new();
    // Track which SDL instance IDs we've opened to detect new devices
    private readonly HashSet<int> _knownInstanceIds = new();
    private volatile bool _isPolling;
    private Task? _pollTask;
    private CancellationTokenSource? _pollCts;
    private bool _isInitialized;
    private HidDeviceService? _hidDeviceService;
    private List<HidDeviceService.HidDeviceInfo>? _hidDevicesCache;
    private readonly HashSet<string> _matchedHidDevicePaths = new();

    // DirectInput support
    private DirectInputReader? _directInputReader;
    private DirectInputService? _directInputService;
    private readonly ConcurrentDictionary<int, Guid> _sdlToDirectInputGuid = new();

    /// <summary>
    /// When true, only fires InputReceived when state changes. Default: false (fire every poll)
    /// </summary>
    public bool OnlyFireOnChange { get; set; } = false;

    /// <summary>
    /// Input backend to use for reading device state. Default: DirectInput (more reliable)
    /// </summary>
    public InputPollingBackend InputBackend { get; set; } = InputPollingBackend.DirectInput;

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
    /// Initialize SDL2 joystick subsystem and DirectInput
    /// </summary>
    public bool Initialize()
    {
        if (_isInitialized) return true;

        if (SDL.SDL_Init(SDL.SDL_INIT_JOYSTICK) < 0)
        {
            var error = SDL.SDL_GetError();
            System.Diagnostics.Debug.WriteLine($"SDL2 init failed: {error}");
            return false;
        }

        // Enable joystick events
        SDL.SDL_JoystickEventState(SDL.SDL_ENABLE);

        // Initialize HidSharp for axis type detection and unique device identification
        try
        {
            _hidDeviceService = new HidDeviceService();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            System.Diagnostics.Debug.WriteLine($"HidSharp initialization failed (axis types will be unknown): {ex.Message}");
            // Continue without HidSharp - we'll just not have axis type info
        }

        // Initialize DirectInput for input reading
        try
        {
            _directInputService = new DirectInputService();
            _directInputReader = new DirectInputReader();
            System.Diagnostics.Debug.WriteLine("DirectInput initialized successfully");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or DllNotFoundException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"DirectInput initialization failed (will use SDL2): {ex.Message}");
            InputBackend = InputPollingBackend.SDL2; // Fall back to SDL2
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
            var (info, _) = GetDeviceInfo(i);
            if (info is not null)
                devices.Add(info);
        }

        return devices;
    }

    /// <summary>
    /// Get the current input state for a device by its device index.
    /// Returns null if the device is not found or not opened.
    /// </summary>
    public DeviceInputState? GetDeviceState(int deviceIndex)
    {
        if (!_isInitialized) return null;

        // Find the joystick by device index
        foreach (var kvp in _openJoysticks)
        {
            int instanceId = kvp.Key;
            IntPtr joystick = kvp.Value;

            if (!_deviceInfo.TryGetValue(instanceId, out var info))
                continue;

            if (info.DeviceIndex == deviceIndex && SDL.SDL_JoystickGetAttached(joystick) == SDL.SDL_bool.SDL_TRUE)
            {
                // Update SDL state first
                SDL.SDL_JoystickUpdate();

                // Poll DirectInput if using it
                if (InputBackend == InputPollingBackend.DirectInput &&
                    _directInputReader is not null &&
                    _sdlToDirectInputGuid.TryGetValue(instanceId, out var diGuid))
                {
                    _directInputReader.PollDevice(diGuid);
                }

                return ReadDeviceState(instanceId, joystick, info);
            }
        }

        return null;
    }

    /// <summary>
    /// Get info about a specific device by index. Returns the SDL instance ID.
    /// </summary>
    private (PhysicalDeviceInfo? info, int instanceId) GetDeviceInfo(int deviceIndex)
    {
        string name = SDL.SDL_JoystickNameForIndex(deviceIndex) ?? $"Unknown Device {deviceIndex}";
        Guid sdlGuid = SDL.SDL_JoystickGetDeviceGUID(deviceIndex);

        // Need to open the joystick to get axis/button counts
        IntPtr joystick = SDL.SDL_JoystickOpen(deviceIndex);
        if (joystick == IntPtr.Zero)
            return (null, -1);

        // Get the instance ID - this is stable and unique per connected device
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

        // Keep joystick open for polling - keyed by instance ID (stable)
        _openJoysticks[instanceId] = joystick;
        _deviceInfo[instanceId] = info;
        _knownInstanceIds.Add(instanceId);

        // Map SDL device to DirectInput GUID for input reading
        MapToDirectInput(instanceId, info);

        return (info, instanceId);
    }

    /// <summary>
    /// Map SDL device to DirectInput GUID and open for DirectInput polling
    /// </summary>
    private void MapToDirectInput(int sdlInstanceId, PhysicalDeviceInfo info)
    {
        if (_directInputService is null || _directInputReader is null)
            return;

        try
        {
            // Enumerate DirectInput devices and find matching one by name
            var diDevices = _directInputService.EnumerateDevices();

            foreach (var diDevice in diDevices)
            {
                // Match by product name (more reliable than instance name)
                if (DeviceNamesMatch(diDevice.ProductName, info.Name) ||
                    DeviceNamesMatch(diDevice.InstanceName, info.Name))
                {
                    // Check if this DirectInput device is already mapped to another SDL device
                    if (_sdlToDirectInputGuid.Values.Contains(diDevice.InstanceGuid))
                        continue;

                    _sdlToDirectInputGuid[sdlInstanceId] = diDevice.InstanceGuid;
                    info.DirectInputGuid = diDevice.InstanceGuid;

                    // Open the device for DirectInput reading
                    _directInputReader.OpenDevice(diDevice.InstanceGuid);

                    LogAxisTypes($"Mapped SDL device '{info.Name}' (instanceId={sdlInstanceId}) to DirectInput GUID {diDevice.InstanceGuid}");
                    return;
                }
            }

            LogAxisTypes($"No DirectInput match found for SDL device '{info.Name}'");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            LogAxisTypes($"Failed to map SDL device to DirectInput: {ex.Message}");
        }
    }

    /// <summary>
    /// Populate axis type information from HidSharp
    /// </summary>
    private void PopulateAxisTypes(PhysicalDeviceInfo info)
    {
        if (_hidDeviceService is null)
        {
            LogAxisTypes($"HidDeviceService is null for {info.Name}");
            return;
        }

        try
        {
            // Cache HID devices on first call (to handle multiple identical devices)
            if (_hidDevicesCache is null)
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

            if (matchingDevice is not null)
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
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            LogAxisTypes($"Failed to get axis types for device '{info.Name}' (InstanceGuid: {info.InstanceGuid}, " +
                         $"AxisCount: {info.AxisCount}). Error type: {ex.GetType().Name}, Details: {ex.Message}");
        }
    }

    private static readonly object s_logLock = new();

    [System.Diagnostics.Conditional("DEBUG")]
    private static void LogAxisTypes(string message)
    {
        try
        {
            lock (s_logLock)
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Asteriq", "axis_types.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore logging errors - debug logging should never crash the app
            // This is intentionally swallowed as it's non-critical diagnostic code
        }
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
        _pollCts = new CancellationTokenSource();
        int delayMs = 1000 / pollRateHz;
        var ct = _pollCts.Token;

        _pollTask = Task.Run(async () =>
        {
            while (_isPolling && !ct.IsCancellationRequested)
            {
                PollAllDevices();
                try
                {
                    await Task.Delay(delayMs, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, ct);
    }

    /// <summary>
    /// Stop polling for input
    /// </summary>
    public void StopPolling()
    {
        _isPolling = false;
        _pollCts?.Cancel();

        // Wait synchronously with timeout - this is acceptable in Dispose pattern
        // Using a short timeout to avoid blocking indefinitely
        if (_pollTask is not null)
        {
            try
            {
                _pollTask.Wait(1000);
            }
            catch (AggregateException)
            {
                // Task was cancelled or faulted - this is expected
            }
        }

        _pollCts?.Dispose();
        _pollCts = null;
    }

    /// <summary>
    /// Stop polling for input asynchronously
    /// </summary>
    public async Task StopPollingAsync(CancellationToken ct = default)
    {
        _isPolling = false;
        _pollCts?.Cancel();

        if (_pollTask is not null)
        {
            try
            {
                // Wait for the poll task with a timeout
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(1000);
                await _pollTask.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Task was cancelled or timed out - this is expected
            }
        }

        _pollCts?.Dispose();
        _pollCts = null;
    }

    /// <summary>
    /// Poll all open joysticks and fire events
    /// </summary>
    private void PollAllDevices()
    {
        // Process SDL events (needed for hot-plug detection)
        // SDL_PumpEvents updates the internal device list for hot-plug detection
        SDL.SDL_PumpEvents();
        SDL.SDL_JoystickUpdate();

        // Check for newly connected devices
        CheckForNewDevices();

        // Poll DirectInput devices if using DirectInput input source
        if (InputBackend == InputPollingBackend.DirectInput && _directInputReader is not null)
        {
            foreach (var kvp in _sdlToDirectInputGuid)
            {
                _directInputReader.PollDevice(kvp.Value);
            }
        }

        foreach (var kvp in _openJoysticks)
        {
            int instanceId = kvp.Key;
            IntPtr joystick = kvp.Value;

            if (SDL.SDL_JoystickGetAttached(joystick) == SDL.SDL_bool.SDL_FALSE)
            {
                // Device disconnected
                HandleDeviceDisconnected(instanceId);
                continue;
            }

            if (!_deviceInfo.TryGetValue(instanceId, out var info))
                continue;

            var state = ReadDeviceState(instanceId, joystick, info);

            if (OnlyFireOnChange)
            {
                if (_lastState.TryGetValue(instanceId, out var last) && !HasStateChanged(last, state))
                    continue;

                _lastState[instanceId] = state;
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
    /// Read current state from a joystick using either SDL2 or DirectInput
    /// </summary>
    private DeviceInputState ReadDeviceState(int instanceId, IntPtr joystick, PhysicalDeviceInfo info)
    {
        // Use DirectInput if configured and available for this device
        if (InputBackend == InputPollingBackend.DirectInput &&
            _directInputReader is not null &&
            _sdlToDirectInputGuid.TryGetValue(instanceId, out var diGuid))
        {
            return ReadDeviceStateDirectInput(diGuid, info);
        }

        // Fall back to SDL2
        return ReadDeviceStateSDL(joystick, info);
    }

    /// <summary>
    /// Read device state using DirectInput
    /// </summary>
    private DeviceInputState ReadDeviceStateDirectInput(Guid diGuid, PhysicalDeviceInfo info)
    {
        // Read axes
        var axes = new float[info.AxisCount];
        for (int i = 0; i < info.AxisCount; i++)
        {
            axes[i] = _directInputReader!.GetAxis(diGuid, i);
        }

        // Read buttons
        var buttons = new bool[info.ButtonCount];
        for (int i = 0; i < info.ButtonCount; i++)
        {
            buttons[i] = _directInputReader!.GetButton(diGuid, i);
        }

        // Read hats
        var hats = new int[info.HatCount];
        for (int i = 0; i < info.HatCount; i++)
        {
            hats[i] = _directInputReader!.GetPov(diGuid, i);
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
    /// Read device state using SDL2
    /// </summary>
    private DeviceInputState ReadDeviceStateSDL(IntPtr joystick, PhysicalDeviceInfo info)
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

    /// <summary>
    /// Check for newly connected devices and open them
    /// </summary>
    private void CheckForNewDevices()
    {
        int numJoysticks = SDL.SDL_NumJoysticks();

        for (int i = 0; i < numJoysticks; i++)
        {
            // Get the instance ID for this device index (without opening)
            // We need to check if this instance ID is already known
            // Unfortunately, SDL2 requires opening the joystick to get instance ID
            // So we open it, check, and close if already known

            IntPtr joystick = SDL.SDL_JoystickOpen(i);
            if (joystick == IntPtr.Zero)
                continue;

            int instanceId = SDL.SDL_JoystickInstanceID(joystick);

            // Check if we already have this device open
            if (_knownInstanceIds.Contains(instanceId))
            {
                // Already tracked - close and continue
                SDL.SDL_JoystickClose(joystick);
                continue;
            }

            // Close temporarily - GetDeviceInfo will reopen and track it
            SDL.SDL_JoystickClose(joystick);

            LogAxisTypes($"CheckForNewDevices: Found new device at index {i}, instanceId={instanceId}, numJoysticks={numJoysticks}");

            // New device found - open and track it properly
            var (info, newInstanceId) = GetDeviceInfo(i);
            if (info is not null)
            {
                LogAxisTypes($"CheckForNewDevices: Opened device '{info.Name}' at index {i}, instanceId={newInstanceId}, firing DeviceConnected");
                DeviceConnected?.Invoke(this, info);
            }
        }
    }

    private void HandleDeviceDisconnected(int instanceId)
    {
        // Get device info before removing so we can clean up HID matching
        if (_deviceInfo.TryRemove(instanceId, out var info))
        {
            // Remove from matched HID paths so it can be re-matched on reconnect
            if (!string.IsNullOrEmpty(info.HidDevicePath))
            {
                _matchedHidDevicePaths.Remove(info.HidDevicePath);
            }
        }

        // Close DirectInput device if mapped
        if (_sdlToDirectInputGuid.TryRemove(instanceId, out var diGuid))
        {
            _directInputReader?.CloseDevice(diGuid);
        }

        if (_openJoysticks.TryRemove(instanceId, out var joystick))
        {
            SDL.SDL_JoystickClose(joystick);
        }

        // Remove from known instance IDs so it can be detected again on reconnect
        _knownInstanceIds.Remove(instanceId);

        // Clear HID cache so devices are re-enumerated on reconnect
        _hidDevicesCache = null;

        // Fire event with the device index from info (for UI tracking)
        DeviceDisconnected?.Invoke(this, info?.DeviceIndex ?? -1);
    }

    public void Dispose()
    {
        StopPolling();

        // Dispose DirectInput resources
        _directInputReader?.Dispose();
        _directInputReader = null;
        _directInputService = null;
        _sdlToDirectInputGuid.Clear();

        foreach (var joystick in _openJoysticks.Values)
        {
            SDL.SDL_JoystickClose(joystick);
        }
        _openJoysticks.Clear();
        _deviceInfo.Clear();
        _lastState.Clear();
        _knownInstanceIds.Clear();

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
