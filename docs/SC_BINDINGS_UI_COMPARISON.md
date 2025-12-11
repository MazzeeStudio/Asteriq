# SC Bindings UI Comparison: SCVirtStick vs Asteriq

This document tracks the feature gap between SCVirtStick's mature SC Bindings UI and Asteriq's current implementation. Use this as a roadmap for completing the SC Bindings tab.

## Status Legend
- ✅ Complete in Asteriq
- 🔶 Partial implementation
- ❌ Not implemented
- ⏭️ Deferred/Low priority

---

## CRITICAL CONCEPT: Default Bindings from P4K

**The action list comes from SC's defaultProfile.xml extracted from Data.p4k**, NOT from saved profiles.

### How SCVirtStick Works:
1. **Extract defaultProfile.xml** from Data.p4k (we have this: `P4kExtractorService`)
2. **Parse all actions** with their default KB/Mouse/JS bindings (we have this: `SCSchemaService`)
3. **Display in multi-column grid**:
   - Column 0: Action name
   - Column 1: Keyboard default binding (from p4k)
   - Column 2: Mouse default binding (from p4k)
   - Column 3+: Joystick bindings (user-configured OR from p4k defaults)
4. **User bindings overlay** the defaults - shown in same columns

### What Asteriq Currently Does:
1. ✅ Extracts defaultProfile.xml from p4k
2. ✅ Parses actions with `SCSchemaService.ParseActions()`
3. ✅ Stores default bindings in `SCAction.DefaultBindings` list
4. ✅ Multi-column grid: ACTION + KB + Mouse + JS1 + JS2...
5. ✅ Displays KB/Mouse defaults from p4k in respective columns
6. ✅ Shows separate columns per vJoy device (dynamic)
7. ✅ User-friendly category names via `SCCategoryMapper`
8. ✅ Categories sorted in logical order (Flight Control first, etc.)

### Remaining Gap:
- **Joystick binding detection**: Click-to-bind for JS columns not working correctly (physical input → vJoy mapping lookup issue)
- **KB/Mouse editing**: Framework exists but needs testing

---

## VISUAL REFERENCE (from SCVirtStick screenshot)

### Column Layout:
```
| Action                          | KB | Mouse | L-Virpil Stick | R-Virpil Stick | Virpil Throttle |
|---------------------------------|----|-------|----------------|----------------|-----------------|
| Ifcs Toggle Gforce Safety       | —  | —     |                | Btn6           | —               |
| Ifcs Vector Decoupling Toggle   | —  | —     | Btn31          | —              | —               |
|   Mining Use Consumable 1       | —  | —     | Btn24          | —              | —               |
| ▼ Weapons                       |    |       |                |                |                 |
|   Weapon Aim Type Cycle         | —  | —     | Ctrl | Btn12   | —              | —               |
```

### Key Visual Features Observed:
1. **Columns**: Action + KB + Mouse + [Device columns]
2. **Device names**: SCVirtStick uses actual names ("L-Virpil Stick")
   - **Asteriq approach**: Use JS1/JS2/JS3 (vJoy device IDs) since we map to vJoy, not physical devices
3. **Keycap badges**: Bindings shown as styled button badges (`Btn24`, `Sl1`)
4. **Modifier display**: Shown as separate badge before main key (`Ctrl` `Btn12`)
5. **Empty cells**: Show "—" dash, not blank
6. **Double-tap**: Shown as `2xctrl+Btn 26` format
7. **Categories**: Bold headers with expand/collapse arrows
8. **Indentation**: Actions indented under category headers
9. **Alternating rows**: Subtle color alternation for readability
10. **Header bar**: Environment dropdown, Profile dropdown, +New, Clear All, Reset buttons
11. **Filter bar**: "Bound only" checkbox + search box (top right)

### Asteriq Design Notes:
- **Maintain FUI styling/motif** - dark futuristic theme, not SCVirtStick's exact look
- **Use JS1/JS2/JS3 for device columns** - we're mapping vJoy outputs to SC, not physical devices
- **Keycap badges should follow FUI colors** - use our theme colors, corner accents, etc.

### Technical Note: KB/Mouse Input Detection
- **No SDL2 or DirectInput needed** for keyboard/mouse
- Use **Windows API directly** (`user32.dll`):
  - `GetAsyncKeyState(int vKey)` - for keyboard keys and mouse buttons
  - `GetCursorPos` - for mouse position (if needed for axis-like mouse input)
