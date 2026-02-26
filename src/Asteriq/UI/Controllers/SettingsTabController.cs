using System.Reflection;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public class SettingsTabController : ITabController
{
    private readonly TabContext _ctx;

    // Profile action button bounds
    private SKRect _newProfileButtonBounds;
    private SKRect _duplicateProfileButtonBounds;
    private SKRect _importProfileButtonBounds;
    private SKRect _exportProfileButtonBounds;
    private SKRect _deleteProfileButtonBounds;

    // Theme selector state
    private SKRect[] _themeButtonBounds = new SKRect[12];

    // Font size stepper ([-] value [+]) — index 0 = minus, 1 = plus
    private SKRect[] _fontSizeButtonBounds = new SKRect[2];
    private SKRect[] _fontFamilyButtonBounds = new SKRect[2];

    // Background settings slider bounds
    private SKRect _bgGridSliderBounds;
    private SKRect _bgGlowSliderBounds;
    private SKRect _bgNoiseSliderBounds;
    private SKRect _bgScanlineSliderBounds;
    private SKRect _bgVignetteSliderBounds;
    private SKRect _autoLoadToggleBounds;
    private SKRect _closeToTrayToggleBounds;
    private SKRect _checkUpdatesToggleBounds;
    private SKRect[] _trayIconTypeButtonBounds = new SKRect[2];
    private string? _draggingBgSlider;

    // Support panel button bounds
    private SKRect _bmacButtonBounds;
    private SKRect _referralCopyButtonBounds;
    private SKRect _referralLinkButtonBounds;

    // Profile name edit
    private SKRect _profileNameBounds;
    private SKRect _profileNameEditBounds;
    private bool _profileNameEditHovered;

    // Driver setup button
    private SKRect _driverSetupButtonBounds;

    // Version / update button bounds
    private SKRect _checkButtonBounds;
    private SKRect _downloadButtonBounds;
    private SKRect _applyButtonBounds;

    public SettingsTabController(TabContext ctx)
    {
        _ctx = ctx;
    }

    public void Draw(SKCanvas canvas, SKRect bounds, float padLeft, float contentTop, float contentBottom)
    {
        float frameInset = FUIRenderer.FrameInset;
        var contentBounds = new SKRect(padLeft, contentTop, bounds.Right - padLeft, contentBottom);

        const float supportPanelHeight = 100f;
        float supportPanelSep = FUIRenderer.SpaceLG;
        float topAreaBottom = contentBounds.Bottom - supportPanelHeight - supportPanelSep;
        var supportBounds = new SKRect(contentBounds.Left, topAreaBottom + supportPanelSep, contentBounds.Right, contentBounds.Bottom);

        // Three-panel layout: Profile Management | System | Visual (each ~1/3 width)
        float panelGap = FUIRenderer.SpaceLG;
        float availableWidth = contentBounds.Width - panelGap * 2;
        float colWidth = availableWidth / 3f;

        var leftBounds = new SKRect(contentBounds.Left, contentBounds.Top,
            contentBounds.Left + colWidth, topAreaBottom);
        var centerBounds = new SKRect(leftBounds.Right + panelGap, contentBounds.Top,
            leftBounds.Right + panelGap + colWidth, topAreaBottom);
        var rightBounds = new SKRect(centerBounds.Right + panelGap, contentBounds.Top,
            contentBounds.Right, topAreaBottom);

        DrawProfileManagementPanel(canvas, leftBounds, frameInset);
        DrawSystemSettingsSubPanel(canvas, centerBounds, frameInset);
        DrawVisualSettingsSubPanel(canvas, rightBounds, frameInset);
        DrawSupportPanel(canvas, supportBounds, frameInset);
    }

    public void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        HandleSettingsTabClick(new SKPoint(e.X, e.Y));
    }

    public void OnMouseMove(MouseEventArgs e)
    {
        if (_draggingBgSlider is not null)
        {
            UpdateBgSliderFromPoint(e.X);
            return;
        }

        var pt = new SKPoint(e.X, e.Y);

        // Profile name box (rename on click)
        if (_profileNameBounds.Contains(pt) && _ctx.ProfileManager.ActiveProfile is not null)
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }

        // Profile action buttons
        if (_newProfileButtonBounds.Contains(pt) ||
            (_duplicateProfileButtonBounds.Contains(pt) && _ctx.ProfileManager.ActiveProfile is not null) ||
            _importProfileButtonBounds.Contains(pt) ||
            (_exportProfileButtonBounds.Contains(pt) && _ctx.ProfileManager.ActiveProfile is not null) ||
            (_deleteProfileButtonBounds != default && _deleteProfileButtonBounds.Contains(pt) && _ctx.ProfileManager.ActiveProfile is not null))
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }

        // Theme buttons
        foreach (var b in _themeButtonBounds)
        {
            if (!b.IsEmpty && b.Contains(pt)) { _ctx.OwnerForm.Cursor = Cursors.Hand; return; }
        }

        // Font size stepper buttons
        foreach (var b in _fontSizeButtonBounds)
        {
            if (!b.IsEmpty && b.Contains(pt)) { _ctx.OwnerForm.Cursor = Cursors.Hand; return; }
        }

        // Font family buttons
        foreach (var b in _fontFamilyButtonBounds)
        {
            if (!b.IsEmpty && b.Contains(pt)) { _ctx.OwnerForm.Cursor = Cursors.Hand; return; }
        }

        // Tray icon type buttons
        foreach (var b in _trayIconTypeButtonBounds)
        {
            if (!b.IsEmpty && b.Contains(pt)) { _ctx.OwnerForm.Cursor = Cursors.Hand; return; }
        }

        // Toggles
        if (_autoLoadToggleBounds.Contains(pt) || _closeToTrayToggleBounds.Contains(pt) || _checkUpdatesToggleBounds.Contains(pt))
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }

        // Background sliders
        if (_bgGridSliderBounds.Contains(pt) || _bgGlowSliderBounds.Contains(pt) ||
            _bgNoiseSliderBounds.Contains(pt) || _bgScanlineSliderBounds.Contains(pt) ||
            _bgVignetteSliderBounds.Contains(pt))
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }

        // Support panel buttons
        if (_bmacButtonBounds.Contains(pt) || _referralCopyButtonBounds.Contains(pt) || _referralLinkButtonBounds.Contains(pt))
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }

        // Driver setup button
        if (!_driverSetupButtonBounds.IsEmpty && _driverSetupButtonBounds.Contains(pt))
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }

        // Update section buttons
        if ((!_checkButtonBounds.IsEmpty && _checkButtonBounds.Contains(pt))
            || (!_downloadButtonBounds.IsEmpty && _downloadButtonBounds.Contains(pt))
            || (!_applyButtonBounds.IsEmpty && _applyButtonBounds.Contains(pt)))
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
        }
    }

    public void OnMouseUp(MouseEventArgs e)
    {
        if (_draggingBgSlider is not null)
        {
            _draggingBgSlider = null;
            SaveBackgroundSettings();
            _ctx.MarkDirty();
        }
    }

    public void OnMouseWheel(MouseEventArgs e) { }
    public bool ProcessCmdKey(ref Message msg, Keys keyData) => false;
    public void OnMouseLeave() { }
    public void OnTick() { }
    public void OnActivated() { }
    public void OnDeactivated() { }

    public bool IsDraggingSlider => _draggingBgSlider is not null;

    #region Drawing

    private void DrawProfileManagementPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        var metrics = FUIRenderer.DrawPanelChrome(canvas, bounds);
        float y = metrics.Y;
        float leftMargin = metrics.LeftMargin;
        float rightMargin = metrics.RightMargin;
        float bottom = bounds.Bottom - frameInset - FUIRenderer.SpaceLG;

        canvas.Save();
        canvas.ClipRect(new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom - frameInset));

        y = FUIRenderer.DrawPanelHeader(canvas, "PROFILE MANAGEMENT", leftMargin, y);

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is not null)
        {
            y = FUIRenderer.DrawSectionHeader(canvas, "ACTIVE PROFILE", leftMargin, y);

            float nameBoxHeight = 32f;
            _profileNameBounds = new SKRect(leftMargin, y, rightMargin, y + nameBoxHeight);
            bool nameHovered = _profileNameBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

            using var nameBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(30) };
            canvas.DrawRoundRect(_profileNameBounds, 4, 4, nameBgPaint);

            var nameFrameColor = nameHovered ? FUIColors.Active : FUIColors.Active;
            using var nameFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = nameFrameColor, StrokeWidth = 1f };
            canvas.DrawRoundRect(_profileNameBounds, 4, 4, nameFramePaint);

            float nameTextY = y + (nameBoxHeight - FUIRenderer.FontBody) / 2 + FUIRenderer.FontBody - 3;
            FUIRenderer.DrawText(canvas, profile.Name, new SKPoint(leftMargin + 10, nameTextY), FUIColors.TextBright, FUIRenderer.FontBody, true);

            // Pencil edit icon on hover (right side of name box)
            if (nameHovered)
            {
                float editSize = 20f;
                float editX = _profileNameBounds.Right - editSize - 6f;
                float editY = _profileNameBounds.MidY - editSize / 2f;
                _profileNameEditBounds = new SKRect(editX, editY, editX + editSize, editY + editSize);
                _profileNameEditHovered = _profileNameEditBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

                var iconColor = _profileNameEditHovered ? FUIColors.Active : FUIColors.TextDim;
                float cx = _profileNameEditBounds.MidX;
                float cy = _profileNameEditBounds.MidY;
                using var penPaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = iconColor,
                    StrokeWidth = 1.2f,
                    IsAntialias = true,
                    StrokeCap = SKStrokeCap.Round
                };
                canvas.DrawLine(cx - 4f, cy + 4f, cx + 3f, cy - 3f, penPaint);
                canvas.DrawLine(cx - 4f, cy + 4f, cx - 5f, cy + 5.5f, penPaint);
                canvas.DrawLine(cx + 3f, cy - 3f, cx + 5f, cy - 5f, penPaint);
            }
            else
            {
                _profileNameEditBounds = SKRect.Empty;
                _profileNameEditHovered = false;
            }

            y += nameBoxHeight + 24f;

            float lineHeight = metrics.RowHeight;
            y = FUIRenderer.DrawSectionHeader(canvas, "STATISTICS", leftMargin, y);

            FUIWidgets.DrawProfileStat(canvas, leftMargin, y, "Axis Mappings", profile.AxisMappings.Count.ToString());
            y += lineHeight;
            FUIWidgets.DrawProfileStat(canvas, leftMargin, y, "Button Mappings", profile.ButtonMappings.Count.ToString());
            y += lineHeight;
            FUIWidgets.DrawProfileStat(canvas, leftMargin, y, "Hat Mappings", profile.HatMappings.Count.ToString());
            y += lineHeight;
            FUIWidgets.DrawProfileStat(canvas, leftMargin, y, "Shift Layers", profile.ShiftLayers.Count.ToString());
            y += lineHeight + 6f;

            FUIWidgets.DrawProfileStat(canvas, leftMargin, y, "Created", profile.CreatedAt.ToLocalTime().ToString("g"));
            y += lineHeight;
            FUIWidgets.DrawProfileStat(canvas, leftMargin, y, "Modified", profile.ModifiedAt.ToLocalTime().ToString("g"));
            y += lineHeight + 10f;
        }
        else
        {
            FUIRenderer.DrawText(canvas, "No profile active", new SKPoint(leftMargin, y), FUIColors.TextDim, 15f);
            y += 40f;
        }

        y = FUIRenderer.DrawSectionHeader(canvas, "ACTIONS", leftMargin, y);

        float buttonHeight = 28f;
        float buttonGap = FUIRenderer.SpaceSM;
        float buttonWidth = (metrics.ContentWidth - buttonGap) / 2;

        _newProfileButtonBounds = new SKRect(leftMargin, y, leftMargin + buttonWidth, y + buttonHeight);
        _duplicateProfileButtonBounds = new SKRect(rightMargin - buttonWidth, y, rightMargin, y + buttonHeight);
        FUIWidgets.DrawSettingsButton(canvas, _newProfileButtonBounds, "New Profile", false);
        FUIWidgets.DrawSettingsButton(canvas, _duplicateProfileButtonBounds,
            profile is not null ? "Duplicate" : "---", profile is null);
        y += buttonHeight + buttonGap;

        _importProfileButtonBounds = new SKRect(leftMargin, y, leftMargin + buttonWidth, y + buttonHeight);
        _exportProfileButtonBounds = new SKRect(rightMargin - buttonWidth, y, rightMargin, y + buttonHeight);
        FUIWidgets.DrawSettingsButton(canvas, _importProfileButtonBounds, "Import", false);
        FUIWidgets.DrawSettingsButton(canvas, _exportProfileButtonBounds,
            profile is not null ? "Export" : "---", profile is null);
        y += buttonHeight + buttonGap;

        if (profile is not null && y + buttonHeight <= bottom)
        {
            _deleteProfileButtonBounds = new SKRect(leftMargin, y, rightMargin, y + buttonHeight);
            using var delBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Danger.WithAlpha(30) };
            canvas.DrawRoundRect(_deleteProfileButtonBounds, 4, 4, delBgPaint);

            using var delFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Danger.WithAlpha(150), StrokeWidth = 1f };
            canvas.DrawRoundRect(_deleteProfileButtonBounds, 4, 4, delFramePaint);

            FUIRenderer.DrawTextCentered(canvas, "Delete Profile", _deleteProfileButtonBounds, FUIColors.Danger, 14f);
            y += buttonHeight + 20f;

            if (y < bottom - 60)
            {
                FUIWidgets.DrawShiftLayersSection(canvas, leftMargin, rightMargin, y, bottom, profile);
            }
        }

        canvas.Restore();
    }

    private void DrawApplicationSettingsPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        float gap = FUIRenderer.SpaceSM;
        float leftWidth = (bounds.Width - gap) * 0.52f;
        float rightWidth = (bounds.Width - gap) * 0.48f;

        var leftBounds = new SKRect(bounds.Left, bounds.Top, bounds.Left + leftWidth, bounds.Bottom);
        var rightBounds = new SKRect(bounds.Left + leftWidth + gap, bounds.Top, bounds.Right, bounds.Bottom);

        DrawSystemSettingsSubPanel(canvas, leftBounds, frameInset);
        DrawVisualSettingsSubPanel(canvas, rightBounds, frameInset);
    }

    private void DrawSystemSettingsSubPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(160),
            IsAntialias = true
        };
        canvas.DrawRect(bounds.Inset(frameInset, frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame, 30f, 8f);

        float cornerPadding = FUIRenderer.SpaceXL;
        float y = bounds.Top + frameInset + cornerPadding;
        float leftMargin = bounds.Left + frameInset + cornerPadding;
        float rightMargin = bounds.Right - frameInset - FUIRenderer.SpaceLG;
        float contentWidth = rightMargin - leftMargin;
        float sectionSpacing = 20f;
        float rowHeight = 24f;
        float minControlGap = 12f;

        FUIRenderer.DrawText(canvas, "SYSTEM", new SKPoint(leftMargin, y), FUIColors.TextBright, FUIRenderer.FontBody, true);
        y += 32f;

        // Auto-load setting
        float toggleWidth = 48f;
        float toggleHeight = 24f;
        float autoLoadLabelMaxWidth = contentWidth - toggleWidth - minControlGap;
        float autoLoadLabelY = y + (rowHeight - 11f) / 2 + 11f - 3;
        FUIRenderer.DrawTextTruncated(canvas, "Auto-load profile", new SKPoint(leftMargin, autoLoadLabelY),
            autoLoadLabelMaxWidth, FUIColors.TextPrimary, 14f);
        float toggleY = y + (rowHeight - toggleHeight) / 2;
        _autoLoadToggleBounds = new SKRect(rightMargin - toggleWidth, toggleY, rightMargin, toggleY + toggleHeight);
        FUIWidgets.DrawToggleSwitch(canvas, _autoLoadToggleBounds, _ctx.AppSettings.AutoLoadLastProfile, _ctx.MousePosition);
        y += rowHeight + sectionSpacing;

        // Close to Tray toggle
        float closeToTrayLabelMaxWidth = contentWidth - toggleWidth - minControlGap;
        float closeToTrayLabelY = y + (rowHeight - 11f) / 2 + 11f - 3;
        FUIRenderer.DrawTextTruncated(canvas, "Close to tray", new SKPoint(leftMargin, closeToTrayLabelY),
            closeToTrayLabelMaxWidth, FUIColors.TextPrimary, 14f);
        float closeToTrayToggleY = y + (rowHeight - toggleHeight) / 2;
        _closeToTrayToggleBounds = new SKRect(rightMargin - toggleWidth, closeToTrayToggleY, rightMargin, closeToTrayToggleY + toggleHeight);
        FUIWidgets.DrawToggleSwitch(canvas, _closeToTrayToggleBounds, _ctx.AppSettings.CloseToTray, _ctx.MousePosition);
        y += rowHeight + sectionSpacing;

        // Tray icon type selection
        TrayIconType[] trayIconValues = { TrayIconType.Throttle, TrayIconType.Joystick };
        string[] trayIconLabels = { "Throttle", "Joystick" };
        float iconBtnWidth = 72f;
        float iconBtnHeight = 32f;
        float iconBtnGap = 4f;
        float iconBtnsWidth = iconBtnWidth * 2 + iconBtnGap;
        float iconLabelMaxWidth = contentWidth - iconBtnsWidth - minControlGap;

        FUIRenderer.DrawTextTruncated(canvas, "Tray icon", new SKPoint(leftMargin, y + 6),
            iconLabelMaxWidth, FUIColors.TextPrimary, 14f);

        float iconBtnsStartX = rightMargin - iconBtnsWidth;

        for (int i = 0; i < trayIconValues.Length; i++)
        {
            var iconBounds = new SKRect(
                iconBtnsStartX + i * (iconBtnWidth + iconBtnGap), y,
                iconBtnsStartX + i * (iconBtnWidth + iconBtnGap) + iconBtnWidth, y + iconBtnHeight);
            _trayIconTypeButtonBounds[i] = iconBounds;

            bool isSelected = _ctx.AppSettings.TrayIconType == trayIconValues[i];
            bool isHovered = iconBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

            var bgColor = isSelected
                ? FUIColors.Active.WithAlpha(60)
                : (isHovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background2);
            var frameColor = isSelected
                ? FUIColors.Active
                : (isHovered ? FUIColors.FrameBright : FUIColors.Frame);
            var textColor = isSelected ? FUIColors.TextBright : FUIColors.TextDim;

            using var iconBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
            canvas.DrawRect(iconBounds, iconBgPaint);

            using var iconBorderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = frameColor,
                StrokeWidth = isSelected ? 1.5f : 1f
            };
            canvas.DrawRect(iconBounds, iconBorderPaint);

            FUIRenderer.DrawTextCentered(canvas, trayIconLabels[i], iconBounds, textColor, 13f);
        }
        y += iconBtnHeight + sectionSpacing;

        // DRIVERS section
        FUIRenderer.DrawText(canvas, "DRIVERS", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        y += sectionSpacing;

        var vjoyDevices = _ctx.VJoyService.EnumerateDevices();
        bool vjoyEnabled = vjoyDevices.Count > 0;
        var driverStatus = _ctx.DriverSetupManager.GetSetupStatus();

        float statusDotRadius = 4f;
        float statusLineHeight = 21f;
        float statusTextX = leftMargin + statusDotRadius * 2 + 8;

        // vJoy status row
        {
            string vjoyStatusStr = vjoyEnabled ? "Driver active" : "Not installed";
            var vjoyColor = vjoyEnabled ? FUIColors.Success : FUIColors.Danger;
            float dotY = y + (statusLineHeight / 2);
            float textY = y + (statusLineHeight / 2) + 4;

            using var vjoyDot = new SKPaint { Style = SKPaintStyle.Fill, Color = vjoyColor, IsAntialias = true };
            canvas.DrawCircle(leftMargin + statusDotRadius + 1, dotY, statusDotRadius, vjoyDot);
            FUIRenderer.DrawText(canvas, "vJoy", new SKPoint(statusTextX, textY), FUIColors.TextPrimary, 14f);
            float vjoyLabelW = FUIRenderer.MeasureText("vJoy", 14f);
            FUIRenderer.DrawText(canvas, vjoyStatusStr, new SKPoint(statusTextX + vjoyLabelW + 12f, textY),
                vjoyEnabled ? FUIColors.TextDim : FUIColors.Danger, 13f);
            y += statusLineHeight;
        }

        // HidHide status row + DRIVER SETUP button
        {
            string hidHideStatusStr = driverStatus.HidHideInstalled ? "Available" : "Not installed";
            var hidHideColor = driverStatus.HidHideInstalled ? FUIColors.Success : FUIColors.Warning;
            float dotY = y + (statusLineHeight / 2);
            float textY = y + (statusLineHeight / 2) + 4;

            using var hidHideDot = new SKPaint { Style = SKPaintStyle.Fill, Color = hidHideColor, IsAntialias = true };
            canvas.DrawCircle(leftMargin + statusDotRadius + 1, dotY, statusDotRadius, hidHideDot);
            FUIRenderer.DrawText(canvas, "HidHide", new SKPoint(statusTextX, textY), FUIColors.TextPrimary, 14f);
            float hidHideLabelW = FUIRenderer.MeasureText("HidHide", 14f);
            FUIRenderer.DrawText(canvas, hidHideStatusStr, new SKPoint(statusTextX + hidHideLabelW + 12f, textY),
                driverStatus.HidHideInstalled ? FUIColors.TextDim : FUIColors.Warning, 13f);

            // DRIVER SETUP button — right-aligned on the HidHide row
            float setupBtnWidth = 110f;
            float setupBtnHeight = 22f;
            float setupBtnY = y + (statusLineHeight - setupBtnHeight) / 2;
            _driverSetupButtonBounds = new SKRect(rightMargin - setupBtnWidth, setupBtnY, rightMargin, setupBtnY + setupBtnHeight);
            bool setupHovered = _driverSetupButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            DrawSupportActionButton(canvas, _driverSetupButtonBounds, "DRIVER SETUP", setupHovered, false);
            y += statusLineHeight;
        }

        // Available devices line (only when vJoy is active)
        if (vjoyEnabled)
        {
            y += 4f; // extra spacing from the status dot rows
            FUIRenderer.DrawTextTruncated(canvas, $"Available devices: {vjoyDevices.Count}",
                new SKPoint(leftMargin, y + 12f), contentWidth, FUIColors.TextDim, 13f);
            y += rowHeight;
        }
        y += sectionSpacing;

        // VERSION & UPDATES section
        FUIRenderer.DrawText(canvas, "VERSION & UPDATES", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        y += sectionSpacing;

        // "Check for updates automatically" toggle — first in this section
        float autoCheckLabelMaxWidth = contentWidth - toggleWidth - minControlGap;
        float autoCheckLabelY = y + (rowHeight - 11f) / 2 + 11f - 3;
        FUIRenderer.DrawTextTruncated(canvas, "Check for updates automatically", new SKPoint(leftMargin, autoCheckLabelY),
            autoCheckLabelMaxWidth, FUIColors.TextPrimary, 14f);
        float autoCheckToggleY = y + (rowHeight - toggleHeight) / 2;
        _checkUpdatesToggleBounds = new SKRect(rightMargin - toggleWidth, autoCheckToggleY, rightMargin, autoCheckToggleY + toggleHeight);
        FUIWidgets.DrawToggleSwitch(canvas, _checkUpdatesToggleBounds, _ctx.AppSettings.AutoCheckUpdates, _ctx.MousePosition);
        y += rowHeight + sectionSpacing;

        // Version row: "v0.8.289" left, [Check for updates] button right
        string currentVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";
        string versionStr = $"v{currentVersion}";

        var updateStatus = _ctx.UpdateService.Status;
        float checkBtnWidth = 150f;
        float checkBtnHeight = 28f;
        bool checkBtnEnabled = updateStatus is UpdateStatus.Unknown or UpdateStatus.UpToDate or UpdateStatus.Error;
        _checkButtonBounds = new SKRect(rightMargin - checkBtnWidth, y, rightMargin, y + checkBtnHeight);
        bool checkHovered = checkBtnEnabled && _checkButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        string checkLabel = updateStatus == UpdateStatus.Checking ? "CHECKING\u2026" : "CHECK FOR UPDATES";
        DrawSupportActionButton(canvas, _checkButtonBounds, checkLabel, checkHovered, false,
            !checkBtnEnabled || updateStatus == UpdateStatus.Checking);

        // Version text — vertically aligned with the button
        float versionTextY = y + (checkBtnHeight - 11f) / 2 + 11f - 3;
        FUIRenderer.DrawText(canvas, versionStr, new SKPoint(leftMargin, versionTextY), FUIColors.TextPrimary, 14f);
        y += checkBtnHeight + 4f;

        // "Last checked" timestamp
        var lastChecked = _ctx.UpdateService.LastChecked;
        string lastCheckedStr = lastChecked.HasValue
            ? $"Last checked: {lastChecked.Value:dd/MM/yyyy HH:mm}"
            : "Last checked: never";
        FUIRenderer.DrawText(canvas, lastCheckedStr, new SKPoint(leftMargin, y + 10f), FUIColors.TextDim, 12f);
        y += 20f;

        // Status banner — contextual based on update status
        _downloadButtonBounds = SKRect.Empty;
        _applyButtonBounds = SKRect.Empty;

        float bannerHeight = 32f;
        float bannerRadius = 4f;
        string latest = _ctx.UpdateService.LatestVersion ?? "?";

        if (updateStatus != UpdateStatus.Unknown && updateStatus != UpdateStatus.Checking)
        {
            var bannerRect = new SKRect(leftMargin, y, rightMargin, y + bannerHeight);
            float dotRadius = 4f;
            float dotX = leftMargin + 12f;
            float dotY = y + bannerHeight / 2f;
            float textStartX = dotX + dotRadius + 8f;
            float textY = y + bannerHeight / 2f + 4f;

            switch (updateStatus)
            {
                case UpdateStatus.UpToDate:
                {
                    using var bannerBg = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(25), IsAntialias = true };
                    canvas.DrawRoundRect(bannerRect, bannerRadius, bannerRadius, bannerBg);
                    using var bannerBorder = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active.WithAlpha(50), StrokeWidth = 1f, IsAntialias = true };
                    canvas.DrawRoundRect(bannerRect, bannerRadius, bannerRadius, bannerBorder);
                    using var dotPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active, IsAntialias = true };
                    canvas.DrawCircle(dotX, dotY, dotRadius, dotPaint);
                    FUIRenderer.DrawText(canvas, "Asteriq is up to date", new SKPoint(textStartX, textY), FUIColors.TextPrimary, 13f);
                    break;
                }

                case UpdateStatus.UpdateAvailable:
                {
                    using var bannerBg = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(25), IsAntialias = true };
                    canvas.DrawRoundRect(bannerRect, bannerRadius, bannerRadius, bannerBg);
                    using var bannerBorder = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active.WithAlpha(50), StrokeWidth = 1f, IsAntialias = true };
                    canvas.DrawRoundRect(bannerRect, bannerRadius, bannerRadius, bannerBorder);
                    using var dotPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active, IsAntialias = true };
                    canvas.DrawCircle(dotX, dotY, dotRadius, dotPaint);
                    string availText = $"Update available: v{latest}";
                    FUIRenderer.DrawText(canvas, availText, new SKPoint(textStartX, textY), FUIColors.TextPrimary, 13f);

                    // Inline "Download update" button
                    float dlBtnWidth = FUIRenderer.MeasureText("DOWNLOAD", 13f) + 24f;
                    _downloadButtonBounds = new SKRect(rightMargin - dlBtnWidth - 6f, y + 4f, rightMargin - 6f, y + bannerHeight - 4f);
                    bool dlHovered = _downloadButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
                    DrawSupportActionButton(canvas, _downloadButtonBounds, "DOWNLOAD", dlHovered, true);
                    break;
                }

                case UpdateStatus.Downloading:
                {
                    int pct = _ctx.UpdateService.DownloadProgress;
                    // Progress bar fill as banner background
                    using var bannerBg = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(15), IsAntialias = true };
                    canvas.DrawRoundRect(bannerRect, bannerRadius, bannerRadius, bannerBg);
                    float fillWidth = bannerRect.Width * (pct / 100f);
                    var fillRect = new SKRect(bannerRect.Left, bannerRect.Top, bannerRect.Left + fillWidth, bannerRect.Bottom);
                    using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(40), IsAntialias = true };
                    canvas.Save();
                    canvas.ClipRoundRect(new SKRoundRect(bannerRect, bannerRadius));
                    canvas.DrawRect(fillRect, fillPaint);
                    canvas.Restore();
                    using var bannerBorder = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active.WithAlpha(50), StrokeWidth = 1f, IsAntialias = true };
                    canvas.DrawRoundRect(bannerRect, bannerRadius, bannerRadius, bannerBorder);
                    using var dotPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active, IsAntialias = true };
                    canvas.DrawCircle(dotX, dotY, dotRadius, dotPaint);
                    FUIRenderer.DrawText(canvas, $"Downloading update\u2026 {pct}%", new SKPoint(textStartX, textY), FUIColors.TextPrimary, 13f);
                    break;
                }

                case UpdateStatus.ReadyToApply:
                {
                    using var bannerBg = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(25), IsAntialias = true };
                    canvas.DrawRoundRect(bannerRect, bannerRadius, bannerRadius, bannerBg);
                    using var bannerBorder = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active.WithAlpha(50), StrokeWidth = 1f, IsAntialias = true };
                    canvas.DrawRoundRect(bannerRect, bannerRadius, bannerRadius, bannerBorder);
                    using var dotPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active, IsAntialias = true };
                    canvas.DrawCircle(dotX, dotY, dotRadius, dotPaint);
                    FUIRenderer.DrawText(canvas, "Update ready", new SKPoint(textStartX, textY), FUIColors.TextPrimary, 13f);

                    // Inline "Apply update" button
                    float apBtnWidth = FUIRenderer.MeasureText("APPLY", 13f) + 24f;
                    _applyButtonBounds = new SKRect(rightMargin - apBtnWidth - 6f, y + 4f, rightMargin - 6f, y + bannerHeight - 4f);
                    bool apHovered = _applyButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
                    DrawSupportActionButton(canvas, _applyButtonBounds, "APPLY", apHovered, true);
                    break;
                }

                case UpdateStatus.Error:
                {
                    using var bannerBg = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Danger.WithAlpha(25), IsAntialias = true };
                    canvas.DrawRoundRect(bannerRect, bannerRadius, bannerRadius, bannerBg);
                    using var bannerBorder = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Danger.WithAlpha(50), StrokeWidth = 1f, IsAntialias = true };
                    canvas.DrawRoundRect(bannerRect, bannerRadius, bannerRadius, bannerBorder);
                    using var dotPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Danger, IsAntialias = true };
                    canvas.DrawCircle(dotX, dotY, dotRadius, dotPaint);
                    FUIRenderer.DrawText(canvas, "Check failed", new SKPoint(textStartX, textY), FUIColors.TextPrimary, 13f);
                    break;
                }
            }
            y += bannerHeight + 8f;
        }
    }

    private void DrawVisualSettingsSubPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(160),
            IsAntialias = true
        };
        canvas.DrawRect(bounds.Inset(frameInset, frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame, 30f, 8f);

        float cornerPadding = FUIRenderer.SpaceXL;
        float y = bounds.Top + frameInset + cornerPadding;
        float leftMargin = bounds.Left + frameInset + cornerPadding;
        float rightMargin = bounds.Right - frameInset - FUIRenderer.SpaceLG;
        float contentWidth = rightMargin - leftMargin;
        float sectionSpacing = 16f;

        FUIRenderer.DrawText(canvas, "VISUAL", new SKPoint(leftMargin, y), FUIColors.TextBright, FUIRenderer.FontBody, true);
        y += 32f;

        // Theme section
        float themeLabelWidth = 36f;
        float themeAreaWidth = contentWidth - themeLabelWidth;
        float themeBtnGap = 4f;
        float themeBtnWidth = Math.Min(40f, (themeAreaWidth - themeBtnGap * 3) / 4);
        float themeBtnHeight = FUIRenderer.TouchTargetMinHeight;
        float themeBtnsStartX = leftMargin + themeLabelWidth;

        float themeLabelY = themeBtnHeight / 2 + 3;
        FUIRenderer.DrawTextTruncated(canvas, "Core", new SKPoint(leftMargin, y + themeLabelY), themeLabelWidth - 5, FUIColors.TextDim, 12f);

        FUITheme[] coreThemes = { FUITheme.Midnight, FUITheme.Matrix, FUITheme.Amber, FUITheme.Ice };
        string[] coreNames = { "MID", "MTX", "AMB", "ICE" };
        SKColor[] coreColors = {
            new SKColor(0x40, 0xA0, 0xFF), new SKColor(0x40, 0xFF, 0x40),
            new SKColor(0xFF, 0xA0, 0x40), new SKColor(0x40, 0xE0, 0xFF)
        };

        for (int i = 0; i < coreThemes.Length; i++)
        {
            var themeBounds = new SKRect(
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap), y,
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap) + themeBtnWidth, y + themeBtnHeight);
            StoreThemeButtonBounds(i, themeBounds);
            FUIWidgets.DrawThemeButton(canvas, themeBounds, coreNames[i], coreColors[i], FUIColors.CurrentTheme == coreThemes[i], _ctx.MousePosition);
        }
        y += themeBtnHeight + 8;

        FUIRenderer.DrawTextTruncated(canvas, "Mfr", new SKPoint(leftMargin, y + themeLabelY), themeLabelWidth - 5, FUIColors.TextDim, 12f);

        FUITheme[] mfrThemes1 = { FUITheme.Drake, FUITheme.Aegis, FUITheme.Anvil, FUITheme.Argo };
        string[] mfrNames1 = { "DRK", "AEG", "ANV", "ARG" };
        SKColor[] mfrColors1 = {
            new SKColor(0xFF, 0x80, 0x20), new SKColor(0x40, 0x90, 0xE0),
            new SKColor(0x90, 0xC0, 0x40), new SKColor(0xFF, 0xC0, 0x00)
        };

        for (int i = 0; i < mfrThemes1.Length; i++)
        {
            var themeBounds = new SKRect(
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap), y,
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap) + themeBtnWidth, y + themeBtnHeight);
            StoreThemeButtonBounds(4 + i, themeBounds);
            FUIWidgets.DrawThemeButton(canvas, themeBounds, mfrNames1[i], mfrColors1[i], FUIColors.CurrentTheme == mfrThemes1[i], _ctx.MousePosition);
        }
        y += themeBtnHeight + 4;

        FUITheme[] mfrThemes2 = { FUITheme.Crusader, FUITheme.Origin, FUITheme.MISC, FUITheme.RSI };
        string[] mfrNames2 = { "CRU", "ORI", "MSC", "RSI" };
        SKColor[] mfrColors2 = {
            new SKColor(0x40, 0x90, 0xE0), new SKColor(0xD4, 0xAF, 0x37),
            new SKColor(0x40, 0xC0, 0x90), new SKColor(0x50, 0xA0, 0xF0)
        };

        for (int i = 0; i < mfrThemes2.Length; i++)
        {
            var themeBounds = new SKRect(
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap), y,
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap) + themeBtnWidth, y + themeBtnHeight);
            StoreThemeButtonBounds(8 + i, themeBounds);
            FUIWidgets.DrawThemeButton(canvas, themeBounds, mfrNames2[i], mfrColors2[i], FUIColors.CurrentTheme == mfrThemes2[i], _ctx.MousePosition);
        }
        y += themeBtnHeight + sectionSpacing + 8;

        // Background effects section — directly below themes
        FUIRenderer.DrawText(canvas, "BACKGROUND", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        y += sectionSpacing + 8;

        string[] sliderLabels = { "Grid", "Glow", "Noise", "Scanlines", "Vignette" };
        float maxLabelWidth = 0;
        foreach (var label in sliderLabels)
        {
            float w = FUIRenderer.MeasureText(label, 14f);
            if (w > maxLabelWidth) maxLabelWidth = w;
        }

        float labelColumnWidth = maxLabelWidth + 10f;
        float valueColumnWidth = FUIRenderer.MeasureText("100", 13f) + 8f;
        float sliderLeft = leftMargin + labelColumnWidth;
        float sliderRight = rightMargin - valueColumnWidth;
        float sliderRowHeight = 22f;
        float sliderRowGap = 8f;

        if (sliderRight - sliderLeft < 50)
        {
            sliderLeft = leftMargin + 50;
            sliderRight = rightMargin - 30;
        }

        float sliderHeight = 12f;
        float sliderYOff = (sliderRowHeight - sliderHeight) / 2;
        float textY = sliderRowHeight / 2 + 4;

        var bg = _ctx.Background;

        FUIRenderer.DrawTextTruncated(canvas, "Grid", new SKPoint(leftMargin, y + textY), labelColumnWidth - 5, FUIColors.TextPrimary, 14f);
        _bgGridSliderBounds = new SKRect(sliderLeft, y + sliderYOff, sliderRight, y + sliderYOff + sliderHeight);
        FUIWidgets.DrawSettingsSlider(canvas, _bgGridSliderBounds, bg.GridStrength, 100);
        FUIRenderer.DrawText(canvas, bg.GridStrength.ToString(), new SKPoint(sliderRight + 8, y + textY), FUIColors.TextDim, 13f);
        y += sliderRowHeight + sliderRowGap;

        FUIRenderer.DrawTextTruncated(canvas, "Glow", new SKPoint(leftMargin, y + textY), labelColumnWidth - 5, FUIColors.TextPrimary, 14f);
        _bgGlowSliderBounds = new SKRect(sliderLeft, y + sliderYOff, sliderRight, y + sliderYOff + sliderHeight);
        FUIWidgets.DrawSettingsSlider(canvas, _bgGlowSliderBounds, bg.GlowIntensity, 100);
        FUIRenderer.DrawText(canvas, bg.GlowIntensity.ToString(), new SKPoint(sliderRight + 8, y + textY), FUIColors.TextDim, 13f);
        y += sliderRowHeight + sliderRowGap;

        FUIRenderer.DrawTextTruncated(canvas, "Noise", new SKPoint(leftMargin, y + textY), labelColumnWidth - 5, FUIColors.TextPrimary, 14f);
        _bgNoiseSliderBounds = new SKRect(sliderLeft, y + sliderYOff, sliderRight, y + sliderYOff + sliderHeight);
        FUIWidgets.DrawSettingsSlider(canvas, _bgNoiseSliderBounds, bg.NoiseIntensity, 100);
        FUIRenderer.DrawText(canvas, bg.NoiseIntensity.ToString(), new SKPoint(sliderRight + 8, y + textY), FUIColors.TextDim, 13f);
        y += sliderRowHeight + sliderRowGap;

        FUIRenderer.DrawTextTruncated(canvas, "Scanlines", new SKPoint(leftMargin, y + textY), labelColumnWidth - 5, FUIColors.TextPrimary, 14f);
        _bgScanlineSliderBounds = new SKRect(sliderLeft, y + sliderYOff, sliderRight, y + sliderYOff + sliderHeight);
        FUIWidgets.DrawSettingsSlider(canvas, _bgScanlineSliderBounds, bg.ScanlineIntensity, 100);
        FUIRenderer.DrawText(canvas, bg.ScanlineIntensity.ToString(), new SKPoint(sliderRight + 8, y + textY), FUIColors.TextDim, 13f);
        y += sliderRowHeight + sliderRowGap;

        FUIRenderer.DrawTextTruncated(canvas, "Vignette", new SKPoint(leftMargin, y + textY), labelColumnWidth - 5, FUIColors.TextPrimary, 14f);
        _bgVignetteSliderBounds = new SKRect(sliderLeft, y + sliderYOff, sliderRight, y + sliderYOff + sliderHeight);
        FUIWidgets.DrawSettingsSlider(canvas, _bgVignetteSliderBounds, bg.VignetteStrength, 100);
        FUIRenderer.DrawText(canvas, bg.VignetteStrength.ToString(), new SKPoint(sliderRight + 8, y + textY), FUIColors.TextDim, 13f);
        y += sliderRowHeight + sectionSpacing;

        // Font section — family selector + size stepper, clearly grouped
        FUIRenderer.DrawText(canvas, "FONT", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        y += sectionSpacing;

        float fontBtnGap = 4f;
        float fontFamilyBtnWidth = (contentWidth - fontBtnGap) / 2;
        float fontFamilyBtnHeight = 28f;
        UIFontFamily[] fontFamilyValues = { UIFontFamily.Carbon, UIFontFamily.Consolas };
        string[] fontFamilyLabels = { "CARBON", "CONSOLAS" };

        for (int i = 0; i < fontFamilyValues.Length; i++)
        {
            var ffBounds = new SKRect(
                leftMargin + i * (fontFamilyBtnWidth + fontBtnGap), y,
                leftMargin + i * (fontFamilyBtnWidth + fontBtnGap) + fontFamilyBtnWidth, y + fontFamilyBtnHeight);
            _fontFamilyButtonBounds[i] = ffBounds;
            bool isActive = _ctx.AppSettings.FontFamily == fontFamilyValues[i];
            bool isHovered = ffBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            var ffBg = isActive ? FUIColors.Active.WithAlpha(60) : (isHovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background2);
            var ffFrame = isActive ? FUIColors.Active : (isHovered ? FUIColors.FrameBright : FUIColors.Frame);
            var ffText = isActive ? FUIColors.TextBright : FUIColors.TextDim;
            using var ffBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = ffBg };
            canvas.DrawRect(ffBounds, ffBgPaint);
            using var ffFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = ffFrame, StrokeWidth = isActive ? 1.5f : 1f };
            canvas.DrawRect(ffBounds, ffFramePaint);
            FUIRenderer.DrawTextCentered(canvas, fontFamilyLabels[i], ffBounds, ffText, 13f, scaleFont: false);
        }
        y += fontFamilyBtnHeight + sectionSpacing;

        // Size stepper on a single labeled row
        float fontBtnWidth = 32f;
        float fontBtnHeight = 28f;
        float fontValueWidth = 44f;
        float fontStepperWidth = fontBtnWidth * 2 + fontBtnGap * 2 + fontValueWidth;

        FUIRenderer.DrawTextTruncated(canvas, "Interface Scale", new SKPoint(leftMargin, y + 6),
            contentWidth - fontStepperWidth - FUIRenderer.SpaceSM, FUIColors.TextPrimary, 13f);

        float scale = _ctx.AppSettings.FontSize;
        float dynamicMax = FUIRenderer.MaxInterfaceScale(Screen.PrimaryScreen?.Bounds.Width ?? 1920);
        float max = MathF.Min(dynamicMax, 1.5f);
        bool canDecrease = scale > 0.8f + 0.01f;
        bool canIncrease = scale < max - 0.01f;
        float stepperX = rightMargin - fontStepperWidth;

        var minusBounds = new SKRect(stepperX, y, stepperX + fontBtnWidth, y + fontBtnHeight);
        _fontSizeButtonBounds[0] = minusBounds;
        bool minusHovered = canDecrease && minusBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        var minusBg = !canDecrease ? FUIColors.Background1 : (minusHovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background2);
        var minusFrame = !canDecrease ? FUIColors.Frame.WithAlpha(60) : (minusHovered ? FUIColors.FrameBright : FUIColors.Frame);
        var minusText = !canDecrease ? FUIColors.TextDim.WithAlpha(60) : (minusHovered ? FUIColors.TextBright : FUIColors.TextPrimary);
        using (var p = new SKPaint { Style = SKPaintStyle.Fill, Color = minusBg }) canvas.DrawRect(minusBounds, p);
        using (var p = new SKPaint { Style = SKPaintStyle.Stroke, Color = minusFrame, StrokeWidth = 1f }) canvas.DrawRect(minusBounds, p);
        FUIRenderer.DrawTextCentered(canvas, "-", minusBounds, minusText, 17f, scaleFont: false);

        string valueText = $"{scale:F1}x";
        var valueBounds = new SKRect(stepperX + fontBtnWidth + fontBtnGap, y, stepperX + fontBtnWidth + fontBtnGap + fontValueWidth, y + fontBtnHeight);
        FUIRenderer.DrawTextCentered(canvas, valueText, valueBounds, FUIColors.TextBright, 14f, scaleFont: false);

        var plusBounds = new SKRect(valueBounds.Right + fontBtnGap, y, valueBounds.Right + fontBtnGap + fontBtnWidth, y + fontBtnHeight);
        _fontSizeButtonBounds[1] = plusBounds;
        bool plusHovered = canIncrease && plusBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        var plusBg = !canIncrease ? FUIColors.Background1 : (plusHovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background2);
        var plusFrame = !canIncrease ? FUIColors.Frame.WithAlpha(60) : (plusHovered ? FUIColors.FrameBright : FUIColors.Frame);
        var plusText = !canIncrease ? FUIColors.TextDim.WithAlpha(60) : (plusHovered ? FUIColors.TextBright : FUIColors.TextPrimary);
        using (var p = new SKPaint { Style = SKPaintStyle.Fill, Color = plusBg }) canvas.DrawRect(plusBounds, p);
        using (var p = new SKPaint { Style = SKPaintStyle.Stroke, Color = plusFrame, StrokeWidth = 1f }) canvas.DrawRect(plusBounds, p);
        FUIRenderer.DrawTextCentered(canvas, "+", plusBounds, plusText, 17f, scaleFont: false);
    }

    private void DrawSupportPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(160),
            IsAntialias = true
        };
        canvas.DrawRect(bounds.Inset(frameInset, frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame, 30f, 8f);

        float cornerPadding = FUIRenderer.SpaceXL;
        float y = bounds.Top + frameInset + cornerPadding;
        float leftMargin = bounds.Left + frameInset + cornerPadding;
        float rightMargin = bounds.Right - frameInset - FUIRenderer.SpaceLG;

        // Header row: "SUPPORT" left, SC referral descriptor right-aligned
        FUIRenderer.DrawText(canvas, "SUPPORT", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        const string scDescriptor = "Referral Code \u00b7 50,000 Bonus aUEC";
        float descWidth = FUIRenderer.MeasureText(scDescriptor, 12f);
        FUIRenderer.DrawText(canvas, scDescriptor, new SKPoint(rightMargin - descWidth, y + 1f), FUIColors.TextDim, 12f);
        y += 20f;

        float btnHeight = 28f;

        // Left: Buy Me a Coffee button
        float bmacWidth = 170f;
        _bmacButtonBounds = new SKRect(leftMargin, y, leftMargin + bmacWidth, y + btnHeight);
        bool bmacHovered = _bmacButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        DrawSupportActionButton(canvas, _bmacButtonBounds, "BUY ME A COFFEE", bmacHovered, false);

        // Right: Join Star Citizen button — anchored to right margin
        float scLinkWidth = 180f;
        _referralLinkButtonBounds = new SKRect(rightMargin - scLinkWidth, y, rightMargin, y + btnHeight);
        bool scLinkHovered = _referralLinkButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        DrawSupportActionButton(canvas, _referralLinkButtonBounds, "JOIN STAR CITIZEN \u2192", scLinkHovered, false);

        // Center: code field + copy button — centered between BMAC and JOIN
        float codeFieldWidth = 138f;
        float copyBtnWidth = 56f;
        float referralGroupWidth = codeFieldWidth + FUIRenderer.SpaceSM + copyBtnWidth;
        float referralGroupX = leftMargin + (rightMargin - leftMargin - referralGroupWidth) / 2;

        var codeDisplayBounds = new SKRect(referralGroupX, y, referralGroupX + codeFieldWidth, y + btnHeight);
        FUIWidgets.DrawSettingsValueField(canvas, codeDisplayBounds, "STAR-RBDQ-Z4JG");

        _referralCopyButtonBounds = new SKRect(
            codeDisplayBounds.Right + FUIRenderer.SpaceSM, y,
            codeDisplayBounds.Right + FUIRenderer.SpaceSM + copyBtnWidth, y + btnHeight);
        bool copyHovered = _referralCopyButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        DrawSupportActionButton(canvas, _referralCopyButtonBounds, "COPY", copyHovered, true);
    }

    private static void DrawSupportActionButton(SKCanvas canvas, SKRect bounds, string text, bool hovered, bool accent, bool disabled = false)
    {
        if (disabled)
        {
            using var dbgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1 };
            canvas.DrawRoundRect(bounds, 4, 4, dbgPaint);
            using var dfPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame.WithAlpha(60), StrokeWidth = 1f };
            canvas.DrawRoundRect(bounds, 4, 4, dfPaint);
            FUIRenderer.DrawTextCentered(canvas, text, bounds, FUIColors.TextDim.WithAlpha(80), 14f);
            return;
        }

        var accentColor = accent ? FUIColors.Active : FUIColors.Primary;
        var bgColor = hovered ? accentColor.WithAlpha(40) : FUIColors.Background2;
        var frameColor = hovered ? accentColor : FUIColors.Frame;
        var textColor = hovered ? FUIColors.TextBright : FUIColors.TextPrimary;

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRoundRect(bounds, 4, 4, bgPaint);

        using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = frameColor, StrokeWidth = hovered ? 1.5f : 1f };
        canvas.DrawRoundRect(bounds, 4, 4, framePaint);

        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 14f);
    }

    private void StoreThemeButtonBounds(int index, SKRect bounds)
    {
        if (index >= 0 && index < _themeButtonBounds.Length)
        {
            _themeButtonBounds[index] = bounds;
        }
    }

    #endregion

    #region Click Handling

    private void HandleSettingsTabClick(SKPoint pt)
    {
        // Profile name edit icon
        if (_profileNameEditBounds != SKRect.Empty && _profileNameEditBounds.Contains(pt))
        {
            RenameActiveProfile();
            return;
        }

        // Profile name box click (whole box)
        if (_profileNameBounds.Contains(pt) && _ctx.ProfileManager.ActiveProfile is not null)
        {
            RenameActiveProfile();
            return;
        }

        // Profile action buttons
        if (_newProfileButtonBounds.Contains(pt))
        {
            _ctx.CreateNewProfilePrompt?.Invoke();
            return;
        }
        if (_duplicateProfileButtonBounds.Contains(pt) && _ctx.ProfileManager.ActiveProfile is not null)
        {
            _ctx.DuplicateActiveProfile?.Invoke();
            return;
        }
        if (_importProfileButtonBounds.Contains(pt))
        {
            _ctx.ImportProfile?.Invoke();
            return;
        }
        if (_exportProfileButtonBounds.Contains(pt) && _ctx.ProfileManager.ActiveProfile is not null)
        {
            _ctx.ExportActiveProfile?.Invoke();
            return;
        }
        if (_deleteProfileButtonBounds != default && _deleteProfileButtonBounds.Contains(pt) &&
            _ctx.ProfileManager.ActiveProfile is not null)
        {
            _ctx.DeleteActiveProfile?.Invoke();
            return;
        }

        // Font size stepper clicks ([-] and [+])
        {
            float scale = _ctx.AppSettings.FontSize;
            float dynamicMax = FUIRenderer.MaxInterfaceScale(Screen.PrimaryScreen?.Bounds.Width ?? 1920);
            float max = MathF.Min(dynamicMax, 1.5f);

            if (_fontSizeButtonBounds[0].Contains(pt) && scale > 0.8f + 0.01f)
            {
                float newScale = MathF.Round((scale - 0.1f) * 10f) / 10f;
                newScale = MathF.Max(newScale, 0.8f);
                _ctx.AppSettings.FontSize = newScale;
                FUIRenderer.InterfaceScale = newScale;
                _ctx.ApplyFontScale?.Invoke();
                _ctx.InvalidateCanvas();
                return;
            }
            if (_fontSizeButtonBounds[1].Contains(pt) && scale < max - 0.01f)
            {
                float newScale = MathF.Round((scale + 0.1f) * 10f) / 10f;
                newScale = MathF.Min(newScale, max);
                _ctx.AppSettings.FontSize = newScale;
                FUIRenderer.InterfaceScale = newScale;
                _ctx.ApplyFontScale?.Invoke();
                _ctx.InvalidateCanvas();
                return;
            }
        }

        // Font family button clicks
        UIFontFamily[] fontFamilies = { UIFontFamily.Carbon, UIFontFamily.Consolas };
        for (int i = 0; i < _fontFamilyButtonBounds.Length; i++)
        {
            if (_fontFamilyButtonBounds[i].Contains(pt))
            {
                _ctx.AppSettings.FontFamily = fontFamilies[i];
                FUIRenderer.FontFamily = fontFamilies[i];
                _ctx.InvalidateCanvas();
                return;
            }
        }

        // Theme button clicks
        FUITheme[] themes = {
            FUITheme.Midnight, FUITheme.Matrix, FUITheme.Amber, FUITheme.Ice,
            FUITheme.Drake, FUITheme.Aegis, FUITheme.Anvil, FUITheme.Argo,
            FUITheme.Crusader, FUITheme.Origin, FUITheme.MISC, FUITheme.RSI
        };
        for (int i = 0; i < _themeButtonBounds.Length && i < themes.Length; i++)
        {
            if (_themeButtonBounds[i].Contains(pt))
            {
                FUIColors.SetTheme(themes[i]);
                _ctx.ThemeService.Theme = themes[i];
                _ctx.BackgroundDirty = true;
                _ctx.TrayIcon.RefreshThemeColors();
                _ctx.InvalidateCanvas();
                return;
            }
        }

        // Auto-load toggle
        if (_autoLoadToggleBounds.Contains(pt))
        {
            _ctx.AppSettings.AutoLoadLastProfile = !_ctx.AppSettings.AutoLoadLastProfile;
            _ctx.InvalidateCanvas();
            return;
        }

        // Close to tray toggle
        if (_closeToTrayToggleBounds.Contains(pt))
        {
            _ctx.AppSettings.CloseToTray = !_ctx.AppSettings.CloseToTray;
            _ctx.InvalidateCanvas();
            return;
        }

        // Check for updates automatically toggle
        if (_checkUpdatesToggleBounds.Contains(pt))
        {
            _ctx.AppSettings.AutoCheckUpdates = !_ctx.AppSettings.AutoCheckUpdates;
            _ctx.InvalidateCanvas();
            return;
        }

        // Tray icon type button clicks
        for (int i = 0; i < _trayIconTypeButtonBounds.Length; i++)
        {
            if (_trayIconTypeButtonBounds[i].Contains(pt))
            {
                var newType = (TrayIconType)i;
                _ctx.AppSettings.TrayIconType = newType;
                _ctx.TrayIcon.SetIconType(newType);
                _ctx.InvalidateCanvas();
                return;
            }
        }

        // Background slider clicks (start drag)
        if (_bgGridSliderBounds.Contains(pt)) { _draggingBgSlider = "grid"; UpdateBgSliderFromPoint(pt.X); return; }
        if (_bgGlowSliderBounds.Contains(pt)) { _draggingBgSlider = "glow"; UpdateBgSliderFromPoint(pt.X); return; }
        if (_bgNoiseSliderBounds.Contains(pt)) { _draggingBgSlider = "noise"; UpdateBgSliderFromPoint(pt.X); return; }
        if (_bgScanlineSliderBounds.Contains(pt)) { _draggingBgSlider = "scanline"; UpdateBgSliderFromPoint(pt.X); return; }
        if (_bgVignetteSliderBounds.Contains(pt)) { _draggingBgSlider = "vignette"; UpdateBgSliderFromPoint(pt.X); return; }

        // Support panel clicks
        if (_bmacButtonBounds.Contains(pt))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://buymeacoffee.com/nerosilentr",
                UseShellExecute = true
            });
            return;
        }
        if (_referralCopyButtonBounds.Contains(pt))
        {
            Clipboard.SetText("STAR-RBDQ-Z4JG");
            return;
        }
        if (_referralLinkButtonBounds.Contains(pt))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://www.robertsspaceindustries.com/enlist?referral=STAR-RBDQ-Z4JG",
                UseShellExecute = true
            });
            return;
        }

        // Driver setup button click
        if (!_driverSetupButtonBounds.IsEmpty && _driverSetupButtonBounds.Contains(pt))
        {
            _ctx.OpenDriverSetup?.Invoke();
            return;
        }

        // Check for updates button click
        if (!_checkButtonBounds.IsEmpty && _checkButtonBounds.Contains(pt))
        {
            var status = _ctx.UpdateService.Status;
            if (status is UpdateStatus.Unknown or UpdateStatus.UpToDate or UpdateStatus.Error)
            {
                _ = _ctx.UpdateService.CheckAsync().ContinueWith(
                    _ => _ctx.OwnerForm.Invoke(_ctx.InvalidateCanvas),
                    TaskContinuationOptions.ExecuteSynchronously);
                _ctx.InvalidateCanvas();
            }
            return;
        }

        // Download update button click (inside UpdateAvailable banner)
        if (!_downloadButtonBounds.IsEmpty && _downloadButtonBounds.Contains(pt))
        {
            _ = _ctx.UpdateService.DownloadAsync().ContinueWith(
                _ => _ctx.OwnerForm.Invoke(_ctx.InvalidateCanvas),
                TaskContinuationOptions.ExecuteSynchronously);
            _ctx.InvalidateCanvas();
            return;
        }

        // Apply update button click (inside ReadyToApply banner)
        if (!_applyButtonBounds.IsEmpty && _applyButtonBounds.Contains(pt))
        {
            _ = _ctx.UpdateService.ApplyUpdateAsync();
            return;
        }
    }

    private void RenameActiveProfile()
    {
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var newName = FUIInputDialog.Show(_ctx.OwnerForm, "Rename Profile", "Profile Name:", profile.Name);
        if (newName is null || newName == profile.Name)
            return;

        profile.Name = newName;
        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.RefreshProfileList();
        _ctx.InvalidateCanvas();
    }

    #endregion

    #region Slider Handling

    private void UpdateBgSliderFromPoint(float x)
    {
        SKRect bounds = _draggingBgSlider switch
        {
            "grid" => _bgGridSliderBounds,
            "glow" => _bgGlowSliderBounds,
            "noise" => _bgNoiseSliderBounds,
            "scanline" => _bgScanlineSliderBounds,
            "vignette" => _bgVignetteSliderBounds,
            _ => default
        };

        if (bounds == default) return;

        float ratio = Math.Clamp((x - bounds.Left) / bounds.Width, 0f, 1f);
        int value = (int)(ratio * 100);
        var bg = _ctx.Background;

        switch (_draggingBgSlider)
        {
            case "grid": bg.GridStrength = value; break;
            case "glow": bg.GlowIntensity = value; break;
            case "noise": bg.NoiseIntensity = value; break;
            case "scanline": bg.ScanlineIntensity = value; break;
            case "vignette": bg.VignetteStrength = value; break;
        }

        _ctx.BackgroundDirty = true;
        _ctx.InvalidateCanvas();
    }

    private void SaveBackgroundSettings()
    {
        var bg = _ctx.Background;
        _ctx.ThemeService.SaveBackgroundSettings(
            bg.GridStrength, bg.GlowIntensity, bg.NoiseIntensity,
            bg.ScanlineIntensity, bg.VignetteStrength);
    }

    #endregion
}
