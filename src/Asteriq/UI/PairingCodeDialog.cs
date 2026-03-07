using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Asteriq.UI;

/// <summary>
/// Shown on the client side when an unknown master requests a connection.
/// Displays the master name and 6-digit code so the user can decide to TRUST or REJECT.
///
/// On DialogResult.OK:  caller should call AcceptPairing() and save TrustedMaster.
/// On DialogResult.Cancel: caller should call RejectPairing().
/// </summary>
public sealed class TrustRequestDialog : FUIBaseDialog
{
    // CA2213: SKControl is a WinForms child control — disposed automatically via Controls collection
#pragma warning disable CA2213
    private SKControl _canvas = null!;
#pragma warning restore CA2213

    private readonly string _peerName;
    private readonly string _code;

    private SKRect _trustButtonBounds;
    private SKRect _rejectButtonBounds;
    private int _hoveredButton = -1; // 0=Trust, 1=Reject

    public TrustRequestDialog(string peerName, string code)
    {
        _peerName = peerName;
        _code     = code;
        InitializeForm();
        InitializeCanvas();
    }

    private void InitializeForm()
    {
        Text = "Network Connection Request";
        float s = FUIRenderer.CanvasScaleFactor;
        Size = new Size((int)(440 * s), (int)(240 * s));
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
        _hoveredButton = _trustButtonBounds.Contains(pt) ? 0
                       : _rejectButtonBounds.Contains(pt) ? 1 : -1;
        if (_hoveredButton != prev) _canvas.Invalidate();
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        float s = FUIRenderer.CanvasScaleFactor;
        var pt = new SKPoint(e.X / s, e.Y / s);
        if (_trustButtonBounds.Contains(pt))  { DialogResult = DialogResult.OK;     Close(); }
        else if (_rejectButtonBounds.Contains(pt)) { DialogResult = DialogResult.Cancel; Close(); }
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
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Warning, 20f, 6f);

        float cx = bounds.MidX;

        // Header
        const string header = "NEW CONNECTION REQUEST";
        float headerW = FUIRenderer.MeasureText(header, 13f);
        FUIRenderer.DrawText(canvas, header,
            new SKPoint(cx - headerW / 2f, 26f), FUIColors.Warning, 13f);

        // Peer name
        string from = $"FROM:  {_peerName.ToUpperInvariant()}";
        float fromW = FUIRenderer.MeasureText(from, 15f);
        FUIRenderer.DrawText(canvas, from,
            new SKPoint(cx - fromW / 2f, 54f), FUIColors.TextPrimary, 15f);

        // Code
        string codeLabel = $"CODE:  {_code}";
        float codeW = FUIRenderer.MeasureText(codeLabel, 22f);
        FUIRenderer.DrawText(canvas, codeLabel,
            new SKPoint(cx - codeW / 2f, 86f), FUIColors.TextBright, 22f);

        // Instruction
        const string hint = "Trust this master to allow future auto-connections";
        float hintW = FUIRenderer.MeasureText(hint, 12f);
        FUIRenderer.DrawText(canvas, hint,
            new SKPoint(cx - hintW / 2f, 114f), FUIColors.TextDim, 12f);

        // Buttons
        float btnW = 120f;
        float btnH = 30f;
        float btnY = bounds.Bottom - btnH - 20f;
        float btnGap = 16f;
        float totalBtnW = btnW * 2 + btnGap;
        float btnStartX = cx - totalBtnW / 2f;

        _trustButtonBounds  = new SKRect(btnStartX,              btnY, btnStartX + btnW,              btnY + btnH);
        _rejectButtonBounds = new SKRect(btnStartX + btnW + btnGap, btnY, btnStartX + totalBtnW, btnY + btnH);

        FUIRenderer.DrawButton(canvas, _trustButtonBounds, "TRUST",
            _hoveredButton == 0 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
        FUIRenderer.DrawButton(canvas, _rejectButtonBounds, "REJECT",
            _hoveredButton == 1 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal,
            isDanger: true);
    }
}
