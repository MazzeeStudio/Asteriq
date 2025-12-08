# UI Design Document

## Overview

The UI follows the FUI (Futuristic User Interface) aesthetic established in SCVirtStick, with a focus on clarity and functionality.

## Main Window Layout

```
┌──────────────────────────────────────────────────────────────────┐
│  VirtualHOTAS                                        [_][□][X]   │
├──────────────────────────────────────────────────────────────────┤
│  [Devices]  [Mappings]  [SC Bindings]  [Settings]                │
├────────────────────────────────────┬─────────────────────────────┤
│                                    │                             │
│   Main Content Area                │   Side Panel                │
│   (changes per tab)                │   (context-sensitive)       │
│                                    │                             │
│                                    │                             │
│                                    │                             │
│                                    │                             │
├────────────────────────────────────┴─────────────────────────────┤
│  Status: Ready | vJoy: OK | HidHide: OK | Devices: 3 connected   │
└──────────────────────────────────────────────────────────────────┘
```

## Tab Views

### 1. Devices Tab

Shows all physical devices and their vJoy assignments.

```
┌─ Physical Devices ──────────────────┬─ Device Details ───────────┐
│                                     │                            │
│  [✓] VPC Alpha-R      → vJoy 1     │  VPC Constellation Alpha-R │
│  [✓] VPC Alpha-L      → vJoy 1     │  ─────────────────────────  │
│  [ ] VPC Throttle     → None       │  VID:PID: 3344:0194        │
│                                     │  Buttons: 32               │
│  [Hide Selected]  [Configure vJoy]  │  Axes: 6                   │
│                                     │                            │
│                                     │  [Device Image/SVG]        │
│                                     │                            │
│                                     │  Live Input:               │
│                                     │  X: ████████░░ 0.75        │
│                                     │  Y: ░░░░░░░░░░ 0.00        │
│                                     │  Btn1: ● Btn2: ○           │
└─────────────────────────────────────┴────────────────────────────┘
```

**Features**:
- List all detected physical devices
- Checkbox to hide/unhide via HidHide
- Dropdown to assign vJoy slot
- Live input visualization

### 2. Mappings Tab

Configure axis/button mappings from physical to virtual.

```
┌─ Device: VPC Alpha-R ───────────────┬─ Mapping Editor ───────────┐
│                                     │                            │
│  Axes:                              │  Selected: X Axis          │
│  ├─ X → vJoy X  [Curve] [Invert]   │  ─────────────────────────  │
│  ├─ Y → vJoy Y  [Curve] [Invert]   │                            │
│  ├─ Z → vJoy Rz [Curve] [Invert]   │  Source: Physical X        │
│  └─ ...                             │  Target: vJoy X            │
│                                     │  Deadzone: [====░░] 5%     │
│  Buttons:                           │  Curve: [Linear ▼]         │
│  ├─ Btn1 → vJoy Btn1               │  Invert: [ ]               │
│  ├─ Btn2 → vJoy Btn2               │                            │
│  └─ ...                             │  [Curve Preview Graph]     │
│                                     │                            │
│  [Auto-Map] [Clear All]             │  [Apply] [Reset]           │
└─────────────────────────────────────┴────────────────────────────┘
```

**Features**:
- Visual mapping editor
- Deadzone configuration
- Response curve editor (linear, S-curve, custom)
- Auto-map function (1:1 mapping)

### 3. SC Bindings Tab

View Star Citizen bindings (read-only).

```
┌─ Action Bindings ───────────────────┬─ Device View ──────────────┐
│                                     │                            │
│  [Search: ________] [Bound Only ✓]  │  L-VPC Alpha-R             │
│                                     │                            │
│  ▼ Flight Control                   │  [Device SVG with          │
│    Pitch       │ Y    │ --  │ --   │   highlighted controls]    │
│    Roll        │ X    │ --  │ --   │                            │
│    Yaw         │ Z    │ --  │ --   │                            │
│  ▼ Weapons                          │                            │
│    Fire Group 1│ Btn1 │ --  │ --   │                            │
│    Fire Group 2│ Btn2 │ --  │ --   │                            │
│                                     │                            │
│  [Load Profile] [Export Bindings]   │  [js1 ▼] [js2 ▼]          │
└─────────────────────────────────────┴────────────────────────────┘
```

**Features**:
- Read-only binding display
- Hover highlights control on device SVG
- Filter by bound actions
- Search functionality
- Export to new actionmaps file

### 4. Settings Tab

Application configuration.

```
┌─ Settings ──────────────────────────────────────────────────────┐
│                                                                  │
│  Star Citizen                                                    │
│  ─────────────────────────────────────────────────────────────  │
│  Install Path: [C:\Program Files\RSI\StarCitizen    ] [Browse]  │
│  Environment:  [LIVE ▼]                                         │
│                                                                  │
│  Appearance                                                      │
│  ─────────────────────────────────────────────────────────────  │
│  Theme: [Slate ▼]                                               │
│                                                                  │
│  Input                                                          │
│  ─────────────────────────────────────────────────────────────  │
│  Poll Rate: [1000 Hz ▼]                                         │
│  Global Deadzone: [====░░░░░░] 2%                               │
│                                                                  │
│  Advanced                                                        │
│  ─────────────────────────────────────────────────────────────  │
│  [  ] Start minimized to tray                                   │
│  [✓] Start with Windows                                         │
│  [  ] Enable debug logging                                      │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

## Theme System

Reuse FUITheme from SCVirtStick:

```csharp
public static class FUITheme
{
    // Background colors
    public static Color BgBase0 { get; }     // Darkest
    public static Color BgBase1 { get; }
    public static Color BgBase2 { get; }     // Lightest

    // Accent colors
    public static Color Accent1 { get; }
    public static Color Accent2 { get; }

    // Text colors
    public static Color TextPrimary { get; }
    public static Color TextSecondary { get; }
    public static Color TextDim { get; }

    // Schemes: slate, mono, amber, magenta, acidgreen, sunset, midnight, star-citizen
    public static void ApplyScheme(string name);
}
```

## Custom Controls to Build

1. **DeviceListItem** - Row in device list with status indicators
2. **AxisMappingEditor** - Visual axis configuration
3. **CurveEditor** - Response curve graph with draggable points
4. **DeviceSilhouette** - SVG device display with highlighting (from SCVirtStick)
5. **BindingMatrix** - Action/binding grid (from SCVirtStick)

## First-Run Experience

1. Detect installed devices
2. Check vJoy/HidHide installation
3. Guide user through initial device → vJoy mapping
4. Offer to detect SC install path

## Responsive Behavior

- Minimum window size: 1024x768
- Side panel collapsible on narrow windows
- Device SVG scales to available space
