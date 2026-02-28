using Asteriq.Services;
using Asteriq.Services.Abstractions;
using Asteriq.UI.Controllers;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Svg.Skia;

namespace Asteriq.UI;

public partial class MainForm
{
    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var bounds = new SKRect(0, 0, e.Info.Width, e.Info.Height);

        canvas.Clear(FUIColors.Void);

        // Apply combined DPI + text scale + user preference as a single canvas transform
        // so that ALL drawn elements scale uniformly without overflow.
        float scale = FUIRenderer.CanvasScaleFactor;
        canvas.Scale(scale);
        var scaledBounds = new SKRect(0, 0, bounds.Width / scale, bounds.Height / scale);

        DrawBackgroundLayer(canvas, scaledBounds);
        DrawStructureLayer(canvas, scaledBounds);
        DrawOverlayLayer(canvas, scaledBounds);
    }

    private void DrawBackgroundLayer(SKCanvas canvas, SKRect bounds)
    {
        int width = (int)bounds.Width;
        int height = (int)bounds.Height;

        // Check if we need to regenerate the background cache
        if (_backgroundDirty || _cachedBackground is null ||
            _cachedBackground.Width != width || _cachedBackground.Height != height)
        {
            // Dispose old cache if size changed
            if (_cachedBackground is not null && (_cachedBackground.Width != width || _cachedBackground.Height != height))
            {
                _cachedBackground.Dispose();
                _cachedBackground = null;
            }

            // Create new bitmap if needed
            _cachedBackground ??= new SKBitmap(width, height);

            // Render background to cache
            using var cacheSurface = SKSurface.Create(new SKImageInfo(width, height));
            var cacheCanvas = cacheSurface.Canvas;
            cacheCanvas.Clear(FUIColors.Void);

            // Render FUI background with all effects to cache
            _background.Render(cacheCanvas, bounds);

            // Copy to cached bitmap
            using var image = cacheSurface.Snapshot();
            using var pixmap = image.PeekPixels();
            pixmap.ReadPixels(_cachedBackground.Info, _cachedBackground.GetPixels(), _cachedBackground.RowBytes);

            _backgroundDirty = false;
        }

        // Draw cached background
        canvas.DrawBitmap(_cachedBackground, 0, 0);
    }

    private void DrawStructureLayer(SKCanvas canvas, SKRect bounds)
    {
        // Title bar
        DrawTitleBar(canvas, bounds);

        // Main content area - all values 4px aligned
        float pad = FUIRenderer.SpaceXL;  // 24px
        float contentTop = 88;  // 4px aligned
        float contentBottom = bounds.Bottom - 56;  // 4px aligned

        // Calculate responsive panel widths based on window size
        // Side-tabbed panels (Devices, Mappings) use reduced left padding
        float sideTabPad = FUIRenderer.SpaceSM;  // 8px
        float contentWidth = bounds.Width - sideTabPad - pad;
        var layout = FUIRenderer.CalculateLayout(contentWidth, minLeftPanel: 360f, minRightPanel: 280f);

        float leftPanelWidth = layout.LeftPanelWidth;
        float rightPanelWidth = layout.RightPanelWidth;
        float gap = layout.Gutter;
        float centerStart = sideTabPad + leftPanelWidth + gap;
        float centerEnd = layout.ShowRightPanel
            ? bounds.Right - pad - rightPanelWidth - gap
            : bounds.Right - pad;

        // Content based on active tab
        if (_activeTab == 1) // MAPPINGS tab
        {
            SyncTabContext();
            _mappingsController.Draw(canvas, bounds, sideTabPad, contentTop, contentBottom);
            SyncFromTabContext();
        }
        else if (_activeTab == 2) // BINDINGS tab (Star Citizen integration)
        {
            SyncTabContext();
            _scBindingsController.Draw(canvas, bounds, pad, contentTop, contentBottom);
            SyncFromTabContext();
        }
        else if (_activeTab == 3) // SETTINGS tab
        {
            SyncTabContext();
            _settingsController.Draw(canvas, bounds, pad, contentTop, contentBottom);
            SyncFromTabContext();
        }
        else
        {
            // Tab 0: DEVICES tab (delegated to controller)
            SyncTabContext();
            _devicesController.Draw(canvas, bounds, sideTabPad, contentTop, contentBottom);
            SyncFromTabContext();
        }

        // Status bar
        DrawStatusBar(canvas, bounds);

        // Draw dropdowns last (on top of everything)
        DrawOpenDropdowns(canvas);
    }

    private void DrawOpenDropdowns(SKCanvas canvas)
    {
        // Profile dropdown (rendered on top of all panels)
        if (_profileDropdownOpen)
        {
            // Get position from profile selector bounds with small gap
            DrawProfileDropdown(canvas, _profileSelectorBounds.Left, _profileSelectorBounds.Bottom + 8);
        }
    }

    private void DrawSelector(SKCanvas canvas, SKRect bounds, string text, bool isHovered, bool isEnabled)
        => FUIWidgets.DrawSelector(canvas, bounds, text, isHovered, isEnabled);

    private void DrawTextFieldReadOnly(SKCanvas canvas, SKRect bounds, string text, bool isHovered)
        => FUIWidgets.DrawTextFieldReadOnly(canvas, bounds, text, isHovered);



    private void DrawTitleBar(SKCanvas canvas, SKRect bounds)
    {
        float titleBarY = FUIRenderer.TitleBarPadding;  // 16px - was 15
        // Note: Title bar uses TitleBarHeightExpanded (48px) for the full title area
        float pad = FUIRenderer.SpaceLG;

        // Title text - aligned with left panel L-corner frame
        // Panel starts at sideTabPad(8) + sideTabWidth(28) = 36
        float titleX = 36f;

        // Measure actual title width (title uses scaled font)
        using var titlePaint = FUIRenderer.CreateTextPaint(FUIColors.Primary, 29f);
        float titleWidth = titlePaint.MeasureText("ASTERIQ");
        FUIRenderer.DrawText(canvas, "ASTERIQ", new SKPoint(titleX, titleBarY + 38), FUIColors.Primary, 29f, true);

        // Window controls - always at fixed position from right edge
        // 3 buttons at 32px + 2 gaps at 8px = 112px
        float btnTotalWidth = FUIRenderer.TouchTargetCompact * 3 + FUIRenderer.SpaceSM * 2;
        float windowControlsX = bounds.Right - pad - btnTotalWidth;

        // Navigation tabs - positioned with gap from window controls
        float tabWindowGap = FUIRenderer.Space2XL;  // 32px - was 40f
        float tabGap = 16f;  // 16px - was 15f
        using var tabMeasurePaint = FUIRenderer.CreateTextPaint(FUIColors.TextDim, 16f);

        // Calculate total tabs width by measuring each visible tab
        var visibleTabs = GetVisibleTabIndices();
        float[] tabWidths = new float[_tabNames.Length]; // keyed by semantic index
        float totalTabsWidth = 0;
        for (int vi = 0; vi < visibleTabs.Length; vi++)
        {
            int i = visibleTabs[vi];
            tabWidths[i] = tabMeasurePaint.MeasureText(_tabNames[i]);
            totalTabsWidth += tabWidths[i];
            if (vi < visibleTabs.Length - 1) totalTabsWidth += tabGap;
        }
        float tabStartX = windowControlsX - tabWindowGap - totalTabsWidth;
        _tabsStartX = tabStartX; // cache for HitTest

        // Left side elements positioning - measure actual widths
        float elementGap = 20f;  // Gap between title/subtitle/profile selector
        float subtitleX = titleX + titleWidth + elementGap;

        // Measure subtitle width
        using var subtitlePaint = FUIRenderer.CreateTextPaint(FUIColors.TextDim, 15f);
        float subtitleWidth = subtitlePaint.MeasureText("UNIFIED HOTAS MANAGEMENT SYSTEM");

        // Profile selector width scales slightly with font
        float profileSelectorWidth = 140f;
        float profileGap = 15f;

        // Subtitle - show if there's room before tabs (need space for separator line too)
        float separatorWidth = 30f; // Space for separator line
        bool showSubtitle = subtitleX + separatorWidth + subtitleWidth + elementGap + profileSelectorWidth + profileGap < tabStartX;

        // Profile selector position - after subtitle (or after title if no subtitle)
        float profileSelectorX;
        if (showSubtitle)
        {
            profileSelectorX = subtitleX + separatorWidth + subtitleWidth + elementGap;
        }
        else
        {
            profileSelectorX = titleX + titleWidth + elementGap;
        }

        // Check if profile selector fits before tabs
        bool showProfileSelector = profileSelectorX + profileSelectorWidth + profileGap < tabStartX;

        // Draw subtitle if there's room
        if (showSubtitle)
        {
            using (var sepPaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame))
            {
                canvas.DrawLine(subtitleX + 10, titleBarY + 18, subtitleX + 10, titleBarY + 48, sepPaint);
            }
            FUIRenderer.DrawText(canvas, "UNIFIED HOTAS MANAGEMENT SYSTEM", new SKPoint(subtitleX + separatorWidth, titleBarY + 38),
                FUIColors.TextDim, 15f);
        }

        // Profile selector (on the left, after subtitle or title)
        if (showProfileSelector)
        {
            DrawProfileSelector(canvas, profileSelectorX, titleBarY + 16, profileSelectorWidth);
        }

        // Draw navigation tabs (only visible ones)
        float tabX = tabStartX;
        for (int vi = 0; vi < visibleTabs.Length; vi++)
        {
            int i = visibleTabs[vi];
            bool isActive = i == _activeTab;
            var tabColor = isActive ? FUIColors.Active : FUIColors.TextDim;

            FUIRenderer.DrawText(canvas, _tabNames[i], new SKPoint(tabX, titleBarY + 38), tabColor, 16f);

            if (isActive)
            {
                using var paint = new SKPaint
                {
                    Color = FUIColors.Active,
                    StrokeWidth = 2f,
                    IsAntialias = true
                };
                canvas.DrawLine(tabX, titleBarY + 44, tabX + tabWidths[i], titleBarY + 44, paint);

                using var glowPaint = new SKPaint
                {
                    Color = FUIColors.ActiveGlow,
                    StrokeWidth = 6f,
                    ImageFilter = SKImageFilter.CreateBlur(4f, 4f)
                };
                canvas.DrawLine(tabX, titleBarY + 44, tabX + tabWidths[i], titleBarY + 44, glowPaint);
            }

            tabX += tabWidths[i] + tabGap;
        }

        // Window controls - always drawn
        FUIRenderer.DrawWindowControls(canvas, windowControlsX, titleBarY + 12,
            _hoveredWindowControl == 0, _hoveredWindowControl == 1, _hoveredWindowControl == 2);
    }

    private void DrawProfileSelector(SKCanvas canvas, float x, float y, float width)
    {
        float height = 26f;
        _profileSelectorBounds = new SKRect(x, y, x + width, y + height);

        // Get profile name
        string profileName = _profileManager.HasActiveProfile
            ? _profileManager.ActiveProfile!.Name
            : "No Profile";

        // Measure text to determine truncation (reserve space for arrow on right)
        float arrowWidth = 12f;
        float maxTextWidth = width - arrowWidth - 15f; // Space for arrow and padding
        using var measurePaint = FUIRenderer.CreateTextPaint(FUIColors.TextPrimary, 14f);
        float textWidth = measurePaint.MeasureText(profileName);

        // Truncate if too long (based on actual measurement)
        if (textWidth > maxTextWidth)
        {
            while (profileName.Length > 1 && measurePaint.MeasureText(profileName + "…") > maxTextWidth)
            {
                profileName = profileName.Substring(0, profileName.Length - 1);
            }
            profileName += "…";
        }

        // Background
        bool isHovered = _profileSelectorBounds.Contains(_mousePosition.X, _mousePosition.Y);
        using var bgPaint = FUIRenderer.CreateFillPaint(isHovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background1.WithAlpha(150));
        canvas.DrawRect(_profileSelectorBounds, bgPaint);

        // Border
        using var borderPaint = FUIRenderer.CreateStrokePaint(_profileDropdownOpen ? FUIColors.Active : (isHovered ? FUIColors.FrameBright : FUIColors.Frame));
        canvas.DrawRect(_profileSelectorBounds, borderPaint);

        // Profile name text
        float textY = y + height / 2 + 4;
        FUIRenderer.DrawText(canvas, profileName, new SKPoint(x + 8, textY),
            _profileDropdownOpen ? FUIColors.Active : FUIColors.TextPrimary, 14f);

        // Dropdown arrow on right side (custom drawn triangle)
        float arrowSize = 4f;
        float arrowX = x + width - 12f;
        float arrowY = y + height / 2;
        var arrowColor = _profileDropdownOpen ? FUIColors.Active : FUIColors.TextPrimary;

        using var arrowPaint = FUIRenderer.CreateFillPaint(arrowColor);

        using var arrowPath = new SKPath();
        arrowPath.MoveTo(arrowX - arrowSize, arrowY - arrowSize / 2);  // Top left
        arrowPath.LineTo(arrowX + arrowSize, arrowY - arrowSize / 2);  // Top right
        arrowPath.LineTo(arrowX, arrowY + arrowSize / 2);              // Bottom center
        arrowPath.Close();
        canvas.DrawPath(arrowPath, arrowPaint);

        // Note: Dropdown is drawn separately in DrawOpenDropdowns() to render on top of all panels
    }

    private void DrawProfileDropdown(SKCanvas canvas, float x, float y)
    {
        float itemHeight = 28f;  // 4px aligned
        float width = 150f;
        float padding = 8f;
        int itemCount = Math.Max(_profiles.Count + 3, 4); // +3 for "New Profile", "Import", "Export", minimum 4
        float height = itemHeight * itemCount + padding * 2 + 2; // Extra for separator

        _profileDropdownBounds = new SKRect(x, y, x + width, y + height);

        // Drop shadow with glow effect
        FUIRenderer.DrawPanelShadow(canvas, _profileDropdownBounds, 4f, 4f, 15f);

        // Outer glow (subtle)
        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active.WithAlpha(30),
            StrokeWidth = 3f,
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(4f, 4f)
        };
        canvas.DrawRect(_profileDropdownBounds, glowPaint);

        // Solid opaque background
        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Void);
        canvas.DrawRect(_profileDropdownBounds, bgPaint);

        // Inner background with slight gradient feel
        using var innerBgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background0);
        canvas.DrawRect(new SKRect(x + 2, y + 2, x + width - 2, y + height - 2), innerBgPaint);

        // L-corner frame (FUI style)
        FUIRenderer.DrawLCornerFrame(canvas, _profileDropdownBounds, FUIColors.Active.WithAlpha(180), 20f, 6f, 1.5f, true);

        // Draw profile items
        float itemY = y + padding;
        for (int i = 0; i < _profiles.Count; i++)
        {
            var profile = _profiles[i];
            var itemBounds = new SKRect(x + 4, itemY, x + width - 4, itemY + itemHeight);
            bool isHovered = _hoveredProfileIndex == i;
            bool isActive = _profileManager.ActiveProfile?.Id == profile.Id;

            // Hover background with FUI glow
            if (isHovered)
            {
                using var hoverPaint = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(40));
                canvas.DrawRect(itemBounds, hoverPaint);

                // Left accent bar on hover
                using var accentPaint = FUIRenderer.CreateFillPaint(FUIColors.Active);
                canvas.DrawRect(new SKRect(x + 4, itemY + 2, x + 6, itemY + itemHeight - 2), accentPaint);
            }

            // Active indicator (always show for active profile)
            if (isActive && !isHovered)
            {
                using var activePaint = FUIRenderer.CreateFillPaint(FUIColors.Active.WithAlpha(60));
                canvas.DrawRect(new SKRect(x + 4, itemY + 2, x + 6, itemY + itemHeight - 2), activePaint);
            }

            // Profile name
            string name = profile.Name;
            if (name.Length > 14)
                name = name.Substring(0, 13) + "…";

            var color = isActive ? FUIColors.Active : (isHovered ? FUIColors.TextBright : FUIColors.TextPrimary);
            FUIRenderer.DrawText(canvas, name, new SKPoint(x + 12, itemY + 17), color, 14f);

            itemY += itemHeight;
        }

        // Separator line before actions (FUI style)
        float sepY = itemY + 1;
        using var sepPaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame);
        canvas.DrawLine(x + 12, sepY, x + width - 12, sepY, sepPaint);

        // Corner accents on separator
        using var accentLinePaint = FUIRenderer.CreateStrokePaint(FUIColors.Active.WithAlpha(120));
        canvas.DrawLine(x + 8, sepY, x + 12, sepY, accentLinePaint);
        canvas.DrawLine(x + width - 12, sepY, x + width - 8, sepY, accentLinePaint);

        itemY += 4;

        // "New Profile" option
        DrawDropdownItem(canvas, x, itemY, width, itemHeight, "+ New Profile",
            _hoveredProfileIndex == _profiles.Count, false, true);
        itemY += itemHeight;

        // "Import" option
        DrawDropdownItem(canvas, x, itemY, width, itemHeight, "↓ Import...",
            _hoveredProfileIndex == _profiles.Count + 1, false, true);
        itemY += itemHeight;

        // "Export" option
        bool canExport = _profileManager.ActiveProfile is not null;
        DrawDropdownItem(canvas, x, itemY, width, itemHeight, "↑ Export...",
            _hoveredProfileIndex == _profiles.Count + 2, false, canExport);
    }

    private void DrawDropdownItem(SKCanvas canvas, float x, float itemY, float width, float itemHeight,
        string text, bool isHovered, bool isActive, bool isEnabled)
        => FUIWidgets.DrawDropdownItem(canvas, x, itemY, width, itemHeight, text, isHovered, isActive, isEnabled);

    private void DrawVerticalSideTab(SKCanvas canvas, SKRect bounds, string label, bool isSelected, bool isHovered)
        => FUIWidgets.DrawVerticalSideTab(canvas, bounds, label, isSelected, isHovered);

    /// <summary>
    /// Draw SVG in bounds with optional mirroring. Updates shared SVG transform state.
    /// Used by Mappings tab (will move to MappingsTabController in future extraction).
    /// </summary>
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

        _svgScale = scale;
        _svgOffset = new SKPoint(offsetX, offsetY);
        _svgMirrored = mirror;

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

    private void DrawStatusBar(SKCanvas canvas, SKRect bounds)
    {
        float y = bounds.Bottom - 40;

        // Far left: window size + mouse position in viewBox coordinates (for JSON anchor editing)
        float viewBoxX = (_mousePosition.X - _svgOffset.X) / _svgScale;
        float viewBoxY = (_mousePosition.Y - _svgOffset.Y) / _svgScale;
        string mousePos = $"WB: {(int)bounds.Width}, {(int)bounds.Height}  VB: {viewBoxX:F0}, {viewBoxY:F0}";
        float mousePosX = 40;
        FUIRenderer.DrawText(canvas, mousePos,
            new SKPoint(mousePosX, y + 22), FUIColors.TextDim, 13f);

        // Centre: connection status, anchored to true window centre
        int connectedCount = _devices.Count(d => !d.IsVirtual && d.IsConnected);
        int disconnectedCount = _devices.Count(d => !d.IsVirtual && !d.IsConnected);
        string deviceText = disconnectedCount > 0
            ? $"{connectedCount} CONNECTED, {disconnectedCount} OFFLINE"
            : connectedCount == 1 ? "1 DEVICE CONNECTED" : $"{connectedCount} DEVICES CONNECTED";
        float deviceTextWidth = FUIRenderer.MeasureText(deviceText, 15f);
        FUIRenderer.DrawText(canvas, deviceText,
            new SKPoint(bounds.MidX - deviceTextWidth / 2f, y + 22), FUIColors.TextDim, 15f);


        // Right: update indicator shape + version and time
        string versionTime = $"v{s_appVersion} | {DateTime.Now:HH:mm:ss}";
        float versionWidth = FUIRenderer.MeasureText(versionTime, 15f);
        var footerUpdateStatus = _updateService.Status;
        if (footerUpdateStatus is UpdateStatus.UpToDate or UpdateStatus.UpdateAvailable
            or UpdateStatus.Downloading or UpdateStatus.ReadyToApply)
        {
            const float indicatorSize = 7f;
            const float indicatorGap = 6f;
            float textX = bounds.Right - versionWidth - 20;
            float startX = textX - indicatorGap - indicatorSize;
            float midY = y + 17f; // visual centre of 12 pt text row

            if (footerUpdateStatus is UpdateStatus.UpToDate or UpdateStatus.ReadyToApply)
            {
                // Small filled circle — green for up-to-date / ready-to-apply
                using var dotPaint = FUIRenderer.CreateFillPaint(FUIColors.Active);
                canvas.DrawCircle(startX + indicatorSize / 2f, midY, indicatorSize / 2f, dotPaint);
            }
            else
            {
                // Downward arrow — amber for update available / downloading
                float arrowMidX = startX + indicatorSize / 2f;
                const float stemW = 2f;
                const float stemH = 5f;
                const float headH = 5f;
                float arrowTop = midY - (stemH + headH) / 2f;

                using var arrowPath = new SKPath();
                // Stem (vertical bar)
                arrowPath.MoveTo(arrowMidX - stemW / 2f, arrowTop);
                arrowPath.LineTo(arrowMidX + stemW / 2f, arrowTop);
                arrowPath.LineTo(arrowMidX + stemW / 2f, arrowTop + stemH);
                // Right wing of arrowhead
                arrowPath.LineTo(arrowMidX + indicatorSize / 2f, arrowTop + stemH);
                // Tip
                arrowPath.LineTo(arrowMidX, arrowTop + stemH + headH);
                // Left wing of arrowhead
                arrowPath.LineTo(arrowMidX - indicatorSize / 2f, arrowTop + stemH);
                arrowPath.LineTo(arrowMidX - stemW / 2f, arrowTop + stemH);
                arrowPath.Close();

                using var arrowPaint = FUIRenderer.CreateFillPaint(FUIColors.Active);
                canvas.DrawPath(arrowPath, arrowPaint);
            }

            FUIRenderer.DrawText(canvas, versionTime, new SKPoint(textX, y + 22), FUIColors.TextDim, 15f);
        }
        else
        {
            FUIRenderer.DrawText(canvas, versionTime,
                new SKPoint(bounds.Right - versionWidth - 20, y + 22), FUIColors.TextDim, 15f);
        }
    }

    private void DrawOverlayLayer(SKCanvas canvas, SKRect bounds)
    {
        // Scan line effect
        FUIRenderer.DrawScanLine(canvas, bounds, _scanLineProgress, FUIColors.Primary.WithAlpha(30), 1f);

        // CRT scan line overlay
        FUIRenderer.DrawScanLineOverlay(canvas, bounds, 2f, 4);
    }

}

