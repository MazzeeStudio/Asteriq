using Asteriq.Services;
using SkiaSharp;

namespace Asteriq.UI;

/// <summary>
/// Form for checking and installing required drivers (vJoy)
/// </summary>
public class DriverSetupForm : Form
{
    private readonly DriverSetupManager _driverSetup;

    private Label _titleLabel = null!;
    private Label _statusLabel = null!;
    private ProgressBar _progressBar = null!;
    private ListBox _logListBox = null!;

    private Panel _vJoyPanel = null!;
    private Label _vJoyStatusLabel = null!;
    private Button _vJoyInstallButton = null!;

    private Panel _hidHidePanel = null!;
    private Label _hidHideStatusLabel = null!;
    private Button _hidHideInstallButton = null!;

    private LinkLabel _manualDownloadLink = null!;

    private Button _continueButton = null!;
    private Button _exitButton = null!;

    public bool SetupComplete { get; private set; }

    public DriverSetupForm(DriverSetupManager driverSetupManager)
    {
        _driverSetup = driverSetupManager ?? throw new ArgumentNullException(nameof(driverSetupManager));
        InitializeComponents();
        RefreshDriverStatus();
    }

    private void InitializeComponents()
    {
        Text = "Asteriq - Driver Setup";
        Size = new Size(720, 680);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = ToColor(FUIColors.Background1);
        ForeColor = ToColor(FUIColors.TextPrimary);

        // Set icon
        try
        {
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "asteriq.ico");
            if (File.Exists(iconPath))
            {
                Icon = new Icon(iconPath);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            // Icon loading is not critical
        }

        // Title
        _titleLabel = new Label
        {
            Text = "DRIVER SETUP",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = ToColor(FUIColors.Primary),
            Location = new Point(20, 20),
            AutoSize = true
        };
        Controls.Add(_titleLabel);

        // Status
        _statusLabel = new Label
        {
            Text = "Checking installed drivers...",
            Font = new Font("Segoe UI", 10),
            ForeColor = ToColor(FUIColors.TextDim),
            Location = new Point(20, 55),
            Size = new Size(670, 30)
        };
        Controls.Add(_statusLabel);

        // vJoy Panel
        _vJoyPanel = CreateDriverPanel(
            "vJoy Virtual Joystick Driver",
            "Required for creating virtual joystick devices that Star Citizen can see",
            95,
            out _vJoyStatusLabel,
            out _vJoyInstallButton,
            false);
        Controls.Add(_vJoyPanel);

        // HidHide Panel
        _hidHidePanel = CreateDriverPanel(
            "HidHide Device Hiding Driver (Recommended)",
            "Hides physical devices so only vJoy devices are seen by Star Citizen",
            245,
            out _hidHideStatusLabel,
            out _hidHideInstallButton,
            true);
        Controls.Add(_hidHidePanel);

        // Manual download link
        _manualDownloadLink = new LinkLabel
        {
            Text = "Download manually from GitHub",
            Location = new Point(20, 395),
            AutoSize = true,
            LinkColor = ToColor(FUIColors.Active),
            ActiveLinkColor = ToColor(FUIColors.Primary),
            VisitedLinkColor = ToColor(FUIColors.Active),
            Font = new Font("Segoe UI", 9)
        };
        _manualDownloadLink.Click += ManualDownload_Click;
        Controls.Add(_manualDownloadLink);

        // Progress bar (styled to match theme)
        _progressBar = new ProgressBar
        {
            Location = new Point(20, 425),
            Size = new Size(670, 23),
            Style = ProgressBarStyle.Continuous,
            Visible = false,
            ForeColor = ToColor(FUIColors.Active)
        };
        Controls.Add(_progressBar);

        // Log list
        _logListBox = new ListBox
        {
            Location = new Point(20, 460),
            Size = new Size(670, 130),
            Font = new Font("Consolas", 8),
            BackColor = ToColor(FUIColors.Background2),
            ForeColor = ToColor(FUIColors.TextPrimary),
            BorderStyle = BorderStyle.FixedSingle
        };
        Controls.Add(_logListBox);

        // Buttons at bottom (16px from bottom)
        _continueButton = new Button
        {
            Text = "Continue",
            Location = new Point(470, 610),
            Size = new Size(110, 35),
            BackColor = ToColor(FUIColors.Active),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Enabled = false,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        _continueButton.FlatAppearance.BorderSize = 0;
        _continueButton.Click += Continue_Click;
        Controls.Add(_continueButton);

        _exitButton = new Button
        {
            Text = "Exit",
            Location = new Point(590, 610),
            Size = new Size(100, 35),
            BackColor = ToColor(FUIColors.Background2),
            ForeColor = ToColor(FUIColors.TextPrimary),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10)
        };
        _exitButton.FlatAppearance.BorderSize = 0;
        _exitButton.Click += Exit_Click;
        Controls.Add(_exitButton);

        AcceptButton = _continueButton;
        CancelButton = _exitButton;
    }

