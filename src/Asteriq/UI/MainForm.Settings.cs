using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;

namespace Asteriq.UI;

/// <summary>
/// MainForm partial - Settings tab rendering and logic
/// </summary>
public partial class MainForm
{
    #region Settings Tab Drawing

    private void DrawSettingsTabContent(SKCanvas canvas, SKRect bounds, float pad, float contentTop, float contentBottom)
    {
        float frameInset = FUIRenderer.FrameInset;  // 4px - 4px aligned
        var contentBounds = new SKRect(pad, contentTop, bounds.Right - pad, contentBottom);

        // Two-panel layout: Left (profile management) | Right (application settings)
        float panelGap = FUIRenderer.SpaceLG;  // 16px - 4px aligned
        float leftPanelWidth = 400f;
        float rightPanelWidth = contentBounds.Width - leftPanelWidth - panelGap;

        var leftBounds = new SKRect(contentBounds.Left, contentBounds.Top,
            contentBounds.Left + leftPanelWidth, contentBounds.Bottom);
        var rightBounds = new SKRect(leftBounds.Right + panelGap, contentBounds.Top,
            contentBounds.Right, contentBounds.Bottom);

        // LEFT PANEL - Profile Management
        DrawProfileManagementPanel(canvas, leftBounds, frameInset);

        // RIGHT PANEL - Application Settings
        DrawApplicationSettingsPanel(canvas, rightBounds, frameInset);
    }

    private void DrawProfileManagementPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Draw panel chrome and get standard layout metrics
        var metrics = FUIRenderer.DrawPanelChrome(canvas, bounds);
        float y = metrics.Y;
        float leftMargin = metrics.LeftMargin;
        float rightMargin = metrics.RightMargin;
        float bottom = bounds.Bottom - frameInset - FUIRenderer.SpaceLG;

