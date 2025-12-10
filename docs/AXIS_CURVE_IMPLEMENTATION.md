# Axis Curve and Response Implementation

This document describes the axis response curve system implementation in Asteriq, following patterns from JoystickGremlinEx.

## Overview

The axis processing pipeline applies transformations in this order (matching JoystickGremlinEx):
1. **Input** - Raw axis value from device (-1.0 to +1.0)
2. **Deadzone** - Filter small movements around center or ends
3. **Saturation** - Scale output range (input at Saturation → output 1.0)
4. **Response Curve** - Shape the output curve (linear, S-curve, exponential, custom)
5. **Inversion** - Optionally flip the output
6. **Output** - Final value sent to vJoy (-1.0 to +1.0)

## Deadzone Model (4-Parameter, JoystickGremlinEx Compatible)

### Core Formula
From JoystickGremlinEx `vjoy.py:1014-1031`:
```python
def deadzone(value, low, low_center, high_center, high):
    # Constraint: -1 <= low < low_center <= 0 <= high_center < high <= 1
    if value >= 0:
        return min(1, max(0, (value - high_center) / abs(high - high_center)))
    else:
        return max(-1, min(0, (value - low_center) / abs(low - low_center)))
```

### Parameters
```
Input Range:   [-1.0] ======== [low_center] ---- 0 ---- [high_center] ======== [+1.0]
                 ↑                  ↑                         ↑                   ↑
             DeadzoneLow     DeadzoneCenterLow        DeadzoneCenterHigh    DeadzoneHigh
```

- **DeadzoneLow** (-1.0 default): Left edge of negative travel
- **DeadzoneCenterLow** (0.0 default): Start of center deadzone
- **DeadzoneCenterHigh** (0.0 default): End of center deadzone
- **DeadzoneHigh** (+1.0 default): Right edge of positive travel

### Behavior
- Values in `[DeadzoneCenterLow, DeadzoneCenterHigh]` → output **0** (deadzone)
- Values in `[DeadzoneLow, DeadzoneCenterLow)` → scale from **-1 to 0**
- Values in `(DeadzoneCenterHigh, DeadzoneHigh]` → scale from **0 to +1**

### Centered Mode (Default)
For joystick axes that return to center. Example "Center 5%" preset:
- `DeadzoneLow = -1.0`
- `DeadzoneCenterLow = -0.05`
- `DeadzoneCenterHigh = +0.05`
- `DeadzoneHigh = +1.0`

### End-Only Mode
For throttle/slider axes that don't return to center:
- `DeadzoneLow`: Start of active range (values below = 0)
- `DeadzoneHigh`: End of active range (values above = max)

## Response Curves

### Linear
Direct 1:1 mapping. Output = Input.

### S-Curve
Smooth curve providing fine control around center with faster response at extremes.
Uses smoothstep function: `output = x * x * (3 - 2 * x)`

The Curvature parameter (-1 to +1) adjusts the curve shape:
- 0 = standard S-curve
- Positive = flatter center, steeper edges
- Negative = steeper center, flatter edges

### Exponential
Progressive response that increases at higher inputs.
`output = x^(1 + curvature * 2)`

Good for flight sims where small corrections need precision but full deflection needs speed.

### Custom
User-defined curve using control points:

- Points are stored as (input, output) pairs, both 0.0 to 1.0
- First point is always (0, 0), last is always (1, 1)
- Interior points can be added by clicking on the curve
- Points can be dragged to adjust the shape
- Linear interpolation between adjacent points

## Axis Inversion

When enabled, the output is flipped: `output = 1 - output` (applied after curve).

For centered axes, this effectively swaps positive and negative.

## UI Components

### Curve Editor (MainForm.cs)
- Visual representation of the curve
- Click curve to add control points (Custom mode only)
- Drag points to adjust shape
- Preset buttons: Linear, S-Curve, Expo, Custom

### Sliders
- **Deadzone slider**: Sets center deadzone amount (0% to 50%)
- **Saturation slider**: Sets where output reaches maximum (50% to 100%)

### Invert Toggle
Click to toggle axis inversion on/off.

## Code Structure

### Models/Mappings.cs
- `DeadzoneMode` enum: `Centered`, `EndOnly`
- `AxisCurve` class: All curve/deadzone settings
  - `Apply(float input)`: Main processing method
  - `ApplyCenteredDeadzone()`: 4-parameter deadzone
  - `ApplyEndDeadzone()`: Simple min/max deadzone
  - `ApplySCurve()`, `ApplyExponential()`, `ApplyCustom()`: Curve functions

### UI/MainForm.cs
- `_curvePresetBounds[]`: Click regions for curve type buttons
- `_curveEditorBounds`: Click region for curve visualization
- `_curveControlPoints`: Custom curve points
- `HandleCurvePresetClick()`: Curve type selection
- `DrawAxisSettings()`: Render curve editor UI
- `DrawCurveVisualization()`: Render the curve itself
- `DrawCurveControlPoints()`: Render draggable points

## Input Detection Noise Filtering

The `InputDetectionService` uses multi-stage noise filtering:

1. **Warmup Phase** (3 polls): Let device state stabilize
2. **Sample Collection** (15 samples): Build statistical baseline
3. **Baseline Computation**:
   - Calculate mean per axis
   - Calculate standard deviation
   - High variance axes use current value instead of mean
4. **Active Detection**:
   - **Jitter Threshold** (2%): Movements below this are noise
   - **Detection Threshold** (50%): How far axis must move to register
   - **Confirmation Frames** (3): Movement must be sustained

This prevents accidental axis assignments from noise/jitter.

## Integration with Mapping Engine

The `MappingEngine` applies `AxisCurve.Apply()` to raw input before sending to vJoy.

Mappings are stored in `MappingProfile.AxisMappings` as `AxisMapping` objects,
each containing an `AxisCurve` instance with all response settings.

## References

This implementation follows patterns from JoystickGremlinEx:
- `gremlin/ui/axis_calibration.py` - Calibration and deadzone UI
- `gremlin/input_devices.py` - Deadzone function (lines 2355-2384)
- `gremlin/curve_handler.py` - Curve processing (lines 2412-2438)
