#if DEBUG
using System.Text.Json;
using System.Text.Json.Serialization;
using Asteriq.Models;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Svg.Skia;

namespace Asteriq.UI;

public partial class DeviceMapEditorForm
{
    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        float scale = FUIRenderer.CanvasScaleFactor;
        canvas.Scale(scale);
        var bounds = new SKRect(0, 0, e.Info.Width / scale, e.Info.Height / scale);

        // Clear background
        canvas.Clear(FUIColors.Background0);

        // Calculate layout regions
        CalculateLayout(bounds);

        // Draw components
        DrawTitleBar(canvas, bounds);
        DrawToolbar(canvas);
        DrawSvgPanel(canvas);
        DrawPropertiesPanel(canvas);
        DrawControlsList(canvas);
        DrawStatusBar(canvas);

        // Draw SVG dropdown if open (on top of everything)
        if (_svgDropdownOpen)
        {
            DrawSvgDropdownMenu(canvas);
        }
    }

    private void CalculateLayout(SKRect bounds)
    {
        float titleHeight = TitleBarHeight;
        float toolbarHeight = 50;
        float statusHeight = 30;
        float rightPanelWidth = 320;

        _toolbarBounds = new SKRect(0, titleHeight, bounds.Right, titleHeight + toolbarHeight);
        _statusBarBounds = new SKRect(0, bounds.Bottom - statusHeight, bounds.Right, bounds.Bottom);

        float contentTop = _toolbarBounds.Bottom;
        float contentBottom = _statusBarBounds.Top;

        _propertiesPanelBounds = new SKRect(bounds.Right - rightPanelWidth, contentTop,
            bounds.Right, contentBottom * 0.6f);
        _controlsListBounds = new SKRect(bounds.Right - rightPanelWidth, _propertiesPanelBounds.Bottom,
            bounds.Right, contentBottom);
        _svgPanelBounds = new SKRect(0, contentTop, bounds.Right - rightPanelWidth, contentBottom);
    }

    private void DrawTitleBar(SKCanvas canvas, SKRect bounds)
    {
        var titleBounds = new SKRect(0, 0, bounds.Right, TitleBarHeight);

        // Background
        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background1);
        canvas.DrawRect(titleBounds, bgPaint);

        // Title
        var title = "DEVICE MAP EDITOR" + (_hasUnsavedChanges ? " *" : "");
        FUIRenderer.DrawText(canvas, title, new SKPoint(20, 26), FUIColors.Active, 17f, true);

        // Close button
        var closeRect = new SKRect(bounds.Right - 40, 8, bounds.Right - 8, 32);
        FUIRenderer.DrawTextCentered(canvas, "X", closeRect, FUIColors.TextDim, 17f);
    }

    private void DrawToolbar(SKCanvas canvas)
    {
        // Background
        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background1.WithAlpha(200));
        canvas.DrawRect(_toolbarBounds, bgPaint);

        float y = _toolbarBounds.Top + 10;
        float x = 20;

        // SVG dropdown
        FUIRenderer.DrawText(canvas, "SVG:", new SKPoint(x, y + 12), FUIColors.TextDim, 13f);
        x += 36;

        _svgDropdownBounds = new SKRect(x, y, x + 180, y + 30);
        FUIWidgets.DrawDropdown(canvas, _svgDropdownBounds, _deviceMap.SvgFile ?? "Select SVG...", _svgDropdownOpen);
        x += 200;

        // JSON filename
        FUIRenderer.DrawText(canvas, "JSON:", new SKPoint(x, y + 12), FUIColors.TextDim, 13f);
        x += 40;

        _jsonTextBoxBounds = new SKRect(x, y, x + 200, y + 30);
        DrawTextBox(canvas, _jsonTextBoxBounds, _jsonFileName);
        x += 220;

        // New button
        _newButtonBounds = new SKRect(x, y, x + 60, y + 30);
        FUIRenderer.DrawButton(canvas, _newButtonBounds, "NEW", _newButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
        x += 70;

        // Load button
        _loadButtonBounds = new SKRect(x, y, x + 60, y + 30);
        FUIRenderer.DrawButton(canvas, _loadButtonBounds, "LOAD", _loadButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
        x += 80;

        // Mirror checkbox
        FUIRenderer.DrawText(canvas, "Mirror (L):", new SKPoint(x, y + 12), FUIColors.TextDim, 13f);
        x += 65;
        _mirrorCheckboxBounds = new SKRect(x, y + 3, x + 24, y + 27);
        FUIWidgets.DrawSCCheckbox(canvas, _mirrorCheckboxBounds, _deviceMap.Mirror, _mirrorCheckboxHovered);
        x += 40;

        // Save button (right side)
        _saveButtonBounds = new SKRect(_toolbarBounds.Right - 100, y, _toolbarBounds.Right - 20, y + 30);
        FUIRenderer.DrawButton(canvas, _saveButtonBounds, "SAVE", _saveButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
    }


    private void DrawStatusBar(SKCanvas canvas)
    {
        // Background
        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background1);
        canvas.DrawRect(_statusBarBounds, bgPaint);

        // Cursor position
        var cursorText = $"Cursor: ({_mouseViewBox.X:F0}, {_mouseViewBox.Y:F0})";
        FUIRenderer.DrawText(canvas, cursorText, new SKPoint(20, _statusBarBounds.Top + 18), FUIColors.TextDim, 13f);

        // ViewBox info
        var viewBoxText = $"ViewBox: {_deviceMap.ViewBox?.X ?? 2048}x{_deviceMap.ViewBox?.Y ?? 2048}";
        FUIRenderer.DrawText(canvas, viewBoxText, new SKPoint(220, _statusBarBounds.Top + 18), FUIColors.TextDim, 13f);

        // Control count
        var countText = $"Controls: {_deviceMap.Controls.Count}";
        FUIRenderer.DrawText(canvas, countText, new SKPoint(420, _statusBarBounds.Top + 18), FUIColors.TextDim, 13f);

        // Show save message for 3 seconds, otherwise show help hint
        if (_lastSaveMessage is not null && (DateTime.Now - _lastSaveTime).TotalSeconds < 3)
        {
            FUIRenderer.DrawText(canvas, _lastSaveMessage, new SKPoint(580, _statusBarBounds.Top + 18), FUIColors.Active, 13f);
        }
        else
        {
            // Help hint - context sensitive
            string helpText;
            if (_selectedControlKey is not null)
            {
                // Check if hovering over a segment handle
                bool hoveringSegment = false;
                for (int i = 0; i < _segmentEndHandles.Count; i++)
                {
                    if (SKPoint.Distance(_segmentEndHandles[i], _mousePos) < HandleHitRadius)
                    {
                        hoveringSegment = true;
                        break;
                    }
                }
                helpText = hoveringSegment
                    ? "Drag to move, Right-click to remove"
                    : "Shift+Click to reposition anchor";
            }
            else
            {
                helpText = "Click on SVG to add control";
            }
            FUIRenderer.DrawText(canvas, helpText, new SKPoint(580, _statusBarBounds.Top + 18), FUIColors.TextDisabled, 13f);
        }
    }


    private void DrawSvgDropdownMenu(SKCanvas canvas)
    {
        float itemHeight = 28;  // 4px aligned
        float menuHeight = _availableSvgFiles.Count * itemHeight + 10;
        var menuBounds = new SKRect(_svgDropdownBounds.Left, _svgDropdownBounds.Bottom + 2,
            _svgDropdownBounds.Right, _svgDropdownBounds.Bottom + 2 + menuHeight);

        // Background
        FUIRenderer.DrawRoundedPanel(canvas, menuBounds, FUIColors.Background1, FUIColors.Primary, 4f);

        _svgDropdownItemBounds.Clear();
        float y = menuBounds.Top + 5;

        for (int i = 0; i < _availableSvgFiles.Count; i++)
        {
            var itemBounds = new SKRect(menuBounds.Left + 5, y, menuBounds.Right - 5, y + itemHeight - 2);
            _svgDropdownItemBounds.Add(itemBounds);

            bool hovered = i == _hoveredSvgDropdownItem;
            if (hovered)
            {
                using var hlPaint = FUIRenderer.CreateFillPaint(FUIColors.Primary.WithAlpha(40));
                canvas.DrawRoundRect(itemBounds, 3, 3, hlPaint);
            }

            FUIRenderer.DrawText(canvas, _availableSvgFiles[i], new SKPoint(itemBounds.Left + 5, y + 16),
                hovered ? FUIColors.TextPrimary : FUIColors.TextDim, 13f);

            y += itemHeight;
        }
    }

    private static void DrawTextBox(SKCanvas canvas, SKRect bounds, string text)
    {
        FUIRenderer.DrawRoundedPanel(canvas, bounds, FUIColors.Background2, FUIColors.Frame, 4f);

        FUIRenderer.DrawText(canvas, text, new SKPoint(bounds.Left + 8, bounds.MidY + 4), FUIColors.TextPrimary, 13f);
    }


    private static SKRect MeasureText(string text, float size)
    {
        // Use the same scaled size as FUIRenderer.DrawText
        float scaledSize = size;
        using var paint = new SKPaint { TextSize = scaledSize, IsAntialias = true };
        var width = paint.MeasureText(text);
        var metrics = paint.FontMetrics;
        var height = metrics.Descent - metrics.Ascent;
        return new SKRect(0, 0, width, height);
    }



}
#endif