        // Clip content to panel bounds to prevent overflow
        canvas.Save();
        canvas.ClipRect(new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom - frameInset));

        // Panel title
        y = FUIRenderer.DrawPanelHeader(canvas, "PROFILE MANAGEMENT", leftMargin, y);

        // Current profile info
        var profile = _profileService.ActiveProfile;
        if (profile is not null)
        {
            // Active profile section
            y = FUIRenderer.DrawSectionHeader(canvas, "ACTIVE PROFILE", leftMargin, y);

            // Profile name with highlight
            float nameBoxHeight = FUIRenderer.ScaleLineHeight(32f);
            var nameBounds = new SKRect(leftMargin, y, rightMargin, y + nameBoxHeight);
            using var nameBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(30) };
            canvas.DrawRoundRect(nameBounds, 4, 4, nameBgPaint);

            using var nameFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Active, StrokeWidth = 1f };
            canvas.DrawRoundRect(nameBounds, 4, 4, nameFramePaint);

            float nameTextY = y + (nameBoxHeight - FUIRenderer.ScaleFont(FUIRenderer.FontBody)) / 2 + FUIRenderer.ScaleFont(FUIRenderer.FontBody) - 3;
            FUIRenderer.DrawText(canvas, profile.Name, new SKPoint(leftMargin + 10, nameTextY), FUIColors.TextBright, FUIRenderer.FontBody, true);
            y += nameBoxHeight + FUIRenderer.ScaleLineHeight(12f);

            // Profile stats section
            float lineHeight = metrics.RowHeight;
            y = FUIRenderer.DrawSectionHeader(canvas, "STATISTICS", leftMargin, y);

            DrawProfileStat(canvas, leftMargin, y, "Axis Mappings", profile.AxisMappings.Count.ToString());
            y += lineHeight;
            DrawProfileStat(canvas, leftMargin, y, "Button Mappings", profile.ButtonMappings.Count.ToString());
            y += lineHeight;
            DrawProfileStat(canvas, leftMargin, y, "Hat Mappings", profile.HatMappings.Count.ToString());
            y += lineHeight;
            DrawProfileStat(canvas, leftMargin, y, "Shift Layers", profile.ShiftLayers.Count.ToString());
            y += lineHeight + FUIRenderer.ScaleSpacing(6f);

            // Timestamps
            DrawProfileStat(canvas, leftMargin, y, "Created", profile.CreatedAt.ToLocalTime().ToString("g"));
            y += lineHeight;
            DrawProfileStat(canvas, leftMargin, y, "Modified", profile.ModifiedAt.ToLocalTime().ToString("g"));
            y += lineHeight + FUIRenderer.ScaleSpacing(10f);
        }
        else
        {
            // No profile active
            FUIRenderer.DrawText(canvas, "No profile active", new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
            y += FUIRenderer.ScaleLineHeight(40f);
        }

        // Actions section
        y = FUIRenderer.DrawSectionHeader(canvas, "ACTIONS", leftMargin, y);

        // Action buttons - scale height for larger fonts
        float buttonHeight = FUIRenderer.ScaleLineHeight(28f);
        float buttonGap = FUIRenderer.SpaceSM;
        float buttonWidth = (metrics.ContentWidth - buttonGap) / 2;

        // New Profile button
        DrawSettingsButton(canvas, new SKRect(leftMargin, y, leftMargin + buttonWidth, y + buttonHeight), "New Profile", false);
        // Duplicate button
        DrawSettingsButton(canvas, new SKRect(rightMargin - buttonWidth, y, rightMargin, y + buttonHeight),
            profile is not null ? "Duplicate" : "---", profile is null);
        y += buttonHeight + buttonGap;

        // Import/Export buttons
        DrawSettingsButton(canvas, new SKRect(leftMargin, y, leftMargin + buttonWidth, y + buttonHeight), "Import", false);
        DrawSettingsButton(canvas, new SKRect(rightMargin - buttonWidth, y, rightMargin, y + buttonHeight),
            profile is not null ? "Export" : "---", profile is null);
        y += buttonHeight + buttonGap;

        // Delete button (danger) - only draw if space available
        if (profile is not null && y + buttonHeight <= bottom)
        {
            var deleteBounds = new SKRect(leftMargin, y, rightMargin, y + buttonHeight);
            using var delBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Danger.WithAlpha(30) };
            canvas.DrawRoundRect(deleteBounds, 4, 4, delBgPaint);

            using var delFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Danger.WithAlpha(150), StrokeWidth = 1f };
            canvas.DrawRoundRect(deleteBounds, 4, 4, delFramePaint);

            FUIRenderer.DrawTextCentered(canvas, "Delete Profile", deleteBounds, FUIColors.Danger, 11f);
            y += buttonHeight + FUIRenderer.ScaleLineHeight(20f);

            // Shift Layers section (only if space available)
            if (y < bottom - 60)
            {
                DrawShiftLayersSection(canvas, leftMargin, rightMargin, y, bottom, profile);
            }
        }

        // Restore clip state
        canvas.Restore();
    }

    private void DrawShiftLayersSection(SKCanvas canvas, float leftMargin, float rightMargin, float y, float bottom, MappingProfile profile)
    {
        float width = rightMargin - leftMargin;
        float lineHeight = FUIRenderer.ScaleLineHeight(16f);

        // Section header
        FUIRenderer.DrawText(canvas, "SHIFT LAYERS", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += lineHeight;

        // Shift layers explanation
        FUIRenderer.DrawText(canvas, "Hold a button to activate alternative mappings", new SKPoint(leftMargin, y), FUIColors.TextDim, 9f);
        y += lineHeight + 4;

        // List existing shift layers
        float layerRowHeight = FUIRenderer.TouchTargetStandard;  // 40px for standard touch targets
        foreach (var layer in profile.ShiftLayers)
        {
            if (y + layerRowHeight > bottom - 50) break;

            // Layer row background
            var rowBounds = new SKRect(leftMargin, y, rightMargin, y + layerRowHeight - 4);
            using var rowBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
            canvas.DrawRoundRect(rowBounds, 4, 4, rowBgPaint);

            using var rowFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
            canvas.DrawRoundRect(rowBounds, 4, 4, rowFramePaint);

            // Layer name
            FUIRenderer.DrawText(canvas, layer.Name, new SKPoint(leftMargin + 10, y + 11), FUIColors.TextPrimary, 11f);

            // Activator button info
            string activatorText = layer.ActivatorButton is not null
                ? $"Button {layer.ActivatorButton.Index + 1} on {layer.ActivatorButton.DeviceName}"
                : "Not assigned";
            FUIRenderer.DrawText(canvas, activatorText, new SKPoint(leftMargin + 100, y + 11),
                layer.ActivatorButton is not null ? FUIColors.TextDim : FUIColors.Warning.WithAlpha(150), 9f);

            // Delete button
            float delSize = 20f;
            var delBounds = new SKRect(rightMargin - delSize - 8, y + (layerRowHeight - delSize) / 2 - 2,
                rightMargin - 8, y + (layerRowHeight + delSize) / 2 - 2);

            using var delPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Danger.WithAlpha(60) };
            canvas.DrawRoundRect(delBounds, 2, 2, delPaint);
            FUIRenderer.DrawTextCentered(canvas, "x", delBounds, FUIColors.Danger, 12f);

            y += layerRowHeight;
        }

        // Add new layer button
        if (y + 36 < bottom)
        {
            var addBounds = new SKRect(leftMargin, y, rightMargin, y + 30);
            using var addBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Success.WithAlpha(20) };
            canvas.DrawRoundRect(addBounds, 4, 4, addBgPaint);

            using var addFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Success.WithAlpha(100), StrokeWidth = 1f };
            canvas.DrawRoundRect(addBounds, 4, 4, addFramePaint);

            FUIRenderer.DrawTextCentered(canvas, "+ Add Shift Layer", addBounds, FUIColors.Success, 11f);
        }
    }

    private void DrawProfileStat(SKCanvas canvas, float x, float y, string label, string value, float valueOffset = 130f)
    {
        FUIRenderer.DrawText(canvas, label, new SKPoint(x, y), FUIColors.TextDim, 10f);
        FUIRenderer.DrawText(canvas, value, new SKPoint(x + valueOffset, y), FUIColors.TextPrimary, 10f);
    }

    private void DrawSettingsButton(SKCanvas canvas, SKRect bounds, string text, bool disabled)
    {
        var bgColor = disabled ? FUIColors.Background2.WithAlpha(100) : FUIColors.Background2;
        var frameColor = disabled ? FUIColors.Frame.WithAlpha(80) : FUIColors.Frame;
        var textColor = disabled ? FUIColors.TextDim.WithAlpha(100) : FUIColors.TextPrimary;

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRoundRect(bounds, 4, 4, bgPaint);

        using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = frameColor, StrokeWidth = 1f };
        canvas.DrawRoundRect(bounds, 4, 4, framePaint);

        FUIRenderer.DrawTextCentered(canvas, text, bounds, textColor, 11f);
    }

    private void DrawApplicationSettingsPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Split into left (system settings) and right (visual settings) sub-panels
        float gap = FUIRenderer.SpaceSM;  // 8px - was 10f
        float leftWidth = (bounds.Width - gap) * 0.52f;  // System settings - more space for labels
        float rightWidth = (bounds.Width - gap) * 0.48f; // Visual settings

        var leftBounds = new SKRect(bounds.Left, bounds.Top, bounds.Left + leftWidth, bounds.Bottom);
        var rightBounds = new SKRect(bounds.Left + leftWidth + gap, bounds.Top, bounds.Right, bounds.Bottom);

        // LEFT: System Settings
        DrawSystemSettingsSubPanel(canvas, leftBounds, frameInset);

        // RIGHT: Visual Settings (Theme, Background)
        DrawVisualSettingsSubPanel(canvas, rightBounds, frameInset);
    }

    private void DrawSystemSettingsSubPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(160),
            IsAntialias = true
        };
        canvas.DrawRect(bounds.Inset(frameInset, frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Primary, 30f, 8f);

        float cornerPadding = FUIRenderer.SpaceXL;  // 24px - was 20f
        float y = bounds.Top + frameInset + cornerPadding;
        float leftMargin = bounds.Left + frameInset + cornerPadding;
        float rightMargin = bounds.Right - frameInset - FUIRenderer.SpaceLG;  // 16px - was 15
        float contentWidth = rightMargin - leftMargin;
        float sectionSpacing = FUIRenderer.ScaleLineHeight(20f);
        float rowHeight = FUIRenderer.ScaleLineHeight(24f);
        float minControlGap = FUIRenderer.ScaleSpacing(12f);  // 12px - was 10f

        // Title - using FontBody (14f) with glow for panel titles
        FUIRenderer.DrawText(canvas, "SYSTEM", new SKPoint(leftMargin, y), FUIColors.TextBright, FUIRenderer.FontBody, true);
        y += FUIRenderer.ScaleLineHeight(32f);

        // Auto-load setting - toggle has fixed height for proper capsule shape
        float toggleWidth = 48f;   // 4px aligned - was 45f
        float toggleHeight = 24f;  // 4px aligned - was 22f, meets TouchTargetMinHeight
        float autoLoadLabelMaxWidth = contentWidth - toggleWidth - minControlGap;
        float autoLoadLabelY = y + (rowHeight - FUIRenderer.ScaleFont(11f)) / 2 + FUIRenderer.ScaleFont(11f) - 3;
        FUIRenderer.DrawTextTruncated(canvas, "Auto-load profile", new SKPoint(leftMargin, autoLoadLabelY),
            autoLoadLabelMaxWidth, FUIColors.TextPrimary, 11f);
        float toggleY = y + (rowHeight - toggleHeight) / 2;  // Center toggle in row
        _autoLoadToggleBounds = new SKRect(rightMargin - toggleWidth, toggleY, rightMargin, toggleY + toggleHeight);
        DrawToggleSwitch(canvas, _autoLoadToggleBounds, _profileService.AutoLoadLastProfile);
        y += rowHeight + sectionSpacing;

        // Font size section - show Windows scale factor and adjustment buttons
        FontSizeOption[] fontSizeValues = { FontSizeOption.Small, FontSizeOption.Medium, FontSizeOption.Large };
        string[] fontSizeLabels = { "-", "=", "+" };
        float fontBtnWidth = 32f;   // 4px aligned, meets TouchTargetCompact - was 28f
        float fontBtnHeight = 32f;  // 4px aligned, meets TouchTargetCompact - was 24f
        float fontBtnGap = 4f;      // 4px aligned - was 3f
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

            bool isActive = _profileService.FontSize == fontSizeValues[i];
            var bgColor = isActive ? FUIColors.Active.WithAlpha(60) : FUIColors.Background2;
            var frameColor = isActive ? FUIColors.Active : FUIColors.Frame;
            var textColor = isActive ? FUIColors.TextBright : FUIColors.TextDim;

            using var fontBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
            canvas.DrawRect(fontBounds, fontBgPaint);

            using var fontFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = frameColor, StrokeWidth = isActive ? 1.5f : 1f };
            canvas.DrawRect(fontBounds, fontFramePaint);

            FUIRenderer.DrawTextCentered(canvas, fontSizeLabels[i], fontBounds, textColor, 14f, scaleFont: false);
        }
        y += fontBtnHeight + sectionSpacing;

        // vJoy section
        FUIRenderer.DrawText(canvas, "VJOY STATUS", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += sectionSpacing;

        var devices = _vjoyService.EnumerateDevices();
        bool vjoyEnabled = devices.Count > 0;
        string vjoyStatus = vjoyEnabled ? "Driver active" : "Not available";
        var statusColor = vjoyEnabled ? FUIColors.Success : FUIColors.Danger;

        // Measure text height for proper vertical centering of dot
        float statusTextSize = FUIRenderer.ScaleFont(11f);
        float statusLineHeight = statusTextSize + 4;
        float statusDotRadius = 4f;
        float statusDotY = y + (statusLineHeight / 2);
        float statusTextX = leftMargin + statusDotRadius * 2 + 8;
        float statusMaxWidth = contentWidth - (statusTextX - leftMargin);

        using var statusDot = new SKPaint { Style = SKPaintStyle.Fill, Color = statusColor, IsAntialias = true };
        canvas.DrawCircle(leftMargin + statusDotRadius + 1, statusDotY, statusDotRadius, statusDot);
        FUIRenderer.DrawTextTruncated(canvas, vjoyStatus, new SKPoint(statusTextX, y), statusMaxWidth,
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
        float labelMaxWidth = contentWidth - fieldWidth - minControlGap;
        float textVerticalOffset = (fieldHeight - FUIRenderer.ScaleFont(11f)) / 2 + FUIRenderer.ScaleFont(11f) - 2;
        float labelY = y + textVerticalOffset - FUIRenderer.ScaleFont(11f) + 4;

        FUIRenderer.DrawTextTruncated(canvas, "Repeat delay (ms)", new SKPoint(leftMargin, labelY),
            labelMaxWidth, FUIColors.TextPrimary, 11f);
        DrawSettingsValueField(canvas, new SKRect(rightMargin - fieldWidth, y, rightMargin, y + fieldHeight), "50");
        y += fieldHeight + 8;

        labelY = y + textVerticalOffset - FUIRenderer.ScaleFont(11f) + 4;
        FUIRenderer.DrawTextTruncated(canvas, "Repeat rate (ms)", new SKPoint(leftMargin, labelY),
            labelMaxWidth, FUIColors.TextPrimary, 11f);
        DrawSettingsValueField(canvas, new SKRect(rightMargin - fieldWidth, y, rightMargin, y + fieldHeight), "30");
    }

    private void DrawVisualSettingsSubPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(160),
            IsAntialias = true
        };
        canvas.DrawRect(bounds.Inset(frameInset, frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Primary, 30f, 8f);

        float cornerPadding = FUIRenderer.SpaceXL;  // 24px - was 20f
        float y = bounds.Top + frameInset + cornerPadding;
        float leftMargin = bounds.Left + frameInset + cornerPadding;
        float rightMargin = bounds.Right - frameInset - FUIRenderer.SpaceLG;  // 16px - was 15
        float contentWidth = rightMargin - leftMargin;
        float sectionSpacing = FUIRenderer.ScaleLineHeight(16f);

        // Title - using FontBody (14f) with glow for panel titles
        FUIRenderer.DrawText(canvas, "VISUAL", new SKPoint(leftMargin, y), FUIColors.TextBright, FUIRenderer.FontBody, true);
        y += FUIRenderer.ScaleLineHeight(32f);

        // Theme section - calculate button sizes based on available width
        float themeLabelWidth = FUIRenderer.ScaleSpacing(36f);  // 36px - was 35f
        float themeAreaWidth = contentWidth - themeLabelWidth;
        float themeBtnGap = 4f;  // 4px - was 3f
        float themeBtnWidth = Math.Min(40f, (themeAreaWidth - themeBtnGap * 3) / 4);  // 40px max - was 38f
        float themeBtnHeight = FUIRenderer.TouchTargetMinHeight;  // 24px minimum for touch
        float themeBtnsStartX = leftMargin + themeLabelWidth;

        // Core themes
        FUIRenderer.DrawTextTruncated(canvas, "Core", new SKPoint(leftMargin, y + 4), themeLabelWidth - 5, FUIColors.TextDim, 9f);

        FUITheme[] coreThemes = { FUITheme.Midnight, FUITheme.Matrix, FUITheme.Amber, FUITheme.Ice };
        string[] coreNames = { "MID", "MTX", "AMB", "ICE" };
        SKColor[] coreColors = {
            new SKColor(0x40, 0xA0, 0xFF),
            new SKColor(0x40, 0xFF, 0x40),
            new SKColor(0xFF, 0xA0, 0x40),
            new SKColor(0x40, 0xE0, 0xFF)
        };

        for (int i = 0; i < coreThemes.Length; i++)
        {
            var themeBounds = new SKRect(
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap), y,
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap) + themeBtnWidth, y + themeBtnHeight);

            StoreThemeButtonBounds(i, themeBounds);
            DrawThemeButton(canvas, themeBounds, coreNames[i], coreColors[i], FUIColors.CurrentTheme == coreThemes[i]);
        }
        y += themeBtnHeight + 6;

        // Manufacturer themes - Row 1
        FUIRenderer.DrawTextTruncated(canvas, "Mfr", new SKPoint(leftMargin, y + 4), themeLabelWidth - 5, FUIColors.TextDim, 9f);

        FUITheme[] mfrThemes1 = { FUITheme.Drake, FUITheme.Aegis, FUITheme.Anvil, FUITheme.Argo };
        string[] mfrNames1 = { "DRK", "AEG", "ANV", "ARG" };
        SKColor[] mfrColors1 = {
            new SKColor(0xFF, 0x80, 0x20),
            new SKColor(0x40, 0x90, 0xE0),
            new SKColor(0x90, 0xC0, 0x40),
            new SKColor(0xFF, 0xC0, 0x00)
        };

        for (int i = 0; i < mfrThemes1.Length; i++)
        {
            var themeBounds = new SKRect(
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap), y,
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap) + themeBtnWidth, y + themeBtnHeight);

            StoreThemeButtonBounds(4 + i, themeBounds);
            DrawThemeButton(canvas, themeBounds, mfrNames1[i], mfrColors1[i], FUIColors.CurrentTheme == mfrThemes1[i]);
        }
        y += themeBtnHeight + 4;

        // Manufacturer themes - Row 2
        FUITheme[] mfrThemes2 = { FUITheme.Crusader, FUITheme.Origin, FUITheme.MISC, FUITheme.RSI };
        string[] mfrNames2 = { "CRU", "ORI", "MSC", "RSI" };
        SKColor[] mfrColors2 = {
            new SKColor(0x40, 0x90, 0xE0),
            new SKColor(0xD4, 0xAF, 0x37),
            new SKColor(0x40, 0xC0, 0x90),
            new SKColor(0x50, 0xA0, 0xF0)
        };

        for (int i = 0; i < mfrThemes2.Length; i++)
        {
            var themeBounds = new SKRect(
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap), y,
                themeBtnsStartX + i * (themeBtnWidth + themeBtnGap) + themeBtnWidth, y + themeBtnHeight);

            StoreThemeButtonBounds(8 + i, themeBounds);
            DrawThemeButton(canvas, themeBounds, mfrNames2[i], mfrColors2[i], FUIColors.CurrentTheme == mfrThemes2[i]);
        }
        y += themeBtnHeight + sectionSpacing;

        // Background effects section
        FUIRenderer.DrawText(canvas, "BACKGROUND", new SKPoint(leftMargin, y), FUIColors.TextDim, 10f);
        y += sectionSpacing;

        // Calculate slider layout dynamically
        // Find the longest label to determine label column width
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

        // Ensure slider has minimum width
        if (sliderRight - sliderLeft < 50)
        {
            sliderLeft = leftMargin + 50;
            sliderRight = rightMargin - 30;
        }

        // Grid strength slider
        FUIRenderer.DrawTextTruncated(canvas, "Grid", new SKPoint(leftMargin, y + 5), labelColumnWidth - 5, FUIColors.TextPrimary, 11f);
        _bgGridSliderBounds = new SKRect(sliderLeft, y + 3, sliderRight, y + sliderRowHeight - 3);
        DrawSettingsSlider(canvas, _bgGridSliderBounds, _background.GridStrength, 100);
        FUIRenderer.DrawText(canvas, _background.GridStrength.ToString(), new SKPoint(sliderRight + 8, y + 5), FUIColors.TextDim, 10f);
        y += sliderRowHeight + sliderRowGap;

        // Glow intensity slider
        FUIRenderer.DrawTextTruncated(canvas, "Glow", new SKPoint(leftMargin, y + 5), labelColumnWidth - 5, FUIColors.TextPrimary, 11f);
        _bgGlowSliderBounds = new SKRect(sliderLeft, y + 3, sliderRight, y + sliderRowHeight - 3);
        DrawSettingsSlider(canvas, _bgGlowSliderBounds, _background.GlowIntensity, 100);
        FUIRenderer.DrawText(canvas, _background.GlowIntensity.ToString(), new SKPoint(sliderRight + 8, y + 5), FUIColors.TextDim, 10f);
        y += sliderRowHeight + sliderRowGap;

        // Noise intensity slider
        FUIRenderer.DrawTextTruncated(canvas, "Noise", new SKPoint(leftMargin, y + 5), labelColumnWidth - 5, FUIColors.TextPrimary, 11f);
        _bgNoiseSliderBounds = new SKRect(sliderLeft, y + 3, sliderRight, y + sliderRowHeight - 3);
        DrawSettingsSlider(canvas, _bgNoiseSliderBounds, _background.NoiseIntensity, 100);
        FUIRenderer.DrawText(canvas, _background.NoiseIntensity.ToString(), new SKPoint(sliderRight + 8, y + 5), FUIColors.TextDim, 10f);
        y += sliderRowHeight + sliderRowGap;

        // Scanline intensity slider
        FUIRenderer.DrawTextTruncated(canvas, "Scanlines", new SKPoint(leftMargin, y + 5), labelColumnWidth - 5, FUIColors.TextPrimary, 11f);
        _bgScanlineSliderBounds = new SKRect(sliderLeft, y + 3, sliderRight, y + sliderRowHeight - 3);
        DrawSettingsSlider(canvas, _bgScanlineSliderBounds, _background.ScanlineIntensity, 100);
        FUIRenderer.DrawText(canvas, _background.ScanlineIntensity.ToString(), new SKPoint(sliderRight + 8, y + 5), FUIColors.TextDim, 10f);
        y += sliderRowHeight + sliderRowGap;

        // Vignette intensity slider
        FUIRenderer.DrawTextTruncated(canvas, "Vignette", new SKPoint(leftMargin, y + 5), labelColumnWidth - 5, FUIColors.TextPrimary, 11f);
        _bgVignetteSliderBounds = new SKRect(sliderLeft, y + 3, sliderRight, y + sliderRowHeight - 3);
        DrawSettingsSlider(canvas, _bgVignetteSliderBounds, _background.VignetteStrength, 100);
        FUIRenderer.DrawText(canvas, _background.VignetteStrength.ToString(), new SKPoint(sliderRight + 8, y + 5), FUIColors.TextDim, 10f);
    }

    private void DrawSettingsValueField(SKCanvas canvas, SKRect bounds, string value)
    {
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        canvas.DrawRoundRect(bounds, 3, 3, bgPaint);

        using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
        canvas.DrawRoundRect(bounds, 3, 3, framePaint);

        FUIRenderer.DrawTextCentered(canvas, value, bounds, FUIColors.TextPrimary, 11f);
    }

    private void StoreThemeButtonBounds(int index, SKRect bounds)
    {
        if (index >= 0 && index < _themeButtonBounds.Length)
        {
            _themeButtonBounds[index] = bounds;
        }
    }

    private void DrawThemeButton(SKCanvas canvas, SKRect bounds, string name, SKColor previewColor, bool isActive)
    {
        var bgColor = isActive ? previewColor.WithAlpha(60) : FUIColors.Background2;
        var frameColor = isActive ? previewColor : FUIColors.Frame;
        var textColor = isActive ? FUIColors.TextBright : FUIColors.TextDim;

        using var themeBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, themeBgPaint);

        using var themeFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = frameColor, StrokeWidth = isActive ? 1.5f : 1f };
        canvas.DrawRect(bounds, themeFramePaint);

        FUIRenderer.DrawTextCentered(canvas, name, bounds, textColor, 8f);

        var indicatorBounds = new SKRect(bounds.Left + 2, bounds.Bottom - 2,
            bounds.Right - 2, bounds.Bottom - 1);
        using var indicatorPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = previewColor.WithAlpha((byte)(isActive ? 200 : 100)) };
        canvas.DrawRect(indicatorBounds, indicatorPaint);
    }

    private void HandleSettingsTabClick(SKPoint pt)
    {
        // Check font size button clicks
        FontSizeOption[] fontSizes = { FontSizeOption.Small, FontSizeOption.Medium, FontSizeOption.Large };
        for (int i = 0; i < _fontSizeButtonBounds.Length; i++)
        {
            if (_fontSizeButtonBounds[i].Contains(pt))
            {
                _profileService.FontSize = fontSizes[i];
                FUIRenderer.FontSizeOption = fontSizes[i];
                _canvas.Invalidate();
                return;
            }
        }

        // Check theme button clicks
        FUITheme[] themes = {
            // Core themes (0-3)
            FUITheme.Midnight, FUITheme.Matrix, FUITheme.Amber, FUITheme.Ice,
            // Manufacturer themes row 1 (4-7)
            FUITheme.Drake, FUITheme.Aegis, FUITheme.Anvil, FUITheme.Argo,
            // Manufacturer themes row 2 (8-11)
            FUITheme.Crusader, FUITheme.Origin, FUITheme.MISC, FUITheme.RSI
        };
        for (int i = 0; i < _themeButtonBounds.Length && i < themes.Length; i++)
        {
            if (_themeButtonBounds[i].Contains(pt))
            {
                FUIColors.SetTheme(themes[i]);
                _profileService.Theme = themes[i];
                _canvas.Invalidate();
                return;
            }
        }

        // Check auto-load toggle click
        if (_autoLoadToggleBounds.Contains(pt))
        {
            _profileService.AutoLoadLastProfile = !_profileService.AutoLoadLastProfile;
            _canvas.Invalidate();
            return;
        }

        // Check background slider clicks (start drag)
        if (_bgGridSliderBounds.Contains(pt))
        {
            _draggingBgSlider = "grid";
            UpdateBgSliderFromPoint(pt.X);
            return;
        }
        if (_bgGlowSliderBounds.Contains(pt))
        {
            _draggingBgSlider = "glow";
            UpdateBgSliderFromPoint(pt.X);
            return;
        }
        if (_bgNoiseSliderBounds.Contains(pt))
        {
            _draggingBgSlider = "noise";
            UpdateBgSliderFromPoint(pt.X);
            return;
        }
        if (_bgScanlineSliderBounds.Contains(pt))
        {
            _draggingBgSlider = "scanline";
            UpdateBgSliderFromPoint(pt.X);
            return;
        }
        if (_bgVignetteSliderBounds.Contains(pt))
        {
            _draggingBgSlider = "vignette";
            UpdateBgSliderFromPoint(pt.X);
            return;
        }
    }

    private void UpdateBgSliderFromPoint(float x)
    {
        SKRect bounds;
        switch (_draggingBgSlider)
        {
            case "grid": bounds = _bgGridSliderBounds; break;
            case "glow": bounds = _bgGlowSliderBounds; break;
            case "noise": bounds = _bgNoiseSliderBounds; break;
            case "scanline": bounds = _bgScanlineSliderBounds; break;
            case "vignette": bounds = _bgVignetteSliderBounds; break;
            default: return;
        }

        float ratio = Math.Clamp((x - bounds.Left) / bounds.Width, 0f, 1f);
        int value = (int)(ratio * 100);

        switch (_draggingBgSlider)
        {
            case "grid": _background.GridStrength = value; break;
            case "glow": _background.GlowIntensity = value; break;
            case "noise": _background.NoiseIntensity = value; break;
            case "scanline": _background.ScanlineIntensity = value; break;
            case "vignette": _background.VignetteStrength = value; break;
        }

        _canvas.Invalidate();
    }

    private void SaveBackgroundSettings()
    {
        _profileService.SaveBackgroundSettings(
            _background.GridStrength,
            _background.GlowIntensity,
            _background.NoiseIntensity,
            _background.ScanlineIntensity,
            _background.VignetteStrength);
    }

    #endregion
}
