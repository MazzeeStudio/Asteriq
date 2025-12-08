# Input Layer Design

## Overview

The input layer handles reading physical HID devices and managing their visibility to other applications via HidHide.

## Why SDL2?

JoystickGremlin uses SDL2 for input and achieves reliable, low-latency device reading. Benefits:

- **Proven reliability** - Used by thousands of games and tools
- **Cross-platform API** - Clean abstraction over DirectInput/XInput/raw HID
- **Hot-plug support** - Handles device connect/disconnect gracefully
- **Permissive license** - zlib, no GPL concerns

## SDL2-CS Integration

```csharp
// NuGet: SDL2-CS (or manual binding)
// SDL2 must be distributed with the app (SDL2.dll)

using SDL2;

// Initialize
SDL.SDL_Init(SDL.SDL_INIT_JOYSTICK | SDL.SDL_INIT_GAMECONTROLLER);

// Enumerate devices
int numJoysticks = SDL.SDL_NumJoysticks();
for (int i = 0; i < numJoysticks; i++)
{
    string name = SDL.SDL_JoystickNameForIndex(i);
    Guid guid = SDL.SDL_JoystickGetDeviceGUID(i);
}

// Open and read
IntPtr joystick = SDL.SDL_JoystickOpen(deviceIndex);
int numAxes = SDL.SDL_JoystickNumAxes(joystick);
int numButtons = SDL.SDL_JoystickNumButtons(joystick);

// Poll loop
SDL.SDL_JoystickUpdate();
short axisValue = SDL.SDL_JoystickGetAxis(joystick, axisIndex);
byte buttonState = SDL.SDL_JoystickGetButton(joystick, buttonIndex);
```

## Device Identification

Physical devices identified by:

```csharp
public class PhysicalDeviceId
{
    public ushort VendorId { get; }      // VID
    public ushort ProductId { get; }     // PID
    public Guid InstanceGuid { get; }    // Unique per physical port
    public string DevicePath { get; }    // HID device path for HidHide
}
```

**Important**: Instance GUID changes if device is plugged into different USB port. Use VID:PID for device type matching, Instance GUID for specific device tracking.

## HidHide Integration

Initially via CLI (proven to work):

```csharp
public class HidHideManager
{
    private const string CliPath = @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe";

    public void HideDevice(string devicePath)
    {
        RunCli($"--dev-hide \"{devicePath}\"");
    }

    public void UnhideDevice(string devicePath)
    {
        RunCli($"--dev-unhide \"{devicePath}\"");
    }

    public void WhitelistApplication(string exePath)
    {
        RunCli($"--app-reg \"{exePath}\"");
    }

    public void SetCloakState(bool enabled)
    {
        RunCli(enabled ? "--cloak-on" : "--cloak-off");
    }
}
```

**Future**: Direct API via HidHide's driver IOCTL interface for tighter integration.

## Input State Model

```csharp
public class DeviceInputState
{
    public PhysicalDeviceId DeviceId { get; }
    public DateTime Timestamp { get; }

    public float[] Axes { get; }        // Normalized -1.0 to 1.0
    public bool[] Buttons { get; }      // True = pressed
    public int[] Hats { get; }          // POV angle or -1 for center
}
```

## Input Pipeline

```
Physical Device
     │
     ▼
┌─────────────┐
│   SDL2      │  Raw input reading
└─────────────┘
     │
     ▼
┌─────────────┐
│ Normalizer  │  Convert to -1.0 to 1.0, apply deadzones
└─────────────┘
     │
     ▼
┌─────────────┐
│  Mapper     │  Route to virtual device (see VIRTUAL_DEVICES.md)
└─────────────┘
     │
     ▼
┌─────────────┐
│   vJoy      │  Output to virtual device
└─────────────┘
```

## Threading Model

- **Input thread** - Dedicated thread polling SDL2 at high frequency (1000Hz)
- **UI thread** - Standard WinForms message pump
- **Communication** - Thread-safe queue or events between input and UI

```csharp
public class InputPoller
{
    private readonly ConcurrentQueue<DeviceInputState> _stateQueue;
    private volatile bool _running;

    public void Start()
    {
        _running = true;
        Task.Run(PollLoop);
    }

    private void PollLoop()
    {
        while (_running)
        {
            SDL.SDL_JoystickUpdate();
            // Read all devices, enqueue states
            Thread.Sleep(1); // ~1000Hz
        }
    }
}
```

## Error Handling

- Device disconnect: Graceful cleanup, notify UI, allow reconnect
- SDL2 init failure: Clear error message, check SDL2.dll presence
- Permission issues: Guide user to run as admin if needed

## Dependencies

| Package | Version | Notes |
|---------|---------|-------|
| SDL2-CS | latest | NuGet or manual |
| SDL2.dll | 2.28+ | Distribute with app |
