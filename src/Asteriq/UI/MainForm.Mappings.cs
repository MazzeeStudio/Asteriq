using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;

namespace Asteriq.UI;

public partial class MainForm
{
    #region Mappings Tab
    private void DrawMappingsTabContent(SKCanvas canvas, SKRect bounds, float sideTabPad, float contentTop, float contentBottom)
    {
        float frameInset = 5f;
        float pad = FUIRenderer.SpaceLG;  // Standard padding for right side
        var contentBounds = new SKRect(sideTabPad, contentTop, bounds.Right - pad, contentBottom);

        // Three-panel layout: Left (bindings list) | Center (device view) | Right (settings)
        float leftPanelWidth = 400f;  // Match Settings panel width
        float rightPanelWidth = 330f;
        float centerPanelWidth = contentBounds.Width - leftPanelWidth - rightPanelWidth - 20;

        var leftBounds = new SKRect(contentBounds.Left, contentBounds.Top,
            contentBounds.Left + leftPanelWidth, contentBounds.Bottom);
        var centerBounds = new SKRect(leftBounds.Right + 10, contentBounds.Top,
            leftBounds.Right + 10 + centerPanelWidth, contentBounds.Bottom);
        var rightBounds = new SKRect(centerBounds.Right + 10, contentBounds.Top,
            contentBounds.Right, contentBounds.Bottom);

        // Refresh vJoy devices list
        if (_vjoyDevices.Count == 0)
        {
            _vjoyDevices = _vjoyService.EnumerateDevices();
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
        float pad = FUIRenderer.PanelPadding;
        float itemGap = FUIRenderer.ItemSpacing;

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
        FUIRenderer.DrawText(canvas, categoryCode, new SKPoint(leftMargin, y + 12), FUIColors.Active, 12f);
        FUIRenderer.DrawText(canvas, categoryName, new SKPoint(leftMargin + 30, y + 12), FUIColors.TextBright, 14f, true);
        y += 30;

        // vJoy device selector: [<] vJoy Device 1 [>]
        float arrowButtonSize = 28f;
        _vjoyPrevButtonBounds = new SKRect(leftMargin, y, leftMargin + arrowButtonSize, y + arrowButtonSize);
        DrawArrowButton(canvas, _vjoyPrevButtonBounds, "<", _vjoyPrevHovered, _selectedVJoyDeviceIndex > 0);

        string deviceName = _vjoyDevices.Count > 0 && _selectedVJoyDeviceIndex < _vjoyDevices.Count
            ? $"vJoy Device {_vjoyDevices[_selectedVJoyDeviceIndex].Id}"
            : "No vJoy Devices";
        // Center the device name between the two arrow buttons
        var labelBounds = new SKRect(leftMargin + arrowButtonSize, y, rightMargin - arrowButtonSize, y + arrowButtonSize);
        FUIRenderer.DrawTextCentered(canvas, deviceName, labelBounds, FUIColors.TextBright, 12f);

        _vjoyNextButtonBounds = new SKRect(rightMargin - arrowButtonSize, y, rightMargin, y + arrowButtonSize);
        DrawArrowButton(canvas, _vjoyNextButtonBounds, ">", _vjoyNextHovered, _selectedVJoyDeviceIndex < _vjoyDevices.Count - 1);
        y += arrowButtonSize + 15;

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
        DrawVerticalSideTab(canvas, buttonsBounds, "BUTTONS_01", _mappingCategory == 0, _hoveredMappingCategory == 0);

        // M2 Axes tab (above M1)
        var axesBounds = new SKRect(x, startY, x + width, startY + tabHeight);
        _mappingCategoryAxesBounds = axesBounds;
        DrawVerticalSideTab(canvas, axesBounds, "AXES_02", _mappingCategory == 1, _hoveredMappingCategory == 1);
    }

    private void DrawBindingsList(SKCanvas canvas, SKRect bounds)
    {
        _mappingRowBounds.Clear();
        _mappingAddButtonBounds.Clear();
        _mappingRemoveButtonBounds.Clear();
        _bindingsListBounds = bounds;

        var profile = _profileService.ActiveProfile;

        bool hasVJoy = _vjoyDevices.Count > 0 && _selectedVJoyDeviceIndex < _vjoyDevices.Count;
        VJoyDeviceInfo? vjoyDevice = hasVJoy ? _vjoyDevices[_selectedVJoyDeviceIndex] : null;

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

                    DrawChunkyBindingRow(canvas, rowBounds, $"Button {i + 1}", binding, isSelected, isHovered, rowIndex, keyParts);
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
        if (output.Modifiers != null && output.Modifiers.Count > 0)
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
        if (profile == null) return null;

        // Find mapping for this button slot that has keyboard output
        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyId &&
            m.Output.Index == buttonIndex &&
            !string.IsNullOrEmpty(m.Output.KeyName));

        if (mapping == null) return null;

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
        bool isSelected, bool isHovered, int rowIndex, List<string>? keyParts = null)
    {
        bool hasBinding = !string.IsNullOrEmpty(binding) && binding != "—";
        bool hasKeyParts = keyParts != null && keyParts.Count > 0;

        // Background
        SKColor bgColor;
        if (isSelected)
            bgColor = FUIColors.Active.WithAlpha(50);
        else if (isHovered)
            bgColor = FUIColors.Primary.WithAlpha(35);
        else
            bgColor = FUIColors.Background2.WithAlpha(100);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRoundRect(bounds, 4, 4, bgPaint);

        // Frame
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = isSelected ? FUIColors.Active : (isHovered ? FUIColors.FrameBright : FUIColors.Frame.WithAlpha(100)),
            StrokeWidth = isSelected ? 2f : 1f
        };
        canvas.DrawRoundRect(bounds, 4, 4, framePaint);

        // Output name (centered vertically)
        float leftTextX = bounds.Left + 12;
        FUIRenderer.DrawText(canvas, outputName, new SKPoint(leftTextX, bounds.MidY + 5),
            isSelected ? FUIColors.Active : FUIColors.TextPrimary, 12f, true);

        // Right side indicator: keyboard keycaps or binding dot
        if (hasKeyParts)
        {
            // Draw keycaps right-aligned within available space
            float keycapHeight = 16f;
            float keycapGap = 2f;
            float keycapPadding = 6f;  // Padding inside each keycap (left + right)
            float fontSize = 8f;  // Slightly smaller font for compact display
            float scaledFontSize = FUIRenderer.ScaleFont(fontSize);
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

        // If we have a selected binding with a device, show the device silhouette
        // For now, show placeholder
        float centerX = bounds.MidX;
        float centerY = bounds.MidY;

        // Draw the joystick SVG if available - use same brightness as device tab
        if (_joystickSvg?.Picture != null)
        {
            // Limit size to 900px max and apply same rendering as device tab
            float maxSize = 900f;
            float maxWidth = Math.Min(bounds.Width - 40, maxSize);
            float maxHeight = Math.Min(bounds.Height - 40, maxSize);

            // Create constrained bounds centered in the panel
            float constrainedWidth = Math.Min(maxWidth, maxHeight); // Keep square-ish
            float constrainedHeight = constrainedWidth;
            var constrainedBounds = new SKRect(
                centerX - constrainedWidth / 2,
                centerY - constrainedHeight / 2,
                centerX + constrainedWidth / 2,
                centerY + constrainedHeight / 2
            );

            // Mappings tab always shows joystick SVG (vJoy is a virtual joystick)
            // Don't use _deviceMap here - that's for the Devices tab context
            DrawSvgInBounds(canvas, _joystickSvg, constrainedBounds, false);
        }
        else
        {
            // Placeholder text
            FUIRenderer.DrawTextCentered(canvas, "Device Preview",
                new SKRect(bounds.Left, centerY - 20, bounds.Right, centerY + 20),
                FUIColors.TextDim, 14f);
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
        float leftMargin = bounds.Left + frameInset + 15;
        float rightMargin = bounds.Right - frameInset - 15;

        // Title
        FUIRenderer.DrawText(canvas, "MAPPING SETTINGS", new SKPoint(leftMargin, y + 12), FUIColors.TextBright, 14f, true);
        y += 35;

        // Show settings for selected row
        if (_selectedMappingRow < 0)
        {
            FUIRenderer.DrawText(canvas, "Select an output to configure",
                new SKPoint(leftMargin, y + 30), FUIColors.TextDim, 12f);
            return;
        }

        // Determine if axis or button based on current category
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        string outputName = GetSelectedOutputName();

        FUIRenderer.DrawText(canvas, outputName, new SKPoint(leftMargin, y + 15), FUIColors.Active, 13f, true);
        y += 35;

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

        FUIRenderer.DrawText(canvas, "INPUT SOURCES", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
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

            FUIRenderer.DrawText(canvas, "No input mapped", new SKPoint(leftMargin + 10, emptyBounds.MidY + 4), FUIColors.TextDisabled, 11f);
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
                FUIRenderer.DrawText(canvas, inputTypeText, new SKPoint(leftMargin + 8, y + 16), FUIColors.TextPrimary, 11f);

                // Line 2: Device name (smaller, dimmer) - vertically centered in bottom half
                FUIRenderer.DrawText(canvas, input.DeviceName, new SKPoint(leftMargin + 8, y + 32), FUIColors.TextDim, 9f);

                // Remove [×] button (full height of row)
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

                FUIRenderer.DrawTextCentered(canvas, "×", removeBounds,
                    removeHovered ? FUIColors.Warning : FUIColors.TextDim, 14f);

                _inputSourceRemoveBounds.Add(removeBounds);
                y += rowHeight + rowGap;
            }
        }

        // Listening indicator
        if (isListening)
        {
            var listenBounds = new SKRect(leftMargin, y, rightMargin, y + rowHeight);
            byte alpha = (byte)(180 + MathF.Sin(_pulsePhase * 3) * 60);

            using var listenBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Warning.WithAlpha(40) };
            canvas.DrawRoundRect(listenBounds, 3, 3, listenBgPaint);

            using var listenFramePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.Warning.WithAlpha(alpha),
                StrokeWidth = 2f
            };
            canvas.DrawRoundRect(listenBounds, 3, 3, listenFramePaint);

