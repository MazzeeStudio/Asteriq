using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
    private void ApplySvgTransform(FUIRenderer.SvgTransform t, bool mirror)
    {
        _ctx.SvgScale = t.Scale;
        _ctx.SvgOffset = t.Offset;
        _ctx.SvgMirrored = mirror;
        _ctx.SilhouetteSourceWidth = t.SourceWidth;
    }

    private void DrawMappingsTabContent(SKCanvas canvas, SKRect bounds, float sideTabPad, float contentTop, float contentBottom)
    {
        float frameInset = 5f;
        float pad = FUIRenderer.SpaceLG;  // Standard padding for right side
        var contentBounds = new SKRect(sideTabPad, contentTop, bounds.Right - pad, contentBottom);

        // Three-panel layout: Left (bindings list) | Center (device view) | Right (settings)
        var layout = FUIRenderer.CalculateLayout(contentBounds.Width, minLeftPanel: 360f, minRightPanel: 280f, maxSidePanel: 500f);
        float gap = layout.Gutter;

        var leftBounds = new SKRect(contentBounds.Left, contentBounds.Top,
            contentBounds.Left + layout.LeftPanelWidth, contentBounds.Bottom);
        var centerBounds = new SKRect(leftBounds.Right + gap, contentBounds.Top,
            leftBounds.Right + gap + layout.CenterWidth, contentBounds.Bottom);
        var rightBounds = new SKRect(centerBounds.Right + gap, contentBounds.Top,
            contentBounds.Right, contentBounds.Bottom);

        // Refresh vJoy devices list
        if (_ctx.VJoyDevices.Count == 0)
        {
            _ctx.VJoyDevices = _ctx.VJoyService.EnumerateDevices();
        }

        // LEFT PANEL - Bindings List
        DrawBindingsPanel(canvas, leftBounds, frameInset);

        // CENTER PANEL - Device Visualization
        DrawDeviceVisualizationPanel(canvas, centerBounds, frameInset);

        // RIGHT PANEL — Mapping Settings
        DrawMappingSettingsPanel(canvas, rightBounds, frameInset);
    }

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

                    DrawChunkyBindingRow(canvas, rowBounds, $"Button {i + 1}", binding, isSelected, isHovered, rowIndex, keyParts, isModifier, isSwitchBtn, isShared);
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

                    DrawChunkyBindingRow(canvas, rowBounds, axisNames[axisIdx], binding, isSelected, isHovered, rowIndex);
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

    private void DrawNetSwitchBadge(SKCanvas canvas, SKRect listBounds, MappingProfile? profile)
    {
        _netSwitch.BadgeBounds = SKRect.Empty;
        _netSwitch.BadgeXBounds = SKRect.Empty;
        _netSwitch.BadgeXHovered = false;

        var cfg = profile?.NetworkSwitchButton;
        if (cfg is null) return;

        const float badgeH = 26f;
        const float badgeGap = 6f;
        float badgeY = listBounds.Bottom + badgeGap;
        var badgeRect = new SKRect(listBounds.Left, badgeY, listBounds.Right, badgeY + badgeH);
        _netSwitch.BadgeBounds = badgeRect;

        FUIRenderer.DrawRoundedPanel(canvas, badgeRect,
            FUIColors.Warning.WithAlpha(FUIColors.AlphaLightTint),
            FUIColors.Warning.WithAlpha(FUIColors.AlphaBorderSoft));

        float textY = badgeRect.MidY + 5f;
        FUIRenderer.DrawText(canvas, "TX TOGGLE: " + cfg.DisplayName,
            new SKPoint(badgeRect.Left + 10f, textY), FUIColors.Warning, 13f);

        // × close button on right
        const float xSize = 16f;
        var xBounds = new SKRect(badgeRect.Right - xSize - 6f, badgeRect.MidY - xSize / 2f,
            badgeRect.Right - 6f, badgeRect.MidY + xSize / 2f);
        _netSwitch.BadgeXBounds = xBounds;
        _netSwitch.BadgeXHovered = xBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

        using var xPaint = FUIRenderer.CreateTextPaint(
            _netSwitch.BadgeXHovered ? FUIColors.TextBright : FUIColors.Warning.WithAlpha(200), 12f);
        float xTextX = xBounds.MidX - 3f;
        canvas.DrawText("\u00D7", xTextX, xBounds.MidY + 5f, xPaint);
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
        bool isSelected, bool isHovered, int rowIndex, List<string>? keyParts = null, bool isModifier = false,
        bool isSwitchButton = false, bool isShared = false)
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
    }

    private void DrawDeviceVisualizationPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background1.WithAlpha(100));
        canvas.DrawRect(new SKRect(bounds.Left + frameInset, bounds.Top + frameInset,
            bounds.Right - frameInset, bounds.Bottom - frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame.WithAlpha(150), 30f, 8f);

        // Show device silhouette - use primary device's map if available
        float centerX = bounds.MidX;

        // Get the appropriate image based on primary device map.
        // Resolve bitmap before applying the JoystickSvg fallback — otherwise bitmap-based maps never render.
        var svg = _ctx.GetSvgForDeviceMap?.Invoke(_ctx.MappingsPrimaryDeviceMap);
        var bitmap = svg is null ? _ctx.GetBitmapForDeviceMap?.Invoke(_ctx.MappingsPrimaryDeviceMap) : null;
        if (svg is null && bitmap is null)
            svg = _ctx.JoystickSvg;
        bool shouldMirror = _ctx.MappingsPrimaryDeviceMap?.Mirror ?? false;

        // Device name label at top of panel
        float labelRowHeight = 20f;
        float labelY = bounds.Top + frameInset + labelRowHeight;
        string deviceLabel = _ctx.MappingsPrimaryDeviceMap?.Device ?? "—";
        FUIRenderer.DrawTextCentered(canvas, deviceLabel,
            new SKRect(bounds.Left, bounds.Top + frameInset, bounds.Right, labelY),
            FUIColors.TextDim, 13f);

        // Reserve space at the bottom for the auto-scroll checkbox row
        float checkboxRowHeight = 26f;
        float checkboxAreaTop = bounds.Bottom - frameInset - checkboxRowHeight;

        // Constrained bounds for silhouette (shared between SVG and bitmap paths)
        float maxSize = 900f;
        float maxWidth = Math.Min(bounds.Width - 40, maxSize);
        float maxHeight = Math.Min(bounds.Height - 40 - checkboxRowHeight - labelRowHeight, maxSize);
        float constrainedWidth = Math.Min(maxWidth, maxHeight);
        float constrainedHeight = constrainedWidth;
        float availableCenterY = labelY + (checkboxAreaTop - labelY) / 2f;
        var constrainedBounds = new SKRect(
            centerX - constrainedWidth / 2,
            availableCenterY - constrainedHeight / 2,
            centerX + constrainedWidth / 2,
            availableCenterY + constrainedHeight / 2
        );

        if (svg?.Picture is not null)
        {
            _ctx.SilhouetteBounds = constrainedBounds;
            var t = FUIRenderer.DrawSvgInBounds(canvas, svg, constrainedBounds, shouldMirror);
            ApplySvgTransform(t, shouldMirror);
            DrawMappingHighlightLeadLine(canvas, constrainedBounds);
        }
        else if (bitmap is not null)
        {
            float vbW = _ctx.MappingsPrimaryDeviceMap?.ViewBox?.X ?? bitmap.Width;
            float vbH = _ctx.MappingsPrimaryDeviceMap?.ViewBox?.Y ?? bitmap.Height;
            _ctx.SilhouetteBounds = constrainedBounds;
            var t = FUIRenderer.DrawBitmapInBounds(canvas, bitmap, vbW, vbH, constrainedBounds, shouldMirror);
            ApplySvgTransform(t, shouldMirror);
            DrawMappingHighlightLeadLine(canvas, constrainedBounds);
        }
        else
        {
            _ctx.SilhouetteBounds = SKRect.Empty;
            FUIRenderer.DrawTextCentered(canvas, "Device Preview",
                new SKRect(bounds.Left, labelY, bounds.Right, checkboxAreaTop),
                FUIColors.TextDim, 14f);
        }

        // Auto-scroll checkbox at bottom of panel
        float leftMargin = bounds.Left + frameInset + 12;
        float checkboxSize = 12f;
        float checkboxY = checkboxAreaTop + (checkboxRowHeight - checkboxSize) / 2f;
        _autoScroll.CheckboxBounds = new SKRect(leftMargin, checkboxY, leftMargin + checkboxSize, checkboxY + checkboxSize);
        FUIWidgets.DrawCheckboxWithLabel(canvas, _autoScroll.CheckboxBounds, _autoScroll.Enabled,
            _autoScroll.CheckboxHovered, "AUTO-SCROLL TO MAPPING");

        // "No mapping" flash indicator ÔÇö centered above the checkbox row, fades out
        if (_highlight.FlashText is not null)
        {
            float elapsed = (Environment.TickCount64 - _highlight.FlashTicks) / 1000f;
            float opacity = elapsed < 1f ? 1f : Math.Max(0f, 1f - (elapsed - 1f) / 1.5f);
            if (opacity > 0.01f)
            {
                var noMapColor = FUIColors.Warning.WithAlpha((byte)(opacity * 220));
                FUIRenderer.DrawTextCentered(canvas, _highlight.FlashText,
                    new SKRect(bounds.Left, checkboxAreaTop - 22, bounds.Right, checkboxAreaTop),
                    noMapColor, 13f);
            }
            else
            {
                _highlight.FlashText = null;
            }
        }
    }

    private void DrawMappingSettingsPanel(SKCanvas canvas, SKRect bounds, float frameInset, bool isExpanded = true)
    {
        bool isCollapsible = _ctx.GetActiveSCExportProfile is not null && _ctx.VJoyDevices.Any(v => v.Exists);
        float y, leftMargin, rightMargin;

        if (isCollapsible)
        {
            bool headerHovered = !isExpanded
                && new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Top + FUIRenderer.PanelHeaderHeight)
                    .Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            var m = FUIWidgets.DrawCollapsiblePanelHeader(canvas, bounds, "MAPPING SETTINGS",
                isExpanded, headerHovered, out _);
            if (!isExpanded) return;
            y = m.Y;
            leftMargin = m.LeftMargin;
            rightMargin = m.RightMargin;
        }
        else
        {
            // No collapsible header — Mapping Settings fills the whole right panel
            var m = FUIRenderer.DrawPanelChrome(canvas, bounds);
            y = m.Y;
            leftMargin = m.LeftMargin;
            rightMargin = m.RightMargin;
            FUIWidgets.DrawPanelTitle(canvas, leftMargin, rightMargin, ref y, "MAPPING SETTINGS");
        }

        // Reset net-switch bounds each frame (set later in DrawButtonSettings if applicable)
        _netSwitch.ActionBounds = SKRect.Empty;
        _netSwitch.ActionHovered = false;
        _netSwitch.BadgeBounds = SKRect.Empty;
        _netSwitch.BadgeXBounds = SKRect.Empty;
        _netSwitch.BadgeXHovered = false;

        // Reset shared-manage button bounds each frame; populated only when the selected row
        // is a shared-away slot (the early-return branch below).
        _sharedManageButtonBounds = SKRect.Empty;
        _sharedManageButtonHovered = false;
        _sharedManageSearchText = null;
        _sharedManageVJoyDevice = 0;

        // Show settings for selected row
        if (_selectedMappingRow < 0)
        {
            FUIRenderer.DrawText(canvas, "Select an output to configure",
                new SKPoint(leftMargin, y + 32), FUIColors.TextDim, 15f);
            return;
        }

        // Determine if axis or button based on current category
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        string outputName = GetSelectedOutputName();

        FUIRenderer.DrawText(canvas, outputName, new SKPoint(leftMargin, y + 16), FUIColors.Active, 16f, true);
        y += 36;

        // Shared-away slot: SC's share feature rerouted this slot's mapping output to the
        // primary's vJoy button. Replace the normal editor with a read-only message block
        // explaining the state and a deep-link to the SC Bindings tab where the share is
        // owned. Buttons-only — axes can't be shared via the SC share feature.
        if (!isAxis && _ctx.VJoyDevices.Count > _ctx.SelectedVJoyDeviceIndex)
        {
            var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
            var sharedInfos = GetSharedSlotInfos(vjoyDevice.Id, _selectedMappingRow);
            if (sharedInfos.Count > 0)
            {
                DrawSharedSlotPanel(canvas, leftMargin, rightMargin, y, sharedInfos, vjoyDevice.Id);
                return;
            }
        }

        // INPUT SOURCES section - shows mapped inputs with add/remove
        y = DrawInputSourcesSection(canvas, leftMargin, rightMargin, y);

        float bottomMargin = bounds.Bottom - frameInset - 10;

        if (isAxis)
        {
            y = DrawAxisOutputModeToggle(canvas, leftMargin, rightMargin, y);

            if (_threshold.IsThresholdMode)
                DrawThresholdSettings(canvas, leftMargin, rightMargin, y, bottomMargin);
            else
                DrawAxisSettings(canvas, leftMargin, rightMargin, y, bottomMargin);
        }
        else
        {
            DrawButtonSettings(canvas, leftMargin, rightMargin, y, bottomMargin);
        }

        // Merge-mode dropdown panel draws on top of subsequent content when open
        DrawMergeDropdownOverlay(canvas);
    }

    private float DrawInputSourcesSection(SKCanvas canvas, float leftMargin, float rightMargin, float y)
    {
        _inputSourceRemoveBounds.Clear();

        FUIWidgets.DrawSectionLabel(canvas, "INPUT SOURCES", leftMargin, ref y);

        // Get current mappings for selected output
        var inputs = GetInputsForSelectedOutput();
        bool isListening = _inputDetection.IsListening;

        float rowHeight = 40f;  // Two-line layout
        float rowGap = 4f;

        if (inputs.Count == 0 && !isListening)
        {
            // No inputs - show "None" with dashed border
            var emptyBounds = new SKRect(leftMargin, y, rightMargin, y + 28);
            using var emptyBgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background1.WithAlpha(100));
            canvas.DrawRoundRect(emptyBounds, 3, 3, emptyBgPaint);

            using var emptyFramePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.FrameDim,
                StrokeWidth = 1f,
                PathEffect = SKPathEffect.CreateDash(new float[] { 4, 2 }, 0)
            };
            canvas.DrawRoundRect(emptyBounds, 3, 3, emptyFramePaint);

            FUIRenderer.DrawText(canvas, "No input mapped", new SKPoint(leftMargin + 10, emptyBounds.MidY + 4), FUIColors.TextDisabled, 14f);
            y += 28 + rowGap;
        }
        else
        {
            // Draw each input source row (two-line layout)
            for (int i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                var rowBounds = new SKRect(leftMargin, y, rightMargin - 30, y + rowHeight);

                // Row background + frame
                FUIRenderer.DrawRoundedPanel(canvas, rowBounds, FUIColors.Background1, FUIColors.Frame);

                // Line 1: Input type and index (e.g., "Button 5") - vertically centered in top half
                string inputTypeText = input.Type == InputType.Button
                    ? $"Button {input.Index + 1}"
                    : $"{input.Type} {input.Index}";
                FUIRenderer.DrawText(canvas, inputTypeText, new SKPoint(leftMargin + 8, y + 16), FUIColors.TextPrimary, 14f);

                // Line 2: Device name (smaller, dimmer) - vertically centered in bottom half
                FUIRenderer.DrawText(canvas, input.DeviceName, new SKPoint(leftMargin + 8, y + 32), FUIColors.TextDim, 12f);

                // Remove [X] button (full height of row)
                var removeBounds = new SKRect(rightMargin - 26, y, rightMargin, y + rowHeight);
                bool removeHovered = _hoveredInputSourceRemove == i;

                FUIRenderer.DrawRoundedPanel(canvas, removeBounds,
                    removeHovered ? FUIColors.WarningTint : FUIColors.Background2,
                    removeHovered ? FUIColors.Warning : FUIColors.Frame);

                FUIRenderer.DrawTextCentered(canvas, "X", removeBounds,
                    removeHovered ? FUIColors.Warning : FUIColors.TextDim, 14f);

                _inputSourceRemoveBounds.Add(removeBounds);
                y += rowHeight + rowGap;
            }
        }

        // Listening indicator with timeout bar
        if (isListening)
        {
            // Check for timeout
            var elapsed = Environment.TickCount64 - _inputDetection.ListeningStartTicks;
            if (elapsed >= InputListeningTimeoutMs)
            {
                CancelInputListening(); // Timeout - cancel listening
            }
            else
            {
                var listenBounds = new SKRect(leftMargin, y, rightMargin, y + rowHeight);
                byte alpha = (byte)(180 + MathF.Sin(_ctx.PulsePhase * 3) * 60);

                using var listenBgPaint = FUIRenderer.CreateFillPaint(FUIColors.SelectionBg);
                canvas.DrawRoundRect(listenBounds, 3, 3, listenBgPaint);

                // Draw timeout progress bar
                float progress = Math.Min(1f, (float)(elapsed / InputListeningTimeoutMs));
                float remaining = 1f - progress;
                float progressWidth = (listenBounds.Width - 6) * remaining;
                if (progressWidth > 0)
                {
                    var progressRect = new SKRect(
                        listenBounds.Left + 3,
                        listenBounds.Top + 3,
                        listenBounds.Left + 3 + progressWidth,
                        listenBounds.Bottom - 3);
                    using var progressPaint = FUIRenderer.CreateFillPaint(FUIColors.SelectionBgStrong);
                    canvas.DrawRoundRect(progressRect, 2, 2, progressPaint);
                }

                using var listenFramePaint = FUIRenderer.CreateStrokePaint(FUIColors.Active.WithAlpha(alpha), 2f);
                canvas.DrawRoundRect(listenBounds, 3, 3, listenFramePaint);

                FUIRenderer.DrawText(canvas, "Press input...", new SKPoint(leftMargin + 10, y + 18),
                    FUIColors.Active.WithAlpha(alpha), 14f);
                y += rowHeight + rowGap;
            }
        }

        // Add input button [+] — hidden in threshold mode when an input already exists
        bool thresholdInputFull = _threshold.IsThresholdMode && inputs.Count >= 1 && !isListening;
        _addInputButtonBounds = SKRect.Empty;

        if (!thresholdInputFull)
        {
            var addBounds = new SKRect(leftMargin, y, rightMargin, y + 28);
            _addInputButtonBounds = addBounds;
            bool addHovered = _addInputButtonHovered;

            using var addBgPaint = FUIRenderer.CreateFillPaint(addHovered ? FUIColors.SelectionBg : FUIColors.Background2);
            canvas.DrawRoundRect(addBounds, 3, 3, addBgPaint);

            using var addFramePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = addHovered ? FUIColors.Active : FUIColors.Frame,
                StrokeWidth = addHovered ? 2f : 1f,
                PathEffect = isListening ? null : SKPathEffect.CreateDash(new float[] { 4, 2 }, 0)
            };
            canvas.DrawRoundRect(addBounds, 3, 3, addFramePaint);

            string addText = isListening ? "Cancel" : "+ Add Input";
            FUIRenderer.DrawTextCentered(canvas, addText, addBounds,
                FUIColors.ContentColor(addHovered), 14f);
            y += 28 + 4;
        }

        // Merge operation selector (only for axes with 2+ inputs)
        bool isAxis = _mappingCategory == 1;
        if (isAxis && inputs.Count >= 2)
        {
            y = DrawMergeOperationSelector(canvas, leftMargin, rightMargin, y);
        }
        else
        {
            // Clear dropdown bounds when hidden so stale hit-tests don't fire
            _merge.SelectorBounds = SKRect.Empty;
            _merge.DropdownBounds = SKRect.Empty;
            _merge.DropdownOpen = false;
            y += 8;  // Extra spacing when no merge selector
        }

        return y;
    }

    // Ordered merge operations shown in the dropdown. Indices used by
    // MergeModeDropdownState.HoveredIndex and panel item positions.
    private static readonly MergeOperation[] s_mergeOps =
    {
        MergeOperation.Average,
        MergeOperation.Maximum,
        MergeOperation.Minimum,
        MergeOperation.Sum,
        MergeOperation.LastSnap,
        MergeOperation.LastTakeover
    };

    private static string GetMergeOpLabel(MergeOperation op) => op switch
    {
        MergeOperation.Average => "Average",
        MergeOperation.Maximum => "Maximum",
        MergeOperation.Minimum => "Minimum",
        MergeOperation.Sum => "Sum",
        MergeOperation.LastSnap => "Last",
        MergeOperation.LastTakeover => "Last",
        _ => op.ToString()
    };

    private static string? GetMergeOpBadge(MergeOperation op) => op switch
    {
        MergeOperation.LastSnap => "SNAP",
        MergeOperation.LastTakeover => "TAKEOVER",
        _ => null
    };

    private static (SKColor bg, SKColor text) GetMergeOpBadgeColors(MergeOperation op) => op switch
    {
        MergeOperation.LastSnap => (FUIColors.Warning.WithAlpha(60), FUIColors.Warning),
        MergeOperation.LastTakeover => (FUIColors.Active.WithAlpha(60), FUIColors.Active),
        _ => (SKColors.Transparent, SKColors.Transparent)
    };

    private static string GetMergeOpDescription(MergeOperation op) => op switch
    {
        MergeOperation.Average => "Averages all input values",
        MergeOperation.Maximum => "Uses highest input value",
        MergeOperation.Minimum => "Uses lowest input value",
        MergeOperation.Sum => "Adds values (clamped -1 to 1)",
        MergeOperation.LastSnap => "Last-touched input wins; output snaps (may jump)",
        MergeOperation.LastTakeover => "Last-touched wins after its position crosses the output (no jumps)",
        _ => ""
    };

    private float DrawMergeOperationSelector(SKCanvas canvas, float leftMargin, float rightMargin, float y)
    {
        var axisMapping = GetCurrentAxisMapping();
        if (axisMapping is null) return y;

        FUIWidgets.DrawSectionLabel(canvas, "MERGE MODE", leftMargin, ref y);

        const float dropdownHeight = 28f;
        _merge.SelectorBounds = new SKRect(leftMargin, y, rightMargin, y + dropdownHeight);

        // Draw dropdown chrome ourselves so the label + pill can be positioned precisely.
        var bgColor = _merge.DropdownOpen ? FUIColors.Primary.WithAlpha(40)
            : (_merge.SelectorHovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);
        using (var bgPaint = FUIRenderer.CreateFillPaint(bgColor))
            canvas.DrawRect(_merge.SelectorBounds, bgPaint);
        using (var framePaint = FUIRenderer.CreateStrokePaint(
            _merge.DropdownOpen ? FUIColors.Primary : FUIColors.Frame))
            canvas.DrawRect(_merge.SelectorBounds, framePaint);

        string label = GetMergeOpLabel(axisMapping.MergeOp);
        string? badge = GetMergeOpBadge(axisMapping.MergeOp);

        const float labelFont = 14f;
        float labelX = _merge.SelectorBounds.Left + 8;
        float labelBaselineY = _merge.SelectorBounds.MidY + 4;
        FUIRenderer.DrawText(canvas, label, new SKPoint(labelX, labelBaselineY), FUIColors.TextPrimary, labelFont);

        if (badge is not null)
        {
            float labelWidth = FUIRenderer.MeasureText(label, labelFont);
            var (bgPill, txtPill) = GetMergeOpBadgeColors(axisMapping.MergeOp);
            FUIWidgets.DrawPill(canvas, labelX + labelWidth + 6f, _merge.SelectorBounds.MidY, badge, bgPill, txtPill);
        }

        string arrow = _merge.DropdownOpen ? "▲" : "▼";
        FUIRenderer.DrawText(canvas, arrow, new SKPoint(_merge.SelectorBounds.Right - 18, labelBaselineY),
            FUIColors.TextDim, 13f);

        y += dropdownHeight + 8;

        // Description text — baseline offset keeps it clear of the dropdown above
        FUIRenderer.DrawText(canvas, GetMergeOpDescription(axisMapping.MergeOp),
            new SKPoint(leftMargin, y + 10), FUIColors.TextDisabled, 12f);
        y += 20;

        return y;
    }

    /// <summary>
    /// Draws the expanded merge-mode dropdown panel as an overlay. Must be
    /// called after all other right-panel content so the panel renders on top.
    /// </summary>
    private void DrawMergeDropdownOverlay(SKCanvas canvas)
    {
        if (!_merge.DropdownOpen || _merge.SelectorBounds.IsEmpty) return;

        const float itemHeight = 28f;
        float listHeight = s_mergeOps.Length * itemHeight + 4f;
        _merge.DropdownBounds = new SKRect(
            _merge.SelectorBounds.Left, _merge.SelectorBounds.Bottom + 2,
            _merge.SelectorBounds.Right, _merge.SelectorBounds.Bottom + 2 + listHeight);

        FUIRenderer.DrawPanelShadow(canvas, _merge.DropdownBounds, 4f, 4f, 15f);
        using (var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.ActiveLight,
            StrokeWidth = 3f,
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(4f, 4f)
        })
            canvas.DrawRect(_merge.DropdownBounds, glowPaint);

        using (var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Void))
            canvas.DrawRect(_merge.DropdownBounds, bgPaint);
        using (var innerPaint = FUIRenderer.CreateFillPaint(FUIColors.Background0))
            canvas.DrawRect(_merge.DropdownBounds.Inset(2, 2), innerPaint);

        FUIRenderer.DrawLCornerFrame(canvas, _merge.DropdownBounds, FUIColors.ActiveStrong, 20f, 6f, 1.5f, true);

        var axisMapping = GetCurrentAxisMapping();
        int selectedIndex = axisMapping is null ? -1 : Array.IndexOf(s_mergeOps, axisMapping.MergeOp);

        canvas.Save();
        canvas.ClipRect(_merge.DropdownBounds);

        float itemY = _merge.DropdownBounds.Top + 2f;
        for (int i = 0; i < s_mergeOps.Length; i++)
        {
            var itemBounds = new SKRect(
                _merge.DropdownBounds.Left + 2, itemY,
                _merge.DropdownBounds.Right - 2, itemY + itemHeight);

            bool isHovered = i == _merge.HoveredIndex;
            bool isSelected = i == selectedIndex;

            if (isHovered)
            {
                using var hoverBg = FUIRenderer.CreateFillPaint(FUIColors.SelectionBg);
                canvas.DrawRect(itemBounds, hoverBg);
                using var accentBar = FUIRenderer.CreateFillPaint(FUIColors.Active);
                canvas.DrawRect(new SKRect(itemBounds.Left, itemBounds.Top + 2, itemBounds.Left + 2, itemBounds.Bottom - 2), accentBar);
            }
            else if (isSelected)
            {
                using var selAccent = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(FUIColors.AlphaGlow));
                canvas.DrawRect(new SKRect(itemBounds.Left, itemBounds.Top + 2, itemBounds.Left + 2, itemBounds.Bottom - 2), selAccent);
            }

            var op = s_mergeOps[i];
            string itemLabel = GetMergeOpLabel(op);
            string? itemBadge = GetMergeOpBadge(op);

            var textColor = isSelected ? FUIColors.Active : (isHovered ? FUIColors.TextBright : FUIColors.TextPrimary);
            const float itemFont = 13f;
            float itemX = itemBounds.Left + 10;
            float itemBaselineY = itemBounds.MidY + 4;
            FUIRenderer.DrawText(canvas, itemLabel, new SKPoint(itemX, itemBaselineY), textColor, itemFont);

            if (itemBadge is not null)
            {
                float itemLabelWidth = FUIRenderer.MeasureText(itemLabel, itemFont);
                var (bgPill, txtPill) = GetMergeOpBadgeColors(op);
                FUIWidgets.DrawPill(canvas, itemX + itemLabelWidth + 6f, itemBounds.MidY, itemBadge, bgPill, txtPill);
            }

            itemY += itemHeight;
        }

        canvas.Restore();
    }

    private List<InputSource> GetInputsForSelectedOutput()
    {
        var inputs = new List<InputSource>();
        if (_selectedMappingRow < 0) return inputs;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return inputs;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return inputs;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        int outputIndex = isAxis ? AxisIndexForRow(_selectedMappingRow) : _selectedMappingRow;
        if (outputIndex < 0) return inputs;

        if (isAxis)
        {
            // Check threshold mode first
            if (_threshold.IsThresholdMode)
            {
                var a2bMappings = profile.AxisToButtonMappings.Where(m =>
                    m.SourceVJoyDevice == vjoyDevice.Id &&
                    m.SourceAxisIndex == outputIndex).ToList();
                if (a2bMappings.Count > 0)
                    inputs.AddRange(a2bMappings[0].Inputs);
            }
            else
            {
                var mapping = profile.AxisMappings.FirstOrDefault(m =>
                    m.Output.Type == OutputType.VJoyAxis &&
                    m.Output.VJoyDevice == vjoyDevice.Id &&
                    m.Output.Index == outputIndex);
                if (mapping is not null)
                    inputs.AddRange(mapping.Inputs);
            }
        }
        else
        {
            // For button rows, find mapping for this vJoy button slot
            // Check both VJoyButton and Keyboard output types (both map to button slots)
            var mapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);
            if (mapping is not null)
                inputs.AddRange(mapping.Inputs);
        }

        return inputs;
    }

    private string GetSelectedOutputName()
    {
        if (_selectedMappingRow < 0) return "";

        // Category 0 = Buttons, Category 1 = Axes
        if (_mappingCategory == 1)
        {
            // Axes — translate visual row to actual axis index
            string[] axisNames = { "X Axis", "Y Axis", "Z Axis", "RX Axis", "RY Axis", "RZ Axis", "Slider 1", "Slider 2" };
            int axisIdx = AxisIndexForRow(_selectedMappingRow);
            return axisIdx >= 0 && axisIdx < axisNames.Length ? axisNames[axisIdx] : $"Axis {_selectedMappingRow}";
        }
        else
        {
            // Buttons
            return $"Button {_selectedMappingRow + 1}";
        }
    }

    /// <summary>
    /// Gets the current axis mapping for the selected output, or null if not an axis or not found.
    /// </summary>
    private AxisMapping? GetCurrentAxisMapping()
    {
        if (_selectedMappingRow < 0 || _mappingCategory != 1) return null;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return null;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return null;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int outputIndex = AxisIndexForRow(_selectedMappingRow);
        if (outputIndex < 0) return null;

        return profile.AxisMappings.FirstOrDefault(m =>
            m.Output.Type == OutputType.VJoyAxis &&
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);
    }

    private List<AxisToButtonMapping> GetCurrentAxisToButtonMappings()
    {
        if (_selectedMappingRow < 0 || _mappingCategory != 1) return new();
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return new();

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return new();

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int outputIndex = AxisIndexForRow(_selectedMappingRow);
        if (outputIndex < 0) return new();

        return profile.AxisToButtonMappings.Where(m =>
            m.SourceVJoyDevice == vjoyDevice.Id &&
            m.SourceAxisIndex == outputIndex).ToList();
    }

    private float DrawAxisOutputModeToggle(SKCanvas canvas, float leftMargin, float rightMargin, float y)
    {
        FUIWidgets.DrawSectionLabel(canvas, "OUTPUT MODE", leftMargin, ref y);

        float width = rightMargin - leftMargin;
        float halfWidth = (width - 4) / 2;
        float btnHeight = 28f;

        _threshold.AxisModeBounds = new SKRect(leftMargin, y, leftMargin + halfWidth, y + btnHeight);
        _threshold.ThresholdModeBounds = new SKRect(leftMargin + halfWidth + 4, y, rightMargin, y + btnHeight);

        // Axis button
        bool axisActive = !_threshold.IsThresholdMode;
        bool axisHovered = _threshold.HoveredOutputMode == 0;
        var axBg = axisActive ? FUIColors.Active.WithAlpha(FUIColors.AlphaGlow) : (axisHovered ? FUIColors.Primary.WithAlpha(40) : FUIColors.Background2);
        var axFrame = axisActive ? FUIColors.Active : (axisHovered ? FUIColors.FrameBright : FUIColors.Frame);
        var axText = axisActive ? FUIColors.TextBright : (axisHovered ? FUIColors.TextPrimary : FUIColors.TextDim);

        using (var bg = FUIRenderer.CreateFillPaint(axBg))
            canvas.DrawRoundRect(_threshold.AxisModeBounds, 3, 3, bg);
        using (var fr = FUIRenderer.CreateStrokePaint(axFrame, axisActive ? 2f : 1f))
            canvas.DrawRoundRect(_threshold.AxisModeBounds, 3, 3, fr);
        FUIRenderer.DrawTextCentered(canvas, "Axis", _threshold.AxisModeBounds, axText, 13f);

        // Threshold button — disabled when merge mode (2+ inputs) to prevent data loss
        bool threshActive = _threshold.IsThresholdMode;
        int inputCount = GetInputsForSelectedOutput().Count;
        bool threshDisabled = !threshActive && inputCount >= 2;
        bool threshHovered = !threshDisabled && _threshold.HoveredOutputMode == 1;
        var thBg = threshDisabled ? FUIColors.Background2
            : threshActive ? FUIColors.Active.WithAlpha(FUIColors.AlphaGlow)
            : (threshHovered ? FUIColors.Primary.WithAlpha(40) : FUIColors.Background2);
        var thFrame = threshDisabled ? FUIColors.Frame.WithAlpha(100)
            : threshActive ? FUIColors.Active
            : (threshHovered ? FUIColors.FrameBright : FUIColors.Frame);
        var thText = threshDisabled ? FUIColors.TextDim.WithAlpha(100)
            : threshActive ? FUIColors.TextBright
            : (threshHovered ? FUIColors.TextPrimary : FUIColors.TextDim);

        using (var bg = FUIRenderer.CreateFillPaint(thBg))
            canvas.DrawRoundRect(_threshold.ThresholdModeBounds, 3, 3, bg);
        using (var fr = FUIRenderer.CreateStrokePaint(thFrame, threshActive ? 2f : 1f))
            canvas.DrawRoundRect(_threshold.ThresholdModeBounds, 3, 3, fr);
        FUIRenderer.DrawTextCentered(canvas, "Threshold", _threshold.ThresholdModeBounds, thText, 13f);

        y += btnHeight + 4;
        return y;
    }

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

    private static void DrawToggleButton(SKCanvas canvas, SKRect bounds, string text, bool active, bool hovered)
    {
        var bg = active ? FUIColors.Active.WithAlpha(FUIColors.AlphaGlow) : (hovered ? FUIColors.Primary.WithAlpha(40) : FUIColors.Background2);
        var frame = active ? FUIColors.Active : (hovered ? FUIColors.FrameBright : FUIColors.Frame);
        var textColor = active ? FUIColors.TextBright : (hovered ? FUIColors.TextPrimary : FUIColors.TextDim);

        using (var bgPaint = FUIRenderer.CreateFillPaint(bg))
            canvas.DrawRoundRect(bounds, 3, 3, bgPaint);
        using (var framePaint = FUIRenderer.CreateStrokePaint(frame, active ? 2f : 1f))
            canvas.DrawRoundRect(bounds, 3, 3, framePaint);
        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 13f);
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
        float scaledFontSize = fontSize;
        float textY = y + (rowHeight / 2) + (scaledFontSize / 3); // Center text baseline

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
    private void DrawSharedSlotPanel(SKCanvas canvas, float leftMargin, float rightMargin, float y, List<SharedSlotInfo> infos, uint rowVJoyDevice)
    {
        // Banner — blue rounded panel with [SHARED] pill + summary count.
        var bannerRect = new SKRect(leftMargin, y, rightMargin, y + 36);
        FUIRenderer.DrawRoundedPanel(canvas, bannerRect,
            FUIColors.Primary.WithAlpha(FUIColors.AlphaLightTint),
            FUIColors.Primary.WithAlpha(FUIColors.AlphaBorderSoft));

        const float pillW = 50f;
        const float pillH = 14f;
        var pillRect = new SKRect(bannerRect.Left + 10f, bannerRect.MidY - pillH / 2f,
            bannerRect.Left + 10f + pillW, bannerRect.MidY + pillH / 2f);
        FUIRenderer.DrawRoundedPanel(canvas, pillRect,
            FUIColors.Primary.WithAlpha(FUIColors.AlphaHoverBg),
            FUIColors.Primary.WithAlpha(FUIColors.AlphaBorderSoft));
        FUIRenderer.DrawTextCentered(canvas, "SHARED", pillRect, FUIColors.Primary, 10f);

        string headline = infos.Count == 1
            ? infos[0].ActionDisplayName
            : $"{infos.Count} keybindings";
        FUIRenderer.DrawText(canvas, headline,
            new SKPoint(pillRect.Right + 10f, bannerRect.MidY + 5f), FUIColors.Primary, 13f);
        y += 36 + 12;

        FUIRenderer.DrawText(canvas, "This output is shared from the Keybindings tab.",
            new SKPoint(leftMargin, y + 14), FUIColors.TextPrimary, 13f);
        y += 22;

        // When every share targets the same primary slot we collapse the primary into a
        // single line; otherwise show the primary inline with each action so the panel
        // never lies about what's driving what.
        bool allSamePrimary = infos.All(i =>
            i.PrimaryVJoyDevice == infos[0].PrimaryVJoyDevice
            && i.PrimaryButtonIndex == infos[0].PrimaryButtonIndex);

        // List of sharing actions. Soft cap on rendered rows so the panel can't overflow
        // the right column on extreme cases — full list is still findable via the deep-link.
        const int maxVisible = 6;
        int rendered = Math.Min(infos.Count, maxVisible);
        for (int i = 0; i < rendered; i++)
        {
            var info = infos[i];
            string line = allSamePrimary
                ? $"  • {info.ActionDisplayName}"
                : $"  • {info.ActionDisplayName} (vJoy {info.PrimaryVJoyDevice} Button {info.PrimaryButtonIndex + 1})";
            FUIRenderer.DrawText(canvas, line, new SKPoint(leftMargin, y + 14), FUIColors.TextDim, 12f);
            y += 18;
        }
        if (infos.Count > maxVisible)
        {
            FUIRenderer.DrawText(canvas, $"  …and {infos.Count - maxVisible} more",
                new SKPoint(leftMargin, y + 14), FUIColors.TextDim, 12f);
            y += 18;
        }
        y += 4;

        if (allSamePrimary)
        {
            FUIRenderer.DrawText(canvas,
                $"Primary: vJoy {infos[0].PrimaryVJoyDevice} Button {infos[0].PrimaryButtonIndex + 1}",
                new SKPoint(leftMargin, y + 14), FUIColors.TextDim, 12f);
            y += 22;
        }

        FUIRenderer.DrawText(canvas, "Pressing this button now drives the primary's binding.",
            new SKPoint(leftMargin, y + 14), FUIColors.TextDim, 12f);
        y += 18;
        FUIRenderer.DrawText(canvas, "To unshare, manage it from the Keybindings tab.",
            new SKPoint(leftMargin, y + 14), FUIColors.TextDim, 12f);
        y += 28;

        // Manage button — deep-links to SC Bindings tab with search pre-filled.
        var btnBounds = new SKRect(leftMargin, y, rightMargin, y + 32);
        _sharedManageButtonBounds = btnBounds;
        _sharedManageButtonHovered = btnBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        _sharedManageSearchText = $"button{_selectedMappingRow + 1}";
        _sharedManageVJoyDevice = rowVJoyDevice;

        FUIRenderer.DrawButton(canvas, btnBounds, "MANAGE IN KEYBINDINGS",
            _sharedManageButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
    }

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

    private void DrawCurveVisualization(SKCanvas canvas, SKRect bounds)
    {
        // Background - darker than the panel
        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background0);
        canvas.DrawRect(bounds, bgPaint);

        // Grid lines (10% increments) - visible but subtle
        using var gridPaint = FUIRenderer.CreateStrokePaint(new SKColor(60, 70, 80));

        for (float t = 0.1f; t < 1f; t += 0.1f)
        {
            // Skip 50% line - we'll draw it brighter
            if (Math.Abs(t - 0.5f) < 0.01f) continue;

            float x = bounds.Left + t * bounds.Width;
            float y = bounds.Bottom - t * bounds.Height;
            canvas.DrawLine(x, bounds.Top, x, bounds.Bottom, gridPaint);
            canvas.DrawLine(bounds.Left, y, bounds.Right, y, gridPaint);
        }

        // Center lines (brighter, 50% mark)
        using var centerPaint = FUIRenderer.CreateStrokePaint(new SKColor(80, 95, 110));
        canvas.DrawLine(bounds.MidX, bounds.Top, bounds.MidX, bounds.Bottom, centerPaint);
        canvas.DrawLine(bounds.Left, bounds.MidY, bounds.Right, bounds.MidY, centerPaint);

        // Reference linear line (dashed diagonal)
        using var refPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.FrameSubtle,
            StrokeWidth = 1f,
            PathEffect = SKPathEffect.CreateDash(new[] { 4f, 4f }, 0)
        };
        canvas.DrawLine(bounds.Left, bounds.Bottom, bounds.Right, bounds.Top, refPaint);

        // Draw the curve
        DrawCurvePath(canvas, bounds);

        // Draw control points (only for custom curve)
        if (_curve.SelectedType == CurveType.Custom)
        {
            DrawCurveControlPoints(canvas, bounds);
        }

        // Frame
        using var framePaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame);
        canvas.DrawRect(bounds, framePaint);

        // Tick marks and labels on edges
        using var tickPaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame.WithAlpha(150));

        float tickLen = 4f;
        float labelOffset = 3f;

        // Draw tick marks at 0%, 50%, 100% on bottom edge (IN axis)
        float[] tickPositions = { 0f, 0.5f, 1f };
        string[] tickLabels = { "0", "50", "100" };

        for (int i = 0; i < tickPositions.Length; i++)
        {
            float t = tickPositions[i];
            float x = bounds.Left + t * bounds.Width;

            // Bottom tick
            canvas.DrawLine(x, bounds.Bottom, x, bounds.Bottom + tickLen, tickPaint);

            // Label below tick
            float labelX = x - (t == 0 ? 0 : (t == 1 ? 12 : 6));
            FUIRenderer.DrawText(canvas, tickLabels[i], new SKPoint(labelX, bounds.Bottom + tickLen + labelOffset + 7), FUIColors.TextDim, 12f);
        }

        // Draw tick marks at 0%, 50%, 100% on left edge (OUT axis)
        for (int i = 0; i < tickPositions.Length; i++)
        {
            float t = tickPositions[i];
            float y = bounds.Bottom - t * bounds.Height;

            // Left tick
            canvas.DrawLine(bounds.Left - tickLen, y, bounds.Left, y, tickPaint);

            // Label left of tick
            float labelY = y + (t == 0 ? 3 : (t == 1 ? 7 : 3));
            float labelX = bounds.Left - tickLen - labelOffset - (tickLabels[i].Length > 1 ? 12 : 6);
            FUIRenderer.DrawText(canvas, tickLabels[i], new SKPoint(labelX, labelY), FUIColors.TextDim, 12f);
        }

    }

    private float DrawAxisMovementIndicator(SKCanvas canvas, float leftMargin, float rightMargin, float y, AxisMapping axisMapping)
    {
        float width = rightMargin - leftMargin;
        float startY = y;

        // Get current raw input values for all input sources
        float rawInput = 0f;
        bool hasInput = false;

        if (axisMapping.Inputs.Count > 0)
        {
            var inputValues = new List<float>();

            foreach (var input in axisMapping.Inputs)
            {
                // Find the physical device
                var device = _ctx.Devices.FirstOrDefault(d => d.InstanceGuid.ToString() == input.DeviceId);
                if (device is null) continue;

                // Get the device state from InputService
                var state = _ctx.InputService.GetDeviceState(device.DeviceIndex);
                if (state is null || input.Index >= state.Axes.Length) continue;

                inputValues.Add(state.Axes[input.Index]);
                hasInput = true;
            }

            // Merge multiple inputs according to merge operation
            if (inputValues.Count > 0)
            {
                rawInput = axisMapping.MergeOp switch
                {
                    MergeOperation.Average => inputValues.Average(),
                    MergeOperation.Maximum => inputValues.Max(),
                    MergeOperation.Minimum => inputValues.Min(),
                    MergeOperation.Sum => Math.Clamp(inputValues.Sum(), -1f, 1f),
                    _ => inputValues[0]
                };
            }
        }

        // Apply the curve to get processed output
        float processedOutput = hasInput ? axisMapping.Curve.Apply(rawInput) : 0f;

        // Check if this is a centered axis (joystick) or end-only (throttle/slider)
        // Auto-detect based on output axis type if mode is set to default Centered
        bool isCentered;
        if (axisMapping.Curve.DeadzoneMode == DeadzoneMode.Centered)
        {
            // Auto-detect: Z axis and sliders are typically end-only (throttles)
            // X, Y, RX, RY, RZ are typically centered (joysticks)
            int outputIndex = axisMapping.Output.Index;
            isCentered = outputIndex switch
            {
                2 => false,  // Z axis - throttle
                6 => false,  // Slider1
                7 => false,  // Slider2
                _ => true    // X, Y, RX, RY, RZ - joystick axes
            };
        }
        else
        {
            isCentered = axisMapping.Curve.DeadzoneMode == DeadzoneMode.Centered;
        }

        // Convert to percentages for display
        float rawPercent, outPercent;
        if (isCentered)
        {
            // Centered: -100% to +100%
            rawPercent = rawInput * 100f;
            outPercent = processedOutput * 100f;
        }
        else
        {
            // End-only: 0% to 100% (convert from -1..1 to 0..100)
            rawPercent = (rawInput + 1f) * 50f;
            outPercent = (processedOutput + 1f) * 50f;
        }

        // Draw section header with live values
        string headerText = hasInput
            ? (isCentered
                ? $"LIVE INPUT: {rawPercent:+0;-0;0}%  >>  OUTPUT: {outPercent:+0;-0;0}%"
                : $"LIVE INPUT: {rawPercent:0}%  >>  OUTPUT: {outPercent:0}%")
            : "LIVE INPUT: (no signal)";

        var headerColor = hasInput ? FUIColors.Active : FUIColors.TextDim.WithAlpha(150);
        FUIRenderer.DrawText(canvas, headerText, new SKPoint(leftMargin, y), headerColor, 12f);
        y += 16f;

        if (hasInput)
        {
            // Draw a visual bar indicator for the processed output
            float barHeight = 8f;
            var barBounds = new SKRect(leftMargin, y, rightMargin, y + barHeight);

            // Background
            using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background0);
            canvas.DrawRect(barBounds, bgPaint);

            // Convert output value to bar position (0..1)
            float normalizedValue = (processedOutput + 1f) / 2f;
            float barX = barBounds.Left + normalizedValue * barBounds.Width;

            if (isCentered)
            {
                // Center line for centered axes
                using var centerPaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame);
                canvas.DrawLine(barBounds.MidX, barBounds.Top, barBounds.MidX, barBounds.Bottom, centerPaint);

                // Fill from center to current position
                var fillBounds = processedOutput >= 0
                    ? new SKRect(barBounds.MidX, barBounds.Top, barX, barBounds.Bottom)
                    : new SKRect(barX, barBounds.Top, barBounds.MidX, barBounds.Bottom);

                using var fillPaint = FUIRenderer.CreateFillPaint(FUIColors.ActiveStrong);
                canvas.DrawRect(fillBounds, fillPaint);
            }
            else
            {
                // Fill from left edge to current position (for sliders/throttles)
                var fillBounds = new SKRect(barBounds.Left, barBounds.Top, barX, barBounds.Bottom);
                using var fillPaint = FUIRenderer.CreateFillPaint(FUIColors.ActiveStrong);
                canvas.DrawRect(fillBounds, fillPaint);
            }

            // Position indicator (vertical line)
            using var indicatorPaint = FUIRenderer.CreateStrokePaint(FUIColors.Active, 2f);
            canvas.DrawLine(barX, barBounds.Top, barX, barBounds.Bottom, indicatorPaint);

            // Frame
            using var framePaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame);
            canvas.DrawRect(barBounds, framePaint);

            y += barHeight + 14f;  // baseline needs +14 so text top (baseline-10) clears bar bottom

            // Labels below bar - different for centered vs end-only
            if (isCentered)
            {
                FUIRenderer.DrawText(canvas, "-100%", new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, "0%", new SKPoint(barBounds.MidX - 8, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, "+100%", new SKPoint(rightMargin - 28, y), FUIColors.TextDim, 12f);
            }
            else
            {
                FUIRenderer.DrawText(canvas, "0%", new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, "50%", new SKPoint(barBounds.MidX - 8, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, "100%", new SKPoint(rightMargin - 20, y), FUIColors.TextDim, 12f);
            }
            y += 12f;
        }

        return y - startY;
    }

    private void DrawCurvePath(SKCanvas canvas, SKRect bounds)
    {
        using var path = new SKPath();
        bool first = true;

        // Sample the curve at many points
        for (float t = 0; t <= 1.001f; t += 0.01f)
        {
            float input = Math.Min(t, 1f);
            float output = ComputeCurveValue(input);

            float x = bounds.Left + input * bounds.Width;
            float y = bounds.Bottom - output * bounds.Height;

            if (first)
            {
                path.MoveTo(x, y);
                first = false;
            }
            else
            {
                path.LineTo(x, y);
            }
        }

        // Glow
        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active.WithAlpha(50),
            StrokeWidth = 5f,
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(4f, 4f)
        };
        canvas.DrawPath(path, glowPaint);

        // Main line
        using var linePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active,
            StrokeWidth = 2f,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };
        canvas.DrawPath(path, linePaint);
    }

    private float ComputeCurveValue(float input)
    {
        // Apply curve type only - deadzone is handled separately
        float output = _curve.SelectedType switch
        {
            CurveType.Linear => input,
            CurveType.SCurve => ApplySCurve(input),
            CurveType.Exponential => ApplyExponential(input),
            CurveType.Custom => InterpolateControlPoints(input),
            _ => input
        };

        output = Math.Clamp(output, 0f, 1f);

        // Apply inversion
        if (_deadzone.AxisInverted)
            output = 1f - output;

        return output;
    }

    private static float ApplySCurve(float x)
    {
        // S-curve using smoothstep-like function
        return x * x * (3f - 2f * x);
    }

    private static float ApplyExponential(float x)
    {
        // Exponential curve (steeper at the end)
        return x * x;
    }

    private float InterpolateControlPoints(float x)
    {
        if (_curve.ControlPoints.Count < 2) return x;

        // Find segment containing x
        for (int i = 0; i < _curve.ControlPoints.Count - 1; i++)
        {
            var p1 = _curve.ControlPoints[i];
            var p2 = _curve.ControlPoints[i + 1];

            if (x >= p1.X && x <= p2.X)
            {
                if (Math.Abs(p2.X - p1.X) < 0.001f) return p1.Y;
                float t = (x - p1.X) / (p2.X - p1.X);

                // Use Catmull-Rom spline interpolation for smooth curves
                // Need 4 points: p0, p1, p2, p3
                var p0 = i > 0 ? _curve.ControlPoints[i - 1] : new SKPoint(p1.X - (p2.X - p1.X), p1.Y - (p2.Y - p1.Y));
                var p3 = i < _curve.ControlPoints.Count - 2 ? _curve.ControlPoints[i + 2] : new SKPoint(p2.X + (p2.X - p1.X), p2.Y + (p2.Y - p1.Y));

                return CatmullRomInterpolate(p0.Y, p1.Y, p2.Y, p3.Y, t);
            }
        }

        // Extrapolate
        return x < _curve.ControlPoints[0].X ? _curve.ControlPoints[0].Y : _curve.ControlPoints[^1].Y;
    }

    /// <summary>
    /// Catmull-Rom spline interpolation for smooth curves through control points.
    /// t ranges from 0 to 1, output is between p1 and p2.
    /// </summary>
    private static float CatmullRomInterpolate(float p0, float p1, float p2, float p3, float t)
    {
        // Catmull-Rom spline formula with tension = 0.5 (centripetal)
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    private void DrawCurveControlPoints(SKCanvas canvas, SKRect bounds)
    {
        const float PointRadius = 7f;
        const float CenterPointRadius = 3.5f; // Half size for center point

        for (int i = 0; i < _curve.ControlPoints.Count; i++)
        {
            var pt = _curve.ControlPoints[i];
            float x = bounds.Left + pt.X * bounds.Width;

            // Apply inversion to display Y position to match the curve
            float displayY = _deadzone.AxisInverted ? (1f - pt.Y) : pt.Y;
            float y = bounds.Bottom - displayY * bounds.Height;

            bool isHovered = i == _curve.HoveredPoint;
            bool isDragging = i == _curve.DraggingPoint;
            bool isEndpoint = i == 0 || i == _curve.ControlPoints.Count - 1;
            bool isCenterPoint = Math.Abs(pt.X - 0.5f) < 0.01f && Math.Abs(pt.Y - 0.5f) < 0.01f;

            // Center point is smaller and not interactive
            float baseRadius = isCenterPoint ? CenterPointRadius : PointRadius;
            float radius = (isHovered || isDragging) && !isCenterPoint ? baseRadius + 2 : baseRadius;
            var color = isDragging ? FUIColors.Warning : (isHovered && !isCenterPoint ? FUIColors.TextBright : FUIColors.Active);

            // Glow (skip for center point)
            if (!isCenterPoint)
            {
                using var glowPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = color.WithAlpha(40),
                    IsAntialias = true,
                    ImageFilter = SKImageFilter.CreateBlur(5f, 5f)
                };
                canvas.DrawCircle(x, y, radius + 4, glowPaint);
            }

            // Fill
            using var fillPaint = FUIRenderer.CreateFillPaint(isEndpoint || isCenterPoint ? FUIColors.Background1 : color.WithAlpha(60));
            canvas.DrawCircle(x, y, radius, fillPaint);

            // Stroke
            using var strokePaint = FUIRenderer.CreateStrokePaint(isCenterPoint ? FUIColors.Frame : color, isEndpoint ? 2f : (isCenterPoint ? 1f : 1.5f));
            canvas.DrawCircle(x, y, radius, strokePaint);

            // Value label when hovered/dragged (not for center point)
            if ((isHovered || isDragging) && !isCenterPoint)
            {
                string label = $"({pt.X:F2}, {pt.Y:F2})";
                float labelY = y - radius - 10;
                if (labelY < bounds.Top + 10)
                    labelY = y + radius + 14;

                FUIRenderer.DrawText(canvas, label, new SKPoint(x - 22, labelY), FUIColors.TextBright, 12f);
            }
        }
    }

    private SKPoint CurveScreenToGraph(SKPoint screenPt, SKRect bounds)
    {
        float x = (screenPt.X - bounds.Left) / bounds.Width;
        float y = (bounds.Bottom - screenPt.Y) / bounds.Height;

        // If inverted, convert screen Y back to graph Y (uninvert)
        if (_deadzone.AxisInverted)
            y = 1f - y;

        return new SKPoint(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
    }

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

    private void DrawMappingRow(SKCanvas canvas, SKRect bounds, string outputName, string binding,
        bool isSelected, bool isHovered, bool isEditing, int rowIndex, bool hasBind)
    {
        // Background
        SKColor bgColor;
        if (isEditing)
            bgColor = FUIColors.Active.WithAlpha(FUIColors.AlphaGlow);
        else if (isSelected)
            bgColor = FUIColors.SelectionBg;
        else if (isHovered)
            bgColor = FUIColors.Primary.WithAlpha(30);
        else
            bgColor = FUIColors.DisabledBg;

        using var bgPaint = FUIRenderer.CreateFillPaint(bgColor);
        canvas.DrawRect(bounds, bgPaint);

        // Frame
        using var framePaint = FUIRenderer.CreateStrokePaint(
            isEditing ? FUIColors.Active : (isSelected ? FUIColors.SelectionBorder : (isHovered ? FUIColors.FrameBright : FUIColors.Frame.WithAlpha(FUIColors.AlphaHoverStrong))),
            isEditing ? 2f : (isSelected ? 1.5f : 1f));
        canvas.DrawRect(bounds, framePaint);

        // Output name (left)
        float textY = bounds.MidY + 5;
        FUIRenderer.DrawText(canvas, outputName, new SKPoint(bounds.Left + 10, textY),
            FUIColors.ContentColor(isEditing), 15f);

        // Binding (center)
        float bindingX = bounds.Left + 100;
        var bindColor = binding == "ÔÇö" ? FUIColors.TextDisabled : FUIColors.TextDim;
        FUIRenderer.DrawText(canvas, binding, new SKPoint(bindingX, textY), bindColor, 14f);

        // [+] button (Edit/Add)
        float buttonSize = 24f;
        float buttonY = bounds.MidY - buttonSize / 2;
        float addButtonX = bounds.Right - (hasBind ? 60 : 36);
        var addBounds = new SKRect(addButtonX, buttonY, addButtonX + buttonSize, buttonY + buttonSize);
        _mappingAddButtonBounds.Add(addBounds);

        bool addHovered = rowIndex == _hoveredAddButton;
        string addIcon = hasBind ? "Ô£Ä" : "+";  // Pencil for edit, plus for add
        FUIWidgets.DrawSmallIconButton(canvas, addBounds, addIcon, addHovered);

        // [X] button (only if bound)
        if (hasBind)
        {
            float removeButtonX = bounds.Right - 32;
            var removeBounds = new SKRect(removeButtonX, buttonY, removeButtonX + buttonSize, buttonY + buttonSize);
            _mappingRemoveButtonBounds.Add(removeBounds);

            bool removeHovered = rowIndex == _hoveredRemoveButton;
            FUIWidgets.DrawSmallIconButton(canvas, removeBounds, "X", removeHovered, true);
        }
        else
        {
            _mappingRemoveButtonBounds.Add(SKRect.Empty);
        }
    }

    private void DrawMappingList(SKCanvas canvas, SKRect bounds)
    {
        float itemHeight = 50f;
        float itemGap = 8f;
        float y = bounds.Top;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null)
        {
            FUIRenderer.DrawText(canvas, "No configuration selected",
                new SKPoint(bounds.Left + 20, y + 20), FUIColors.TextDim, 15f);
            FUIRenderer.DrawText(canvas, "Select or create a configuration to add mappings",
                new SKPoint(bounds.Left + 20, y + 40), FUIColors.TextDisabled, 14f);
            return;
        }

        var allMappings = new List<(string source, string target, string type, bool enabled)>();

        // Collect all mappings
        foreach (var m in profile.ButtonMappings)
        {
            string source = m.Inputs.Count > 0 ? $"{m.Inputs[0].DeviceName} Btn {m.Inputs[0].Index + 1}" : "Unknown";
            string target = m.Output.Type == OutputType.VJoyButton
                ? $"vJoy {m.Output.VJoyDevice} Btn {m.Output.Index}"
                : $"Key {m.Output.Index}";
            allMappings.Add((source, target, "BUTTON", m.Enabled));
        }

        foreach (var m in profile.AxisMappings)
        {
            string source = m.Inputs.Count > 0 ? $"{m.Inputs[0].DeviceName} Axis {m.Inputs[0].Index}" : "Unknown";
            string target = $"vJoy {m.Output.VJoyDevice} Axis {m.Output.Index}";
            allMappings.Add((source, target, "AXIS", m.Enabled));
        }

        if (allMappings.Count == 0)
        {
            FUIRenderer.DrawText(canvas, "No mappings configured",
                new SKPoint(bounds.Left + 20, y + 20), FUIColors.TextDim, 15f);
            FUIRenderer.DrawText(canvas, "Click '+ ADD MAPPING' to create your first mapping",
                new SKPoint(bounds.Left + 20, y + 40), FUIColors.TextDisabled, 14f);
            return;
        }

        // Draw mapping items
        foreach (var (source, target, type, enabled) in allMappings)
        {
            if (y + itemHeight > bounds.Bottom) break;

            var itemBounds = new SKRect(bounds.Left, y, bounds.Right, y + itemHeight);
            FUIWidgets.DrawMappingItem(canvas, itemBounds, source, target, type, enabled);
            y += itemHeight + itemGap;
        }
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
    private SKPoint ViewBoxToScreen(float viewBoxX, float viewBoxY)
    {
        float screenX = _ctx.SvgMirrored
            ? _ctx.SvgOffset.X + _ctx.SilhouetteSourceWidth * _ctx.SvgScale - viewBoxX * _ctx.SvgScale
            : _ctx.SvgOffset.X + viewBoxX * _ctx.SvgScale;

        float screenY = _ctx.SvgOffset.Y + viewBoxY * _ctx.SvgScale;
        return new SKPoint(screenX, screenY);
    }

    /// <summary>
    /// Draws a lead line from the highlighted control anchor to its label position.
    /// Fades over 1 second hold + 2 seconds fade.
    /// </summary>
    private void DrawMappingHighlightLeadLine(SKCanvas canvas, SKRect panelBounds)
    {
        if (_highlight.ControlDef?.Anchor is null) return;

        float elapsed = (Environment.TickCount64 - _highlight.ControlHighlightTicks) / 1000f;
        float opacity = elapsed < 1f ? 1f : Math.Max(0f, 1f - (elapsed - 1f) / 2f);
        if (opacity < 0.01f) return;

        SKPoint anchorScreen = ViewBoxToScreen(_highlight.ControlDef.Anchor.X, _highlight.ControlDef.Anchor.Y);

        float labelX, labelY;
        bool goesRight = true;

        if (_highlight.ControlDef.LabelOffset is not null)
        {
            var labelScreen = ViewBoxToScreen(
                _highlight.ControlDef.Anchor.X + _highlight.ControlDef.LabelOffset.X,
                _highlight.ControlDef.Anchor.Y + _highlight.ControlDef.LabelOffset.Y);
            labelX = labelScreen.X;
            labelY = labelScreen.Y;
            bool offsetGoesRight = _highlight.ControlDef.LabelOffset.X >= 0;
            goesRight = _ctx.SvgMirrored ? !offsetGoesRight : offsetGoesRight;
        }
        else
        {
            labelY = panelBounds.Top + 80;
            labelX = _ctx.SilhouetteBounds.Right + 20;
        }

        var fakeInput = new ActiveInputState
        {
            Binding = _highlight.ControlDef.Label,
            Value = 1f,
            IsAxis = false,
            Control = _highlight.ControlDef,
            LastActivity = DateTime.Now, // approximate — uses current time since highlight ticks aren't DateTime
            AppearProgress = 1f
        };

        DeviceLeadLineRenderer.DrawInputLeadLine(
            canvas, anchorScreen, new SKPoint(labelX, labelY),
            goesRight, opacity, fakeInput, _ctx.SvgMirrored, _ctx.SvgScale);
    }

}
