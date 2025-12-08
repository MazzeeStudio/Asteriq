# Lessons Learned from SCVirtStick

## What Worked Well

### 1. FUI Theming System
The `FUITheme` static class with multiple color schemes worked excellently:
- Centralized color management
- Easy to add new themes
- Consistent look across all controls

**Keep**: Theme architecture, color scheme system

### 2. Device Control Maps (JSON)
Mapping physical controls to SVG elements via JSON was flexible:
- Easy to add new devices without code changes
- Clear separation of data and logic
- Supports multiple bindings per control

**Keep**: JSON-based control maps

### 3. SVG Device Silhouettes
Using SVG for device visualization:
- Scalable at any resolution
- Can highlight individual control groups
- Professional appearance

**Keep**: SVG approach, stroke-based highlighting

### 4. Binding Matrix Grid
Custom grid control for showing bindings:
- Clear at-a-glance view of all bindings
- Category grouping
- Filtering (bound only, search)

**Keep**: Matrix concept, filtering

### 5. P4K Reader
Reading game defaults from Data.p4k:
- Zip-based reading worked reliably
- Caching improved performance

**Keep**: P4K reading approach

## What Didn't Work Well

### 1. DirectInput/Raw HID for Input
Using .NET's joystick input was problematic:
- Timing issues, missed inputs
- Device enumeration unreliable
- Hot-plug handling fragile

**Replace with**: SDL2 via SDL2-CS

### 2. Loose vJoy Slot Assignment
No strict physical device → vJoy slot binding:
- Users confused about which device was which
- Device order could change between sessions

**Replace with**: Explicit slot assignment in config, user confirms mapping

### 3. Live Actionmaps Mutation
Modifying user's actionmaps.xml directly:
- Risk of data loss
- Complex merge logic
- User trust issues

**Replace with**: Read-only + export approach

### 4. Complex SVG Fill Modification
Trying to change SVG fill colors for highlighting:
- Inline styles override programmatic changes
- Opacity/alpha didn't work as expected
- Stroke-only ended up being simpler and effective

**Keep**: Stroke-only highlighting

### 5. Too Many Features Too Fast
Added features before core was stable:
- Device settings panel
- Binding editing
- Multiple views

**Better approach**: Nail input reliability first, then add features

## Architecture Insights

### Threading
- Input polling must be on dedicated thread
- UI updates via Invoke/BeginInvoke
- Be careful with shared state

### Configuration
- Single JSON config file is cleaner than scattered settings
- Version the config format
- Provide migration path for config changes

### Error Handling
- HidHide/vJoy not installed: Clear guidance needed
- Device disconnect: Graceful degradation
- SC not installed: Partial functionality still useful

## UI/UX Insights

### Device Panel
- Show device image with overlaid controls
- Highlight on button press is valuable feedback
- Keep info panel separate from interaction

### Binding View
- Matrix view most useful for overview
- Filter to "bound only" reduces noise
- Search is essential for large binding lists

### Status Feedback
- Show connected/disconnected state clearly
- Show active inputs in real-time
- Log panel for debugging helpful during development

## Code to Salvage

### Reusable As-Is
- `FUITheme.cs` - Theming system
- `Models/DeviceControlMap.cs` - Control map models
- `vJoyWrapper/` - vJoy P/Invoke bindings

### Reusable with Cleanup
- `Services/StarCitizenService.cs` - Path detection, P4K reading
- `UI/FUI/BindingMatrixControl.cs` - Grid concepts
- JSON control map files

### Do Not Reuse
- Input handling code (replace with SDL2)
- ActionMaps mutation logic
- Complex SVG fill manipulation

## Performance Notes

- SVG rendering: Cache rendered bitmaps, only re-render on highlight change
- P4K reading: Cache parsed data, invalidate on file change
- Config loading: Load once at startup, save on explicit action

## Testing Priorities

For new project, test these first:
1. SDL2 input - all axes and buttons read correctly
2. vJoy output - values arrive at virtual device
3. HidHide - physical device hidden from other apps
4. Round-trip - physical input → virtual output with <10ms latency
