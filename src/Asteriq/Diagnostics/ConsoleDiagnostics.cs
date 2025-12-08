using Asteriq.Models;
using Asteriq.Services;

namespace Asteriq.Diagnostics;

/// <summary>
/// Console-based diagnostic display with live in-place updates
/// </summary>
public class ConsoleDiagnostics : IDisposable
{
    private readonly InputService _inputService;
    private readonly Dictionary<int, int> _deviceLineMap = new();
    private int _nextLine;
    private int _headerLines;
    private volatile bool _running;

    public ConsoleDiagnostics(InputService inputService)
    {
        _inputService = inputService;
    }

    /// <summary>
    /// Run the diagnostic display until ESC is pressed
    /// </summary>
    public void Run()
    {
        Console.Clear();
        Console.CursorVisible = false;
        _running = true;

        WriteHeader();

        _inputService.InputReceived += OnInputReceived;
        _inputService.DeviceDisconnected += OnDeviceDisconnected;

        // Enumerate and display initial devices
        var devices = _inputService.EnumerateDevices();
        foreach (var device in devices)
        {
            RegisterDevice(device);
        }

        _inputService.StartPolling(500);

        WriteFooter();

        // Wait for ESC
        while (_running)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Escape)
                {
                    _running = false;
                }
                else if (key.Key == ConsoleKey.R)
                {
                    RefreshDevices();
                }
            }
            Thread.Sleep(10);
        }

        _inputService.StopPolling();
        _inputService.InputReceived -= OnInputReceived;
        _inputService.DeviceDisconnected -= OnDeviceDisconnected;

        Console.CursorVisible = true;
    }

    private void WriteHeader()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                    ASTERIQ - Input Diagnostics                               ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");
        Console.ResetColor();
        _headerLines = 3;
        _nextLine = _headerLines;
    }

    private void WriteFooter()
    {
        Console.SetCursorPosition(0, Console.WindowHeight - 2);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  [ESC] Exit    [R] Refresh devices                                           ║");
        Console.ResetColor();
    }

    private void RegisterDevice(PhysicalDeviceInfo device)
    {
        if (_deviceLineMap.ContainsKey(device.DeviceIndex))
            return;

        int baseLine = _nextLine;
        _deviceLineMap[device.DeviceIndex] = baseLine;
        _nextLine += 4; // 4 lines per device: name, axes, buttons, separator

        // Write device header
        Console.SetCursorPosition(0, baseLine);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"║ [{device.DeviceIndex}] ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"{device.Name,-70}");
        Console.ResetColor();
    }

    private void OnInputReceived(object? sender, DeviceInputState state)
    {
        if (!_deviceLineMap.TryGetValue(state.DeviceIndex, out int baseLine))
            return;

        try
        {
            // Axes line (baseLine + 1)
            Console.SetCursorPosition(0, baseLine + 1);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("║   Axes: ");
            Console.ForegroundColor = ConsoleColor.Green;

            var axisStr = string.Join(" ", state.Axes.Select((v, i) =>
                $"{i}:{FormatAxis(v)}"));
            Console.Write(axisStr.PadRight(68));
            Console.ResetColor();
            Console.WriteLine("║");

            // Buttons line (baseLine + 2)
            Console.SetCursorPosition(0, baseLine + 2);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("║   Btns: ");
            Console.ForegroundColor = ConsoleColor.Magenta;

            var pressedButtons = state.Buttons
                .Select((pressed, i) => pressed ? (i + 1).ToString() : null)
                .Where(b => b != null)
                .ToList();

            var btnStr = pressedButtons.Count > 0
                ? string.Join(" ", pressedButtons)
                : "(none)";
            Console.Write(btnStr.PadRight(68));
            Console.ResetColor();
            Console.WriteLine("║");

            // Separator (baseLine + 3)
            Console.SetCursorPosition(0, baseLine + 3);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("╟──────────────────────────────────────────────────────────────────────────────╢");
            Console.ResetColor();
        }
        catch
        {
            // Ignore console errors during resize
        }
    }

    private void OnDeviceDisconnected(object? sender, int deviceIndex)
    {
        if (_deviceLineMap.TryGetValue(deviceIndex, out int baseLine))
        {
            Console.SetCursorPosition(0, baseLine);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"║ [{deviceIndex}] DISCONNECTED".PadRight(79) + "║");
            Console.ResetColor();
        }
    }

    private void RefreshDevices()
    {
        Console.Clear();
        _deviceLineMap.Clear();
        _nextLine = 0;

        WriteHeader();

        var devices = _inputService.EnumerateDevices();
        foreach (var device in devices)
        {
            RegisterDevice(device);
        }

        WriteFooter();
    }

    private static string FormatAxis(float value)
    {
        // Format as +0.00 or -0.00 with fixed width
        return value >= 0 ? $"+{value:0.00}" : $"{value:0.00}";
    }

    public void Dispose()
    {
        _running = false;
    }
}
