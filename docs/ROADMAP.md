# Asteriq Development Roadmap

## Phase 1: Input Foundation (Current)
**Goal**: Reliable physical device reading and diagnostics

- [x] Project setup (.NET 8, WinForms)
- [x] SDL2 integration via ppy.SDL2-CS
- [x] InputService - device enumeration, polling, events
- [x] Console diagnostics mode (--diag)
- [x] xUnit test project setup
- [x] Input reliability testing with VPC WarBRD sticks
- [x] Change detection (OnlyFireOnChange) to avoid event spam
- [ ] Device identification improvements (VID/PID extraction from SDL2 GUID)
- [ ] Deadzone and curve application in input pipeline

## Phase 2: Virtual Device Output
**Goal**: Feed physical input to vJoy virtual devices

- [ ] vJoy SDK integration (copy wrapper from SCVirtStick)
- [ ] VJoyService - device acquisition, feeding
- [ ] Basic physical → vJoy passthrough (1:1 mapping)
- [ ] Device mapping configuration model
- [ ] Mapping persistence (JSON config)
- [ ] Unit tests for mapping logic

## Phase 3: Device Hiding
**Goal**: Hide physical devices from other apps, expose only vJoy

- [ ] HidHideService - CLI wrapper
- [ ] Device path extraction for HidHide
- [ ] Auto-whitelist Asteriq.exe
- [ ] Hide/unhide on app start/stop
- [ ] Integration tests with actual hardware

## Phase 4: Configuration UI
**Goal**: Device mapping and configuration interface

- [ ] Custom window chrome (borderless, dark theme)
- [ ] Device list panel (physical devices with status)
- [ ] vJoy slot assignment UI
- [ ] Axis/button mapping editor
- [ ] Mapping preview (show what input maps where)
- [ ] Configuration save/load

## Phase 5: Star Citizen Integration
**Goal**: Read and visualize SC bindings

- [ ] P4K reader (extract default actionmaps)
- [ ] User actionmaps.xml parser
- [ ] Binding data model
- [ ] Device → SC instance mapping (js1, js2, etc.)
- [ ] Binding visualization on device silhouette
- [ ] Export to actionmaps.xml format

## Phase 6: Polish and Release
**Goal**: Production-ready application

- [ ] Installer/setup
- [ ] Auto-start with Windows option
- [ ] System tray mode
- [ ] Error handling and user feedback
- [ ] Documentation for end users
- [ ] Performance optimization

---

## Current Focus

**Phase 1** - Completing input foundation:
1. Test SDL2 input with your VKB/Virpil devices
2. Verify axis/button/hat reading is reliable
3. Extract VID:PID from device GUID for stable identification

## Dependencies Between Phases

```
Phase 1 (Input) ──┬──> Phase 2 (vJoy Output)
                  │
                  └──> Phase 3 (HidHide)
                            │
                            v
                      Phase 4 (Config UI)
                            │
                            v
                      Phase 5 (SC Integration)
                            │
                            v
                      Phase 6 (Polish)
```

Phases 2 and 3 can be worked in parallel after Phase 1.
Phase 4 requires both 2 and 3.
Phase 5 can start once basic UI is in place.

## Reusable Code from SCVirtStick

| Component | Source | Effort |
|-----------|--------|--------|
| vJoy wrapper | `vJoyWrapper/` | Copy directly |
| P4K reading | `Services/StarCitizenService.cs` | Extract and clean |
| Actionmaps parsing | `Services/ActionMapsService.cs` | Heavy refactor |
| Control maps | `Models/DeviceControlMap.cs` | Copy and simplify |
| SVG concepts | `UI/FUI/DeviceSilhouette.cs` | Rewrite from scratch |

## Non-Goals (Out of Scope)

- Macro/scripting support (unlike JoystickGremlin)
- Xbox controller remapping (XInput devices)
- Cross-platform support (Windows only)
- Plugin system
