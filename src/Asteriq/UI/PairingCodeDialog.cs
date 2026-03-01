using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Asteriq.UI;

/// <summary>
/// Displays a 6-digit pairing code in the FUI style — one large digit per card tile.
/// Shown on both source (which generated the code) and receiver (which received it).
///
/// Source side: shows the code it sent — user just visually confirms it matches the receiver screen.
/// Receiver side: shows the code it received — user clicks OK to accept or Escape to reject.
/// </summary>
public sealed class PairingCodeDialog : FUIBaseDialog
{
    private SKControl _canvas = null!;

    private readonly string _peerName;
    private readonly string _code;
    private readonly bool _isSource;

    private SKRect _okButtonBounds;
    private SKRect _cancelButtonBounds;
    private int _hoveredButton = -1; // 0=OK, 1=Cancel

    public PairingCodeDialog(string peerName, string code, bool isSource)
    {
        _peerName = peerName;
        _code = code;
        _isSource = isSource;
        InitializeForm();
        InitializeCanvas();
    }

    private void InitializeForm()
    {
        Text = "Network Pairing";
        float s = FUIRenderer.CanvasScaleFactor;
        Size = new Size((int)(480 * s), (int)(280 * s));
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.Black;
        ShowInTaskbar = false;
        KeyPreview = true;
    }

    private void InitializeCanvas()
    {
        _canvas = new SKControl { Dock = DockStyle.Fill };
        _canvas.PaintSurface += OnPaintSurface;
        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseDown += OnMouseDown;
        Controls.Add(_canvas);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }
        if (keyData == Keys.Enter)
        {
            DialogResult = DialogResult.OK;
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        float s = FUIRenderer.CanvasScaleFactor;
        var pt = new SKPoint(e.X / s, e.Y / s);
        int prev = _hoveredButton;
        _hoveredButton = _okButtonBounds.Contains(pt) ? 0 : _cancelButtonBounds.Contains(pt) ? 1 : -1;
        if (_hoveredButton != prev) _canvas.Invalidate();
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        float s = FUIRenderer.CanvasScaleFactor;
        var pt = new SKPoint(e.X / s, e.Y / s);
        if (_okButtonBounds.Contains(pt)) { DialogResult = DialogResult.OK; Close(); }
        else if (_cancelButtonBounds.Contains(pt)) { DialogResult = DialogResult.Cancel; Close(); }
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        float scale = FUIRenderer.CanvasScaleFactor;
        canvas.Scale(scale);
        var bounds = new SKRect(0, 0, e.Info.Width / scale, e.Info.Height / scale);

        canvas.Clear(FUIColors.Background1);

        // Outer border
        using var framePaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame, 2f);
        canvas.DrawRect(bounds, framePaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Primary, 20f, 6f);

        // Title
        string title = _isSource ? "CONNECTING TO PEER" : "INCOMING CONNECTION";
        float titleY = 22f;
        float titleW = FUIRenderer.MeasureText(title, 13f);
        FUIRenderer.DrawText(canvas, title, new SKPoint(bounds.MidX - titleW / 2f, titleY),
            FUIColors.TextDim, 13f);

        // Peer name
        string peerLabel = _isSource
            ? $"Sending to: {_peerName}"
            : $"From: {_peerName}";
        float peerW = FUIRenderer.MeasureText(peerLabel, 15f);
        FUIRenderer.DrawText(canvas, peerLabel, new SKPoint(bounds.MidX - peerW / 2f, 44f),
            FUIColors.TextPrimary, 15f);

        // Instruction
        string instruction = _isSource
            ? "Confirm this code matches the remote screen"
            : "Confirm this code to allow the connection";
        float instrW = FUIRenderer.MeasureText(instruction, 13f);
        FUIRenderer.DrawText(canvas, instruction,
            new SKPoint(bounds.MidX - instrW / 2f, 66f), FUIColors.TextDim, 13f);

        // ── Digit tiles ──────────────────────────────────────────────────────
        DrawDigitTiles(canvas, bounds);

        // ── Buttons ──────────────────────────────────────────────────────────
        float btnW = 110f;
        float btnH = 30f;
        float btnY = bounds.Bottom - btnH - 20f;
        float btnGap = 12f;
        float totalBtnW = btnW * 2 + btnGap;
        float btnStartX = bounds.MidX - totalBtnW / 2f;

        _okButtonBounds = new SKRect(btnStartX, btnY, btnStartX + btnW, btnY + btnH);
        _cancelButtonBounds = new SKRect(btnStartX + btnW + btnGap, btnY,
            btnStartX + totalBtnW, btnY + btnH);

        FUIRenderer.DrawButton(canvas, _okButtonBounds, "CONFIRM",
            _hoveredButton == 0 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
        FUIRenderer.DrawButton(canvas, _cancelButtonBounds, "REJECT",
            _hoveredButton == 1 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal,
            isDanger: true);
    }

    private void DrawDigitTiles(SKCanvas canvas, SKRect bounds)
    {
        const int digitCount = 6;
        const float tileW = 54f;
        const float tileH = 72f;
        const float tileGap = 10f;
        const float tileRadius = 6f;
        float totalW = tileW * digitCount + tileGap * (digitCount - 1);
        float startX = bounds.MidX - totalW / 2f;
        float tileY = 90f;

        // Pad / truncate code to exactly 6 digits
        string digits = (_code + "      ").Substring(0, digitCount);

        for (int i = 0; i < digitCount; i++)
        {
            float tx = startX + i * (tileW + tileGap);
            var tile = new SKRect(tx, tileY, tx + tileW, tileY + tileH);

            // Tile background — dark panel with accent border
            FUIRenderer.FillFrame(canvas, tile, FUIColors.Active.WithAlpha(FUIColors.AlphaLightTint), tileRadius);
            FUIRenderer.DrawFrame(canvas, tile, FUIColors.Active.WithAlpha(FUIColors.AlphaBorderSoft),
                tileRadius, 1.5f);

            // Digit character
            char digit = digits[i];
            string digitStr = digit.ToString();
            float digitW = FUIRenderer.MeasureText(digitStr, 38f);
            FUIRenderer.DrawText(canvas, digitStr,
                new SKPoint(tx + (tileW - digitW) / 2f, tileY + tileH - 16f),
                FUIColors.TextBright, 38f);
        }
    }
}