- SCVirtStick uses this approach in `UnifiedInputManager.cs`
- We already have `KeyboardService.cs` using these APIs for output; similar code for input detection

### Technical Note: Physical Input → vJoy Output Lookup
**Critical for SC Bindings:** User presses physical joystick, we need to know the vJoy output to bind.

**Flow:**
1. User presses physical button/axis on their joystick
2. Look up the mapping: Physical Input → vJoy Output
3. Format as SC binding string (e.g., `js1_button5`, `js2_x`)

**Current Implementation Status:**
- ✅ `ActiveInputTracker` detects button/axis activity
- ✅ `DetectJoystickInput()` looks up mapping by device GUID and input index
- ❌ **NOT WORKING**: Detection not finding mappings correctly - needs debugging

**Files:**
- `Models/Mappings.cs` - `GetVJoyOutputForPhysicalInput()` and `FormatAsSCBinding()`
- `Services/SCCategoryMapper.cs` - Category name mapping and sorting

---

## 1. ACTION LIST DISPLAY

### Action Name Formatting
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Text truncation with ellipsis | `StringTrimming.EllipsisCharacter` | Binary search truncation with "..." | ✅ |
| "○" prefix for actions not in user profile | Yes | No | ❌ |
| Type indicators ("⟷" axis, "✛" hat) | Yes, at end of action name | Icons in badges | ✅ |
| Indentation by hierarchy level | 0/16/32px based on level | 18px indent under category | 🔶 |
| Dynamic action column width | Min 180px, max 320px, calculated | 280px base, dynamic max 45% | ✅ |

### Grid Columns
| Feature | SCVirtStick | Asteriq | Asteriq Target | Status |
|---------|-------------|---------|----------------|--------|
| Multiple device columns | Yes: Action + KB + Mouse + devices | Action + KB + Mouse + JS1-JSn | Action + KB + Mouse + JS1-JSn | ✅ |
| KB column | Shows/edits keyboard bindings | Shows defaults, click to edit | Editable | ✅ |
| Mouse column | Shows/edits mouse bindings | Shows defaults, click to edit | Editable | ✅ |
| Joystick columns | Per physical device | Per vJoy device (JS1, JS2...) | Per vJoy device | ✅ |
| Device column headers | Actual device names | "KB", "Mouse", "JS1", "JS2"... | As designed | ✅ |
| Column width (devices) | Fixed 120px each | 90px each | ~100px, FUI styled | ✅ |
| Horizontal scrolling for devices | Yes, custom scrollbar | Yes, auto horizontal scrollbar | Yes | ✅ |
| Default bindings from p4k | Pre-populated in cells | Pre-populated from SCAction.DefaultBindings | As designed | ✅ |

### Binding Display in Cells
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Empty cell indicator | "—" dash in dim color | "—" dash in muted color | ✅ |
| Keycap-style visualization | Styled button badges (`Btn24`, `Sl1`) | Rounded badge with color border | ✅ |
| Modifier keys display | Separate badge before main key (`Ctrl` `Btn12`) | Combined format (`SHFT+Btn1`) | ✅ |
| Double-tap indicator | `2xctrl+Btn 26` format | Not supported | ❌ |
| Axis inputs | Shown as `Sl1`, `X`, `Y` badges | `Sl1`, `X`, `MX`, `MY` badges | ✅ |
| Fallback to compact text | When keycaps too wide | Truncation with ellipsis | ✅ |
| "Press..." listening state | Animated progress bar | Pulsing animation | ✅ |

### Row Styling
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Alternating row colors | Odd rows have subtle tint | Yes, even rows tinted | ✅ |
| Category row background | Distinct BgBase3 | Basic styling | 🔶 |
| Selection highlighting | 40-opacity active color | Yes | ✅ |
| Hover state | FUITheme.Hover background | Yes | ✅ |
| Listening state background | 80-opacity warning color | Pulsing animation | ✅ |

---

## 2. CATEGORY/ACTIONMAP ORGANIZATION

### Category Structure
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| User-friendly category names | Yes (via CategoryMapper) | Yes (via SCCategoryMapper) | ✅ |
| Category sort order | Flight → Weapons → Targeting... | Same order | ✅ |
| Three-level hierarchy | Category → SubCategory → Action | Category → Action | 🔶 |
| Subcategory support | Full support | Not implemented | ❌ |