    private Panel CreateDriverPanel(
        string title,
        string description,
        int top,
        out Label statusLabel,
        out Button installButton,
        bool isRecommended)
    {
        var panel = new Panel
        {
            Location = new Point(20, top),
            Size = new Size(670, 135),
            BackColor = ToColor(FUIColors.Background2),
            BorderStyle = BorderStyle.FixedSingle
        };

        var titleLbl = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = ToColor(FUIColors.TextPrimary),
            Location = new Point(15, 15),
            AutoSize = true
        };
        panel.Controls.Add(titleLbl);

        var descLbl = new Label
        {
            Text = description,
            Font = new Font("Segoe UI", 9),
            ForeColor = ToColor(FUIColors.TextDim),
            Location = new Point(15, 42),
            Size = new Size(635, 40)
        };
        panel.Controls.Add(descLbl);

        statusLabel = new Label
        {
            Text = "Checking...",
            Font = new Font("Segoe UI", 9),
            ForeColor = ToColor(FUIColors.TextDim),
            Location = new Point(15, 90),
            Size = new Size(500, 35)
        };
        panel.Controls.Add(statusLabel);

        installButton = new Button
        {
            Text = "Install",
            Location = new Point(560, 90),
            Size = new Size(95, 30),
            BackColor = ToColor(FUIColors.Active),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Enabled = false
        };
        installButton.FlatAppearance.BorderSize = 0;
        panel.Controls.Add(installButton);

