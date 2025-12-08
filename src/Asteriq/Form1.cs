using Asteriq.Services;
using Asteriq.Models;

namespace Asteriq;

public partial class Form1 : Form
{
    private readonly InputService _inputService;
    private ListBox _deviceList = null!;
    private TextBox _inputLog = null!;
    private Label _axisDisplay = null!;
    private Label _buttonDisplay = null!;

    public Form1()
    {
        InitializeComponent();
        _inputService = new InputService();
        SetupUI();
        InitializeInput();
    }

    private void SetupUI()
    {
        Text = "Asteriq - Input Test";
        Size = new Size(800, 600);
        BackColor = Color.FromArgb(20, 25, 30);

        // Device list
        var deviceLabel = new Label
        {
            Text = "Detected Devices:",
            ForeColor = Color.White,
            Location = new Point(10, 10),
            AutoSize = true
        };
        Controls.Add(deviceLabel);

        _deviceList = new ListBox
        {
            Location = new Point(10, 35),
            Size = new Size(350, 150),
            BackColor = Color.FromArgb(30, 35, 40),
            ForeColor = Color.White
        };
        Controls.Add(_deviceList);

        var refreshBtn = new Button
        {
            Text = "Refresh Devices",
            Location = new Point(370, 35),
            Size = new Size(120, 30),
            BackColor = Color.FromArgb(50, 60, 70),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        refreshBtn.Click += (s, e) => RefreshDevices();
        Controls.Add(refreshBtn);

        // Axis display
        var axisLabel = new Label
        {
            Text = "Axes:",
            ForeColor = Color.White,
            Location = new Point(10, 200),
            AutoSize = true
        };
        Controls.Add(axisLabel);

        _axisDisplay = new Label
        {
            Text = "No input",
            ForeColor = Color.LightGreen,
            Font = new Font("Consolas", 10),
            Location = new Point(10, 220),
            Size = new Size(760, 80),
            BackColor = Color.FromArgb(25, 30, 35)
        };
        Controls.Add(_axisDisplay);

        // Button display
        var buttonLabel = new Label
        {
            Text = "Buttons:",
            ForeColor = Color.White,
            Location = new Point(10, 310),
            AutoSize = true
        };
        Controls.Add(buttonLabel);

        _buttonDisplay = new Label
        {
            Text = "No input",
            ForeColor = Color.Orange,
            Font = new Font("Consolas", 10),
            Location = new Point(10, 330),
            Size = new Size(760, 60),
            BackColor = Color.FromArgb(25, 30, 35)
        };
        Controls.Add(_buttonDisplay);

        // Input log
        var logLabel = new Label
        {
            Text = "Event Log:",
            ForeColor = Color.White,
            Location = new Point(10, 400),
            AutoSize = true
        };
        Controls.Add(logLabel);

        _inputLog = new TextBox
        {
            Location = new Point(10, 420),
            Size = new Size(760, 120),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(25, 30, 35),
            ForeColor = Color.LightGray,
            Font = new Font("Consolas", 9),
            ReadOnly = true
        };
        Controls.Add(_inputLog);
    }

    private void InitializeInput()
    {
        if (!_inputService.Initialize())
        {
            Log("Failed to initialize SDL2!");
            return;
        }

        Log("SDL2 initialized successfully");

        _inputService.InputReceived += OnInputReceived;
        _inputService.DeviceDisconnected += OnDeviceDisconnected;

        RefreshDevices();
        _inputService.StartPolling(500); // 500Hz polling
        Log("Input polling started at 500Hz");
    }

    private void RefreshDevices()
    {
        _deviceList.Items.Clear();
        var devices = _inputService.EnumerateDevices();

        if (devices.Count == 0)
        {
            _deviceList.Items.Add("No devices found");
            Log("No joystick devices detected");
        }
        else
        {
            foreach (var device in devices)
            {
                _deviceList.Items.Add(device.ToString());
                Log($"Found: {device}");
            }
        }
    }

    private void OnInputReceived(object? sender, DeviceInputState state)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnInputReceived(sender, state));
            return;
        }

        // Update axis display
        var axisText = string.Join("  ", state.Axes.Select((v, i) => $"A{i}:{v:+0.00;-0.00}"));
        _axisDisplay.Text = axisText;

        // Update button display - show which are pressed
        var pressedButtons = state.Buttons
            .Select((pressed, i) => pressed ? $"B{i + 1}" : null)
            .Where(b => b != null)
            .ToList();

        _buttonDisplay.Text = pressedButtons.Count > 0
            ? string.Join(" ", pressedButtons)
            : "(none pressed)";
    }

    private void OnDeviceDisconnected(object? sender, int deviceIndex)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnDeviceDisconnected(sender, deviceIndex));
            return;
        }

        Log($"Device {deviceIndex} disconnected");
        RefreshDevices();
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(message));
            return;
        }

        _inputLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _inputService.Dispose();
        base.OnFormClosing(e);
    }
}