### Collapsible Sections
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Expand/collapse categories | Yes, persisted | Yes | ✅ |
| Expand/collapse subcategories | Yes, persisted | N/A (no subcategories) | ❌ |
| Expansion indicators | "▼" / "▶" arrows | Triangle indicators | ✅ |
| Show all when searching | Ignores collapse state | Yes | ✅ |

### Category Headers
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Bound/total count display | Yes | Yes | ✅ |
| Distinct font for categories | FontHeader vs FontNormal | Same font | ❌ |
| Category vs subcategory colors | Accent2 vs TextSecondary | Single color | ❌ |
| Variable row heights | 32px category, 26px subcategory, 28px action | Fixed 24px | ❌ |

---

## 3. BINDING ASSIGNMENT UI

### Input Listening/Capture
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Start listening on click | Yes | Yes (single click) | ✅ |
| Listening timeout | 5 seconds default | 5 seconds | ✅ |
| Visual feedback | Warning color background | Pulsing animation | ✅ |
| Cancel listening (Escape) | Yes | Yes | ✅ |

### Input Detection
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Keyboard detection | Yes (GetAsyncKeyState) | Yes (GetAsyncKeyState) | ✅ |
| Mouse detection | Yes | Yes | ✅ |
| Joystick detection | Yes | **NOT WORKING** | ❌ |
| Axis merge dialog | AxisMergeDialog for conflicts | No merge support | ❌ |

### Modifier Key Support
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Multiple modifiers | List of modifiers | Supported | ✅ |
| Modifier keycap display | Separate badges | Combined format | 🔶 |
| Double-tap support | DoubleTap boolean | Not supported | ❌ |

---

## 4. VISUAL FEEDBACK

### Conflict Indicators
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Conflict detection | HashSet of conflicts | Yes | ✅ |
| Conflict background color | 40-opacity warning | Red tint | ✅ |
| Conflict text color | Warning color | Red (Danger) | ✅ |
| Conflict indicator | Red border | Warning triangle | ✅ |

### Selection Indicators
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Selected cell border | Blue border (80-opacity) | Active color border | ✅ |
| Hover highlight | Yes | Yes | ✅ |

---

## 5. FILTERING/SEARCH

### Search Functionality
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Search by action name | Yes | Yes | ✅ |
| Search by display name | Yes | Yes | ✅ |
| Search by category | Yes | Yes | ✅ |
| Search binding values | Yes (input names, modifiers) | Yes | ✅ |
| Case-insensitive | Yes | Yes | ✅ |

### Filter Options
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Show bound only | Yes | Yes | ✅ |
| Category filter dropdown | Yes | Yes (user-friendly names) | ✅ |
| Filter by SC version | LIVE/PTU/EPTU dropdown | No | ❌ |

---

## 6. PROFILE MANAGEMENT

### Profile Operations
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Save profile | Auto-save on change | Manual save button | ✅ |
| Create new profile | Dialog with name prompt | Yes | ✅ |
| Delete profile | With confirmation | Yes | ✅ |
| Load profile | From dropdown | Yes | ✅ |
| Export to SC folder | SaveFileDialog to Mappings | Export button | ✅ |

---

## IMPLEMENTATION PRIORITY

### Completed (Phase 1-3)
- ✅ Multi-column grid layout (KB + Mouse + JS columns)
- ✅ Default bindings display from p4k
- ✅ Keycap-style badges
- ✅ Modifier display
- ✅ Alternating row colors
- ✅ Category expand/collapse
- ✅ User-friendly category names (SCCategoryMapper)
- ✅ Category sort order matching SCVirtStick
- ✅ Search (action name, category, bindings)
- ✅ Input listening mode (KB/Mouse working)
- ✅ Type indicators (axis/button/hat icons)
- ✅ Conflict highlighting

### BLOCKING ISSUES - FIXED (2025-12-11)

#### 1. Joystick Input Detection - CRITICAL ✅ FIXED
- **Problem**: Click JS cell, press physical button, binding not detected
- **Solution**: Rewrote `DetectJoystickInput()` to poll `InputService.GetDeviceState()` directly
- **Added**: State tracking to detect button press transitions
- **Added**: 1:1 fallback mapping for devices assigned to vJoy without explicit mappings
- **Added**: Axis baseline tracking - captures axis positions at start of listening
- **Added**: Maximum deflection tracking - registers axis when >70% deflection OR when user releases
- **Files**: MainForm.SCBindings.cs, Services/InputService.cs (added GetDeviceState method)

