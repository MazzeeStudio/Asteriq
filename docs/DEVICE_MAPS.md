# Device Maps and Images

This document explains how to add custom device images and control maps for Asteriq's visual device display.

## Folder Structure

```
Images/
└── Devices/
    ├── joystick.svg           # Generic joystick silhouette
    ├── throttle.svg           # Generic throttle silhouette
    ├── virpil_alpha.svg       # Device-specific SVG (example)
    └── Maps/
        ├── device-control-map.schema.json  # JSON schema
        ├── joystick.json      # Generic joystick control map
        ├── throttle.json      # Generic throttle control map
        └── virpil_alpha_r.json # Device-specific map (example)
```

## Adding a New Device

### Step 1: Create the SVG Image

Create an SVG file in `Images/Devices/`. Requirements:

- **Stroke-based design**: Use strokes rather than fills for the silhouette
- **Dark theme compatible**: Light colored strokes (the app applies its own styling)
- **ViewBox**: Note your viewBox dimensions (e.g., `viewBox="0 0 2048 2048"`)
- **Control anchors**: Identify X,Y coordinates for each control's anchor point

Example: `Images/Devices/my_joystick.svg`

### Step 2: Create the Device Map JSON

Create a JSON file in `Images/Devices/Maps/`. The filename should be descriptive (e.g., `vkb_gladiator_r.json`).

```json
{
  "$schema": "device-control-map.schema.json",
  "schemaVersion": "1.1",
  "device": "Gladiator",
  "vidPid": "231D:0200",
  "deviceType": "Stick",
  "svgFile": "vkb_gladiator.svg",
  "mirror": false,
  "viewBox": { "x": 2048, "y": 2048 },
  "controls": {
    "trigger": {
      "id": "control_trigger",
      "type": "Button",
      "bindings": ["button1"],
      "label": "Trigger",
      "description": "Primary trigger",
      "anchor": { "x": 500, "y": 400 },
      "labelOffset": { "x": 150, "y": -50 },
      "leadLine": {
        "shelfSide": "right",
        "shelfLength": 80,
        "segments": [
          { "angle": 45, "length": 100 }
        ]
      }
    }
  }
}
```

### Step 3: Create Left-Hand Variant (Optional)

For devices with left/right variants, create two map files:

- `my_joystick_r.json` - Right hand (`"mirror": false`)
- `my_joystick_l.json` - Left hand (`"mirror": true`)

Both can reference the same SVG file; the `mirror` property flips it horizontally.

## Device Map Properties

| Property | Required | Description |
|----------|----------|-------------|
| `schemaVersion` | Yes | Schema version (currently `"1.1"`) |
| `device` | Yes | Device name for matching (partial match supported) |
| `vidPid` | No | USB VID:PID for exact matching (e.g., `"3344:0194"`) |
| `deviceType` | Yes | `Stick`, `Throttle`, `Pedals`, `Grip`, or `Generic` |
| `svgFile` | Yes | SVG filename in `Images/Devices/` |
| `mirror` | No | `true` to flip image horizontally (for left-hand devices) |
| `viewBox` | Yes | SVG viewBox dimensions `{ "x": width, "y": height }` |
| `controls` | Yes | Map of control definitions |

## Control Properties

| Property | Required | Description |
|----------|----------|-------------|
| `id` | No | SVG element ID for highlighting |
| `type` | Yes | `Button`, `Axis`, `Hat`, `Encoder`, `Toggle`, `Slider`, `Ministick` |
| `bindings` | Yes | Array of input names (e.g., `["button1"]`, `["hat1_up", "hat1_down"]`) |
| `label` | Yes | Display name |
| `description` | No | Tooltip/description text |
| `anchor` | Yes | Control position in SVG coordinates `{ "x": 500, "y": 400 }` |
| `labelOffset` | Yes | Label position relative to anchor `{ "x": 150, "y": -50 }` |
| `leadLine` | No | Lead line configuration (see below) |

## Lead Line Configuration

Lead lines connect control anchors to their labels:

```json
"leadLine": {
  "shelfSide": "right",
  "shelfLength": 80,
  "segments": [
    { "angle": 45, "length": 100 },
    { "angle": 0, "length": 50 }
  ]
}
```

- `shelfSide`: `"left"` or `"right"` - which side the label shelf extends
- `shelfLength`: Length of horizontal shelf line at the label
- `segments`: Array of line segments from anchor toward label
  - `angle`: Degrees (0 = right, 90 = up, -90 = down)
  - `length`: Segment length in SVG units

## Device Matching

When a device is selected, Asteriq searches for a matching map:

1. **Exact name match**: Device name contains map's `device` value
2. **VID:PID match**: If `vidPid` is specified and matches
3. **Device type fallback**: Match by `deviceType` based on keywords in device name
4. **Generic fallback**: Use `joystick.json` or `throttle.json`

### Auto-Mirror Detection

If no specific map is found, Asteriq auto-enables mirroring for devices with names containing:
- `"LEFT"` (prefix)
- `"- L"` (anywhere)
- `" L"` (suffix)

## Binding Names

Use these standard binding names in the `bindings` array:

**Buttons**: `button1`, `button2`, ... `button32`

**Axes**: `x`, `y`, `z`, `rx`, `ry`, `rz`, `slider1`, `slider2`

