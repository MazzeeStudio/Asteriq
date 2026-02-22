using Asteriq.Services;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace Asteriq.UI;

/// <summary>
/// FUI-styled form for checking and installing required drivers (vJoy, HidHide).
/// </summary>
public class DriverSetupForm : Form
{
    private readonly DriverSetupManager _driverSetup;
    private readonly SKControl _canvas;

    // Native controls overlaid on canvas
    private readonly Button _vJoyInstallButton;
    private readonly Button _hidHideInstallButton;
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

    // Canvas-drawn interactive regions
    private SKRect _continueButtonBounds;
    private SKRect _exitButtonBounds;
    private SKRect _vJoyLinkBounds;
    private SKRect _hidHideLinkBounds;
    private int _hoveredRegion = -1; // 0=left-btn (continue or skip), 1=exit, 2=vjoy-link, 3=hidhide-link

    // Dragging
    private bool _isDragging;
    private Point _dragStart;

    // Layout constants
    private const float TitleBarH = 36f;
    private const float FormW = 720f;
    private const float FormH = 600f;
    private const float Pad = 20f;
    private const float ContentW = FormW - Pad * 2;   // 680
    private const float Panel1Y = 76f;                // vJoy panel top
    private const float PanelH = 122f;                // panel height
    private const float Panel2Y = Panel1Y + PanelH + 10f;  // HidHide panel top = 208
    private const float InstallBtnOffsetY = 82f;      // install button y relative to panel top

    public bool SetupComplete { get; private set; }
    public bool SkippedVJoy { get; private set; }

    public DriverSetupForm(DriverSetupManager driverSetupManager)
    {
        _driverSetup = driverSetupManager ?? throw new ArgumentNullException(nameof(driverSetupManager));
        _statusColor = FUIColors.TextDim;
        _vJoyStatusColor = FUIColors.TextDim;
        _hidHideStatusColor = FUIColors.TextDim;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(6, 8, 10);
        ShowInTaskbar = true;
        KeyPreview = true;
        ClientSize = new Size((int)FormW, (int)FormH);

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

        // vJoy install button (overlaid on vJoy panel)
        int installBtnX = (int)(Pad + ContentW - 92);
        _vJoyInstallButton = MakeInstallButton(installBtnX, (int)(Panel1Y + InstallBtnOffsetY));
        _vJoyInstallButton.Click += VJoyInstall_Click;

        // HidHide install button (overlaid on HidHide panel)
        _hidHideInstallButton = MakeInstallButton(installBtnX, (int)(Panel2Y + InstallBtnOffsetY));
        _hidHideInstallButton.Click += HidHideInstall_Click;

        // Progress bar (shown only during download)
        _progressBar = new ProgressBar
        {
            Location = new Point((int)Pad, 362),
            Size = new Size((int)ContentW, 14),
            Style = ProgressBarStyle.Continuous,
            Visible = false,
        };

        // Install log
        _logListBox = new ListBox
        {
            Location = new Point((int)Pad, 398),
            Size = new Size((int)ContentW, 122),
            Font = new Font("Consolas", 8),
            BackColor = ToColor(FUIColors.Background2),
            ForeColor = ToColor(FUIColors.TextPrimary),
            BorderStyle = BorderStyle.None,
        };

        foreach (Control ctl in new Control[] { _vJoyInstallButton, _hidHideInstallButton, _progressBar, _logListBox })
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

    private static Button MakeInstallButton(int x, int y)
    {
        var btn = new Button
        {
            Text = "INSTALL",
            Location = new Point(x, y),
            Size = new Size(88, 28),
            BackColor = ToColor(FUIColors.Active),
            ForeColor = Color.FromArgb(240, 244, 248),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Consolas", 8, FontStyle.Bold),
            Enabled = false,
        };
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.BorderColor = ToColor(FUIColors.FrameBright);
        return btn;
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var b = new SKRect(0, 0, e.Info.Width, e.Info.Height);

        canvas.Clear(FUIColors.Background0);

        // Outer chamfered frame
        FUIRenderer.DrawFrame(canvas, b.Inset(-1, -1), FUIColors.Frame, FUIRenderer.ChamferSize);

        // Title bar
        var titleBar = new SKRect(b.Left + 2, b.Top + 2, b.Right - 2, b.Top + TitleBarH);
        using var titleBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        canvas.DrawRect(titleBar, titleBgPaint);
        using var sepPaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
        canvas.DrawLine(titleBar.Left, titleBar.Bottom, titleBar.Right, titleBar.Bottom, sepPaint);
        FUIRenderer.DrawText(canvas, "DRIVER SETUP", new SKPoint(16, titleBar.MidY + 5), FUIColors.TextBright, 13f, false);

        // Overall status
        FUIRenderer.DrawText(canvas, _statusText, new SKPoint(Pad, TitleBarH + 24), _statusColor, 11f);

        // Driver panels
        DrawDriverPanel(canvas,
            new SKRect(Pad, Panel1Y, Pad + ContentW, Panel1Y + PanelH),
            "vJOY VIRTUAL JOYSTICK", "REQUIRED",
            "Creates virtual joystick devices visible to Star Citizen",
            _vJoyStatusText, _vJoyStatusColor, required: true);

        DrawDriverPanel(canvas,
            new SKRect(Pad, Panel2Y, Pad + ContentW, Panel2Y + PanelH),
            "HIDHIDE DEVICE HIDING", "RECOMMENDED",
            "Hides physical devices so only virtual devices are visible",
            _hidHideStatusText, _hidHideStatusColor, required: false);

        // Manual download links
        float linkY = Panel2Y + PanelH + 18;
        FUIRenderer.DrawText(canvas, "MANUAL DOWNLOAD:", new SKPoint(Pad, linkY), FUIColors.TextDim, 9f, false);

        using var linkPaint = FUIRenderer.CreateTextPaint(FUIColors.Active, FUIRenderer.ScaleFont(9f));

        float vJoyLinkX = Pad + 142;
        float vJoyLinkW = linkPaint.MeasureText("vJoy from GitHub");
        _vJoyLinkBounds = new SKRect(vJoyLinkX, linkY - 12, vJoyLinkX + vJoyLinkW, linkY + 4);
        FUIRenderer.DrawText(canvas, "vJoy from GitHub", new SKPoint(vJoyLinkX, linkY),
            _hoveredRegion == 2 ? FUIColors.Primary : FUIColors.Active, 9f);

        float hidHideLinkX = vJoyLinkX + vJoyLinkW + 24;
        float hidHideLinkW = linkPaint.MeasureText("HidHide from GitHub");
        _hidHideLinkBounds = new SKRect(hidHideLinkX, linkY - 12, hidHideLinkX + hidHideLinkW, linkY + 4);
        FUIRenderer.DrawText(canvas, "HidHide from GitHub", new SKPoint(hidHideLinkX, linkY),
            _hoveredRegion == 3 ? FUIColors.Primary : FUIColors.Active, 9f);

        // Install log header
        FUIRenderer.DrawText(canvas, "INSTALL LOG", new SKPoint(Pad, 388), FUIColors.TextDim, 9f, false);

        // Log frame
        var logFrame = new SKRect(Pad - 1, 394, Pad + ContentW + 1, 394 + 124);
        using var logFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.FrameDim, StrokeWidth = 1f };
        canvas.DrawRect(logFrame, logFramePaint);

        // Bottom buttons
        float btnW = 110f, btnH = 34f, btnSpacing = 12f;
        float btnY = b.Bottom - 50;

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
            // vJoy not installed - offer skip instead of a disabled continue
            FUIRenderer.DrawButton(canvas, _continueButtonBounds, "SKIP VJOY",
                _hoveredRegion == 0 ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal,
                FUIColors.Warning.WithAlpha(180));
        }