        return panel;
    }

    private void RefreshDriverStatus()
    {
        var status = _driverSetup.GetSetupStatus();

        // vJoy status
        if (status.VJoyInstalled)
        {
            _vJoyStatusLabel.Text = $"Installed at: {status.VJoyInstallPath ?? "Unknown"}";
            _vJoyStatusLabel.ForeColor = ToColor(FUIColors.Success);
            _vJoyInstallButton.Enabled = false;
            _vJoyInstallButton.Text = "Installed";
            _vJoyInstallButton.BackColor = ToColor(FUIColors.Background2);
        }
        else
        {
            _vJoyStatusLabel.Text = "Not installed";
            _vJoyStatusLabel.ForeColor = ToColor(FUIColors.Danger);
            _vJoyInstallButton.Enabled = true;
            _vJoyInstallButton.Click -= VJoyInstall_Click;
            _vJoyInstallButton.Click += VJoyInstall_Click;
        }

        // HidHide status
        if (status.HidHideInstalled)
        {
            _hidHideStatusLabel.Text = "Installed";
            _hidHideStatusLabel.ForeColor = ToColor(FUIColors.Success);
            _hidHideInstallButton.Enabled = false;
            _hidHideInstallButton.Text = "Installed";
            _hidHideInstallButton.BackColor = ToColor(FUIColors.Background2);
        }
        else
        {
            _hidHideStatusLabel.Text = "Not installed (Recommended for best experience)";
            _hidHideStatusLabel.ForeColor = ToColor(FUIColors.Warning);
            _hidHideInstallButton.Enabled = true;
            _hidHideInstallButton.Click -= HidHideInstall_Click;
            _hidHideInstallButton.Click += HidHideInstall_Click;
        }

        // Update overall status
        if (status.IsComplete)
        {
            if (status.HidHideInstalled)
            {
                _statusLabel.Text = "All drivers installed and ready!";
            }
            else
            {
                _statusLabel.Text = "vJoy driver is installed. HidHide is recommended but optional.";
            }
            _statusLabel.ForeColor = ToColor(FUIColors.Success);
            _continueButton.Enabled = true;
        }
        else
        {
            _statusLabel.Text = "vJoy driver is required to run Asteriq";
            _statusLabel.ForeColor = ToColor(FUIColors.Danger);
            _continueButton.Enabled = false;
        }

        Log($"vJoy: {(status.VJoyInstalled ? "Installed" : "Not installed")}");
        Log($"HidHide: {(status.HidHideInstalled ? "Installed" : "Not installed")}");
    }

    private async void VJoyInstall_Click(object? sender, EventArgs e)
    {
        _vJoyInstallButton.Enabled = false;
        _progressBar.Visible = true;
        _progressBar.Value = 0;

        Log("Downloading vJoy installer...");

        var progress = new Progress<int>(p =>
        {
            _progressBar.Value = p;
        });

        var installerPath = await _driverSetup.DownloadVJoyInstallerAsync(progress);

        if (string.IsNullOrEmpty(installerPath))
        {
            Log("Failed to download vJoy installer");

            var result = MessageBox.Show(
                "Failed to download vJoy installer.\n\n" +
                "Would you like to download it manually from GitHub?",
                "Download Failed",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Error);

            if (result == DialogResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _driverSetup.GetVJoyReleasesUrl(),
                    UseShellExecute = true
                });
            }

            _vJoyInstallButton.Enabled = true;
            _progressBar.Visible = false;
            return;
        }

        Log($"Downloaded to: {installerPath}");
        Log("Launching installer (UAC prompt may appear)...");

        if (_driverSetup.LaunchVJoyInstaller(installerPath))
        {
            Log("Installer launched - please complete the installation");

            var result = MessageBox.Show(
                "The vJoy installer has been launched.\n\n" +
                "Please complete the installation wizard, then click 'Refresh' to check the driver status.\n\n" +
                "Note: A system restart may be required after installation.",
                "vJoy Installation",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);

            if (result == DialogResult.OK)
            {
                RefreshDriverStatus();
            }
        }
        else
        {
            Log("Failed to launch installer");
            MessageBox.Show(
                "Failed to launch the vJoy installer.\n\n" +
                "The installer was downloaded to:\n" + installerPath + "\n\n" +
                "Please run it manually with administrator privileges.",
                "Installation Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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

        var progress = new Progress<int>(p =>
        {
            _progressBar.Value = p;
        });

        var installerPath = await _driverSetup.DownloadHidHideInstallerAsync(progress);

        if (string.IsNullOrEmpty(installerPath))
        {
            Log("Failed to download HidHide installer");

            var result = MessageBox.Show(
                "Failed to download HidHide installer.\n\n" +
                "Would you like to download it manually from GitHub?",
                "Download Failed",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Error);

            if (result == DialogResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _driverSetup.GetHidHideReleasesUrl(),
                    UseShellExecute = true
                });
            }

            _hidHideInstallButton.Enabled = true;
            _progressBar.Visible = false;
            return;
        }

        Log($"Downloaded to: {installerPath}");
        Log("Launching installer (UAC prompt may appear)...");

        if (_driverSetup.LaunchHidHideInstaller(installerPath))
        {
            Log("Installer launched - please complete the installation");

            var result = MessageBox.Show(
                "The HidHide installer has been launched.\n\n" +
                "Please complete the installation wizard, then click 'Refresh' to check the driver status.\n\n" +
                "Note: A system restart may be required after installation.",
                "HidHide Installation",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);

            if (result == DialogResult.OK)
            {
                RefreshDriverStatus();
            }
        }
        else
        {
            Log("Failed to launch installer");
            MessageBox.Show(
                "Failed to launch the HidHide installer.\n\n" +
                "The installer was downloaded to:\n" + installerPath + "\n\n" +
                "Please run it manually with administrator privileges.",
                "Installation Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            _hidHideInstallButton.Enabled = true;
        }

        _progressBar.Visible = false;
    }

    private void ManualDownload_Click(object? sender, EventArgs e)
    {
        // Show context menu with manual download links
        var menu = new ContextMenuStrip();
        menu.Items.Add("vJoy from GitHub", null, (s, ev) =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _driverSetup.GetVJoyReleasesUrl(),
                UseShellExecute = true
            });
        });
        menu.Items.Add("HidHide from GitHub", null, (s, ev) =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _driverSetup.GetHidHideReleasesUrl(),
                UseShellExecute = true
            });
        });
        menu.Show(_manualDownloadLink, new Point(0, _manualDownloadLink.Height));
    }

    private void Continue_Click(object? sender, EventArgs e)
    {
        SetupComplete = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Exit_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
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

    /// <summary>
    /// Convert SKColor to System.Drawing.Color
    /// </summary>
    private static Color ToColor(SKColor skColor)
    {
        return Color.FromArgb(skColor.Alpha, skColor.Red, skColor.Green, skColor.Blue);
    }
}
