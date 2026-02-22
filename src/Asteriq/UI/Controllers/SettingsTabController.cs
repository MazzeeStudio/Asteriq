using Asteriq.Models;
using Asteriq.Services;
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

    // Font size / font family selector state
    private SKRect[] _fontSizeButtonBounds = new SKRect[3];
    private SKRect[] _fontFamilyButtonBounds = new SKRect[2];

    // Background settings slider bounds
    private SKRect _bgGridSliderBounds;
    private SKRect _bgGlowSliderBounds;
    private SKRect _bgNoiseSliderBounds;
    private SKRect _bgScanlineSliderBounds;
    private SKRect _bgVignetteSliderBounds;
    private SKRect _autoLoadToggleBounds;
    private SKRect _closeToTrayToggleBounds;
    private SKRect[] _trayIconTypeButtonBounds = new SKRect[2];
    private string? _draggingBgSlider;

    public SettingsTabController(TabContext ctx)
    {
        _ctx = ctx;
    }

    public void Draw(SKCanvas canvas, SKRect bounds, float padLeft, float contentTop, float contentBottom)
    {
        float frameInset = FUIRenderer.FrameInset;
        var contentBounds = new SKRect(padLeft, contentTop, bounds.Right - padLeft, contentBottom);

        // Two-panel layout: Left (profile management) | Right (application settings)
        float panelGap = FUIRenderer.SpaceLG;
        float leftPanelWidth = 400f;

        var leftBounds = new SKRect(contentBounds.Left, contentBounds.Top,
            contentBounds.Left + leftPanelWidth, contentBounds.Bottom);
        var rightBounds = new SKRect(leftBounds.Right + panelGap, contentBounds.Top,
            contentBounds.Right, contentBounds.Bottom);

        DrawProfileManagementPanel(canvas, leftBounds, frameInset);
        DrawApplicationSettingsPanel(canvas, rightBounds, frameInset);
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

            float nameBoxHeight = FUIRenderer.ScaleLineHeight(32f);
            var nameBounds = new SKRect(leftMargin, y, rightMargin, y + nameBoxHeight);
            using var nameBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(30) };
            canvas.DrawRoundRect(nameBounds, 4, 4, nameBgPaint);

            using var nameFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active, StrokeWidth = 1f };
            canvas.DrawRoundRect(nameBounds, 4, 4, nameFramePaint);

            float nameTextY = y + (nameBoxHeight - FUIRenderer.ScaleFont(FUIRenderer.FontBody)) / 2 + FUIRenderer.ScaleFont(FUIRenderer.FontBody) - 3;
            FUIRenderer.DrawText(canvas, profile.Name, new SKPoint(leftMargin + 10, nameTextY), FUIColors.TextBright, FUIRenderer.FontBody, true);
            y += nameBoxHeight + FUIRenderer.ScaleLineHeight(24f);

            float lineHeight = metrics.RowHeight;
            y = FUIRenderer.DrawSectionHeader(canvas, "STATISTICS", leftMargin, y);

            FUIWidgets.DrawProfileStat(canvas, leftMargin, y, "Axis Mappings", profile.AxisMappings.Count.ToString());
            y += lineHeight;
            FUIWidgets.DrawProfileStat(canvas, leftMargin, y, "Button Mappings", profile.ButtonMappings.Count.ToString());
            y += lineHeight;
            FUIWidgets.DrawProfileStat(canvas, leftMargin, y, "Hat Mappings", profile.HatMappings.Count.ToString());
            y += lineHeight;
            FUIWidgets.DrawProfileStat(canvas, leftMargin, y, "Shift Layers", profile.ShiftLayers.Count.ToString());
            y += lineHeight + FUIRenderer.ScaleSpacing(6f);

            FUIWidgets.DrawProfileStat(canvas, leftMargin, y, "Created", profile.CreatedAt.ToLocalTime().ToString("g"));
            y += lineHeight;
            FUIWidgets.DrawProfileStat(canvas, leftMargin, y, "Modified", profile.ModifiedAt.ToLocalTime().ToString("g"));
            y += lineHeight + FUIRenderer.ScaleSpacing(10f);
        }
        else
        {
            FUIRenderer.DrawText(canvas, "No profile active", new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
            y += FUIRenderer.ScaleLineHeight(40f);
        }

        y = FUIRenderer.DrawSectionHeader(canvas, "ACTIONS", leftMargin, y);

        float buttonHeight = FUIRenderer.ScaleLineHeight(28f);
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

            FUIRenderer.DrawTextCentered(canvas, "Delete Profile", _deleteProfileButtonBounds, FUIColors.Danger, 11f);
            y += buttonHeight + FUIRenderer.ScaleLineHeight(20f);

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
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Primary, 30f, 8f);

        float cornerPadding = FUIRenderer.SpaceXL;
        float y = bounds.Top + frameInset + cornerPadding;
        float leftMargin = bounds.Left + frameInset + cornerPadding;
        float rightMargin = bounds.Right - frameInset - FUIRenderer.SpaceLG;
        float contentWidth = rightMargin - leftMargin;
        float sectionSpacing = FUIRenderer.ScaleLineHeight(20f);
        float rowHeight = FUIRenderer.ScaleLineHeight(24f);
        float minControlGap = FUIRenderer.ScaleSpacing(12f);

        FUIRenderer.DrawText(canvas, "SYSTEM", new SKPoint(leftMargin, y), FUIColors.TextBright, FUIRenderer.FontBody, true);
        y += FUIRenderer.ScaleLineHeight(32f);

        // Auto-load setting
        float toggleWidth = 48f;
        float toggleHeight = 24f;
        float autoLoadLabelMaxWidth = contentWidth - toggleWidth - minControlGap;
        float autoLoadLabelY = y + (rowHeight - FUIRenderer.ScaleFont(11f)) / 2 + FUIRenderer.ScaleFont(11f) - 3;
        FUIRenderer.DrawTextTruncated(canvas, "Auto-load profile", new SKPoint(leftMargin, autoLoadLabelY),
            autoLoadLabelMaxWidth, FUIColors.TextPrimary, 11f);
        float toggleY = y + (rowHeight - toggleHeight) / 2;
        _autoLoadToggleBounds = new SKRect(rightMargin - toggleWidth, toggleY, rightMargin, toggleY + toggleHeight);
        FUIWidgets.DrawToggleSwitch(canvas, _autoLoadToggleBounds, _ctx.AppSettings.AutoLoadLastProfile, _ctx.MousePosition);
        y += rowHeight + sectionSpacing;

        // Close to Tray toggle
        float closeToTrayLabelMaxWidth = contentWidth - toggleWidth - minControlGap;
        float closeToTrayLabelY = y + (rowHeight - FUIRenderer.ScaleFont(11f)) / 2 + FUIRenderer.ScaleFont(11f) - 3;
        FUIRenderer.DrawTextTruncated(canvas, "Close to tray", new SKPoint(leftMargin, closeToTrayLabelY),
            closeToTrayLabelMaxWidth, FUIColors.TextPrimary, 11f);
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
            iconLabelMaxWidth, FUIColors.TextPrimary, 11f);

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

            FUIRenderer.DrawTextCentered(canvas, trayIconLabels[i], iconBounds, textColor, 10f);
        }
        y += iconBtnHeight + sectionSpacing;

        // Font size section
        FontSizeOption[] fontSizeValues = { FontSizeOption.Small, FontSizeOption.Medium, FontSizeOption.Large };
        string[] fontSizeLabels = { "-", "=", "+" };
        float fontBtnWidth = 32f;
        float fontBtnHeight = 32f;
        float fontBtnGap = 4f;
        float fontBtnsWidth = fontBtnWidth * 3 + fontBtnGap * 2;
        float fontLabelMaxWidth = contentWidth - fontBtnsWidth - minControlGap;

        string fontLabel = $"Font Size (Display: {FUIRenderer.DisplayScaleFactor:P0})";
        FUIRenderer.DrawTextTruncated(canvas, fontLabel, new SKPoint(leftMargin, y + 6),
            fontLabelMaxWidth, FUIColors.TextPrimary, 11f);

        float fontBtnsStartX = rightMargin - fontBtnsWidth;

        for (int i = 0; i < fontSizeValues.Length; i++)
        {
            var fontBounds = new SKRect(
                fontBtnsStartX + i * (fontBtnWidth + fontBtnGap), y,
                fontBtnsStartX + i * (fontBtnWidth + fontBtnGap) + fontBtnWidth, y + fontBtnHeight);

            _fontSizeButtonBounds[i] = fontBounds;

            bool isActive = _ctx.AppSettings.FontSize == fontSizeValues[i];
            bool isHovered = fontBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

            var bgColorFont = isActive
                ? FUIColors.Active.WithAlpha(60)
                : (isHovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background2);
            var frameColorFont = isActive
                ? FUIColors.Active
                : (isHovered ? FUIColors.FrameBright : FUIColors.Frame);
            var textColorFont = isActive ? FUIColors.TextBright : FUIColors.TextDim;

            using var fontBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColorFont };
            canvas.DrawRect(fontBounds, fontBgPaint);

            using var fontFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = frameColorFont, StrokeWidth = isActive ? 1.5f : 1f };
            canvas.DrawRect(fontBounds, fontFramePaint);

            FUIRenderer.DrawTextCentered(canvas, fontSizeLabels[i], fontBounds, textColorFont, 14f, scaleFont: false);
        }
        y += fontBtnHeight + sectionSpacing;

        // Font family section
        UIFontFamily[] fontFamilyValues = { UIFontFamily.Carbon, UIFontFamily.Consolas };
        string[] fontFamilyLabels = { "CARBON", "CONSOLAS" };
        float fontFamilyBtnWidth = (contentWidth - fontBtnGap) / 2;
        float fontFamilyBtnHeight = 32f;

        FUIRenderer.DrawText(canvas, "Font", new SKPoint(leftMargin, y + 6),
            FUIColors.TextPrimary, 11f);
        y += FUIRenderer.ScaleLineHeight(20f);

        for (int i = 0; i < fontFamilyValues.Length; i++)
        {
            var fontFamilyBounds = new SKRect(
                leftMargin + i * (fontFamilyBtnWidth + fontBtnGap), y,
                leftMargin + i * (fontFamilyBtnWidth + fontBtnGap) + fontFamilyBtnWidth, y + fontFamilyBtnHeight);

            _fontFamilyButtonBounds[i] = fontFamilyBounds;

            bool isActive = _ctx.AppSettings.FontFamily == fontFamilyValues[i];
            bool isHovered = fontFamilyBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

            var bgColorFF = isActive
                ? FUIColors.Active.WithAlpha(60)
                : (isHovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background2);
            var frameColorFF = isActive
                ? FUIColors.Active
                : (isHovered ? FUIColors.FrameBright : FUIColors.Frame);
            var textColorFF = isActive ? FUIColors.TextBright : FUIColors.TextDim;

            using var fontFamilyBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColorFF };
            canvas.DrawRect(fontFamilyBounds, fontFamilyBgPaint);

            using var fontFamilyFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = frameColorFF, StrokeWidth = isActive ? 1.5f : 1f };
            canvas.DrawRect(fontFamilyBounds, fontFamilyFramePaint);

            FUIRenderer.DrawTextCentered(canvas, fontFamilyLabels[i], fontFamilyBounds, textColorFF, 10f, scaleFont: false);
        }
        y += fontFamilyBtnHeight + sectionSpacing;

        // vJoy section
        FUIRenderer.DrawText(canvas, "VJOY STATUS", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += sectionSpacing;

        var devices = _ctx.VJoyService.EnumerateDevices();
        bool vjoyEnabled = devices.Count > 0;
        string vjoyStatus = vjoyEnabled ? "Driver active" : "Not available";
        var statusColor = vjoyEnabled ? FUIColors.Success : FUIColors.Danger;

        float statusTextSize = FUIRenderer.ScaleFont(11f);
        float statusLineHeight = statusTextSize + 4;
        float statusDotRadius = 4f;
        float statusDotY = y + (statusLineHeight / 2);
        float statusTextY = y + (statusLineHeight / 2) + 4;
        float statusTextX = leftMargin + statusDotRadius * 2 + 8;
        float statusMaxWidth = contentWidth - (statusTextX - leftMargin);

        using var statusDot = new SKPaint { Style = SKPaintStyle.Fill, Color = statusColor, IsAntialias = true };
        canvas.DrawCircle(leftMargin + statusDotRadius + 1, statusDotY, statusDotRadius, statusDot);
        FUIRenderer.DrawTextTruncated(canvas, vjoyStatus, new SKPoint(statusTextX, statusTextY), statusMaxWidth,
            vjoyEnabled ? FUIColors.TextPrimary : FUIColors.Danger, 11f);
        y += rowHeight;

        if (vjoyEnabled)
        {
            FUIRenderer.DrawTextTruncated(canvas, $"Available devices: {devices.Count}",
                new SKPoint(leftMargin, y), contentWidth, FUIColors.TextDim, 10f);
        }
        y += rowHeight + sectionSpacing;

        // Keyboard simulation section
        FUIRenderer.DrawText(canvas, "KEYBOARD OUTPUT", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += sectionSpacing;

        float fieldWidth = 60f;
        float fieldHeight = FUIRenderer.ScaleLineHeight(26f);
        float labelMaxWidth2 = contentWidth - fieldWidth - minControlGap;
        float textVerticalOffset = (fieldHeight - FUIRenderer.ScaleFont(11f)) / 2 + FUIRenderer.ScaleFont(11f) - 2;
        float labelY2 = y + textVerticalOffset - FUIRenderer.ScaleFont(11f) + 4;

        FUIRenderer.DrawTextTruncated(canvas, "Repeat delay (ms)", new SKPoint(leftMargin, labelY2),
            labelMaxWidth2, FUIColors.TextPrimary, 11f);
        FUIWidgets.DrawSettingsValueField(canvas, new SKRect(rightMargin - fieldWidth, y, rightMargin, y + fieldHeight), "50");
        y += fieldHeight + 8;

        labelY2 = y + textVerticalOffset - FUIRenderer.ScaleFont(11f) + 4;
        FUIRenderer.DrawTextTruncated(canvas, "Repeat rate (ms)", new SKPoint(leftMargin, labelY2),
            labelMaxWidth2, FUIColors.TextPrimary, 11f);
        FUIWidgets.DrawSettingsValueField(canvas, new SKRect(rightMargin - fieldWidth, y, rightMargin, y + fieldHeight), "30");
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
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Primary, 30f, 8f);

        float cornerPadding = FUIRenderer.SpaceXL;
        float y = bounds.Top + frameInset + cornerPadding;
        float leftMargin = bounds.Left + frameInset + cornerPadding;
        float rightMargin = bounds.Right - frameInset - FUIRenderer.SpaceLG;
        float contentWidth = rightMargin - leftMargin;
        float sectionSpacing = FUIRenderer.ScaleLineHeight(16f);

        FUIRenderer.DrawText(canvas, "VISUAL", new SKPoint(leftMargin, y), FUIColors.TextBright, FUIRenderer.FontBody, true);
        y += FUIRenderer.ScaleLineHeight(32f);

        // Theme section
        float themeLabelWidth = FUIRenderer.ScaleSpacing(36f);
        float themeAreaWidth = contentWidth - themeLabelWidth;
        float themeBtnGap = 4f;
        float themeBtnWidth = Math.Min(40f, (themeAreaWidth - themeBtnGap * 3) / 4);
        float themeBtnHeight = FUIRenderer.TouchTargetMinHeight;
        float themeBtnsStartX = leftMargin + themeLabelWidth;

        float themeLabelY = themeBtnHeight / 2 + 3;
        FUIRenderer.DrawTextTruncated(canvas, "Core", new SKPoint(leftMargin, y + themeLabelY), themeLabelWidth - 5, FUIColors.TextDim, 9f);

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

        FUIRenderer.DrawTextTruncated(canvas, "Mfr", new SKPoint(leftMargin, y + themeLabelY), themeLabelWidth - 5, FUIColors.TextDim, 9f);

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

        // Background effects section
        FUIRenderer.DrawText(canvas, "BACKGROUND", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += sectionSpacing;

        string[] sliderLabels = { "Grid", "Glow", "Noise", "Scanlines", "Vignette" };
        float maxLabelWidth = 0;
        foreach (var label in sliderLabels)
        {
            float w = FUIRenderer.MeasureText(label, 11f);
            if (w > maxLabelWidth) maxLabelWidth = w;
        }

        float labelColumnWidth = maxLabelWidth + FUIRenderer.ScaleSpacing(10f);
        float valueColumnWidth = FUIRenderer.MeasureText("100", 10f) + FUIRenderer.ScaleSpacing(8f);
        float sliderLeft = leftMargin + labelColumnWidth;
        float sliderRight = rightMargin - valueColumnWidth;
        float sliderRowHeight = FUIRenderer.ScaleLineHeight(22f);
        float sliderRowGap = FUIRenderer.ScaleSpacing(8f);

        if (sliderRight - sliderLeft < 50)
        {
            sliderLeft = leftMargin + 50;
            sliderRight = rightMargin - 30;
        }

        float sliderHeight = 12f;
        float sliderYOff = (sliderRowHeight - sliderHeight) / 2;
        float textY = sliderRowHeight / 2 + 4;

        var bg = _ctx.Background;

        FUIRenderer.DrawTextTruncated(canvas, "Grid", new SKPoint(leftMargin, y + textY), labelColumnWidth - 5, FUIColors.TextPrimary, 11f);
        _bgGridSliderBounds = new SKRect(sliderLeft, y + sliderYOff, sliderRight, y + sliderYOff + sliderHeight);
        FUIWidgets.DrawSettingsSlider(canvas, _bgGridSliderBounds, bg.GridStrength, 100);
        FUIRenderer.DrawText(canvas, bg.GridStrength.ToString(), new SKPoint(sliderRight + 8, y + textY), FUIColors.TextDim, 10f);
        y += sliderRowHeight + sliderRowGap;

        FUIRenderer.DrawTextTruncated(canvas, "Glow", new SKPoint(leftMargin, y + textY), labelColumnWidth - 5, FUIColors.TextPrimary, 11f);
        _bgGlowSliderBounds = new SKRect(sliderLeft, y + sliderYOff, sliderRight, y + sliderYOff + sliderHeight);
        FUIWidgets.DrawSettingsSlider(canvas, _bgGlowSliderBounds, bg.GlowIntensity, 100);
        FUIRenderer.DrawText(canvas, bg.GlowIntensity.ToString(), new SKPoint(sliderRight + 8, y + textY), FUIColors.TextDim, 10f);
        y += sliderRowHeight + sliderRowGap;

        FUIRenderer.DrawTextTruncated(canvas, "Noise", new SKPoint(leftMargin, y + textY), labelColumnWidth - 5, FUIColors.TextPrimary, 11f);
        _bgNoiseSliderBounds = new SKRect(sliderLeft, y + sliderYOff, sliderRight, y + sliderYOff + sliderHeight);
        FUIWidgets.DrawSettingsSlider(canvas, _bgNoiseSliderBounds, bg.NoiseIntensity, 100);
        FUIRenderer.DrawText(canvas, bg.NoiseIntensity.ToString(), new SKPoint(sliderRight + 8, y + textY), FUIColors.TextDim, 10f);
        y += sliderRowHeight + sliderRowGap;

        FUIRenderer.DrawTextTruncated(canvas, "Scanlines", new SKPoint(leftMargin, y + textY), labelColumnWidth - 5, FUIColors.TextPrimary, 11f);
        _bgScanlineSliderBounds = new SKRect(sliderLeft, y + sliderYOff, sliderRight, y + sliderYOff + sliderHeight);
        FUIWidgets.DrawSettingsSlider(canvas, _bgScanlineSliderBounds, bg.ScanlineIntensity, 100);
        FUIRenderer.DrawText(canvas, bg.ScanlineIntensity.ToString(), new SKPoint(sliderRight + 8, y + textY), FUIColors.TextDim, 10f);
        y += sliderRowHeight + sliderRowGap;

        FUIRenderer.DrawTextTruncated(canvas, "Vignette", new SKPoint(leftMargin, y + textY), labelColumnWidth - 5, FUIColors.TextPrimary, 11f);
        _bgVignetteSliderBounds = new SKRect(sliderLeft, y + sliderYOff, sliderRight, y + sliderYOff + sliderHeight);
        FUIWidgets.DrawSettingsSlider(canvas, _bgVignetteSliderBounds, bg.VignetteStrength, 100);
        FUIRenderer.DrawText(canvas, bg.VignetteStrength.ToString(), new SKPoint(sliderRight + 8, y + textY), FUIColors.TextDim, 10f);
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

        // Font size button clicks
        FontSizeOption[] fontSizes = { FontSizeOption.Small, FontSizeOption.Medium, FontSizeOption.Large };
        for (int i = 0; i < _fontSizeButtonBounds.Length; i++)
        {
            if (_fontSizeButtonBounds[i].Contains(pt))
            {
                _ctx.AppSettings.FontSize = fontSizes[i];
                FUIRenderer.FontSizeOption = fontSizes[i];
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