#### 2. ASSIGN Button Handler - HIGH ✅ VERIFIED WORKING
- **Status**: Already implemented - opens manual assignment dialog
- **Method**: `AssignSCBinding()` at line 2667
- **Note**: `_scAssigningInput` state variable was unused (cosmetic issue only)

#### 3. Action Name Display - MEDIUM ✅ FIXED
- **Problem**: Raw action names shown (e.g., `v_strafe_forward`)
- **Solution**: Now uses `SCCategoryMapper.FormatActionName()` for display
- **Files**: MainForm.SCBindings.cs:938

#### 4. Category Name Mismatch - MEDIUM ✅ FIXED
- **Problem**: SCSchemaService.DeriveCategory() conflicted with SCCategoryMapper
- **Solution**: Removed DeriveCategory(), now uses SCCategoryMapper.GetCategoryName() directly
- **Files**: SCSchemaService.cs:36

#### 5. Binding Count Bug - LOW ❌
- **Problem**: Shows filtered count, not total
- **Example**: "150 actions" when filtered, user thinks that's total
- **Should show**: "150 of 3000 actions" or similar
- **Files**: MainForm.SCBindings.cs:730-731
- **Status**: Still pending (low priority)

#### 6. Axis Export Bug - CRITICAL ✅ FIXED (2025-12-11)
- **Problem**: Axis bindings were being exported as buttons
- **Root cause**: Input type inference in `AssignJoystickBinding()` wasn't comprehensive
- **Solution**: Added `InferInputTypeFromName()` method that handles all input formats:
  - Standard axes: x, y, z, rx, ry, rz
  - Sliders: slider1, slider2, slider*
  - Fallback axes: axis0, axis1, etc.
  - Hats: hat1_up, hat2_right, etc.
- **Files**: MainForm.SCBindings.cs:2742-2764

#### 7. Clear All / Reset Defaults - HIGH ✅ IMPLEMENTED (2025-12-11)
- **Feature**: Added Clear All Bindings and Reset to Defaults buttons
- **Location**: Right panel, above Export button
- **Clear All**: Removes all user joystick bindings from profile
- **Reset Defaults**: Clears bindings and reloads schema from p4k cache
- **Files**: MainForm.SCBindings.cs, MainForm.cs

#### 8. VTOL Actions Missing from Filter - CRITICAL ✅ FIXED (2025-12-11)
- **Problem**: User couldn't find VTOL bindings when searching
- **Root cause**: `FilterJoystickActions()` was excluding actions without default joystick bindings
- **Example**: `v_vtol_toggle` has `keyboard="k"` but `joystick=" "` (empty) - was filtered out
- **Solution**: Changed `FilterJoystickActions()` to return ALL actions since users should be able to bind any action to their joystick
- **Files**: SCSchemaService.cs:333-339, SCSchemaServiceTests.cs
- **Available actions now visible**: v_toggle_landing_system, v_deploy_landing_system, v_retract_landing_system, v_vtol_toggle, v_vtol_on, v_vtol_off

### Future Work
- ❌ Double-tap binding support
- ❌ Subcategory support (3-level hierarchy)
- ❌ Import existing SC bindings from actionmaps.xml
- ❌ Tooltips for truncated text
- ❌ Variable row heights

---

## REFERENCE FILES

### SCVirtStick Key Files
- `BindingMatrixControl.cs` - Main grid control (2400+ lines)
- `BindingCentricMainForm.cs` - Main form with profile management
- `CategoryMapper.cs` - Category name mapping and sorting
- `FUISearchBox.cs` - Styled search control

### Asteriq Current Files
- `MainForm.SCBindings.cs` - SC Bindings tab (~2700 lines)
- `MainForm.cs` - State variables for SC Bindings
- `Services/SCCategoryMapper.cs` - Category mapping (new)
- `SCExportProfileService.cs` - Profile persistence
- `SCExportProfile.cs` - Profile model

---

## NOTES

- SCVirtStick uses a sophisticated matrix-based grid with per-device columns
- Asteriq uses JS1/JS2/JS3 columns for vJoy devices (not physical device names)
- The keycap visualization follows FUI theme colors
- Real-time input listening works for KB/Mouse but not joysticks
- Category mapper provides same logical grouping as SCVirtStick

Last Updated: 2025-12-11
