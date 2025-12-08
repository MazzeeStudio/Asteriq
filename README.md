# Asteriq

A unified Windows application for HOTAS device management, specifically designed for Star Citizen.

## Status

**Planning Phase** - Design documentation complete, implementation not started.

## Documentation

| Document | Description |
|----------|-------------|
| [OVERVIEW.md](docs/OVERVIEW.md) | Project goals, architecture layers, technology stack |
| [INPUT_LAYER.md](docs/INPUT_LAYER.md) | SDL2 input handling, HidHide integration |
| [VIRTUAL_DEVICES.md](docs/VIRTUAL_DEVICES.md) | vJoy integration, device mapping |
| [SC_INTEGRATION.md](docs/SC_INTEGRATION.md) | Star Citizen bindings (read-only) |
| [UI_DESIGN.md](docs/UI_DESIGN.md) | User interface layout and components |
| [LESSONS_LEARNED.md](docs/LESSONS_LEARNED.md) | Insights from SCVirtStick development |

## Goals

1. **Single Application** - No separate HidHide/vJoy configuration tools needed
2. **Reliable Input** - SDL2 for proven, low-latency device reading
3. **Clean Device Mapping** - Explicit physical device → vJoy slot assignment
4. **SC Integration** - Read-only binding visualization and export

## Technology

- .NET 8 (WinForms)
- SDL2 via SDL2-CS
- vJoy (MIT license)
- HidHide (BSD license)

## Prior Art

This project builds on lessons learned from [SCVirtStick](../SCVirtStick/), incorporating what worked well while addressing architectural limitations.
