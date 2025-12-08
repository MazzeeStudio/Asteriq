# Asteriq Development Roadmap

## Phase 1: Input Foundation ✅ COMPLETE
**Goal**: Reliable physical device reading and diagnostics

- [x] Project setup (.NET 8, WinForms)
- [x] SDL2 integration via ppy.SDL2-CS
- [x] InputService - device enumeration, polling, events
- [x] Console diagnostics mode (--diag)
- [x] xUnit test project setup
- [x] Input reliability testing with VPC WarBRD sticks
- [x] Change detection (OnlyFireOnChange) to avoid event spam
- [x] VID/PID extraction from SDL2 GUID for device matching

## Phase 2: Virtual Device Output ✅ COMPLETE
**Goal**: Feed physical input to vJoy virtual devices

- [x] vJoy SDK integration (VJoyInterop, VJoyService)
- [x] VJoyService - device acquisition, axis/button feeding
- [x] Basic physical → vJoy passthrough (--passthrough CLI)
- [x] Device mapping configuration model (Mappings.cs)
- [x] Unit tests for VJoyService

## Phase 3: Device Hiding ✅ COMPLETE
**Goal**: Hide physical devices from other apps, expose only vJoy

- [x] HidHideService - CLI wrapper
- [x] Device path extraction for HidHide
- [x] Auto-whitelist Asteriq.exe (--whitelist CLI)
- [x] Device matching between SDL and HidHide (--match CLI)
- [x] Inverse mode support (EnsureSelfCanSeeDevices)
- [x] Unit tests for DeviceMatchingService

## Phase 4: Mapping Engine ✅ COMPLETE
**Goal**: Flexible input transformation and routing

- [x] MappingEngine - processes input through mappings
- [x] Axis curves (Linear, Exponential, S-Curve, Custom)
- [x] Axis parameters (deadzone, saturation, inversion)
- [x] Button modes (Normal, Toggle, Pulse, Hold-to-Activate)
- [x] Multi-input merge operations (Average, Min, Max, Sum)
- [x] Keyboard output via Windows SendInput API
- [x] --maptest CLI for testing with curves
- [x] --keytest CLI for keyboard output testing
- [x] Unit tests for AxisCurve, MappingEngine, KeyboardService

## Phase 5: Profile Persistence ✅ COMPLETE
**Goal**: Save and load mapping configurations

- [x] ProfileService - save/load/list/delete profiles
- [x] JSON serialization to %APPDATA%\Asteriq\Profiles
- [x] Profile export/import for sharing
- [x] Profile duplication
- [x] Last profile tracking and auto-load setting
- [x] CLI commands (--profiles, --profile-save, --profile-load, etc.)
- [x] Unit tests for ProfileService

## Phase 6: Advanced Mapping Features (Current)
**Goal**: Complete mapping functionality for HOTAS use cases

- [ ] Hat/POV mapping - pass hats through to vJoy POV outputs
- [ ] Shift/mode layers - hold button to change what other inputs do
- [ ] Axis-to-button - trigger button when axis crosses threshold
- [ ] Button-to-axis - map button press to axis value
- [ ] Unit tests for all new mapping types

## Phase 7: Configuration UI
**Goal**: Device mapping and configuration interface

- [ ] Custom window chrome (borderless, dark theme)
- [ ] Device list panel (physical devices with status)
- [ ] vJoy slot assignment UI
- [ ] Axis/button/hat mapping editor
- [ ] Curve editor with visual preview
- [ ] Shift layer configuration
- [ ] Real-time input visualization
- [ ] Profile management UI

## Phase 8: Star Citizen Integration
**Goal**: Read and visualize SC bindings

- [ ] P4K reader (extract default actionmaps)
- [ ] User actionmaps.xml parser
- [ ] Binding data model
- [ ] Device → SC instance mapping (js1, js2, etc.)
- [ ] Binding visualization on device silhouette
- [ ] Export to actionmaps.xml format

## Phase 9: Polish and Release
**Goal**: Production-ready application

- [ ] Installer/setup
- [ ] Auto-start with Windows option
- [ ] System tray mode
- [ ] Device hot-plug handling
- [ ] Error handling and user feedback
- [ ] Documentation for end users
- [ ] Performance optimization

---

## Current Focus

**Phase 6** - Advanced Mapping Features:
1. Hat/POV passthrough to vJoy
2. Shift/mode layers for button remapping
3. Axis-to-button threshold triggers

## Dependencies Between Phases

```
Phase 1 (Input) ──> Phase 2 (vJoy) ──> Phase 3 (HidHide)
                          │
                          v
                    Phase 4 (Mapping Engine)
                          │
                          v
                    Phase 5 (Profiles)
                          │
                          v
                    Phase 6 (Advanced Mappings) <-- CURRENT
                          │
                          v
                    Phase 7 (Config UI)
                          │
                          v
                    Phase 8 (SC Integration)
                          │
                          v
                    Phase 9 (Polish)
```

## Reusable Code from SCVirtStick

| Component | Source | Effort |
|-----------|--------|--------|
| vJoy wrapper | `vJoyWrapper/` | ✅ Done |
| P4K reading | `Services/StarCitizenService.cs` | Extract and clean |
| Actionmaps parsing | `Services/ActionMapsService.cs` | Heavy refactor |
| Control maps | `Models/DeviceControlMap.cs` | Copy and simplify |
| SVG concepts | `UI/FUI/DeviceSilhouette.cs` | Rewrite from scratch |

## Non-Goals (Out of Scope)

- Macro/scripting support (unlike JoystickGremlin)
- Xbox controller remapping (XInput devices)
- Cross-platform support (Windows only)
- Plugin system
