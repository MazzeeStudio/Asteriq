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
    private void DrawPropertiesPanel(SKCanvas canvas)
    {
        // Background
        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background1);
        canvas.DrawRect(_propertiesPanelBounds, bgPaint);

        // Frame
        using var framePaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame);
        canvas.DrawRect(_propertiesPanelBounds, framePaint);

        // Header
        float y = _propertiesPanelBounds.Top + 16;
        FUIRenderer.DrawText(canvas, "CONTROL PROPERTIES", new SKPoint(_propertiesPanelBounds.Left + 16, y),
            FUIColors.Active, 14f, true);
        y += 24;

        if (_selectedControlKey is null)
        {
            FUIRenderer.DrawText(canvas, "Select a control or click on SVG",
                new SKPoint(_propertiesPanelBounds.Left + 16, y), FUIColors.TextDisabled, 13f);
            FUIRenderer.DrawText(canvas, "to add a new control anchor.",
                new SKPoint(_propertiesPanelBounds.Left + 16, y + 16), FUIColors.TextDisabled, 13f);
            return;
        }

        if (!_deviceMap.Controls.TryGetValue(_selectedControlKey, out var control))
            return;

        float leftMargin = _propertiesPanelBounds.Left + 16;
        float rightMargin = _propertiesPanelBounds.Right - 16;
        float labelWidth = 70;

        // Control Key (read-only for now)
        DrawPropertyRow(canvas, ref y, "Key:", _selectedControlKey, leftMargin, labelWidth, rightMargin, true);

        // Label
        DrawPropertyRow(canvas, ref y, "Label:", control.Label, leftMargin, labelWidth, rightMargin, false);

        // Type
        DrawPropertyRow(canvas, ref y, "Type:", control.Type, leftMargin, labelWidth, rightMargin, false);

        // Bindings
        var bindingsText = control.Bindings is not null ? string.Join(", ", control.Bindings) : "";
        DrawPropertyRow(canvas, ref y, "Bindings:", bindingsText, leftMargin, labelWidth, rightMargin, false);

        y += 10;
        FUIRenderer.DrawText(canvas, "POSITION", new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
        y += 18;

        // Anchor
        var anchorText = control.Anchor is not null ? $"X: {control.Anchor.X:F0}  Y: {control.Anchor.Y:F0}" : "Not set";
        DrawPropertyRow(canvas, ref y, "Anchor:", anchorText, leftMargin, labelWidth, rightMargin, true);

        // Offset
        var offsetText = control.LabelOffset is not null ? $"X: {control.LabelOffset.X:F0}  Y: {control.LabelOffset.Y:F0}" : "0, 0";
        DrawPropertyRow(canvas, ref y, "Offset:", offsetText, leftMargin, labelWidth, rightMargin, true);

        y += 10;
        FUIRenderer.DrawText(canvas, "LEAD LINE", new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
        y += 18;

        var ll = control.LeadLine;

        // Shelf Side with toggle button
        FUIRenderer.DrawText(canvas, "Side:", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        _shelfSideButtonBounds = new SKRect(leftMargin + labelWidth, y - 12, leftMargin + labelWidth + 60, y + 8);
        FUIRenderer.DrawButton(canvas, _shelfSideButtonBounds, ll?.ShelfSide ?? "right", FUIRenderer.ButtonState.Normal);
        y += 24;

        // Shelf Length with +/- buttons
        FUIRenderer.DrawText(canvas, "Shelf:", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        float btnX = leftMargin + labelWidth;
        _shelfLengthMinusBounds = new SKRect(btnX, y - 12, btnX + 24, y + 8);
        FUIRenderer.DrawButton(canvas, _shelfLengthMinusBounds, "-", FUIRenderer.ButtonState.Normal);
        FUIRenderer.DrawText(canvas, (ll?.ShelfLength ?? 80).ToString("F0"), new SKPoint(btnX + 32, y), FUIColors.TextPrimary, 13f);
        _shelfLengthPlusBounds = new SKRect(btnX + 60, y - 12, btnX + 84, y + 8);
        FUIRenderer.DrawButton(canvas, _shelfLengthPlusBounds, "+", FUIRenderer.ButtonState.Normal);
        y += 24;

        // Segments section
        FUIRenderer.DrawText(canvas, "Segments:", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        _addSegmentButtonBounds = new SKRect(rightMargin - 50, y - 12, rightMargin, y + 8);
        FUIRenderer.DrawButton(canvas, _addSegmentButtonBounds, "+ Add", FUIRenderer.ButtonState.Normal);
        y += 22;

        _segmentButtonBounds.Clear();
        if (ll?.Segments is not null && ll.Segments.Count > 0)
        {
            for (int i = 0; i < ll.Segments.Count; i++)
            {
                var seg = ll.Segments[i];

                // Segment label
                FUIRenderer.DrawText(canvas, $"  {i + 1}:", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);

                // Angle controls
                float angX = leftMargin + 30;
                FUIRenderer.DrawText(canvas, "A:", new SKPoint(angX, y), FUIColors.TextDim, 12f);
                var angMinus = new SKRect(angX + 16, y - 10, angX + 36, y + 6);
                FUIRenderer.DrawButton(canvas, angMinus, "-", FUIRenderer.ButtonState.Normal);
                FUIRenderer.DrawText(canvas, seg.Angle.ToString("F0"), new SKPoint(angX + 40, y), FUIColors.TextPrimary, 12f);
                var angPlus = new SKRect(angX + 65, y - 10, angX + 85, y + 6);
                FUIRenderer.DrawButton(canvas, angPlus, "+", FUIRenderer.ButtonState.Normal);

                // Length controls
                float lenX = angX + 95;
                FUIRenderer.DrawText(canvas, "L:", new SKPoint(lenX, y), FUIColors.TextDim, 12f);
                var lenMinus = new SKRect(lenX + 16, y - 10, lenX + 36, y + 6);
                FUIRenderer.DrawButton(canvas, lenMinus, "-", FUIRenderer.ButtonState.Normal);
                FUIRenderer.DrawText(canvas, seg.Length.ToString("F0"), new SKPoint(lenX + 40, y), FUIColors.TextPrimary, 12f);
                var lenPlus = new SKRect(lenX + 65, y - 10, lenX + 85, y + 6);
                FUIRenderer.DrawButton(canvas, lenPlus, "+", FUIRenderer.ButtonState.Normal);

                _segmentButtonBounds.Add((angMinus, angPlus, lenMinus, lenPlus));
                y += 20;
            }

            // Remove segment button
            _removeSegmentButtonBounds = new SKRect(leftMargin + 30, y - 2, leftMargin + 100, y + 14);
            FUIRenderer.DrawButton(canvas, _removeSegmentButtonBounds, "- Remove", FUIRenderer.ButtonState.Normal);
            y += 20;
        }
        else
        {
            FUIRenderer.DrawText(canvas, "  (none - adds straight line)", new SKPoint(leftMargin, y), FUIColors.TextDisabled, 12f);
            y += 20;
        }

        // Angle guide
        y += 5;
        FUIRenderer.DrawText(canvas, "Angles: 0=horiz, 90=up, -90=down", new SKPoint(leftMargin, y), FUIColors.TextDisabled, 12f);
        y += 12;
        FUIRenderer.DrawText(canvas, "        45=diag-up, -45=diag-down", new SKPoint(leftMargin, y), FUIColors.TextDisabled, 12f);
    }



    private static void DrawPropertyRow(SKCanvas canvas, ref float y, string label, string value,
        float leftMargin, float labelWidth, float rightMargin, bool readOnly)
    {
        FUIRenderer.DrawText(canvas, label, new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);

        var valueBounds = new SKRect(leftMargin + labelWidth, y - 12, rightMargin, y + 8);

        if (!readOnly)
        {
            FUIRenderer.DrawRoundedPanel(canvas, valueBounds, FUIColors.Background2, FUIColors.Frame);
        }

        FUIRenderer.DrawText(canvas, value, new SKPoint(leftMargin + labelWidth + 4, y),
            readOnly ? FUIColors.TextPrimary : FUIColors.TextPrimary, 13f);

        y += 24;
    }

    private void DrawControlsList(SKCanvas canvas)
    {
        // Background
        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background1);
        canvas.DrawRect(_controlsListBounds, bgPaint);

        // Frame
        using var framePaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame);
        canvas.DrawRect(_controlsListBounds, framePaint);

        // Header
        float headerY = _controlsListBounds.Top + 16;
        FUIRenderer.DrawText(canvas, "CONTROLS", new SKPoint(_controlsListBounds.Left + 16, headerY),
            FUIColors.Active, 14f, true);

        // Content area (between header and buttons)
        float contentTop = _controlsListBounds.Top + 40;
        float contentBottom = _controlsListBounds.Bottom - 50;
        var contentBounds = new SKRect(_controlsListBounds.Left, contentTop, _controlsListBounds.Right, contentBottom);

        // Calculate total content height
        float itemHeight = 24;
        float itemGap = 2;
        float totalContentHeight = _deviceMap.Controls.Count * (itemHeight + itemGap);

        // Clamp scroll offset
        float maxScroll = Math.Max(0, totalContentHeight - contentBounds.Height);
        _controlsListScroll = Math.Clamp(_controlsListScroll, 0, maxScroll);

        // Clip to content area
        canvas.Save();
        canvas.ClipRect(contentBounds);

        // Control list items
        _controlListItemBounds.Clear();
        float y = contentTop - _controlsListScroll;
        int index = 0;

        foreach (var kvp in _deviceMap.Controls)
        {
            var itemBounds = new SKRect(_controlsListBounds.Left + 10, y,
                _controlsListBounds.Right - 10, y + itemHeight);
            _controlListItemBounds.Add(itemBounds);

            // Only draw if visible
            if (y + itemHeight > contentTop && y < contentBottom)
            {
                bool isSelected = kvp.Key == _selectedControlKey;
                bool isHovered = index == _hoveredControlListItem;

                if (isSelected || isHovered)
                {
                    using var hlPaint = FUIRenderer.CreateFillPaint(isSelected ? FUIColors.SelectionBg : FUIColors.Primary.WithAlpha(20));
                    canvas.DrawRoundRect(itemBounds, 3, 3, hlPaint);
                }

                var indicator = isSelected ? "> " : "  ";
                FUIRenderer.DrawText(canvas, indicator + kvp.Key, new SKPoint(itemBounds.Left + 5, y + 14),
                    FUIColors.ContentColor(isSelected), 13f);
            }

            y += itemHeight + itemGap;
            index++;
        }

        canvas.Restore();

        // Draw scroll indicator if needed
        if (totalContentHeight > contentBounds.Height)
        {
            var trackBounds = new SKRect(_controlsListBounds.Right - 6, contentTop,
                _controlsListBounds.Right - 2, contentTop + contentBounds.Height);
            FUIWidgets.DrawScrollbar(canvas, trackBounds, _controlsListScroll,
                totalContentHeight, contentBounds.Height, isHovered: false, out _,
                cornerRadius: 2f, drawTrack: false);
        }

        // Add/Delete buttons
        float btnY = _controlsListBounds.Bottom - 40;
        float btnWidth = 80;
        float btnGap = 12;
        float btnX = _controlsListBounds.Left + 16;

        _addControlButtonBounds = new SKRect(btnX, btnY, btnX + btnWidth, btnY + 26);
        FUIRenderer.DrawButton(canvas, _addControlButtonBounds, "+ ADD", _addControlHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

        _deleteControlButtonBounds = new SKRect(btnX + btnWidth + btnGap, btnY,
            btnX + btnWidth * 2 + btnGap, btnY + 26);
        FUIRenderer.DrawButton(canvas, _deleteControlButtonBounds, "DELETE", _deleteControlHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal, isDanger: true);
    }

}
#endif