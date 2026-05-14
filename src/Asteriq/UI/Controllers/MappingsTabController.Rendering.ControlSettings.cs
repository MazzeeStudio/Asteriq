using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
    private void DrawThresholdSettings(SKCanvas canvas, float leftMargin, float rightMargin, float y, float bottom)
    {
        float width = rightMargin - leftMargin;

        float liveValue = GetLiveAxisValueForThreshold();
        float checkboxSize = 12f;

        // --- ABOVE section ---
        y += 8;
        _threshold.AboveBounds = new SKRect(leftMargin, y, leftMargin + checkboxSize, y + checkboxSize);
        FUIWidgets.DrawCheckboxWithLabel(canvas, _threshold.AboveBounds, _threshold.AboveEnabled,
            _threshold.HoveredDirection == 0, "ABOVE THRESHOLD");
        y += checkboxSize + 14;

        if (_threshold.AboveEnabled)
        {
            y = DrawThresholdSection(canvas, leftMargin, rightMargin, y, width, liveValue,
                _threshold.AboveThreshold, _threshold.AboveHysteresis, true,
                _threshold.AboveKeyName, _threshold.AboveModifiers,
                ref _threshold.AboveSliderBounds, ref _threshold.AboveHystSliderBounds,
                ref _threshold.AboveCaptureBounds, ref _threshold.AboveClearBounds,
                ref _threshold.AboveCapturing, _threshold.AboveCaptureStartTicks,
                _threshold.AboveCaptureHovered, _threshold.AboveClearHovered);
        }

        // --- BELOW section ---
        y += 8;
        _threshold.BelowBounds = new SKRect(leftMargin, y, leftMargin + checkboxSize, y + checkboxSize);
        FUIWidgets.DrawCheckboxWithLabel(canvas, _threshold.BelowBounds, _threshold.BelowEnabled,
            _threshold.HoveredDirection == 1, "BELOW THRESHOLD");
        y += checkboxSize + 14;

        if (_threshold.BelowEnabled)
        {
            y = DrawThresholdSection(canvas, leftMargin, rightMargin, y, width, liveValue,
                _threshold.BelowThreshold, _threshold.BelowHysteresis, false,
                _threshold.BelowKeyName, _threshold.BelowModifiers,
                ref _threshold.BelowSliderBounds, ref _threshold.BelowHystSliderBounds,
                ref _threshold.BelowCaptureBounds, ref _threshold.BelowClearBounds,
                ref _threshold.BelowCapturing, _threshold.BelowCaptureStartTicks,
                _threshold.BelowCaptureHovered, _threshold.BelowClearHovered);
        }
    }

    private static float DrawThresholdSection(SKCanvas canvas, float leftMargin, float rightMargin, float y, float width,
        float liveValue, float threshold, float hysteresis, bool isAbove,
        string keyName, List<string>? modifiers,
        ref SKRect sliderBounds, ref SKRect hystSliderBounds,
        ref SKRect captureBounds, ref SKRect clearBounds,
        ref bool isCapturing, long captureStartTicks,
        bool captureHovered, bool clearHovered)
    {
        // Threshold slider
        FUIWidgets.DrawSectionLabel(canvas, $"VALUE  {threshold:F2}", leftMargin, ref y, 12f);

        float sliderHeight = 24f;
        sliderBounds = new SKRect(leftMargin, y, rightMargin, y + sliderHeight);

        using (var trackBg = FUIRenderer.CreateFillPaint(FUIColors.Background1))
            canvas.DrawRoundRect(sliderBounds, 3, 3, trackBg);
        using (var trackFrame = FUIRenderer.CreateStrokePaint(FUIColors.Frame))
            canvas.DrawRoundRect(sliderBounds, 3, 3, trackFrame);

        // Active zone tint
        float threshX = sliderBounds.Left + ((threshold + 1f) / 2f) * sliderBounds.Width;
        var activeZone = isAbove
            ? new SKRect(threshX, sliderBounds.Top, sliderBounds.Right, sliderBounds.Bottom)
            : new SKRect(sliderBounds.Left, sliderBounds.Top, threshX, sliderBounds.Bottom);
        using (var tint = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(20)))
            canvas.DrawRect(activeZone, tint);

        // Hysteresis band
        float hystPixels = (hysteresis / 2f) * sliderBounds.Width;
        var hystBands = new SKRect(threshX - hystPixels, sliderBounds.Top, threshX + hystPixels, sliderBounds.Bottom);
        using (var hystPaint = FUIRenderer.CreateFillPaint(FUIColors.Warning.WithAlpha(30)))
            canvas.DrawRect(hystBands, hystPaint);

        // Threshold line
        using (var threshLine = FUIRenderer.CreateStrokePaint(FUIColors.Primary, 2f))
            canvas.DrawLine(threshX, sliderBounds.Top, threshX, sliderBounds.Bottom, threshLine);

        // Live axis indicator
        float liveX = Math.Clamp(sliderBounds.Left + ((liveValue + 1f) / 2f) * sliderBounds.Width, sliderBounds.Left, sliderBounds.Right);
        using (var livePaint = FUIRenderer.CreateStrokePaint(FUIColors.Active, 2f))
            canvas.DrawLine(liveX, sliderBounds.Top, liveX, sliderBounds.Bottom, livePaint);

        // Tick labels
        y += sliderHeight + 4;
        FUIRenderer.DrawText(canvas, "-1.0", new SKPoint(leftMargin, y + 8), FUIColors.TextDim, 10f);
        using var tickPaint = FUIRenderer.CreateTextPaint(FUIColors.TextDim, 10f);
        float rightLabelWidth = tickPaint.MeasureText("1.0");
        FUIRenderer.DrawText(canvas, "1.0", new SKPoint(rightMargin - rightLabelWidth, y + 8), FUIColors.TextDim, 10f);
        y += 12;

        // Hysteresis slider
        FUIWidgets.DrawSectionLabel(canvas, $"HYSTERESIS  {hysteresis:F2}", leftMargin, ref y);

        float hystSliderHeight = 16f;
        hystSliderBounds = new SKRect(leftMargin, y, rightMargin, y + hystSliderHeight);

        using (var trackBg2 = FUIRenderer.CreateFillPaint(FUIColors.Background1))
            canvas.DrawRoundRect(hystSliderBounds, 3, 3, trackBg2);
        using (var trackFrame2 = FUIRenderer.CreateStrokePaint(FUIColors.Frame))
            canvas.DrawRoundRect(hystSliderBounds, 3, 3, trackFrame2);

        float hystNorm = hysteresis / 0.25f;
        float hystHandleX = Math.Clamp(hystSliderBounds.Left + hystNorm * hystSliderBounds.Width, hystSliderBounds.Left, hystSliderBounds.Right);

        var hystFill = new SKRect(hystSliderBounds.Left, hystSliderBounds.Top, hystHandleX, hystSliderBounds.Bottom);
        using (var fillPaint = FUIRenderer.CreateFillPaint(FUIColors.Warning.WithAlpha(40)))
            canvas.DrawRoundRect(hystFill, 3, 3, fillPaint);
        using (var handlePaint = FUIRenderer.CreateStrokePaint(FUIColors.Warning, 2f))
            canvas.DrawLine(hystHandleX, hystSliderBounds.Top, hystHandleX, hystSliderBounds.Bottom, handlePaint);

        y += hystSliderHeight + 4;

        // Key capture
        FUIWidgets.DrawSectionLabel(canvas, "KEY", leftMargin, ref y);

        float capHeight = 32f;
        captureBounds = new SKRect(leftMargin, y, rightMargin, y + capHeight);

        // Timeout check
        if (isCapturing)
        {
            var capElapsed = Environment.TickCount64 - captureStartTicks;
            if (capElapsed >= KeyCaptureTimeoutMs)
                isCapturing = false;
        }

        bool hasKey = !string.IsNullOrEmpty(keyName);
        var capBg = isCapturing ? FUIColors.Active.WithAlpha(FUIColors.AlphaGlow) : (captureHovered ? FUIColors.Primary.WithAlpha(40) : FUIColors.Background1);
        var capFrame = isCapturing ? FUIColors.Active : (captureHovered ? FUIColors.FrameBright : FUIColors.Frame);

        using (var bg = FUIRenderer.CreateFillPaint(capBg))
            canvas.DrawRoundRect(captureBounds, 3, 3, bg);
        using (var fr = FUIRenderer.CreateStrokePaint(capFrame))
            canvas.DrawRoundRect(captureBounds, 3, 3, fr);

        if (isCapturing)
        {
            FUIRenderer.DrawTextCentered(canvas, "Press key combo...", captureBounds, FUIColors.Active, 14f);
            float elapsed = (Environment.TickCount64 - captureStartTicks) / (float)KeyCaptureTimeoutMs;
            float progress = Math.Clamp(1f - elapsed, 0f, 1f);
            var progressBounds = new SKRect(leftMargin + 2, y + capHeight - 3, leftMargin + 2 + (width - 4) * progress, y + capHeight - 1);
            using var progressPaint = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(80));
            canvas.DrawRect(progressBounds, progressPaint);
        }
        else if (hasKey)
        {
            FUIWidgets.DrawKeycapsInBounds(canvas, captureBounds, keyName, modifiers);

            float clearSize = 20f;
            clearBounds = new SKRect(rightMargin - clearSize - 4, y + (capHeight - clearSize) / 2,
                rightMargin - 4, y + (capHeight + clearSize) / 2);
            FUIRenderer.DrawRoundedPanel(canvas, clearBounds,
                clearHovered ? FUIColors.WarningTint : SKColors.Transparent,
                clearHovered ? FUIColors.Warning : FUIColors.Frame);
            FUIRenderer.DrawTextCentered(canvas, "X", clearBounds,
                clearHovered ? FUIColors.Warning : FUIColors.TextDim, 12f);
        }
        else
        {
            FUIRenderer.DrawTextCentered(canvas, "Click to capture key", captureBounds, FUIColors.TextDim, 14f);
        }

        y += capHeight + 4;
        return y;
    }

    private float GetLiveAxisValueForThreshold()
    {
        var mappings = GetCurrentAxisToButtonMappings();
        if (mappings.Count == 0 || mappings[0].Inputs.Count == 0) return 0f;

        var input = mappings[0].Inputs[0];
        var device = _ctx.Devices.FirstOrDefault(d => d.InstanceGuid.ToString() == input.DeviceId);
        if (device is null) return 0f;

        var state = _ctx.InputService.GetDeviceState(device.DeviceIndex);
        if (state is null || input.Index >= state.Axes.Length) return 0f;

        return state.Axes[input.Index];
    }

    private void DrawAxisSettings(SKCanvas canvas, float leftMargin, float rightMargin, float y, float bottom)
    {
        float width = rightMargin - leftMargin;

        // Response Curve header
        FUIWidgets.DrawSectionLabel(canvas, "RESPONSE CURVE", leftMargin, ref y);

        // Symmetrical, Centre, and Invert checkboxes on their own row
        // Symmetrical on left, Centre and Invert on right
        float checkboxSize = 12f;
        float rowHeight = 16f;
        float checkboxY = y + (rowHeight - checkboxSize) / 2; // Center checkbox in row
        float fontSize = 12f;

        // Symmetrical checkbox (leftmost) - checkbox then label
        _curve.CheckboxBounds = new SKRect(leftMargin, checkboxY, leftMargin + checkboxSize, checkboxY + checkboxSize);
        bool symHovered = _curve.CheckboxBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawCheckboxWithLabel(canvas, _curve.CheckboxBounds, _curve.Symmetrical, symHovered, "Symmetrical", fontSize);

        // Invert checkbox (rightmost) - label then checkbox
        float invertCheckX = rightMargin - checkboxSize;
        _deadzone.InvertToggleBounds = new SKRect(invertCheckX, checkboxY, invertCheckX + checkboxSize, checkboxY + checkboxSize);
        bool invHovered = _deadzone.InvertToggleBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawCheckboxWithLabelLeft(canvas, _deadzone.InvertToggleBounds, _deadzone.AxisInverted, invHovered, "Invert", fontSize);

        // Centre checkbox (left of Invert) - label then checkbox
        float invertLabelWidth = FUIRenderer.MeasureText("Invert", fontSize);
        float centreCheckX = invertCheckX - invertLabelWidth - 7 - 12 - checkboxSize;
        _deadzone.CenterCheckboxBounds = new SKRect(centreCheckX, checkboxY, centreCheckX + checkboxSize, checkboxY + checkboxSize);
        bool ctrHovered = _deadzone.CenterCheckboxBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawCheckboxWithLabelLeft(canvas, _deadzone.CenterCheckboxBounds, _deadzone.CenterEnabled, ctrHovered, "Centre", fontSize);

        y += rowHeight + 6f;

        // Curve preset buttons - store bounds for click handling
        string[] presets = { "LINEAR", "S-CURVE", "EXPO", "CUSTOM" };
        float buttonWidth = (width - 12) / presets.Length; // 3 gaps of 4px each
        float buttonHeight = 24f;  // 4px aligned minimum

        for (int i = 0; i < presets.Length; i++)
        {
            var presetBounds = new SKRect(
                leftMargin + i * (buttonWidth + 4), y,
                leftMargin + i * (buttonWidth + 4) + buttonWidth, y + buttonHeight);

            // Store bounds for click detection
            _curve.PresetBounds[i] = presetBounds;

            CurveType presetType = i switch
            {
                0 => CurveType.Linear,
                1 => CurveType.SCurve,
                2 => CurveType.Exponential,
                _ => CurveType.Custom
            };

            bool isActive = _curve.SelectedType == presetType;
            bool isHovered = _hoveredCurvePreset == i;

            var bgColor = isActive ? FUIColors.Active.WithAlpha(FUIColors.AlphaGlow) : (isHovered ? FUIColors.Primary.WithAlpha(40) : FUIColors.Background2);
            var frameColor = isActive ? FUIColors.Active : (isHovered ? FUIColors.FrameBright : FUIColors.Frame);
            var textColor = isActive ? FUIColors.TextBright : (isHovered ? FUIColors.TextPrimary : FUIColors.TextDim);

            using var bgPaint = FUIRenderer.CreateFillPaint(bgColor);
            canvas.DrawRoundRect(presetBounds, 3, 3, bgPaint);

            using var framePaint = FUIRenderer.CreateStrokePaint(frameColor, isActive ? 2f : 1f);
            canvas.DrawRoundRect(presetBounds, 3, 3, framePaint);

            FUIRenderer.DrawTextCentered(canvas, presets[i], presetBounds, textColor, 12f);
        }
        y += buttonHeight + 6f;

        // Curve editor visualization
        float curveHeight = 140f;
        _curve.Bounds = new SKRect(leftMargin, y, rightMargin, y + curveHeight);
        DrawCurveVisualization(canvas, _curve.Bounds);
        y += curveHeight + 43f;  // tick labels end at bounds.Bottom+17; +16px gap before live indicator

        // Live axis movement indicator
        var axisMapping = GetCurrentAxisMapping();
        if (axisMapping is not null)
        {
            float indicatorHeight = DrawAxisMovementIndicator(canvas, leftMargin, rightMargin, y, axisMapping);
            y += indicatorHeight + 6f;
        }
        y += 4f;

        // Deadzone section — draw if at least the header + slider fits (50px)
        if (y + 50 < bottom)
        {
            // Header row: "DEADZONE" label + preset buttons + selected handle indicator
            FUIRenderer.DrawText(canvas, "DEADZONE", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);

            // Preset buttons - always visible, apply to selected handle
            string[] presetLabels = { "0%", "2%", "5%", "10%" };
            float presetBtnWidth = 32f;
            float presetStartX = rightMargin - (presetBtnWidth * 4 + 9);

            // CA2000: using var inside for loop is safe — analyzer false positive
#pragma warning disable CA2000
            for (int col = 0; col < 4; col++)
            {
                var btnBounds = new SKRect(
                    presetStartX + col * (presetBtnWidth + 3), y - 2,
                    presetStartX + col * (presetBtnWidth + 3) + presetBtnWidth, y + 14);
                _deadzone.PresetBounds[col] = btnBounds;

                bool enabled = _deadzone.SelectedHandle >= 0;
                bool isHovered = _hoveredDeadzonePreset == col;

                var bgColor = enabled
                    ? (isHovered ? FUIColors.Primary.WithAlpha(40) : FUIColors.Background2)
                    : FUIColors.Background2;
                var frameColor = enabled
                    ? (isHovered ? FUIColors.FrameBright : FUIColors.Frame)
                    : FUIColors.Frame.WithAlpha(100);
                var textColor = enabled
                    ? (isHovered ? FUIColors.TextPrimary : FUIColors.TextDim)
                    : FUIColors.TextDim.WithAlpha(100);

                using var btnBg = FUIRenderer.CreateFillPaint(bgColor);
                canvas.DrawRoundRect(btnBounds, 2, 2, btnBg);
                using var btnFrame = FUIRenderer.CreateStrokePaint(frameColor);
                canvas.DrawRoundRect(btnBounds, 2, 2, btnFrame);
                FUIRenderer.DrawTextCentered(canvas, presetLabels[col], btnBounds, textColor, 12f);
            }
#pragma warning restore CA2000

            // Show which handle is selected (if any)
            if (_deadzone.SelectedHandle >= 0)
            {
                string[] handleNames = { "Start", "Ctr-", "Ctr+", "End" };
                string selectedName = handleNames[_deadzone.SelectedHandle];
                FUIRenderer.DrawText(canvas, $"[{selectedName}]", new SKPoint(presetStartX - 45, y), FUIColors.Active, 12f);
            }
            y += 20f;

            // Dual deadzone slider (always shows min/max, optionally shows center handles)
            float sliderHeight = 24f;
            _deadzone.SliderBounds = new SKRect(leftMargin, y, rightMargin, y + sliderHeight);
            DrawDualDeadzoneSlider(canvas, _deadzone.SliderBounds);
            y += sliderHeight + 16f;  // baseline needs +16 so text top (baseline-10) clears slider handles

            // Value labels - fixed positions at track edges (prevents collision)
            if (_deadzone.CenterEnabled)
            {
                // Two-track layout - fixed positions at each track edge
                float gap = 24f;
                float centerX = _deadzone.SliderBounds.MidX;
                float leftTrackRight = centerX - gap / 2;
                float rightTrackLeft = centerX + gap / 2;

                // Min at left edge, CtrMin at right edge of left track
                // CtrMax at left edge of right track, Max at right edge
                FUIRenderer.DrawText(canvas, $"{_deadzone.Min:F2}", new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, $"{_deadzone.CenterMin:F2}", new SKPoint(leftTrackRight - 24, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, $"{_deadzone.CenterMax:F2}", new SKPoint(rightTrackLeft, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, $"{_deadzone.Max:F2}", new SKPoint(rightMargin - 20, y), FUIColors.TextDim, 12f);
            }
            else
            {
                // Single track - just show start and end at edges
                FUIRenderer.DrawText(canvas, $"{_deadzone.Min:F2}", new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, $"{_deadzone.Max:F2}", new SKPoint(rightMargin - 20, y), FUIColors.TextDim, 12f);
            }
        }
    }

    private void DrawDualDeadzoneSlider(SKCanvas canvas, SKRect bounds)
    {
        // Convert -1..1 values to 0..1 for display
        float minPos = (_deadzone.Min + 1f) / 2f;
        float centerMinPos = (_deadzone.CenterMin + 1f) / 2f;
        float centerMaxPos = (_deadzone.CenterMax + 1f) / 2f;
        float maxPos = (_deadzone.Max + 1f) / 2f;

        float handleRadius = 8f;
        float trackHeight = 8f;
        float trackY = bounds.MidY - trackHeight / 2;

        using var activePaint = FUIRenderer.CreateFillPaint(FUIColors.SelectionBorder);

        if (_deadzone.CenterEnabled)
        {
            // Two physically separate tracks like JoystickGremlinEx
            // Gap must be > 2 * handleRadius so handles never overlap when both at center
            float gap = 24f;
            float centerX = bounds.MidX;

            // Left track: from bounds.Left to centerX - gap/2
            var leftTrack = new SKRect(bounds.Left, trackY, centerX - gap / 2, trackY + trackHeight);
            FUIRenderer.DrawRoundedPanel(canvas, leftTrack, FUIColors.Background2, FUIColors.Frame, 4f);

            // Right track: from centerX + gap/2 to bounds.Right
            var rightTrack = new SKRect(centerX + gap / 2, trackY, bounds.Right, trackY + trackHeight);
            FUIRenderer.DrawRoundedPanel(canvas, rightTrack, FUIColors.Background2, FUIColors.Frame, 4f);

            // Active fill on left track (from min handle to center-min handle)
            float leftTrackWidth = leftTrack.Width;
            float minPosInLeft = (minPos - 0f) / 0.5f; // Map 0..0.5 to 0..1 for left track
            float ctrMinPosInLeft = (centerMinPos - 0f) / 0.5f;
            minPosInLeft = Math.Clamp(minPosInLeft, 0f, 1f);
            ctrMinPosInLeft = Math.Clamp(ctrMinPosInLeft, 0f, 1f);

            float leftFillStart = leftTrack.Left + minPosInLeft * leftTrackWidth;
            float leftFillEnd = leftTrack.Left + ctrMinPosInLeft * leftTrackWidth;
            if (leftFillEnd > leftFillStart + 1)
            {
                var leftFill = new SKRect(leftFillStart, trackY + 1, leftFillEnd, trackY + trackHeight - 1);
                canvas.DrawRoundRect(leftFill, 3, 3, activePaint);
            }

            // Active fill on right track (from center-max handle to max handle)
            float rightTrackWidth = rightTrack.Width;
            float ctrMaxPosInRight = (centerMaxPos - 0.5f) / 0.5f; // Map 0.5..1 to 0..1 for right track
            float maxPosInRight = (maxPos - 0.5f) / 0.5f;
            ctrMaxPosInRight = Math.Clamp(ctrMaxPosInRight, 0f, 1f);
            maxPosInRight = Math.Clamp(maxPosInRight, 0f, 1f);

            float rightFillStart = rightTrack.Left + ctrMaxPosInRight * rightTrackWidth;
            float rightFillEnd = rightTrack.Left + maxPosInRight * rightTrackWidth;
            if (rightFillEnd > rightFillStart + 1)
            {
                var rightFill = new SKRect(rightFillStart, trackY + 1, rightFillEnd, trackY + trackHeight - 1);
                canvas.DrawRoundRect(rightFill, 3, 3, activePaint);
            }

            // Draw handles - all same size
            // Min handle on left edge of left track
            float minHandleX = leftTrack.Left + minPosInLeft * leftTrackWidth;
            DrawDeadzoneHandle(canvas, bounds.MidY, minHandleX, 0, FUIColors.Active, handleRadius);

            // CtrMin handle on right edge of left track
            float ctrMinHandleX = leftTrack.Left + ctrMinPosInLeft * leftTrackWidth;
            DrawDeadzoneHandle(canvas, bounds.MidY, ctrMinHandleX, 1, FUIColors.Active, handleRadius);

            // CtrMax handle on left edge of right track
            float ctrMaxHandleX = rightTrack.Left + ctrMaxPosInRight * rightTrackWidth;
            DrawDeadzoneHandle(canvas, bounds.MidY, ctrMaxHandleX, 2, FUIColors.Active, handleRadius);

            // Max handle on right edge of right track
            float maxHandleX = rightTrack.Left + maxPosInRight * rightTrackWidth;
            DrawDeadzoneHandle(canvas, bounds.MidY, maxHandleX, 3, FUIColors.Active, handleRadius);
        }
        else
        {
            // Single track spanning full width
            var track = new SKRect(bounds.Left, trackY, bounds.Right, trackY + trackHeight);
            FUIRenderer.DrawRoundedPanel(canvas, track, FUIColors.Background2, FUIColors.Frame, 4f);

            // Active fill from min to max
            float fillStart = bounds.Left + minPos * bounds.Width;
            float fillEnd = bounds.Left + maxPos * bounds.Width;
            if (fillEnd > fillStart + 1)
            {
                var fill = new SKRect(fillStart, trackY + 1, fillEnd, trackY + trackHeight - 1);
                canvas.DrawRoundRect(fill, 3, 3, activePaint);
            }

            // Draw handles - same size
            float minHandleX = bounds.Left + minPos * bounds.Width;
            float maxHandleX = bounds.Left + maxPos * bounds.Width;
            DrawDeadzoneHandle(canvas, bounds.MidY, minHandleX, 0, FUIColors.Active, handleRadius);
            DrawDeadzoneHandle(canvas, bounds.MidY, maxHandleX, 3, FUIColors.Active, handleRadius);
        }
    }

    private void DrawDeadzoneHandle(SKCanvas canvas, float centerY, float x, int handleIndex, SKColor color, float radius)
    {
        bool isDragging = _deadzone.DraggingHandle == handleIndex;
        bool isSelected = _deadzone.SelectedHandle == handleIndex;
        float drawRadius = isDragging ? radius + 2f : radius;

        // Selected handles get a highlighted fill
        SKColor fillColor = isDragging ? color : (isSelected ? color.WithAlpha(200) : FUIColors.TextPrimary);

        using var fillPaint = FUIRenderer.CreateFillPaint(fillColor);
        canvas.DrawCircle(x, centerY, drawRadius, fillPaint);

        using var strokePaint = FUIRenderer.CreateStrokePaint(color, isSelected ? 2.5f : 1.5f);
        canvas.DrawCircle(x, centerY, drawRadius, strokePaint);
    }

    /// <summary>
    /// Renders the read-only "this row has been shared from the SC Bindings tab" panel that
    /// replaces the normal mapping editor. Lists every action sharing this slot, an
    /// explanation, and a "MANAGE IN KEYBINDINGS" button that deep-links to the SC Bindings
    /// tab with the search box pre-set so the user can see / unshare the originating actions.
    /// </summary>
    private void DrawButtonSettings(SKCanvas canvas, float leftMargin, float rightMargin, float y, float bottom)
    {
        float width = rightMargin - leftMargin;

        FUIWidgets.DrawSectionLabel(canvas, "OUTPUT TYPE", leftMargin, ref y);

        // Output type tabs
        string[] outputTypes = { "Button", "Keyboard" };
        float typeButtonWidth = (width - 5) / 2;
        float typeButtonHeight = 28f;

        for (int i = 0; i < 2; i++)
        {
            var typeBounds = new SKRect(leftMargin + i * (typeButtonWidth + 5), y,
                leftMargin + i * (typeButtonWidth + 5) + typeButtonWidth, y + typeButtonHeight);

            if (i == 0) _keyboardOutput.BtnBounds = typeBounds;
            else _keyboardOutput.KeyBounds = typeBounds;

            bool selected = (i == 0 && !_keyboardOutput.IsKeyboard) || (i == 1 && _keyboardOutput.IsKeyboard);
            bool hovered = _keyboardOutput.HoveredOutputType == i;

            var bgColor = selected
                ? FUIColors.Active.WithAlpha(FUIColors.AlphaGlow)
                : (hovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);
            var textColor = selected ? FUIColors.Active : (hovered ? FUIColors.TextPrimary : FUIColors.TextDim);

            using var typeBgPaint = FUIRenderer.CreateFillPaint(bgColor);
            canvas.DrawRoundRect(typeBounds, 3, 3, typeBgPaint);

            using var typeFramePaint = FUIRenderer.CreateStrokePaint(selected ? FUIColors.Active : FUIColors.Frame, selected ? 2f : 1f);
            canvas.DrawRoundRect(typeBounds, 3, 3, typeFramePaint);

            FUIRenderer.DrawTextCentered(canvas, outputTypes[i], typeBounds, textColor, 14f);
        }
        y += typeButtonHeight + 4;

        // KEY COMBO section (only when Keyboard is selected)
        if (_keyboardOutput.IsKeyboard)
        {
            FUIWidgets.DrawSectionLabel(canvas, "KEY COMBO", leftMargin, ref y);

            float keyFieldHeight = 32f;
            _keyboardOutput.CaptureBounds = new SKRect(leftMargin, y, rightMargin, y + keyFieldHeight);

            // Check for key capture timeout
            if (_keyboardOutput.IsCapturing)
            {
                var elapsed = Environment.TickCount64 - _keyboardOutput.CaptureStartTicks;
                if (elapsed >= KeyCaptureTimeoutMs)
                {
                    _keyboardOutput.IsCapturing = false; // Timeout - cancel capture
                }
            }

            // Draw key capture field background
            var keyBgColor = _keyboardOutput.IsCapturing
                ? FUIColors.SelectionBg
                : (_keyboardOutput.CaptureHovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);

            using var keyBgPaint = FUIRenderer.CreateFillPaint(keyBgColor);
            canvas.DrawRoundRect(_keyboardOutput.CaptureBounds, 3, 3, keyBgPaint);

            // Draw timeout progress bar when capturing
            if (_keyboardOutput.IsCapturing)
            {
                var elapsed = Environment.TickCount64 - _keyboardOutput.CaptureStartTicks;
                float progress = Math.Min(1f, (float)(elapsed / KeyCaptureTimeoutMs));
                float remaining = 1f - progress;

                // Progress bar fills the field and shrinks from right to left
                float progressWidth = (_keyboardOutput.CaptureBounds.Width - 6) * remaining;
                if (progressWidth > 0)
                {
                    var progressRect = new SKRect(
                        _keyboardOutput.CaptureBounds.Left + 3,
                        _keyboardOutput.CaptureBounds.Top + 3,
                        _keyboardOutput.CaptureBounds.Left + 3 + progressWidth,
                        _keyboardOutput.CaptureBounds.Bottom - 3);
                    using var progressPaint = FUIRenderer.CreateFillPaint(FUIColors.SelectionBgStrong);
                    canvas.DrawRoundRect(progressRect, 2, 2, progressPaint);
                }
            }

            var keyFrameColor = _keyboardOutput.IsCapturing
                ? FUIColors.Active
                : (_keyboardOutput.CaptureHovered ? FUIColors.Primary : FUIColors.Frame);

            using var keyFramePaint = FUIRenderer.CreateStrokePaint(keyFrameColor, _keyboardOutput.IsCapturing ? 2f : 1f);
            canvas.DrawRoundRect(_keyboardOutput.CaptureBounds, 3, 3, keyFramePaint);

            // Display key combo or prompt
            if (_keyboardOutput.IsCapturing)
            {
                byte alpha = (byte)(180 + MathF.Sin(_ctx.PulsePhase * 3) * 60);
                FUIRenderer.DrawTextCentered(canvas, "Press key combo...", _keyboardOutput.CaptureBounds, FUIColors.Warning.WithAlpha(alpha), 14f);
            }
            else if (!string.IsNullOrEmpty(_keyboardOutput.SelectedKeyName))
            {
                // Draw keycaps centered in the field
                FUIWidgets.DrawKeycapsInBounds(canvas, _keyboardOutput.CaptureBounds, _keyboardOutput.SelectedKeyName, _keyboardOutput.SelectedModifiers);
            }
            else
            {
                FUIRenderer.DrawTextCentered(canvas, "Click to capture key", _keyboardOutput.CaptureBounds, FUIColors.TextDim, 14f);
            }
            y += keyFieldHeight + 4;
        }

        // Button Mode section
        // Modifier keys must stay in Normal mode ÔÇö the OS handles the modifier behaviour.
        bool isModifierKey = _keyboardOutput.IsKeyboard && IsModifierKeyName(_keyboardOutput.SelectedKeyName);

        FUIWidgets.DrawSectionLabel(canvas, "BUTTON MODE", leftMargin, ref y);

        // Mode buttons - all on one row
        string[] modes = { "Normal", "Toggle", "Pulse", "Hold" };
        float buttonHeight = 28f;  // 4px aligned, meets minimum touch target
        float buttonGap = 4f;
        float totalGap = buttonGap * (modes.Length - 1);
        float buttonWidth = (width - totalGap) / modes.Length;

        for (int i = 0; i < modes.Length; i++)
        {
            float buttonX = leftMargin + i * (buttonWidth + buttonGap);
            var modeBounds = new SKRect(buttonX, y, buttonX + buttonWidth, y + buttonHeight);

            if (isModifierKey)
            {
                // Disabled appearance — clear bounds so hover and click don't fire
                FUIRenderer.DrawRoundedPanel(canvas, modeBounds, FUIColors.Background2.WithAlpha(100), FUIColors.Frame.WithAlpha(100));

                FUIRenderer.DrawTextCentered(canvas, modes[i], modeBounds, FUIColors.TextDimSubtle, 12f);
                _buttonMode.ModeBounds[i] = SKRect.Empty;
            }
            else
            {
                bool selected = i == (int)_buttonMode.SelectedMode;
                bool hovered = i == _buttonMode.HoveredMode;

                SKColor bgColor = selected ? FUIColors.Active.WithAlpha(FUIColors.AlphaGlow) :
                    (hovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);

                using var modeBgPaint = FUIRenderer.CreateFillPaint(bgColor);
                canvas.DrawRoundRect(modeBounds, 3, 3, modeBgPaint);

                using var modeFramePaint = FUIRenderer.CreateStrokePaint(selected ? FUIColors.Active : FUIColors.Frame, selected ? 2f : 1f);
                canvas.DrawRoundRect(modeBounds, 3, 3, modeFramePaint);

                FUIRenderer.DrawTextCentered(canvas, modes[i], modeBounds,
                    FUIColors.ContentColor(selected), 12f);

                _buttonMode.ModeBounds[i] = modeBounds;
            }
        }
        y += buttonHeight + 4;

        // Duration slider for Pulse mode
        if (_buttonMode.SelectedMode == ButtonMode.Pulse && y + 50 < bottom)
        {
            FUIWidgets.DrawSectionLabel(canvas, "PULSE DURATION", leftMargin, ref y);

            float sliderHeight = 24f;
            _buttonMode.PulseSliderBounds = new SKRect(leftMargin, y, rightMargin, y + sliderHeight);

            float normalizedPulse = (_buttonMode.PulseDurationMs - 100f) / 900f;
            FUIWidgets.DrawDurationSlider(canvas, _buttonMode.PulseSliderBounds, normalizedPulse, _buttonMode.DraggingPulse);
            y += sliderHeight + 4;

            string pulseLabel = $"{_buttonMode.PulseDurationMs}ms";
            float pulseLabelW = FUIRenderer.MeasureText(pulseLabel, 12f);
            FUIRenderer.DrawText(canvas, pulseLabel,
                new SKPoint(rightMargin - pulseLabelW, y + 10), FUIColors.TextPrimary, 12f);
            y += 14 + 8;
        }

        // Duration slider for Hold mode
        if (_buttonMode.SelectedMode == ButtonMode.HoldToActivate && y + 50 < bottom)
        {
            FUIWidgets.DrawSectionLabel(canvas, "HOLD DURATION", leftMargin, ref y);

            float sliderHeight = 24f;
            _buttonMode.HoldSliderBounds = new SKRect(leftMargin, y, rightMargin, y + sliderHeight);

            float normalizedHold = (_buttonMode.HoldDurationMs - 200f) / 1800f;
            FUIWidgets.DrawDurationSlider(canvas, _buttonMode.HoldSliderBounds, normalizedHold, _buttonMode.DraggingHold);
            y += sliderHeight + 4;

            string holdLabel = $"{_buttonMode.HoldDurationMs}ms";
            float holdLabelW = FUIRenderer.MeasureText(holdLabel, 12f);
            FUIRenderer.DrawText(canvas, holdLabel,
                new SKPoint(rightMargin - holdLabelW, y + 10), FUIColors.TextPrimary, 12f);
            y += 14 + 8;
        }

        // Clear binding button
        if (y + 40 < bottom)
        {
            var clearBounds = new SKRect(leftMargin, y, rightMargin, y + 32);
            _clearAllButtonBounds = clearBounds;

            var state = _clearAllButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
            FUIRenderer.DrawButton(canvas, clearBounds, "CLEAR MAPPING", state, isDanger: true);
            y += 32;
        }

        // NET SWITCH section (only when network is enabled and in button category)
        if (_ctx.AppSettings.NetworkEnabled)
        {
            // Determine if this row is already the configured switch button
            bool isCurrentRowSwitchBtn = false;
            string switchDisplayName = "";
            var profile = _ctx.ProfileManager.ActiveProfile;
            var switchCfg = profile?.NetworkSwitchButton;
            if (switchCfg is not null && profile is not null &&
                _ctx.VJoyDevices.Count > _ctx.SelectedVJoyDeviceIndex)
            {
                var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
                int switchRowIndex = GetSwitchButtonRowIndex(profile, vjoyDevice.Id, switchCfg);
                isCurrentRowSwitchBtn = _selectedMappingRow == switchRowIndex;
                if (isCurrentRowSwitchBtn) switchDisplayName = switchCfg.DisplayName;
            }

            // Amber banner with × — shown when this row IS the net switch button
            if (isCurrentRowSwitchBtn)
            {
                y += 8;
                var bannerRect = new SKRect(leftMargin, y, rightMargin, y + 32);
                FUIRenderer.DrawRoundedPanel(canvas, bannerRect,
                    FUIColors.Warning.WithAlpha(FUIColors.AlphaLightTint),
                    FUIColors.Warning.WithAlpha(FUIColors.AlphaBorderSoft));
                FUIRenderer.DrawText(canvas, "TX TOGGLE: " + switchDisplayName,
                    new SKPoint(bannerRect.Left + 10f, bannerRect.MidY + 5f), FUIColors.Warning, 13f);

                const float xSize = 16f;
                var xBounds = new SKRect(bannerRect.Right - xSize - 6f, bannerRect.MidY - xSize / 2f,
                    bannerRect.Right - 6f, bannerRect.MidY + xSize / 2f);
                _netSwitch.BadgeBounds = bannerRect;
                _netSwitch.BadgeXBounds = xBounds;
                _netSwitch.BadgeXHovered = xBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
                using var xPaint = FUIRenderer.CreateTextPaint(
                    _netSwitch.BadgeXHovered ? FUIColors.TextBright : FUIColors.Warning.WithAlpha(200), 12f);
                canvas.DrawText("\u00D7", xBounds.MidX - 3f, xBounds.MidY + 5f, xPaint);
            }

            // SET AS TX TOGGLE / TX TOGGLE ACTIVE — anchored to panel bottom
            var netBounds = new SKRect(leftMargin, bottom - 32, rightMargin, bottom);
            _netSwitch.ActionBounds = isCurrentRowSwitchBtn ? SKRect.Empty : netBounds;
            _netSwitch.ActionHovered = !isCurrentRowSwitchBtn &&
                netBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

            if (isCurrentRowSwitchBtn)
            {
                FUIRenderer.DrawButton(canvas, netBounds, "TX TOGGLE ACTIVE",
                    FUIRenderer.ButtonState.Disabled);
            }
            else
            {
                FUIRenderer.DrawButton(canvas, netBounds, "SET AS TX TOGGLE",
                    _netSwitch.ActionHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
            }
        }
    }

    /// <summary>
    /// Format key combo for display as simple text (used in mapping names)
    /// </summary>
    private static string FormatKeyComboForDisplay(string keyName, List<string>? modifiers)
    {
        if (string.IsNullOrEmpty(keyName)) return "";

        var parts = new List<string>();
        if (modifiers is not null && modifiers.Count > 0)
        {
            parts.AddRange(modifiers);
        }
        parts.Add(keyName);
        return string.Join("+", parts);
    }

}