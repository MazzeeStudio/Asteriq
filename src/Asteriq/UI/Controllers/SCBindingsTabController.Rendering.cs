using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class SCBindingsTabController
{
    private void DrawBindingsTabContent(SKCanvas canvas, SKRect bounds, float pad, float contentTop, float contentBottom)
    {
        float frameInset = 5f;
        var contentBounds = new SKRect(pad, contentTop, bounds.Right - pad, contentBottom);

        // Two-panel layout: Left (bindings table) | Right (Game Environment + Control Profiles + contextual panel)
        float rightPanelWidth = Math.Min(500f, Math.Max(280f, contentBounds.Width * 0.24f));
        float gap = 10f;

        var leftBounds = new SKRect(contentBounds.Left, contentBounds.Top,
            contentBounds.Right - rightPanelWidth - gap, contentBounds.Bottom);
        var rightBounds = new SKRect(leftBounds.Right + gap, contentBounds.Top,
            contentBounds.Right, contentBounds.Bottom);

        // Right panel stacking order (top → bottom):
        //   1. Game Environment (fixed, always visible)
        //   2. Control Profiles (fills remaining, collapses when contextual panel is expanded)
        //   3. Contextual panel (Column Actions, OR Binding Definition + Cell Details sub-stack)
        // The contextual area itself can be a stacked pair of [Binding Definition, Cell Details]
        // when a row is selected — see the sub-layout block below.
        float verticalGap = 8f;
        float installationHeight = 110f;


        var installationBounds = new SKRect(rightBounds.Left, rightBounds.Top,
            rightBounds.Right, rightBounds.Top + installationHeight);

        // Determine which contextual content is active.
        bool showColumnActions = IsColumnActionsVisible();
        bool hasRowSelection = !showColumnActions
            && _cell.SelectedCell.actionIndex >= 0
            && _scFilteredActions is not null
            && _cell.SelectedCell.actionIndex < _scFilteredActions.Count;
        bool showCellDetails = hasRowSelection && _cell.SelectedCell.colIndex >= 0;
        ref var anim = ref _cpPanel.Anim;

        float afterInstall = installationBounds.Bottom + verticalGap;
        float bottomAreaBottom = rightBounds.Bottom;
        var splitArea = new SKRect(rightBounds.Left, afterInstall, rightBounds.Right, bottomAreaBottom);

        // When the contextual area stacks Binding Definition + Cell Details, B's collapsed
        // minimum must fit BOTH headers (single collapsedH would leave one off-screen).
        const float subStackGap = 4f;
        float collapsedBH = showCellDetails
            ? 2 * FUIRenderer.CollapsedPanelHeight + subStackGap
            : FUIRenderer.CollapsedPanelHeight;

        SKRect controlProfilesBounds;
        SKRect contextualBounds = SKRect.Empty;
        if (anim.UseAnimatedLayout)
        {
            (controlProfilesBounds, contextualBounds) = anim.ComputeBounds(
                splitArea, verticalGap, FUIRenderer.CollapsedPanelHeight, collapsedBH);
        }
        else
        {
            controlProfilesBounds = splitArea;
        }

        // LEFT PANEL
        DrawSCBindingsTablePanel(canvas, leftBounds, frameInset);

        // RIGHT 1 — Game Environment (always visible)
        DrawSCInstallationPanelCompact(canvas, installationBounds, frameInset);

        // RIGHT 2 — Control Profiles (clipped to bounds during animation)
        bool cpExpanded = !anim.UseAnimatedLayout || _cpPanel.IsExpanded || anim.IsAnimatingOut;
        bool cpCollapsible = anim.UseAnimatedLayout && !anim.IsAnimatingOut;
        canvas.SaveLayer(controlProfilesBounds, null);
        DrawSCExportPanelCompact(canvas, controlProfilesBounds, frameInset,
            isExpanded: cpExpanded, isCollapsible: cpCollapsible);
        canvas.Restore();

        // RIGHT 3 — Contextual panel (Column Actions, or Binding Definition + Cell Details sub-stack)
        // Reset header bounds each frame; the relevant panels populate them when drawn.
        _bdPanel.HeaderBounds = SKRect.Empty;
        _cellDetails.HeaderBounds = SKRect.Empty;

        if (anim.UseAnimatedLayout)
        {
            bool contextualExpanded = !_cpPanel.IsExpanded && !anim.IsAnimatingOut;
            canvas.SaveLayer(contextualBounds, null);

            if (showColumnActions)
            {
                DrawColumnActionsPanel(canvas, contextualBounds, frameInset, contextualExpanded);
            }
            else if (hasRowSelection)
            {
                if (!showCellDetails)
                {
                    // Row selected, no cell — Binding Definition fills the contextual area.
                    DrawBindingDefinitionPanel(canvas, contextualBounds, frameInset, contextualExpanded);
                }
                else
                {
                    // Row + cell selected — animated [BD, Cell Details] sub-stack. SubAnim
                    // lerps the split: T=1 → BD expanded, T=0 → Cell Details expanded.
                    // Content gating mirrors CP's pattern: only the spotlight (target-expanded)
                    // panel draws content; the collapsing panel draws header-only so its
                    // bottom-anchored content (e.g. ASSIGN/CLEAR) doesn't hover in place
                    // while the header slides down underneath. ClipRect on the growing
                    // panel keeps its content from bleeding past its current bounds.
                    float collapsedH = FUIRenderer.CollapsedPanelHeight;
                    var (bdBounds, detailsBounds) = _bdPanel.SubAnim.ComputeBounds(
                        contextualBounds, subStackGap, collapsedH, collapsedH);

                    canvas.Save();
                    canvas.ClipRect(bdBounds);
                    DrawBindingDefinitionPanel(canvas, bdBounds, frameInset,
                        isExpanded: contextualExpanded && _bdPanel.IsExpanded);
                    canvas.Restore();

                    canvas.Save();
                    canvas.ClipRect(detailsBounds);
                    DrawCellDetailsPanel(canvas, detailsBounds, frameInset,
                        isExpanded: contextualExpanded && !_bdPanel.IsExpanded);
                    canvas.Restore();
                }
            }

            canvas.Restore();
        }

        // Draw dropdowns last (on top) so they render over all panels
        if (_profileMgmt.DropdownOpen && !_profileMgmt.DropdownListBounds.IsEmpty)
            DrawSCProfileDropdownList(canvas, _profileMgmt.DropdownListBounds);
        if (_scInstall.DropdownOpen && _scInstall.Installations.Count > 0)
            DrawSCInstallationDropdown(canvas);
        if (_searchFilter.FilterDropdownOpen && _searchFilter.ActionMaps.Count > 0)
            DrawSCActionMapFilterDropdown(canvas);
        if (showColumnActions && _colImport.ProfileDropdownOpen)
            DrawColImportProfileDropdown(canvas);
        if (showColumnActions && _colImport.ColumnDropdownOpen && _colImport.SourceColumns.Count > 0)
            DrawColImportColumnDropdown(canvas);
    }

    /// <summary>
    /// Returns true when the Column Actions panel should be visible (vJoy column selected).
    /// </summary>
    private bool IsColumnActionsVisible()
    {
        return _colImport.HighlightedColumn >= 0
            && _grid.Columns is not null
            && _colImport.HighlightedColumn < _grid.Columns.Count
            && _grid.Columns[_colImport.HighlightedColumn].IsJoystick
            && !_grid.Columns[_colImport.HighlightedColumn].IsPhysical
            && !_grid.Columns[_colImport.HighlightedColumn].IsReadOnly;
    }

    private void DrawSCInstallationPanelCompact(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        var m = FUIRenderer.DrawPanelChrome(canvas, bounds);
        float y = m.Y;
        FUIWidgets.DrawPanelTitle(canvas, m.LeftMargin, m.RightMargin, ref y, "GAME ENVIRONMENT");

        bool hasInstallations = _scInstall.Installations.Count > 0;
        float selectorHeight = 32f;

        if (hasInstallations)
        {
            _scInstall.SelectorBounds = new SKRect(m.LeftMargin, y, m.RightMargin, y + selectorHeight);

            string installationText = _scInstall.SelectedInstallation < _scInstall.Installations.Count
                ? _scInstall.Installations[_scInstall.SelectedInstallation].DisplayName
                : "No SC found";

            bool selectorHovered = _scInstall.SelectorBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            FUIWidgets.DrawSelector(canvas, _scInstall.SelectorBounds, installationText, selectorHovered || _scInstall.DropdownOpen, true);
            y += selectorHeight + 6f; // +2px extra spacing from selector

            // Path + pencil "Manage" link on same line
            var installation = _scInstall.SelectedInstallation < _scInstall.Installations.Count
                ? _scInstall.Installations[_scInstall.SelectedInstallation]
                : null;

            const float detailFontSize = 12f;
            float manageTextWidth = FUIRenderer.MeasureText("Manage", detailFontSize);
            float pencilGap = 14f; // space for pencil icon
            float manageTotalWidth = pencilGap + manageTextWidth;
            float pathMaxWidth = m.RightMargin - m.LeftMargin - manageTotalWidth - 8f;

            float detailLineY = y + 11f; // text baseline for path and "Manage"

            if (installation is not null)
            {
                FUIRenderer.DrawTextTruncated(canvas, installation.InstallPath, new SKPoint(m.LeftMargin, detailLineY),
                    pathMaxWidth, FUIColors.TextDim, detailFontSize);
            }

            // Manage link bounds (pencil icon + text)
            float manageX = m.RightMargin - manageTotalWidth;
            _scInstall.BrowseBounds = new SKRect(manageX - 4f, y, m.RightMargin + 4f, y + 20f);
            _scInstall.BrowseHovered = _scInstall.BrowseBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            var manageColor = _scInstall.BrowseHovered ? FUIColors.TextBright : FUIColors.TextDim;

            // Draw pencil icon — vertically centred on the text line
            float pcx = manageX + 6f;
            float pcy = detailLineY - 4f; // centre pencil on text midpoint
            using (var penPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = manageColor,
                StrokeWidth = 1.2f,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round
            })
            {
                canvas.DrawLine(pcx - 4f, pcy + 4f, pcx + 3f, pcy - 3f, penPaint); // body
                canvas.DrawLine(pcx - 4f, pcy + 4f, pcx - 5f, pcy + 5.5f, penPaint); // tip
                canvas.DrawLine(pcx + 3f, pcy - 3f, pcx + 5f, pcy - 5f, penPaint); // top
            }

            // Draw "Manage" text
            FUIRenderer.DrawText(canvas, "Manage", new SKPoint(manageX + pencilGap, detailLineY), manageColor, detailFontSize);
        }
        else
        {
            _scInstall.SelectorBounds = SKRect.Empty;

            // Browse button + helper text
            float browseHeight = 28f;
            _scInstall.BrowseBounds = new SKRect(m.LeftMargin, y, m.RightMargin, y + browseHeight);
            var browseState = _scInstall.BrowseHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
            FUIRenderer.DrawButton(canvas, _scInstall.BrowseBounds, "SET STAR CITIZEN PATH", browseState);
            y += browseHeight + 4f;

            FUIRenderer.DrawText(canvas, "Star Citizen not found",
                new SKPoint(m.LeftMargin, y + 10f), FUIColors.TextDim, 12f);
        }
    }

    private void DrawSCInstallationDropdown(SKCanvas canvas)
    {
        float itemH = 28f;
        _scInstall.DropdownBounds = new SKRect(
            _scInstall.SelectorBounds.Left,
            _scInstall.SelectorBounds.Bottom + 2,
            _scInstall.SelectorBounds.Right,
            _scInstall.SelectorBounds.Bottom + 2 + Math.Min(_scInstall.Installations.Count * itemH + 8f, 200f));

        var items = _scInstall.Installations.Select(s => s.DisplayName).ToList();
        FUIWidgets.DrawDropdownPanel(canvas, _scInstall.DropdownBounds, items,
            _scInstall.SelectedInstallation, _scInstall.HoveredInstallation, itemH);
    }

    private static void DrawButtonCaptureToggle(SKCanvas canvas, SKRect bounds, bool active, bool hovered)
    {
        // Background
        var bgColor = active
            ? FUIColors.Active.WithAlpha(FUIColors.AlphaGlow)
            : hovered ? FUIColors.Background2.WithAlpha(180) : FUIColors.Background2.WithAlpha(100);
        var borderColor = active ? FUIColors.Active : (hovered ? FUIColors.FrameBright : FUIColors.Frame);
        FUIRenderer.DrawRoundedPanel(canvas, bounds, bgColor, borderColor, 4f);

        // Keycap icon: outer rounded square + smaller inner square (like a physical button)
        var iconColor = active ? FUIColors.Active : (hovered ? FUIColors.TextBright : FUIColors.TextDim);
        float cx = bounds.MidX;
        float cy = bounds.MidY;
        const float outerR = 5.5f;
        const float innerR = 3f;

        using var strokePaint = FUIRenderer.CreateStrokePaint(iconColor, 1.5f);
        using var fillPaint = FUIRenderer.CreateFillPaint(iconColor.WithAlpha(active ? (byte)180 : (byte)100));

        // Outer keycap border
        var outerRect = new SKRect(cx - outerR, cy - outerR, cx + outerR, cy + outerR);
        canvas.DrawRoundRect(outerRect, 2f, 2f, strokePaint);
        // Inner filled square (pressed indicator)
        var innerRect = new SKRect(cx - innerR, cy - innerR, cx + innerR, cy + innerR);
        canvas.DrawRoundRect(innerRect, 1f, 1f, fillPaint);
    }

    private void DrawStatusBanner(SKCanvas canvas, SKRect bounds)
    {
        if (string.IsNullOrEmpty(_scExportStatus)) return;

        var color = _scStatusKind switch
        {
            SCStatusKind.Success => FUIColors.Success,
            SCStatusKind.Error   => FUIColors.Danger,
            SCStatusKind.Warning => FUIColors.Warning,
            _                    => FUIColors.TextDim,
        };

        using var bgPaint4 = FUIRenderer.CreateFillPaint(color.WithAlpha(25));
        canvas.DrawRoundRect(bounds, 2f, 2f, bgPaint4);

        using var accentPaint = FUIRenderer.CreateFillPaint(color.WithAlpha(180));
        canvas.DrawRect(new SKRect(bounds.Left, bounds.Top, bounds.Left + 3f, bounds.Bottom), accentPaint);

        string statusText = FUIRenderer.TruncateText(_scExportStatus, bounds.Width - 16f, 13f);
        FUIRenderer.DrawTextCentered(canvas, statusText, bounds, color, 13f);
    }

    private SCActionBinding? FindBindingForCell(SCAction action, SCGridColumn col)
    {
        if (col.IsPhysical)
        {
            return _scExportProfile.Bindings.FirstOrDefault(b =>
                b.ActionMap == action.ActionMap && b.ActionName == action.ActionName &&
                b.DeviceType == SCDeviceType.Joystick &&
                b.PhysicalDeviceId == col.PhysicalDeviceKey);
        }
        if (col.IsJoystick)
        {
            return _scExportProfile.Bindings.FirstOrDefault(b =>
                b.ActionMap == action.ActionMap && b.ActionName == action.ActionName &&
                b.DeviceType == SCDeviceType.Joystick &&
                b.PhysicalDeviceId is null &&
                _scExportProfile.GetSCInstance(b.VJoyDevice) == col.SCInstance);
        }
        return _scExportProfile.GetBinding(action.ActionMap, action.ActionName);
    }

}