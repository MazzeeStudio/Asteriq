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
4. ❌ Only shows action name + single "user binding" column
5. ❌ Does NOT display KB/Mouse defaults from p4k
6. ❌ Does NOT show separate columns per device

### The Gap:
We have the data (`SCDefaultBinding` with `DevicePrefix`, `Input`, `Modifiers`) but we're not displaying it in the UI. The UI should show:
- What the SC defaults are (KB/Mouse) - read-only info
- What joystick bindings the user has configured

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
1. **Columns**: Action + KB + Mouse + [Device names from connected hardware]
2. **Device names**: Uses ACTUAL device names ("L-Virpil Stick") not generic "JS1"
3. **Keycap badges**: Bindings shown as styled button badges (`Btn24`, `Sl1`)
4. **Modifier display**: Shown as separate badge before main key (`Ctrl` `Btn12`)
5. **Empty cells**: Show "—" dash, not blank
6. **Double-tap**: Shown as `2xctrl+Btn 26` format
7. **Categories**: Bold headers with expand/collapse arrows
8. **Indentation**: Actions indented under category headers
9. **Alternating rows**: Subtle color alternation for readability
10. **Header bar**: Environment dropdown, Profile dropdown, +New, Clear All, Reset buttons
11. **Filter bar**: "Bound only" checkbox + search box (top right)

---

## 1. ACTION LIST DISPLAY

### Action Name Formatting
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Text truncation with ellipsis | `StringTrimming.EllipsisCharacter` | Basic substring truncation | 🔶 |
| "○" prefix for actions not in user profile | Yes | No | ❌ |
| Type indicators ("⟷" axis, "✛" hat) | Yes, at end of action name | No | ❌ |
| Indentation by hierarchy level | 0/16/32px based on level | No indentation | ❌ |
| Dynamic action column width | Min 180px, max 320px, calculated | Fixed width | ❌ |

### Grid Columns
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Multiple device columns | Yes: Action + KB + Mouse + [connected devices] | Single binding display | ❌ |
| KB column for keyboard defaults | Yes, shows SC defaults from p4k | No | ❌ |
| Mouse column for mouse defaults | Yes, shows SC defaults from p4k | No | ❌ |
| Per-device joystick columns | Yes, uses actual device names ("L-Virpil Stick") | No | ❌ |
| Device column headers | Actual device names, not "JS1/JS2" | N/A | ❌ |
| Column width (devices) | Fixed 120px each | N/A | ❌ |
| Horizontal scrolling for devices | Yes, custom scrollbar | No | ❌ |

### Binding Display in Cells
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Empty cell indicator | "—" dash in dim color | No indicator | ❌ |
| Keycap-style visualization | Styled button badges (`Btn24`, `Sl1`) | Plain text | ❌ |
| Modifier keys display | Separate badge before main key (`Ctrl` `Btn12`) | Not shown | ❌ |
| Double-tap indicator | `2xctrl+Btn 26` format | Not supported | ❌ |
| Axis inputs | Shown as `Sl1`, `X`, `Y` badges | Plain text | ❌ |
| Fallback to compact text | When keycaps too wide | N/A | ❌ |
| "Press..." listening state | Animated progress bar | Static text | 🔶 |

### Row Styling
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Alternating row colors | Odd rows have subtle tint | No | ❌ |
| Category row background | Distinct BgBase3 | Basic styling | 🔶 |
| Selection highlighting | 40-opacity active color | Basic highlight | 🔶 |
| Hover state | FUITheme.Hover background | Basic hover | 🔶 |
| Listening state background | 80-opacity warning color | No distinct color | ❌ |

---

## 2. CATEGORY/ACTIONMAP ORGANIZATION

### Category Structure
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Three-level hierarchy | Category → SubCategory → Action | Category → Action | 🔶 |
| MatrixRowType distinction | Category, SubCategory, Action types | Category headers only | 🔶 |
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
| Start listening on double-click | Yes | Dialog-based assignment | ❌ |
| Listening timeout | 5 seconds default, configurable | No timeout | ❌ |
| Animated progress bar | 20 FPS animation | No animation | ❌ |
| Visual cell state change | Warning color background | No change | ❌ |
| Cancel listening (Escape) | Yes | N/A | ❌ |

