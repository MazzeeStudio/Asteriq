# Performance Refactoring Plan

## Issue Summary
User experiencing stuttering and glitching during window resizes and general UI interaction on a capable computer. The application doesn't feel "solid" or performant.

## Root Causes Identified

### 1. Continuous Full-Screen Redraws (CRITICAL)
**Location**: `MainForm.cs:1587` - Update timer
```csharp
private void UpdateTimer_Tick(object? sender, EventArgs e)
{
    // ...
    _background.Update(0.016f);
    _canvas.Invalidate();  // Forces full redraw every 16ms (60fps)
}
```

**Problem**:
- Entire UI redrawn 60 times per second regardless of whether anything changed
- Background animation runs constantly even when idle
- During resize, this compounds with Windows' own paint events

**Impact**: High CPU usage, constant GPU activity, visual stuttering

### 2. No Rendering Optimization
**Location**: `MainForm.cs:3277` - `OnPaintSurface()`

**Problem**:
- No dirty region tracking - every pixel redrawn every frame
- No cached surfaces - complex panels recalculated from scratch
- Complex layered rendering (background grid, structure panels, overlays, tabs)
- Each tab renders multiple complex panels with gradients, borders, text

**Impact**: Unnecessary CPU/GPU work, poor resize performance

### 3. Multiple Invalidation Calls
**Locations**: Throughout `MainForm.cs` - 20+ calls to `_canvas.Invalidate()`

**Problem**:
- Every mouse move over certain areas invalidates entire canvas
- Every scroll event invalidates entire canvas
- Every drag operation invalidates entire canvas
- No batching or throttling

**Impact**: Wasted rendering cycles, input lag

### 4. SkiaSharp + WinForms Limitations
**Current Stack**: WinForms + SKGLControl (SkiaSharp OpenGL)

**Problem**:
- WinForms doesn't provide optimal hardware acceleration for custom rendering
- SkiaSharp is CPU-bound for many operations
- SKGLControl has known performance issues with resize
- No composition/layering support from framework

**Impact**: Baseline performance ceiling

## Performance Refactoring Phases

### Phase 1: Quick Wins (Immediate)
**Goal**: Stop unnecessary redraws, add basic optimization

#### 1.1 Conditional Animation
- [ ] Only run animation timer when background effects are visible
- [ ] Pause animation when window is minimized/inactive
- [ ] Make animation optional (user setting)
- [ ] Reduce animation update rate from 60fps to 30fps

#### 1.2 Smart Invalidation
- [ ] Add `_isDirty` flag - only invalidate when state actually changes
- [ ] Batch multiple invalidation requests into single redraw
- [ ] Add throttling for mouse-move invalidations (max 30fps)
- [ ] Skip invalidation during window resize (suppress until resize complete)

#### 1.3 Resize Optimization
- [ ] Suspend rendering during active resize operations
- [ ] Use `WM_ENTERSIZEMOVE` and `WM_EXITSIZEMOVE` to detect resize start/end
- [ ] Only redraw on final size, not during drag
- [ ] Alternative: Use low-quality render mode during resize

**Expected Impact**: 70-80% reduction in draw calls, much smoother resize

### Phase 2: Rendering Optimization (Short-term)
**Goal**: Cache and optimize what we do render

#### 2.1 Surface Caching
- [ ] Cache static panel backgrounds to `SKSurface`
- [ ] Cache device list items (redraw only on data change)
- [ ] Cache text rendering (pre-render labels)
- [ ] Implement dirty regions per panel

#### 2.2 Selective Rendering
- [ ] Only redraw panels that changed
- [ ] Clip rendering to visible scroll regions
- [ ] Skip rendering for collapsed/hidden panels
- [ ] Implement render layers (static background, dynamic content, overlay)

#### 2.3 Draw Call Reduction
- [ ] Batch similar drawing operations
- [ ] Reduce gradient complexity where not visible
- [ ] Simplify grid rendering (fewer lines, simpler pattern)
- [ ] Use simpler paths for hit testing vs rendering

**Expected Impact**: 50-60% reduction in frame render time

### Phase 3: Framework Evaluation (Medium-term)
**Goal**: Determine if current stack can meet performance requirements

#### 3.1 Measure Performance After Phase 1 & 2
- [ ] Profile frame times with optimizations
- [ ] Test resize performance
- [ ] Test on different hardware
- [ ] Get user feedback

#### 3.2 If Performance Still Poor, Evaluate:

**Option A: WPF Migration**
- Pros: Better hardware acceleration, XAML structure, keep SkiaSharp for custom elements
- Cons: Different layout paradigm, some rewrite needed
- Effort: Medium (2-3 weeks)
- Keep: All business logic (InputService, VJoyService, MappingEngine, ProfileService)
- Rewrite: UI layer only

**Option B: C++ with ImGui**
- Pros: Maximum performance, minimal code, perfect for technical UIs
- Cons: Different aesthetic (functional vs FUI), C++ complexity
- Effort: Medium-High (3-4 weeks)
- Keep: Architecture concepts, designs
- Rewrite: Everything (but simpler codebase)

**Option C: C++ with Direct2D**
- Pros: Native Windows rendering, maximum control, keep FUI aesthetic
- Cons: Most code to write, manual resource management
- Effort: High (4-6 weeks)
- Keep: Architecture concepts, designs
- Rewrite: Everything

**Option D: Avalonia UI**
- Pros: Modern XAML, cross-platform, good Skia integration
- Cons: Less mature than WPF, some rough edges
- Effort: Medium (2-3 weeks)
- Keep: All business logic
- Rewrite: UI layer only

## Implementation Priority

1. **Phase 1.1 & 1.2** - Critical, implement immediately
2. **Phase 1.3** - High priority for resize smoothness
3. **Measure & evaluate** - Can we stop here?
4. **Phase 2.1 & 2.2** if needed
5. **Phase 3** - Only if Phases 1-2 insufficient

## Success Criteria

After Phase 1:
- No stuttering during window resize
- CPU usage near 0% when idle
- Smooth 60fps during active interaction
- User reports "solid" feel

After Phase 2:
- Maintain 60fps with complex UI state
- Resize feels instant
- No visual artifacts

## Current Status

- [x] Performance issues identified and documented
- [x] Phase 1.1: Conditional animation (COMPLETED)
  - Added `_enableAnimations` flag to toggle animations
  - Animations only run when enabled
  - Background/decorative animations conditional
- [x] Phase 1.2: Smart invalidation (PARTIALLY COMPLETED)
  - Added `_isDirty` flag for state tracking
  - OnAnimationTick only invalidates when animations run or state is dirty
  - ActiveInputTracker now returns bool indicating if redraw needed
  - **TODO**: Replace remaining direct `_canvas.Invalidate()` calls with dirty flag pattern
- [ ] Phase 1.3: Resize optimization (NOT STARTED)
  - Need to add WM_ENTERSIZEMOVE/WM_EXITSIZEMOVE detection
  - Suppress rendering during active resize drag
- [ ] Phase 2: Rendering optimization (NOT STARTED)
- [ ] Phase 3: Framework evaluation (PENDING Phase 1 & 2 results)

## Notes

The current SkiaSharp implementation has beautiful output but wasn't optimized for performance. The issues are fixable without changing frameworks - the question is whether the optimizations provide enough improvement to meet user expectations.

C# + WPF or Avalonia would keep development velocity high while improving baseline performance. C++ would be the nuclear option - maximum performance but slower development.
