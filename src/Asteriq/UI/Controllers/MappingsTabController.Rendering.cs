using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
    private void ApplySvgTransform(FUIRenderer.SvgTransform t, bool mirror)
    {
        _ctx.SvgScale = t.Scale;
        _ctx.SvgOffset = t.Offset;
        _ctx.SvgMirrored = mirror;
        _ctx.SilhouetteSourceWidth = t.SourceWidth;
    }

    private void DrawMappingsTabContent(SKCanvas canvas, SKRect bounds, float sideTabPad, float contentTop, float contentBottom)
    {
        float frameInset = 5f;
        float pad = FUIRenderer.SpaceLG;  // Standard padding for right side
        var contentBounds = new SKRect(sideTabPad, contentTop, bounds.Right - pad, contentBottom);

        // Three-panel layout: Left (bindings list) | Center (device view) | Right (settings)
        var layout = FUIRenderer.CalculateLayout(contentBounds.Width, minLeftPanel: 360f, minRightPanel: 280f, maxSidePanel: 500f);
        float gap = layout.Gutter;

        var leftBounds = new SKRect(contentBounds.Left, contentBounds.Top,
            contentBounds.Left + layout.LeftPanelWidth, contentBounds.Bottom);
        var centerBounds = new SKRect(leftBounds.Right + gap, contentBounds.Top,
            leftBounds.Right + gap + layout.CenterWidth, contentBounds.Bottom);
        var rightBounds = new SKRect(centerBounds.Right + gap, contentBounds.Top,
            contentBounds.Right, contentBounds.Bottom);

        // Refresh vJoy devices list
        if (_ctx.VJoyDevices.Count == 0)
        {
            _ctx.VJoyDevices = _ctx.VJoyService.EnumerateDevices();
        }

        // LEFT PANEL - Bindings List
        DrawBindingsPanel(canvas, leftBounds, frameInset);

        // CENTER PANEL - Device Visualization
        DrawDeviceVisualizationPanel(canvas, centerBounds, frameInset);

        // RIGHT PANEL — Mapping Settings
        DrawMappingSettingsPanel(canvas, rightBounds, frameInset);
    }

    private void DrawDeviceVisualizationPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background1.WithAlpha(100));
        canvas.DrawRect(new SKRect(bounds.Left + frameInset, bounds.Top + frameInset,
            bounds.Right - frameInset, bounds.Bottom - frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame.WithAlpha(150), 30f, 8f);

        // Show device silhouette - use primary device's map if available
        float centerX = bounds.MidX;

        // Get the appropriate image based on primary device map.
        // Resolve bitmap before applying the JoystickSvg fallback — otherwise bitmap-based maps never render.
        var svg = _ctx.GetSvgForDeviceMap?.Invoke(_ctx.MappingsPrimaryDeviceMap);
        var bitmap = svg is null ? _ctx.GetBitmapForDeviceMap?.Invoke(_ctx.MappingsPrimaryDeviceMap) : null;
        if (svg is null && bitmap is null)
            svg = _ctx.JoystickSvg;
        bool shouldMirror = _ctx.MappingsPrimaryDeviceMap?.Mirror ?? false;

        // Device name label at top of panel
        float labelRowHeight = 20f;
        float labelY = bounds.Top + frameInset + labelRowHeight;
        string deviceLabel = _ctx.MappingsPrimaryDeviceMap?.Device ?? "—";
        FUIRenderer.DrawTextCentered(canvas, deviceLabel,
            new SKRect(bounds.Left, bounds.Top + frameInset, bounds.Right, labelY),
            FUIColors.TextDim, 13f);

        // Reserve space at the bottom for the auto-scroll checkbox row
        float checkboxRowHeight = 26f;
        float checkboxAreaTop = bounds.Bottom - frameInset - checkboxRowHeight;

        // Constrained bounds for silhouette (shared between SVG and bitmap paths)
        float maxSize = 900f;
        float maxWidth = Math.Min(bounds.Width - 40, maxSize);
        float maxHeight = Math.Min(bounds.Height - 40 - checkboxRowHeight - labelRowHeight, maxSize);
        float constrainedWidth = Math.Min(maxWidth, maxHeight);
        float constrainedHeight = constrainedWidth;
        float availableCenterY = labelY + (checkboxAreaTop - labelY) / 2f;
        var constrainedBounds = new SKRect(
            centerX - constrainedWidth / 2,
            availableCenterY - constrainedHeight / 2,
            centerX + constrainedWidth / 2,
            availableCenterY + constrainedHeight / 2
        );

        if (svg?.Picture is not null)
        {
            _ctx.SilhouetteBounds = constrainedBounds;
            var t = FUIRenderer.DrawSvgInBounds(canvas, svg, constrainedBounds, shouldMirror);
            ApplySvgTransform(t, shouldMirror);
            DrawMappingHighlightLeadLine(canvas, constrainedBounds);
        }
        else if (bitmap is not null)
        {
            float vbW = _ctx.MappingsPrimaryDeviceMap?.ViewBox?.X ?? bitmap.Width;
            float vbH = _ctx.MappingsPrimaryDeviceMap?.ViewBox?.Y ?? bitmap.Height;
            _ctx.SilhouetteBounds = constrainedBounds;
            var t = FUIRenderer.DrawBitmapInBounds(canvas, bitmap, vbW, vbH, constrainedBounds, shouldMirror);
            ApplySvgTransform(t, shouldMirror);
            DrawMappingHighlightLeadLine(canvas, constrainedBounds);
        }
        else
        {
            _ctx.SilhouetteBounds = SKRect.Empty;
            FUIRenderer.DrawTextCentered(canvas, "Device Preview",
                new SKRect(bounds.Left, labelY, bounds.Right, checkboxAreaTop),
                FUIColors.TextDim, 14f);
        }

        // Auto-scroll checkbox at bottom of panel
        float leftMargin = bounds.Left + frameInset + 12;
        float checkboxSize = 12f;
        float checkboxY = checkboxAreaTop + (checkboxRowHeight - checkboxSize) / 2f;
        _autoScroll.CheckboxBounds = new SKRect(leftMargin, checkboxY, leftMargin + checkboxSize, checkboxY + checkboxSize);
        FUIWidgets.DrawCheckboxWithLabel(canvas, _autoScroll.CheckboxBounds, _autoScroll.Enabled,
            _autoScroll.CheckboxHovered, "AUTO-SCROLL TO MAPPING");

        // "No mapping" flash indicator ÔÇö centered above the checkbox row, fades out
        if (_highlight.FlashText is not null)
        {
            float elapsed = (Environment.TickCount64 - _highlight.FlashTicks) / 1000f;
            float opacity = elapsed < 1f ? 1f : Math.Max(0f, 1f - (elapsed - 1f) / 1.5f);
            if (opacity > 0.01f)
            {
                var noMapColor = FUIColors.Warning.WithAlpha((byte)(opacity * 220));
                FUIRenderer.DrawTextCentered(canvas, _highlight.FlashText,
                    new SKRect(bounds.Left, checkboxAreaTop - 22, bounds.Right, checkboxAreaTop),
                    noMapColor, 13f);
            }
            else
            {
                _highlight.FlashText = null;
            }
        }
    }

    private SKPoint ViewBoxToScreen(float viewBoxX, float viewBoxY)
    {
        float screenX = _ctx.SvgMirrored
            ? _ctx.SvgOffset.X + _ctx.SilhouetteSourceWidth * _ctx.SvgScale - viewBoxX * _ctx.SvgScale
            : _ctx.SvgOffset.X + viewBoxX * _ctx.SvgScale;

        float screenY = _ctx.SvgOffset.Y + viewBoxY * _ctx.SvgScale;
        return new SKPoint(screenX, screenY);
    }

    /// <summary>
    /// Draws a lead line from the highlighted control anchor to its label position.
    /// Fades over 1 second hold + 2 seconds fade.
    /// </summary>
    private void DrawMappingHighlightLeadLine(SKCanvas canvas, SKRect panelBounds)
    {
        if (_highlight.ControlDef?.Anchor is null) return;

        float elapsed = (Environment.TickCount64 - _highlight.ControlHighlightTicks) / 1000f;
        float opacity = elapsed < 1f ? 1f : Math.Max(0f, 1f - (elapsed - 1f) / 2f);
        if (opacity < 0.01f) return;

        SKPoint anchorScreen = ViewBoxToScreen(_highlight.ControlDef.Anchor.X, _highlight.ControlDef.Anchor.Y);

        float labelX, labelY;
        bool goesRight = true;

        if (_highlight.ControlDef.LabelOffset is not null)
        {
            var labelScreen = ViewBoxToScreen(
                _highlight.ControlDef.Anchor.X + _highlight.ControlDef.LabelOffset.X,
                _highlight.ControlDef.Anchor.Y + _highlight.ControlDef.LabelOffset.Y);
            labelX = labelScreen.X;
            labelY = labelScreen.Y;
            bool offsetGoesRight = _highlight.ControlDef.LabelOffset.X >= 0;
            goesRight = _ctx.SvgMirrored ? !offsetGoesRight : offsetGoesRight;
        }
        else
        {
            labelY = panelBounds.Top + 80;
            labelX = _ctx.SilhouetteBounds.Right + 20;
        }

        var fakeInput = new ActiveInputState
        {
            Binding = _highlight.ControlDef.Label,
            Value = 1f,
            IsAxis = false,
            Control = _highlight.ControlDef,
            LastActivity = DateTime.Now, // approximate — uses current time since highlight ticks aren't DateTime
            AppearProgress = 1f
        };

        DeviceLeadLineRenderer.DrawInputLeadLine(
            canvas, anchorScreen, new SKPoint(labelX, labelY),
            goesRight, opacity, fakeInput, _ctx.SvgMirrored, _ctx.SvgScale);
    }

}