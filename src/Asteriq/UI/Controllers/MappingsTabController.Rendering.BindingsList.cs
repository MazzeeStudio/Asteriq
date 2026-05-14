using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
    private void DrawBindingsPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        float sideTabWidth = FUIRenderer.SideTabWidth;

        // Panel shadow
        FUIRenderer.DrawPanelShadow(canvas, bounds, 3f, 3f, 10f);

        // Panel background (shifted right to make room for side tabs)
        var contentBounds = new SKRect(bounds.Left + frameInset + sideTabWidth, bounds.Top + frameInset,
                                        bounds.Right - frameInset, bounds.Bottom - frameInset);
        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background1.WithAlpha(140));
        canvas.DrawRect(contentBounds, bgPaint);

        // Draw vertical side tabs (M1 Axes, M2 Buttons)
        DrawMappingCategorySideTabs(canvas, bounds.Left + frameInset, bounds.Top + frameInset,
            sideTabWidth, bounds.Height - frameInset * 2);

        // L-corner frame (adjusted for side tabs)
        var frameBounds = new SKRect(bounds.Left + sideTabWidth, bounds.Top, bounds.Right, bounds.Bottom);
        FUIRenderer.DrawLCornerFrame(canvas, frameBounds, FUIColors.Frame, 30f, 8f);

        float y = contentBounds.Top + 10;
        float leftMargin = contentBounds.Left + 10;
        float rightMargin = contentBounds.Right - 10;

        // Header with category code
        string categoryCode = _mappingCategory == 0 ? "M1" : "M2";
        string categoryName = "VJOY MAPPINGS";
        FUIRenderer.DrawText(canvas, categoryCode, new SKPoint(leftMargin, y + 12), FUIColors.Active, 15f);
        FUIRenderer.DrawText(canvas, categoryName, new SKPoint(leftMargin + 30, y + 12), FUIColors.TextBright, 17f, true);
        y += 30;

        // vJoy device selector: [<] vJoy Device 1 [>]
        float arrowButtonSize = 28f;
        _vjoyPrevButtonBounds = new SKRect(leftMargin, y, leftMargin + arrowButtonSize, y + arrowButtonSize);
        FUIWidgets.DrawArrowButton(canvas, _vjoyPrevButtonBounds, "<", _vjoyPrevHovered, _ctx.SelectedVJoyDeviceIndex > 0);

        string deviceName = _ctx.VJoyDevices.Count > 0 && _ctx.SelectedVJoyDeviceIndex < _ctx.VJoyDevices.Count
            ? $"vJoy Device {_ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex].Id}"
            : "No vJoy Devices";
        // Center the device name between the two arrow buttons
        var labelBounds = new SKRect(leftMargin + arrowButtonSize, y, rightMargin - arrowButtonSize, y + arrowButtonSize);
        FUIRenderer.DrawTextCentered(canvas, deviceName, labelBounds, FUIColors.TextBright, 15f);

        _vjoyNextButtonBounds = new SKRect(rightMargin - arrowButtonSize, y, rightMargin, y + arrowButtonSize);
        FUIWidgets.DrawArrowButton(canvas, _vjoyNextButtonBounds, ">", _vjoyNextHovered, _ctx.SelectedVJoyDeviceIndex < _ctx.VJoyDevices.Count - 1);
        y += arrowButtonSize + 6;

        // Scrollable binding rows (filtered by category)
        float listBottom = contentBounds.Bottom - 10;
        DrawBindingsList(canvas, new SKRect(leftMargin - 5, y, rightMargin + 5, listBottom));
    }

    private void DrawMappingCategorySideTabs(SKCanvas canvas, float x, float y, float width, float height)
    {
        // Style matching Device category tabs: narrow vertical tabs with text reading bottom-to-top
        float tabHeight = 80f;
        float tabGap = 4f;

        // Calculate total tabs height and start from bottom of available space
        float totalTabsHeight = tabHeight * 2 + tabGap;
        float startY = y + height - totalTabsHeight - 10f;

        // M1 Buttons tab (bottom)
        var buttonsBounds = new SKRect(x, startY + tabHeight + tabGap, x + width, startY + tabHeight * 2 + tabGap);
        _mappingCategoryButtonsBounds = buttonsBounds;
        FUIWidgets.DrawVerticalSideTab(canvas, buttonsBounds, "BUTTONS_01", _mappingCategory == 0, _hoveredMappingCategory == 0);

        // M2 Axes tab (above M1)
        var axesBounds = new SKRect(x, startY, x + width, startY + tabHeight);
        _mappingCategoryAxesBounds = axesBounds;
        FUIWidgets.DrawVerticalSideTab(canvas, axesBounds, "AXES_02", _mappingCategory == 1, _hoveredMappingCategory == 1);
    }

    private void DrawBindingsList(SKCanvas canvas, SKRect bounds)
    {
        _mappingRowBounds.Clear();
        _mappingAddButtonBounds.Clear();
        _mappingRemoveButtonBounds.Clear();
        _listScroll.ListBounds = bounds;

        var profile = _ctx.ProfileManager.ActiveProfile;

        bool hasVJoy = _ctx.VJoyDevices.Count > 0 && _ctx.SelectedVJoyDeviceIndex < _ctx.VJoyDevices.Count;
        VJoyDeviceInfo? vjoyDevice = hasVJoy ? _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex] : null;

        float rowHeight = 32f;  // Compact rows
        float rowGap = 4f;

        // Get counts based on current category
        string[] axisNames = { "X Axis", "Y Axis", "Z Axis", "RX Axis", "RY Axis", "RZ Axis", "Slider 1", "Slider 2" };
        _visibleAxisIndices = hasVJoy ? GetVJoyAxisIndices(vjoyDevice!) : new List<int>();
        int axisCount = _visibleAxisIndices.Count;
        int buttonCount = vjoyDevice?.ButtonCount ?? 0;

        // Calculate content height based on selected category (no section headers when filtered)
        // Category 0 = Buttons, Category 1 = Axes
        int itemCount = _mappingCategory == 0 ? buttonCount : axisCount;
        _listScroll.ContentHeight = itemCount * (rowHeight + rowGap);

        // Clamp scroll offset
        float maxScroll = Math.Max(0, _listScroll.ContentHeight - bounds.Height);
        _listScroll.ScrollOffset = Math.Clamp(_listScroll.ScrollOffset, 0, maxScroll);

        // Set up clipping
        canvas.Save();
        canvas.ClipRect(bounds);

        float y = bounds.Top - _listScroll.ScrollOffset;
        int rowIndex = 0;

        // Pre-compute NET SWITCH row index for this vJoy device (only for button category)
        var switchCfg = profile?.NetworkSwitchButton;
        int switchRowIndex = -1;
        if (switchCfg is not null && _mappingCategory == 0 && vjoyDevice is not null)
            switchRowIndex = GetSwitchButtonRowIndex(profile!, vjoyDevice.Id, switchCfg);

        // Show BUTTONS when category is 0
        if (_mappingCategory == 0 && hasVJoy && buttonCount > 0)
        {
            for (int i = 0; i < buttonCount; i++)
            {
                float rowTop = y;
                float rowBottom = y + rowHeight;

                // Only draw if visible
                if (rowBottom > bounds.Top && rowTop < bounds.Bottom)
                {
                    var rowBounds = new SKRect(bounds.Left, rowTop, bounds.Right, rowBottom);
                    string binding = GetButtonBindingText(profile, vjoyDevice!.Id, i);
                    var keyParts = GetButtonKeyParts(profile, vjoyDevice!.Id, i);
                    bool isSelected = rowIndex == _selectedMappingRow;
                    bool isHovered = rowIndex == _hoveredMappingRow;
                    bool isModifier = keyParts?.Count == 1 && IsModifierKeyName(keyParts[0]);
                    bool isSwitchBtn = rowIndex == switchRowIndex;
                    bool isShared = GetSharedSlotInfos(vjoyDevice!.Id, i).Count > 0;

                    DrawChunkyBindingRow(canvas, rowBounds, $"Button {i + 1}", binding, isSelected, isHovered, keyParts, isModifier, isSwitchBtn, isShared);
                    _mappingRowBounds.Add(rowBounds);
                }
                else
                {
                    _mappingRowBounds.Add(new SKRect(bounds.Left, rowTop, bounds.Right, rowBottom));
                }

                y += rowHeight + rowGap;
                rowIndex++;
            }
        }

        // Show AXES when category is 1
        if (_mappingCategory == 1 && hasVJoy && axisCount > 0)
        {
            for (int vi = 0; vi < axisCount; vi++)
            {
                int axisIdx = _visibleAxisIndices[vi];
                float rowTop = y;
                float rowBottom = y + rowHeight;

                // Only draw if visible
                if (rowBottom > bounds.Top && rowTop < bounds.Bottom)
                {
                    var rowBounds = new SKRect(bounds.Left, rowTop, bounds.Right, rowBottom);
                    string binding = GetAxisBindingText(profile, vjoyDevice!.Id, axisIdx);
                    bool isSelected = rowIndex == _selectedMappingRow;
                    bool isHovered = rowIndex == _hoveredMappingRow;
                    bool isMergedAway = GetMergedAwayInfo(profile, vjoyDevice!.Id, axisIdx) is not null;

                    DrawChunkyBindingRow(canvas, rowBounds, axisNames[axisIdx], binding, isSelected, isHovered,
                        isMergedAway: isMergedAway);
                    _mappingRowBounds.Add(rowBounds);
                }
                else
                {
                    // Add placeholder bounds for hit testing even when not visible
                    _mappingRowBounds.Add(new SKRect(bounds.Left, rowTop, bounds.Right, rowBottom));
                }

                y += rowHeight + rowGap;
                rowIndex++;
            }
        }

        canvas.Restore();

        // Draw scroll indicator if content overflows
        if (_listScroll.ContentHeight > bounds.Height)
        {
            float trackHeight = bounds.Height - 20;
            float trackX = bounds.Right + 8;
            float trackTop = bounds.Top + 10;
            float trackWidth = 3f;
            var trackBounds = new SKRect(trackX, trackTop, trackX + trackWidth, trackTop + trackHeight);
            FUIWidgets.DrawScrollIndicator(canvas, trackBounds, _listScroll.ScrollOffset,
                _listScroll.ContentHeight, bounds.Height);
        }
    }


    /// <summary>
    /// Get the keyboard key parts for a button mapping (modifiers + key as separate items)
    /// </summary>
    private static List<string>? GetKeyboardMappingParts(ButtonMapping mapping)
    {
        var output = mapping.Output;
        if (string.IsNullOrEmpty(output.KeyName)) return null;

        var parts = new List<string>();
        if (output.Modifiers is not null && output.Modifiers.Count > 0)
        {
            parts.AddRange(output.Modifiers);
        }
        parts.Add(output.KeyName);
        return parts;
    }

    /// <summary>
    /// Get the keyboard key parts for a button slot (if it outputs to keyboard)
    /// Returns list of key parts (e.g., ["LCtrl", "LShift", "A"]) for drawing as separate keycaps
    /// </summary>
    private static List<string>? GetButtonKeyParts(MappingProfile? profile, uint vjoyId, int buttonIndex)
    {
        if (profile is null) return null;

        // Find mapping for this button slot that has keyboard output
        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyId &&
            m.Output.Index == buttonIndex &&
            !string.IsNullOrEmpty(m.Output.KeyName));

        if (mapping is null) return null;

        return GetKeyboardMappingParts(mapping);
    }

    /// <summary>
    /// Returns the row index of the vJoy button slot that has the network switch physical input as its source.
    /// Returns -1 if the switch button is not mapped to any output on this device.
    /// </summary>
    private static int GetSwitchButtonRowIndex(MappingProfile profile, uint vjoyId, NetworkSwitchConfig cfg)
    {
        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.Type == OutputType.VJoyButton &&
            m.Output.VJoyDevice == vjoyId &&
            m.Inputs.Any(inp =>
                inp.Type == InputType.Button &&
                inp.Index == cfg.ButtonIndex &&
                inp.DeviceId.Equals(cfg.DeviceId, StringComparison.OrdinalIgnoreCase)));

        if (mapping is null) return -1;
        return mapping.Output.Index; // 0-based output index = row index in button category
    }


    private void DrawChunkyBindingRow(SKCanvas canvas, SKRect bounds, string outputName, string binding,
        bool isSelected, bool isHovered, List<string>? keyParts = null, bool isModifier = false,
        bool isSwitchButton = false, bool isShared = false, bool isMergedAway = false)
    {
        bool hasBinding = !string.IsNullOrEmpty(binding) && binding != "ÔÇö";
        bool hasKeyParts = keyParts is not null && keyParts.Count > 0;

        // Check for attention highlight (physical input was pressed that maps to this output)
        bool hasAttentionHighlight = false;
        float attentionIntensity = 0f;
        if (_highlight.Row >= 0 &&
            _ctx.VJoyDevices.Count > 0 && _ctx.SelectedVJoyDeviceIndex < _ctx.VJoyDevices.Count)
        {
            var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
            // Parse output index from the outputName (e.g., "Button 5" -> 4, "Axis 0" -> 0)
            int outputIndex = -1;
            if (outputName.StartsWith("Button ") && int.TryParse(outputName.AsSpan(7), out int btnNum))
                outputIndex = btnNum - 1; // Buttons are 1-indexed in display
            else if (outputName.StartsWith("Axis ") && int.TryParse(outputName.AsSpan(5), out int axisNum))
                outputIndex = axisNum;

            if (outputIndex == _highlight.Row && vjoyDevice.Id == _highlight.VJoyDevice)
            {
                var elapsed = Environment.TickCount64 - _highlight.StartTicks;
                if (elapsed < HighlightDurationMs)
                {
                    hasAttentionHighlight = true;
                    // Ease-out fade: starts bright and fades slowly, then accelerates fade at end
                    // Using cubic ease-in for the FADE (so highlight fades slowly at first, faster at end)
                    float t = (float)(elapsed / HighlightDurationMs); // 0 to 1
                    float easeIn = t * t * t; // Cubic ease-in: 0 to 1, starts slow, ends fast
                    attentionIntensity = 1f - easeIn; // 1 to 0, fades slowly at first, faster at end
                }
                else
                {
                    _highlight.Row = -1; // Clear expired highlight
                }
            }
        }

        // Background - selection state is independent of attention highlight
        SKColor bgColor;
        if (isSelected)
            bgColor = FUIColors.Active.WithAlpha(50);
        else if (isHovered)
            bgColor = FUIColors.Primary.WithAlpha(35);
        else
            bgColor = FUIColors.Background2.WithAlpha(100);

        using var bgPaint = FUIRenderer.CreateFillPaint(bgColor);
        canvas.DrawRoundRect(bounds, 4, 4, bgPaint);

        // Draw attention highlight as overlay (additive, doesn't replace selection)
        if (hasAttentionHighlight)
        {
            // Pulsing glow effect that fades out - use theme active color
            byte glowAlpha = (byte)(100 * attentionIntensity);
            using var glowPaint = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(glowAlpha));
            canvas.DrawRoundRect(bounds, 4, 4, glowPaint);
        }

        // Frame
        SKColor frameColor;
        float frameWidth;
        if (hasAttentionHighlight)
        {
            // Attention frame pulses with the highlight - use theme active color
            frameColor = FUIColors.Active.WithAlpha((byte)(200 * attentionIntensity + 55));
            frameWidth = 2f + attentionIntensity; // Slightly thicker when fresh
        }
        else if (isSelected)
        {
            frameColor = FUIColors.Active;
            frameWidth = 2f;
        }
        else
        {
            frameColor = isHovered ? FUIColors.FrameBright : FUIColors.Frame.WithAlpha(100);
            frameWidth = 1f;
        }

        using var framePaint = FUIRenderer.CreateStrokePaint(frameColor, frameWidth);
        canvas.DrawRoundRect(bounds, 4, 4, framePaint);

        // Output name (centered vertically)
        float leftTextX = bounds.Left + 12;
        FUIRenderer.DrawText(canvas, outputName, new SKPoint(leftTextX, bounds.MidY + 5),
            FUIColors.ContentColor(isSelected), 15f, true);

        // Right side indicator: keyboard keycaps or binding dot
        if (hasKeyParts)
        {
            // Draw keycaps right-aligned within available space
            float keycapHeight = 16f;
            float keycapGap = 2f;
            float keycapPadding = 6f;  // Padding inside each keycap (left + right)
            float fontSize = 11f;  // Slightly smaller font for compact display
            float scaledFontSize = fontSize;
            float keycapRight = bounds.Right - 8;
            float keycapTop = bounds.MidY - keycapHeight / 2;

            // Use same font settings as DrawTextCentered for accurate measurement
            using var measurePaint = new SKPaint
            {
                TextSize = scaledFontSize,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal)
            };

            // Draw keycaps from right to left (key rightmost, then modifiers)
            for (int i = keyParts!.Count - 1; i >= 0; i--)
            {
                string keyText = keyParts[i].ToUpperInvariant();
                float textWidth = measurePaint.MeasureText(keyText);
                float keycapWidth = textWidth + keycapPadding * 2;
                float keycapLeft = keycapRight - keycapWidth;

                var keycapBounds = new SKRect(keycapLeft, keycapTop, keycapRight, keycapTop + keycapHeight);

                // Keycap background + frame
                FUIRenderer.DrawRoundedPanel(canvas, keycapBounds, FUIColors.TextPrimary.WithAlpha(20), FUIColors.TextPrimary.WithAlpha(100));

                // Keycap text - draw manually centered to ensure padding is respected
                float textX = keycapLeft + keycapPadding;
                float textY = keycapBounds.MidY + scaledFontSize / 3;
                using var textPaint = new SKPaint
                {
                    Color = FUIColors.TextPrimary,
                    TextSize = scaledFontSize,
                    IsAntialias = true,
                    Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal)
                };
                canvas.DrawText(keyText, textX, textY, textPaint);

                // Move left for next keycap
                keycapRight = keycapLeft - keycapGap;
            }

            // Draw MODIFIER badge to the left of the keycaps when this button acts as a modifier key
            if (isModifier)
            {
                const string modText = "MODIFIER";
                float modTextWidth = measurePaint.MeasureText(modText);
                float modBadgeWidth = modTextWidth + keycapPadding * 2;
                float modBadgeRight = keycapRight - 4f;
                float modBadgeLeft = modBadgeRight - modBadgeWidth;
                var modBadgeBounds = new SKRect(modBadgeLeft, keycapTop, modBadgeRight, keycapTop + keycapHeight);

                FUIRenderer.DrawRoundedPanel(canvas, modBadgeBounds, FUIColors.Primary.WithAlpha(40), FUIColors.Primary.WithAlpha(180));

                float modTextY = modBadgeBounds.MidY + scaledFontSize / 3f;
                using var modTextPaint = new SKPaint
                {
                    Color = FUIColors.Primary,
                    TextSize = scaledFontSize,
                    IsAntialias = true,
                    Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal)
                };
                canvas.DrawText(modText, modBadgeLeft + keycapPadding, modTextY, modTextPaint);
            }
        }
        else if (hasBinding && !isSwitchButton)
        {
            // Binding indicator dot on the right
            float dotX = bounds.Right - 20;
            float dotY = bounds.MidY;
            using var dotPaint = FUIRenderer.CreateFillPaint(FUIColors.Active);
            canvas.DrawCircle(dotX, dotY, 5f, dotPaint);
        }

        // ── Network switch button indicator — amber "NET" pill on the right (replaces dot) ──
        if (isSwitchButton)
        {
            const float pillW = 30f;
            const float pillH = 14f;
            float pillX = bounds.Right - pillW - 8f;
            float pillY = bounds.MidY - pillH / 2f;
            var pillRect = new SKRect(pillX, pillY, pillX + pillW, pillY + pillH);
            FUIRenderer.DrawRoundedPanel(canvas, pillRect,
                FUIColors.Warning.WithAlpha(FUIColors.AlphaHoverBg),
                FUIColors.Warning.WithAlpha(FUIColors.AlphaBorderSoft));
            FUIRenderer.DrawTextCentered(canvas, "NET", pillRect, FUIColors.Warning, 10f);
        }
        else if (isShared)
        {
            // Shared-away slot: SC's share feature has rerouted this slot's mapping output to
            // the primary's vJoy button, so the slot has no standalone binding. Show a blue
            // "SHARED" pill so the row's empty state isn't confusing.
            const float pillW = 50f;
            const float pillH = 14f;
            float pillX = bounds.Right - pillW - 8f;
            float pillY = bounds.MidY - pillH / 2f;
            var pillRect = new SKRect(pillX, pillY, pillX + pillW, pillY + pillH);
            FUIRenderer.DrawRoundedPanel(canvas, pillRect,
                FUIColors.Primary.WithAlpha(FUIColors.AlphaHoverBg),
                FUIColors.Primary.WithAlpha(FUIColors.AlphaBorderSoft));
            FUIRenderer.DrawTextCentered(canvas, "SHARED", pillRect, FUIColors.Primary, 10f);
        }
        else if (isMergedAway)
        {
            // Merged-away slot: this axis's natural physical input is being consumed by a
            // merge on another vJoy slot. Blue pill mirrors the SHARED treatment for buttons —
            // same "this slot is owned by something elsewhere" semantic. Editor is replaced
            // with an explanatory panel when selected.
            const float pillW = 60f;
            const float pillH = 14f;
            float pillX = bounds.Right - pillW - 8f;
            float pillY = bounds.MidY - pillH / 2f;
            var pillRect = new SKRect(pillX, pillY, pillX + pillW, pillY + pillH);
            FUIRenderer.DrawRoundedPanel(canvas, pillRect,
                FUIColors.Primary.WithAlpha(FUIColors.AlphaHoverBg),
                FUIColors.Primary.WithAlpha(FUIColors.AlphaBorderSoft));
            FUIRenderer.DrawTextCentered(canvas, "MERGED →", pillRect, FUIColors.Primary, 10f);
        }
    }

    private static string GetAxisBindingText(MappingProfile? profile, uint vjoyId, int axisIndex)
    {
        if (profile is null) return "ÔÇö";

        var mapping = profile.AxisMappings.FirstOrDefault(m =>
            m.Output.Type == OutputType.VJoyAxis &&
            m.Output.VJoyDevice == vjoyId &&
            m.Output.Index == axisIndex);

        if (mapping is not null && mapping.Inputs.Count > 0)
        {
            var input = mapping.Inputs[0];
            return $"{input.DeviceName} - Axis {input.Index}";
        }

        // Check AxisToButtonMappings (threshold mode)
        var a2bs = profile.AxisToButtonMappings.Where(m =>
            m.SourceVJoyDevice == vjoyId &&
            m.SourceAxisIndex == axisIndex).ToList();

        if (a2bs.Count > 0)
        {
            var parts = a2bs.Select(m =>
            {
                string dir = m.ActivateAbove ? "\u25b2" : "\u25bc";
                string key = !string.IsNullOrEmpty(m.Output.KeyName) ? m.Output.KeyName : "?";
                return $"{dir}{key}";
            });
            return $"Threshold {string.Join(" ", parts)}";
        }

        return "ÔÇö";
    }

    private static string GetButtonBindingText(MappingProfile? profile, uint vjoyId, int buttonIndex)
    {
        if (profile is null) return "ÔÇö";

        // Find mapping for this button slot (either VJoyButton or Keyboard output type)
        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyId &&
            m.Output.Index == buttonIndex);

        if (mapping is null || mapping.Inputs.Count == 0) return "ÔÇö";

        var input = mapping.Inputs[0];
        if (input.Type == InputType.Button)
            return $"{input.DeviceName} - Button {input.Index + 1}";
        return $"{input.DeviceName} - {input.Type} {input.Index}";
    }


    private static string GetAxisBindingName(int axisIndex) => axisIndex switch
    {
        0 => "x",  1 => "y",  2 => "z",
        3 => "rx", 4 => "ry", 5 => "rz",
        6 => "slider1", 7 => "slider2",
        _ => $"axis{axisIndex}"
    };

    /// <summary>
    /// Finds the DeviceMap control for the given mapping row index.
    /// Row index is relative to the current category (Buttons or Axes), starting at 0.
    /// Category 0 (Buttons): row i = button output index i.
    /// Category 1 (Axes): row i = axis output index i.
    /// Returns null if no mapping or no device map anchor.
    /// </summary>
    private ControlDefinition? GetControlForRow(int rowIndex)
    {
        var deviceMap = _ctx.MappingsPrimaryDeviceMap;
        if (deviceMap is null) return null;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return null;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return null;

        string? binding;
        if (_mappingCategory == 1)
        {
            // Axes category: translate visual row to actual axis index
            int axisIdx = AxisIndexForRow(rowIndex);
            if (axisIdx < 0) return null;
            var mapping = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.VJoyDevice == vjoyDevice.Id && m.Output.Index == axisIdx);
            binding = mapping?.Inputs.Count > 0 ? GetAxisBindingName(mapping.Inputs[0].Index) : null;
        }
        else
        {
            // Buttons category: row i = button output index i
            var mapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.VJoyDevice == vjoyDevice.Id && m.Output.Index == rowIndex);
            binding = mapping?.Inputs.Count > 0 ? $"button{mapping.Inputs[0].Index + 1}" : null;
        }

        return binding is not null ? deviceMap.FindControlByBinding(binding) : null;
    }

    /// <summary>
    /// Converts a device-map viewBox coordinate to canvas screen coordinates,
    /// using the scale/offset set by the most recent FUIRenderer.DrawSvgInBounds call.
    /// </summary>
}
