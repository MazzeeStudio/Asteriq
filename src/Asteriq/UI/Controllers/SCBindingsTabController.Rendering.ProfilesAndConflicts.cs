using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class SCBindingsTabController
{
    private void DrawSCProfileDropdownList(SKCanvas canvas, SKRect bounds)
    {
        // Drop shadow with glow effect (FUI style)
        FUIRenderer.DrawPanelShadow(canvas, bounds, 4f, 4f, 15f);

        // Outer glow (subtle)
        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.ActiveLight,
            StrokeWidth = 3f,
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(4f, 4f)
        };
        canvas.DrawRect(bounds, glowPaint);

        // Solid opaque background
        using var bgPaint5 = FUIRenderer.CreateFillPaint(FUIColors.Void);
        canvas.DrawRect(bounds, bgPaint5);

        // Inner background
        using var innerBgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background0);
        canvas.DrawRect(bounds.Inset(2, 2), innerBgPaint);

        // L-corner frame (FUI style)
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.ActiveStrong, 20f, 6f, 1.5f, true);

        // Items
        float rowHeight = 24f;
        float y = bounds.Top + 8;
        _profileMgmt.HoveredProfileIndex = -1;
        _profileMgmt.DropdownDeleteProfileName = "";
        _profileMgmt.DropdownDeleteSCFilePath = "";
        _profileMgmt.DropdownDeleteSCDisplayName = "";

        // Section 1: Asteriq saved profiles (always shown)
        FUIRenderer.DrawText(canvas, "SAVED PROFILES", new SKPoint(bounds.Left + 10, y + 9f), FUIColors.TextDim, 12f, true);
        y += 16f;

        if (_profileMgmt.ExportProfiles.Count == 0)
        {
            FUIRenderer.DrawText(canvas, "No saved profiles", new SKPoint(bounds.Left + 14, y + 12f), FUIColors.TextDim, 11f);
            y += 20f;
        }

        for (int i = 0; i < _profileMgmt.ExportProfiles.Count && y + rowHeight <= bounds.Bottom; i++)
        {
            var profile = _profileMgmt.ExportProfiles[i];
            bool isActive  = profile.ProfileName == _scExportProfile.ProfileName;
            var rowBounds  = new SKRect(bounds.Left + 4, y, bounds.Right - 4, y + rowHeight);
            bool isHovered = rowBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

            // Active profile: always show accent bar (even when not hovered)
            if (isActive)
            {
                using var activeAccentPaint = FUIRenderer.CreateFillPaint(FUIColors.ActiveStrong);
                canvas.DrawRect(new SKRect(rowBounds.Left, rowBounds.Top + 2, rowBounds.Left + 2, rowBounds.Bottom - 2), activeAccentPaint);
            }

            // FUI hover style
            if (isHovered)
            {
                _profileMgmt.HoveredProfileIndex = i;
                using var hoverPaint = FUIRenderer.CreateFillPaint(FUIColors.SelectionBg);
                canvas.DrawRect(rowBounds, hoverPaint);
                if (!isActive)
                {
                    using var accentPaint = FUIRenderer.CreateFillPaint(FUIColors.Active);
                    canvas.DrawRect(new SKRect(rowBounds.Left, rowBounds.Top + 2, rowBounds.Left + 2, rowBounds.Bottom - 2), accentPaint);
                }

                // Delete (×) button — shown on hover for all profiles including active
                _profileMgmt.DropdownDeleteBounds = new SKRect(rowBounds.Right - 22, rowBounds.Top + 4, rowBounds.Right - 4, rowBounds.Bottom - 4);
                _profileMgmt.DropdownDeleteProfileName = profile.ProfileName;
                bool delHovered = _profileMgmt.DropdownDeleteBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
                FUIRenderer.DrawText(canvas, "×", new SKPoint(_profileMgmt.DropdownDeleteBounds.MidX - 3f, _profileMgmt.DropdownDeleteBounds.MidY + 4f),
                    FUIColors.SecondaryColor(delHovered), 14f);
            }

            var textColor = isHovered ? FUIColors.TextBright : (isActive ? FUIColors.Active : FUIColors.TextPrimary);
            float maxTextWidth = rowBounds.Width - (isHovered ? 56f : 40f); // extra room for × on hover
            string displayName = FUIWidgets.TruncateTextToWidth(profile.ProfileName, maxTextWidth, 10f);
            FUIRenderer.DrawText(canvas, displayName, new SKPoint(rowBounds.Left + 10, rowBounds.MidY + 4f), textColor, 13f);

            // Binding count badge
            if (profile.BindingCount > 0)
            {
                string countStr = profile.BindingCount.ToString();
                float badgeX = rowBounds.Right - (isHovered ? 28f : 8f) - FUIRenderer.MeasureText(countStr, 12f);
                FUIRenderer.DrawText(canvas, countStr, new SKPoint(badgeX, rowBounds.MidY + 3f), FUIColors.TextDim, 12f);
            }

            y += rowHeight;
        }

        // Section 2: SC mapping files from mappings folder (always shown with separator)
        {
            // Separator line (FUI style)
            y += 4f;
            float sepY = y;
            using var sepPaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame);
            canvas.DrawLine(bounds.Left + 12, sepY, bounds.Right - 12, sepY, sepPaint);

            // Corner accents on separator
            using var accentLinePaint = FUIRenderer.CreateStrokePaint(FUIColors.ActiveSubtle);
            canvas.DrawLine(bounds.Left + 8, sepY, bounds.Left + 12, sepY, accentLinePaint);
            canvas.DrawLine(bounds.Right - 12, sepY, bounds.Right - 8, sepY, accentLinePaint);

            y += 6f;

            // Section label
            FUIRenderer.DrawText(canvas, "IMPORT FROM SC", new SKPoint(bounds.Left + 10, y + 9f), FUIColors.TextDim, 12f, true);
            y += 16f;

            if (_scAvailableProfiles.Count > 0)
            {
                // SC mapping files
                int scFileIndexOffset = _profileMgmt.ExportProfiles.Count + 1000; // Use offset to distinguish from Asteriq profiles
                for (int i = 0; i < _scAvailableProfiles.Count && y + rowHeight <= bounds.Bottom; i++)
                {
                    var scFile = _scAvailableProfiles[i];
                    var rowBounds = new SKRect(bounds.Left + 4, y, bounds.Right - 4, y + rowHeight);
                    bool isHovered = rowBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

                    if (isHovered)
                    {
                        _profileMgmt.HoveredProfileIndex = scFileIndexOffset + i;
                        using var hoverPaint = FUIRenderer.CreateFillPaint(FUIColors.SelectionBg);
                        canvas.DrawRect(rowBounds, hoverPaint);
                        using var accentPaint = FUIRenderer.CreateFillPaint(FUIColors.Active);
                        canvas.DrawRect(new SKRect(rowBounds.Left, rowBounds.Top + 2, rowBounds.Left + 2, rowBounds.Bottom - 2), accentPaint);

                        // Delete (×) button — shown on hover, same pattern as saved profiles
                        _profileMgmt.DropdownDeleteBounds = new SKRect(rowBounds.Right - 22, rowBounds.Top + 4, rowBounds.Right - 4, rowBounds.Bottom - 4);
                        _profileMgmt.DropdownDeleteSCFilePath = scFile.FilePath;
                        _profileMgmt.DropdownDeleteSCDisplayName = scFile.DisplayName;
                        bool delHovered = _profileMgmt.DropdownDeleteBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
                        FUIRenderer.DrawText(canvas, "×", new SKPoint(_profileMgmt.DropdownDeleteBounds.MidX - 3f, _profileMgmt.DropdownDeleteBounds.MidY + 4f),
                            FUIColors.SecondaryColor(delHovered), 14f);
                    }

                    var textColor = isHovered ? FUIColors.TextBright : FUIColors.TextPrimary;
                    float maxTextWidth = rowBounds.Width - (isHovered ? 40f : 20f); // extra room for × on hover
                    string displayName = scFile.DisplayName;
                    displayName = FUIWidgets.TruncateTextToWidth(displayName, maxTextWidth, 10f);
                    FUIRenderer.DrawText(canvas, displayName, new SKPoint(rowBounds.Left + 10, rowBounds.MidY + 4f), textColor, 13f);

                    y += rowHeight;
                }
            }
            else
            {
                // Empty state: FUI folder icon centred in the section
                float iconW = 72f;
                float iconH = 48f;
                float iconX = bounds.MidX - iconW / 2f;
                float iconY = y + 20f;
                var iconBounds = new SKRect(iconX, iconY, iconX + iconW, iconY + iconH);
                FUIWidgets.DrawFUIFolderIcon(canvas, iconBounds, FUIColors.FrameDim.WithAlpha(180), FUIColors.Active.WithAlpha(90));
                y += 20f + iconH + 20f; // 20px pad top + icon + 20px pad bottom
            }
        }

        // Section 3: Remote SC control profiles received from TX master
        var remoteProfiles = _ctx.RemoteControlProfiles;
        if (remoteProfiles.Count > 0 && y + rowHeight <= bounds.Bottom)
        {
            // Separator (same style as Section 2)
            y += 4f;
            float sepY2 = y;
            using var sepPaint2 = FUIRenderer.CreateStrokePaint(FUIColors.Frame);
            canvas.DrawLine(bounds.Left + 12, sepY2, bounds.Right - 12, sepY2, sepPaint2);
            using var accentLinePaint2 = FUIRenderer.CreateStrokePaint(FUIColors.ActiveSubtle);
            canvas.DrawLine(bounds.Left + 8, sepY2, bounds.Left + 12, sepY2, accentLinePaint2);
            canvas.DrawLine(bounds.Right - 12, sepY2, bounds.Right - 8, sepY2, accentLinePaint2);
            y += 6f;

            string masterLabel = string.IsNullOrEmpty(_ctx.RemoteControlProfilesMasterName)
                ? "FROM MASTER"
                : $"FROM {_ctx.RemoteControlProfilesMasterName.ToUpperInvariant()}";
            FUIRenderer.DrawText(canvas, masterLabel, new SKPoint(bounds.Left + 10, y + 9f), FUIColors.TextDim, 12f, true);
            y += 16f;

            int remoteIndexOffset = _profileMgmt.ExportProfiles.Count + 2000;
            for (int i = 0; i < remoteProfiles.Count && y + rowHeight <= bounds.Bottom; i++)
            {
                var (remoteName, _) = remoteProfiles[i];
                var rowBounds = new SKRect(bounds.Left + 4, y, bounds.Right - 4, y + rowHeight);
                bool isHovered = rowBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

                if (isHovered)
                {
                    _profileMgmt.HoveredProfileIndex = remoteIndexOffset + i;
                    using var hoverPaint = FUIRenderer.CreateFillPaint(FUIColors.SelectionBg);
                    canvas.DrawRect(rowBounds, hoverPaint);
                    using var accentPaint = FUIRenderer.CreateFillPaint(FUIColors.Active);
                    canvas.DrawRect(new SKRect(rowBounds.Left, rowBounds.Top + 2, rowBounds.Left + 2, rowBounds.Bottom - 2), accentPaint);
                }

                var textColor = isHovered ? FUIColors.TextBright : FUIColors.TextPrimary;
                string displayName = FUIWidgets.TruncateTextToWidth(remoteName, rowBounds.Width - 20f, 10f);
                FUIRenderer.DrawText(canvas, displayName, new SKPoint(rowBounds.Left + 10, rowBounds.MidY + 4f), textColor, 13f);

                y += rowHeight;
            }
        }
    }

    /// <summary>
    /// Returns the physical device name for a vJoy column, or null if not mapped.
    /// </summary>
    private void DrawConflictLinksPanel(SKCanvas canvas, float left, float right, float top, float maxBottom)
    {
        // Available height — cap at 4 rows so we don't blow the layout on large conflict lists
        const float rowH = 22f;
        const float padV = 8f;
        const float padH = 10f;
        const float headerH = 20f;

        int maxVisible = (int)Math.Max(1, Math.Min(_conflicts.ConflictLinks.Count, (maxBottom - top - headerH - padV * 2) / rowH));
        float boxH = padV + headerH + maxVisible * rowH + padV;

        if (top + boxH > maxBottom)
            return; // not enough room — skip

        var boxBounds = new SKRect(left, top, right, top + boxH);

        // Amber glow background + border
        using var glowFilter = SKImageFilter.CreateBlur(6f, 6f);
        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Warning.WithAlpha(60),
            StrokeWidth = 3f,
            IsAntialias = true,
            ImageFilter = glowFilter
        };
        canvas.DrawRoundRect(boxBounds, 4, 4, glowPaint);

        FUIRenderer.DrawRoundedPanel(canvas, boxBounds, FUIColors.Warning.WithAlpha(20), FUIColors.Warning.WithAlpha(160), 4f);

        // Header
        float y = top + padV;
        FUIRenderer.DrawText(canvas, "ALSO BOUND TO THIS INPUT:", new SKPoint(left + padH, y + 13f), FUIColors.Warning, 10f, true);
        y += headerH;

        // Resize bounds list for click detection
        while (_conflicts.ConflictLinkBounds.Count < _conflicts.ConflictLinks.Count)
            _conflicts.ConflictLinkBounds.Add(SKRect.Empty);
        for (int i = 0; i < _conflicts.ConflictLinkBounds.Count; i++)
            _conflicts.ConflictLinkBounds[i] = SKRect.Empty;

        // Link rows
        for (int i = 0; i < maxVisible; i++)
        {
            var (map, name) = _conflicts.ConflictLinks[i];
            string category = SCCategoryMapper.GetCategoryNameForAction(map, name);
            string formatted = SCCategoryMapper.FormatActionName(name);
            string label = $"{category} > {formatted}";

            var rowBounds = new SKRect(left + padH / 2, y, right - padH / 2, y + rowH);
            _conflicts.ConflictLinkBounds[i] = rowBounds;

            bool hovered = i == _conflicts.ConflictLinkHovered;
            if (hovered)
            {
                using var hoverPaint = FUIRenderer.CreateFillPaint(FUIColors.WarningTint);
                canvas.DrawRoundRect(rowBounds, 2, 2, hoverPaint);
            }

            string truncated = FUIWidgets.TruncateTextToWidth(label, right - left - padH * 2, 10f);
            byte linkAlpha = hovered ? (byte)255 : (byte)180;
            FUIRenderer.DrawText(canvas, truncated, new SKPoint(left + padH, y + rowH / 2 + 4f), FUIColors.Warning.WithAlpha(linkAlpha), 10f);

            y += rowH;
        }

        // "and N more" if truncated
        if (_conflicts.ConflictLinks.Count > maxVisible)
        {
            int remaining = _conflicts.ConflictLinks.Count - maxVisible;
            FUIRenderer.DrawText(canvas, $"and {remaining} more…", new SKPoint(left + padH, y + 12f),
                FUIColors.TextDimSubtle, 10f);
        }
    }

}