using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class SCBindingsTabController
{
    private void DrawColumnActionsPanel(SKCanvas canvas, SKRect bounds, float frameInset, bool isExpanded)
    {
        if (_grid.Columns is null || _colImport.HighlightedColumn < 0 || _colImport.HighlightedColumn >= _grid.Columns.Count)
            return;

        var col = _grid.Columns[_colImport.HighlightedColumn];

        bool headerHovered = !isExpanded
            && new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Top + FUIRenderer.PanelHeaderHeight)
                .Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        var m = FUIWidgets.DrawCollapsiblePanelHeader(canvas, bounds, "COLUMN ACTIONS",
            isExpanded, headerHovered, out var hdrBounds);
        _colImport.HeaderBounds = hdrBounds;
        if (!isExpanded) return;
        float y = m.Y;
        float leftMargin = m.LeftMargin;
        float rightMargin = m.RightMargin;

        y += 14f;

        // Column label + device name
        string colLabel = $"JS{col.SCInstance}";
        FUIRenderer.DrawText(canvas, colLabel, new SKPoint(leftMargin, y), FUIColors.Active, 13f, true);
        string? deviceName = GetPhysicalDeviceNameForVJoyColumn(col);
        if (deviceName is not null)
        {
            FUIRenderer.DrawText(canvas, " — ", new SKPoint(leftMargin + 26f, y), FUIColors.TextDim, 13f);
            string shortName = FUIWidgets.TruncateTextToWidth(deviceName, rightMargin - leftMargin - 48f, 10f);
            FUIRenderer.DrawText(canvas, shortName, new SKPoint(leftMargin + 44f, y), FUIColors.TextPrimary, 12f);
        }
        y += 17f;

        // Binding count for this column
        int bindingCount = _scExportProfile.Bindings.Count(b =>
            b.DeviceType == SCDeviceType.Joystick &&
            b.PhysicalDeviceId is null &&
            _scExportProfile.GetSCInstance(b.VJoyDevice) == col.SCInstance);
        string countStr = bindingCount == 0 ? "No bindings" : $"{bindingCount} binding{(bindingCount == 1 ? "" : "s")}";
        FUIRenderer.DrawText(canvas, countStr, new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
        y += 20f;

        // IMPORT FROM section
        FUIRenderer.DrawText(canvas, "IMPORT FROM", new SKPoint(leftMargin, y), FUIColors.TextDim, 11f, true);
        y += 14f;

        // Source profile selector — shows saved Asteriq profiles + SC XML files
        float selectorH = 28f;
        var (savedProfiles, xmlFiles) = GetColImportSources();
        int totalSources = savedProfiles.Count + xmlFiles.Count;
        bool hasProfiles = totalSources > 0;
        string profileSelectorLabel = _colImport.ProfileIndex >= 0 && _colImport.ProfileIndex < totalSources
            ? GetColImportSourceLabel(_colImport.ProfileIndex, savedProfiles, xmlFiles)
            : (hasProfiles ? "Select profile…" : "No other profiles");
        _colImport.ProfileSelectorBounds = new SKRect(leftMargin, y, rightMargin, y + selectorH);
        bool profileSelectorHovered = _colImport.ProfileSelectorBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawSelector(canvas, _colImport.ProfileSelectorBounds, profileSelectorLabel,
            profileSelectorHovered || _colImport.ProfileDropdownOpen, hasProfiles);
        y += selectorH + 4f;

        // Source column selector
        bool hasSourceColumns = _colImport.SourceColumns.Count > 0;
        string columnSelectorLabel = _colImport.ColumnIndex >= 0 && _colImport.ColumnIndex < _colImport.SourceColumns.Count
            ? _colImport.SourceColumns[_colImport.ColumnIndex].Label
            : (_colImport.ProfileIndex >= 0 && !hasSourceColumns ? "No columns found" : "Select column…");
        _colImport.ColumnSelectorBounds = new SKRect(leftMargin, y, rightMargin, y + selectorH);
        bool columnSelectorHovered = _colImport.ColumnSelectorBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIWidgets.DrawSelector(canvas, _colImport.ColumnSelectorBounds, columnSelectorLabel,
            columnSelectorHovered || _colImport.ColumnDropdownOpen, hasSourceColumns);

        // Action buttons anchored to panel bottom
        float btnH = 28f;
        float btnW = (rightMargin - leftMargin - 8f) / 2f;
        float btnY = bounds.Bottom - frameInset - FUIRenderer.SpaceLG - btnH;

        bool canImport = _colImport.ProfileIndex >= 0 && _colImport.ColumnIndex >= 0;
        _colImport.ImportButtonBounds = new SKRect(leftMargin, btnY, leftMargin + btnW, btnY + btnH);
        _colImport.ImportButtonHovered = _colImport.ImportButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        if (canImport)
        {
            FUIRenderer.DrawButton(canvas, _colImport.ImportButtonBounds, "IMPORT",
                _colImport.ImportButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
        }
        else
        {
            using var disabledPaint = FUIRenderer.CreateFillPaint(FUIColors.DisabledBg);
            canvas.DrawRect(_colImport.ImportButtonBounds, disabledPaint);
            FUIRenderer.DrawTextCentered(canvas, "IMPORT", _colImport.ImportButtonBounds, FUIColors.TextDim.WithAlpha(100), 12f);
        }

        _colImport.ClearColumnBounds = new SKRect(leftMargin + btnW + 8f, btnY, rightMargin, btnY + btnH);
        _colImport.ClearColumnHovered = _colImport.ClearColumnBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        if (bindingCount > 0)
        {
            FUIRenderer.DrawButton(canvas, _colImport.ClearColumnBounds, "CLEAR COL",
                _colImport.ClearColumnHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal,
                isDanger: true);
        }
        else
        {
            using var disabledPaint = FUIRenderer.CreateFillPaint(FUIColors.DisabledBg);
            canvas.DrawRect(_colImport.ClearColumnBounds, disabledPaint);
            FUIRenderer.DrawTextCentered(canvas, "CLEAR COL", _colImport.ClearColumnBounds, FUIColors.TextDim.WithAlpha(100), 12f);
        }
    }

    private void DrawColImportProfileDropdown(SKCanvas canvas)
    {
        var (savedProfiles, xmlFiles) = GetColImportSources();
        int totalSources = savedProfiles.Count + xmlFiles.Count;
        if (totalSources == 0) return;

        float itemH = 28f;
        float dropdownH = Math.Min(totalSources * itemH + 8f, 200f);
        _colImport.ProfileDropdownBounds = new SKRect(
            _colImport.ProfileSelectorBounds.Left,
            _colImport.ProfileSelectorBounds.Bottom + 2,
            _colImport.ProfileSelectorBounds.Right,
            _colImport.ProfileSelectorBounds.Bottom + 2 + dropdownH);

        var items = savedProfiles.Select(p => p.ProfileName)
            .Concat(xmlFiles.Select(f => $"[SC] {f.DisplayName}"))
            .ToList();
        FUIWidgets.DrawDropdownPanel(canvas, _colImport.ProfileDropdownBounds, items,
            _colImport.ProfileIndex, _colImport.ProfileHoveredIndex, itemH);
    }

    /// <summary>
    /// Returns the two separate source collections for the Import From Profile picker.
    /// Asteriq saved profiles first (excluding the active one), then SC XML mapping files.
    /// </summary>
    private (List<SCExportProfileInfo> Saved, List<SCMappingFile> Xml) GetColImportSources()
    {
        var saved = _profileMgmt.ExportProfiles.Where(p => p.ProfileName != _scExportProfile.ProfileName).ToList();
        return (saved, _scAvailableProfiles);
    }

    private static string GetColImportSourceLabel(int index, List<SCExportProfileInfo> saved, List<SCMappingFile> xml)
    {
        if (index < saved.Count)
            return saved[index].ProfileName;
        int xmlIdx = index - saved.Count;
        return xmlIdx < xml.Count ? $"[SC] {xml[xmlIdx].DisplayName}" : "?";
    }

    private void DrawColImportColumnDropdown(SKCanvas canvas)
    {
        if (_colImport.SourceColumns.Count == 0) return;

        float itemH = 28f;
        float dropdownH = Math.Min(_colImport.SourceColumns.Count * itemH + 8f, 200f);
        _colImport.ColumnDropdownBounds = new SKRect(
            _colImport.ColumnSelectorBounds.Left,
            _colImport.ColumnSelectorBounds.Bottom + 2,
            _colImport.ColumnSelectorBounds.Right,
            _colImport.ColumnSelectorBounds.Bottom + 2 + dropdownH);

        var items = _colImport.SourceColumns.Select(c => c.Label).ToList();
        FUIWidgets.DrawDropdownPanel(canvas, _colImport.ColumnDropdownBounds, items,
            _colImport.ColumnIndex, _colImport.ColumnHoveredIndex, itemH);
    }

    /// <summary>
    /// Read-only "Binding Definition" panel — renders Asteriq's authored long-form description and
    /// use cases for the currently-selected action, plus CIG's short tagline if available. Lives
    /// in the contextual slot above Cell Details when a row is selected. Auto-expanded when the
    /// user clicks the action name; auto-collapsed (header only) when they click a cell.
    /// </summary>
    private void DrawBindingDefinitionPanel(SKCanvas canvas, SKRect bounds, float frameInset, bool isExpanded)
    {
        if (_scFilteredActions is null
            || _cell.SelectedCell.actionIndex < 0
            || _cell.SelectedCell.actionIndex >= _scFilteredActions.Count)
            return;

        bool headerHovered = !isExpanded
            && new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Top + FUIRenderer.PanelHeaderHeight)
                .Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        var m = FUIWidgets.DrawCollapsiblePanelHeader(canvas, bounds, "BINDING DEFINITION",
            isExpanded, headerHovered, out var hdrBounds);
        _bdPanel.HeaderBounds = hdrBounds;
        if (!isExpanded) return;

        float y = m.Y;
        float leftMargin = m.LeftMargin;
        float rightMargin = m.RightMargin;
        float panelWidth = rightMargin - leftMargin;

        var selectedAction = _scFilteredActions[_cell.SelectedCell.actionIndex];

        // Action label heading — prefer SC's localised label, fall back to mechanical formatter.
        string actionLabel = selectedAction.DisplayLabel
            ?? SCCategoryMapper.FormatActionName(selectedAction.ActionName);
        FUIRenderer.DrawText(canvas, actionLabel,
            new SKPoint(leftMargin, y + 14), FUIColors.TextPrimary, 14f, true);
        y += 18f;

        // Raw action code (the identifier SC writes to the .xml profile) shown as a subtitle so
        // power users can correlate the friendly label back to the underlying binding name.
        FUIRenderer.DrawText(canvas, selectedAction.ActionName,
            new SKPoint(leftMargin, y + 14), FUIColors.TextDim, 14f);
        y += 30f;

        // CIG's short tagline when present.
        if (!string.IsNullOrEmpty(selectedAction.Description))
        {
            foreach (var wrapped in FUIWidgets.WrapTextToWidth(selectedAction.Description, panelWidth - 4, 14f))
            {
                FUIRenderer.DrawText(canvas, wrapped, new SKPoint(leftMargin, y + 14), FUIColors.TextDim, 14f);
                y += 18f;
            }
            y += 6f;
        }

        // Asteriq long-form description + use cases. Renders nothing when the action has no
        // entry in the locale JSON yet — we surface a small "no description yet" line below
        // instead so the panel doesn't look broken on un-documented actions.
        var asteriqDesc = _bindingDescriptionService.Get(selectedAction.ActionName);
        if (asteriqDesc is not null)
        {
            foreach (var wrapped in FUIWidgets.WrapTextToWidth(asteriqDesc.Description, panelWidth - 4, 14f))
            {
                FUIRenderer.DrawText(canvas, wrapped, new SKPoint(leftMargin, y + 14), FUIColors.TextPrimary, 14f);
                y += 18f;
            }

            if (asteriqDesc.UseCases.Count > 0)
            {
                y += 8f;
                FUIWidgets.DrawSectionLabel(canvas, "USE CASES", leftMargin, ref y);
                y += 2f;
                foreach (var useCase in asteriqDesc.UseCases)
                {
                    var lines = FUIWidgets.WrapTextToWidth(useCase, panelWidth - 18f, 14f);
                    bool first = true;
                    foreach (var wrapped in lines)
                    {
                        // First line of each use case gets the bullet; wrapped continuations
                        // are indented to line up with the bullet text.
                        var prefix = first ? "•  " : "    ";
                        FUIRenderer.DrawText(canvas, prefix + wrapped,
                            new SKPoint(leftMargin + 2f, y + 14), FUIColors.TextPrimary, 14f);
                        y += 18f;
                        first = false;
                    }
                }
            }
        }
        else if (string.IsNullOrEmpty(selectedAction.Description))
        {
            // No tagline AND no Asteriq entry — make the empty state explicit.
            FUIRenderer.DrawText(canvas, "No description yet for this action.",
                new SKPoint(leftMargin, y + 14), FUIColors.TextDim, 14f);
        }
    }

    private void DrawCellDetailsPanel(SKCanvas canvas, SKRect bounds, float frameInset, bool isExpanded)
    {
        if (_scFilteredActions is null || _cell.SelectedCell.actionIndex < 0 || _cell.SelectedCell.actionIndex >= _scFilteredActions.Count)
            return;

        bool headerHovered = !isExpanded
            && new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Top + FUIRenderer.PanelHeaderHeight)
                .Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        var m = FUIWidgets.DrawCollapsiblePanelHeader(canvas, bounds, "BINDING DETAILS",
            isExpanded, headerHovered, out var hdrBounds);
        _cellDetails.HeaderBounds = hdrBounds;
        if (!isExpanded) return;
        float y = m.Y;
        float leftMargin = m.LeftMargin;
        float rightMargin = m.RightMargin;

        y += 14f;

        var selectedAction = _scFilteredActions[_cell.SelectedCell.actionIndex];
        float lineHeight = 15f;
        float panelWidth = rightMargin - leftMargin;

        // Action name — prefer SC's own localised label when available, fall back to the
        // mechanical name-formatter so un-hydrated actions still get a readable heading.
        string actionLabel = selectedAction.DisplayLabel
            ?? SCCategoryMapper.FormatActionName(selectedAction.ActionName);
        string actionDisplay = FUIWidgets.TruncateTextToWidth(actionLabel, panelWidth - 10, 14f);
        FUIRenderer.DrawText(canvas, actionDisplay, new SKPoint(leftMargin, y), FUIColors.TextPrimary, 14f, true);
        y += 18f;

        FUIRenderer.DrawText(canvas, $"Type: {selectedAction.InputType}", new SKPoint(leftMargin, y), FUIColors.TextDim, 14f);
        y += 18f;

        // Optional description — sourced from ui_<action>_desc in SC's localisation.
        // Only present when SC ships a genuinely different sentence from the label.
        if (!string.IsNullOrEmpty(selectedAction.Description))
        {
            y += 2f;
            foreach (var wrapped in FUIWidgets.WrapTextToWidth(selectedAction.Description, panelWidth - 4, 14f))
            {
                FUIRenderer.DrawText(canvas, wrapped, new SKPoint(leftMargin, y), FUIColors.TextDim, 14f);
                y += 18f;
            }
            y += 2f;
        }

        // Detect shared cell state
        bool isCellShared = false;
        uint sharedPrimaryVJoy = 0;
        string sharedPrimaryInput = "";
        SCActionBinding? existingBinding = null;
        SCGridColumn? selCol = null;

        if (_cell.SelectedCell.colIndex >= 0 && _grid.Columns is not null && _cell.SelectedCell.colIndex < _grid.Columns.Count)
        {
            selCol = _grid.Columns[_cell.SelectedCell.colIndex];

            // Check shared cell
            if (selCol.IsJoystick && !selCol.IsPhysical)
            {
                string sharedKey = $"{selectedAction.Key}|{selCol.VJoyDeviceId}";
                if (_conflicts.SharedCells.TryGetValue(sharedKey, out var sharedInfo))
                {
                    isCellShared = true;
                    (sharedPrimaryVJoy, sharedPrimaryInput, _) = sharedInfo;
                }
            }

            // Find existing binding
            existingBinding = FindBindingForCell(selectedAction, selCol);
        }

        if (isCellShared)
        {
            int primaryInstance = _scExportProfile.GetSCInstance(sharedPrimaryVJoy);
            string primaryFormatted = SCBindingsRenderer.FormatInputName(sharedPrimaryInput);
            FUIRenderer.DrawText(canvas, $"Routed to JS{primaryInstance} / {primaryFormatted}",
                new SKPoint(leftMargin, y), FUIColors.Primary.WithAlpha(200), 11f, true);
            y += lineHeight;
        }

        y += 12f;

        // Activation mode is per-binding in SC's XML — it controls how a press triggers the
        // action. So the gate is the BINDING's input type, not the action's heuristic
        // classification. KB / Mouse cells are always discrete, JS button + hat are discrete,
        // only a true axis input (e.g. "x", "u", "rotz") is continuous and has no concept of
        // press timing. When no binding exists yet, fall back to the column type so a freshly
        // selected button-style cell still shows the controls.
        bool bindingIsButtonLike;
        if (existingBinding is not null)
        {
            var inputName = existingBinding.InputName;
            bool isJoystickAxisBinding = existingBinding.DeviceType == SCDeviceType.Joystick
                && !inputName.StartsWith("button", StringComparison.OrdinalIgnoreCase)
                && !inputName.StartsWith("hat", StringComparison.OrdinalIgnoreCase);
            bindingIsButtonLike = !isJoystickAxisBinding;
        }
        else
        {
            // No binding yet — use the column type as the proxy. Only a JS column on a true
            // axis-typed action would default to "axis-style" (no activation mode).
            bool jsAxisColumn = selCol is not null && selCol.IsJoystick
                && selectedAction.InputType == SCInputType.Axis;
            bindingIsButtonLike = !jsAxisColumn;
        }

        if (bindingIsButtonLike)
        {
            FUIRenderer.DrawText(canvas, "ACTIVATION MODE", new SKPoint(leftMargin, y), FUIColors.TextDim, 11f, true);
            y += 14f;

            var activationLabels = new[] { "PRESS", "HOLD", "2TAP", "3TAP", "DELAY" };
            int selectedMode = existingBinding is not null ? (int)existingBinding.ActivationMode : 0;
            bool modeEnabled = existingBinding is not null && !isCellShared;

            var segBounds = new SKRect(leftMargin, y, rightMargin, y + 24f);
            var segRects = FUIWidgets.DrawSegmentedControl(canvas, segBounds, activationLabels,
                selectedMode, _cellDetails.HoveredModeIndex, modeEnabled);

            // Store bounds for hit-testing
            for (int i = 0; i < segRects.Length && i < _cellDetails.ActivationModeBounds.Length; i++)
                _cellDetails.ActivationModeBounds[i] = segRects[i];

            y += 28f;
        }

        // ASSIGN / CLEAR buttons — anchored to panel bottom
        float btnWidth = (rightMargin - leftMargin - 8) / 2;
        float btnHeight = 24f;
        float btnY = bounds.Bottom - frameInset - FUIRenderer.SpaceLG - btnHeight;

        _scAssignInputButtonBounds = new SKRect(leftMargin, btnY, leftMargin + btnWidth, btnY + btnHeight);
        _scAssignInputButtonHovered = _scAssignInputButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

        bool isCellReadOnly = _cell.SelectedCell.colIndex >= 0 && _grid.Columns is not null
            && _cell.SelectedCell.colIndex < _grid.Columns.Count
            && _grid.Columns[_cell.SelectedCell.colIndex].IsReadOnly;

        if (_scListening.IsListening)
        {
            using var waitBgPaint = FUIRenderer.CreateFillPaint(FUIColors.SelectionBgStrong);
            canvas.DrawRect(_scAssignInputButtonBounds, waitBgPaint);
            FUIRenderer.DrawTextCentered(canvas, "LISTENING...", _scAssignInputButtonBounds, FUIColors.Active, 12f);
        }
        else if (isCellReadOnly)
        {
            using var disabledPaint = FUIRenderer.CreateFillPaint(FUIColors.DisabledBg);
            canvas.DrawRect(_scAssignInputButtonBounds, disabledPaint);
            FUIRenderer.DrawTextCentered(canvas, "ASSIGN", _scAssignInputButtonBounds, FUIColors.TextDim.WithAlpha(100), 13f);
        }
        else
        {
            // ASSIGN is available on shared cells too — it replaces the shared input with
            // a new one without disturbing the primary or any other shares.
            FUIRenderer.DrawButton(canvas, _scAssignInputButtonBounds, "ASSIGN",
                _scAssignInputButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
        }

        _scClearBindingButtonBounds = new SKRect(leftMargin + btnWidth + 8, btnY, rightMargin, btnY + btnHeight);
        _scClearBindingButtonHovered = _scClearBindingButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

        if (isCellShared)
        {
            FUIRenderer.DrawButton(canvas, _scClearBindingButtonBounds, "UNSHARE",
                _scClearBindingButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
        }
        else if (existingBinding is not null)
        {
            FUIRenderer.DrawButton(canvas, _scClearBindingButtonBounds, "CLEAR",
                _scClearBindingButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
        }
        else
        {
            using var disabledPaint = FUIRenderer.CreateFillPaint(FUIColors.DisabledBg);
            canvas.DrawRect(_scClearBindingButtonBounds, disabledPaint);
            FUIRenderer.DrawTextCentered(canvas, "CLEAR", _scClearBindingButtonBounds, FUIColors.TextDim.WithAlpha(100), 13f);
        }

        // Conflict links panel — fills space between content and buttons
        y += 6f;
        if (_conflicts.ConflictLinks.Count > 0)
            DrawConflictLinksPanel(canvas, leftMargin, rightMargin, y, btnY - 10f);
    }

    /// <summary>
    /// Finds the binding for a specific cell (action + column).
    /// </summary>
}