### Input Detection
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Real-time input capture | Yes, from InputService | Dialog with combo box | ❌ |
| Axis merge dialog | AxisMergeDialog for conflicts | No merge support | ❌ |
| Auto-stop after binding | Yes | N/A | ❌ |

### Modifier Key Support
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Multiple modifiers | List of modifiers | Not supported | ❌ |
| Modifier keycap display | Separate badges | N/A | ❌ |
| Double-tap support | DoubleTap boolean | Not supported | ❌ |

### Multi-Device Support
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Per-device bindings | DeviceBindings dictionary | Single vJoy binding | 🔶 |
| KB/Mouse bindings | Full support | Not supported | ❌ |
| Multiple joystick bindings | Yes | vJoy only | 🔶 |

---

## 4. VISUAL FEEDBACK

### Tooltips
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Action name tooltip | When truncated, 500ms delay | No tooltips | ❌ |
| Tooltip positioning | 15px offset from cursor | N/A | ❌ |
| Auto-hide timeout | 5000ms | N/A | ❌ |

### Hover States
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Cell hover highlight | 40-opacity active | Basic highlight | 🔶 |
| Column hover highlight | 30-opacity for device column | No | ❌ |
| Scrollbar hover | Color change to Accent2 | No custom scrollbar | ❌ |
| Cursor changes | Hand over clickable | No cursor change | ❌ |

### Conflict Indicators
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Conflict cell tracking | HashSet of conflicts | Detection only | 🔶 |
| Conflict background color | 40-opacity warning | No visual | ❌ |
| Conflict text color | Warning color | No change | ❌ |
| Conflict cell border | Red border (100-opacity) | No | ❌ |
| Conflict cache updates | Auto-rebuild on changes | No cache | ❌ |

### Merge Indicators
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Merge indicator badge | 12px green circle with "m" | Not supported | ❌ |
| Merge border | Green border | N/A | ❌ |

### Selection Indicators
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Selected cell border | Blue border (80-opacity) | Basic highlight | 🔶 |
| Selection persistence | Maintains across operations | Basic | 🔶 |

---

## 5. FILTERING/SEARCH

### Search Functionality
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Search by action name | Yes | Yes | ✅ |
| Search by category | Yes | Yes | ✅ |
| Search by subcategory | Yes | N/A | ❌ |
| Search binding values | Yes (input names, modifiers) | No | ❌ |
| Case-insensitive | Yes | Yes | ✅ |

### Filter Options
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Show bound only | Yes | Yes | ✅ |
| Filter by SC version | LIVE/PTU/EPTU dropdown | No | ❌ |
| Version availability check | PresentInVersions list | No version tracking | ❌ |
| Combined AND filtering | Search + bound + version | Search + bound only | 🔶 |

---

## 6. PROFILE MANAGEMENT

### Profile Selection
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Profile dropdown | Full list from SC folder | Asteriq profiles only | 🔶 |
| Active Profile option | "Active Profile (actionmaps.xml)" | No SC profile loading | ❌ |
| SC running detection | Warns about auto-save | No detection | ❌ |

### Profile Operations
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Save profile | Auto-save on change | Manual save button | ✅ |
| Create new profile | Dialog with name prompt | Yes | ✅ |
| Delete profile | With confirmation | Yes | ✅ |
| Load profile | From dropdown | Yes | ✅ |

### Import/Export
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Export to SC folder | SaveFileDialog to Mappings | Export button | ✅ |
| Import SC profile | SCProfileImporter | Not supported | ❌ |
| Import existing bindings | From actionmaps.xml | Not supported | ❌ |

### Advanced Features
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Clear all bindings | With confirmation dialog | Not implemented | ❌ |
| Reset to SC defaults | Extract from Data.p4k | Not implemented | ❌ |
| Binding count display | In confirmation dialogs | No | ❌ |

---

## 7. INTERACTION MODEL

### Click Behavior
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Single-click to select | Yes, visual feedback | Row selection | 🔶 |
| Double-click to bind | Enters listening mode | Opens dialog | ❌ |
| Click selected to unbind | Delayed unbind check | Clear button | 🔶 |
| Right-click to unbind | Immediate unbind | Not implemented | ❌ |

