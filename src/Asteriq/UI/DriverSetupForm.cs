using Asteriq.Services;
using Asteriq.Services.Abstractions;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Asteriq.UI;

/// <summary>
/// FUI-styled form for checking and installing required drivers (vJoy, HidHide).
/// </summary>
public class DriverSetupForm : FUIBaseDialog
{
    private readonly DriverSetupManager _driverSetup;
    private readonly IApplicationSettingsService? _appSettings;
    private readonly bool _settingsMode;
    private readonly FUIBackground _background = new();
    private readonly SKControl _canvas;

    // Native controls overlaid on canvas
    private readonly ProgressBar _progressBar;
    private readonly ListBox _logListBox;

    // State drawn on canvas (invalidate on change)
    private string _statusText = "Checking installed drivers...";
    private SKColor _statusColor;
    private string _vJoyStatusText = "Checking...";
    private SKColor _vJoyStatusColor;
    private string _hidHideStatusText = "Checking...";
    private SKColor _hidHideStatusColor;
    private bool _continueEnabled;

    // Install button state
    private bool _vJoyInstalled;
    private bool _hidHideInstalled;
    private bool _vJoyInstalling;
    private bool _hidHideInstalling;

    // Canvas-drawn interactive regions
    private SKRect _continueButtonBounds;
    private SKRect _exitButtonBounds;
    private SKRect _vJoyLinkBounds;
    private SKRect _hidHideLinkBounds;
    private SKRect _vJoyInstallBounds;
    private SKRect _hidHideInstallBounds;
    private int _hoveredRegion = -1; // 0=continue/skip, 1=exit, 2=vjoy-link, 3=hidhide-link, 4=vjoy-install, 5=hidhide-install, 6=dont-show-again

    // "Don't show again" checkbox
    private SKRect _dontShowAgainBounds;
    private bool _dontShowAgain;

    // Dragging
    private bool _isDragging;
    private Point _dragStart;

    // Logical layout constants (all in unscaled canvas space)
    private const float TitleBarH = 36f;
    private const float LogicalW = 560f;
    private const float LogicalH = 600f;
    private const float Pad = 20f;
    private const float PanelH = 122f;
    private const float InstallBtnW = 100f;
    private const float InstallBtnH = 30f;
    private const float InstallBtnOffsetY = 78f;

    // Top section - computed each paint so panels flow below status text naturally
    private float _panel1Y = 90f;
    private float _panel2Y = 222f;

    public bool SetupComplete { get; private set; }
    public bool SkippedVJoy { get; private set; }
    public bool DontShowAgain => _dontShowAgain;

    public DriverSetupForm(DriverSetupManager driverSetupManager, IUIThemeService themeService,
        IApplicationSettingsService? appSettings = null, bool settingsMode = false)
    {
        _driverSetup = driverSetupManager ?? throw new ArgumentNullException(nameof(driverSetupManager));
        _appSettings = appSettings;
        _settingsMode = settingsMode;
        _dontShowAgain = appSettings?.SkipDriverSetup ?? false;
        _statusColor = FUIColors.TextDim;
        _vJoyStatusColor = FUIColors.TextDim;
        _hidHideStatusColor = FUIColors.TextDim;

        // Apply user's background theme settings
        var bg = themeService.LoadBackgroundSettings();
        _background.GridStrength = bg.gridStrength;
        _background.GlowIntensity = bg.glowIntensity;
        _background.NoiseIntensity = bg.noiseIntensity;
        _background.ScanlineIntensity = bg.scanlineIntensity;
        _background.VignetteStrength = bg.vignetteStrength;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(6, 8, 10);
        ShowInTaskbar = true;
        KeyPreview = true;

        try
        {
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "asteriq.ico");
            if (File.Exists(iconPath)) Icon = new Icon(iconPath);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException) { }

        _canvas = new SKControl { Dock = DockStyle.Fill };
        _canvas.PaintSurface += OnPaintSurface;
        _canvas.MouseMove += OnCanvasMouseMove;
        _canvas.MouseDown += OnCanvasMouseDown;
        _canvas.MouseUp += OnCanvasMouseUp;
        _canvas.MouseLeave += OnCanvasMouseLeave;
        Controls.Add(_canvas);

        // Create native controls (positioned later in ApplyScale)
        _progressBar = new ProgressBar
        {
            Style = ProgressBarStyle.Continuous,
            Visible = false,
        };

