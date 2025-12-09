# vJoy Mapping System Design

## Overview

The mapping system allows users to assign physical device inputs (buttons, axes, hats) to vJoy virtual device outputs or keyboard keys. Mappings are organized into profiles that can be saved, loaded, and switched.

## Core Concepts

### Profile
A named collection of all device mappings. Only one profile can be active at a time.
- Profiles are stored as JSON files in a `Profiles/` directory
- A profile must be selected before any physical→vJoy mapping is active
- Default behavior: Load last-used profile on app startup (configurable)
- Profiles can be exported/imported for sharing

### Mapping
A single input→output assignment:
- **Source**: Physical device + control (e.g., "LEFT VPC Stick" + "button4")
- **Target**: vJoy device + control OR keyboard key/sequence

### Device Assignment
When a physical device is mapped to a vJoy device, the vJoy device inherits:
- The device visualization (SVG + device map)
- Mirror setting (left/right hand)
- All input mappings for that device

## File Structure

```
Asteriq/
├── Profiles/
│   ├── default.json        # Default profile
│   ├── star_citizen.json   # User profile
│   └── elite.json          # Another user profile
└── settings.json           # App settings including last-used profile
```

## Profile JSON Schema

```json
{
  "name": "Star Citizen",
  "version": "1.0",
  "created": "2024-01-15T10:30:00Z",
  "modified": "2024-01-15T14:22:00Z",
  "deviceAssignments": [
    {
      "physicalDevice": {
        "name": "LEFT VPC Stick WarBRD",
        "guid": "abc123...",
        "vidPid": "3344:0194"
      },
      "vJoyDevice": 1,
      "deviceMapOverride": null
    }
  ],
  "mappings": [
    {
      "id": "mapping-001",
      "source": {
        "deviceGuid": "abc123...",
        "inputType": "button",
        "inputIndex": 4,
        "inputName": "Trigger Stage 1"
      },
      "target": {
        "type": "vjoy",
        "vJoyDevice": 1,
        "outputType": "button",
        "outputIndex": 1
      },
      "options": {
        "mode": "normal"
      }
    },
    {
      "id": "mapping-002",
      "source": {
        "deviceGuid": "abc123...",
        "inputType": "axis",
        "inputIndex": 0,
        "inputName": "X Axis"
      },
      "target": {
        "type": "vjoy",
        "vJoyDevice": 1,
        "outputType": "axis",
        "outputIndex": 0
      },
      "options": {
        "curve": "linear",
        "deadzone": 0.05,
        "saturation": 1.0,
        "invert": false
      }
    },
    {
      "id": "mapping-003",
      "source": {
        "deviceGuid": "abc123...",
        "inputType": "button",
        "inputIndex": 7,
        "inputName": "Top Button"
      },
      "target": {
        "type": "keyboard",
        "key": "Space",
        "modifiers": []
      },
      "options": {
        "mode": "normal"
      }
    }
  ]
}
```

## Target Types

### vJoy Target
Maps to a vJoy virtual device output:
- Button → Button
- Axis → Axis
- Hat → Hat (or 4 buttons)

### Keyboard Target
Maps to keyboard key press:
- Single key: "Space", "A", "F1", etc.
- With modifiers: Ctrl+Space, Alt+Shift+F1
- Key sequences (future): Press A, then B

## Mapping Options

### Button Modes
- **normal**: Press = on, release = off
- **toggle**: Press toggles on/off state
- **pulse**: Press sends brief pulse
- **hold**: Must hold for specified duration

### Axis Options
- **curve**: linear, exponential, s-curve, custom
- **deadzone**: 0.0 - 0.5 (percentage from center)
- **saturation**: 0.5 - 1.0 (percentage for full output)
- **invert**: Reverse axis direction

## UI Design

### Mappings Tab

```
+------------------------------------------------------------------+
| MAPPINGS                                          [Profile: ▼ SC] |
+------------------------------------------------------------------+
| [+ New Mapping]  [Import]  [Export]  [Delete Profile]            |
+------------------------------------------------------------------+
| SOURCE                    | TARGET                   | OPTIONS   |
+------------------------------------------------------------------+
| LEFT VPC Stick            |                          |           |
|   Trigger (btn4)          | vJoy 1 → Button 1        | Normal    |
|   Trigger S2 (btn5)       | vJoy 1 → Button 2        | Normal    |
|   X Axis                  | vJoy 1 → X Axis          | Linear    |
|   Y Axis                  | vJoy 1 → Y Axis          | S-Curve   |
|   Top Button (btn7)       | Keyboard → Space         | Normal    |
+------------------------------------------------------------------+
| RIGHT VPC Stick           |                          |           |
|   Trigger (btn4)          | vJoy 2 → Button 1        | Normal    |
|   ...                     |                          |           |
+------------------------------------------------------------------+
```

