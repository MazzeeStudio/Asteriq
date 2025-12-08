# Virtual Devices Layer Design

## Overview

This layer manages vJoy virtual joystick devices and the mapping from physical devices to virtual outputs.

## vJoy Architecture

vJoy consists of:
- **Kernel driver** - Creates virtual HID devices visible to Windows
- **vJoyInterface.dll** - User-mode API for feeding input to virtual devices
- **vJoyConfig.exe** - Configuration tool for device setup

## Device Slot Model

```csharp
public class VJoySlot
{
    public uint SlotId { get; }              // 1-16
    public bool IsConfigured { get; }        // Has axes/buttons configured
    public bool IsAcquired { get; }          // Currently owned by this app

    public int AxisCount { get; }
    public int ButtonCount { get; }
    public int HatCount { get; }
}

public class VJoyManager
{
    public IReadOnlyList<VJoySlot> Slots { get; }

    public bool AcquireSlot(uint slotId);
    public void ReleaseSlot(uint slotId);
    public void SetAxisValue(uint slotId, Axis axis, float value);
    public void SetButton(uint slotId, int button, bool pressed);
}
```

## Physical to Virtual Mapping

**Core concept**: Each physical device explicitly maps to one vJoy slot.

```csharp
public class DeviceMapping
{
    public PhysicalDeviceId PhysicalDevice { get; }
    public uint VJoySlot { get; }

    public List<AxisMapping> Axes { get; }
    public List<ButtonMapping> Buttons { get; }
}

public class AxisMapping
{
    public int SourceAxis { get; }           // Physical axis index
    public Axis TargetAxis { get; }          // vJoy axis (X, Y, Z, Rx, Ry, Rz, Sl0, Sl1)
    public bool Inverted { get; }
    public float DeadZone { get; }           // 0.0 - 1.0
    public CurveType Curve { get; }          // Linear, SCurve, Custom
}

public class ButtonMapping
{
    public int SourceButton { get; }
    public int TargetButton { get; }         // 1-128
}
```

## Device Merging

Multiple physical devices can feed a single vJoy slot:

```csharp
public class MergedDevice
{
    public uint VJoySlot { get; }
    public List<DeviceMapping> Sources { get; }
}

// Example: Left grip + Right grip → Single vJoy device
var merged = new MergedDevice
{
    VJoySlot = 1,
    Sources = new[]
    {
        new DeviceMapping { PhysicalDevice = leftGrip, ... },
        new DeviceMapping { PhysicalDevice = rightGrip, ... }
    }
};
```

**Conflict resolution**: If two physical buttons map to same virtual button, either is sufficient (OR logic).

## vJoy Configuration

vJoy slots must be pre-configured with correct axis/button counts.

```csharp
public class VJoyConfigurator
{
    // Uses vJoyConfig.exe CLI
    public void ConfigureSlot(uint slot, VJoyConfig config)
    {
        // vJoyConfig.exe {slot} -f -a X Y Z Rx Ry Rz Sl0 Sl1 -b {buttonCount}
        var args = $"{slot} -f -a X Y Z Rx Ry Rz Sl0 Sl1 -b {config.ButtonCount}";
        Process.Start("vJoyConfig.exe", args);
    }
}

public class VJoyConfig
{
    public List<Axis> Axes { get; } = new() { Axis.X, Axis.Y, Axis.Z, Axis.Rx, Axis.Ry, Axis.Rz, Axis.Sl0, Axis.Sl1 };
    public int ButtonCount { get; } = 128;
    public int HatCount { get; } = 0;        // We map hats to buttons instead
}
```

## Output Pipeline

```csharp
public class VJoyFeeder
{
    private readonly VJoyManager _vjoy;
    private readonly Dictionary<PhysicalDeviceId, DeviceMapping> _mappings;

    public void ProcessInput(DeviceInputState input)
    {
        if (!_mappings.TryGetValue(input.DeviceId, out var mapping))
            return;

        var slot = mapping.VJoySlot;

        // Process axes
        foreach (var axisMap in mapping.Axes)
        {
            float value = input.Axes[axisMap.SourceAxis];
            value = ApplyDeadzone(value, axisMap.DeadZone);
            value = ApplyCurve(value, axisMap.Curve);
            if (axisMap.Inverted) value = -value;

            _vjoy.SetAxisValue(slot, axisMap.TargetAxis, value);
        }

        // Process buttons
        foreach (var btnMap in mapping.Buttons)
        {
            bool pressed = input.Buttons[btnMap.SourceButton];
            _vjoy.SetButton(slot, btnMap.TargetButton, pressed);
        }
    }
}
```

## Keyboard Integration

For keyboard passthrough (modifier keys, etc.):

```csharp
public class KeyboardMapping
{
    public int SourceButton { get; }         // Physical button
    public Keys TargetKey { get; }           // Keyboard key to simulate
    public bool IsModifier { get; }          // Ctrl, Alt, Shift
}

// Use Windows SendInput API for keyboard simulation
[DllImport("user32.dll")]
static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
```

## Configuration Persistence

```json
{
  "version": "1.0",
  "devices": [
    {
      "physicalDevice": {
        "vid": "3344",
        "pid": "0194",
        "name": "VPC Constellation Alpha-R"
      },
      "vjoySlot": 1,
      "axes": [
        { "source": 0, "target": "X", "inverted": false, "deadzone": 0.02 },
        { "source": 1, "target": "Y", "inverted": false, "deadzone": 0.02 }
      ],
      "buttons": [
        { "source": 0, "target": 1 },
        { "source": 1, "target": 2 }
      ]
    }
  ]
}
```

## Existing Code to Reuse

From SCVirtStick:
- `vJoyWrapper/` - P/Invoke bindings to vJoyInterface.dll (MIT licensed, our code)
- Basic vJoy acquisition and feeding logic

## Error Handling

- vJoy not installed: Clear error, link to download
- Slot not configured: Prompt to configure or auto-configure
- Slot acquired by another app: Clear error message
- Driver version mismatch: Version check on startup
