# Asteriq - Project Overview

## Purpose

A unified Windows application for HOTAS (Hands On Throttle And Stick) device management, specifically designed for Star Citizen. Combines physical device hiding, virtual joystick creation, and SC binding visualization into a single application.

## Core Goals

1. **Single Application** - No separate tool dependencies (HidHide GUI, vJoy Config, etc.)
2. **Reliable Input** - Use SDL2 for proven, low-latency input handling
3. **Clean Device Mapping** - Strict physical device → vJoy slot assignment
4. **SC Integration** - Read-only binding visualization and export (no actionmaps mutation)

## Architecture Layers

```
┌─────────────────────────────────────────┐
│  UI Layer (.NET WinForms/WPF)           │
│  - Device management views              │
│  - Binding visualization                │
│  - Configuration                        │
├─────────────────────────────────────────┤
│  SC Integration Layer                   │
│  - P4K reader (game defaults)           │
│  - Actionmaps parser (user bindings)    │
│  - Binding export/generation            │
│  See: SC_INTEGRATION.md                 │
├─────────────────────────────────────────┤
│  Virtual Device Layer                   │
│  - vJoy device creation/management      │
│  - Physical → Virtual mapping           │
│  - Device merging                       │
│  See: VIRTUAL_DEVICES.md                │
├─────────────────────────────────────────┤
│  Input Layer                            │
│  - SDL2 physical device reading         │
│  - HidHide integration                  │
│  - Input state management               │
│  See: INPUT_LAYER.md                    │
└─────────────────────────────────────────┘
```

## Technology Stack

| Component | Technology | License | Notes |
|-----------|------------|---------|-------|
| UI | .NET 8 WinForms | MIT | Proven from SCVirtStick |
| Input | SDL2 via SDL2-CS | zlib | Same as JoystickGremlin |
| Virtual Devices | vJoy | MIT | Existing wrapper usable |
| Device Hiding | HidHide | BSD | CLI initially, API later |
| SC Data | Custom parsers | - | P4K (zip), XML |

## Key Principles

1. **Read-only SC integration** - Never modify user's actionmaps.xml directly
2. **Explicit device assignment** - User explicitly assigns physical → vJoy slot
3. **Fail-safe defaults** - If config is missing, don't assume
4. **Single source of truth** - Device mappings stored in one config file

## Related Documents

- [INPUT_LAYER.md](INPUT_LAYER.md) - Physical device input handling
- [VIRTUAL_DEVICES.md](VIRTUAL_DEVICES.md) - vJoy integration
- [SC_INTEGRATION.md](SC_INTEGRATION.md) - Star Citizen binding system
- [UI_DESIGN.md](UI_DESIGN.md) - User interface concepts
- [LESSONS_LEARNED.md](LESSONS_LEARNED.md) - Insights from SCVirtStick