            FUIRenderer.DrawText(canvas, "Press input...", new SKPoint(leftMargin + 10, y + 18),
                FUIColors.Warning.WithAlpha(alpha), 11f);
            y += rowHeight + rowGap;
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
            addHovered ? FUIColors.Active : FUIColors.TextPrimary, 11f);
        y += 28 + 15;

        return y;
    }

    private List<InputSource> GetInputsForSelectedOutput()
    {
        var inputs = new List<InputSource>();
        if (_selectedMappingRow < 0) return inputs;
        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count) return inputs;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return inputs;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        int outputIndex = _selectedMappingRow;

        if (isAxis)
        {
            var mapping = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);
            if (mapping != null)
                inputs.AddRange(mapping.Inputs);
        }
        else
        {
            // For button rows, find mapping for this vJoy button slot
            // Check both VJoyButton and Keyboard output types (both map to button slots)
            var mapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);
            if (mapping != null)
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

    private void DrawAxisSettings(SKCanvas canvas, float leftMargin, float rightMargin, float y, float bottom)
    {
        float width = rightMargin - leftMargin;

        // Response Curve header
        FUIRenderer.DrawText(canvas, "RESPONSE CURVE", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += FUIRenderer.ScaleLineHeight(18f);

        // Symmetrical, Centre, and Invert checkboxes on their own row
        // Symmetrical on left, Centre and Invert on right
        float checkboxSize = FUIRenderer.ScaleLineHeight(12f);
        float rowHeight = FUIRenderer.ScaleLineHeight(16f);
        float checkboxY = y + (rowHeight - checkboxSize) / 2; // Center checkbox in row
        float fontSize = 9f;
        float scaledFontSize = FUIRenderer.ScaleFont(fontSize);
        float textY = y + (rowHeight / 2) + (scaledFontSize / 3); // Center text baseline

        // Measure label widths for positioning
        using var labelPaint = FUIRenderer.CreateTextPaint(FUIColors.TextDim, scaledFontSize);
        float invertLabelWidth = labelPaint.MeasureText("Invert");
        float centreLabelWidth = labelPaint.MeasureText("Centre");
        float symmetricalLabelWidth = labelPaint.MeasureText("Symmetrical");
        float labelGap = FUIRenderer.ScaleSpacing(4f);
        float checkboxGap = FUIRenderer.ScaleSpacing(12f);

        // Symmetrical checkbox (leftmost) - checkbox then label
        _curveSymmetricalCheckboxBounds = new SKRect(leftMargin, checkboxY, leftMargin + checkboxSize, checkboxY + checkboxSize);
        DrawCheckbox(canvas, _curveSymmetricalCheckboxBounds, _curveSymmetrical);
        FUIRenderer.DrawText(canvas, "Symmetrical", new SKPoint(leftMargin + checkboxSize + labelGap, textY), FUIColors.TextDim, fontSize);

        // Invert checkbox (rightmost) - label then checkbox
        float invertCheckX = rightMargin - checkboxSize;
        _invertToggleBounds = new SKRect(invertCheckX, checkboxY, invertCheckX + checkboxSize, checkboxY + checkboxSize);
        DrawCheckbox(canvas, _invertToggleBounds, _axisInverted);
        FUIRenderer.DrawText(canvas, "Invert", new SKPoint(invertCheckX - invertLabelWidth - labelGap, textY), FUIColors.TextDim, fontSize);

        // Centre checkbox (left of Invert) - label then checkbox
        float centreCheckX = invertCheckX - invertLabelWidth - labelGap - checkboxGap - checkboxSize;
        _deadzoneCenterCheckboxBounds = new SKRect(centreCheckX, checkboxY, centreCheckX + checkboxSize, checkboxY + checkboxSize);
        DrawCheckbox(canvas, _deadzoneCenterCheckboxBounds, _deadzoneCenterEnabled);
        FUIRenderer.DrawText(canvas, "Centre", new SKPoint(centreCheckX - centreLabelWidth - labelGap, textY), FUIColors.TextDim, fontSize);

        y += rowHeight + FUIRenderer.ScaleSpacing(6f);

        // Curve preset buttons - store bounds for click handling
        string[] presets = { "LINEAR", "S-CURVE", "EXPO", "CUSTOM" };
        float buttonWidth = (width - 9) / presets.Length;
        float buttonHeight = FUIRenderer.ScaleLineHeight(22f);

        for (int i = 0; i < presets.Length; i++)
        {
            var presetBounds = new SKRect(
                leftMargin + i * (buttonWidth + 3), y,
                leftMargin + i * (buttonWidth + 3) + buttonWidth, y + buttonHeight);

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
            var bgColor = isActive ? FUIColors.Active.WithAlpha(60) : FUIColors.Background2;
            var frameColor = isActive ? FUIColors.Active : FUIColors.Frame;
            var textColor = isActive ? FUIColors.TextBright : FUIColors.TextDim;

            using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
            canvas.DrawRect(presetBounds, bgPaint);

            using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = frameColor, StrokeWidth = 1f };
            canvas.DrawRect(presetBounds, framePaint);

            FUIRenderer.DrawTextCentered(canvas, presets[i], presetBounds, textColor, 8f);
        }
        y += buttonHeight + FUIRenderer.ScaleSpacing(6f);

        // Curve editor visualization
        float curveHeight = 140f;
        _curveEditorBounds = new SKRect(leftMargin, y, rightMargin, y + curveHeight);
        DrawCurveVisualization(canvas, _curveEditorBounds);
        y += curveHeight + FUIRenderer.ScaleLineHeight(16f);

        // Deadzone section
        if (y + 100 < bottom)
        {
            // Header row: "DEADZONE" label + preset buttons + selected handle indicator
            FUIRenderer.DrawText(canvas, "DEADZONE", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);

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
                using var btnBg = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
                canvas.DrawRect(btnBounds, btnBg);
                using var btnFrame = new SKPaint { Style = SKPaintStyle.Stroke, Color = enabled ? FUIColors.Frame : FUIColors.Frame.WithAlpha(100), StrokeWidth = 1f };
                canvas.DrawRect(btnBounds, btnFrame);
                FUIRenderer.DrawTextCentered(canvas, presetLabels[col], btnBounds, enabled ? FUIColors.TextDim : FUIColors.TextDim.WithAlpha(100), 9f);
            }

            // Show which handle is selected (if any)
            if (_selectedDeadzoneHandle >= 0)
            {
                string[] handleNames = { "Start", "Ctr-", "Ctr+", "End" };
                string selectedName = handleNames[_selectedDeadzoneHandle];
                FUIRenderer.DrawText(canvas, $"[{selectedName}]", new SKPoint(presetStartX - 45, y), FUIColors.Active, 9f);
            }
            y += FUIRenderer.ScaleLineHeight(20f);

            // Dual deadzone slider (always shows min/max, optionally shows center handles)
            float sliderHeight = FUIRenderer.ScaleLineHeight(24f);
            _deadzoneSliderBounds = new SKRect(leftMargin, y, rightMargin, y + sliderHeight);
            DrawDualDeadzoneSlider(canvas, _deadzoneSliderBounds);
            y += sliderHeight + FUIRenderer.ScaleSpacing(6f);

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
                FUIRenderer.DrawText(canvas, $"{_deadzoneMin:F2}", new SKPoint(leftMargin, y), FUIColors.TextDim, 9f);
                FUIRenderer.DrawText(canvas, $"{_deadzoneCenterMin:F2}", new SKPoint(leftTrackRight - 25, y), FUIColors.TextDim, 9f);
                FUIRenderer.DrawText(canvas, $"{_deadzoneCenterMax:F2}", new SKPoint(rightTrackLeft, y), FUIColors.TextDim, 9f);
                FUIRenderer.DrawText(canvas, $"{_deadzoneMax:F2}", new SKPoint(rightMargin - 20, y), FUIColors.TextDim, 9f);
            }
            else
            {
                // Single track - just show start and end at edges
                FUIRenderer.DrawText(canvas, $"{_deadzoneMin:F2}", new SKPoint(leftMargin, y), FUIColors.TextDim, 9f);
                FUIRenderer.DrawText(canvas, $"{_deadzoneMax:F2}", new SKPoint(rightMargin - 20, y), FUIColors.TextDim, 9f);
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

    private void DrawCheckbox(SKCanvas canvas, SKRect bounds, bool isChecked)
    {
        // Box background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = isChecked ? FUIColors.Active.WithAlpha(60) : FUIColors.Background2
        };
        canvas.DrawRoundRect(bounds, 2, 2, bgPaint);

        // Box frame
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = isChecked ? FUIColors.Active : FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRoundRect(bounds, 2, 2, framePaint);

        // Checkmark
        if (isChecked)
        {
            using var checkPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.Active,
                StrokeWidth = 2f,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round
            };
            float cx = bounds.MidX;
            float cy = bounds.MidY;
            float s = bounds.Width * 0.3f;
            canvas.DrawLine(cx - s, cy, cx - s * 0.3f, cy + s * 0.7f, checkPaint);
            canvas.DrawLine(cx - s * 0.3f, cy + s * 0.7f, cx + s, cy - s * 0.5f, checkPaint);
        }
    }

    private void DrawInteractiveSlider(SKCanvas canvas, SKRect bounds, float value, SKColor color, bool dragging)
    {
        // Track background
        using var trackPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        canvas.DrawRoundRect(bounds, 4, 4, trackPaint);

        // Track frame
        using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
        canvas.DrawRoundRect(bounds, 4, 4, framePaint);

        // Fill
        float fillWidth = bounds.Width * Math.Clamp(value, 0, 1);
        if (fillWidth > 2)
        {
            var fillBounds = new SKRect(bounds.Left + 1, bounds.Top + 1, bounds.Left + fillWidth - 1, bounds.Bottom - 1);
            using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = color.WithAlpha(100) };
            canvas.DrawRoundRect(fillBounds, 3, 3, fillPaint);
        }

        // Handle
        float handleX = bounds.Left + fillWidth;
        float handleRadius = dragging ? 8f : 6f;
        using var handlePaint = new SKPaint { Style = SKPaintStyle.Fill, Color = dragging ? color : FUIColors.TextPrimary, IsAntialias = true };
        canvas.DrawCircle(handleX, bounds.MidY, handleRadius, handlePaint);

        using var handleStroke = new SKPaint { Style = SKPaintStyle.Stroke, Color = color, StrokeWidth = 1.5f, IsAntialias = true };
        canvas.DrawCircle(handleX, bounds.MidY, handleRadius, handleStroke);
    }

    private void DrawDurationSlider(SKCanvas canvas, SKRect bounds, float value, bool dragging)
    {
        value = Math.Clamp(value, 0f, 1f);

        // Track background
        using var trackPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        canvas.DrawRoundRect(bounds, 4, 4, trackPaint);

        // Track frame
        using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
        canvas.DrawRoundRect(bounds, 4, 4, framePaint);

        // Fill
        float fillWidth = bounds.Width * value;
        if (fillWidth > 2)
        {
            var fillBounds = new SKRect(bounds.Left + 1, bounds.Top + 1, bounds.Left + fillWidth - 1, bounds.Bottom - 1);
            using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(80) };
            canvas.DrawRoundRect(fillBounds, 3, 3, fillPaint);
        }

        // Handle
        float handleX = bounds.Left + fillWidth;
        float handleRadius = dragging ? 8f : 6f;
        using var handlePaint = new SKPaint { Style = SKPaintStyle.Fill, Color = dragging ? FUIColors.Active : FUIColors.TextPrimary, IsAntialias = true };
        canvas.DrawCircle(handleX, bounds.MidY, handleRadius, handlePaint);

        using var handleStroke = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active, StrokeWidth = 1.5f, IsAntialias = true };
        canvas.DrawCircle(handleX, bounds.MidY, handleRadius, handleStroke);
    }

    private void DrawButtonSettings(SKCanvas canvas, float leftMargin, float rightMargin, float y, float bottom)
    {
        float width = rightMargin - leftMargin;

        // OUTPUT TYPE section - vJoy Button vs Keyboard
        FUIRenderer.DrawText(canvas, "OUTPUT TYPE", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
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

            FUIRenderer.DrawTextCentered(canvas, outputTypes[i], typeBounds, textColor, 11f);
        }
        y += typeButtonHeight + 15;

        // KEY COMBO section (only when Keyboard is selected)
        if (_outputTypeIsKeyboard)
        {
            FUIRenderer.DrawText(canvas, "KEY COMBO", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
            y += 20;

            float keyFieldHeight = 32f;
            _keyCaptureBounds = new SKRect(leftMargin, y, rightMargin, y + keyFieldHeight);

            // Draw key capture field
            var keyBgColor = _isCapturingKey
                ? FUIColors.Warning.WithAlpha(40)
                : (_keyCaptureBoundsHovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);

            using var keyBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = keyBgColor };
            canvas.DrawRoundRect(_keyCaptureBounds, 3, 3, keyBgPaint);

            var keyFrameColor = _isCapturingKey
                ? FUIColors.Warning
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
                byte alpha = (byte)(180 + MathF.Sin(_pulsePhase * 3) * 60);
                FUIRenderer.DrawTextCentered(canvas, "Press key combo...", _keyCaptureBounds, FUIColors.Warning.WithAlpha(alpha), 11f);
            }
            else if (!string.IsNullOrEmpty(_selectedKeyName))
            {
                // Draw keycaps centered in the field
                DrawKeycapsInBounds(canvas, _keyCaptureBounds, _selectedKeyName, _selectedModifiers);
            }
            else
            {
                FUIRenderer.DrawTextCentered(canvas, "Click to capture key", _keyCaptureBounds, FUIColors.TextDim, 11f);
            }
            y += keyFieldHeight + 15;
        }

        // Button Mode section
        FUIRenderer.DrawText(canvas, "BUTTON MODE", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += 20;

        // Mode buttons - all on one row
        string[] modes = { "Normal", "Toggle", "Pulse", "Hold" };
        float buttonHeight = 26f;
        float buttonGap = 4f;
        float totalGap = buttonGap * (modes.Length - 1);
        float buttonWidth = (width - totalGap) / modes.Length;

        for (int i = 0; i < modes.Length; i++)
        {
            float buttonX = leftMargin + i * (buttonWidth + buttonGap);
            var modeBounds = new SKRect(buttonX, y, buttonX + buttonWidth, y + buttonHeight);
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
                selected ? FUIColors.Active : FUIColors.TextPrimary, 9f);

            _buttonModeBounds[i] = modeBounds;
        }
        y += buttonHeight + 12;

        // Duration slider for Pulse mode
        if (_selectedButtonMode == ButtonMode.Pulse && y + 50 < bottom)
        {
            FUIRenderer.DrawText(canvas, "PULSE DURATION", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
            y += 18;

            float sliderHeight = 24f;
            _pulseDurationSliderBounds = new SKRect(leftMargin, y, rightMargin - 50, y + sliderHeight);

            // Normalize value: 100-1000ms mapped to 0-1
            float normalizedPulse = (_pulseDurationMs - 100f) / 900f;
            DrawDurationSlider(canvas, _pulseDurationSliderBounds, normalizedPulse, _draggingPulseDuration);

            // Value label
            FUIRenderer.DrawText(canvas, $"{_pulseDurationMs}ms",
                new SKPoint(rightMargin - 45, y + sliderHeight / 2 + 4), FUIColors.TextPrimary, 10f);

            y += sliderHeight + 12;
        }

        // Duration slider for Hold mode
        if (_selectedButtonMode == ButtonMode.HoldToActivate && y + 50 < bottom)
        {
            FUIRenderer.DrawText(canvas, "HOLD DURATION", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
            y += 18;

            float sliderHeight = 24f;
            _holdDurationSliderBounds = new SKRect(leftMargin, y, rightMargin - 50, y + sliderHeight);

            // Normalize value: 200-2000ms mapped to 0-1
            float normalizedHold = (_holdDurationMs - 200f) / 1800f;
            DrawDurationSlider(canvas, _holdDurationSliderBounds, normalizedHold, _draggingHoldDuration);

            // Value label
            FUIRenderer.DrawText(canvas, $"{_holdDurationMs}ms",
                new SKPoint(rightMargin - 45, y + sliderHeight / 2 + 4), FUIColors.TextPrimary, 10f);

            y += sliderHeight + 12;
        }

        // Clear binding button
        if (y + 40 < bottom)
        {
            var clearBounds = new SKRect(leftMargin, y, rightMargin, y + 32);
            _clearAllButtonBounds = clearBounds;

            using var clearBgPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = _clearAllButtonHovered ? FUIColors.Warning.WithAlpha(40) : FUIColors.Background2
            };
            canvas.DrawRoundRect(clearBounds, 3, 3, clearBgPaint);

            using var clearFramePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = _clearAllButtonHovered ? FUIColors.Warning : FUIColors.Warning.WithAlpha(150),
                StrokeWidth = _clearAllButtonHovered ? 2f : 1f
            };
            canvas.DrawRoundRect(clearBounds, 3, 3, clearFramePaint);

            FUIRenderer.DrawTextCentered(canvas, "Clear Binding", clearBounds,
                _clearAllButtonHovered ? FUIColors.Warning : FUIColors.Warning.WithAlpha(200), 11f);
        }
    }

    /// <summary>
    /// Format key combo for display as simple text (used in mapping names)
    /// </summary>
    private string FormatKeyComboForDisplay(string keyName, List<string>? modifiers)
    {
        if (string.IsNullOrEmpty(keyName)) return "";

        var parts = new List<string>();
        if (modifiers != null && modifiers.Count > 0)
        {
            parts.AddRange(modifiers);
        }
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    /// <summary>
    /// Draw keycaps centered within given bounds
    /// </summary>
    private void DrawKeycapsInBounds(SKCanvas canvas, SKRect bounds, string keyName, List<string>? modifiers)
    {
        // Build list of key parts
        var parts = new List<string>();
        if (modifiers != null && modifiers.Count > 0)
        {
            parts.AddRange(modifiers);
        }
        parts.Add(keyName);

        float keycapHeight = 20f;
        float keycapGap = 4f;
        float keycapPadding = 8f;  // Padding on each side of text
        float fontSize = 10f;
        float scaledFontSize = FUIRenderer.ScaleFont(fontSize);

        // Use consistent font for measurement
        using var measurePaint = new SKPaint
        {
            TextSize = scaledFontSize,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal)
        };

        // Calculate total width of all keycaps
        float totalWidth = 0;
        var keycapWidths = new List<float>();
        var textWidths = new List<float>();
        foreach (var part in parts)
        {
            string upperPart = part.ToUpperInvariant();
            float textWidth = measurePaint.MeasureText(upperPart);
            textWidths.Add(textWidth);
            float keycapWidth = textWidth + keycapPadding * 2;
            keycapWidths.Add(keycapWidth);
            totalWidth += keycapWidth;
        }
        totalWidth += (parts.Count - 1) * keycapGap;

        // Start position to center the keycaps
        float startX = bounds.MidX - totalWidth / 2;
        float keycapTop = bounds.MidY - keycapHeight / 2;

        // Draw each keycap
        for (int i = 0; i < parts.Count; i++)
        {
            string keyText = parts[i].ToUpperInvariant();
            float keycapWidth = keycapWidths[i];
            var keycapBounds = new SKRect(startX, keycapTop, startX + keycapWidth, keycapTop + keycapHeight);

            // Keycap background
            using var keycapBgPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = FUIColors.TextPrimary.WithAlpha(25),
                IsAntialias = true
            };
            canvas.DrawRoundRect(keycapBounds, 3, 3, keycapBgPaint);

            // Keycap frame
            using var keycapFramePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.TextPrimary.WithAlpha(150),
                StrokeWidth = 1f,
                IsAntialias = true
            };
            canvas.DrawRoundRect(keycapBounds, 3, 3, keycapFramePaint);

            // Keycap text - draw with explicit padding from left edge
            float textX = startX + keycapPadding;
            float textY = keycapBounds.MidY + scaledFontSize / 3;
            using var textPaint = new SKPaint
            {
                Color = FUIColors.TextPrimary,
                TextSize = scaledFontSize,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal)
            };
            canvas.DrawText(keyText, textX, textY, textPaint);

            startX += keycapWidth + keycapGap;
        }
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
            FUIRenderer.DrawText(canvas, tickLabels[i], new SKPoint(labelX, bounds.Bottom + tickLen + labelOffset + 7), FUIColors.TextDim, 7f);
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
            FUIRenderer.DrawText(canvas, tickLabels[i], new SKPoint(labelX, labelY), FUIColors.TextDim, 7f);
        }

        // Axis labels
        FUIRenderer.DrawText(canvas, "IN", new SKPoint(bounds.MidX - 6, bounds.Bottom + 22), FUIColors.TextDim, 8f);

        // Rotated "OUT" label
        canvas.Save();
        canvas.Translate(bounds.Left - 24, bounds.MidY + 8);
        canvas.RotateDegrees(-90);
        FUIRenderer.DrawText(canvas, "OUT", new SKPoint(0, 0), FUIColors.TextDim, 8f);
        canvas.Restore();
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

                FUIRenderer.DrawText(canvas, label, new SKPoint(x - 22, labelY), FUIColors.TextBright, 8f);
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

    private int FindCurvePointAt(SKPoint screenPt, SKRect bounds)
    {
        const float HitRadius = 12f;

        for (int i = 0; i < _curveControlPoints.Count; i++)
        {
            var pt = _curveControlPoints[i];

            // Skip center point - it's not selectable
            bool isCenterPoint = Math.Abs(pt.X - 0.5f) < 0.01f && Math.Abs(pt.Y - 0.5f) < 0.01f;
            if (isCenterPoint)
                continue;

            float x = bounds.Left + pt.X * bounds.Width;

            // Apply inversion to display Y position to match the visual
            float displayY = _axisInverted ? (1f - pt.Y) : pt.Y;
            float y = bounds.Bottom - displayY * bounds.Height;

            float dist = MathF.Sqrt(MathF.Pow(screenPt.X - x, 2) + MathF.Pow(screenPt.Y - y, 2));
            if (dist <= HitRadius)
                return i;
        }
        return -1;
    }

    private int FindDeadzoneHandleAt(SKPoint screenPt)
    {
        const float HitRadius = 12f;
        var bounds = _deadzoneSliderBounds;
        if (bounds.Width <= 0) return -1;

        // Convert deadzone values to 0..1 range
        float minPos = (_deadzoneMin + 1f) / 2f;
        float centerMinPos = (_deadzoneCenterMin + 1f) / 2f;
        float centerMaxPos = (_deadzoneCenterMax + 1f) / 2f;
        float maxPos = (_deadzoneMax + 1f) / 2f;

        if (_deadzoneCenterEnabled)
        {
            // Two separate tracks - must calculate handle positions on each track
            // Gap must match DrawDualDeadzoneSlider
            float gap = 24f;
            float centerX = bounds.MidX;

            // Left track: from bounds.Left to centerX - gap/2
            float leftTrackLeft = bounds.Left;
            float leftTrackRight = centerX - gap / 2;
            float leftTrackWidth = leftTrackRight - leftTrackLeft;

            // Right track: from centerX + gap/2 to bounds.Right
            float rightTrackLeft = centerX + gap / 2;
            float rightTrackRight = bounds.Right;
            float rightTrackWidth = rightTrackRight - rightTrackLeft;

            // Map positions to track coordinates
            float minPosInLeft = Math.Clamp((minPos - 0f) / 0.5f, 0f, 1f);
            float ctrMinPosInLeft = Math.Clamp((centerMinPos - 0f) / 0.5f, 0f, 1f);
            float ctrMaxPosInRight = Math.Clamp((centerMaxPos - 0.5f) / 0.5f, 0f, 1f);
            float maxPosInRight = Math.Clamp((maxPos - 0.5f) / 0.5f, 0f, 1f);

            // Calculate screen positions for each handle
            float minHandleX = leftTrackLeft + minPosInLeft * leftTrackWidth;
            float ctrMinHandleX = leftTrackLeft + ctrMinPosInLeft * leftTrackWidth;
            float ctrMaxHandleX = rightTrackLeft + ctrMaxPosInRight * rightTrackWidth;
            float maxHandleX = rightTrackLeft + maxPosInRight * rightTrackWidth;

            // Check each handle (check all 4)
            float[] handleXs = { minHandleX, ctrMinHandleX, ctrMaxHandleX, maxHandleX };
            for (int i = 0; i < 4; i++)
            {
                float dist = MathF.Sqrt(MathF.Pow(screenPt.X - handleXs[i], 2) + MathF.Pow(screenPt.Y - bounds.MidY, 2));
                if (dist <= HitRadius)
                    return i;
            }
        }
        else
        {
            // Single track - only min (0) and max (3) handles
            float minHandleX = bounds.Left + minPos * bounds.Width;
            float maxHandleX = bounds.Left + maxPos * bounds.Width;

            float distMin = MathF.Sqrt(MathF.Pow(screenPt.X - minHandleX, 2) + MathF.Pow(screenPt.Y - bounds.MidY, 2));
            if (distMin <= HitRadius) return 0;

            float distMax = MathF.Sqrt(MathF.Pow(screenPt.X - maxHandleX, 2) + MathF.Pow(screenPt.Y - bounds.MidY, 2));
            if (distMax <= HitRadius) return 3;
        }

        return -1;
    }

    private void UpdateDraggedDeadzoneHandle(SKPoint screenPt)
    {
        if (_draggingDeadzoneHandle < 0) return;
        var bounds = _deadzoneSliderBounds;
        if (bounds.Width <= 0) return;

        float value;

        if (_deadzoneCenterEnabled)
        {
            // Two-track layout - convert screen position to value based on which track
            // Gap must match DrawDualDeadzoneSlider
            float gap = 24f;
            float centerX = bounds.MidX;

            // Left track: maps to -1..0 (handles 0 and 1)
            float leftTrackLeft = bounds.Left;
            float leftTrackRight = centerX - gap / 2;
            float leftTrackWidth = leftTrackRight - leftTrackLeft;

            // Right track: maps to 0..1 (handles 2 and 3)
            float rightTrackLeft = centerX + gap / 2;
            float rightTrackRight = bounds.Right;
            float rightTrackWidth = rightTrackRight - rightTrackLeft;

            switch (_draggingDeadzoneHandle)
            {
                case 0: // Min handle on left track
                    float normLeft0 = Math.Clamp((screenPt.X - leftTrackLeft) / leftTrackWidth, 0f, 1f);
                    value = normLeft0 - 1f; // Maps 0..1 to -1..0
                    value = Math.Clamp(value, -1f, _deadzoneCenterMin - 0.02f);
                    _deadzoneMin = value;
                    break;
                case 1: // CenterMin handle on left track (right edge)
                    float normLeft1 = Math.Clamp((screenPt.X - leftTrackLeft) / leftTrackWidth, 0f, 1f);
                    value = normLeft1 - 1f; // Maps 0..1 to -1..0
                    value = Math.Clamp(value, _deadzoneMin + 0.02f, 0f);
                    _deadzoneCenterMin = value;
                    break;
                case 2: // CenterMax handle on right track (left edge)
                    float normRight2 = Math.Clamp((screenPt.X - rightTrackLeft) / rightTrackWidth, 0f, 1f);
                    value = normRight2; // Maps 0..1 to 0..1
                    value = Math.Clamp(value, 0f, _deadzoneMax - 0.02f);
                    _deadzoneCenterMax = value;
                    break;
                case 3: // Max handle on right track
                    float normRight3 = Math.Clamp((screenPt.X - rightTrackLeft) / rightTrackWidth, 0f, 1f);
                    value = normRight3; // Maps 0..1 to 0..1
                    value = Math.Clamp(value, _deadzoneCenterMax + 0.02f, 1f);
                    _deadzoneMax = value;
                    break;
            }
        }
        else
        {
            // Single track layout - convert screen X to -1..1 range
            float normalized = (screenPt.X - bounds.Left) / bounds.Width;
            value = normalized * 2f - 1f;

            switch (_draggingDeadzoneHandle)
            {
                case 0: // Min handle
                    value = Math.Clamp(value, -1f, _deadzoneMax - 0.1f);
                    _deadzoneMin = value;
                    break;
                case 3: // Max handle
                    value = Math.Clamp(value, _deadzoneMin + 0.1f, 1f);
                    _deadzoneMax = value;
                    break;
            }
        }
    }

    private void UpdateDraggedCurvePoint(SKPoint screenPt)
    {
        if (_draggingCurvePoint < 0 || _draggingCurvePoint >= _curveControlPoints.Count)
            return;

        var graphPt = CurveScreenToGraph(screenPt, _curveEditorBounds);

        // Constrain endpoints to X edges
        if (_draggingCurvePoint == 0)
            graphPt.X = 0;
        else if (_draggingCurvePoint == _curveControlPoints.Count - 1)
            graphPt.X = 1;
        else
        {
            // Interior points: constrain X between neighbors
            float minX = _curveControlPoints[_draggingCurvePoint - 1].X + 0.02f;
            float maxX = _curveControlPoints[_draggingCurvePoint + 1].X - 0.02f;
            graphPt.X = Math.Clamp(graphPt.X, minX, maxX);
        }

        _curveControlPoints[_draggingCurvePoint] = graphPt;

        // If symmetrical mode is enabled, mirror the change
        if (_curveSymmetrical)
        {
            UpdateSymmetricalPoint(_draggingCurvePoint, graphPt);
        }
    }

    /// <summary>
    /// Update the symmetrical counterpart of a curve point.
    /// Points are mirrored around the center (0.5, 0.5).
    /// </summary>
    private void UpdateSymmetricalPoint(int pointIndex, SKPoint graphPt)
    {
        // Mirror point: (x, y) -> (1-x, 1-y)
        float mirrorX = 1f - graphPt.X;
        float mirrorY = 1f - graphPt.Y;
        var mirrorPt = new SKPoint(mirrorX, mirrorY);

        // Find the corresponding mirror point in the list
        // Points are stored sorted by X, so we need to find the one with matching mirror X
        int mirrorIndex = FindMirrorPointIndex(pointIndex, mirrorX);

        if (mirrorIndex >= 0 && mirrorIndex != pointIndex)
        {
            // Update mirror point, but constrain to valid range
            if (mirrorIndex > 0 && mirrorIndex < _curveControlPoints.Count - 1)
            {
                // Interior point - constrain X between neighbors
                float minX = _curveControlPoints[mirrorIndex - 1].X + 0.02f;
                float maxX = _curveControlPoints[mirrorIndex + 1].X - 0.02f;
                mirrorPt = new SKPoint(Math.Clamp(mirrorPt.X, minX, maxX), mirrorPt.Y);
            }
            else if (mirrorIndex == 0)
            {
                mirrorPt = new SKPoint(0, mirrorPt.Y);
            }
            else if (mirrorIndex == _curveControlPoints.Count - 1)
            {
                mirrorPt = new SKPoint(1, mirrorPt.Y);
            }

            _curveControlPoints[mirrorIndex] = mirrorPt;
        }
    }

    /// <summary>
    /// Find the index of the mirror point for symmetry.
    /// Returns -1 if no suitable mirror point exists.
    /// </summary>
    private int FindMirrorPointIndex(int sourceIndex, float targetX)
    {
        // Special cases for endpoints
        if (sourceIndex == 0) return _curveControlPoints.Count - 1;
        if (sourceIndex == _curveControlPoints.Count - 1) return 0;

        // For interior points, find the one closest to the mirror X position
        int bestIndex = -1;
        float bestDist = float.MaxValue;

        for (int i = 0; i < _curveControlPoints.Count; i++)
        {
            if (i == sourceIndex) continue;

            float dist = Math.Abs(_curveControlPoints[i].X - targetX);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    /// <summary>
    /// Make the current curve points symmetrical around the center.
    /// </summary>
    private void MakeCurveSymmetrical()
    {
        if (_curveControlPoints.Count < 2) return;

        // Create a new symmetrical set of points
        var newPoints = new List<SKPoint>();

        // Always include start point
        newPoints.Add(new SKPoint(0, 0));

        // For each point in the left half (X < 0.5), create its mirror
        var leftHalf = _curveControlPoints
            .Where(p => p.X > 0 && p.X < 0.5f)
            .OrderBy(p => p.X)
            .ToList();

        foreach (var pt in leftHalf)
        {
            newPoints.Add(pt);
        }

        // Add center point if there's one
        var centerPoint = _curveControlPoints.FirstOrDefault(p => Math.Abs(p.X - 0.5f) < 0.02f);
        if (centerPoint.X > 0.4f && centerPoint.X < 0.6f)
        {
            newPoints.Add(new SKPoint(0.5f, 0.5f)); // Center is always (0.5, 0.5) for perfect symmetry
        }
        else if (leftHalf.Count > 0)
        {
            // Add a center point if we have left half points
            newPoints.Add(new SKPoint(0.5f, 0.5f));
        }

        // Add mirrored points from left half (in reverse order for right half)
        for (int i = leftHalf.Count - 1; i >= 0; i--)
        {
            var pt = leftHalf[i];
            newPoints.Add(new SKPoint(1f - pt.X, 1f - pt.Y));
        }

        // Always include end point
        newPoints.Add(new SKPoint(1, 1));

        _curveControlPoints = newPoints;
    }

    private void AddCurveControlPoint(SKPoint graphPt)
    {
        // Don't add points at exact endpoints
        if (graphPt.X <= 0.01f || graphPt.X >= 0.99f) return;

        // Find insertion position (maintain sorted order by X)
        int insertIndex = 0;
        for (int i = 0; i < _curveControlPoints.Count; i++)
        {
            if (_curveControlPoints[i].X < graphPt.X)
                insertIndex = i + 1;
        }

        _curveControlPoints.Insert(insertIndex, graphPt);

        // If symmetrical mode is enabled, also add the mirror point
        if (_curveSymmetrical)
        {
            float mirrorX = 1f - graphPt.X;
            float mirrorY = 1f - graphPt.Y;

            // Don't add if mirror point would be too close to existing point
            bool tooClose = _curveControlPoints.Any(p => Math.Abs(p.X - mirrorX) < 0.04f);
            if (!tooClose && mirrorX > 0.01f && mirrorX < 0.99f)
            {
                // Find insertion position for mirror point
                int mirrorInsertIndex = 0;
                for (int i = 0; i < _curveControlPoints.Count; i++)
                {
                    if (_curveControlPoints[i].X < mirrorX)
                        mirrorInsertIndex = i + 1;
                }

                _curveControlPoints.Insert(mirrorInsertIndex, new SKPoint(mirrorX, mirrorY));
            }
        }

        _selectedCurveType = CurveType.Custom;
        _canvas.Invalidate();
    }

    private void RemoveCurveControlPoint(int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= _curveControlPoints.Count)
            return;

        var pt = _curveControlPoints[pointIndex];

        // Don't remove endpoints (0,0) or (1,1)
        bool isEndpoint = pointIndex == 0 || pointIndex == _curveControlPoints.Count - 1;
        if (isEndpoint)
            return;

        // Don't remove center point (0.5, 0.5)
        bool isCenterPoint = Math.Abs(pt.X - 0.5f) < 0.01f && Math.Abs(pt.Y - 0.5f) < 0.01f;
        if (isCenterPoint)
            return;

        // Remove the point
        _curveControlPoints.RemoveAt(pointIndex);

        // If symmetrical mode is enabled, also remove the mirror point
        if (_curveSymmetrical)
        {
            float mirrorX = 1f - pt.X;

            // Find and remove the mirror point
            for (int i = _curveControlPoints.Count - 1; i >= 0; i--)
            {
                var mirrorPt = _curveControlPoints[i];
                // Skip endpoints and center
                if (i == 0 || i == _curveControlPoints.Count - 1)
                    continue;
                if (Math.Abs(mirrorPt.X - 0.5f) < 0.01f && Math.Abs(mirrorPt.Y - 0.5f) < 0.01f)
                    continue;

                if (Math.Abs(mirrorPt.X - mirrorX) < 0.02f)
                {
                    _curveControlPoints.RemoveAt(i);
                    break;
                }
            }
        }

        _canvas.Invalidate();
    }

    private bool HandleCurvePresetClick(SKPoint pt)
    {
        // Check each stored preset button bound
        for (int i = 0; i < _curvePresetBounds.Length; i++)
        {
            if (_curvePresetBounds[i].Contains(pt))
            {
                _selectedCurveType = i switch
                {
                    0 => CurveType.Linear,
                    1 => CurveType.SCurve,
                    2 => CurveType.Exponential,
                    _ => CurveType.Custom
                };

                // Reset control points when switching to custom
                if (_selectedCurveType == CurveType.Custom && _curveControlPoints.Count == 2)
                {
                    // Add a middle point for custom curve
                    _curveControlPoints = new List<SKPoint>
                    {
                        new(0, 0),
                        new(0.5f, 0.5f),
                        new(1, 1)
                    };
                }

                _canvas.Invalidate();
                return true;
            }
        }

        // Check invert checkbox
        if (_invertToggleBounds.Contains(pt))
        {
            _axisInverted = !_axisInverted;
            _canvas.Invalidate();
            return true;
        }

        // Check symmetrical checkbox (only for Custom curve)
        if (!_curveSymmetricalCheckboxBounds.IsEmpty && _curveSymmetricalCheckboxBounds.Contains(pt))
        {
            _curveSymmetrical = !_curveSymmetrical;
            if (_curveSymmetrical)
            {
                // When enabling symmetry, mirror existing points around center
                MakeCurveSymmetrical();
            }
            _canvas.Invalidate();
            return true;
        }

        // Check centre checkbox and deadzone presets
        if (HandleDeadzonePresetClick(pt))
            return true;

        return false;
    }

    private bool HandleDeadzonePresetClick(SKPoint pt)
    {
        // Centre checkbox click
        if (_deadzoneCenterCheckboxBounds.Contains(pt))
        {
            _deadzoneCenterEnabled = !_deadzoneCenterEnabled;
            // When disabling center, reset center values and clear selection if center handle was selected
            if (!_deadzoneCenterEnabled)
            {
                _deadzoneCenterMin = 0.0f;
                _deadzoneCenterMax = 0.0f;
                if (_selectedDeadzoneHandle == 1 || _selectedDeadzoneHandle == 2)
                    _selectedDeadzoneHandle = -1;
            }
            _canvas.Invalidate();
            return true;
        }

        // Preset buttons - apply to selected handle
        if (_selectedDeadzoneHandle >= 0)
        {
            // Preset values: 0%, 2%, 5%, 10%
            float[] presetValues = { 0.0f, 0.02f, 0.05f, 0.10f };

            for (int i = 0; i < _deadzonePresetBounds.Length; i++)
            {
                if (!_deadzonePresetBounds[i].IsEmpty && _deadzonePresetBounds[i].Contains(pt))
                {
                    float presetVal = presetValues[i];

                    switch (_selectedDeadzoneHandle)
                    {
                        case 0: // Min (Start) - set distance from -1
                            _deadzoneMin = -1.0f + presetVal;
                            break;
                        case 1: // CenterMin - set negative offset from 0
                            _deadzoneCenterMin = -presetVal;
                            break;
                        case 2: // CenterMax - set positive offset from 0
                            _deadzoneCenterMax = presetVal;
                            break;
                        case 3: // Max (End) - set distance from 1
                            _deadzoneMax = 1.0f - presetVal;
                            break;
                    }
                    _canvas.Invalidate();
                    return true;
                }
            }
        }
        return false;
    }

    private void DrawSlider(SKCanvas canvas, SKRect bounds, float value)
    {
        // Track background
        using var trackPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        canvas.DrawRoundRect(bounds, 4, 4, trackPaint);

        // Fill
        float fillWidth = bounds.Width * Math.Clamp(value, 0, 1);
        var fillBounds = new SKRect(bounds.Left, bounds.Top, bounds.Left + fillWidth, bounds.Bottom);
        using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active };
        canvas.DrawRoundRect(fillBounds, 4, 4, fillPaint);

        // Handle
        float handleX = bounds.Left + fillWidth;
        float handleRadius = bounds.Height;
        using var handlePaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.TextBright };
        canvas.DrawCircle(handleX, bounds.MidY, handleRadius, handlePaint);
    }

    private void DrawToggleSwitch(SKCanvas canvas, SKRect bounds, bool on)
    {
        // Track
        SKColor trackColor = on ? FUIColors.Active.WithAlpha(150) : FUIColors.Background2;
        using var trackPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = trackColor };
        canvas.DrawRoundRect(bounds, bounds.Height / 2, bounds.Height / 2, trackPaint);

        // Frame
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = on ? FUIColors.Active : FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRoundRect(bounds, bounds.Height / 2, bounds.Height / 2, framePaint);

        // Knob
        float knobRadius = bounds.Height / 2 - 3;
        float knobX = on ? bounds.Right - knobRadius - 3 : bounds.Left + knobRadius + 3;
        using var knobPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.TextBright };
        canvas.DrawCircle(knobX, bounds.MidY, knobRadius, knobPaint);
    }

    private void DrawSettingsSlider(SKCanvas canvas, SKRect bounds, int value, int maxValue)
    {
        float trackHeight = 4f;
        float trackY = bounds.MidY - trackHeight / 2;
        var trackRect = new SKRect(bounds.Left, trackY, bounds.Right, trackY + trackHeight);

        // Track background
        using var trackBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        canvas.DrawRoundRect(trackRect, 2, 2, trackBgPaint);

        // Track frame
        using var trackFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
        canvas.DrawRoundRect(trackRect, 2, 2, trackFramePaint);

        // Filled portion
        float fillWidth = (bounds.Width - 6) * (value / (float)maxValue);
        if (fillWidth > 0)
        {
            var fillRect = new SKRect(bounds.Left + 2, trackY + 1, bounds.Left + 2 + fillWidth, trackY + trackHeight - 1);
            using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(180) };
            canvas.DrawRoundRect(fillRect, 1, 1, fillPaint);
        }

        // Knob
        float knobX = bounds.Left + 3 + (bounds.Width - 6) * (value / (float)maxValue);
        float knobRadius = 6f;
        using var knobPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.TextBright, IsAntialias = true };
        canvas.DrawCircle(knobX, bounds.MidY, knobRadius, knobPaint);

        using var knobFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active, StrokeWidth = 1f, IsAntialias = true };
        canvas.DrawCircle(knobX, bounds.MidY, knobRadius, knobFramePaint);
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

        float y = bounds.Top + frameInset + 15;
        float leftMargin = bounds.Left + frameInset + 15;
        float rightMargin = bounds.Right - frameInset - 15;

        // Title
        string outputName = GetEditingOutputName();
        FUIRenderer.DrawText(canvas, $"EDIT: {outputName}", new SKPoint(leftMargin, y),
            FUIColors.Active, 14f, true);
        y += 30;

        // INPUT SOURCE section
        FUIRenderer.DrawText(canvas, "INPUT SOURCE", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += 20;

        // Input field - double-click to listen for input
        float inputFieldHeight = 36f;
        _inputFieldBounds = new SKRect(leftMargin, y, rightMargin, y + inputFieldHeight);
        DrawInputField(canvas, _inputFieldBounds);
        y += inputFieldHeight + 10;

        // Manual entry toggle button
        _manualEntryButtonBounds = new SKRect(leftMargin, y, leftMargin + 120, y + 24);
        DrawToggleButton(canvas, _manualEntryButtonBounds, "Manual Entry", _manualEntryMode, _manualEntryButtonHovered);
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
            FUIRenderer.DrawText(canvas, "OUTPUT TYPE", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
            y += 20;
            DrawOutputTypeSelector(canvas, leftMargin, y, rightMargin - leftMargin);
            y += 38;

            // Key capture field (only when Keyboard is selected)
            if (_outputTypeIsKeyboard)
            {
                FUIRenderer.DrawText(canvas, "KEY", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
                y += 20;
                float keyFieldHeight = 32f;
                _keyCaptureBounds = new SKRect(leftMargin, y, rightMargin, y + keyFieldHeight);
                DrawKeyCapture(canvas, _keyCaptureBounds);
                y += keyFieldHeight + 10;
            }

            // Button mode selector
            y += 10;
            FUIRenderer.DrawText(canvas, "BUTTON MODE", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
            y += 20;
            DrawButtonModeSelector(canvas, leftMargin, y, rightMargin - leftMargin);
            y += 40;
        }

        // Action buttons at bottom
        float buttonWidth = 80f;
        float buttonHeight = 32f;
        float buttonY = bounds.Bottom - frameInset - buttonHeight - 15;

        _cancelButtonBounds = new SKRect(rightMargin - buttonWidth * 2 - 10, buttonY,
            rightMargin - buttonWidth - 10, buttonY + buttonHeight);
        _saveButtonBounds = new SKRect(rightMargin - buttonWidth, buttonY,
            rightMargin, buttonY + buttonHeight);

        DrawActionButton(canvas, _cancelButtonBounds, "Cancel", _cancelButtonHovered, false);
        DrawActionButton(canvas, _saveButtonBounds, "Save", _saveButtonHovered, true);
    }

    private string GetEditingOutputName()
    {
        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count)
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
            byte alpha = (byte)(180 + MathF.Sin(_pulsePhase * 3) * 60);
            FUIRenderer.DrawText(canvas, "Press a button or move an axis...",
                new SKPoint(bounds.Left + 10, textY), FUIColors.Warning.WithAlpha(alpha), 12f);
        }
        else if (_pendingInput != null)
        {
            FUIRenderer.DrawText(canvas, _pendingInput.ToString(),
                new SKPoint(bounds.Left + 10, textY), FUIColors.TextBright, 12f);
        }
        else
        {
            FUIRenderer.DrawText(canvas, "Double-click to detect input",
                new SKPoint(bounds.Left + 10, textY), FUIColors.TextDisabled, 12f);
        }

        // Clear button if there's input
        if (_pendingInput != null && !_isListeningForInput)
        {
            var clearBounds = new SKRect(bounds.Right - 28, bounds.Top + 6, bounds.Right - 6, bounds.Bottom - 6);
            DrawSmallIconButton(canvas, clearBounds, "×", false, true);
        }
    }

    private void DrawToggleButton(SKCanvas canvas, SKRect bounds, string text, bool active, bool hovered)
    {
        var bgColor = active
            ? FUIColors.Active.WithAlpha(60)
            : (hovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);
        var textColor = active ? FUIColors.Active : (hovered ? FUIColors.TextPrimary : FUIColors.TextDim);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = active ? FUIColors.Active : FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 11f);
    }

    private float DrawManualEntrySection(SKCanvas canvas, SKRect bounds, float y, float leftMargin, float rightMargin)
    {
        // Device dropdown
        FUIRenderer.DrawText(canvas, "Device:", new SKPoint(leftMargin, y + 12), FUIColors.TextDim, 10f);
        float dropdownX = leftMargin + 55;
        _deviceDropdownBounds = new SKRect(dropdownX, y, rightMargin, y + 28);
        string deviceText = _devices.Count > 0 && _selectedSourceDevice < _devices.Count
            ? _devices[_selectedSourceDevice].Name
            : "No devices";
        DrawDropdown(canvas, _deviceDropdownBounds, deviceText, _deviceDropdownOpen);
        y += 36;

        // Control dropdown
        string controlLabel = _isEditingAxis ? "Axis:" : "Button:";
        FUIRenderer.DrawText(canvas, controlLabel, new SKPoint(leftMargin, y + 12), FUIColors.TextDim, 10f);
        _controlDropdownBounds = new SKRect(dropdownX, y, rightMargin, y + 28);
        string controlText = GetControlDropdownText();
        DrawDropdown(canvas, _controlDropdownBounds, controlText, _controlDropdownOpen);
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
        if (_devices.Count == 0 || _selectedSourceDevice >= _devices.Count)
            return "—";

        var device = _devices[_selectedSourceDevice];
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
        return "—";
    }

    private void DrawDropdown(SKCanvas canvas, SKRect bounds, string text, bool open)
    {
        var bgColor = open ? FUIColors.Primary.WithAlpha(40) : FUIColors.Background2;
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = open ? FUIColors.Primary : FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        FUIRenderer.DrawText(canvas, text, new SKPoint(bounds.Left + 8, bounds.MidY + 4),
            FUIColors.TextPrimary, 11f);

        // Arrow indicator
        string arrow = open ? "▲" : "▼";
        FUIRenderer.DrawText(canvas, arrow, new SKPoint(bounds.Right - 18, bounds.MidY + 4),
            FUIColors.TextDim, 10f);
    }

    private void DrawDeviceDropdownList(SKCanvas canvas, SKRect anchorBounds)
    {
        float itemHeight = 26f;
        float listHeight = Math.Min(_devices.Count * itemHeight, 200);
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
        for (int i = 0; i < _devices.Count && y < listBounds.Bottom; i++)
        {
            var itemBounds = new SKRect(listBounds.Left, y, listBounds.Right, y + itemHeight);
            bool hovered = i == _hoveredDeviceIndex;

            if (hovered)
            {
                using var hoverPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Primary.WithAlpha(60) };
                canvas.DrawRect(itemBounds, hoverPaint);
            }

            FUIRenderer.DrawText(canvas, _devices[i].Name, new SKPoint(itemBounds.Left + 8, itemBounds.MidY + 4),
                hovered ? FUIColors.TextBright : FUIColors.TextPrimary, 11f);
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
                hovered ? FUIColors.TextBright : FUIColors.TextPrimary, 11f);
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

    private void DrawButtonModeSelector(SKCanvas canvas, float x, float y, float width)
    {
        ButtonMode[] modes = { ButtonMode.Normal, ButtonMode.Toggle, ButtonMode.Pulse, ButtonMode.HoldToActivate };
        string[] labels = { "Normal", "Toggle", "Pulse", "Hold" };
        float buttonWidth = (width - 15) / 4;
        float buttonHeight = 28f;

        for (int i = 0; i < modes.Length; i++)
        {
            var modeBounds = new SKRect(x + i * (buttonWidth + 5), y, x + i * (buttonWidth + 5) + buttonWidth, y + buttonHeight);
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

            FUIRenderer.DrawTextCentered(canvas, labels[i], modeBounds, textColor, 10f);
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

            FUIRenderer.DrawTextCentered(canvas, labels[i], typeBounds, textColor, 11f);
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
            byte alpha = (byte)(180 + MathF.Sin(_pulsePhase * 3) * 60);
            FUIRenderer.DrawText(canvas, "Press a key...",
                new SKPoint(bounds.Left + 10, textY), FUIColors.Warning.WithAlpha(alpha), 12f);
        }
        else if (!string.IsNullOrEmpty(_selectedKeyName))
        {
            FUIRenderer.DrawText(canvas, _selectedKeyName,
                new SKPoint(bounds.Left + 10, textY), FUIColors.TextBright, 12f);
        }
        else
        {
            FUIRenderer.DrawText(canvas, "Click to capture key",
                new SKPoint(bounds.Left + 10, textY), FUIColors.TextDisabled, 12f);
        }

        // Clear button if there's a key
        if (!string.IsNullOrEmpty(_selectedKeyName) && !_isCapturingKey)
        {
            var clearBounds = new SKRect(bounds.Right - 28, bounds.Top + 6, bounds.Right - 6, bounds.Bottom - 6);
            DrawSmallIconButton(canvas, clearBounds, "×", false, true);
        }
    }

    private void DrawActionButton(SKCanvas canvas, SKRect bounds, string text, bool hovered, bool isPrimary)
    {
        var bgColor = isPrimary
            ? (hovered ? FUIColors.Active : FUIColors.Active.WithAlpha(180))
            : (hovered ? FUIColors.Primary.WithAlpha(60) : FUIColors.Background2);
        var textColor = isPrimary
            ? FUIColors.Background1
            : (hovered ? FUIColors.TextBright : FUIColors.TextPrimary);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = isPrimary ? FUIColors.Active : FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 12f);
    }

    private void DrawArrowButton(SKCanvas canvas, SKRect bounds, string arrow, bool hovered, bool enabled)
    {
        var bgColor = enabled
            ? (hovered ? FUIColors.Primary.WithAlpha(80) : FUIColors.Background2)
            : FUIColors.Background1;
        var arrowColor = enabled
            ? (hovered ? FUIColors.TextBright : FUIColors.TextPrimary)
            : FUIColors.TextDisabled;

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = enabled ? FUIColors.Frame : FUIColors.FrameDim,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        // Draw arrow shape instead of text (more reliable rendering)
        float centerX = bounds.MidX;
        float centerY = bounds.MidY;
        float arrowSize = 8f;

        using var arrowPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = arrowColor,
            IsAntialias = true
        };

        using var path = new SKPath();
        if (arrow == "<")
        {
            // Left arrow: <
            path.MoveTo(centerX + arrowSize / 2, centerY - arrowSize);
            path.LineTo(centerX - arrowSize / 2, centerY);
            path.LineTo(centerX + arrowSize / 2, centerY + arrowSize);
            path.Close();
        }
        else
        {
            // Right arrow: >
            path.MoveTo(centerX - arrowSize / 2, centerY - arrowSize);
            path.LineTo(centerX + arrowSize / 2, centerY);
            path.LineTo(centerX - arrowSize / 2, centerY + arrowSize);
            path.Close();
        }
        canvas.DrawPath(path, arrowPaint);
    }

    private void DrawOutputMappingList(SKCanvas canvas, SKRect bounds)
    {
        _mappingRowBounds.Clear();
        _mappingAddButtonBounds.Clear();
        _mappingRemoveButtonBounds.Clear();

        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count)
        {
            FUIRenderer.DrawText(canvas, "No vJoy devices available",
                new SKPoint(bounds.Left + 20, bounds.Top + 20), FUIColors.TextDim, 12f);
            FUIRenderer.DrawText(canvas, "Install vJoy driver to create mappings",
                new SKPoint(bounds.Left + 20, bounds.Top + 40), FUIColors.TextDisabled, 11f);
            return;
        }

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        var profile = _profileService.ActiveProfile;

        float rowHeight = 32f;
        float rowGap = 4f;
        float y = bounds.Top;
        int rowIndex = 0;

        // Section: AXES
        FUIRenderer.DrawText(canvas, "AXES", new SKPoint(bounds.Left + 5, y + 14), FUIColors.Active, 11f);
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

            DrawMappingRow(canvas, rowBounds, axisNames[i], binding, isSelected, isHovered, isEditing, rowIndex, !string.IsNullOrEmpty(binding) && binding != "—");

            _mappingRowBounds.Add(rowBounds);
            y += rowHeight + rowGap;
            rowIndex++;
        }

        // Section: BUTTONS
        y += 10;
        if (y + 20 < bounds.Bottom)
        {
            FUIRenderer.DrawText(canvas, "BUTTONS", new SKPoint(bounds.Left + 5, y + 14), FUIColors.Active, 11f);
            y += 20;
        }

        for (int i = 0; i < vjoyDevice.ButtonCount && y + rowHeight <= bounds.Bottom; i++)
        {
            var rowBounds = new SKRect(bounds.Left, y, bounds.Right, y + rowHeight);
            string binding = GetButtonBindingText(profile, vjoyDevice.Id, i);
            bool isSelected = rowIndex == _selectedMappingRow;
            bool isHovered = rowIndex == _hoveredMappingRow;
            bool isEditing = _mappingEditorOpen && rowIndex == _editingRowIndex;

            DrawMappingRow(canvas, rowBounds, $"Button {i + 1}", binding, isSelected, isHovered, isEditing, rowIndex, !string.IsNullOrEmpty(binding) && binding != "—");

            _mappingRowBounds.Add(rowBounds);
            y += rowHeight + rowGap;
            rowIndex++;
        }
    }

    private string GetAxisBindingText(MappingProfile? profile, uint vjoyId, int axisIndex)
    {
        if (profile == null) return "—";

        var mapping = profile.AxisMappings.FirstOrDefault(m =>
            m.Output.Type == OutputType.VJoyAxis &&
            m.Output.VJoyDevice == vjoyId &&
            m.Output.Index == axisIndex);

        if (mapping == null || mapping.Inputs.Count == 0) return "—";

        var input = mapping.Inputs[0];
        return $"{input.DeviceName} - Axis {input.Index}";
    }

    private string GetButtonBindingText(MappingProfile? profile, uint vjoyId, int buttonIndex)
    {
        if (profile == null) return "—";

        // Find mapping for this button slot (either VJoyButton or Keyboard output type)
        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyId &&
            m.Output.Index == buttonIndex);

        if (mapping == null || mapping.Inputs.Count == 0) return "—";

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
            isEditing ? FUIColors.Active : FUIColors.TextPrimary, 12f);

        // Binding (center)
        float bindingX = bounds.Left + 100;
        var bindColor = binding == "—" ? FUIColors.TextDisabled : FUIColors.TextDim;
        FUIRenderer.DrawText(canvas, binding, new SKPoint(bindingX, textY), bindColor, 11f);

        // [+] button (Edit/Add)
        float buttonSize = 24f;
        float buttonY = bounds.MidY - buttonSize / 2;
        float addButtonX = bounds.Right - (hasBind ? 60 : 35);
        var addBounds = new SKRect(addButtonX, buttonY, addButtonX + buttonSize, buttonY + buttonSize);
        _mappingAddButtonBounds.Add(addBounds);

        bool addHovered = rowIndex == _hoveredAddButton;
        string addIcon = hasBind ? "✎" : "+";  // Pencil for edit, plus for add
        DrawSmallIconButton(canvas, addBounds, addIcon, addHovered);

        // [×] button (only if bound)
        if (hasBind)
        {
            float removeButtonX = bounds.Right - 32;
            var removeBounds = new SKRect(removeButtonX, buttonY, removeButtonX + buttonSize, buttonY + buttonSize);
            _mappingRemoveButtonBounds.Add(removeBounds);

            bool removeHovered = rowIndex == _hoveredRemoveButton;
            DrawSmallIconButton(canvas, removeBounds, "×", removeHovered, true);
        }
        else
        {
            _mappingRemoveButtonBounds.Add(SKRect.Empty);
        }
    }

    private void DrawSmallIconButton(SKCanvas canvas, SKRect bounds, string icon, bool hovered, bool isDanger = false)
    {
        var bgColor = hovered
            ? (isDanger ? FUIColors.Warning.WithAlpha(60) : FUIColors.Active.WithAlpha(60))
            : FUIColors.Background2.WithAlpha(100);
        var textColor = hovered
            ? (isDanger ? FUIColors.Warning : FUIColors.Active)
            : FUIColors.TextDim;

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = hovered ? (isDanger ? FUIColors.Warning : FUIColors.Active) : FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        FUIRenderer.DrawTextCentered(canvas, icon, bounds, textColor, 14f);
    }

    private void OpenMappingEditor(int rowIndex)
    {
        if (!_profileService.HasActiveProfile)
        {
            CreateNewProfilePrompt();
            if (!_profileService.HasActiveProfile) return;
        }
        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count) return;

        // Cancel any existing listening
        CancelInputListening();

        _mappingEditorOpen = true;
        _editingRowIndex = rowIndex;
        _selectedMappingRow = rowIndex;
        _isEditingAxis = rowIndex < 8;
        _pendingInput = null;
        _manualEntryMode = false;
        _selectedButtonMode = ButtonMode.Normal;
        _selectedSourceDevice = 0;
        _selectedSourceControl = 0;

        // Load existing binding if present
        LoadExistingBinding(rowIndex);
    }

    private void LoadExistingBinding(int rowIndex)
    {
        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        bool isAxis = rowIndex < 8;
        int outputIndex = isAxis ? rowIndex : rowIndex - 8;

        if (isAxis)
        {
            var mapping = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (mapping != null && mapping.Inputs.Count > 0)
            {
                var input = mapping.Inputs[0];
                _pendingInput = new DetectedInput
                {
                    DeviceGuid = Guid.TryParse(input.DeviceId, out var guid) ? guid : Guid.Empty,
                    DeviceName = input.DeviceName,
                    Type = input.Type,
                    Index = input.Index,
                    Value = 0
                };

                // Set selected device in dropdown
                for (int i = 0; i < _devices.Count; i++)
                {
                    if (_devices[i].InstanceGuid.ToString() == input.DeviceId)
                    {
                        _selectedSourceDevice = i;
                        break;
                    }
                }
                _selectedSourceControl = input.Index;
            }
        }
        else
        {
            var mapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyButton &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (mapping != null && mapping.Inputs.Count > 0)
            {
                var input = mapping.Inputs[0];
                _pendingInput = new DetectedInput
                {
                    DeviceGuid = Guid.TryParse(input.DeviceId, out var guid) ? guid : Guid.Empty,
                    DeviceName = input.DeviceName,
                    Type = input.Type,
                    Index = input.Index,
                    Value = 0
                };
                _selectedButtonMode = mapping.Mode;

                // Set selected device in dropdown
                for (int i = 0; i < _devices.Count; i++)
                {
                    if (_devices[i].InstanceGuid.ToString() == input.DeviceId)
                    {
                        _selectedSourceDevice = i;
                        break;
                    }
                }
                _selectedSourceControl = input.Index;
            }
        }
    }

    private void CloseMappingEditor()
    {
        CancelInputListening();
        _mappingEditorOpen = false;
        _editingRowIndex = -1;
        _pendingInput = null;
        _deviceDropdownOpen = false;
        _controlDropdownOpen = false;
    }

    private async void StartListeningForInput()
    {
        if (_isListeningForInput) return;
        if (!_mappingEditorOpen) return;

        _isListeningForInput = true;
        _pendingInput = null;

        // Determine input type based on what we're editing
        var filter = _isEditingAxis ? InputDetectionFilter.Axes : InputDetectionFilter.Buttons;

        _inputDetectionService ??= new InputDetectionService(_inputService);

        try
        {
            // Wait for actual input change - use a delay to skip initial state
            await Task.Delay(200); // Small delay to let user release any currently pressed buttons

            var detected = await _inputDetectionService.WaitForInputAsync(filter, 0.5f, 15000);

            if (detected != null && _mappingEditorOpen)
            {
                _pendingInput = detected;

                // Update manual entry dropdowns to match detected input
                for (int i = 0; i < _devices.Count; i++)
                {
                    if (_devices[i].InstanceGuid == detected.DeviceGuid)
                    {
                        _selectedSourceDevice = i;
                        break;
                    }
                }
                _selectedSourceControl = detected.Index;
            }
        }
        catch (Exception)
        {
            // Cancelled or error
        }
        finally
        {
            _isListeningForInput = false;
        }
    }

    private void SaveMapping()
    {
        if (!_mappingEditorOpen || _pendingInput == null) return;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        int outputIndex = _isEditingAxis ? _editingRowIndex : _editingRowIndex - 8;

        // Remove existing binding
        RemoveBindingAtRow(_editingRowIndex, save: false);

        if (_isEditingAxis)
        {
            var mapping = new AxisMapping
            {
                Name = $"{_pendingInput.DeviceName} Axis {_pendingInput.Index} -> vJoy {vjoyDevice.Id} Axis {outputIndex}",
                Inputs = new List<InputSource> { _pendingInput.ToInputSource() },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyAxis,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                },
                Curve = new AxisCurve()
            };
            profile.AxisMappings.Add(mapping);
        }
        else
        {
            var mapping = new ButtonMapping
            {
                Name = $"{_pendingInput.DeviceName} Button {_pendingInput.Index + 1} -> vJoy {vjoyDevice.Id} Button {outputIndex + 1}",
                Inputs = new List<InputSource> { _pendingInput.ToInputSource() },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyButton,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                },
                Mode = _selectedButtonMode
            };
            profile.ButtonMappings.Add(mapping);
        }

        _profileService.SaveActiveProfile();
        CloseMappingEditor();
    }

    private void CreateBindingFromManualEntry()
    {
        if (!_manualEntryMode || _devices.Count == 0 || _selectedSourceDevice >= _devices.Count) return;

        var device = _devices[_selectedSourceDevice];
        _pendingInput = new DetectedInput
        {
            DeviceGuid = device.InstanceGuid,
            DeviceName = device.Name,
            Type = _isEditingAxis ? InputType.Axis : InputType.Button,
            Index = _selectedSourceControl,
            Value = 0
        };
    }

    /// <summary>
    /// Create 1:1 mappings from the selected physical device to a user-selected vJoy device.
    /// Maps all axes, buttons, and hats directly without any curves or modifications.
    /// </summary>
    private void CreateOneToOneMappings()
    {
        // Validate selection
        if (_selectedDevice < 0 || _selectedDevice >= _devices.Count) return;

        var physicalDevice = _devices[_selectedDevice];
        if (physicalDevice.IsVirtual) return; // Only map physical devices

        // Ensure vJoy devices are loaded
        if (_vjoyDevices.Count == 0)
        {
            _vjoyDevices = _vjoyService.EnumerateDevices();
        }

        // Check if we have any vJoy devices
        if (_vjoyDevices.Count == 0)
        {
            ShowVJoyConfigurationHelp(physicalDevice, noDevices: true);
            return;
        }

        // Show vJoy device selection dialog
        var vjoyDevice = ShowVJoyDeviceSelectionDialog(physicalDevice);
        if (vjoyDevice == null) return; // User cancelled

        // Update selected vJoy device index
        _selectedVJoyDeviceIndex = _vjoyDevices.IndexOf(vjoyDevice);

        // Ensure we have an active profile
        var profile = _profileService.ActiveProfile;
        if (profile == null)
        {
            profile = _profileService.CreateAndActivateProfile($"1:1 - {physicalDevice.Name}");
        }

        // Build device ID for InputSource (using GUID)
        string deviceId = physicalDevice.InstanceGuid.ToString();

        // Remove any existing mappings from this device to this vJoy device
        profile.AxisMappings.RemoveAll(m =>
            m.Inputs.Any(i => i.DeviceId == deviceId) &&
            m.Output.VJoyDevice == vjoyDevice.Id);
        profile.ButtonMappings.RemoveAll(m =>
            m.Inputs.Any(i => i.DeviceId == deviceId) &&
            m.Output.VJoyDevice == vjoyDevice.Id);
        profile.HatMappings.RemoveAll(m =>
            m.Inputs.Any(i => i.DeviceId == deviceId) &&
            m.Output.VJoyDevice == vjoyDevice.Id);

        // Create axis mappings using actual axis types when available
        // Maps each physical axis to the corresponding vJoy axis based on type (X->X, Slider->Slider, etc.)
        var vjoyAxisIndices = GetVJoyAxisIndices(vjoyDevice);
        var usedVJoyAxes = new HashSet<int>();
        var sliderCount = 0; // Track how many sliders we've mapped

        LogMapping($"=== 1:1 Mapping for {physicalDevice.Name} ===");
        LogMapping($"Device: {physicalDevice.Name}, AxisCount: {physicalDevice.AxisCount}, AxisInfos.Count: {physicalDevice.AxisInfos.Count}");
        LogMapping($"HidDevicePath: {physicalDevice.HidDevicePath}");
        foreach (var ai in physicalDevice.AxisInfos)
        {
            LogMapping($"  AxisInfo: Index={ai.Index}, Type={ai.Type}, vJoyIndex={ai.ToVJoyAxisIndex()}");
        }

        for (int i = 0; i < physicalDevice.AxisCount; i++)
        {
            // Determine the target vJoy axis index based on axis type
            int vjoyAxisIndex;
            var axisInfo = physicalDevice.AxisInfos.FirstOrDefault(a => a.Index == i);

            LogMapping($"Processing axis {i}: axisInfo={axisInfo?.Type.ToString() ?? "NULL"}");

            if (axisInfo != null && axisInfo.Type != AxisType.Unknown)
            {
                // Use actual axis type to determine vJoy target
                vjoyAxisIndex = axisInfo.ToVJoyAxisIndex();

                // Handle multiple sliders (Slider0 = 6, Slider1 = 7)
                if (axisInfo.Type == AxisType.Slider)
                {
                    vjoyAxisIndex = 6 + sliderCount; // Map to Slider0 first, then Slider1
                    sliderCount++;
                }
            }
            else
            {
                // Fallback: use sequential mapping if axis type is unknown
                vjoyAxisIndex = i < vjoyAxisIndices.Count ? vjoyAxisIndices[i] : -1;
            }

            // Skip if the vJoy axis isn't available or already used
            if (vjoyAxisIndex < 0 || !vjoyAxisIndices.Contains(vjoyAxisIndex) || usedVJoyAxes.Contains(vjoyAxisIndex))
            {
                // Try to find an unused vJoy axis as fallback
                vjoyAxisIndex = vjoyAxisIndices.FirstOrDefault(idx => !usedVJoyAxes.Contains(idx), -1);
                if (vjoyAxisIndex < 0) continue; // No more vJoy axes available
            }

            usedVJoyAxes.Add(vjoyAxisIndex);
            string vjoyAxisName = GetVJoyAxisName(vjoyAxisIndex);
            string physicalAxisName = axisInfo?.TypeName ?? $"Axis {i}";

            var mapping = new AxisMapping
            {
                Name = $"{physicalDevice.Name} {physicalAxisName} -> vJoy {vjoyDevice.Id} {vjoyAxisName}",
                Inputs = new List<InputSource>
                {
                    new InputSource
                    {
                        DeviceId = deviceId,
                        DeviceName = physicalDevice.Name,
                        Type = InputType.Axis,
                        Index = i
                    }
                },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyAxis,
                    VJoyDevice = vjoyDevice.Id,
                    Index = vjoyAxisIndex
                },
                Curve = new AxisCurve { Type = CurveType.Linear }
            };
            profile.AxisMappings.Add(mapping);
        }

        // Create button mappings
        int buttonsToMap = Math.Min(physicalDevice.ButtonCount, vjoyDevice.ButtonCount);

        for (int i = 0; i < buttonsToMap; i++)
        {
            var mapping = new ButtonMapping
            {
                Name = $"{physicalDevice.Name} Btn {i + 1} -> vJoy {vjoyDevice.Id} Btn {i + 1}",
                Inputs = new List<InputSource>
                {
                    new InputSource
                    {
                        DeviceId = deviceId,
                        DeviceName = physicalDevice.Name,
                        Type = InputType.Button,
                        Index = i
                    }
                },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyButton,
                    VJoyDevice = vjoyDevice.Id,
                    Index = i
                },
                Mode = ButtonMode.Normal
            };
            profile.ButtonMappings.Add(mapping);
        }

        // Create hat/POV mappings
        int hatsToMap = Math.Min(physicalDevice.HatCount, vjoyDevice.ContPovCount + vjoyDevice.DiscPovCount);

        for (int i = 0; i < hatsToMap; i++)
        {
            var mapping = new HatMapping
            {
                Name = $"{physicalDevice.Name} Hat {i} -> vJoy {vjoyDevice.Id} POV {i}",
                Inputs = new List<InputSource>
                {
                    new InputSource
                    {
                        DeviceId = deviceId,
                        DeviceName = physicalDevice.Name,
                        Type = InputType.Hat,
                        Index = i
                    }
                },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyPov,
                    VJoyDevice = vjoyDevice.Id,
                    Index = i
                },
                UseContinuous = vjoyDevice.ContPovCount > i
            };
            profile.HatMappings.Add(mapping);
        }

        // Save the profile
        _profileService.SaveActiveProfile();

        // Refresh profiles list
        _profiles = _profileService.ListProfiles();

        // Switch to Mappings tab to show the new mappings
        _activeTab = 1;
        _canvas.Invalidate();
    }

    /// <summary>
    /// Count the number of axes configured on a vJoy device
    /// </summary>
    private int CountVJoyAxes(VJoyDeviceInfo vjoy)
    {
        int count = 0;
        if (vjoy.HasAxisX) count++;
        if (vjoy.HasAxisY) count++;
        if (vjoy.HasAxisZ) count++;
        if (vjoy.HasAxisRX) count++;
        if (vjoy.HasAxisRY) count++;
        if (vjoy.HasAxisRZ) count++;
        if (vjoy.HasSlider0) count++;
        if (vjoy.HasSlider1) count++;
        return count;
    }

    /// <summary>
    /// Get the list of available vJoy axis indices in standard order.
    /// Returns indices 0-7 corresponding to X, Y, Z, RX, RY, RZ, Slider0, Slider1.
    /// </summary>
    private List<int> GetVJoyAxisIndices(VJoyDeviceInfo vjoy)
    {
        var indices = new List<int>();
        if (vjoy.HasAxisX) indices.Add(0);   // X
        if (vjoy.HasAxisY) indices.Add(1);   // Y
        if (vjoy.HasAxisZ) indices.Add(2);   // Z
        if (vjoy.HasAxisRX) indices.Add(3);  // RX
        if (vjoy.HasAxisRY) indices.Add(4);  // RY
        if (vjoy.HasAxisRZ) indices.Add(5);  // RZ
        if (vjoy.HasSlider0) indices.Add(6); // Slider0
        if (vjoy.HasSlider1) indices.Add(7); // Slider1
        return indices;
    }

    /// <summary>
    /// Get a human-readable name for a vJoy axis index.
    /// </summary>
    private string GetVJoyAxisName(int index)
    {
        return index switch
        {
            0 => "X",
            1 => "Y",
            2 => "Z",
            3 => "RX",
            4 => "RY",
            5 => "RZ",
            6 => "Slider1",
            7 => "Slider2",
            _ => $"Axis{index}"
        };
    }

    /// <summary>
    /// Find the best vJoy device that can accommodate all controls from the physical device
    /// </summary>
    private VJoyDeviceInfo? FindBestVJoyDevice(PhysicalDeviceInfo physical)
    {
        VJoyDeviceInfo? best = null;
        int bestScore = -1;

        foreach (var vjoy in _vjoyDevices)
        {
            int axes = CountVJoyAxes(vjoy);
            int buttons = vjoy.ButtonCount;
            int povs = vjoy.ContPovCount + vjoy.DiscPovCount;

            // Check if this vJoy can accommodate all controls
            if (axes >= physical.AxisCount &&
                buttons >= physical.ButtonCount &&
                povs >= physical.HatCount)
            {
                // Score based on how close the match is (lower excess = better)
                int excess = (axes - physical.AxisCount) +
                            (buttons - physical.ButtonCount) +
                            (povs - physical.HatCount);
                int score = 1000 - excess; // Higher score = better match

                if (score > bestScore)
                {
                    bestScore = score;
                    best = vjoy;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Show help dialog for vJoy configuration with recommended settings
    /// </summary>
    private void ShowVJoyConfigurationHelp(PhysicalDeviceInfo physical, bool noDevices)
    {
        string message;
        if (noDevices)
        {
            message = "No vJoy devices are configured.\n\n";
        }
        else
        {
            message = "No vJoy device has enough capacity for this physical device.\n\n";
        }

        message += $"To create a 1:1 mapping for {physical.Name}, configure a vJoy device with:\n\n" +
                   $"  Axes: {physical.AxisCount} (X, Y, Z, Rx, Ry, Rz, Slider, Dial)\n" +
                   $"  Buttons: {physical.ButtonCount}\n" +
                   $"  POV Hats: {physical.HatCount} (Continuous recommended)\n\n" +
                   "Would you like to open the vJoy Configuration utility?";

        var result = FUIMessageBox.ShowQuestion(this, message, "vJoy Configuration Required");

        if (result)
        {
            LaunchVJoyConfigurator();
        }
    }

    /// <summary>
    /// Attempt to launch the vJoy configuration utility
    /// </summary>
    private void LaunchVJoyConfigurator()
    {
        // Common vJoy installation paths
        string[] possiblePaths = new[]
        {
            @"C:\Program Files\vJoy\x64\vJoyConf.exe",
            @"C:\Program Files\vJoy\x86\vJoyConf.exe",
            @"C:\Program Files (x86)\vJoy\x64\vJoyConf.exe",
            @"C:\Program Files (x86)\vJoy\x86\vJoyConf.exe"
        };

        string? vjoyConfPath = possiblePaths.FirstOrDefault(File.Exists);

        if (vjoyConfPath != null)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = vjoyConfPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                FUIMessageBox.ShowError(this,
                    $"Failed to launch vJoy Configurator:\n{ex.Message}",
                    "Launch Failed");
            }
        }
        else
        {
            FUIMessageBox.ShowWarning(this,
                "vJoy Configuration utility (vJoyConf.exe) was not found.\n\n" +
                "Please install vJoy from:\nhttps://github.com/jshafer817/vJoy/releases\n\n" +
                "Or manually run vJoyConf.exe from your vJoy installation folder.",
                "vJoy Not Found");
        }
    }

    /// <summary>
    /// Show a dialog to select a vJoy device for 1:1 mapping.
    /// Returns the selected device or null if cancelled.
    /// </summary>
    private VJoyDeviceInfo? ShowVJoyDeviceSelectionDialog(PhysicalDeviceInfo physicalDevice)
    {
        var items = new List<FUISelectionDialog.SelectionItem>();

        // Add vJoy devices to list
        foreach (var vjoy in _vjoyDevices)
        {
            int axes = CountVJoyAxes(vjoy);
            int buttons = vjoy.ButtonCount;
            int povs = vjoy.ContPovCount + vjoy.DiscPovCount;

            string status;
            if (axes >= physicalDevice.AxisCount &&
                buttons >= physicalDevice.ButtonCount &&
                povs >= physicalDevice.HatCount)
            {
                status = "[OK]";
            }
            else
            {
                status = "[partial]";
            }

            items.Add(new FUISelectionDialog.SelectionItem
            {
                Text = $"vJoy #{vjoy.Id}: {axes} axes, {buttons} buttons, {povs} POVs",
                Status = status,
                Tag = vjoy
            });
        }

        // Add option to configure new vJoy device
        items.Add(new FUISelectionDialog.SelectionItem
        {
            Text = "+ Configure new vJoy device...",
            IsAction = true
        });

        string description = $"Select a vJoy device to map {physicalDevice.Name}:\n" +
                           $"({physicalDevice.AxisCount} axes, {physicalDevice.ButtonCount} buttons, {physicalDevice.HatCount} hats)";

        int selectedIndex = FUISelectionDialog.Show(this, "Select vJoy Device", description, items, "Map 1:1", "Cancel");

        if (selectedIndex < 0)
            return null;

        // Check if user selected "Configure new vJoy device"
        if (selectedIndex == _vjoyDevices.Count)
        {
            ShowVJoyConfigurationHelp(physicalDevice, noDevices: false);
            return null;
        }

        if (selectedIndex >= 0 && selectedIndex < _vjoyDevices.Count)
        {
            var selectedVJoy = _vjoyDevices[selectedIndex];

            // Warn about partial mappings if necessary
            int axes = CountVJoyAxes(selectedVJoy);
            int buttons = selectedVJoy.ButtonCount;
            int povs = selectedVJoy.ContPovCount + selectedVJoy.DiscPovCount;

            if (axes < physicalDevice.AxisCount ||
                buttons < physicalDevice.ButtonCount ||
                povs < physicalDevice.HatCount)
            {
                var result = FUIMessageBox.ShowQuestion(this,
                    $"vJoy #{selectedVJoy.Id} doesn't have enough capacity.\n\n" +
                    $"Physical device: {physicalDevice.AxisCount} axes, {physicalDevice.ButtonCount} buttons, {physicalDevice.HatCount} hats\n" +
                    $"vJoy #{selectedVJoy.Id}: {axes} axes, {buttons} buttons, {povs} POVs\n\n" +
                    "Some controls will not be mapped. Continue?",
                    "Partial Mapping");

                if (!result)
                    return null;
            }

            return selectedVJoy;
        }

        return null;
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private static void LogMapping(string message)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Asteriq", "axis_types.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] [Mapping] {message}\n");
    }

    /// <summary>
    /// Clear all mappings for the selected physical device.
    /// </summary>
    private void ClearDeviceMappings()
    {
        if (_selectedDevice < 0 || _selectedDevice >= _devices.Count) return;

        var physicalDevice = _devices[_selectedDevice];
        if (physicalDevice.IsVirtual) return;

        var result = FUIMessageBox.ShowQuestion(this,
            $"Remove all mappings for {physicalDevice.Name}?\n\nThis will remove axis, button, and hat mappings from all vJoy devices.",
            "Clear Mappings");

        if (!result) return;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        string deviceId = physicalDevice.InstanceGuid.ToString();

        // Remove all mappings from this device
        int axisRemoved = profile.AxisMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));
        int buttonRemoved = profile.ButtonMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));
        int hatRemoved = profile.HatMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));

        _profileService.SaveActiveProfile();

        FUIMessageBox.ShowInfo(this,
            $"Removed {axisRemoved} axis, {buttonRemoved} button, and {hatRemoved} hat mappings.",
            "Mappings Cleared");

        _canvas.Invalidate();
    }

    /// <summary>
    /// Remove a disconnected device completely from the app's data.
    /// This clears all mappings and removes it from the disconnected devices list.
    /// </summary>
    private void RemoveDisconnectedDevice()
    {
        if (_selectedDevice < 0 || _selectedDevice >= _devices.Count) return;

        var device = _devices[_selectedDevice];
        if (device.IsConnected || device.IsVirtual) return; // Only works for disconnected physical devices

        var result = FUIMessageBox.ShowQuestion(this,
            $"Permanently remove {device.Name}?\n\n" +
            "This will:\n" +
            "• Clear all axis, button, and hat mappings\n" +
            "• Remove the device from the disconnected list\n\n" +
            "This cannot be undone.",
            "Remove Device");

        if (!result) return;

        var profile = _profileService.ActiveProfile;
        string deviceId = device.InstanceGuid.ToString();

        // Remove all mappings from this device
        int axisRemoved = 0, buttonRemoved = 0, hatRemoved = 0;
        if (profile != null)
        {
            axisRemoved = profile.AxisMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));
            buttonRemoved = profile.ButtonMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));
            hatRemoved = profile.HatMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));
            _profileService.SaveActiveProfile();
        }

        // Remove from disconnected devices list
        _disconnectedDevices.RemoveAll(d => d.InstanceGuid == device.InstanceGuid);
        SaveDisconnectedDevices();

        // Refresh and update selection
        RefreshDevices();
        if (_selectedDevice >= _devices.Count)
        {
            _selectedDevice = Math.Max(0, _devices.Count - 1);
        }

        FUIMessageBox.ShowInfo(this,
            $"Device removed.\n\nCleared {axisRemoved} axis, {buttonRemoved} button, and {hatRemoved} hat mappings.",
            "Device Removed");

        _canvas.Invalidate();
    }

    private void CreateBindingForRow(int rowIndex, DetectedInput input)
    {
        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        // Use current mapping category to determine axis vs button
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        // rowIndex is already the correct index within the current category
        int outputIndex = rowIndex;

        // Remove existing binding for this output
        RemoveBindingAtRow(rowIndex, save: false);

        if (isAxis)
        {
            var mapping = new AxisMapping
            {
                Name = $"{input.DeviceName} Axis {input.Index} -> vJoy {vjoyDevice.Id} Axis {outputIndex}",
                Inputs = new List<InputSource> { input.ToInputSource() },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyAxis,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                },
                Curve = new AxisCurve()
            };
            profile.AxisMappings.Add(mapping);
        }
        else
        {
            var mapping = new ButtonMapping
            {
                Name = $"{input.DeviceName} Button {input.Index + 1} -> vJoy {vjoyDevice.Id} Button {outputIndex + 1}",
                Inputs = new List<InputSource> { input.ToInputSource() },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyButton,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                },
                Mode = ButtonMode.Normal
            };
            profile.ButtonMappings.Add(mapping);
        }

        _profileService.SaveActiveProfile();
    }

    private void RemoveBindingAtRow(int rowIndex, bool save = true)
    {
        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        // Use current mapping category to determine axis vs button
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        // rowIndex is already the correct index within the current category
        int outputIndex = rowIndex;

        if (isAxis)
        {
            var existing = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);
            if (existing != null)
            {
                profile.AxisMappings.Remove(existing);
            }
        }
        else
        {
            var existing = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyButton &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);
            if (existing != null)
            {
                profile.ButtonMappings.Remove(existing);
            }
        }

        if (save)
        {
            _profileService.SaveActiveProfile();
        }
    }

    private void CancelInputListening()
    {
        if (_isListeningForInput)
        {
            _inputDetectionService?.Cancel();
            _isListeningForInput = false;
        }
    }

    /// <summary>
    /// Check if a physical input is already mapped anywhere in the profile.
    /// Returns the mapping name if found, null otherwise.
    /// </summary>
    private string? FindExistingMappingForInput(MappingProfile profile, InputSource inputToCheck)
    {
        // Check axis mappings
        foreach (var mapping in profile.AxisMappings)
        {
            foreach (var input in mapping.Inputs)
            {
                if (input.DeviceId == inputToCheck.DeviceId &&
                    input.Type == inputToCheck.Type &&
                    input.Index == inputToCheck.Index)
                {
                    return mapping.Name;
                }
            }
        }

        // Check button mappings
        foreach (var mapping in profile.ButtonMappings)
        {
            foreach (var input in mapping.Inputs)
            {
                if (input.DeviceId == inputToCheck.DeviceId &&
                    input.Type == inputToCheck.Type &&
                    input.Index == inputToCheck.Index)
                {
                    return mapping.Name;
                }
            }
        }

        // Check hat mappings
        foreach (var mapping in profile.HatMappings)
        {
            foreach (var input in mapping.Inputs)
            {
                if (input.DeviceId == inputToCheck.DeviceId &&
                    input.Type == inputToCheck.Type &&
                    input.Index == inputToCheck.Index)
                {
                    return mapping.Name;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Show a confirmation dialog when a duplicate mapping is detected.
    /// Returns true if the user wants to proceed and replace the existing mapping.
    /// </summary>
    private bool ConfirmDuplicateMapping(string existingMappingName, string newMappingTarget)
    {
        using var dialog = new FUIConfirmDialog(
            "Duplicate Mapping",
            $"This input is already mapped to:\n\n{existingMappingName}\n\nRemove existing and create new mapping for {newMappingTarget}?",
            "Replace",
            "Cancel");
        return dialog.ShowDialog(this) == DialogResult.Yes;
    }

    /// <summary>
    /// Remove any existing mappings that use the specified input source.
    /// </summary>
    private void RemoveExistingMappingsForInput(MappingProfile profile, InputSource inputToRemove)
    {
        // Remove from axis mappings
        foreach (var mapping in profile.AxisMappings.ToList())
        {
            mapping.Inputs.RemoveAll(i =>
                i.DeviceId == inputToRemove.DeviceId &&
                i.Type == inputToRemove.Type &&
                i.Index == inputToRemove.Index);

            // If no inputs remain, remove the mapping entirely
            if (mapping.Inputs.Count == 0)
            {
                profile.AxisMappings.Remove(mapping);
            }
        }

        // Remove from button mappings
        foreach (var mapping in profile.ButtonMappings.ToList())
        {
            mapping.Inputs.RemoveAll(i =>
                i.DeviceId == inputToRemove.DeviceId &&
                i.Type == inputToRemove.Type &&
                i.Index == inputToRemove.Index);

            if (mapping.Inputs.Count == 0)
            {
                profile.ButtonMappings.Remove(mapping);
            }
        }

        // Remove from hat mappings
        foreach (var mapping in profile.HatMappings.ToList())
        {
            mapping.Inputs.RemoveAll(i =>
                i.DeviceId == inputToRemove.DeviceId &&
                i.Type == inputToRemove.Type &&
                i.Index == inputToRemove.Index);

            if (mapping.Inputs.Count == 0)
            {
                profile.HatMappings.Remove(mapping);
            }
        }
    }

    private async void StartInputListening(int rowIndex)
    {
        if (_isListeningForInput) return;
        if (rowIndex < 0) return;

        _isListeningForInput = true;
        _pendingInput = null;

        // Determine input type based on current mapping category tab
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        var filter = isAxis ? InputDetectionFilter.Axes : InputDetectionFilter.Buttons;

        _inputDetectionService ??= new InputDetectionService(_inputService);

        try
        {
            // Small delay to let user release any currently pressed buttons
            await Task.Delay(200);

            var detected = await _inputDetectionService.WaitForInputAsync(filter, 0.5f, 15000);

            if (detected != null && _selectedMappingRow == rowIndex)
            {
                _pendingInput = detected;
                var inputSource = detected.ToInputSource();

                // Check for duplicate mapping
                var profile = _profileService.ActiveProfile;
                if (profile != null)
                {
                    var existingMapping = FindExistingMappingForInput(profile, inputSource);
                    if (existingMapping != null)
                    {
                        string newTarget = isAxis ? $"vJoy Axis {rowIndex}" : $"vJoy Button {rowIndex + 1}";
                        if (!ConfirmDuplicateMapping(existingMapping, newTarget))
                        {
                            // User cancelled, don't create the mapping
                            return;
                        }
                        // User confirmed, remove existing mapping first
                        RemoveExistingMappingsForInput(profile, inputSource);
                    }
                }

                // Save the mapping using current panel settings (output type, key combo, button mode)
                SaveMappingForRow(rowIndex, detected, isAxis);
            }
        }
        catch (Exception)
        {
            // Cancelled or error
        }
        finally
        {
            _isListeningForInput = false;
        }
    }

    private void SaveMappingForRow(int rowIndex, DetectedInput input, bool isAxis)
    {
        var profile = _profileService.ActiveProfile;
        if (profile == null) return;
        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        // rowIndex is already the correct index within the current category (axes or buttons)
        int outputIndex = rowIndex;
        var newInputSource = input.ToInputSource();

        if (isAxis)
        {
            // Find existing mapping or create new one
            var existingMapping = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (existingMapping != null)
            {
                // Add input to existing mapping (support multiple inputs)
                existingMapping.Inputs.Add(newInputSource);
                existingMapping.Name = $"vJoy {vjoyDevice.Id} Axis {outputIndex} ({existingMapping.Inputs.Count} inputs)";
            }
            else
            {
                // Create new mapping
                var mapping = new AxisMapping
                {
                    Name = $"{input.DeviceName} Axis {input.Index} -> vJoy {vjoyDevice.Id} Axis {outputIndex}",
                    Inputs = new List<InputSource> { newInputSource },
                    Output = new OutputTarget
                    {
                        Type = OutputType.VJoyAxis,
                        VJoyDevice = vjoyDevice.Id,
                        Index = outputIndex
                    },
                    Curve = new AxisCurve()
                };
                profile.AxisMappings.Add(mapping);
            }
        }
        else
        {
            // Find existing mapping for this button slot (regardless of output type)
            var existingMapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (existingMapping != null)
            {
                // Add input to existing mapping (support multiple inputs)
                existingMapping.Inputs.Add(newInputSource);

                // Update with current panel settings
                existingMapping.Output.Type = _outputTypeIsKeyboard ? OutputType.Keyboard : OutputType.VJoyButton;
                if (_outputTypeIsKeyboard)
                {
                    existingMapping.Output.KeyName = _selectedKeyName;
                    existingMapping.Output.Modifiers = _selectedModifiers?.ToList();
                }
                else
                {
                    existingMapping.Output.KeyName = null;
                    existingMapping.Output.Modifiers = null;
                }
                existingMapping.Mode = _selectedButtonMode;
                existingMapping.Name = $"vJoy {vjoyDevice.Id} Button {outputIndex + 1} ({existingMapping.Inputs.Count} inputs)";
            }
            else
            {
                // Create new mapping using current panel settings
                var outputType = _outputTypeIsKeyboard ? OutputType.Keyboard : OutputType.VJoyButton;
                var outputTarget = new OutputTarget
                {
                    Type = outputType,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                };

                if (_outputTypeIsKeyboard)
                {
                    outputTarget.KeyName = _selectedKeyName;
                    outputTarget.Modifiers = _selectedModifiers?.ToList();
                }

                string mappingName = _outputTypeIsKeyboard && !string.IsNullOrEmpty(_selectedKeyName)
                    ? $"{input.DeviceName} Button {input.Index + 1} -> {FormatKeyComboForDisplay(_selectedKeyName, _selectedModifiers)}"
                    : $"{input.DeviceName} Button {input.Index + 1} -> vJoy {vjoyDevice.Id} Button {outputIndex + 1}";

                var mapping = new ButtonMapping
                {
                    Name = mappingName,
                    Inputs = new List<InputSource> { newInputSource },
                    Output = outputTarget,
                    Mode = _selectedButtonMode
                };
                profile.ButtonMappings.Add(mapping);
            }
        }

        profile.ModifiedAt = DateTime.UtcNow;
        _profileService.SaveActiveProfile();
        _pendingInput = null;
    }

    private void RemoveInputSourceAtIndex(int inputIndex)
    {
        if (_selectedMappingRow < 0) return;
        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count) return;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        int outputIndex = _selectedMappingRow;

        if (isAxis)
        {
            var mapping = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (mapping != null && inputIndex >= 0 && inputIndex < mapping.Inputs.Count)
            {
                mapping.Inputs.RemoveAt(inputIndex);
                if (mapping.Inputs.Count == 0)
                {
                    // Remove the entire mapping if no inputs left
                    profile.AxisMappings.Remove(mapping);
                }
            }
        }
        else
        {
            var mapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyButton &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (mapping != null && inputIndex >= 0 && inputIndex < mapping.Inputs.Count)
            {
                mapping.Inputs.RemoveAt(inputIndex);
                if (mapping.Inputs.Count == 0)
                {
                    // Remove the entire mapping if no inputs left
                    profile.ButtonMappings.Remove(mapping);
                }
            }
        }

        profile.ModifiedAt = DateTime.UtcNow;
        _profileService.SaveActiveProfile();
    }

    private void LoadOutputTypeStateForRow()
    {
        // Reset state
        _outputTypeIsKeyboard = false;
        _selectedKeyName = "";
        _selectedModifiers = null;
        _isCapturingKey = false;
        _selectedButtonMode = ButtonMode.Normal;
        _pulseDurationMs = 100;
        _holdDurationMs = 500;

        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count) return;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping != null)
        {
            _outputTypeIsKeyboard = mapping.Output.Type == OutputType.Keyboard;
            _selectedKeyName = mapping.Output.KeyName ?? "";
            _selectedModifiers = mapping.Output.Modifiers?.ToList();
            _selectedButtonMode = mapping.Mode;
            _pulseDurationMs = mapping.PulseDurationMs;
            _holdDurationMs = mapping.HoldDurationMs;
        }
    }

    private void UpdateButtonModeForSelected()
    {
        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count) return;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        // Find mapping for this button slot (either VJoyButton or Keyboard output)
        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping != null)
        {
            mapping.Mode = _selectedButtonMode;
            profile.ModifiedAt = DateTime.UtcNow;
            _profileService.SaveActiveProfile();
        }
    }

    private void UpdateOutputTypeForSelected()
    {
        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count) return;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        // Find mapping for this button slot
        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping != null)
        {
            // Update output type and clear/set key name
            mapping.Output.Type = _outputTypeIsKeyboard ? OutputType.Keyboard : OutputType.VJoyButton;
            if (!_outputTypeIsKeyboard)
            {
                mapping.Output.KeyName = null;
                mapping.Output.Modifiers = null;
            }
            else if (!string.IsNullOrEmpty(_selectedKeyName))
            {
                mapping.Output.KeyName = _selectedKeyName;
            }
            profile.ModifiedAt = DateTime.UtcNow;
            _profileService.SaveActiveProfile();
        }
    }

    private void UpdateKeyNameForSelected()
    {
        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count) return;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping != null)
        {
            mapping.Output.KeyName = _selectedKeyName;
            mapping.Output.Modifiers = _selectedModifiers?.ToList();
            profile.ModifiedAt = DateTime.UtcNow;
            _profileService.SaveActiveProfile();
        }
    }

    private void UpdatePulseDurationFromMouse(float mouseX)
    {
        if (_pulseDurationSliderBounds.Width <= 0) return;

        float normalized = (mouseX - _pulseDurationSliderBounds.Left) / _pulseDurationSliderBounds.Width;
        normalized = Math.Clamp(normalized, 0f, 1f);

        // Map 0-1 to 100-1000ms
        _pulseDurationMs = (int)(100f + normalized * 900f);
    }

    private void UpdateHoldDurationFromMouse(float mouseX)
    {
        if (_holdDurationSliderBounds.Width <= 0) return;

        float normalized = (mouseX - _holdDurationSliderBounds.Left) / _holdDurationSliderBounds.Width;
        normalized = Math.Clamp(normalized, 0f, 1f);

        // Map 0-1 to 200-2000ms
        _holdDurationMs = (int)(200f + normalized * 1800f);
    }

    private void UpdateDurationForSelectedMapping()
    {
        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_vjoyDevices.Count == 0 || _selectedVJoyDeviceIndex >= _vjoyDevices.Count) return;

        var profile = _profileService.ActiveProfile;
        if (profile == null) return;

        var vjoyDevice = _vjoyDevices[_selectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping != null)
        {
            mapping.PulseDurationMs = _pulseDurationMs;
            mapping.HoldDurationMs = _holdDurationMs;
            profile.ModifiedAt = DateTime.UtcNow;
            _profileService.SaveActiveProfile();
        }
    }

    private void DrawAddMappingButton(SKCanvas canvas, SKRect bounds, bool hovered)
    {
        var bgColor = hovered ? FUIColors.Active.WithAlpha(60) : FUIColors.Primary.WithAlpha(30);
        var frameColor = hovered ? FUIColors.Active : FUIColors.Primary;

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = frameColor,
            StrokeWidth = hovered ? 2f : 1f,
            IsAntialias = true
        };
        canvas.DrawRect(bounds, framePaint);

        // Plus icon
        float iconX = bounds.Left + 15;
        float iconY = bounds.MidY;
        using var iconPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = hovered ? FUIColors.TextBright : FUIColors.TextPrimary,
            StrokeWidth = 2f,
            IsAntialias = true
        };
        canvas.DrawLine(iconX - 6, iconY, iconX + 6, iconY, iconPaint);
        canvas.DrawLine(iconX, iconY - 6, iconX, iconY + 6, iconPaint);

        FUIRenderer.DrawText(canvas, "ADD MAPPING",
            new SKPoint(bounds.Left + 30, bounds.MidY + 5),
            hovered ? FUIColors.TextBright : FUIColors.TextPrimary, 12f);
    }

    private void DrawMappingList(SKCanvas canvas, SKRect bounds)
    {
        float itemHeight = 50f;
        float itemGap = 8f;
        float y = bounds.Top;

        var profile = _profileService.ActiveProfile;
        if (profile == null)
        {
            FUIRenderer.DrawText(canvas, "No profile selected",
                new SKPoint(bounds.Left + 20, y + 20), FUIColors.TextDim, 12f);
            FUIRenderer.DrawText(canvas, "Select or create a profile to add mappings",
                new SKPoint(bounds.Left + 20, y + 40), FUIColors.TextDisabled, 11f);
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
                new SKPoint(bounds.Left + 20, y + 20), FUIColors.TextDim, 12f);
            FUIRenderer.DrawText(canvas, "Click '+ ADD MAPPING' to create your first mapping",
                new SKPoint(bounds.Left + 20, y + 40), FUIColors.TextDisabled, 11f);
            return;
        }

        // Draw mapping items
        foreach (var (source, target, type, enabled) in allMappings)
        {
            if (y + itemHeight > bounds.Bottom) break;

            var itemBounds = new SKRect(bounds.Left, y, bounds.Right, y + itemHeight);
            DrawMappingItem(canvas, itemBounds, source, target, type, enabled);
            y += itemHeight + itemGap;
        }
    }

    private void DrawMappingItem(SKCanvas canvas, SKRect bounds, string source, string target, string type, bool enabled)
    {
        // Background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = enabled ? FUIColors.Background2.WithAlpha(100) : FUIColors.Background1.WithAlpha(80)
        };
        canvas.DrawRect(bounds, bgPaint);

        // Frame
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = enabled ? FUIColors.Frame : FUIColors.FrameDim,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        // Type badge
        var typeColor = type == "BUTTON" ? FUIColors.Active : FUIColors.Primary;
        FUIRenderer.DrawText(canvas, type, new SKPoint(bounds.Left + 10, bounds.Top + 18),
            enabled ? typeColor : typeColor.WithAlpha(100), 10f);

        // Source
        FUIRenderer.DrawText(canvas, source, new SKPoint(bounds.Left + 80, bounds.Top + 18),
            enabled ? FUIColors.TextPrimary : FUIColors.TextDim, 12f);

        // Arrow
        FUIRenderer.DrawText(canvas, "->", new SKPoint(bounds.Left + 80, bounds.Top + 36),
            FUIColors.TextDim, 11f);

        // Target
        FUIRenderer.DrawText(canvas, target, new SKPoint(bounds.Left + 110, bounds.Top + 36),
            enabled ? FUIColors.TextPrimary : FUIColors.TextDim, 12f);

        // Status indicator
        var statusColor = enabled ? FUIColors.Success : FUIColors.TextDisabled;
        FUIRenderer.DrawGlowingDot(canvas, new SKPoint(bounds.Right - 20, bounds.MidY),
            statusColor, 4f, enabled ? 6f : 2f);
    }

    private void OpenAddMappingDialog()
    {
        // Ensure we have an active profile
        if (!_profileService.HasActiveProfile)
        {
            CreateNewProfilePrompt();
            if (!_profileService.HasActiveProfile) return;
        }

        using var dialog = new MappingDialog(_inputService, _vjoyService);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result.Success)
        {
            var result = dialog.Result;

            // Create the mapping based on detected input type
            if (result.Input!.Type == InputType.Button)
            {
                var mapping = new ButtonMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    Mode = result.ButtonMode
                };
                _profileService.ActiveProfile!.ButtonMappings.Add(mapping);
            }
            else if (result.Input.Type == InputType.Axis)
            {
                var mapping = new AxisMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    Curve = result.AxisCurve ?? new AxisCurve()
                };
                _profileService.ActiveProfile!.AxisMappings.Add(mapping);
            }
            else if (result.Input.Type == InputType.Hat)
            {
                var mapping = new HatMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    UseContinuous = true // Default to continuous POV
                };
                _profileService.ActiveProfile!.HatMappings.Add(mapping);
            }

            // Save the profile
            _profileService.SaveActiveProfile();
        }
    }

    private void OpenMappingDialogForControl(string controlId)
    {
        // Need device map, selected device, and control info
        if (_deviceMap == null || _selectedDevice < 0 || _selectedDevice >= _devices.Count)
            return;

        // Find the control definition in the device map
        if (!_deviceMap.Controls.TryGetValue(controlId, out var control))
            return;

        // Get the binding from the control (e.g., "button0", "x", "hat0")
        if (control.Bindings == null || control.Bindings.Count == 0)
            return;

        var device = _devices[_selectedDevice];
        var binding = control.Bindings[0];

        // Parse the binding to determine input type and index
        var (inputType, inputIndex) = ParseBinding(binding, control.Type);
        if (inputType == null)
            return;

        // Ensure we have an active profile
        if (!_profileService.HasActiveProfile)
        {
            CreateNewProfilePrompt();
            if (!_profileService.HasActiveProfile) return;
        }

        // Create a pre-selected DetectedInput
        var preSelectedInput = new DetectedInput
        {
            DeviceGuid = device.InstanceGuid,
            DeviceName = device.Name,
            Type = inputType.Value,
            Index = inputIndex,
            Value = 0
        };

        // Open dialog with pre-selected input (skips "wait for input" phase)
        using var dialog = new MappingDialog(_inputService, _vjoyService, preSelectedInput);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result.Success)
        {
            var result = dialog.Result;

            // Create the mapping based on detected input type
            if (result.Input!.Type == InputType.Button)
            {
                var mapping = new ButtonMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    Mode = result.ButtonMode
                };
                _profileService.ActiveProfile!.ButtonMappings.Add(mapping);
            }
            else if (result.Input.Type == InputType.Axis)
            {
                var mapping = new AxisMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    Curve = result.AxisCurve ?? new AxisCurve()
                };
                _profileService.ActiveProfile!.AxisMappings.Add(mapping);
            }
            else if (result.Input.Type == InputType.Hat)
            {
                var mapping = new HatMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    UseContinuous = true
                };
                _profileService.ActiveProfile!.HatMappings.Add(mapping);
            }

            // Save the profile
            _profileService.SaveActiveProfile();
        }
    }

    private (InputType? type, int index) ParseBinding(string binding, string controlType)
    {
        // Handle button bindings: "button0", "button1", etc.
        if (binding.StartsWith("button", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(binding.Substring(6), out int buttonIndex))
                return (InputType.Button, buttonIndex);
        }

        // Handle axis bindings: "x", "y", "z", "rx", "ry", "rz", "slider0", "slider1"
        var axisMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "x", 0 }, { "y", 1 }, { "z", 2 },
            { "rx", 3 }, { "ry", 4 }, { "rz", 5 },
            { "slider0", 6 }, { "slider1", 7 }
        };
        if (axisMap.TryGetValue(binding, out int axisIndex))
            return (InputType.Axis, axisIndex);

        // Handle hat bindings: "hat0", "hat1", etc.
        if (binding.StartsWith("hat", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(binding.Substring(3), out int hatIndex))
                return (InputType.Hat, hatIndex);
        }

        // Fall back to control type if binding doesn't parse
        return controlType.ToUpperInvariant() switch
        {
            "BUTTON" => (InputType.Button, 0),
            "AXIS" => (InputType.Axis, 0),
            "HAT" or "POV" => (InputType.Hat, 0),
            _ => (null, 0)
        };
    }

    #endregion
}
