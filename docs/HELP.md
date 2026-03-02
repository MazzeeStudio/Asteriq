# Asteriq User Guide

> Asteriq is a unified HOTAS management application for Star Citizen. It replaces the need for separate tools like JoystickGremlin, the vJoy configuration GUI, and HidHide's own interface — everything is managed from one place.

---

## Table of Contents

- [Getting Started](#getting-started)
  - [System Requirements](#system-requirements)
  - [Installation](#installation)
  - [Driver Setup](#driver-setup)
  - [First Launch](#first-launch)
- [Devices Tab](#devices-tab)
  - [Physical Devices](#physical-devices)
  - [Virtual Devices (vJoy)](#virtual-devices-vjoy)
  - [Device Ordering](#device-ordering)
  - [Device Silhouettes](#device-silhouettes)
  - [Hiding Devices (HidHide)](#hiding-devices-hidhide)
  - [Live Input Monitor](#live-input-monitor)
- [Mappings Tab](#mappings-tab)
  - [Creating a Mapping](#creating-a-mapping)
  - [Axis Mappings](#axis-mappings)
  - [Button Mappings](#button-mappings)
  - [Hat / POV Mappings](#hat--pov-mappings)
  - [Axis-to-Button Mappings](#axis-to-button-mappings)
  - [Button-to-Axis Mappings](#button-to-axis-mappings)
  - [Curve Editor](#curve-editor)
  - [Mapping List](#mapping-list)
  - [TX Toggle Assignment](#tx-toggle-assignment)
- [SC Bindings Tab](#sc-bindings-tab)
  - [SC Installation Detection](#sc-installation-detection)
  - [Binding Grid](#binding-grid)
  - [Assigning Bindings](#assigning-bindings)
  - [Column Actions](#column-actions)
  - [Import From Profile](#import-from-profile)
  - [Exporting to Star Citizen](#exporting-to-star-citizen)
  - [Conflict Detection](#conflict-detection)
- [Settings Tab](#settings-tab)
  - [Profile Management](#profile-management)
  - [System Settings](#system-settings)
  - [Visual Settings](#visual-settings)
  - [Network Forwarding](#network-forwarding)
  - [Updates](#updates)
- [Network Forwarding](#network-forwarding-guide)
  - [Overview](#network-overview)
  - [TX (Master) Setup](#tx-master-setup)
  - [RX (Client) Setup](#rx-client-setup)
  - [Connecting Machines](#connecting-machines)
  - [TX Toggle Button](#tx-toggle-button)
  - [Troubleshooting Network](#troubleshooting-network)
- [Profiles](#profiles)
  - [What a Profile Contains](#what-a-profile-contains)
  - [Creating Profiles](#creating-profiles)
  - [Sharing Profiles](#sharing-profiles)
- [Auto-Update](#auto-update)
- [Command Line Reference](#command-line-reference)
- [Data & File Locations](#data--file-locations)
- [Troubleshooting](#troubleshooting)

---

## Getting Started

### System Requirements

| Requirement | Details |
|---|---|
| OS | Windows 10 or 11 (64-bit) |
| Runtime | None — Asteriq is self-contained |
| Drivers | vJoy 2.x *(optional but recommended)* |
|  | HidHide *(optional, needed to hide physical devices from SC)* |

vJoy and HidHide are the same drivers you may already have from JoystickGremlin setups. Asteriq replaces the GUI tools, not the drivers themselves.

### Installation

1. Download `Asteriq.zip` from [the latest release](https://github.com/MazzeeStudio/Asteriq/releases/latest/download/Asteriq.zip).
2. Extract the ZIP to any folder you choose (e.g. `C:\Asteriq`).
3. Run `Asteriq.exe`.

There is no installer — just extract and run. To uninstall, delete the folder.

### Driver Setup

On first launch, Asteriq checks whether vJoy and HidHide are installed. If either is missing, a setup dialog appears:

<!-- SCREENSHOT: Driver Setup dialog showing vJoy and HidHide status -->

- **vJoy** is required for virtual device output. Without it, Asteriq can still be used for SC binding visualization, but mapping and forwarding will be unavailable.
- **HidHide** is optional. It hides your physical devices from Star Citizen so only the vJoy virtual devices are visible — preventing double-input problems.

You can re-open the driver setup dialog at any time from **Settings > DRIVER SETUP**.

### First Launch

After driver setup, Asteriq opens with four tabs across the top:

| Tab | Purpose |
|---|---|
| **Devices** | See your connected hardware and vJoy assignments |
| **Mappings** | Create axis curves, button maps, and output configurations |
| **SC Bindings** | Manage Star Citizen keybind XML files |
| **Settings** | Profiles, themes, network, updates |

A profile dropdown in the title bar lets you switch between saved configurations. If no profile exists, a default one is created automatically.

---

## Devices Tab

The Devices tab shows all connected hardware and how it maps to vJoy virtual devices.

<!-- SCREENSHOT: Devices tab with physical device list, vJoy assignments, and silhouette -->

### Physical Devices

The left panel lists every physical joystick, throttle, gamepad, and input device detected via SDL2. Each entry shows:

- Device name and manufacturer
- VID:PID hardware identifier
- Connection status (green dot = active, dim = idle)

Press any button or move any axis on your hardware — the corresponding device entry will pulse briefly to confirm detection.

### Virtual Devices (vJoy)

When vJoy is installed, the right panel shows which vJoy slots are in use:

- **vJoy 1** through **vJoy 16** — one slot per physical device
- The assignment follows the order of devices in the left panel: first device → vJoy 1, second → vJoy 2, and so on

### Device Ordering

You can drag physical devices up and down in the list to change their vJoy slot assignment. This order is saved in your active profile, so swapping USB ports won't change your mapping — Asteriq tracks devices by hardware identity, not port order.

### Device Silhouettes

When a device has a matching silhouette map, a visual representation appears showing the physical layout of buttons, axes, hats, and switches. Control labels are drawn with lead-lines pointing to each element.

<!-- SCREENSHOT: Device silhouette with lead-line labels -->

- Hover over a control on the silhouette to highlight it
- Click a control to select it (useful in the Mappings tab for quick binding)
- Use the **< >** arrows to browse alternate silhouettes if available

Silhouettes are currently available for Virpil hardware. Other devices display a generic placeholder.

### Hiding Devices (HidHide)

If HidHide is installed, Asteriq can hide your physical devices from other applications (like Star Citizen) so they only see the vJoy virtual devices. This prevents the "double input" problem where SC reads both the physical stick and the virtual one.

Asteriq automatically whitelists itself so it can always see all devices.

### Live Input Monitor

The Devices tab shows live input state for the selected device:

- **Axes** — horizontal bars showing current deflection in real-time
- **Buttons** — grid of indicators (lit = pressed)
- **Hats** — directional indicator showing current position

---

## Mappings Tab

The Mappings tab is where you define how physical inputs translate to vJoy outputs.

<!-- SCREENSHOT: Mappings tab showing mapping list, curve editor, and silhouette -->

### Creating a Mapping

1. Select a vJoy device using the **< >** arrows at the top.
2. Click **Add Mapping** (or double-click an empty row).
3. The Mapping Dialog opens:

<!-- SCREENSHOT: Mapping Dialog -->

**Input detection:** Move an axis or press a button on your hardware. Asteriq auto-detects the device, input type (axis/button/hat), and index. There is a 15-second timeout.

**Manual entry:** If auto-detection doesn't work, use the dropdown selectors to choose the input device and specific control.

**Output selection:** Choose the vJoy target device and output type:
- vJoy Axis (X, Y, Z, Rx, Ry, Rz, Slider, Dial)
- vJoy Button (1–128)
- vJoy POV / Hat
- Keyboard key (with Ctrl/Shift/Alt modifiers)

### Axis Mappings

Axis mappings translate analog inputs (sticks, throttle sliders) to vJoy axis outputs.

**Options:**
- **Curve** — shape the response (see [Curve Editor](#curve-editor))
- **Deadzone** — ignore small movements near centre (centred mode) or at one end (throttle mode)
- **Saturation** — limit the output range
- **Inversion** — reverse the axis direction

Multiple physical inputs can drive a single output using merge operations: Average, Minimum, Maximum, or Sum.

### Button Mappings

Button mappings connect physical buttons to vJoy buttons or keyboard keys.

**Modes:**

| Mode | Behaviour |
|---|---|
| **Normal** | Output mirrors input — pressed when held, released when let go |
| **Toggle** | Each press flips the output state (on → off → on) |
| **Pulse** | Brief activation on press (configurable 100–1000 ms) |
| **Hold-to-Activate** | Output activates only after holding for a duration (200–2000 ms) |

### Hat / POV Mappings

Map physical hat switches and D-pads to vJoy POV outputs. Supports both continuous (angle-based) and discrete (4-direction) modes.

### Axis-to-Button Mappings

Trigger a button output when an axis crosses a threshold value. Useful for "detent" positions or throttle cutoff switches.

- **Threshold**: –1.0 to 1.0
- **Direction**: Activate above or below threshold
- **Hysteresis**: Prevents flickering around the threshold point

### Button-to-Axis Mappings

Set an axis to a specific value when a button is pressed.

- **Pressed value**: The axis position when the button is held (–1.0 to 1.0)
- **Released value**: The axis position when the button is released
- **Smoothing**: Optional easing (0–2000 ms) for gradual transitions

### Curve Editor

The curve editor lets you shape the response of axis mappings with an interactive graph:

<!-- SCREENSHOT: Curve editor graph -->

- **Drag control points** to adjust the curve shape
- **Presets**: Linear (1:1), S-Curve (gentle centre + aggressive edges), Exponential (slow centre + fast edges)
- The preview shows real-time output as you move the physical axis
- Curves use Catmull-Rom interpolation for smooth response

### Mapping List

The left panel shows all mappings for the selected vJoy device:

- **Physical input** → **vJoy output** with mode indicator
- Single-click a row to highlight the control on the silhouette
- Double-click to open the Mapping Dialog for editing
- Press a mapped button/axis on your hardware and the corresponding row glows briefly
- Use **Clear All** to remove every mapping on the selected device

### TX Toggle Assignment

In the Mappings tab, you can designate a physical button as the **TX TOGGLE** — a hardware switch that toggles network forwarding mode on the master machine. See [Network Forwarding](#network-forwarding-guide) for details.

When assigned, the button row shows a "TX TOGGLE" badge. The label "SET AS TX TOGGLE" appears on right-click or via the context action.

---

## SC Bindings Tab

The SC Bindings tab manages Star Citizen's keybinding XML files — import existing layouts, assign new bindings, and export them for SC to use.

<!-- SCREENSHOT: SC Bindings tab showing binding grid -->

### SC Installation Detection

Asteriq auto-detects Star Citizen installations:

- **LIVE** — the main release branch
- **PTU** — Public Test Universe
- **EPTU** — Experimental PTU

Select the target environment from the dropdown. Asteriq reads `defaultProfile.xml` from SC's `Data.p4k` archive to understand all available actions.

### Binding Grid

The grid displays every SC action organised by category:

- **Rows** — individual actions (e.g. `v_attack_yaw_left`, `v_pitch`)
- **Columns** — input sources: Mouse, Keyboard, and vJoy devices (JS1, JS2, etc.)
- **Cells** — show which physical input is bound to each action

Filter by action category using the dropdown (Spaceship, Vehicle, Character, etc.) or type in the search field to find specific actions.

### Assigning Bindings

1. Click a cell in the vJoy column for the action you want to bind.
2. The cell enters **listening mode** — press a button or move an axis on your hardware.
3. The binding is automatically assigned (5-second timeout).

For keyboard bindings, press the desired key with any modifier combination (Ctrl, Shift, Alt).

### Column Actions

Click a **vJoy column header** (e.g. "JS1") to select it. The Column Actions panel appears:

| Action | Description |
|---|---|
| **Import from Profile** | Load bindings from a saved Asteriq profile or SC XML layout file |
| **Clear Column** | Remove all bindings for this vJoy device |
| **Deselect** | Return to the full grid view |

### Import From Profile

1. Select a source profile from the dropdown. Saved Asteriq profiles and SC XML layout files (prefixed with `[SC]`) both appear.
2. Choose the source joystick column (JS1, JS2, etc.) from that profile.
3. Click **IMPORT** — all bindings on the target column are replaced with the source bindings, reassigned to the target vJoy device.

This is useful for copying a known-good layout between profiles or importing community-shared SC XML files.

### Exporting to Star Citizen

Once your bindings are configured:

1. Set a **Profile Name** (defaults to the active Asteriq profile name).
2. Click **EXPORT** to generate an `actionmaps.xml` file.
3. The file is saved to SC's bindings directory for the selected environment.

Asteriq **never modifies** SC's existing binding files — it only writes new export files.

### Conflict Detection

The grid highlights conflicts:

- **Amber**: A binding is shared across multiple vJoy devices for the same action
- **Red**: A hard conflict that must be resolved before export

Resolve conflicts by clearing or reassigning the duplicate binding.

---

## Settings Tab

The Settings tab is divided into sections: Profile Management, System Settings, Visual Settings, Network, and Updates.

<!-- SCREENSHOT: Settings tab -->

### Profile Management

At the top of the Settings tab:

- **Profile name** — click to rename the active profile
- **Statistics** — axis, button, hat, and shift layer counts

**Actions:**

| Button | Description |
|---|---|
| **NEW** | Create a blank profile |
| **DUPLICATE** | Copy all mappings from the active profile to a new one |
| **IMPORT** | Load a `.json` profile file from disk |
| **EXPORT** | Save the active profile as a `.json` file for sharing |
| **DELETE** | Remove the active profile permanently |

### System Settings

- **Auto-load profile** — toggle: automatically load the last-used profile on startup
- **Close to tray** — toggle: closing the window minimises to the system tray instead of exiting
- **Tray icon type** — choose between a throttle or joystick icon in the system tray

**Driver Status:**
- Shows vJoy and HidHide installation status with coloured indicators
- **DRIVER SETUP** button reopens the driver installation dialog
- Displays "Available devices: N" when vJoy is active

### Visual Settings

**Font:**
- **Size** — stepper from 0.8x to 2.0x (affects all UI text)
- **Family** — Carbon (futuristic, default) or Consolas (clean monospace)

**Theme:**
- 12 colour themes in a grid selector
- Core themes: Midnight, Matrix, Amber, Ice
- Star Citizen manufacturer themes: Drake, Aegis, Anvil, Argo, Crusader, Origin, MISC, RSI

**Background Effects** (sliders, 0–100):

| Effect | Description |
|---|---|
| Grid | Intensity of the grid pattern overlay |
| Glow | Bloom/glow on active UI elements |
| Noise | Film grain texture overlay |
| Scanline | Horizontal line effect |
| Vignette | Edge darkening |

All changes apply immediately — experiment freely.

### Network Forwarding

See [Network Forwarding Guide](#network-forwarding-guide) below for the full walkthrough. The Settings tab provides:

- **Enable network forwarding** toggle
- Machine name and listen port display
- **Role selector**: TX (Master) or RX (Client)
- Per-peer connection toggles (TX mode)
- Trust code management
- Connection status

### Updates

- **Version display** — current version (e.g. `v0.8.373`)
- **Check for updates automatically** — toggle for periodic background checks
- **CHECK FOR UPDATES** — manual check button
- Update status: Up-to-date (green), Update available (amber), Downloading, Ready to apply, Error

When an update is available:
1. Click **DOWNLOAD** to fetch the new release.
2. Once downloaded, click **APPLY** to install.
3. Asteriq closes, replaces its files, and relaunches automatically.

---

## Network Forwarding Guide

### Network Overview

Network forwarding lets two Asteriq machines share input over a LAN connection. One machine acts as the **TX (master/transmitter)** and the other as the **RX (client/receiver)**.

**Use case:** Two monitors, two Star Citizen instances, one set of HOTAS — the master runs the mapping engine and forwards post-mapped vJoy output to the client machine.

**How it works:**

```
┌───────────────┐    TCP/LAN    ┌───────────────┐
│  TX (Master)  │──────────────▶│  RX (Client)  │
│               │               │               │
│  Physical     │               │  vJoy devices │
│  devices      │               │  receive      │
│  → MappingEng │               │  snapshots    │
│  → vJoy out   │               │  from master  │
│  → Snapshot   │               │               │
│    to client  │               │  SC reads     │
│               │               │  local vJoy   │
│  SC reads     │               │               │
│  local vJoy   │               │               │
└───────────────┘               └───────────────┘
```

Both machines run Star Citizen. Both see identical vJoy input.

### TX (Master) Setup

1. Go to **Settings > Network** and enable network forwarding.
2. Set the role to **TX**.
3. A trust code is generated (6-digit alphanumeric). Share this with the RX machine operator.
4. Discovered RX machines appear in the peer list with a toggle switch each.

### RX (Client) Setup

1. Go to **Settings > Network** and enable network forwarding.
2. Set the role to **RX**.
3. Wait for the TX machine to connect, or enter the trust code to pair.
4. Once paired, the trusted master's info is saved automatically.

On the RX machine, the Devices and Mappings tabs are locked (read-only) while connected — all input comes from the master.

### Connecting Machines

Both machines must be on the same LAN. Peer discovery happens automatically via UDP broadcast on port 47191.

**On the TX machine:**
- Discovered RX peers appear in the peer list
- Click the toggle switch next to a peer to connect
- The toggle slides to the middle (connecting) then fully right (connected)
- Status shows "Connected — sending"

**Switching peers:** Click a different peer's toggle — the current connection drops and the new one establishes in a single action.

**Disconnecting:** Click the toggle of the connected peer to switch it off.

### TX Toggle Button

You can assign a physical button on your HOTAS as the **TX TOGGLE** in the Mappings tab. This button cycles through network forwarding states:

- **Press once**: Connect to the first available peer
- **Press again**: Disconnect from current, connect to next peer
- **Press again** (when on last peer): Disconnect — return to local mode

The toggle includes a 400 ms debounce to handle physical button bounce.

### Troubleshooting Network

| Symptom | Cause | Fix |
|---|---|---|
| No peers discovered | Firewall blocking UDP 47191 | Allow Asteriq through Windows Firewall |
| Peers show as dim/stale | UDP beacons not reaching | Ensure both machines are on the same subnet |
| Toggle does nothing | Button bounce or debounce | Wait 400 ms between presses; check logs |
| Connection drops frequently | Network instability | Use wired Ethernet for reliable forwarding |
| RX tabs are locked | Client mode is active | Disconnect to regain local control |

---

## Profiles

### What a Profile Contains

A profile stores your complete input configuration:

- All axis mappings (curves, deadzones, saturation, inversion)
- All button mappings (modes, durations)
- All hat/POV mappings
- Axis-to-button and button-to-axis mappings
- Device ordering (which physical device → which vJoy slot)
- Shift layers and their mappings
- Device silhouette selections
- TX Toggle button assignment

### Creating Profiles

- **New** — creates a blank profile with an auto-generated name
- **Duplicate** — copies all mappings from the active profile
- **Import** — loads a `.json` file (shared by another user or backed up)

Profiles auto-save on every change. There is no manual "Save" action — your work is always preserved.

### Sharing Profiles

1. Go to **Settings > EXPORT** to save the active profile as a `.json` file.
2. Share the file with another Asteriq user.
3. They use **Settings > IMPORT** to load it.

Imported profiles are independent copies — changing one does not affect the other.

---

## Auto-Update

Asteriq checks GitHub for new releases on startup (if enabled) and on manual request.

**Update flow:**

1. **Check** — polls the GitHub releases API for the latest version.
2. **Download** — fetches `Asteriq.zip` to a temporary location.
3. **Apply** — a background script replaces the application files and relaunches.

The update preserves all your settings, profiles, and preferences — only the application binary and its supporting files are replaced.

If the installation directory requires administrator access to write (e.g. `C:\Program Files`), the update script requests elevation for the file copy but relaunches Asteriq at normal user privilege.

---

## Command Line Reference

Asteriq supports diagnostic and automation flags:

```
Asteriq.exe                          Launch GUI (normal mode)
Asteriq.exe --diag                   Live input diagnostics (console)
Asteriq.exe --hidhide                Show HidHide configuration
Asteriq.exe --match                  Correlate SDL ↔ HidHide devices
Asteriq.exe --whitelist              Add Asteriq to HidHide whitelist

Asteriq.exe --passthrough N M        Pass device N directly to vJoy M
Asteriq.exe --maptest N M            Test mapping engine with S-curve
Asteriq.exe --keytest [dev] [btn] [key]  Test keyboard output

Asteriq.exe --profiles               List all saved profiles
Asteriq.exe --profile-run ID         Load and run a profile
Asteriq.exe --profile-export ID PATH Export profile to file
Asteriq.exe --profile-import PATH    Import profile from file

Asteriq.exe --scdetect               Detect SC installations
Asteriq.exe --scextract [ENV]        Extract defaultProfile.xml
Asteriq.exe --scschema [ENV]         Parse SC action schema
Asteriq.exe --scexport [ENV]         Test SC XML export

Asteriq.exe --driver-setup           Force driver setup dialog
```

---

## Data & File Locations

| Location | Contents |
|---|---|
| `%APPDATA%\Asteriq\appsettings.json` | All application settings |
| `%APPDATA%\Asteriq\Profiles\` | Saved profiles (`.json` files) |
| `%APPDATA%\Asteriq\Logs\` | Diagnostic logs (7-day rolling retention) |
| Application folder | `Asteriq.exe`, `vJoyInterface.dll`, `asteriq.ico`, `Images/` |

---

## Troubleshooting

### Devices not detected

- Ensure the device is connected and recognised by Windows (check Device Manager).
- Asteriq uses SDL2, not DirectInput. Some very old or exotic devices may not be detected.
- Try unplugging and reconnecting the device.

### vJoy not working

- Run **Settings > DRIVER SETUP** to verify vJoy is installed.
- Check that vJoy devices are configured (the vJoy Configuration utility should show at least one device enabled).
- Restart Asteriq after installing or reconfiguring vJoy.

### Star Citizen sees double inputs

- Install HidHide and use Asteriq's device hiding to hide physical devices.
- Only vJoy devices should be visible to SC.
- Verify in SC's `Options > Keybinding` that only joystick entries with "vJoy" names appear.

### SC Bindings tab shows no actions

- Ensure a Star Citizen installation is detected (check the dropdown at the top of the tab).
- Asteriq reads `defaultProfile.xml` from SC's `Data.p4k`. If SC's installation is corrupted or incomplete, detection may fail.
- Try running `Asteriq.exe --scdetect` from the command line to diagnose.

### Update check fails

- Ensure you have internet access.
- GitHub's API has rate limits — if you check too frequently, wait a few minutes.
- Check `%APPDATA%\Asteriq\Logs\` for detailed error messages.

### Application crashes on startup

- Delete `%APPDATA%\Asteriq\appsettings.json` to reset settings to defaults.
- Run `Asteriq.exe --driver-setup` to re-check drivers.
- Check the latest log file in `%APPDATA%\Asteriq\Logs\` for the error.

---

*Asteriq is developed by MazzeeStudio. Report issues at [github.com/MazzeeStudio/Asteriq/issues](https://github.com/MazzeeStudio/Asteriq/issues).*
