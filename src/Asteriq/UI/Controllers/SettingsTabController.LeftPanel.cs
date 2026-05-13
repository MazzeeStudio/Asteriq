using System.Reflection;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using Serilog;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public sealed partial class SettingsTabController
{
    private void DrawProfileManagementPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        var metrics = FUIRenderer.DrawPanelChrome(canvas, bounds);
        float y = metrics.Y;
        float leftMargin = metrics.LeftMargin;
        float rightMargin = metrics.RightMargin;
        float bottom = bounds.Bottom - frameInset - FUIRenderer.SpaceLG;

        canvas.Save();
        canvas.ClipRect(new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom - frameInset));

        y = FUIRenderer.DrawPanelHeader(canvas, "CONFIGURATION MANAGEMENT", leftMargin, y);

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is not null)
        {
            y = FUIRenderer.DrawSectionHeader(canvas, "ACTIVE CONFIGURATION", leftMargin, y);

            float nameBoxHeight = 32f;
            _profileNameBounds = new SKRect(leftMargin, y, rightMargin, y + nameBoxHeight);
            bool nameHovered = _profileNameBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

            FUIRenderer.DrawRoundedPanel(canvas, _profileNameBounds, FUIColors.ActiveLight, FUIColors.Active, 4f);

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

                var iconColor = FUIColors.InteractiveColor(_profileNameEditHovered);
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
            FUIRenderer.DrawText(canvas, "No configuration active", new SKPoint(leftMargin, y), FUIColors.TextDim, 15f);
            y += 40f;
        }

        y = FUIRenderer.DrawSectionHeader(canvas, "ACTIONS", leftMargin, y);

        float buttonHeight = 28f;
        float buttonGap = FUIRenderer.SpaceSM;
        float buttonWidth = (metrics.ContentWidth - buttonGap) / 2;

        _newProfileButtonBounds = new SKRect(leftMargin, y, leftMargin + buttonWidth, y + buttonHeight);
        _duplicateProfileButtonBounds = new SKRect(rightMargin - buttonWidth, y, rightMargin, y + buttonHeight);
        bool newHovered = _newProfileButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        bool dupHovered = _duplicateProfileButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIRenderer.DrawButton(canvas, _newProfileButtonBounds, "New Configuration",
            newHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
        FUIRenderer.DrawButton(canvas, _duplicateProfileButtonBounds,
            profile is not null ? "Duplicate" : "---",
            profile is null ? FUIRenderer.ButtonState.Disabled : (dupHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal));
        y += buttonHeight + buttonGap;

        _importProfileButtonBounds = new SKRect(leftMargin, y, leftMargin + buttonWidth, y + buttonHeight);
        _exportProfileButtonBounds = new SKRect(rightMargin - buttonWidth, y, rightMargin, y + buttonHeight);
        bool importHovered = _importProfileButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        bool exportHovered = _exportProfileButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIRenderer.DrawButton(canvas, _importProfileButtonBounds, "Import",
            importHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
        FUIRenderer.DrawButton(canvas, _exportProfileButtonBounds,
            profile is not null ? "Export" : "---",
            profile is null ? FUIRenderer.ButtonState.Disabled : (exportHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal));
        y += buttonHeight + buttonGap;

        if (profile is not null && y + buttonHeight <= bottom)
        {
            _deleteProfileButtonBounds = new SKRect(leftMargin, y, rightMargin, y + buttonHeight);
            bool deleteHovered = _deleteProfileButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
            FUIRenderer.DrawButton(canvas, _deleteProfileButtonBounds, "Delete Configuration",
                deleteHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal, isDanger: true);
            y += buttonHeight + 20f;

            if (y < bottom - 60)
            {
                FUIWidgets.DrawShiftLayersSection(canvas, leftMargin, rightMargin, y, bottom, profile);
            }
        }

        canvas.Restore();
    }

    private void RenameActiveProfile()
    {
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var newName = FUIInputDialog.Show(_ctx.OwnerForm, "Rename Configuration", "Configuration Name:", profile.Name);
        if (newName is null || newName == profile.Name)
            return;

        profile.Name = newName;
        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.RefreshProfileList();
        _ctx.InvalidateCanvas();
    }



}