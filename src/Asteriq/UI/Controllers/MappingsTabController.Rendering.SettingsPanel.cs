using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
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

        // Reset merged-away manage button bounds each frame; populated only when the selected
        // axis row is a merged-away slot (the early-return branch in the axis section below).
        _mergedAwayManageButtonBounds = SKRect.Empty;
        _mergedAwayManageButtonHovered = false;
        _mergedAwayManageTarget = null;

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

        // Merged-away slot: this axis's natural physical input is being consumed by a merge
        // on another vJoy slot. Replace the editor with a panel pointing at the merge target —
        // editing here would either be ignored (no input bound) or duplicate effort with the
        // merge. The "GO TO MERGED AXIS" button switches to the target slot.
        if (isAxis && _ctx.VJoyDevices.Count > _ctx.SelectedVJoyDeviceIndex)
        {
            var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
            int axisIdx = AxisIndexForRow(_selectedMappingRow);
            var info = GetMergedAwayInfo(_ctx.ProfileManager.ActiveProfile, vjoyDevice.Id, axisIdx);
            if (info is not null)
            {
                DrawMergedAwaySlotPanel(canvas, leftMargin, rightMargin, y, info.Value.SecondaryInput, info.Value.Target);
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

    /// <summary>
    /// Renders the read-only "this slot's natural input is consumed by a merge elsewhere"
    /// panel that replaces the normal axis editor. Mirrors DrawSharedSlotPanel — banner +
    /// explanation + manage button that jumps to the merge-target slot. Editor is hidden so
    /// the user can't make changes here that would be silently overridden by the merge.
    /// </summary>
    private void DrawMergedAwaySlotPanel(SKCanvas canvas, float leftMargin, float rightMargin, float y,
        InputSource source, AxisMapping target)
    {
        // Banner — blue rounded panel with [MERGED →] pill + target headline.
        var bannerRect = new SKRect(leftMargin, y, rightMargin, y + 36);
        FUIRenderer.DrawRoundedPanel(canvas, bannerRect,
            FUIColors.Primary.WithAlpha(FUIColors.AlphaLightTint),
            FUIColors.Primary.WithAlpha(FUIColors.AlphaBorderSoft));

        const float pillW = 60f;
        const float pillH = 14f;
        var pillRect = new SKRect(bannerRect.Left + 10f, bannerRect.MidY - pillH / 2f,
            bannerRect.Left + 10f + pillW, bannerRect.MidY + pillH / 2f);
        FUIRenderer.DrawRoundedPanel(canvas, pillRect,
            FUIColors.Primary.WithAlpha(FUIColors.AlphaHoverBg),
            FUIColors.Primary.WithAlpha(FUIColors.AlphaBorderSoft));
        FUIRenderer.DrawTextCentered(canvas, "MERGED →", pillRect, FUIColors.Primary, 10f);

        string headline = $"vJoy {target.Output.VJoyDevice} {GetVJoyAxisName(target.Output.Index)}";
        FUIRenderer.DrawText(canvas, headline,
            new SKPoint(pillRect.Right + 10f, bannerRect.MidY + 5f), FUIColors.Primary, 13f);
        y += 36 + 12;

        FUIRenderer.DrawText(canvas, "This input is merged into another vJoy axis.",
            new SKPoint(leftMargin, y + 14), FUIColors.TextPrimary, 13f);
        y += 22;

        FUIRenderer.DrawText(canvas, $"  • Source: {source.DeviceName} Axis {source.Index}",
            new SKPoint(leftMargin, y + 14), FUIColors.TextDim, 12f);
        y += 18;
        FUIRenderer.DrawText(canvas,
            $"  • Feeds vJoy {target.Output.VJoyDevice} {GetVJoyAxisName(target.Output.Index)} ({target.Inputs.Count} merged inputs, {target.MergeOp})",
            new SKPoint(leftMargin, y + 14), FUIColors.TextDim, 12f);
        y += 22;

        FUIRenderer.DrawText(canvas, "Settings on this slot are inactive. Manage from the merge target.",
            new SKPoint(leftMargin, y + 14), FUIColors.TextDim, 12f);
        y += 28;

        // Manage button — switches to the merge-target slot.
        var btnBounds = new SKRect(leftMargin, y, rightMargin, y + 32);
        _mergedAwayManageButtonBounds = btnBounds;
        _mergedAwayManageButtonHovered = btnBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        _mergedAwayManageTarget = (target.Output.VJoyDevice, target.Output.Index);

        FUIRenderer.DrawButton(canvas, btnBounds, "GO TO MERGED AXIS",
            _mergedAwayManageButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
    }

}