        // L-corner decorations
        FUIRenderer.DrawLCornerFrame(canvas, b.Inset(-4, -4), FUIColors.Frame.WithAlpha(100), 20f, 6f, 1f);
    }

    private static void DrawDriverPanel(SKCanvas canvas, SKRect bounds, string title, string badge,
        string description, string statusText, SKColor statusColor, bool required)
    {
        // Background
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        canvas.DrawRect(bounds, bgPaint);

        // Frame
        FUIRenderer.DrawFrame(canvas, bounds, FUIColors.Frame, FUIRenderer.ChamferSize);

        // Left accent bar
        var accentColor = required ? FUIColors.Active : FUIColors.Warning.WithAlpha(200);
        using var accentPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = accentColor };
        canvas.DrawRect(new SKRect(bounds.Left + 2, bounds.Top + 2, bounds.Left + 5, bounds.Bottom - 2), accentPaint);

        // Title
        FUIRenderer.DrawText(canvas, title, new SKPoint(bounds.Left + 14, bounds.Top + 22),
            FUIColors.TextBright, 12f, false);

        // Badge (REQUIRED / RECOMMENDED)
        var badgeColor = required ? FUIColors.Active : FUIColors.Warning;
        FUIRenderer.DrawText(canvas, badge, new SKPoint(bounds.Left + 14, bounds.Top + 38), badgeColor, 9f, false);

        // Description
        FUIRenderer.DrawText(canvas, description, new SKPoint(bounds.Left + 14, bounds.Top + 60),
            FUIColors.TextDim, 10f);

        // Status
        FUIRenderer.DrawText(canvas, statusText, new SKPoint(bounds.Left + 14, bounds.Top + 95),
            statusColor, 10f);
    }

    private void OnCanvasMouseMove(object? sender, MouseEventArgs e)
    {
        var pt = new SKPoint(e.X, e.Y);
        int newHovered = -1;

        if (_continueButtonBounds.Contains(pt)) newHovered = 0;
        else if (_exitButtonBounds.Contains(pt)) newHovered = 1;
        else if (_vJoyLinkBounds.Contains(pt)) newHovered = 2;
        else if (_hidHideLinkBounds.Contains(pt)) newHovered = 3;

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
        if (e.Y < TitleBarH)
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

        if (status.VJoyInstalled)
        {
            _vJoyStatusText = $"Installed at: {status.VJoyInstallPath ?? "Unknown"}";
            _vJoyStatusColor = FUIColors.Success;
            _vJoyInstallButton.Enabled = false;
            _vJoyInstallButton.Text = "INSTALLED";
            _vJoyInstallButton.BackColor = ToColor(FUIColors.Background2);
        }
        else
        {
            _vJoyStatusText = "Not installed";
            _vJoyStatusColor = FUIColors.Danger;
            _vJoyInstallButton.Enabled = true;
            _vJoyInstallButton.Text = "INSTALL";
            _vJoyInstallButton.BackColor = ToColor(FUIColors.Active);
            _vJoyInstallButton.Click -= VJoyInstall_Click;
            _vJoyInstallButton.Click += VJoyInstall_Click;
        }

        if (status.HidHideInstalled)
        {
            _hidHideStatusText = "Installed";
            _hidHideStatusColor = FUIColors.Success;
            _hidHideInstallButton.Enabled = false;
            _hidHideInstallButton.Text = "INSTALLED";
            _hidHideInstallButton.BackColor = ToColor(FUIColors.Background2);
        }
        else
        {
            _hidHideStatusText = "Not installed (optional but recommended)";
            _hidHideStatusColor = FUIColors.Warning;
            _hidHideInstallButton.Enabled = true;
            _hidHideInstallButton.Text = "INSTALL";
            _hidHideInstallButton.BackColor = ToColor(FUIColors.Active);
            _hidHideInstallButton.Click -= HidHideInstall_Click;
            _hidHideInstallButton.Click += HidHideInstall_Click;
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
            _statusText = "vJoy required for forwarding. You may continue in configuration-only mode.";
            _statusColor = FUIColors.Warning;
            _continueEnabled = false;
        }

        _canvas.Invalidate();

        Log($"vJoy: {(status.VJoyInstalled ? "Installed" : "Not installed")}");
        Log($"HidHide: {(status.HidHideInstalled ? "Installed" : "Not installed")}");
    }

    private async void VJoyInstall_Click(object? sender, EventArgs e)
    {
        _vJoyInstallButton.Enabled = false;
        _progressBar.Visible = true;
        _progressBar.Value = 0;

        Log("Downloading vJoy installer...");

        var progress = new Progress<int>(p => _progressBar.Value = p);
        var installerPath = await _driverSetup.DownloadVJoyInstallerAsync(progress);

        if (string.IsNullOrEmpty(installerPath))
        {
            Log("Failed to download vJoy installer");
            int choice = FUIMessageBox.Show(this,
                "Failed to download the vJoy installer.\n\nOpen the GitHub releases page to download manually?",
                "Download Failed", FUIMessageBox.MessageBoxType.Error, "Open GitHub", "Cancel");
            if (choice == 0) OpenUrl(_driverSetup.GetVJoyReleasesUrl());

            _vJoyInstallButton.Enabled = true;
            _progressBar.Visible = false;
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
            _vJoyInstallButton.Enabled = true;
        }

        _progressBar.Visible = false;
    }

    private async void HidHideInstall_Click(object? sender, EventArgs e)
    {
        _hidHideInstallButton.Enabled = false;
        _progressBar.Visible = true;
        _progressBar.Value = 0;

        Log("Downloading HidHide installer...");

        var progress = new Progress<int>(p => _progressBar.Value = p);
        var installerPath = await _driverSetup.DownloadHidHideInstallerAsync(progress);

        if (string.IsNullOrEmpty(installerPath))
        {
            Log("Failed to download HidHide installer");
            int choice = FUIMessageBox.Show(this,
                "Failed to download the HidHide installer.\n\nOpen the GitHub releases page to download manually?",
                "Download Failed", FUIMessageBox.MessageBoxType.Error, "Open GitHub", "Cancel");
            if (choice == 0) OpenUrl(_driverSetup.GetHidHideReleasesUrl());

            _hidHideInstallButton.Enabled = true;
            _progressBar.Visible = false;
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
            _hidHideInstallButton.Enabled = true;
        }

        _progressBar.Visible = false;
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

    private static Color ToColor(SKColor skColor) =>
        Color.FromArgb(skColor.Alpha, skColor.Red, skColor.Green, skColor.Blue);
}
