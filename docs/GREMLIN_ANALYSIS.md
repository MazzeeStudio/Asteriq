# JoystickGremlinEx Architecture Analysis

Analysis of JoystickGremlinEx codebase for Asteriq design reference.

## Key Architectural Decisions

### 1. Input Layer - DILL (DirectInput Layer)

GremlinEx uses a **custom native C++ DLL** (`dill.dll`) wrapping DirectInput, not raw SDL2:

```
Windows DirectInput API
        ↓
    dill.dll (Native C++)
        ↓
    Python ctypes wrapper
        ↓
    Callback-driven events
```

**Implications for Asteriq**:
- We're using SDL2 which is similar but cross-platform
- SDL2 provides polling, not callbacks - we poll at high frequency instead
- Our approach is simpler but requires efficient polling loop

### 2. Change Detection Strategy

GremlinEx tracks state to avoid event spam:

| Input Type | Strategy |
|------------|----------|
| **Buttons** | Dict tracks pressed state, skips if already pressed |
| **Axes** | Calibration filters noise below deadzone threshold |
| **Hats** | Dual-event system (release old → press new) |
| **Devices** | 0.5s debounce timer on connect/disconnect |

**Implemented in Asteriq**:
- `OnlyFireOnChange` flag with `HasStateChanged()` comparison
- Axis threshold (0.01) to filter jitter
- Button state comparison for actual changes

### 3. Device Identification

GremlinEx uses DirectInput GUID (32-char hex string):
```
f765ae4c4dac40cbabefe9f6187d4689
```

**For Asteriq**:
- SDL2 provides similar GUID via `SDL_JoystickGetDeviceGUID()`
- Need to extract VID:PID for stable identification
- Instance GUID changes per USB port

### 4. vJoy Integration Pattern

```python
# GremlinEx pattern
vjoy_proxy = VJoyProxy()[vjoy_id]  # Singleton accessor
vjoy_proxy.button(index).is_pressed = value
vjoy_proxy.axis(index).value = value  # -1.0 to 1.0
```

Key features:
- **VJoyProxy singleton** for thread-safe access
- **Device matching** via config hash (axis/button/hat counts)
- **Output suppression** to avoid feedback loops
- **Loopback detection** when vJoy feeds back as input

### 5. Threading Model

```
Main UI Thread (Qt)
    ↓
Background threads:
  - Event queue processor
  - Heartbeat/keep-alive
  - Native DLL callbacks
  - Keyboard hook listener
```

**For Asteriq**:
- Simpler model: UI thread + dedicated polling thread
- Thread-safe queue for UI updates (we use `BeginInvoke`)
- Lock protection for shared state

### 6. Device Persistence

GremlinEx tracks ALL devices (connected + disconnected):
```python
_joystick_devices = []         # Connected only
_all_joystick_devices = []     # All (for profile persistence)
```

**For Asteriq**:
- Store device mappings by VID:PID (survives reconnection)
- Track "known devices" even when disconnected
- Restore bindings when device reconnects

## What to Adopt

1. **Change detection with thresholds** - Implemented ✓
2. **Device persistence by identifier** - Need VID:PID extraction
3. **vJoy singleton pattern** - Adopt for thread safety
4. **Debounce on device connect/disconnect** - Add to InputService
5. **Loopback detection** - Important when vJoy is input source

## What to Avoid

1. **Complex plugin architecture** - We want simpler, focused tool
2. **Multiple DLL dependencies** - Keep it self-contained
3. **Qt/Python complexity** - Stay with .NET/C#

---

## Device Merging

GremlinEx merges multiple physical axes into a single vJoy output using mathematical operations.

### Merge Operations

```python
class MergeAxisOperation(Enum):
    Average = 1   # (axis1 - axis2) / 2.0  (note: subtraction, not addition)
    Minimum = 2   # min(axis1, axis2)
    Maximum = 3   # max(axis1, axis2)
    Sum = 4       # clamp(axis1 + axis2, -1.0, 1.0)
```

### Configuration Structure

```python
{
    "vjoy": { "vjoy_id": 1, "axis_id": 1 },
    "lower": { "device_guid": "<GUID>", "axis_id": 0 },
    "upper": { "device_guid": "<GUID>", "axis_id": 0 },
    "mode": "<profile_mode>",
    "operation": MergeAxisOperation.Average
}
```

### Execution (code_runner.py)

