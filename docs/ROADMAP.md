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

## Phase 6: Advanced Mapping Features ✅ COMPLETE
**Goal**: Complete mapping functionality for HOTAS use cases

- [x] Hat/POV mapping - pass hats through to vJoy POV outputs (continuous + discrete)
- [x] Shift/mode layers - hold button to change what other inputs do
- [x] Axis-to-button - trigger button when axis crosses threshold (with hysteresis)
- [x] Button-to-axis - map button press to axis value (with smoothing)
- [x] Unit tests for all new mapping types (35 tests)

## Phase 7: Configuration UI (Current)
**Goal**: FUI (Futuristic User Interface) for device mapping and configuration

- [x] FUI theme system (slate primary color scheme)
- [x] Custom window chrome (borderless, FUI styling)
- [x] Tab navigation (Devices, Mappings, SC Bindings, Settings)
- [x] Device list panel with live input visualization
- [x] vJoy slot assignment UI (MAPPINGS tab)
- [x] Axis/button/hat mapping editor
- [x] Curve editor with visual preview
- [ ] Shift layer configuration
- [x] Profile management UI (dropdown in title bar)
- [x] Device silhouette display (SVG-based with lead-lines)
- [x] Code refactoring: Split MainForm.cs into partial classes
  - MainForm.cs: Core form logic (2,800 lines)
  - MainForm.Mappings.cs: Mappings tab (4,699 lines)
  - MainForm.Devices.cs: Devices tab (850 lines)
  - MainForm.SCBindings.cs: SC Bindings tab (1,388 lines)
  - MainForm.Settings.cs: Settings tab (646 lines)
- [x] Input forwarding UI (Start/Stop Forwarding button in Status panel)
- [x] vJoy acquisition with Gremlin-style retry logic and PID checking
- [x] Device drag-to-reorder in device list
- [x] Device order controls vJoy slot assignment (1st device -> vJoy 1, etc.)
- [x] Fix button index off-by-one (vJoy buttons are 1-indexed)

## Phase 8: Star Citizen Integration
**Goal**: Export vJoy mappings as SC actionmaps.xml (export-only, never modify user's files)

See `docs/SC_BINDINGS_IMPLEMENTATION.md` for detailed implementation plan.

### Session 1: Foundation ✅ COMPLETE
- [x] Add NuGet packages (SharpZipLib, ZstdSharp)
- [x] Create `Models/SCInstallation.cs`
- [x] Create `Services/SCInstallationService.cs`
- [x] Test installation detection (--scdetect CLI)

### Session 2: P4K Extraction ✅ COMPLETE
- [x] Create `Services/P4kExtractorService.cs`
- [x] Create `Services/CryXmlService.cs` (CryXmlB binary parsing)
- [x] Create `Services/SCProfileCacheService.cs`
- [x] Test defaultProfile.xml extraction (--scextract CLI)

### Session 3: Schema & Export ✅ COMPLETE
- [x] Create `Models/SCAction.cs`, `SCExportProfile.cs`
- [x] Create `Services/SCSchemaService.cs` (change detection)
- [x] Create `Services/SCXmlExportService.cs`
- [x] Test export generation (--scschema, --scexport CLI)

### Session 4: UI ✅ COMPLETE
- [x] Update BINDINGS tab with real UI
- [x] Installation selector panel
- [x] Export configuration panel (profile name, vJoy-to-SC mapping)
- [x] Export button with status display
- [x] Multi-column grid (KB, Mouse, JS1-JSn)
- [x] Default bindings from p4k displayed
- [x] Category expand/collapse
- [x] Search functionality
- [x] KB/Mouse input listening (framework)

### Session 5: Input Detection & Binding ✅ COMPLETE
- [x] Fix joystick input detection - rewrote DetectJoystickInput() to poll InputService directly
- [x] Implement physical input → vJoy output lookup - uses MappingProfile.GetVJoyOutputForPhysicalInput() with 1:1 fallback
- [x] ASSIGN button click handler - verified working (opens manual assignment dialog)
- [x] Fix action name display - now uses SCCategoryMapper.FormatActionName()
- [x] Fix category naming - SCSchemaService now uses SCCategoryMapper.GetCategoryName()
- [x] Added InputService.GetDeviceState() method for on-demand state polling

### Session 6: Polish & Integration
- [ ] Fix binding count to show total vs filtered (low priority)
- [ ] Wire up settings persistence
- [ ] Add export success/error notifications
- [ ] Test full workflow end-to-end
- [ ] Handle edge cases (no SC installed, corrupt p4k, etc.)

Reference implementation: `C:\Users\mhams\source\repos\SCVirtStick\SCVirtStick\Core\`

## Phase 9: Polish and Release
**Goal**: Production-ready application

- [ ] Installer/setup
- [ ] Auto-start with Windows option
- [ ] System tray mode
- [ ] Device hot-plug handling
- [ ] Error handling and user feedback
- [ ] Documentation for end users
- [ ] Performance optimization
- [ ] Auto-download updates toggle (download in background when update detected, then prompt to apply)

---

## Current Focus

**Phase 7** - Configuration UI (FUI):
- Input forwarding is working (physical devices -> vJoy)
- Device order drag-to-reorder controls vJoy slot assignment
- Next: System tray icon (user mentioned needing this to avoid SCVirtStick conflicts)
- Next: Shift layer configuration UI

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
                    Phase 6 (Advanced Mappings) ✅
                          │
                          v
                    Phase 7 (Config UI) <-- CURRENT
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