### Status Feedback
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Status bar binding display | Shows current selection | Status messages | 🔶 |
| Progress indicators | Listening animation | None | ❌ |

---

## 8. SCROLLING & NAVIGATION

### Scrollbars
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Vertical scrollbar | Custom FUI styled | Basic scroll | 🔶 |
| Horizontal scrollbar | For device columns | N/A (single column) | ❌ |
| Scrollbar hover effects | Color changes | No | ❌ |
| Mouse wheel support | Yes | Yes | ✅ |

### Keyboard Navigation
| Feature | SCVirtStick | Asteriq | Status |
|---------|-------------|---------|--------|
| Arrow key navigation | Implied | Not implemented | ❌ |
| Enter to bind | Implied | Not implemented | ❌ |
| Escape to cancel | Yes | Unfocus search only | 🔶 |

---

## IMPLEMENTATION PRIORITY

### Phase 1: Multi-Column Grid Layout (CRITICAL)
This is the foundation - without this, the UI is fundamentally different from SCVirtStick.

1. ❌ **Redesign grid to multi-column layout**:
   - Column 0: Action name (wide, ~200px)
   - Column 1: KB (keyboard defaults from p4k)
   - Column 2: Mouse (mouse defaults from p4k)
   - Column 3+: Connected joystick devices (using actual device names)
2. ❌ Display default bindings from `SCAction.DefaultBindings` in KB/Mouse columns
3. ❌ Get connected device names from InputService for JS column headers
4. ❌ Horizontal scrolling when many devices connected
5. ❌ Empty cell "—" indicator

### Phase 2: Keycap Badge Rendering (High Priority)
The visual treatment of bindings is a key UX element.

1. ❌ **Keycap-style badges** for binding display (`Btn24`, `Sl1`, `X`)
2. ❌ **Modifier badges** shown separately before main key (`Ctrl` `Btn12`)
3. ❌ **Double-tap indicator** (`2x` prefix)
4. ❌ **Axis badge styling** (different color/style for axes vs buttons)
5. ❌ Proper badge sizing and spacing within cells

### Phase 3: Row & Category Styling (Medium Priority)
1. ❌ Alternating row colors for readability
2. ❌ Action indentation under categories (16px indent)
3. ❌ Category header distinct styling (bold, different background)
4. ❌ Type indicators after action name ("⟷" axis, "✛" hat)
5. ❌ Variable row heights (28px actions, 32px categories)

### Phase 4: Binding Interaction (Medium Priority)
1. ❌ Double-click cell to enter binding mode
2. ❌ Real-time input listening (not dialog-based)
3. ❌ Listening timeout with visual progress
4. ❌ Click selected cell to unbind
5. ❌ Conflict cell highlighting (red background/border)

### Phase 5: Polish & Advanced (Lower Priority)
1. ❌ Tooltips for truncated action names
2. ❌ Column hover highlighting
3. ❌ Subcategory support (3-level hierarchy)
4. ❌ Import existing SC bindings from actionmaps.xml
5. ❌ Reset to SC defaults button
6. ❌ SC version/environment filtering (LIVE/PTU)
7. ❌ Search binding values (not just action names)

---

## REFERENCE FILES

### SCVirtStick Key Files
- `BindingMatrixControl.cs` - Main grid control (2400+ lines)
- `BindingCentricMainForm.cs` - Main form with profile management
- `BindingConflictDialog.cs` - Conflict resolution UI
- `FUISearchBox.cs` - Styled search control
- `FUITheme.cs` - Theme system

### Asteriq Current Files
- `MainForm.SCBindings.cs` - SC Bindings tab (~2100 lines)
- `MainForm.cs` - State variables for SC Bindings
- `SCExportProfileService.cs` - Profile persistence
- `SCExportProfile.cs` - Profile model

---

## NOTES

- SCVirtStick uses a sophisticated matrix-based grid with per-device columns
- Asteriq currently uses a simpler single-binding-per-action model
- The keycap visualization in SCVirtStick is a key UX differentiator
- Real-time input listening vs dialog-based is a major workflow difference
- Multi-device support (KB/Mouse/multiple joysticks) is core to SCVirtStick

Last Updated: 2024-12-11