### Assignment Flow

1. User clicks [+ New Mapping] or clicks empty row
2. Dialog appears: "Press a button or move an axis on your device..."
3. User activates physical control
4. System detects input, shows: "LEFT VPC Stick - Button 4 (Trigger)"
5. User selects target:
   - vJoy device dropdown + control dropdown, OR
   - "Keyboard" tab + key capture
6. User sets options (mode, curve, etc.)
7. Mapping saved to active profile

### Physical Device View (Devices Tab)

When viewing a physical device:
- Shows device SVG with lead-lines for active inputs
- Below SVG, shows mapping table for this device only:

```
+------------------------------------------------------------------+
| CONTROL              | MAPPED TO                      | OPTIONS   |
+------------------------------------------------------------------+
| Trigger (btn4)       | vJoy 1 → Button 1              | Normal    |
| X Axis               | vJoy 1 → X Axis                | Linear    |
| Top Button (btn7)    | Keyboard → Space               | Normal    |
+------------------------------------------------------------------+
```

### vJoy Device View (Devices Tab)

When viewing a vJoy device:
- Shows table of all inputs mapped TO this vJoy:

```
+------------------------------------------------------------------+
| SOURCE DEVICE        | SOURCE CONTROL    | VJOY CONTROL | OPTIONS |
+------------------------------------------------------------------+
| LEFT VPC Stick       | Trigger (btn4)    | Button 1     | Normal  |
| LEFT VPC Stick       | X Axis            | X Axis       | Linear  |
| RIGHT VPC Stick      | Trigger (btn4)    | Button 2     | Normal  |
+------------------------------------------------------------------+
```

## Implementation Phases

### Phase 1: Profile Infrastructure
- [ ] Create Profile model class
- [ ] Create ProfileService for load/save/switch
- [ ] Add profile selector to UI
- [ ] Store last-used profile in settings

### Phase 2: Basic Button Mapping
- [ ] Create Mapping model class
- [ ] Implement input detection (wait for button press)
- [ ] Create mapping dialog UI
- [ ] Wire up vJoy output

### Phase 3: Axis Mapping
- [ ] Extend mapping for axes
- [ ] Implement axis options (deadzone, curve, invert)
- [ ] Add axis curve editor UI

### Phase 4: Keyboard Output
- [ ] Add keyboard target type
- [ ] Implement key simulation (SendInput)
- [ ] Add modifier key support

### Phase 5: Advanced Features
- [ ] SVG click-to-assign
- [ ] Import/Export profiles
- [ ] Hat mappings
- [ ] Button modes (toggle, pulse, hold)

## Technical Notes

### Device Identification
Physical devices are identified by:
1. **GUID** (primary) - Unique instance identifier
2. **VID:PID** (fallback) - Vendor/Product ID for device type matching
3. **Name** (display) - Human-readable name

GUIDs may change between sessions if USB ports change. VID:PID helps re-match devices.

### Input Processing Pipeline
```
Physical Input → Mapping Lookup → Transform (curve/deadzone) → Output (vJoy/Keyboard)
```

### Thread Safety
- Input polling on background thread
- Mapping lookups must be thread-safe
- UI updates via dispatcher/invoke

## Design Decisions

1. **One input to multiple outputs**: No. Not needed for current scope. May revisit if macro support is added later.

2. **Virtual layers/shift states**: Future feature. Star Citizen already supports modifier keys (e.g., R+Ctrl), but native layer support in Asteriq would be useful. Marked as pending.

3. **Device disconnect handling**: Must be graceful. JoystickGremlin has poor behavior when devices disconnect. Asteriq should:
   - Continue running without crashing
   - Show disconnected devices as "OFFLINE" in device list
   - Pause mappings for disconnected devices (no phantom inputs)
   - Auto-resume mappings when device reconnects (match by VID:PID + name)
   - Never block or hang waiting for a device

## Future Features (Out of Scope for Initial Implementation)

- [ ] Macro support (sequences, delays)
- [ ] Virtual layers/shift states
- [ ] One-to-many mappings
- [ ] Conditional mappings (if X then Y)
