using System.Reflection;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using Serilog;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class SettingsTabController
{
    private void DrawSupportPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        var m = FUIRenderer.DrawPanelChrome(canvas, bounds);
        float y = m.Y;
        float leftMargin = m.LeftMargin;
        float rightMargin = m.RightMargin;

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
        FUIRenderer.DrawButton(canvas, _bmacButtonBounds, "BUY ME A COFFEE",
            bmacHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

        // Right: Join Star Citizen button — anchored to right margin
        float scLinkWidth = 180f;
        _referralLinkButtonBounds = new SKRect(rightMargin - scLinkWidth, y, rightMargin, y + btnHeight);
        bool scLinkHovered = _referralLinkButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIRenderer.DrawButton(canvas, _referralLinkButtonBounds, "JOIN STAR CITIZEN \u2192",
            scLinkHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

        // Center: code field + copy button — centered between BMAC and JOIN
        float codeFieldWidth = 138f;
        float copyBtnWidth = 56f;
        float referralGroupWidth = codeFieldWidth + FUIRenderer.SpaceSM + copyBtnWidth;
        float referralGroupX = leftMargin + (rightMargin - leftMargin - referralGroupWidth) / 2;


        // "Copied" feedback — fade in, hold, fade out
        if (_referralCopiedTicks > 0)
        {
            long elapsedMs = Environment.TickCount64 - _referralCopiedTicks;

            const long fadeInMs = 250;
            const long holdMs = 2000;
            const long fadeOutMs = 500;

            const long totalMs = fadeInMs + holdMs + fadeOutMs;

            if (elapsedMs < totalMs)
            {
                float alpha;

                if (elapsedMs < fadeInMs)
                {
                    // Fade in
                    alpha = elapsedMs / (float)fadeInMs;
                }
                else if (elapsedMs < fadeInMs + holdMs)
                {
                    // Fully visible
                    alpha = 1f;
                }
                else
                {
                    // Fade out
                    long fadeElapsed = elapsedMs - (fadeInMs + holdMs);
                    alpha = 1f - fadeElapsed / (float)fadeOutMs;
                }

                var copiedColor = FUIColors.Active.WithAlpha((byte)(alpha * 255));

                float copiedWidth = FUIRenderer.MeasureText("COPIED", 12f);
                float copiedX = referralGroupX + (codeFieldWidth - copiedWidth) / 2;

                FUIRenderer.DrawText(
                    canvas,
                    "COPIED",
                    new SKPoint(copiedX, y - 8f),
                    copiedColor,
                    12f
                );

                _ctx.MarkDirty();
            }
            else
            {
                _referralCopiedTicks = 0;
            }
        }

        var codeDisplayBounds = new SKRect(referralGroupX, y, referralGroupX + codeFieldWidth, y + btnHeight);
        FUIWidgets.DrawSettingsValueField(canvas, codeDisplayBounds, "STAR-RBDQ-Z4JG");

        _referralCopyButtonBounds = new SKRect(
            codeDisplayBounds.Right + FUIRenderer.SpaceSM, y,
            codeDisplayBounds.Right + FUIRenderer.SpaceSM + copyBtnWidth, y + btnHeight);
        bool copyHovered = _referralCopyButtonBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);
        FUIRenderer.DrawButton(canvas, _referralCopyButtonBounds, "COPY",
            copyHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
    }


}