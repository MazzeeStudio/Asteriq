using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class SCBindingsTabController
{
    private void DrawSCExportPanelCompact(SKCanvas canvas, SKRect bounds, float frameInset,
        bool isExpanded = true, bool isCollapsible = false)
    {
        float y, leftMargin, rightMargin;

        if (isCollapsible)
        {
            bool headerHovered = !isExpanded
                && new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Top + FUIRenderer.PanelHeaderHeight)
                    .Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            var m = FUIWidgets.DrawCollapsiblePanelHeader(canvas, bounds, "CONTROL PROFILES",
                isExpanded, headerHovered, out var hdrBounds);
            _cpPanel.HeaderBounds = hdrBounds;
            if (!isExpanded) return;
            y = m.Y;
            leftMargin = m.LeftMargin;
            rightMargin = m.RightMargin;
        }
        else
        {
            _cpPanel.HeaderBounds = SKRect.Empty;
            var m = FUIRenderer.DrawPanelChrome(canvas, bounds);
            y = m.Y;
            leftMargin = m.LeftMargin;
            rightMargin = m.RightMargin;
            FUIWidgets.DrawPanelTitle(canvas, leftMargin, rightMargin, ref y, "CONTROL PROFILES");
        }

        float buttonGap = 6f;

        // Control Profile dropdown (full width)
        float dropdownHeight = FUIRenderer.SelectorHeight;
        _profileMgmt.DropdownBounds = new SKRect(leftMargin, y, rightMargin, y + dropdownHeight);
        bool dropdownHovered = _profileMgmt.DropdownBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        string dropdownLabel = string.IsNullOrEmpty(_scExportProfile.ProfileName)
            ? "— No Profile Selected —"
            : _scExportProfile.ProfileName;
        SCBindingsRenderer.DrawSCProfileDropdown(canvas, _profileMgmt.DropdownBounds, dropdownLabel, dropdownHovered, _profileMgmt.DropdownOpen);

        // Pencil edit icon inside dropdown box (left of arrow), visible on hover when a profile is loaded
        bool hasProfile = !string.IsNullOrEmpty(_scExportProfile.ProfileName);
        if (hasProfile && dropdownHovered && !_profileMgmt.DropdownOpen)
        {
            float editSize = 20f;
            float editX = _profileMgmt.DropdownBounds.Right - 28f - editSize;
            float editY = _profileMgmt.DropdownBounds.MidY - editSize / 2f;
            _profileMgmt.ProfileEditBounds = new SKRect(editX, editY, editX + editSize, editY + editSize);
            _profileMgmt.ProfileEditHovered = _profileMgmt.ProfileEditBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

            // Draw pencil icon
            var iconColor = FUIColors.InteractiveColor(_profileMgmt.ProfileEditHovered);
            float cx = _profileMgmt.ProfileEditBounds.MidX;
            float cy = _profileMgmt.ProfileEditBounds.MidY;
            using var penPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = iconColor,
                StrokeWidth = 1.2f,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round
            };
            // Pencil body (diagonal line)
            canvas.DrawLine(cx - 4f, cy + 4f, cx + 3f, cy - 3f, penPaint);
            // Pencil tip
            canvas.DrawLine(cx - 4f, cy + 4f, cx - 5f, cy + 5.5f, penPaint);
            // Pencil top
            canvas.DrawLine(cx + 3f, cy - 3f, cx + 5f, cy - 5f, penPaint);
        }
        else
        {
            _profileMgmt.ProfileEditBounds = SKRect.Empty;
            _profileMgmt.ProfileEditHovered = false;
        }

        y += dropdownHeight + 6f;

        // Buttons row: [Import]  [+ New] — all text-width sized, right-aligned
        const float textBtnPad = 16f;
        float textBtnHeight = FUIRenderer.TouchTargetMinHeight;  // 24px minimum
        float newBtnWidth = FUIRenderer.MeasureText("+ New", 14f) + textBtnPad;
        float importBtnWidth = FUIRenderer.MeasureText("Import", 14f) + textBtnPad;

        // New button (rightmost)
        float newBtnX = rightMargin - newBtnWidth;
        _profileMgmt.NewProfileBounds = new SKRect(newBtnX, y, newBtnX + newBtnWidth, y + textBtnHeight);
        _profileMgmt.NewProfileHovered = _profileMgmt.NewProfileBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIRenderer.DrawButton(canvas, _profileMgmt.NewProfileBounds, "+ New",
            _profileMgmt.NewProfileHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

        // Import button (left of + New)
        float importBtnX = newBtnX - buttonGap - importBtnWidth;
        _profileMgmt.ImportProfileBounds = new SKRect(importBtnX, y, importBtnX + importBtnWidth, y + textBtnHeight);
        _profileMgmt.ImportProfileHovered = _profileMgmt.ImportProfileBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIRenderer.DrawButton(canvas, _profileMgmt.ImportProfileBounds, "Import",
            _profileMgmt.ImportProfileHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

        // Compute profile dropdown list bounds so the draw-last pass can render it on top of all panels
        if (_profileMgmt.DropdownOpen)
        {
            int asteriqCount = _profileMgmt.ExportProfiles.Count;
            int scFileCount  = _scAvailableProfiles.Count;
            int remoteCount  = _ctx.RemoteControlProfiles.Count;
            // Accurate height: 8px padding + rows (24px each) + per-section header (16px for sec1, 26px for others: 4+6+16)
            const float sec1HeaderH = 16f;
            const float sepHeaderH  = 26f;
            const float emptyFolderH = 88f; // 20px pad + 48px icon + 20px pad
            float savedProfilesH = sec1HeaderH + (asteriqCount > 0 ? asteriqCount * 24f : 20f); // 20f for "No saved profiles" text
            float scSectionH = scFileCount > 0 ? sepHeaderH + scFileCount * 24f : sepHeaderH + emptyFolderH;
            float listHeight = 8f
                + savedProfilesH
                + scSectionH
                + (remoteCount > 0 ? sepHeaderH + remoteCount * 24f : 0f);
            listHeight = Math.Min(Math.Max(listHeight, 36f), 280f);
            _profileMgmt.DropdownListBounds = new SKRect(leftMargin, _profileMgmt.DropdownBounds.Bottom + 4, rightMargin, _profileMgmt.DropdownBounds.Bottom + 4 + listHeight);
        }

        // Button group — built bottom-up so every button has a consistent anchor
        float buttonHeight = 32f;
        float smallBtnHeight = 24f;
        float smallBtnWidth = (rightMargin - leftMargin - FUIRenderer.SpaceSM) / 2;

        float exportBtnY  = bounds.Bottom - frameInset - FUIRenderer.SpaceLG - buttonHeight;
        float clearBtnY   = exportBtnY - FUIRenderer.SpaceSM - smallBtnHeight;
        float statusBannerY = clearBtnY - FUIRenderer.SpaceSM - 24f;

        // CLEAR ALL / RESET DFLTS
        _scClearAllButtonBounds = new SKRect(leftMargin, clearBtnY, leftMargin + smallBtnWidth, clearBtnY + smallBtnHeight);
        _scClearAllButtonHovered = _scClearAllButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        bool hasBoundActions = _scExportProfile.Bindings.Count > 0;
        FUIRenderer.DrawButton(canvas, _scClearAllButtonBounds, "CLEAR ALL",
            !hasBoundActions ? FUIRenderer.ButtonState.Disabled
            : (_scClearAllButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal),
            isDanger: true);

        _scResetDefaultsButtonBounds = new SKRect(leftMargin + smallBtnWidth + FUIRenderer.SpaceSM, clearBtnY, rightMargin, clearBtnY + smallBtnHeight);
        _scResetDefaultsButtonHovered = _scResetDefaultsButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIRenderer.DrawButton(canvas, _scResetDefaultsButtonBounds, "RESET DFLTS",
            _scResetDefaultsButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal,
            isDanger: true);

        // EXPORT TO SC — primary CTA, anchored to panel bottom
        _scExportButtonBounds = new SKRect(leftMargin, exportBtnY, rightMargin, exportBtnY + buttonHeight);
        _scExportButtonHovered = _scExportButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        bool canExport = _scInstall.Installations.Count > 0 && _conflicts.DuplicateActionBindings.Count == 0;
        FUIRenderer.DrawButton(canvas, _scExportButtonBounds, "EXPORT TO SC",
            !canExport ? FUIRenderer.ButtonState.Disabled : (_scExportButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal));

        // Status banner — appears above the button group so it is never clipped
        if (!string.IsNullOrEmpty(_scExportStatus))
        {
            var elapsed = DateTime.Now - _scExportStatusTime;
            if (elapsed.TotalSeconds < 10)
                DrawStatusBanner(canvas, new SKRect(leftMargin, statusBannerY, rightMargin, statusBannerY + 24f));
            else
                _scExportStatus = null;
        }
    }


    private void DrawSCActionMapFilterDropdown(SKCanvas canvas)
    {
        float itemHeight = 24f;
        // items[0] = "All Categories", items[1..] = action map names
        var items = new List<string> { "All Categories" };
        items.AddRange(_searchFilter.ActionMaps);

        float totalContentHeight = items.Count * itemHeight + 4;
        float maxDropdownHeight = 300f;
        float dropdownHeight = Math.Min(totalContentHeight, maxDropdownHeight);
        bool needsScroll = totalContentHeight > maxDropdownHeight;
        float scrollbarWidth = needsScroll ? 8f : 0f;

        _searchFilter.FilterMaxScroll = Math.Max(0, totalContentHeight - dropdownHeight);
        _searchFilter.FilterScrollOffset = Math.Clamp(_searchFilter.FilterScrollOffset, 0, _searchFilter.FilterMaxScroll);

        _searchFilter.FilterDropdownBounds = new SKRect(
            _searchFilter.FilterBounds.Right - _searchFilter.FilterBounds.Width,
            _searchFilter.FilterBounds.Bottom + 2,
            _searchFilter.FilterBounds.Right,
            _searchFilter.FilterBounds.Bottom + 2 + dropdownHeight);

        // Map hover: _searchFilter.HoveredFilter == -1 means "All Categories" row OR nothing.
        // Disambiguate using mouse position against the first item's Y range.
        float firstItemTop = _searchFilter.FilterDropdownBounds.Top + 2 - _searchFilter.FilterScrollOffset;
        bool allCatHovered = _searchFilter.HoveredFilter == -1
            && _ctx.MousePosition.Y >= firstItemTop
            && _ctx.MousePosition.Y < firstItemTop + itemHeight;
        int hoveredIdx = allCatHovered ? 0
            : _searchFilter.HoveredFilter >= 0 ? _searchFilter.HoveredFilter + 1
            : -1;
        int selectedIdx = string.IsNullOrEmpty(_searchFilter.ActionMapFilter) ? 0
            : _searchFilter.ActionMaps.IndexOf(_searchFilter.ActionMapFilter) + 1;

        FUIWidgets.DrawDropdownPanel(canvas, _searchFilter.FilterDropdownBounds, items,
            selectedIdx, hoveredIdx, itemHeight, _searchFilter.FilterScrollOffset, scrollbarWidth);

        // Scrollbar (drawn on top of the panel)
        if (needsScroll)
        {
            float scrollTrackX = _searchFilter.FilterDropdownBounds.Right - scrollbarWidth - 2;
            float scrollTrackY = _searchFilter.FilterDropdownBounds.Top + 2;
            float scrollTrackHeight = dropdownHeight - 4;

            using var trackPaint = FUIRenderer.CreateFillPaint(FUIColors.Background2.WithAlpha(80));
            using var scrollTrack = new SKRoundRect(new SKRect(scrollTrackX, scrollTrackY, scrollTrackX + scrollbarWidth, scrollTrackY + scrollTrackHeight), 2f);
            canvas.DrawRoundRect(scrollTrack, trackPaint);

            float thumbHeight = Math.Max(20f, scrollTrackHeight * (dropdownHeight / totalContentHeight));
            float thumbY = scrollTrackY + (_searchFilter.FilterScrollOffset / _searchFilter.FilterMaxScroll) * (scrollTrackHeight - thumbHeight);
            using var thumbPaint = FUIRenderer.CreateFillPaint(FUIColors.TextDim.WithAlpha(150));
            using var scrollThumb = new SKRoundRect(new SKRect(scrollTrackX, thumbY, scrollTrackX + scrollbarWidth, thumbY + thumbHeight), 2f);
            canvas.DrawRoundRect(scrollThumb, thumbPaint);
        }
    }

}