        _logListBox = new ListBox
        {
            BackColor = ToColor(FUIColors.Background2),
            ForeColor = ToColor(FUIColors.TextDim),
            BorderStyle = BorderStyle.None,
        };

        foreach (Control ctl in new Control[] { _progressBar, _logListBox })
        {
            Controls.Add(ctl);
            ctl.BringToFront();
        }

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            else if (e.KeyCode == Keys.Enter && _continueEnabled) { SetupComplete = true; DialogResult = DialogResult.OK; Close(); }
        };

        RefreshDriverStatus();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // DeviceDpi is correct only after the window handle is created
        FUIRenderer.SetDisplayScale(DeviceDpi);

        // Now that DPI is known, size the form and position native controls
        ApplyScale();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        FUIRenderer.SetDisplayScale(DeviceDpi);
        ApplyScale();
        _canvas.Invalidate();
    }

    /// <summary>
    /// Size the form and position native controls using the current CanvasScaleFactor.
    /// Called from OnLoad (after DPI is known) and OnDpiChanged.
    /// </summary>
    private void ApplyScale()
    {
        float scale = FUIRenderer.CanvasScaleFactor;

        ClientSize = new Size((int)(LogicalW * scale), (int)(LogicalH * scale));

        // Re-center on screen after resize
        if (StartPosition == FormStartPosition.CenterScreen)
        {
            var screen = Screen.FromControl(this).WorkingArea;
            Location = new Point(
                screen.X + (screen.Width - Width) / 2,
                screen.Y + (screen.Height - Height) / 2);
        }

        // Bottom-anchored layout positions (logical)
        float btnY = LogicalH - 50f;
        float logBoxBottom = btnY - 10f;
        float logBoxTop = logBoxBottom - 120f;
        float logHeaderY = logBoxTop - 14f;
        float progressBarY = logHeaderY - 20f;

        // Position native controls in physical pixels
        float contentW = LogicalW - Pad * 2;

        _progressBar.Location = new Point((int)(Pad * scale), (int)(progressBarY * scale));
        _progressBar.Size = new Size((int)(contentW * scale), (int)(14 * scale));

        _logListBox.Location = new Point((int)(Pad * scale), (int)((logBoxTop + 2) * scale));
        _logListBox.Size = new Size((int)(contentW * scale), (int)(116 * scale));
        _logListBox.Font = new Font("Consolas", 9f * FUIRenderer.InterfaceScale);
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        float scale = FUIRenderer.CanvasScaleFactor;
        canvas.Scale(scale);
        var b = new SKRect(0, 0, e.Info.Width / scale, e.Info.Height / scale);

        float contentW = b.Width - Pad * 2;

        canvas.Clear(FUIColors.Background0);

        // FUI background effects (grid, glow, noise, scanlines, vignette)
        _background.Render(canvas, b);

        // Outer chamfered frame
        FUIRenderer.DrawFrame(canvas, b.Inset(-1, -1), FUIColors.Frame, FUIRenderer.ChamferSize);

        // Title bar
        var titleBar = new SKRect(b.Left + 2, b.Top + 2, b.Right - 2, b.Top + TitleBarH);
        using var titleBgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background2);
        canvas.DrawRect(titleBar, titleBgPaint);
        using var sepPaint = FUIRenderer.CreateStrokePaint(FUIColors.Frame);
        canvas.DrawLine(titleBar.Left, titleBar.Bottom, titleBar.Right, titleBar.Bottom, sepPaint);
        FUIRenderer.DrawText(canvas, "DRIVER SETUP", new SKPoint(16, titleBar.MidY + 5), FUIColors.TextBright, 16f, false);

        // Overall status — draw first so we know how much vertical space it needs
        var statusLines = _statusText.Split('\n');
        float statusY = TitleBarH + 18f;
        for (int i = 0; i < statusLines.Length; i++)
        {
            FUIRenderer.DrawText(canvas, statusLines[i], new SKPoint(Pad, statusY), _statusColor, 13f);
            if (i < statusLines.Length - 1) statusY += 15f;
        }

        // Panels start 10px below the last text baseline — tight but readable
        _panel1Y = statusY + 10f;
        _panel2Y = _panel1Y + PanelH + 10f;

        DrawDriverPanel(canvas,
            new SKRect(Pad, _panel1Y, Pad + contentW, _panel1Y + PanelH),
            "vJOY VIRTUAL JOYSTICK", "REQUIRED",
            "Creates virtual joystick devices visible to Star Citizen",
            _vJoyStatusText, _vJoyStatusColor, required: true);

        DrawDriverPanel(canvas,
            new SKRect(Pad, _panel2Y, Pad + contentW, _panel2Y + PanelH),
            "HIDHIDE DEVICE HIDING", "RECOMMENDED",
            "Hides physical devices so only virtual devices are visible",
            _hidHideStatusText, _hidHideStatusColor, required: false);

        // Canvas-drawn install buttons (inside panels)
        float installBtnX = Pad + contentW - InstallBtnW - 14f;

        _vJoyInstallBounds = new SKRect(installBtnX, _panel1Y + InstallBtnOffsetY,
            installBtnX + InstallBtnW, _panel1Y + InstallBtnOffsetY + InstallBtnH);
        DrawInstallButton(canvas, _vJoyInstallBounds, _vJoyInstalled, _vJoyInstalling, _hoveredRegion == 4);

        _hidHideInstallBounds = new SKRect(installBtnX, _panel2Y + InstallBtnOffsetY,
            installBtnX + InstallBtnW, _panel2Y + InstallBtnOffsetY + InstallBtnH);
        DrawInstallButton(canvas, _hidHideInstallBounds, _hidHideInstalled, _hidHideInstalling, _hoveredRegion == 5);

        // Manual download links — anchored below panel 2
        float linkY = _panel2Y + PanelH + 18f;
        FUIRenderer.DrawText(canvas, "MANUAL DOWNLOAD:", new SKPoint(Pad, linkY), FUIColors.TextDim, 12f, false);

        using var linkPaint = FUIRenderer.CreateTextPaint(FUIColors.Active, 12f);

        float vJoyLinkX = Pad + 142;
        float vJoyLinkW = linkPaint.MeasureText("vJoy from GitHub");
        _vJoyLinkBounds = new SKRect(vJoyLinkX, linkY - 12, vJoyLinkX + vJoyLinkW, linkY + 4);
        FUIRenderer.DrawText(canvas, "vJoy from GitHub", new SKPoint(vJoyLinkX, linkY),
            _hoveredRegion == 2 ? FUIColors.Primary : FUIColors.Active, 12f);

        float hidHideLinkX = vJoyLinkX + vJoyLinkW + 24;
        float hidHideLinkW = linkPaint.MeasureText("HidHide from GitHub");
        _hidHideLinkBounds = new SKRect(hidHideLinkX, linkY - 12, hidHideLinkX + hidHideLinkW, linkY + 4);
        FUIRenderer.DrawText(canvas, "HidHide from GitHub", new SKPoint(hidHideLinkX, linkY),
            _hoveredRegion == 3 ? FUIColors.Primary : FUIColors.Active, 12f);

        // Bottom-anchored layout — all relative to b.Bottom
        float btnW = 110f, btnH = 34f, btnSpacing = 12f;
        float btnY = b.Bottom - 50;
        float logBoxBottom = btnY - 10f;
        float logBoxH = 120f;
        float logBoxTop = logBoxBottom - logBoxH;
        float logHeaderY = logBoxTop - 14f;

        // Install log header
        FUIRenderer.DrawText(canvas, "INSTALL LOG", new SKPoint(Pad, logHeaderY), FUIColors.TextDim, 12f, false);

        // Log frame
        var logFrame = new SKRect(Pad - 1, logBoxTop, Pad + contentW + 1, logBoxTop + logBoxH);
        using var logFramePaint = FUIRenderer.CreateStrokePaint(FUIColors.FrameDim);
        canvas.DrawRect(logFrame, logFramePaint);

        // Bottom buttons
        if (_settingsMode)
        {
            // Settings mode: single CLOSE button, right-aligned
            _continueButtonBounds = SKRect.Empty;
            _exitButtonBounds = new SKRect(b.Right - Pad - btnW, btnY, b.Right - Pad, btnY + btnH);
            FUIRenderer.DrawButton(canvas, _exitButtonBounds, "CLOSE",
                _hoveredRegion == 1 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);
        }
        else
        {
            // Startup mode: CONTINUE/SKIP VJOY + EXIT
            _exitButtonBounds = new SKRect(b.Right - Pad - btnW, btnY, b.Right - Pad, btnY + btnH);
            _continueButtonBounds = new SKRect(b.Right - Pad - btnW * 2 - btnSpacing, btnY,
                b.Right - Pad - btnW - btnSpacing, btnY + btnH);

            FUIRenderer.DrawButton(canvas, _exitButtonBounds, "EXIT",
                _hoveredRegion == 1 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal);

            if (_continueEnabled)
            {
                FUIRenderer.DrawButton(canvas, _continueButtonBounds, "CONTINUE",
                    _hoveredRegion == 0 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal,
                    FUIColors.Active);
            }
            else
            {
                FUIRenderer.DrawButton(canvas, _continueButtonBounds, "SKIP VJOY",
                    _hoveredRegion == 0 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal,
                    FUIColors.Warning.WithAlpha(180));
            }
        }

        // "Don't show again" checkbox — bottom-left, aligned with button row
        float cbSize = 16f;
        float cbY = btnY + (btnH - cbSize) / 2;
        _dontShowAgainBounds = new SKRect(Pad, cbY, Pad + cbSize, cbY + cbSize);
        FUIWidgets.DrawCheckbox(canvas, _dontShowAgainBounds, _dontShowAgain,
            new System.Drawing.Point((int)(_hoveredRegion == 6 ? _dontShowAgainBounds.MidX : -1),
                                     (int)(_hoveredRegion == 6 ? _dontShowAgainBounds.MidY : -1)));
        float cbLabelX = Pad + cbSize + 8f;
        float cbLabelY = btnY + btnH / 2 + 5f;
        FUIRenderer.DrawText(canvas, "Don't show again", new SKPoint(cbLabelX, cbLabelY),
            _hoveredRegion == 6 ? FUIColors.TextBright : FUIColors.TextDim, 13f);

        // L-corner decorations
        FUIRenderer.DrawLCornerFrame(canvas, b.Inset(-4, -4), FUIColors.Frame.WithAlpha(100), 20f, 6f, 1f);
    }

    private static void DrawInstallButton(SKCanvas canvas, SKRect bounds, bool installed, bool installing, bool hovered)
    {
        if (installed)
        {
            FUIRenderer.DrawButton(canvas, bounds, "INSTALLED",
                FUIRenderer.ButtonState.Active, FUIColors.Success);
        }
        else if (installing)
        {
            FUIRenderer.DrawButton(canvas, bounds, "DOWNLOADING",
                FUIRenderer.ButtonState.Disabled);
        }
        else
        {
            FUIRenderer.DrawButton(canvas, bounds, "INSTALL",
                hovered ? FUIRenderer.ButtonState.Active : FUIRenderer.ButtonState.Hover,
                FUIColors.Active);
        }
    }

    private static void DrawDriverPanel(SKCanvas canvas, SKRect bounds, string title, string badge,
        string description, string statusText, SKColor statusColor, bool required)
    {
        // Background
        using var bgPaint = FUIRenderer.CreateFillPaint(FUIColors.Background2);
        canvas.DrawRect(bounds, bgPaint);

        // Frame
        FUIRenderer.DrawFrame(canvas, bounds, FUIColors.Frame, FUIRenderer.ChamferSize);

        // Left accent bar
        var accentColor = required ? FUIColors.Active : FUIColors.Warning.WithAlpha(200);
        using var accentPaint = FUIRenderer.CreateFillPaint(accentColor);
        canvas.DrawRect(new SKRect(bounds.Left + 2, bounds.Top + 2, bounds.Left + 5, bounds.Bottom - 2), accentPaint);

        // Title
        FUIRenderer.DrawText(canvas, title, new SKPoint(bounds.Left + 14, bounds.Top + 22),
            FUIColors.TextBright, 15f, false);

        // Badge (REQUIRED / RECOMMENDED)
        var badgeColor = required ? FUIColors.Active : FUIColors.Warning;
        FUIRenderer.DrawText(canvas, badge, new SKPoint(bounds.Left + 14, bounds.Top + 38), badgeColor, 12f, false);

        // Description
        FUIRenderer.DrawText(canvas, description, new SKPoint(bounds.Left + 14, bounds.Top + 60),
            FUIColors.TextDim, 13f);

        // Status
        FUIRenderer.DrawText(canvas, statusText, new SKPoint(bounds.Left + 14, bounds.Top + 95),
            statusColor, 13f);
    }

    private void OnCanvasMouseMove(object? sender, MouseEventArgs e)
    {
        float s = FUIRenderer.CanvasScaleFactor;
        var pt = new SKPoint(e.X / s, e.Y / s);
        int newHovered = -1;

        if (_continueButtonBounds.Contains(pt)) newHovered = 0;
        else if (_exitButtonBounds.Contains(pt)) newHovered = 1;
        else if (_vJoyLinkBounds.Contains(pt)) newHovered = 2;
        else if (_hidHideLinkBounds.Contains(pt)) newHovered = 3;
        else if (!_vJoyInstalled && !_vJoyInstalling && _vJoyInstallBounds.Contains(pt)) newHovered = 4;
        else if (!_hidHideInstalled && !_hidHideInstalling && _hidHideInstallBounds.Contains(pt)) newHovered = 5;
        else if (_dontShowAgainBounds.Contains(pt) || CheckDontShowAgainLabelHit(pt)) newHovered = 6;

        if (newHovered != _hoveredRegion)
        {
            _hoveredRegion = newHovered;
            Cursor = newHovered >= 0 ? Cursors.Hand : Cursors.Default;
            _canvas.Invalidate();
        }

        if (_isDragging)
        {
            var screen = PointToScreen(e.Location);
            Location = new Point(screen.X - _dragStart.X, screen.Y - _dragStart.Y);
        }
    }

    private void OnCanvasMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        float s = FUIRenderer.CanvasScaleFactor;
        if (e.Y / s < TitleBarH)
        {
            _isDragging = true;
            _dragStart = e.Location;
        }
    }

    private void OnCanvasMouseUp(object? sender, MouseEventArgs e)
    {
        _isDragging = false;
        if (e.Button != MouseButtons.Left) return;

        if (_hoveredRegion == 0)
        {
            if (_continueEnabled)
            {
                SetupComplete = true;
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                SkippedVJoy = true;
                SetupComplete = true;
                DialogResult = DialogResult.OK;
                Close();
            }
        }
        else if (_hoveredRegion == 1) { DialogResult = DialogResult.Cancel; Close(); }
        else if (_hoveredRegion == 2) OpenUrl(_driverSetup.GetVJoyReleasesUrl());
        else if (_hoveredRegion == 3) OpenUrl(_driverSetup.GetHidHideReleasesUrl());
        else if (_hoveredRegion == 4 && !_vJoyInstalled && !_vJoyInstalling) _ = VJoyInstallAsync();
        else if (_hoveredRegion == 5 && !_hidHideInstalled && !_hidHideInstalling) _ = HidHideInstallAsync();
        else if (_hoveredRegion == 6)
        {
            _dontShowAgain = !_dontShowAgain;
            if (_appSettings is not null) _appSettings.SkipDriverSetup = _dontShowAgain;
            _canvas.Invalidate();
        }
    }

    private void OnCanvasMouseLeave(object? sender, EventArgs e)
    {
        if (_hoveredRegion >= 0)
        {
            _hoveredRegion = -1;
            _canvas.Invalidate();
        }
    }

    private void RefreshDriverStatus()
    {
        var status = _driverSetup.GetSetupStatus();

        _vJoyInstalled = status.VJoyInstalled;
        if (status.VJoyInstalled)
        {
            _vJoyStatusText = $"Installed at: {status.VJoyInstallPath ?? "Unknown"}";
            _vJoyStatusColor = FUIColors.Success;
        }
        else
        {
            _vJoyStatusText = "Not installed";
            _vJoyStatusColor = FUIColors.Danger;
        }

        _hidHideInstalled = status.HidHideInstalled;
        if (status.HidHideInstalled)
        {
            _hidHideStatusText = "Installed";
            _hidHideStatusColor = FUIColors.Success;
        }
        else
        {
            _hidHideStatusText = "Not installed (optional but recommended)";
            _hidHideStatusColor = FUIColors.Warning;
        }

        if (status.IsComplete)
        {
            _statusText = status.HidHideInstalled
                ? "All drivers installed and ready."
                : "vJoy installed. HidHide is recommended but optional.";
            _statusColor = FUIColors.Success;
            _continueEnabled = true;
        }
        else
        {
            _statusText = "vJoy is required to enable input forwarding.\nYou can continue without vJoy, but the app will run in configuration-only mode.";
            _statusColor = FUIColors.Warning;
            _continueEnabled = false;
        }

        _canvas.Invalidate();

        Log($"vJoy: {(status.VJoyInstalled ? "Installed" : "Not installed")}");
        Log($"HidHide: {(status.HidHideInstalled ? "Installed" : "Not installed")}");
    }

    private async Task VJoyInstallAsync()
    {
        _vJoyInstalling = true;
        _progressBar.Visible = true;
        _progressBar.Value = 0;
        _canvas.Invalidate();

        Log("Downloading vJoy installer...");

        var progress = new Progress<int>(p => _progressBar.Value = p);
        var installerPath = await _driverSetup.DownloadVJoyInstallerAsync(progress);

        if (string.IsNullOrEmpty(installerPath))
        {
            Log("Failed to download vJoy installer");
            _vJoyInstalling = false;
            _progressBar.Visible = false;
            _canvas.Invalidate();

            int choice = FUIMessageBox.Show(this,
                "Failed to download the vJoy installer.\n\nOpen the GitHub releases page to download manually?",
                "Download Failed", FUIMessageBox.MessageBoxType.Error, "Open GitHub", "Cancel");
            if (choice == 0) OpenUrl(_driverSetup.GetVJoyReleasesUrl());
            return;
        }

        Log($"Downloaded to: {installerPath}");
        Log("Launching installer (UAC prompt may appear)...");

        if (_driverSetup.LaunchVJoyInstaller(installerPath))
        {
            Log("Installer launched - please complete the installation");
            int choice = FUIMessageBox.Show(this,
                "The vJoy installer has been launched.\n\nComplete the installation wizard, then click Refresh.\nA system restart may be required.",
                "vJoy Installation", FUIMessageBox.MessageBoxType.Information, "Refresh", "Later");
            if (choice == 0) RefreshDriverStatus();
        }
        else
        {
            Log("Failed to launch installer");
            FUIMessageBox.ShowError(this,
                $"Failed to launch the vJoy installer.\n\nInstaller downloaded to:\n{installerPath}\n\nRun it manually with administrator privileges.",
                "Installation Error");
        }

        _vJoyInstalling = false;
        _progressBar.Visible = false;
        _canvas.Invalidate();
    }

    private async Task HidHideInstallAsync()
    {
        _hidHideInstalling = true;
        _progressBar.Visible = true;
        _progressBar.Value = 0;
        _canvas.Invalidate();

        Log("Downloading HidHide installer...");

        var progress = new Progress<int>(p => _progressBar.Value = p);
        var installerPath = await _driverSetup.DownloadHidHideInstallerAsync(progress);

        if (string.IsNullOrEmpty(installerPath))
        {
            Log("Failed to download HidHide installer");
            _hidHideInstalling = false;
            _progressBar.Visible = false;
            _canvas.Invalidate();

            int choice = FUIMessageBox.Show(this,
                "Failed to download the HidHide installer.\n\nOpen the GitHub releases page to download manually?",
                "Download Failed", FUIMessageBox.MessageBoxType.Error, "Open GitHub", "Cancel");
            if (choice == 0) OpenUrl(_driverSetup.GetHidHideReleasesUrl());
            return;
        }

        Log($"Downloaded to: {installerPath}");
        Log("Launching installer (UAC prompt may appear)...");

        if (_driverSetup.LaunchHidHideInstaller(installerPath))
        {
            Log("Installer launched - please complete the installation");
            int choice = FUIMessageBox.Show(this,
                "The HidHide installer has been launched.\n\nComplete the installation wizard, then click Refresh.\nA system restart may be required.",
                "HidHide Installation", FUIMessageBox.MessageBoxType.Information, "Refresh", "Later");
            if (choice == 0) RefreshDriverStatus();
        }
        else
        {
            Log("Failed to launch installer");
            FUIMessageBox.ShowError(this,
                $"Failed to launch the HidHide installer.\n\nInstaller downloaded to:\n{installerPath}\n\nRun it manually with administrator privileges.",
                "Installation Error");
        }

        _hidHideInstalling = false;
        _progressBar.Visible = false;
        _canvas.Invalidate();
    }

    private bool CheckDontShowAgainLabelHit(SKPoint pt)
    {
        if (_dontShowAgainBounds == default) return false;
        float labelX = _dontShowAgainBounds.Right + 8f;
        float labelW = FUIRenderer.MeasureText("Don't show again", 13f);
        var labelRect = new SKRect(labelX, _dontShowAgainBounds.Top, labelX + labelW, _dontShowAgainBounds.Bottom);
        return labelRect.Contains(pt);
    }

    private static void OpenUrl(string url)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            Invoke(() => Log(message));
            return;
        }

        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        _logListBox.Items.Add($"[{timestamp}] {message}");
        _logListBox.TopIndex = Math.Max(0, _logListBox.Items.Count - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _background.Dispose();
        base.Dispose(disposing);
    }

    private static Color ToColor(SKColor skColor) =>
        Color.FromArgb(skColor.Alpha, skColor.Red, skColor.Green, skColor.Blue);
}
