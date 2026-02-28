using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
    private void DrawSvgInBounds(SKCanvas canvas, SKSvg svg, SKRect bounds, bool mirror = false)
    {
        if (svg.Picture is null) return;

        var svgBounds = svg.Picture.CullRect;
        if (svgBounds.Width <= 0 || svgBounds.Height <= 0) return;

        float scaleX = bounds.Width / svgBounds.Width;
        float scaleY = bounds.Height / svgBounds.Height;
        float scale = Math.Min(scaleX, scaleY) * 0.95f;

        float scaledWidth = svgBounds.Width * scale;
        float scaledHeight = svgBounds.Height * scale;

        float offsetX = bounds.Left + (bounds.Width - scaledWidth) / 2 - svgBounds.Left * scale;
        float offsetY = bounds.Top + (bounds.Height - scaledHeight) / 2 - svgBounds.Top * scale;

        _ctx.SvgScale = scale;
        _ctx.SvgOffset = new SKPoint(offsetX, offsetY);
        _ctx.SvgMirrored = mirror;

        canvas.Save();
        canvas.Translate(offsetX, offsetY);

        if (mirror)
        {
            canvas.Translate(scaledWidth, 0);
            canvas.Scale(-scale, scale);
        }
        else
        {
            canvas.Scale(scale);
        }

        canvas.DrawPicture(svg.Picture);
        canvas.Restore();
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

        // RIGHT PANEL - Settings
        DrawMappingSettingsPanel(canvas, rightBounds, frameInset);
    }

    private void DrawBindingsPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Vertical side tabs width
        float sideTabWidth = 28f;

        // Panel shadow
        FUIRenderer.DrawPanelShadow(canvas, bounds, 3f, 3f, 10f);

        // Panel background (shifted right to make room for side tabs)
        var contentBounds = new SKRect(bounds.Left + frameInset + sideTabWidth, bounds.Top + frameInset,
                                        bounds.Right - frameInset, bounds.Bottom - frameInset);
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(140),
            IsAntialias = true
        };
        canvas.DrawRect(contentBounds, bgPaint);

        // Draw vertical side tabs (M1 Axes, M2 Buttons)
        DrawMappingCategorySideTabs(canvas, bounds.Left + frameInset, bounds.Top + frameInset,
            sideTabWidth, bounds.Height - frameInset * 2);

        // L-corner frame (adjusted for side tabs)
        var frameBounds = new SKRect(bounds.Left + sideTabWidth, bounds.Top, bounds.Right, bounds.Bottom);
        FUIRenderer.DrawLCornerFrame(canvas, frameBounds, FUIColors.Frame, 40f, 10f);

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
        _bindingsListBounds = bounds;

        var profile = _ctx.ProfileManager.ActiveProfile;

        bool hasVJoy = _ctx.VJoyDevices.Count > 0 && _ctx.SelectedVJoyDeviceIndex < _ctx.VJoyDevices.Count;
        VJoyDeviceInfo? vjoyDevice = hasVJoy ? _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex] : null;

        float rowHeight = 32f;  // Compact rows
        float rowGap = 4f;

        // Get counts based on current category
        string[] axisNames = { "X Axis", "Y Axis", "Z Axis", "RX Axis", "RY Axis", "RZ Axis", "Slider 1", "Slider 2" };
        int axisCount = hasVJoy ? Math.Min(axisNames.Length, 8) : 0;
        int buttonCount = vjoyDevice?.ButtonCount ?? 0;

        // Calculate content height based on selected category (no section headers when filtered)
        // Category 0 = Buttons, Category 1 = Axes
        int itemCount = _mappingCategory == 0 ? buttonCount : axisCount;
        _bindingsContentHeight = itemCount * (rowHeight + rowGap);

        // Clamp scroll offset
        float maxScroll = Math.Max(0, _bindingsContentHeight - bounds.Height);
        _bindingsScrollOffset = Math.Clamp(_bindingsScrollOffset, 0, maxScroll);

        // Set up clipping
        canvas.Save();
        canvas.ClipRect(bounds);

        float y = bounds.Top - _bindingsScrollOffset;
        int rowIndex = 0;

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
                    // A button is a modifier when it outputs a single modifier key (no combo modifiers)
                    bool isModifier = keyParts?.Count == 1 && IsModifierKeyName(keyParts[0]);

                    DrawChunkyBindingRow(canvas, rowBounds, $"Button {i + 1}", binding, isSelected, isHovered, rowIndex, keyParts, isModifier);
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
            for (int i = 0; i < axisCount; i++)
            {
                float rowTop = y;
                float rowBottom = y + rowHeight;

                // Only draw if visible
                if (rowBottom > bounds.Top && rowTop < bounds.Bottom)
                {
                    var rowBounds = new SKRect(bounds.Left, rowTop, bounds.Right, rowBottom);
                    string binding = GetAxisBindingText(profile, vjoyDevice!.Id, i);
                    bool isSelected = rowIndex == _selectedMappingRow;
                    bool isHovered = rowIndex == _hoveredMappingRow;

                    DrawChunkyBindingRow(canvas, rowBounds, axisNames[i], binding, isSelected, isHovered, rowIndex);
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
        if (_bindingsContentHeight > bounds.Height)
        {
            DrawScrollIndicator(canvas, bounds, _bindingsScrollOffset, _bindingsContentHeight);
        }
    }

    /// <summary>
    /// Get the keyboard key parts for a button mapping (modifiers + key as separate items)
    /// </summary>
    private List<string>? GetKeyboardMappingParts(ButtonMapping mapping)
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
    private List<string>? GetButtonKeyParts(MappingProfile? profile, uint vjoyId, int buttonIndex)
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

    private void DrawScrollIndicator(SKCanvas canvas, SKRect bounds, float scrollOffset, float contentHeight)
    {
        float trackHeight = bounds.Height - 20;
        float thumbRatio = bounds.Height / contentHeight;
        float thumbHeight = Math.Max(20, trackHeight * thumbRatio);
        float thumbOffset = (scrollOffset / (contentHeight - bounds.Height)) * (trackHeight - thumbHeight);

        // Position track outside the list bounds, aligned with corner frame edge
        float trackX = bounds.Right + 8; // Outside cells, inline with frame
        float trackTop = bounds.Top + 10;
        float trackWidth = 3f;

        // Track (subtle)
        using var trackPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Frame.WithAlpha(40)
        };
        canvas.DrawRoundRect(new SKRect(trackX, trackTop, trackX + trackWidth, trackTop + trackHeight), 1.5f, 1.5f, trackPaint);

        // Thumb
        using var thumbPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Primary.WithAlpha(200)
        };
        canvas.DrawRoundRect(new SKRect(trackX, trackTop + thumbOffset, trackX + trackWidth, trackTop + thumbOffset + thumbHeight), 1.5f, 1.5f, thumbPaint);
    }

    private void DrawChunkyBindingRow(SKCanvas canvas, SKRect bounds, string outputName, string binding,
        bool isSelected, bool isHovered, int rowIndex, List<string>? keyParts = null, bool isModifier = false)
    {
        bool hasBinding = !string.IsNullOrEmpty(binding) && binding != "ÔÇö";
        bool hasKeyParts = keyParts is not null && keyParts.Count > 0;

        // Check for attention highlight (physical input was pressed that maps to this output)
        bool hasAttentionHighlight = false;
        float attentionIntensity = 0f;
        if (_highlightedMappingRow >= 0 &&
            _ctx.VJoyDevices.Count > 0 && _ctx.SelectedVJoyDeviceIndex < _ctx.VJoyDevices.Count)
        {
            var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
            // Parse output index from the outputName (e.g., "Button 5" -> 4, "Axis 0" -> 0)
            int outputIndex = -1;
            if (outputName.StartsWith("Button ") && int.TryParse(outputName.Substring(7), out int btnNum))
                outputIndex = btnNum - 1; // Buttons are 1-indexed in display
            else if (outputName.StartsWith("Axis ") && int.TryParse(outputName.Substring(5), out int axisNum))
                outputIndex = axisNum;

            if (outputIndex == _highlightedMappingRow && vjoyDevice.Id == _highlightedVJoyDevice)
            {
                var elapsed = (DateTime.Now - _highlightStartTime).TotalMilliseconds;
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
                    _highlightedMappingRow = -1; // Clear expired highlight
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

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRoundRect(bounds, 4, 4, bgPaint);

        // Draw attention highlight as overlay (additive, doesn't replace selection)
        if (hasAttentionHighlight)
        {
            // Pulsing glow effect that fades out - use theme active color
            byte glowAlpha = (byte)(100 * attentionIntensity);
            using var glowPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = FUIColors.Active.WithAlpha(glowAlpha)
            };
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

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = frameColor,
            StrokeWidth = frameWidth
        };
        canvas.DrawRoundRect(bounds, 4, 4, framePaint);

        // Output name (centered vertically)
        float leftTextX = bounds.Left + 12;
        FUIRenderer.DrawText(canvas, outputName, new SKPoint(leftTextX, bounds.MidY + 5),
            isSelected ? FUIColors.Active : FUIColors.TextPrimary, 15f, true);

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

                // Keycap background
                using var keycapBgPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = FUIColors.TextPrimary.WithAlpha(20),
                    IsAntialias = true
                };
                canvas.DrawRoundRect(keycapBounds, 3, 3, keycapBgPaint);

                // Keycap frame
                using var keycapFramePaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = FUIColors.TextPrimary.WithAlpha(100),
                    StrokeWidth = 1f,
                    IsAntialias = true
                };
                canvas.DrawRoundRect(keycapBounds, 3, 3, keycapFramePaint);

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

                using var modBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Primary.WithAlpha(40), IsAntialias = true };
                canvas.DrawRoundRect(modBadgeBounds, 3, 3, modBgPaint);

                using var modFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Primary.WithAlpha(180), StrokeWidth = 1f, IsAntialias = true };
                canvas.DrawRoundRect(modBadgeBounds, 3, 3, modFramePaint);

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
        else if (hasBinding)
        {
            // Binding indicator dot on the right
            float dotX = bounds.Right - 20;
            float dotY = bounds.MidY;
            using var dotPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = FUIColors.Active,
                IsAntialias = true
            };
            canvas.DrawCircle(dotX, dotY, 5f, dotPaint);
        }
    }

    private void DrawDeviceVisualizationPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(100),
            IsAntialias = true
        };
        canvas.DrawRect(new SKRect(bounds.Left + frameInset, bounds.Top + frameInset,
            bounds.Right - frameInset, bounds.Bottom - frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame.WithAlpha(150), 30f, 8f);

        // Show device silhouette - use primary device's map if available
        float centerX = bounds.MidX;

        // Get the appropriate SVG based on primary device map
        var svg = _ctx.GetSvgForDeviceMap?.Invoke(_ctx.MappingsPrimaryDeviceMap) ?? _ctx.JoystickSvg;
        bool shouldMirror = _ctx.MappingsPrimaryDeviceMap?.Mirror ?? false;

        // Device name label at top of panel
        float labelRowHeight = 20f;
        float labelY = bounds.Top + frameInset + labelRowHeight;
        string deviceLabel = _ctx.MappingsPrimaryDeviceMap?.Device ?? "ÔÇö";
        FUIRenderer.DrawTextCentered(canvas, deviceLabel,
            new SKRect(bounds.Left, bounds.Top + frameInset, bounds.Right, labelY),
            FUIColors.TextDim, 13f);

        // Reserve space at the bottom for the auto-scroll checkbox row
        float checkboxRowHeight = 26f;
        float checkboxAreaTop = bounds.Bottom - frameInset - checkboxRowHeight;

        if (svg?.Picture is not null)
        {
            // Limit size to 900px max and apply same rendering as device tab
            float maxSize = 900f;
            float maxWidth = Math.Min(bounds.Width - 40, maxSize);
            float maxHeight = Math.Min(bounds.Height - 40 - checkboxRowHeight - labelRowHeight, maxSize);

            // Create constrained bounds centered in the available area (below label, above checkbox row)
            float constrainedWidth = Math.Min(maxWidth, maxHeight); // Keep square-ish
            float constrainedHeight = constrainedWidth;
            float availableCenterY = labelY + (checkboxAreaTop - labelY) / 2f;
            var constrainedBounds = new SKRect(
                centerX - constrainedWidth / 2,
                availableCenterY - constrainedHeight / 2,
                centerX + constrainedWidth / 2,
                availableCenterY + constrainedHeight / 2
            );

            _ctx.SilhouetteBounds = constrainedBounds;
            DrawSvgInBounds(canvas, svg, constrainedBounds, shouldMirror);
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
        _autoScrollCheckboxBounds = new SKRect(leftMargin, checkboxY, leftMargin + checkboxSize, checkboxY + checkboxSize);
        FUIWidgets.DrawCheckbox(canvas, _autoScrollCheckboxBounds, _autoScrollEnabled, _ctx.MousePosition);

        var labelColor = _autoScrollCheckboxHovered ? FUIColors.TextBright : FUIColors.TextDim;
        FUIRenderer.DrawText(canvas, "AUTO-SCROLL TO MAPPING",
            new SKPoint(leftMargin + checkboxSize + 7, checkboxY + checkboxSize - 1),
            labelColor, 13f);

        // "No mapping" flash indicator ÔÇö centered above the checkbox row, fades out
        if (_noMappingFlashText is not null)
        {
            float elapsed = (float)(DateTime.Now - _noMappingFlashTime).TotalSeconds;
            float opacity = elapsed < 1f ? 1f : Math.Max(0f, 1f - (elapsed - 1f) / 1.5f);
            if (opacity > 0.01f)
            {
                var noMapColor = FUIColors.Warning.WithAlpha((byte)(opacity * 220));
                FUIRenderer.DrawTextCentered(canvas, _noMappingFlashText,
                    new SKRect(bounds.Left, checkboxAreaTop - 22, bounds.Right, checkboxAreaTop),
                    noMapColor, 13f);
            }
            else
            {
                _noMappingFlashText = null;
            }
        }
    }

    private void DrawMappingSettingsPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(140),
            IsAntialias = true
        };
        canvas.DrawRect(new SKRect(bounds.Left + frameInset, bounds.Top + frameInset,
            bounds.Right - frameInset, bounds.Bottom - frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame, 30f, 8f);

        float y = bounds.Top + frameInset + 10;
        float leftMargin = bounds.Left + frameInset + 16;
        float rightMargin = bounds.Right - frameInset - 16;

        // Title
        FUIRenderer.DrawText(canvas, "MAPPING SETTINGS", new SKPoint(leftMargin, y + 12), FUIColors.TextBright, 17f, true);
        y += 36;

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

        // INPUT SOURCES section - shows mapped inputs with add/remove
        y = DrawInputSourcesSection(canvas, leftMargin, rightMargin, y);

        float bottomMargin = bounds.Bottom - frameInset - 10;

        if (isAxis)
        {
            DrawAxisSettings(canvas, leftMargin, rightMargin, y, bottomMargin);
        }
        else
        {
            DrawButtonSettings(canvas, leftMargin, rightMargin, y, bottomMargin);
        }
    }

    private float DrawInputSourcesSection(SKCanvas canvas, float leftMargin, float rightMargin, float y)
    {
        _inputSourceRemoveBounds.Clear();

        FUIRenderer.DrawText(canvas, "INPUT SOURCES", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        y += 18;

        // Get current mappings for selected output
        var inputs = GetInputsForSelectedOutput();
        bool isListening = _isListeningForInput;

        float rowHeight = 40f;  // Two-line layout
        float rowGap = 4f;

        if (inputs.Count == 0 && !isListening)
        {
            // No inputs - show "None" with dashed border
            var emptyBounds = new SKRect(leftMargin, y, rightMargin, y + 28);
            using var emptyBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1.WithAlpha(100) };
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

                // Row background
                using var rowBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1 };
                canvas.DrawRoundRect(rowBounds, 3, 3, rowBgPaint);

                using var rowFramePaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = FUIColors.Frame,
                    StrokeWidth = 1f
                };
                canvas.DrawRoundRect(rowBounds, 3, 3, rowFramePaint);

                // Line 1: Input type and index (e.g., "Button 5") - vertically centered in top half
                string inputTypeText = input.Type == InputType.Button
                    ? $"Button {input.Index + 1}"
                    : $"{input.Type} {input.Index}";
                FUIRenderer.DrawText(canvas, inputTypeText, new SKPoint(leftMargin + 8, y + 16), FUIColors.TextPrimary, 14f);

                // Line 2: Device name (smaller, dimmer) - vertically centered in bottom half
                FUIRenderer.DrawText(canvas, input.DeviceName, new SKPoint(leftMargin + 8, y + 32), FUIColors.TextDim, 12f);

                // Remove [├ù] button (full height of row)
                var removeBounds = new SKRect(rightMargin - 26, y, rightMargin, y + rowHeight);
                bool removeHovered = _hoveredInputSourceRemove == i;

                using var removeBgPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = removeHovered ? FUIColors.Warning.WithAlpha(40) : FUIColors.Background2
                };
                canvas.DrawRoundRect(removeBounds, 3, 3, removeBgPaint);

                using var removeFramePaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = removeHovered ? FUIColors.Warning : FUIColors.Frame,
                    StrokeWidth = 1f
                };
                canvas.DrawRoundRect(removeBounds, 3, 3, removeFramePaint);

                FUIRenderer.DrawTextCentered(canvas, "├ù", removeBounds,
                    removeHovered ? FUIColors.Warning : FUIColors.TextDim, 14f);

                _inputSourceRemoveBounds.Add(removeBounds);
                y += rowHeight + rowGap;
            }
        }

        // Listening indicator with timeout bar
        if (isListening)
        {
            // Check for timeout
            var elapsed = (DateTime.Now - _inputListeningStartTime).TotalMilliseconds;
            if (elapsed >= InputListeningTimeoutMs)
            {
                CancelInputListening(); // Timeout - cancel listening
            }
            else
            {
                var listenBounds = new SKRect(leftMargin, y, rightMargin, y + rowHeight);
                byte alpha = (byte)(180 + MathF.Sin(_ctx.PulsePhase * 3) * 60);

                using var listenBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(40) };
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
                    using var progressPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Fill,
                        Color = FUIColors.Active.WithAlpha(80)
                    };
                    canvas.DrawRoundRect(progressRect, 2, 2, progressPaint);
                }

                using var listenFramePaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = FUIColors.Active.WithAlpha(alpha),
                    StrokeWidth = 2f
                };
                canvas.DrawRoundRect(listenBounds, 3, 3, listenFramePaint);

                FUIRenderer.DrawText(canvas, "Press input...", new SKPoint(leftMargin + 10, y + 18),
                    FUIColors.Active.WithAlpha(alpha), 14f);
                y += rowHeight + rowGap;
            }
        }

        // Add input button [+]
        var addBounds = new SKRect(leftMargin, y, rightMargin, y + 28);
        _addInputButtonBounds = addBounds;
        bool addHovered = _addInputButtonHovered;

        using var addBgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = addHovered ? FUIColors.Active.WithAlpha(40) : FUIColors.Background2
        };
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
            addHovered ? FUIColors.Active : FUIColors.TextPrimary, 14f);
        y += 28 + 8;  // Button height + small gap

        // Merge operation selector (only for axes with 2+ inputs)
        bool isAxis = _mappingCategory == 1;
        if (isAxis && inputs.Count >= 2)
        {
            y = DrawMergeOperationSelector(canvas, leftMargin, rightMargin, y);
        }
        else
        {
            // Clear merge button bounds when not shown
            for (int i = 0; i < _mergeOpButtonBounds.Length; i++)
                _mergeOpButtonBounds[i] = SKRect.Empty;
            y += 8;  // Extra spacing when no merge selector
        }

        return y;
    }

    private float DrawMergeOperationSelector(SKCanvas canvas, float leftMargin, float rightMargin, float y)
    {
        var axisMapping = GetCurrentAxisMapping();
        if (axisMapping is null) return y;

        // Section header with top margin
        y += 12;  // Space before section
        FUIRenderer.DrawText(canvas, "MERGE MODE", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        y += 16;

        // Four merge operation buttons in a row
        string[] labels = { "AVG", "MAX", "MIN", "SUM" };
        MergeOperation[] ops = { MergeOperation.Average, MergeOperation.Maximum, MergeOperation.Minimum, MergeOperation.Sum };

        float width = rightMargin - leftMargin;
        float buttonWidth = (width - 12) / 4; // 3 gaps of 4px each
        float buttonHeight = 28f;  // 4px aligned, meets minimum touch target

        for (int i = 0; i < 4; i++)
        {
            var btnBounds = new SKRect(
                leftMargin + i * (buttonWidth + 4), y,
                leftMargin + i * (buttonWidth + 4) + buttonWidth, y + buttonHeight);
            _mergeOpButtonBounds[i] = btnBounds;

            bool isActive = axisMapping.MergeOp == ops[i];
            bool isHovered = _hoveredMergeOpButton == i;

            var bgColor = isActive ? FUIColors.Active.WithAlpha(60) : (isHovered ? FUIColors.Primary.WithAlpha(40) : FUIColors.Background2);
            var frameColor = isActive ? FUIColors.Active : (isHovered ? FUIColors.FrameBright : FUIColors.Frame);
            var textColor = isActive ? FUIColors.TextBright : (isHovered ? FUIColors.TextPrimary : FUIColors.TextDim);

            using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
            canvas.DrawRoundRect(btnBounds, 3, 3, bgPaint);

            using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = frameColor, StrokeWidth = isActive ? 2f : 1f };
            canvas.DrawRoundRect(btnBounds, 3, 3, framePaint);

            FUIRenderer.DrawTextCentered(canvas, labels[i], btnBounds, textColor, 13f);
        }

        y += buttonHeight + 16;  // Move well below buttons (4px aligned)

        // Description of current merge mode
        string description = axisMapping.MergeOp switch
        {
            MergeOperation.Average => "Averages all input values",
            MergeOperation.Maximum => "Uses highest input value",
            MergeOperation.Minimum => "Uses lowest input value",
            MergeOperation.Sum => "Adds values (clamped -1 to 1)",
            _ => ""
        };
        FUIRenderer.DrawText(canvas, description, new SKPoint(leftMargin, y), FUIColors.TextDisabled, 12f);
        y += 16;  // Space after description before next section

        return y;
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
        int outputIndex = _selectedMappingRow;

        if (isAxis)
        {
            var mapping = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);
            if (mapping is not null)
                inputs.AddRange(mapping.Inputs);
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
            // Axes
            string[] axisNames = { "X Axis", "Y Axis", "Z Axis", "RX Axis", "RY Axis", "RZ Axis", "Slider 1", "Slider 2" };
            return _selectedMappingRow < axisNames.Length ? axisNames[_selectedMappingRow] : $"Axis {_selectedMappingRow}";
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
        int outputIndex = _selectedMappingRow;

        return profile.AxisMappings.FirstOrDefault(m =>
            m.Output.Type == OutputType.VJoyAxis &&
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);
    }

    private void DrawAxisSettings(SKCanvas canvas, float leftMargin, float rightMargin, float y, float bottom)
    {
        float width = rightMargin - leftMargin;

        // Response Curve header (with top margin for section separation)
        y += 8;  // Section separation
        FUIRenderer.DrawText(canvas, "RESPONSE CURVE", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        y += 16f;

        // Symmetrical, Centre, and Invert checkboxes on their own row
        // Symmetrical on left, Centre and Invert on right
        float checkboxSize = 12f;
        float rowHeight = 16f;
        float checkboxY = y + (rowHeight - checkboxSize) / 2; // Center checkbox in row
        float fontSize = 12f;
        float scaledFontSize = fontSize;
        float textY = y + (rowHeight / 2) + (scaledFontSize / 3); // Center text baseline

        // Measure label widths for positioning
        using var labelPaint = FUIRenderer.CreateTextPaint(FUIColors.TextDim, scaledFontSize);
        float invertLabelWidth = labelPaint.MeasureText("Invert");
        float centreLabelWidth = labelPaint.MeasureText("Centre");
        float symmetricalLabelWidth = labelPaint.MeasureText("Symmetrical");
        float labelGap = 4f;
        float checkboxGap = 12f;

        // Symmetrical checkbox (leftmost) - checkbox then label
        _curveSymmetricalCheckboxBounds = new SKRect(leftMargin, checkboxY, leftMargin + checkboxSize, checkboxY + checkboxSize);
        FUIWidgets.DrawCheckbox(canvas, _curveSymmetricalCheckboxBounds, _curveSymmetrical, _ctx.MousePosition);
        FUIRenderer.DrawText(canvas, "Symmetrical", new SKPoint(leftMargin + checkboxSize + labelGap, textY), FUIColors.TextDim, fontSize);

        // Invert checkbox (rightmost) - label then checkbox
        float invertCheckX = rightMargin - checkboxSize;
        _invertToggleBounds = new SKRect(invertCheckX, checkboxY, invertCheckX + checkboxSize, checkboxY + checkboxSize);
        FUIWidgets.DrawCheckbox(canvas, _invertToggleBounds, _axisInverted, _ctx.MousePosition);
        FUIRenderer.DrawText(canvas, "Invert", new SKPoint(invertCheckX - invertLabelWidth - labelGap, textY), FUIColors.TextDim, fontSize);

        // Centre checkbox (left of Invert) - label then checkbox
        float centreCheckX = invertCheckX - invertLabelWidth - labelGap - checkboxGap - checkboxSize;
        _deadzoneCenterCheckboxBounds = new SKRect(centreCheckX, checkboxY, centreCheckX + checkboxSize, checkboxY + checkboxSize);
        FUIWidgets.DrawCheckbox(canvas, _deadzoneCenterCheckboxBounds, _deadzoneCenterEnabled, _ctx.MousePosition);
        FUIRenderer.DrawText(canvas, "Centre", new SKPoint(centreCheckX - centreLabelWidth - labelGap, textY), FUIColors.TextDim, fontSize);

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
            _curvePresetBounds[i] = presetBounds;

            CurveType presetType = i switch
            {
                0 => CurveType.Linear,
                1 => CurveType.SCurve,
                2 => CurveType.Exponential,
                _ => CurveType.Custom
            };

            bool isActive = _selectedCurveType == presetType;
            bool isHovered = presetBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

            var bgColor = isActive
                ? FUIColors.Active.WithAlpha(60)
                : (isHovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background2);
            var frameColor = isActive
                ? FUIColors.Active
                : (isHovered ? FUIColors.FrameBright : FUIColors.Frame);
            var textColor = isActive ? FUIColors.TextBright : FUIColors.TextDim;

            using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
            canvas.DrawRect(presetBounds, bgPaint);

            using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = frameColor, StrokeWidth = 1f };
            canvas.DrawRect(presetBounds, framePaint);

            FUIRenderer.DrawTextCentered(canvas, presets[i], presetBounds, textColor, 12f);
        }
        y += buttonHeight + 6f;

        // Curve editor visualization
        float curveHeight = 140f;
        _curveEditorBounds = new SKRect(leftMargin, y, rightMargin, y + curveHeight);
        DrawCurveVisualization(canvas, _curveEditorBounds);
        y += curveHeight + 6f;

        // Live axis movement indicator
        var axisMapping = GetCurrentAxisMapping();
        if (axisMapping is not null)
        {
            float indicatorHeight = DrawAxisMovementIndicator(canvas, leftMargin, rightMargin, y, axisMapping);
            y += indicatorHeight + 6f;
        }
        y += 4f;

        // Deadzone section
        if (y + 100 < bottom)
        {
            // Header row: "DEADZONE" label + preset buttons + selected handle indicator
            FUIRenderer.DrawText(canvas, "DEADZONE", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);

            // Preset buttons - always visible, apply to selected handle
            string[] presetLabels = { "0%", "2%", "5%", "10%" };
            float presetBtnWidth = 32f;
            float presetStartX = rightMargin - (presetBtnWidth * 4 + 9);

            for (int col = 0; col < 4; col++)
            {
                var btnBounds = new SKRect(
                    presetStartX + col * (presetBtnWidth + 3), y - 2,
                    presetStartX + col * (presetBtnWidth + 3) + presetBtnWidth, y + 14);
                _deadzonePresetBounds[col] = btnBounds;

                // Dim buttons if no handle selected
                bool enabled = _selectedDeadzoneHandle >= 0;
                bool isHovered = enabled && btnBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

                var bgColor = enabled
                    ? (isHovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background2)
                    : FUIColors.Background2;
                var frameColor = enabled
                    ? (isHovered ? FUIColors.FrameBright : FUIColors.Frame)
                    : FUIColors.Frame.WithAlpha(100);

                using var btnBg = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
                canvas.DrawRect(btnBounds, btnBg);
                using var btnFrame = new SKPaint { Style = SKPaintStyle.Stroke, Color = frameColor, StrokeWidth = 1f };
                canvas.DrawRect(btnBounds, btnFrame);
                FUIRenderer.DrawTextCentered(canvas, presetLabels[col], btnBounds, enabled ? FUIColors.TextDim : FUIColors.TextDim.WithAlpha(100), 12f);
            }

            // Show which handle is selected (if any)
            if (_selectedDeadzoneHandle >= 0)
            {
                string[] handleNames = { "Start", "Ctr-", "Ctr+", "End" };
                string selectedName = handleNames[_selectedDeadzoneHandle];
                FUIRenderer.DrawText(canvas, $"[{selectedName}]", new SKPoint(presetStartX - 45, y), FUIColors.Active, 12f);
            }
            y += 20f;

            // Dual deadzone slider (always shows min/max, optionally shows center handles)
            float sliderHeight = 24f;
            _deadzoneSliderBounds = new SKRect(leftMargin, y, rightMargin, y + sliderHeight);
            DrawDualDeadzoneSlider(canvas, _deadzoneSliderBounds);
            y += sliderHeight + 6f;

            // Value labels - fixed positions at track edges (prevents collision)
            if (_deadzoneCenterEnabled)
            {
                // Two-track layout - fixed positions at each track edge
                float gap = 24f;
                float centerX = _deadzoneSliderBounds.MidX;
                float leftTrackRight = centerX - gap / 2;
                float rightTrackLeft = centerX + gap / 2;

                // Min at left edge, CtrMin at right edge of left track
                // CtrMax at left edge of right track, Max at right edge
                FUIRenderer.DrawText(canvas, $"{_deadzoneMin:F2}", new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, $"{_deadzoneCenterMin:F2}", new SKPoint(leftTrackRight - 24, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, $"{_deadzoneCenterMax:F2}", new SKPoint(rightTrackLeft, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, $"{_deadzoneMax:F2}", new SKPoint(rightMargin - 20, y), FUIColors.TextDim, 12f);
            }
            else
            {
                // Single track - just show start and end at edges
                FUIRenderer.DrawText(canvas, $"{_deadzoneMin:F2}", new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, $"{_deadzoneMax:F2}", new SKPoint(rightMargin - 20, y), FUIColors.TextDim, 12f);
            }
        }
    }

    private void DrawDualDeadzoneSlider(SKCanvas canvas, SKRect bounds)
    {
        // Convert -1..1 values to 0..1 for display
        float minPos = (_deadzoneMin + 1f) / 2f;
        float centerMinPos = (_deadzoneCenterMin + 1f) / 2f;
        float centerMaxPos = (_deadzoneCenterMax + 1f) / 2f;
        float maxPos = (_deadzoneMax + 1f) / 2f;

        float handleRadius = 8f;
        float trackHeight = 8f;
        float trackY = bounds.MidY - trackHeight / 2;

        using var trackBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        using var trackFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
        using var activePaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(150) };

        if (_deadzoneCenterEnabled)
        {
            // Two physically separate tracks like JoystickGremlinEx
            // Gap must be > 2 * handleRadius so handles never overlap when both at center
            float gap = 24f;
            float centerX = bounds.MidX;

            // Left track: from bounds.Left to centerX - gap/2
            var leftTrack = new SKRect(bounds.Left, trackY, centerX - gap / 2, trackY + trackHeight);
            canvas.DrawRoundRect(leftTrack, 4, 4, trackBgPaint);
            canvas.DrawRoundRect(leftTrack, 4, 4, trackFramePaint);

            // Right track: from centerX + gap/2 to bounds.Right
            var rightTrack = new SKRect(centerX + gap / 2, trackY, bounds.Right, trackY + trackHeight);
            canvas.DrawRoundRect(rightTrack, 4, 4, trackBgPaint);
            canvas.DrawRoundRect(rightTrack, 4, 4, trackFramePaint);

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
            canvas.DrawRoundRect(track, 4, 4, trackBgPaint);
            canvas.DrawRoundRect(track, 4, 4, trackFramePaint);

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
        bool isDragging = _draggingDeadzoneHandle == handleIndex;
        bool isSelected = _selectedDeadzoneHandle == handleIndex;
        float drawRadius = isDragging ? radius + 2f : radius;

        // Selected handles get a highlighted fill
        SKColor fillColor = isDragging ? color : (isSelected ? color.WithAlpha(200) : FUIColors.TextPrimary);

        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = fillColor,
            IsAntialias = true
        };
        canvas.DrawCircle(x, centerY, drawRadius, fillPaint);

        using var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = isSelected ? 2.5f : 1.5f,
            IsAntialias = true
        };
        canvas.DrawCircle(x, centerY, drawRadius, strokePaint);
    }

    private void DrawButtonSettings(SKCanvas canvas, float leftMargin, float rightMargin, float y, float bottom)
    {
        float width = rightMargin - leftMargin;

        // OUTPUT TYPE section - vJoy Button vs Keyboard
        FUIRenderer.DrawText(canvas, "OUTPUT TYPE", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        y += 20;

        // Output type tabs
        string[] outputTypes = { "Button", "Keyboard" };
        float typeButtonWidth = (width - 5) / 2;
        float typeButtonHeight = 28f;

        for (int i = 0; i < 2; i++)
        {
            var typeBounds = new SKRect(leftMargin + i * (typeButtonWidth + 5), y,
                leftMargin + i * (typeButtonWidth + 5) + typeButtonWidth, y + typeButtonHeight);

            if (i == 0) _outputTypeBtnBounds = typeBounds;
            else _outputTypeKeyBounds = typeBounds;

            bool selected = (i == 0 && !_outputTypeIsKeyboard) || (i == 1 && _outputTypeIsKeyboard);
            bool hovered = _hoveredOutputType == i;

            var bgColor = selected
                ? FUIColors.Active.WithAlpha(60)
                : (hovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);
            var textColor = selected ? FUIColors.Active : (hovered ? FUIColors.TextPrimary : FUIColors.TextDim);

            using var typeBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
            canvas.DrawRoundRect(typeBounds, 3, 3, typeBgPaint);

            using var typeFramePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = selected ? FUIColors.Active : FUIColors.Frame,
                StrokeWidth = selected ? 2f : 1f
            };
            canvas.DrawRoundRect(typeBounds, 3, 3, typeFramePaint);

            FUIRenderer.DrawTextCentered(canvas, outputTypes[i], typeBounds, textColor, 14f);
        }
        y += typeButtonHeight + 16;

        // KEY COMBO section (only when Keyboard is selected)
        if (_outputTypeIsKeyboard)
        {
            FUIRenderer.DrawText(canvas, "KEY COMBO", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
            y += 20;

            float keyFieldHeight = 32f;
            _keyCaptureBounds = new SKRect(leftMargin, y, rightMargin, y + keyFieldHeight);

            // Check for key capture timeout
            if (_isCapturingKey)
            {
                var elapsed = (DateTime.Now - _keyCaptureStartTime).TotalMilliseconds;
                if (elapsed >= KeyCaptureTimeoutMs)
                {
                    _isCapturingKey = false; // Timeout - cancel capture
                }
            }

            // Draw key capture field background
            var keyBgColor = _isCapturingKey
                ? FUIColors.Active.WithAlpha(40)
                : (_keyCaptureBoundsHovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);

            using var keyBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = keyBgColor };
            canvas.DrawRoundRect(_keyCaptureBounds, 3, 3, keyBgPaint);

            // Draw timeout progress bar when capturing
            if (_isCapturingKey)
            {
                var elapsed = (DateTime.Now - _keyCaptureStartTime).TotalMilliseconds;
                float progress = Math.Min(1f, (float)(elapsed / KeyCaptureTimeoutMs));
                float remaining = 1f - progress;

                // Progress bar fills the field and shrinks from right to left
                float progressWidth = (_keyCaptureBounds.Width - 6) * remaining;
                if (progressWidth > 0)
                {
                    var progressRect = new SKRect(
                        _keyCaptureBounds.Left + 3,
                        _keyCaptureBounds.Top + 3,
                        _keyCaptureBounds.Left + 3 + progressWidth,
                        _keyCaptureBounds.Bottom - 3);
                    using var progressPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Fill,
                        Color = FUIColors.Active.WithAlpha(80)
                    };
                    canvas.DrawRoundRect(progressRect, 2, 2, progressPaint);
                }
            }

            var keyFrameColor = _isCapturingKey
                ? FUIColors.Active
                : (_keyCaptureBoundsHovered ? FUIColors.Primary : FUIColors.Frame);

            using var keyFramePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = keyFrameColor,
                StrokeWidth = _isCapturingKey ? 2f : 1f
            };
            canvas.DrawRoundRect(_keyCaptureBounds, 3, 3, keyFramePaint);

            // Display key combo or prompt
            if (_isCapturingKey)
            {
                byte alpha = (byte)(180 + MathF.Sin(_ctx.PulsePhase * 3) * 60);
                FUIRenderer.DrawTextCentered(canvas, "Press key combo...", _keyCaptureBounds, FUIColors.Warning.WithAlpha(alpha), 14f);
            }
            else if (!string.IsNullOrEmpty(_selectedKeyName))
            {
                // Draw keycaps centered in the field
                FUIWidgets.DrawKeycapsInBounds(canvas, _keyCaptureBounds, _selectedKeyName, _selectedModifiers);
            }
            else
            {
                FUIRenderer.DrawTextCentered(canvas, "Click to capture key", _keyCaptureBounds, FUIColors.TextDim, 14f);
            }
            y += keyFieldHeight + 16;
        }

        // Button Mode section
        // Modifier keys must stay in Normal mode ÔÇö the OS handles the modifier behaviour.
        bool isModifierKey = _outputTypeIsKeyboard && IsModifierKeyName(_selectedKeyName);

        FUIRenderer.DrawText(canvas, "BUTTON MODE", new SKPoint(leftMargin, y),
            isModifierKey ? FUIColors.TextDim.WithAlpha(60) : FUIColors.TextDim, 13f);
        y += 20;

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
                // Disabled appearance ÔÇö clear bounds so hover and click don't fire
                using var disabledBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2.WithAlpha(100) };
                canvas.DrawRoundRect(modeBounds, 3, 3, disabledBgPaint);

                using var disabledFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame.WithAlpha(100), StrokeWidth = 1f };
                canvas.DrawRoundRect(modeBounds, 3, 3, disabledFramePaint);

                FUIRenderer.DrawTextCentered(canvas, modes[i], modeBounds, FUIColors.TextDim.WithAlpha(120), 12f);
                _buttonModeBounds[i] = SKRect.Empty;
            }
            else
            {
                bool selected = i == (int)_selectedButtonMode;
                bool hovered = i == _hoveredButtonMode;

                SKColor bgColor = selected ? FUIColors.Active.WithAlpha(60) :
                    (hovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);

                using var modeBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
                canvas.DrawRoundRect(modeBounds, 3, 3, modeBgPaint);

                using var modeFramePaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = selected ? FUIColors.Active : FUIColors.Frame,
                    StrokeWidth = selected ? 2f : 1f
                };
                canvas.DrawRoundRect(modeBounds, 3, 3, modeFramePaint);

                FUIRenderer.DrawTextCentered(canvas, modes[i], modeBounds,
                    selected ? FUIColors.Active : FUIColors.TextPrimary, 12f);

                _buttonModeBounds[i] = modeBounds;
            }
        }
        y += buttonHeight + 12;

        // Duration slider for Pulse mode
        if (_selectedButtonMode == ButtonMode.Pulse && y + 50 < bottom)
        {
            FUIRenderer.DrawText(canvas, "PULSE DURATION", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
            y += 18;

            float sliderHeight = 24f;
            _pulseDurationSliderBounds = new SKRect(leftMargin, y, rightMargin - 50, y + sliderHeight);

            // Normalize value: 100-1000ms mapped to 0-1
            float normalizedPulse = (_pulseDurationMs - 100f) / 900f;
            FUIWidgets.DrawDurationSlider(canvas, _pulseDurationSliderBounds, normalizedPulse, _draggingPulseDuration);

            // Value label
            FUIRenderer.DrawText(canvas, $"{_pulseDurationMs}ms",
                new SKPoint(rightMargin - 45, y + sliderHeight / 2 + 4), FUIColors.TextPrimary, 13f);

            y += sliderHeight + 12;
        }

        // Duration slider for Hold mode
        if (_selectedButtonMode == ButtonMode.HoldToActivate && y + 50 < bottom)
        {
            FUIRenderer.DrawText(canvas, "HOLD DURATION", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
            y += 18;

            float sliderHeight = 24f;
            _holdDurationSliderBounds = new SKRect(leftMargin, y, rightMargin - 50, y + sliderHeight);

            // Normalize value: 200-2000ms mapped to 0-1
            float normalizedHold = (_holdDurationMs - 200f) / 1800f;
            FUIWidgets.DrawDurationSlider(canvas, _holdDurationSliderBounds, normalizedHold, _draggingHoldDuration);

            // Value label
            FUIRenderer.DrawText(canvas, $"{_holdDurationMs}ms",
                new SKPoint(rightMargin - 45, y + sliderHeight / 2 + 4), FUIColors.TextPrimary, 13f);

            y += sliderHeight + 12;
        }

        // Clear binding button
        if (y + 40 < bottom)
        {
            var clearBounds = new SKRect(leftMargin, y, rightMargin, y + 32);
            _clearAllButtonBounds = clearBounds;

            var state = _clearAllButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
            FUIRenderer.DrawButton(canvas, clearBounds, "CLEAR MAPPING", state, FUIColors.Danger);
        }
    }

    /// <summary>
    /// Format key combo for display as simple text (used in mapping names)
    /// </summary>
    private string FormatKeyComboForDisplay(string keyName, List<string>? modifiers)
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
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background0 };
        canvas.DrawRect(bounds, bgPaint);

        // Grid lines (10% increments) - visible but subtle
        using var gridPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(60, 70, 80), // Visible gray grid lines
            StrokeWidth = 1f
        };

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
        using var centerPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(80, 95, 110), // More visible center lines
            StrokeWidth = 1f
        };
        canvas.DrawLine(bounds.MidX, bounds.Top, bounds.MidX, bounds.Bottom, centerPaint);
        canvas.DrawLine(bounds.Left, bounds.MidY, bounds.Right, bounds.MidY, centerPaint);

        // Reference linear line (dashed diagonal)
        using var refPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame.WithAlpha(50),
            StrokeWidth = 1f,
            PathEffect = SKPathEffect.CreateDash(new[] { 4f, 4f }, 0)
        };
        canvas.DrawLine(bounds.Left, bounds.Bottom, bounds.Right, bounds.Top, refPaint);

        // Draw the curve
        DrawCurvePath(canvas, bounds);

        // Draw control points (only for custom curve)
        if (_selectedCurveType == CurveType.Custom)
        {
            DrawCurveControlPoints(canvas, bounds);
        }

        // Frame
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        // Tick marks and labels on edges
        using var tickPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame.WithAlpha(150),
            StrokeWidth = 1f
        };

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

        // Axis labels
        FUIRenderer.DrawText(canvas, "IN", new SKPoint(bounds.MidX - 6, bounds.Bottom + 22), FUIColors.TextDim, 12f);

        // Rotated "OUT" label
        canvas.Save();
        canvas.Translate(bounds.Left - 24, bounds.MidY + 8);
        canvas.RotateDegrees(-90);
        FUIRenderer.DrawText(canvas, "OUT", new SKPoint(0, 0), FUIColors.TextDim, 12f);
        canvas.Restore();
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
                ? $"LIVE INPUT: {rawPercent:+0;-0;0}%  ÔåÆ  OUTPUT: {outPercent:+0;-0;0}%"
                : $"LIVE INPUT: {rawPercent:0}%  ÔåÆ  OUTPUT: {outPercent:0}%")
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
            using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background0 };
            canvas.DrawRect(barBounds, bgPaint);

            // Convert output value to bar position (0..1)
            float normalizedValue = (processedOutput + 1f) / 2f;
            float barX = barBounds.Left + normalizedValue * barBounds.Width;

            if (isCentered)
            {
                // Center line for centered axes
                using var centerPaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = FUIColors.Frame,
                    StrokeWidth = 1f
                };
                canvas.DrawLine(barBounds.MidX, barBounds.Top, barBounds.MidX, barBounds.Bottom, centerPaint);

                // Fill from center to current position
                var fillBounds = processedOutput >= 0
                    ? new SKRect(barBounds.MidX, barBounds.Top, barX, barBounds.Bottom)
                    : new SKRect(barX, barBounds.Top, barBounds.MidX, barBounds.Bottom);

                using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(180) };
                canvas.DrawRect(fillBounds, fillPaint);
            }
            else
            {
                // Fill from left edge to current position (for sliders/throttles)
                var fillBounds = new SKRect(barBounds.Left, barBounds.Top, barX, barBounds.Bottom);
                using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(180) };
                canvas.DrawRect(fillBounds, fillPaint);
            }

            // Position indicator (vertical line)
            using var indicatorPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.Active,
                StrokeWidth = 2f
            };
            canvas.DrawLine(barX, barBounds.Top, barX, barBounds.Bottom, indicatorPaint);

            // Frame
            using var framePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.Frame,
                StrokeWidth = 1f
            };
            canvas.DrawRect(barBounds, framePaint);

            y += barHeight + 2f;

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
        float output = _selectedCurveType switch
        {
            CurveType.Linear => input,
            CurveType.SCurve => ApplySCurve(input),
            CurveType.Exponential => ApplyExponential(input),
            CurveType.Custom => InterpolateControlPoints(input),
            _ => input
        };

        output = Math.Clamp(output, 0f, 1f);

        // Apply inversion
        if (_axisInverted)
            output = 1f - output;

        return output;
    }

    private float ApplySCurve(float x)
    {
        // S-curve using smoothstep-like function
        return x * x * (3f - 2f * x);
    }

    private float ApplyExponential(float x)
    {
        // Exponential curve (steeper at the end)
        return x * x;
    }

    private float InterpolateControlPoints(float x)
    {
        if (_curveControlPoints.Count < 2) return x;

        // Find segment containing x
        for (int i = 0; i < _curveControlPoints.Count - 1; i++)
        {
            var p1 = _curveControlPoints[i];
            var p2 = _curveControlPoints[i + 1];

            if (x >= p1.X && x <= p2.X)
            {
                if (Math.Abs(p2.X - p1.X) < 0.001f) return p1.Y;
                float t = (x - p1.X) / (p2.X - p1.X);

                // Use Catmull-Rom spline interpolation for smooth curves
                // Need 4 points: p0, p1, p2, p3
                var p0 = i > 0 ? _curveControlPoints[i - 1] : new SKPoint(p1.X - (p2.X - p1.X), p1.Y - (p2.Y - p1.Y));
                var p3 = i < _curveControlPoints.Count - 2 ? _curveControlPoints[i + 2] : new SKPoint(p2.X + (p2.X - p1.X), p2.Y + (p2.Y - p1.Y));

                return CatmullRomInterpolate(p0.Y, p1.Y, p2.Y, p3.Y, t);
            }
        }

        // Extrapolate
        return x < _curveControlPoints[0].X ? _curveControlPoints[0].Y : _curveControlPoints[^1].Y;
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

        for (int i = 0; i < _curveControlPoints.Count; i++)
        {
            var pt = _curveControlPoints[i];
            float x = bounds.Left + pt.X * bounds.Width;

            // Apply inversion to display Y position to match the curve
            float displayY = _axisInverted ? (1f - pt.Y) : pt.Y;
            float y = bounds.Bottom - displayY * bounds.Height;

            bool isHovered = i == _hoveredCurvePoint;
            bool isDragging = i == _draggingCurvePoint;
            bool isEndpoint = i == 0 || i == _curveControlPoints.Count - 1;
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
            using var fillPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = isEndpoint || isCenterPoint ? FUIColors.Background1 : color.WithAlpha(60),
                IsAntialias = true
            };
            canvas.DrawCircle(x, y, radius, fillPaint);

            // Stroke
            using var strokePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = isCenterPoint ? FUIColors.Frame : color,
                StrokeWidth = isEndpoint ? 2f : (isCenterPoint ? 1f : 1.5f),
                IsAntialias = true
            };
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
        if (_axisInverted)
            y = 1f - y;

        return new SKPoint(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
    }

    private void DrawMappingEditorPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(160),
            IsAntialias = true
        };
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
        _inputFieldBounds = new SKRect(leftMargin, y, rightMargin, y + inputFieldHeight);
        DrawInputField(canvas, _inputFieldBounds);
        y += inputFieldHeight + 10;

        // Manual entry toggle button
        _manualEntryButtonBounds = new SKRect(leftMargin, y, leftMargin + 120, y + 24);
        FUIWidgets.DrawToggleButton(canvas, _manualEntryButtonBounds, "Manual Entry", _manualEntryMode, _manualEntryButtonHovered);
        y += 34;

        // Manual entry dropdowns (if enabled)
        if (_manualEntryMode)
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
            if (_outputTypeIsKeyboard)
            {
                FUIRenderer.DrawText(canvas, "KEY", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
                y += 20;
                float keyFieldHeight = 32f;
                _keyCaptureBounds = new SKRect(leftMargin, y, rightMargin, y + keyFieldHeight);
                DrawKeyCapture(canvas, _keyCaptureBounds);
                y += keyFieldHeight + 10;
            }

            // Button mode selector (disabled for modifier keys)
            bool editIsModifier = _outputTypeIsKeyboard && IsModifierKeyName(_selectedKeyName);
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
            int axisIndex = _editingRowIndex;
            return axisIndex < axisNames.Length ? axisNames[axisIndex] : $"Axis {axisIndex}";
        }
        else
        {
            int buttonIndex = _editingRowIndex - 8;
            return $"Button {buttonIndex + 1}";
        }
    }

    private void DrawInputField(SKCanvas canvas, SKRect bounds)
    {
        // Background
        var bgColor = _isListeningForInput
            ? FUIColors.Warning.WithAlpha(40)
            : (_inputFieldHovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        // Frame
        var frameColor = _isListeningForInput
            ? FUIColors.Warning
            : (_inputFieldHovered ? FUIColors.Primary : FUIColors.Frame);
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = frameColor,
            StrokeWidth = _isListeningForInput ? 2f : 1f
        };
        canvas.DrawRect(bounds, framePaint);

        // Text content
        float textY = bounds.MidY + 5;
        if (_isListeningForInput)
        {
            byte alpha = (byte)(180 + MathF.Sin(_ctx.PulsePhase * 3) * 60);
            FUIRenderer.DrawText(canvas, "Press a button or move an axis...",
                new SKPoint(bounds.Left + 10, textY), FUIColors.Warning.WithAlpha(alpha), 15f);
        }
        else if (_pendingInput is not null)
        {
            FUIRenderer.DrawText(canvas, _pendingInput.ToString(),
                new SKPoint(bounds.Left + 10, textY), FUIColors.TextBright, 15f);
        }
        else
        {
            FUIRenderer.DrawText(canvas, "Double-click to detect input",
                new SKPoint(bounds.Left + 10, textY), FUIColors.TextDisabled, 15f);
        }

        // Clear button if there's input
        if (_pendingInput is not null && !_isListeningForInput)
        {
            var clearBounds = new SKRect(bounds.Right - 28, bounds.Top + 6, bounds.Right - 6, bounds.Bottom - 6);
            FUIWidgets.DrawSmallIconButton(canvas, clearBounds, "├ù", false, true);
        }
    }

    private float DrawManualEntrySection(SKCanvas canvas, SKRect bounds, float y, float leftMargin, float rightMargin)
    {
        // Device dropdown
        FUIRenderer.DrawText(canvas, "Device:", new SKPoint(leftMargin, y + 12), FUIColors.TextDim, 13f);
        float dropdownX = leftMargin + 55;
        _deviceDropdownBounds = new SKRect(dropdownX, y, rightMargin, y + 28);
        string deviceText = _ctx.Devices.Count > 0 && _selectedSourceDevice < _ctx.Devices.Count
            ? _ctx.Devices[_selectedSourceDevice].Name
            : "No devices";
        FUIWidgets.DrawDropdown(canvas, _deviceDropdownBounds, deviceText, _deviceDropdownOpen);
        y += 36;

        // Control dropdown
        string controlLabel = _isEditingAxis ? "Axis:" : "Button:";
        FUIRenderer.DrawText(canvas, controlLabel, new SKPoint(leftMargin, y + 12), FUIColors.TextDim, 13f);
        _controlDropdownBounds = new SKRect(dropdownX, y, rightMargin, y + 28);
        string controlText = GetControlDropdownText();
        FUIWidgets.DrawDropdown(canvas, _controlDropdownBounds, controlText, _controlDropdownOpen);
        y += 36;

        // Draw dropdown lists if open
        if (_deviceDropdownOpen)
        {
            DrawDeviceDropdownList(canvas, _deviceDropdownBounds);
        }
        else if (_controlDropdownOpen)
        {
            DrawControlDropdownList(canvas, _controlDropdownBounds);
        }

        return y;
    }

    private string GetControlDropdownText()
    {
        if (_ctx.Devices.Count == 0 || _selectedSourceDevice >= _ctx.Devices.Count)
            return "ÔÇö";

        var device = _ctx.Devices[_selectedSourceDevice];
        if (_isEditingAxis)
        {
            int axisCount = 8; // Typical axis count
            if (_selectedSourceControl < axisCount)
                return $"Axis {_selectedSourceControl}";
        }
        else
        {
            if (_selectedSourceControl < 128)
                return $"Button {_selectedSourceControl + 1}";
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
        using var shadowPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.Black.WithAlpha(120)
        };
        var shadowBounds = new SKRect(listBounds.Left - 1, listBounds.Top - 1, listBounds.Right + 5, listBounds.Bottom + 5);
        canvas.DrawRect(shadowBounds, shadowPaint);

        // Solid opaque background
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1 };
        canvas.DrawRect(listBounds, bgPaint);

        // Draw items
        float y = listBounds.Top;
        for (int i = 0; i < _ctx.Devices.Count && y < listBounds.Bottom; i++)
        {
            var itemBounds = new SKRect(listBounds.Left, y, listBounds.Right, y + itemHeight);
            bool hovered = i == _hoveredDeviceIndex;

            if (hovered)
            {
                using var hoverPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Primary.WithAlpha(60) };
                canvas.DrawRect(itemBounds, hoverPaint);
            }

            FUIRenderer.DrawText(canvas, _ctx.Devices[i].Name, new SKPoint(itemBounds.Left + 8, itemBounds.MidY + 4),
                hovered ? FUIColors.TextBright : FUIColors.TextPrimary, 14f);
            y += itemHeight;
        }

        // Frame on top
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Primary,
            StrokeWidth = 1f
        };
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
        using var shadowPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.Black.WithAlpha(120)
        };
        var shadowBounds = new SKRect(listBounds.Left - 1, listBounds.Top - 1, listBounds.Right + 5, listBounds.Bottom + 5);
        canvas.DrawRect(shadowBounds, shadowPaint);

        // Solid opaque background
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1 };
        canvas.DrawRect(listBounds, bgPaint);

        // Draw items
        float y = listBounds.Top;
        for (int i = 0; i < controlCount && y < listBounds.Bottom; i++)
        {
            var itemBounds = new SKRect(listBounds.Left, y, listBounds.Right, y + itemHeight);
            bool hovered = i == _hoveredControlIndex;

            if (hovered)
            {
                using var hoverPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Primary.WithAlpha(60) };
                canvas.DrawRect(itemBounds, hoverPaint);
            }

            string name = _isEditingAxis ? $"Axis {i}" : $"Button {i + 1}";
            FUIRenderer.DrawText(canvas, name, new SKPoint(itemBounds.Left + 8, itemBounds.MidY + 4),
                hovered ? FUIColors.TextBright : FUIColors.TextPrimary, 14f);
            y += itemHeight;
        }

        // Frame on top
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Primary,
            StrokeWidth = 1f
        };
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
                using var disabledBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2.WithAlpha(100) };
                canvas.DrawRect(modeBounds, disabledBgPaint);

                using var disabledFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame.WithAlpha(100), StrokeWidth = 1f };
                canvas.DrawRect(modeBounds, disabledFramePaint);

                FUIRenderer.DrawTextCentered(canvas, labels[i], modeBounds, FUIColors.TextDim.WithAlpha(120), 13f);
                _buttonModeBounds[i] = SKRect.Empty;
            }
            else
            {
                _buttonModeBounds[i] = modeBounds;

                bool selected = _selectedButtonMode == modes[i];
                bool hovered = _hoveredButtonMode == i;

                var bgColor = selected
                    ? FUIColors.Active.WithAlpha(60)
                    : (hovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);
                var textColor = selected ? FUIColors.Active : (hovered ? FUIColors.TextPrimary : FUIColors.TextDim);

                using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
                canvas.DrawRect(modeBounds, bgPaint);

                using var framePaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = selected ? FUIColors.Active : FUIColors.Frame,
                    StrokeWidth = selected ? 2f : 1f
                };
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
            if (i == 0) _outputTypeBtnBounds = typeBounds;
            else _outputTypeKeyBounds = typeBounds;

            bool selected = (i == 0 && !_outputTypeIsKeyboard) || (i == 1 && _outputTypeIsKeyboard);
            bool hovered = _hoveredOutputType == i;

            var bgColor = selected
                ? FUIColors.Active.WithAlpha(60)
                : (hovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);
            var textColor = selected ? FUIColors.Active : (hovered ? FUIColors.TextPrimary : FUIColors.TextDim);

            using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
            canvas.DrawRect(typeBounds, bgPaint);

            using var framePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = selected ? FUIColors.Active : FUIColors.Frame,
                StrokeWidth = selected ? 2f : 1f
            };
            canvas.DrawRect(typeBounds, framePaint);

            FUIRenderer.DrawTextCentered(canvas, labels[i], typeBounds, textColor, 14f);
        }
    }

    private void DrawKeyCapture(SKCanvas canvas, SKRect bounds)
    {
        // Background
        var bgColor = _isCapturingKey
            ? FUIColors.Warning.WithAlpha(40)
            : (_keyCaptureBoundsHovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        // Frame
        var frameColor = _isCapturingKey
            ? FUIColors.Warning
            : (_keyCaptureBoundsHovered ? FUIColors.Primary : FUIColors.Frame);
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = frameColor,
            StrokeWidth = _isCapturingKey ? 2f : 1f
        };
        canvas.DrawRect(bounds, framePaint);

        // Text content
        float textY = bounds.MidY + 5;
        if (_isCapturingKey)
        {
            byte alpha = (byte)(180 + MathF.Sin(_ctx.PulsePhase * 3) * 60);
            FUIRenderer.DrawText(canvas, "Press a key...",
                new SKPoint(bounds.Left + 10, textY), FUIColors.Warning.WithAlpha(alpha), 15f);
        }
        else if (!string.IsNullOrEmpty(_selectedKeyName))
        {
            FUIRenderer.DrawText(canvas, _selectedKeyName,
                new SKPoint(bounds.Left + 10, textY), FUIColors.TextBright, 15f);
        }
        else
        {
            FUIRenderer.DrawText(canvas, "Click to capture key",
                new SKPoint(bounds.Left + 10, textY), FUIColors.TextDisabled, 15f);
        }

        // Clear button if there's a key
        if (!string.IsNullOrEmpty(_selectedKeyName) && !_isCapturingKey)
        {
            _keyClearButtonBounds = new SKRect(bounds.Right - 28, bounds.Top + 6, bounds.Right - 6, bounds.Bottom - 6);
            FUIWidgets.DrawSmallIconButton(canvas, _keyClearButtonBounds, "├ù", _keyClearButtonHovered, true);
        }
        else
        {
            _keyClearButtonBounds = SKRect.Empty;
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

    private string GetAxisBindingText(MappingProfile? profile, uint vjoyId, int axisIndex)
    {
        if (profile is null) return "ÔÇö";

        var mapping = profile.AxisMappings.FirstOrDefault(m =>
            m.Output.Type == OutputType.VJoyAxis &&
            m.Output.VJoyDevice == vjoyId &&
            m.Output.Index == axisIndex);

        if (mapping is null || mapping.Inputs.Count == 0) return "ÔÇö";

        var input = mapping.Inputs[0];
        return $"{input.DeviceName} - Axis {input.Index}";
    }

    private string GetButtonBindingText(MappingProfile? profile, uint vjoyId, int buttonIndex)
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
            bgColor = FUIColors.Active.WithAlpha(60);
        else if (isSelected)
            bgColor = FUIColors.Active.WithAlpha(40);
        else if (isHovered)
            bgColor = FUIColors.Primary.WithAlpha(30);
        else
            bgColor = FUIColors.Background2.WithAlpha(60);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        // Frame
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = isEditing ? FUIColors.Active : (isSelected ? FUIColors.Active.WithAlpha(150) : (isHovered ? FUIColors.FrameBright : FUIColors.Frame.WithAlpha(80))),
            StrokeWidth = isEditing ? 2f : (isSelected ? 1.5f : 1f)
        };
        canvas.DrawRect(bounds, framePaint);

        // Output name (left)
        float textY = bounds.MidY + 5;
        FUIRenderer.DrawText(canvas, outputName, new SKPoint(bounds.Left + 10, textY),
            isEditing ? FUIColors.Active : FUIColors.TextPrimary, 15f);

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

        // [├ù] button (only if bound)
        if (hasBind)
        {
            float removeButtonX = bounds.Right - 32;
            var removeBounds = new SKRect(removeButtonX, buttonY, removeButtonX + buttonSize, buttonY + buttonSize);
            _mappingRemoveButtonBounds.Add(removeBounds);

            bool removeHovered = rowIndex == _hoveredRemoveButton;
            FUIWidgets.DrawSmallIconButton(canvas, removeBounds, "├ù", removeHovered, true);
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
            FUIRenderer.DrawText(canvas, "No profile selected",
                new SKPoint(bounds.Left + 20, y + 20), FUIColors.TextDim, 15f);
            FUIRenderer.DrawText(canvas, "Select or create a profile to add mappings",
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
            // Axes category: row i = axis output index i
            var mapping = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.VJoyDevice == vjoyDevice.Id && m.Output.Index == rowIndex);
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
    /// using the scale/offset set by the most recent DrawSvgInBounds call.
    /// </summary>
    private SKPoint ViewBoxToScreen(float viewBoxX, float viewBoxY)
    {
        var svg = _ctx.GetSvgForDeviceMap?.Invoke(_ctx.MappingsPrimaryDeviceMap) ?? _ctx.JoystickSvg;
        if (svg?.Picture is null)
            return new SKPoint(viewBoxX, viewBoxY);

        float screenX = _ctx.SvgMirrored
            ? _ctx.SvgOffset.X + svg.Picture.CullRect.Width * _ctx.SvgScale - viewBoxX * _ctx.SvgScale
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
        if (_mappingHighlightControl?.Anchor is null) return;

        float elapsed = (float)(DateTime.Now - _mappingHighlightTime).TotalSeconds;
        float opacity = elapsed < 1f ? 1f : Math.Max(0f, 1f - (elapsed - 1f) / 2f);
        if (opacity < 0.01f) return;

        SKPoint anchorScreen = ViewBoxToScreen(_mappingHighlightControl.Anchor.X, _mappingHighlightControl.Anchor.Y);

        float labelX, labelY;
        bool goesRight = true;

        if (_mappingHighlightControl.LabelOffset is not null)
        {
            var labelScreen = ViewBoxToScreen(
                _mappingHighlightControl.Anchor.X + _mappingHighlightControl.LabelOffset.X,
                _mappingHighlightControl.Anchor.Y + _mappingHighlightControl.LabelOffset.Y);
            labelX = labelScreen.X;
            labelY = labelScreen.Y;
            bool offsetGoesRight = _mappingHighlightControl.LabelOffset.X >= 0;
            goesRight = _ctx.SvgMirrored ? !offsetGoesRight : offsetGoesRight;
        }
        else
        {
            labelY = panelBounds.Top + 80;
            labelX = _ctx.SilhouetteBounds.Right + 20;
        }

        var fakeInput = new ActiveInputState
        {
            Binding = _mappingHighlightControl.Label,
            Value = 1f,
            IsAxis = false,
            Control = _mappingHighlightControl,
            LastActivity = _mappingHighlightTime,
            AppearProgress = 1f
        };

        DeviceLeadLineRenderer.DrawInputLeadLine(
            canvas, anchorScreen, new SKPoint(labelX, labelY),
            goesRight, opacity, fakeInput, _ctx.SvgMirrored, _ctx.SvgScale);
    }

}