**Hats**: For hat directions, use the button indices that correspond to each direction.

## SVG Creation Workflow

This section documents how to create device SVGs from scratch, particularly when only real-world photos are available (no line drawings or vector source material).

### Phase 1: 2D Silhouettes (Current)

The current system uses flat SVGs rendered in SkiaSharp. The workflow to produce them:

1. **Source a clean image** — Product photos from manufacturer websites or press kits work best (clean backgrounds, consistent lighting). For VKB devices where only real-world photos exist, start here.

2. **Remove background** — Use remove.bg, GIMP (Fuzzy Select + Delete), or Photoshop to isolate the device on a transparent background. Export as PNG.

3. **Convert to high-contrast silhouette** — In GIMP: Colors > Threshold to produce a clean black-and-white shape. Adjust the threshold slider until the outline is clean with minimal noise. Export as PNG.

4. **Vectorize** — Options:
   - **Inkscape** (free): File > Import the B&W PNG, then Path > Trace Bitmap > Brightness Cutoff. Adjust threshold, click OK. Delete the raster image underneath.
   - **Vectorizer.ai** (online): Upload the B&W PNG, download the SVG result.
   - **Illustrator**: File > Place, then Image Trace > Silhouettes preset.

5. **Clean up in Inkscape** —
   - Simplify paths (Path > Simplify) to reduce node count
   - Set document viewBox to `0 0 2048 2048` to match existing convention
   - Scale/position the silhouette to fill the viewBox appropriately
   - Remove any stray artifacts from the trace

6. **Add control groups** — For each control on the device, wrap the relevant path elements in a group:
   ```xml
   <g id="control_trigger">
     <path d="..." />
   </g>
   ```
   These `control_*` IDs are referenced by the JSON map for bounding-box calculation.

7. **Build the JSON map** — Create a map file in `Images/Devices/Maps/` with the device's VID:PID, control anchors, label offsets, and leadline definitions (see schema above).

8. **Test** — Run Asteriq, select the device in the Devices tab, verify the silhouette renders correctly and leadlines connect anchors to labels.

### Tips

- **Simpler is fine**: Flat single-fill silhouettes work just as well as the gradient-heavy existing SVGs. The leadline system only depends on anchor coordinates from the JSON, not SVG detail level.
- **Mirror reuse**: For left/right variants of the same device, create one SVG and use `"mirror": true` in the left-hand JSON map.
- **VKB source images**: Check VKB's product pages for clean renders. Their marketing photos on white backgrounds trace much more cleanly than user photos.
- **Virpil source images**: Virpil's product pages typically have clean studio shots suitable for direct tracing.

### Phase 2: 3D Renders (Future)

The long-term goal is to replace flat SVGs with 3D models that can:

- **Rotate** when a control on the back/opposing side is clicked, showing the relevant face
- **Animate axes** to replicate physical device movement in real-time (e.g., stick deflection on X/Y, throttle travel on Z)

This will require:

1. **3D model format** — Evaluate options: glTF/GLB (widely supported, compact), OBJ (simple but no animation), or FBX (complex but full-featured). glTF is the likely choice for web-ecosystem tooling and runtime efficiency.

2. **Model creation** — Options:
   - **CAD-to-mesh**: If manufacturers provide STEP/IGES files, convert via Blender or FreeCAD
   - **Photogrammetry**: Capture multiple angles of the physical device, reconstruct in Meshroom or RealityCapture
   - **Manual modeling**: Build simplified models in Blender from reference photos (most control over result)
   - **AI-assisted**: Tools like Tripo3D, Meshy, or CSM can generate 3D meshes from photos — quality varies, cleanup needed

3. **Rendering integration** — Options for .NET/WinForms:
   - **SkiaSharp + custom projection**: Software-render a simplified wireframe/silhouette (limited but no new dependencies)
   - **OpenTK (OpenGL)**: Embed a GL surface in the WinForms panel for hardware-accelerated 3D
   - **Silk.NET**: Modern .NET bindings for Vulkan/OpenGL/DirectX
   - **Helix Toolkit**: WPF-based 3D toolkit (would require WPF interop)

4. **Control mapping in 3D** — Extend the JSON map schema to include:
   - 3D anchor coordinates (x, y, z) instead of 2D
   - Face/side associations for each control (front, back, left, right, top)
   - Camera presets for each face so clicking a back-side control rotates to show it

5. **Axis visualization** — Map physical axis values to model transforms:
   - Stick X/Y → model pitch/roll rotation
   - Throttle Z → slider translation along axis
   - Twist (Rz) → model yaw rotation

This is a significant architectural addition and should be prototyped with a single device (e.g., VKB Gladiator) before building out the full system.

## Example: Multi-Binding Control

A dual-stage trigger with two buttons:
```json
"trigger": {
  "type": "Button",
  "bindings": ["button4", "button5"],
  "label": "Trigger",
  "description": "Dual-stage: Stage1=button4, Stage2=button5"
}
```

A 5-way hat:
```json
"top_hat": {
  "type": "Hat",
  "bindings": ["button8", "button9", "button10", "button11", "button12"],
  "label": "Top Hat",
  "description": "5-way: Center=8, Up=9, Left=10, Down=11, Right=12"
}
```