```python
class MergeAxis:
    def __init__(self, vjoy_id, input_id, operation):
        self.axis_values = [0.0, 0.0]  # Two input sources

    def _update(self):
        if self.operation == MergeAxisOperation.Average:
            value = (self.axis_values[0] - self.axis_values[1]) / 2.0
        elif self.operation == MergeAxisOperation.Sum:
            value = clamp(self.axis_values[0] + self.axis_values[1], -1.0, 1.0)
        # ... etc

        VJoyProxy()[self.vjoy_id].axis(self.input_id).value = value
```

**Key insight**: Each physical axis registers a callback. When either changes, `_update()` recalculates and writes to vJoy.

### Conflict Resolution

GremlinEx does **NOT** implement explicit conflict resolution:
- Multiple inputs can map to same output (last-write-wins)
- For buttons, simultaneous presses are allowed
- vJoy handles state updates sequentially

**For Asteriq**: We should consider explicit conflict warnings in UI.

---

## Keyboard Binding

GremlinEx maps joystick buttons to keyboard keys using Windows SendInput API.

### SendInput Implementation (sendinput.py)

```python
class _KEYBDINPUT(ctypes.Structure):
    _fields_ = (
        ("wVk", ctypes.wintypes.WORD),      # Virtual key code
        ("wScan", ctypes.wintypes.WORD),    # Scan code
        ("dwFlags", ctypes.wintypes.DWORD), # KEYUP, EXTENDED flags
        ("time", ctypes.wintypes.DWORD),
        ("wExtraInfo", ctypes.POINTER(ctypes.wintypes.ULONG))
    )

def send_key(virtual_code, scan_code, flags):
    _send_input(_keyboard_input(virtual_code, scan_code, flags))
```

**Key Flags**:
- `KEYEVENTF_EXTENDEDKEY` (0x0001) - For extended keys (arrows, home, etc.)
- `KEYEVENTF_KEYUP` (0x0002) - For key release events

### Button-to-Keyboard Flow (map_to_keyboard plugin)

```python
class MapToKeyboardFunctor:
    def __init__(self, action):
        # Build press macro
        self.press = Macro()
        for key in action.keys:
            self.press.press(key_from_code(key[0], key[1]))

        # Build release macro (reversed order!)
        self.release = Macro()
        for key in reversed(action.keys):
            self.release.release(key_from_code(key[0], key[1]))
```

**Important**: Release order is reversed - if you press Ctrl+Shift+A, you release A, then Shift, then Ctrl.

### Modifier Key Handling

Modifiers are sorted and pressed in correct order:
1. Modifiers first (Shift, Control, Alt, Win)
2. Then regular keys

```python
_keyboard_modifiers = [
    "leftshift", "leftcontrol", "leftalt",
    "rightshift", "rightcontrol", "rightalt",
    "leftwin", "rightwin"
]
```

### Timing (macro.py)

```python
class MacroManager:
    def __init__(self):
        self.default_delay = 0.025  # 25ms between actions
```

**Why 25ms?** Prevents games from missing rapid successive key events.

### For Asteriq Implementation

```csharp
// C# equivalent using user32.dll
[DllImport("user32.dll")]
static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

[StructLayout(LayoutKind.Sequential)]
struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

const uint KEYEVENTF_KEYUP = 0x0002;
const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
```

---

## Code Reference

| GremlinEx File | Purpose | Asteriq Equivalent |
|----------------|---------|-------------------|
| `dinput/__init__.py` | Input wrapper | `Services/InputService.cs` |
| `gremlin/joystick_handling.py` | Device management | `Services/DeviceManager.cs` (future) |
| `vjoy/vjoy.py` | vJoy abstraction | Copy from SCVirtStick |
| `gremlin/event_handler.py` | Event processing | `Services/MappingService.cs` (future) |
| `gremlin/code_runner.py` | Merge axis execution | `Services/MergeService.cs` (future) |
| `gremlin/sendinput.py` | Keyboard simulation | `Services/KeyboardService.cs` (future) |
| `action_plugins/map_to_keyboard/` | Button→Key mapping | Part of MappingService |
| `gremlin/macro.py` | Macro/timing system | `Services/MacroService.cs` (future) |

## SDL2 vs DirectInput (DILL)

| Aspect | DILL (GremlinEx) | SDL2 (Asteriq) |
|--------|------------------|----------------|
| Platform | Windows only | Cross-platform |
| Event model | Callback (push) | Polling (pull) |
| Latency | Native callbacks | Poll at 500-1000Hz |
| Complexity | Custom DLL | NuGet package |
| Maintenance | Must maintain DLL | Community maintained |

SDL2 is simpler and well-maintained. Polling at high frequency achieves similar responsiveness.
