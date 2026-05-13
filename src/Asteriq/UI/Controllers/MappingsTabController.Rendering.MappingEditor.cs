using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
    private void DrawMappingEditorPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background1.WithAlpha(160));
        canvas.DrawRect(bounds.Inset(frameInset, frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Active, 30f, 8f);

        float y = bounds.Top + frameInset + 16;
        float leftMargin = bounds.Left + frameInset + 16;
        float rightMargin = bounds.Right - frameInset - 16;

        // Title
        string outputName = GetEditingOutputName();
        FUIRenderer.DrawText(canvas, $"EDIT: {outputName}", new SKPoint(leftMargin, y),
            FUIColors.Active, 14f, true);
        y += 30;

        // INPUT SOURCE section
        FUIRenderer.DrawText(canvas, "INPUT SOURCE", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        y += 20;

        // Input field - double-click to listen for input
        float inputFieldHeight = 36f;
        _inputDetection.FieldBounds = new SKRect(leftMargin, y, rightMargin, y + inputFieldHeight);
        DrawInputField(canvas, _inputDetection.FieldBounds);
        y += inputFieldHeight + 10;

        // Manual entry toggle button
        _inputDetection.ManualEntryBounds = new SKRect(leftMargin, y, leftMargin + 120, y + 24);
        FUIWidgets.DrawToggleButton(canvas, _inputDetection.ManualEntryBounds, "Manual Entry", _inputDetection.ManualEntryMode, false);
        y += 34;

        // Manual entry dropdowns (if enabled)
        if (_inputDetection.ManualEntryMode)
        {
            y = DrawManualEntrySection(canvas, bounds, y, leftMargin, rightMargin);
        }

        // Output type and button mode section (only for button outputs)
        if (!_isEditingAxis)
        {
            // Output type selector (Button vs Keyboard)
            y += 10;
            FUIRenderer.DrawText(canvas, "OUTPUT TYPE", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
            y += 20;
            DrawOutputTypeSelector(canvas, leftMargin, y, rightMargin - leftMargin);
            y += 38;

            // Key capture field (only when Keyboard is selected)
            if (_keyboardOutput.IsKeyboard)
            {
                FUIRenderer.DrawText(canvas, "KEY", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
                y += 20;
                float keyFieldHeight = 32f;
                _keyboardOutput.CaptureBounds = new SKRect(leftMargin, y, rightMargin, y + keyFieldHeight);
                DrawKeyCapture(canvas, _keyboardOutput.CaptureBounds);
                y += keyFieldHeight + 10;
            }

            // Button mode selector (disabled for modifier keys)
            bool editIsModifier = _keyboardOutput.IsKeyboard && IsModifierKeyName(_keyboardOutput.SelectedKeyName);
            y += 10;
            FUIRenderer.DrawText(canvas, "BUTTON MODE", new SKPoint(leftMargin, y),
                editIsModifier ? FUIColors.TextDim.WithAlpha(60) : FUIColors.TextDim, 13f);
            y += 20;
            DrawButtonModeSelector(canvas, leftMargin, y, rightMargin - leftMargin, editIsModifier);
            y += 40;
        }

        // Action buttons at bottom
        float buttonWidth = 80f;
        float buttonHeight = 32f;
        float buttonY = bounds.Bottom - frameInset - buttonHeight - 16;

        _cancelButtonBounds = new SKRect(rightMargin - buttonWidth * 2 - 10, buttonY,
            rightMargin - buttonWidth - 10, buttonY + buttonHeight);
        _saveButtonBounds = new SKRect(rightMargin - buttonWidth, buttonY,
            rightMargin, buttonY + buttonHeight);

        FUIWidgets.DrawActionButton(canvas, _cancelButtonBounds, "Cancel", _cancelButtonHovered, false);
        FUIWidgets.DrawActionButton(canvas, _saveButtonBounds, "Save", _saveButtonHovered, true);
    }

    private string GetEditingOutputName()
    {
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count)
            return "Unknown";

        if (_isEditingAxis)
        {
            string[] axisNames = { "X Axis", "Y Axis", "Z Axis", "RX Axis", "RY Axis", "RZ Axis", "Slider 1", "Slider 2" };
            int axisIndex = AxisIndexForRow(_editingRowIndex);
            return axisIndex >= 0 && axisIndex < axisNames.Length ? axisNames[axisIndex] : $"Axis {_editingRowIndex}";
        }
        else
        {
            return $"Button {_editingRowIndex + 1}";
        }
    }

    private void DrawInputField(SKCanvas canvas, SKRect bounds)
    {
        // Background
        var bgColor = _inputDetection.IsListening
            ? FUIColors.WarningTint
            : FUIColors.Background2;

        using var bgPaint = FUIRenderer.CreateFillPaint(bgColor);
        canvas.DrawRect(bounds, bgPaint);

        // Frame
        var frameColor = _inputDetection.IsListening
            ? FUIColors.Warning
            : FUIColors.Frame;
        using var framePaint = FUIRenderer.CreateStrokePaint(frameColor, _inputDetection.IsListening ? 2f : 1f);
        canvas.DrawRect(bounds, framePaint);

        // Text content
        float textY = bounds.MidY + 5;
        if (_inputDetection.IsListening)
        {
            byte alpha = (byte)(180 + MathF.Sin(_ctx.PulsePhase * 3) * 60);
            FUIRenderer.DrawText(canvas, "Press a button or move an axis...",
                new SKPoint(bounds.Left + 10, textY), FUIColors.Warning.WithAlpha(alpha), 15f);
        }
        else if (_inputDetection.PendingInput is not null)
        {
            FUIRenderer.DrawText(canvas, _inputDetection.PendingInput.ToString(),
                new SKPoint(bounds.Left + 10, textY), FUIColors.TextBright, 15f);
        }
        else
        {
            FUIRenderer.DrawText(canvas, "Double-click to detect input",
                new SKPoint(bounds.Left + 10, textY), FUIColors.TextDisabled, 15f);
        }

        // Clear button if there's input
        if (_inputDetection.PendingInput is not null && !_inputDetection.IsListening)
        {
            var clearBounds = new SKRect(bounds.Right - 28, bounds.Top + 6, bounds.Right - 6, bounds.Bottom - 6);
            FUIWidgets.DrawSmallIconButton(canvas, clearBounds, "X", false, true);
        }
    }

    private float DrawManualEntrySection(SKCanvas canvas, SKRect bounds, float y, float leftMargin, float rightMargin)
    {
        // Device dropdown
        FUIRenderer.DrawText(canvas, "Device:", new SKPoint(leftMargin, y + 12), FUIColors.TextDim, 13f);
        float dropdownX = leftMargin + 55;
        _inputDetection.DeviceDropdownBounds = new SKRect(dropdownX, y, rightMargin, y + 28);
        string deviceText = _ctx.Devices.Count > 0 && _inputDetection.SelectedSourceDevice < _ctx.Devices.Count
            ? _ctx.Devices[_inputDetection.SelectedSourceDevice].Name
            : "No devices";
        FUIWidgets.DrawDropdown(canvas, _inputDetection.DeviceDropdownBounds, deviceText, _inputDetection.DeviceDropdownOpen);
        y += 36;

        // Control dropdown
        string controlLabel = _isEditingAxis ? "Axis:" : "Button:";
        FUIRenderer.DrawText(canvas, controlLabel, new SKPoint(leftMargin, y + 12), FUIColors.TextDim, 13f);
        _inputDetection.ControlDropdownBounds = new SKRect(dropdownX, y, rightMargin, y + 28);
        string controlText = GetControlDropdownText();
        FUIWidgets.DrawDropdown(canvas, _inputDetection.ControlDropdownBounds, controlText, _inputDetection.ControlDropdownOpen);
        y += 36;

        // Draw dropdown lists if open
        if (_inputDetection.DeviceDropdownOpen)
        {
            DrawDeviceDropdownList(canvas, _inputDetection.DeviceDropdownBounds);
        }
        else if (_inputDetection.ControlDropdownOpen)
        {
            DrawControlDropdownList(canvas, _inputDetection.ControlDropdownBounds);
        }

        return y;
    }

    private string GetControlDropdownText()
    {
        if (_ctx.Devices.Count == 0 || _inputDetection.SelectedSourceDevice >= _ctx.Devices.Count)
            return "ÔÇö";

        var device = _ctx.Devices[_inputDetection.SelectedSourceDevice];
        if (_isEditingAxis)
        {
            int axisCount = 8; // Typical axis count
            if (_inputDetection.SelectedSourceControl < axisCount)
                return $"Axis {_inputDetection.SelectedSourceControl}";
        }
        else
        {
            if (_inputDetection.SelectedSourceControl < 128)
                return $"Button {_inputDetection.SelectedSourceControl + 1}";
        }
        return "ÔÇö";
    }

    private void DrawDeviceDropdownList(SKCanvas canvas, SKRect anchorBounds)
    {
        float itemHeight = 28f;  // 4px aligned
        float listHeight = Math.Min(_ctx.Devices.Count * itemHeight, 200);
        var listBounds = new SKRect(anchorBounds.Left, anchorBounds.Bottom + 2,
            anchorBounds.Right, anchorBounds.Bottom + 2 + listHeight);

        // Draw shadow/backdrop for visual separation
        using var shadowPaint = FUIRenderer.CreateFillPaint(SKColors.Black.WithAlpha(120));
        var shadowBounds = new SKRect(listBounds.Left - 1, listBounds.Top - 1, listBounds.Right + 5, listBounds.Bottom + 5);
        canvas.DrawRect(shadowBounds, shadowPaint);

        // Solid opaque background
        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background1);
        canvas.DrawRect(listBounds, bgPaint);

        // Draw items
        float y = listBounds.Top;
        for (int i = 0; i < _ctx.Devices.Count && y < listBounds.Bottom; i++)
        {
            var itemBounds = new SKRect(listBounds.Left, y, listBounds.Right, y + itemHeight);
            bool hovered = i == _inputDetection.HoveredDeviceIndex;

            if (hovered)
            {
                using var hoverPaint = FUIRenderer.CreateFillPaint(FUIColors.Primary.WithAlpha(60));
                canvas.DrawRect(itemBounds, hoverPaint);
            }

            FUIRenderer.DrawText(canvas, _ctx.Devices[i].Name, new SKPoint(itemBounds.Left + 8, itemBounds.MidY + 4),
                hovered ? FUIColors.TextBright : FUIColors.TextPrimary, 14f);
            y += itemHeight;
        }

        // Frame on top
        using var framePaint = FUIRenderer.CreateStrokePaint(FUIColors.Primary);
        canvas.DrawRect(listBounds, framePaint);
    }

    private void DrawControlDropdownList(SKCanvas canvas, SKRect anchorBounds)
    {
        int controlCount = _isEditingAxis ? 8 : 32; // Show first 8 axes or 32 buttons
        float itemHeight = 24f;
        float listHeight = Math.Min(controlCount * itemHeight, 200);
        var listBounds = new SKRect(anchorBounds.Left, anchorBounds.Bottom + 2,
            anchorBounds.Right, anchorBounds.Bottom + 2 + listHeight);

        // Draw shadow/backdrop for visual separation
        using var shadowPaint = FUIRenderer.CreateFillPaint(SKColors.Black.WithAlpha(120));
        var shadowBounds = new SKRect(listBounds.Left - 1, listBounds.Top - 1, listBounds.Right + 5, listBounds.Bottom + 5);
        canvas.DrawRect(shadowBounds, shadowPaint);

        // Solid opaque background
        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background1);
        canvas.DrawRect(listBounds, bgPaint);

        // Draw items
        float y = listBounds.Top;
        for (int i = 0; i < controlCount && y < listBounds.Bottom; i++)
        {
            var itemBounds = new SKRect(listBounds.Left, y, listBounds.Right, y + itemHeight);
            bool hovered = i == _inputDetection.HoveredControlIndex;

            if (hovered)
            {
                using var hoverPaint = FUIRenderer.CreateFillPaint(FUIColors.Primary.WithAlpha(60));
                canvas.DrawRect(itemBounds, hoverPaint);
            }

            string name = _isEditingAxis ? $"Axis {i}" : $"Button {i + 1}";
            FUIRenderer.DrawText(canvas, name, new SKPoint(itemBounds.Left + 8, itemBounds.MidY + 4),
                hovered ? FUIColors.TextBright : FUIColors.TextPrimary, 14f);
            y += itemHeight;
        }

        // Frame on top
        using var framePaint = FUIRenderer.CreateStrokePaint(FUIColors.Primary);
        canvas.DrawRect(listBounds, framePaint);
    }

    private void DrawButtonModeSelector(SKCanvas canvas, float x, float y, float width, bool isModifier = false)
    {
        ButtonMode[] modes = { ButtonMode.Normal, ButtonMode.Toggle, ButtonMode.Pulse, ButtonMode.HoldToActivate };
        string[] labels = { "Normal", "Toggle", "Pulse", "Hold" };
        float buttonWidth = (width - 16) / 4;
        float buttonHeight = 28f;

        for (int i = 0; i < modes.Length; i++)
        {
            var modeBounds = new SKRect(x + i * (buttonWidth + 5), y, x + i * (buttonWidth + 5) + buttonWidth, y + buttonHeight);

            if (isModifier)
            {
                // Disabled appearance ÔÇö clear bounds so hover and click don't fire
                using var disabledBgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background2.WithAlpha(100));
                canvas.DrawRect(modeBounds, disabledBgPaint);

                using var disabledFramePaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame.WithAlpha(100));
                canvas.DrawRect(modeBounds, disabledFramePaint);

                FUIRenderer.DrawTextCentered(canvas, labels[i], modeBounds, FUIColors.TextDimSubtle, 13f);
                _buttonMode.ModeBounds[i] = SKRect.Empty;
            }
            else
            {
                _buttonMode.ModeBounds[i] = modeBounds;

                bool selected = _buttonMode.SelectedMode == modes[i];
                bool hovered = _buttonMode.HoveredMode == i;

                var bgColor = selected
                    ? FUIColors.Active.WithAlpha(FUIColors.AlphaGlow)
                    : (hovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);
                var textColor = selected ? FUIColors.Active : (hovered ? FUIColors.TextPrimary : FUIColors.TextDim);

                using var bgPaint = FUIRenderer.CreateFillPaint(bgColor);
                canvas.DrawRect(modeBounds, bgPaint);

                using var framePaint = FUIRenderer.CreateStrokePaint(selected ? FUIColors.Active : FUIColors.Frame, selected ? 2f : 1f);
                canvas.DrawRect(modeBounds, framePaint);

                FUIRenderer.DrawTextCentered(canvas, labels[i], modeBounds, textColor, 13f);
            }
        }
    }

    private void DrawOutputTypeSelector(SKCanvas canvas, float x, float y, float width)
    {
        string[] labels = { "Button", "Keyboard" };
        float buttonWidth = (width - 5) / 2;
        float buttonHeight = 28f;

        for (int i = 0; i < 2; i++)
        {
            var typeBounds = new SKRect(x + i * (buttonWidth + 5), y, x + i * (buttonWidth + 5) + buttonWidth, y + buttonHeight);
            if (i == 0) _keyboardOutput.BtnBounds = typeBounds;
            else _keyboardOutput.KeyBounds = typeBounds;

            bool selected = (i == 0 && !_keyboardOutput.IsKeyboard) || (i == 1 && _keyboardOutput.IsKeyboard);
            bool hovered = _keyboardOutput.HoveredOutputType == i;

            var bgColor = selected
                ? FUIColors.Active.WithAlpha(FUIColors.AlphaGlow)
                : (hovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);
            var textColor = selected ? FUIColors.Active : (hovered ? FUIColors.TextPrimary : FUIColors.TextDim);

            using var bgPaint = FUIRenderer.CreateFillPaint(bgColor);
            canvas.DrawRect(typeBounds, bgPaint);

            using var framePaint = FUIRenderer.CreateStrokePaint(selected ? FUIColors.Active : FUIColors.Frame, selected ? 2f : 1f);
            canvas.DrawRect(typeBounds, framePaint);

            FUIRenderer.DrawTextCentered(canvas, labels[i], typeBounds, textColor, 14f);
        }
    }

    private void DrawKeyCapture(SKCanvas canvas, SKRect bounds)
    {
        // Background
        var bgColor = _keyboardOutput.IsCapturing
            ? FUIColors.WarningTint
            : (_keyboardOutput.CaptureHovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);

        using var bgPaint = FUIRenderer.CreateFillPaint(bgColor);
        canvas.DrawRect(bounds, bgPaint);

        // Frame
        var frameColor = _keyboardOutput.IsCapturing
            ? FUIColors.Warning
            : (_keyboardOutput.CaptureHovered ? FUIColors.Primary : FUIColors.Frame);
        using var framePaint = FUIRenderer.CreateStrokePaint(frameColor, _keyboardOutput.IsCapturing ? 2f : 1f);
        canvas.DrawRect(bounds, framePaint);

        // Text content
        float textY = bounds.MidY + 5;
        if (_keyboardOutput.IsCapturing)
        {
            byte alpha = (byte)(180 + MathF.Sin(_ctx.PulsePhase * 3) * 60);
            FUIRenderer.DrawText(canvas, "Press a key...",
                new SKPoint(bounds.Left + 10, textY), FUIColors.Warning.WithAlpha(alpha), 15f);
        }
        else if (!string.IsNullOrEmpty(_keyboardOutput.SelectedKeyName))
        {
            FUIRenderer.DrawText(canvas, _keyboardOutput.SelectedKeyName,
                new SKPoint(bounds.Left + 10, textY), FUIColors.TextBright, 15f);
        }
        else
        {
            FUIRenderer.DrawText(canvas, "Click to capture key",
                new SKPoint(bounds.Left + 10, textY), FUIColors.TextDisabled, 15f);
        }

        // Clear button if there's a key
        if (!string.IsNullOrEmpty(_keyboardOutput.SelectedKeyName) && !_keyboardOutput.IsCapturing)
        {
            _keyboardOutput.ClearBounds = new SKRect(bounds.Right - 28, bounds.Top + 6, bounds.Right - 6, bounds.Bottom - 6);
            FUIWidgets.DrawSmallIconButton(canvas, _keyboardOutput.ClearBounds, "X", _keyboardOutput.ClearHovered, true);
        }
        else
        {
            _keyboardOutput.ClearBounds = SKRect.Empty;
        }
    }

    private void DrawOutputMappingList(SKCanvas canvas, SKRect bounds)
    {
        _mappingRowBounds.Clear();
        _mappingAddButtonBounds.Clear();
        _mappingRemoveButtonBounds.Clear();

        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count)
        {
            FUIRenderer.DrawText(canvas, "No vJoy devices available",
                new SKPoint(bounds.Left + 20, bounds.Top + 20), FUIColors.TextDim, 15f);
            FUIRenderer.DrawText(canvas, "Install vJoy driver to create mappings",
                new SKPoint(bounds.Left + 20, bounds.Top + 40), FUIColors.TextDisabled, 14f);
            return;
        }

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        var profile = _ctx.ProfileManager.ActiveProfile;

        float rowHeight = 32f;
        float rowGap = 4f;
        float y = bounds.Top;
        int rowIndex = 0;

        // Section: AXES
        FUIRenderer.DrawText(canvas, "AXES", new SKPoint(bounds.Left + 5, y + 14), FUIColors.Active, 14f);
        y += 20;

        string[] axisNames = { "X Axis", "Y Axis", "Z Axis", "RX Axis", "RY Axis", "RZ Axis", "Slider 1", "Slider 2" };
        for (int i = 0; i < Math.Min(axisNames.Length, 8); i++)
        {
            if (y + rowHeight > bounds.Bottom) break;

            var rowBounds = new SKRect(bounds.Left, y, bounds.Right, y + rowHeight);
            string binding = GetAxisBindingText(profile, vjoyDevice.Id, i);
            bool isSelected = rowIndex == _selectedMappingRow;
            bool isHovered = rowIndex == _hoveredMappingRow;
            bool isEditing = _mappingEditorOpen && rowIndex == _editingRowIndex;

            DrawMappingRow(canvas, rowBounds, axisNames[i], binding, isSelected, isHovered, isEditing, rowIndex, !string.IsNullOrEmpty(binding) && binding != "ÔÇö");

            _mappingRowBounds.Add(rowBounds);
            y += rowHeight + rowGap;
            rowIndex++;
        }

        // Section: BUTTONS
        y += 10;
        if (y + 20 < bounds.Bottom)
        {
            FUIRenderer.DrawText(canvas, "BUTTONS", new SKPoint(bounds.Left + 5, y + 14), FUIColors.Active, 14f);
            y += 20;
        }

        for (int i = 0; i < vjoyDevice.ButtonCount && y + rowHeight <= bounds.Bottom; i++)
        {
            var rowBounds = new SKRect(bounds.Left, y, bounds.Right, y + rowHeight);
            string binding = GetButtonBindingText(profile, vjoyDevice.Id, i);
            bool isSelected = rowIndex == _selectedMappingRow;
            bool isHovered = rowIndex == _hoveredMappingRow;
            bool isEditing = _mappingEditorOpen && rowIndex == _editingRowIndex;

            DrawMappingRow(canvas, rowBounds, $"Button {i + 1}", binding, isSelected, isHovered, isEditing, rowIndex, !string.IsNullOrEmpty(binding) && binding != "ÔÇö");

            _mappingRowBounds.Add(rowBounds);
            y += rowHeight + rowGap;
            rowIndex++;
        }
    }

    /// <summary>
    /// Auto-derives whether the (vjoyId, axisIndex) slot is a merged-away secondary slot of
    /// some merge in the profile. For every input of every merge we compute its "natural
    /// slot" — the vJoy slot whose VJoyPrimaryDevices entry matches the input's physical
    /// device, at the same axis index. The input that lands ON the merge target's own slot
    /// is the primary; every other input's natural slot is a merged-away secondary.
    /// Returns the merge target + the input that landed here, or null when the slot
    /// doesn't qualify (no matching merge, or the slot is mapped independently to a
    /// different input — we don't hijack legit mappings).
    /// </summary>
    private static (AxisMapping Target, InputSource SecondaryInput)? GetMergedAwayInfo(MappingProfile? profile, uint vjoyId, int axisIndex)
    {
        if (profile is null) return null;

        var slot = profile.AxisMappings.FirstOrDefault(m =>
            m.Output.Type == OutputType.VJoyAxis &&
            m.Output.VJoyDevice == vjoyId &&
            m.Output.Index == axisIndex);

        foreach (var merge in profile.AxisMappings)
        {
            if (merge.Inputs.Count < 2) continue;
            if (merge.Output.Type != OutputType.VJoyAxis) continue;
            if (merge.Output.VJoyDevice == vjoyId && merge.Output.Index == axisIndex)
                continue; // skip merge target itself

            foreach (var input in merge.Inputs)
            {
                // Natural slot for this physical input = (its primary vJoy device, its axis index).
                uint? naturalVJoy = null;
                foreach (var kv in profile.VJoyPrimaryDevices)
                {
                    if (kv.Value == input.DeviceId) { naturalVJoy = kv.Key; break; }
                }
                if (naturalVJoy is null) continue;

                // Skip the input that IS at the merge target — that one is the primary, not
                // a merged-away secondary. (Robust whether the user added inputs in primary-
                // first order or any other order.)
                if (naturalVJoy == merge.Output.VJoyDevice && input.Index == merge.Output.Index)
                    continue;

                if (naturalVJoy != vjoyId) continue;
                if (input.Index != axisIndex) continue;

                // Slot is the merged-away secondary. Mark it iff the slot is empty OR mapped
                // solo to the same physical input (a parallel single mapping rendered
                // redundant by the merge). Otherwise the slot is doing something independent.
                if (slot is null || slot.Inputs.Count == 0)
                    return (merge, input);
                if (slot.Inputs.Count == 1 &&
                    slot.Inputs[0].DeviceId == input.DeviceId &&
                    slot.Inputs[0].Type == input.Type &&
                    slot.Inputs[0].Index == input.Index)
                    return (merge, input);
                return null;
            }
        }

        return null;
    }

}