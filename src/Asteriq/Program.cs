using Asteriq.Diagnostics;
using Asteriq.Services;
using Asteriq.VJoy;
using System.Runtime.InteropServices;

namespace Asteriq;

static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h") || args.Contains("/?"))
        {
            if (!AttachConsole(-1))
                AllocConsole();
            ShowHelp();
            return;
        }

        if (args.Contains("--diag") || args.Contains("-d"))
        {
            if (!AttachConsole(-1))
                AllocConsole();
            RunDiagnostics();
            return;
        }

        if (args.Contains("--passthrough") || args.Contains("-p"))
        {
            if (!AttachConsole(-1))
                AllocConsole();
            RunPassthrough(args);
            return;
        }

        if (args.Contains("--hidhide"))
        {
            if (!AttachConsole(-1))
                AllocConsole();
            RunHidHideDiag();
            return;
        }

        if (args.Contains("--match"))
        {
            if (!AttachConsole(-1))
                AllocConsole();
            RunDeviceMatching();
            return;
        }

        if (args.Contains("--whitelist"))
        {
            if (!AttachConsole(-1))
                AllocConsole();
            RunWhitelist();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }

    private static void ShowHelp()
    {
        Console.WriteLine(@"
Asteriq - Unified HOTAS Management

Usage: Asteriq.exe [command] [options]

Commands:
  (none)              Launch the GUI application

  --help, -h, /?      Show this help message

  --diag, -d          Run input diagnostics
                      Lists all connected joysticks and displays real-time
                      axis/button state for each device. Useful for verifying
                      SDL2 is detecting your devices correctly.

  --passthrough, -p <device> <vjoy>
                      Pass physical device input to vJoy device
                      <device>  Physical device index (from --diag output)
                      <vjoy>    Target vJoy device ID (1-16)
                      Example: Asteriq.exe --passthrough 1 1

  --hidhide           Show HidHide configuration
                      Lists gaming devices, hidden devices, and whitelisted
                      applications. Requires HidHide to be installed.

  --match             Show device matching between SDL and HidHide
                      Correlates SDL device indices with HID instance paths.
                      Useful for understanding which devices to hide.

  --whitelist         Add Asteriq to HidHide whitelist
                      Allows Asteriq to see hidden devices.

Examples:
  Asteriq.exe                     Launch GUI
  Asteriq.exe --diag              Test input devices
  Asteriq.exe --passthrough 1 1   Map device 1 to vJoy 1
  Asteriq.exe --hidhide           Show HidHide status
");
    }

    private static void RunDiagnostics()
    {
        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "asteriq_diag.log");

        void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
            File.AppendAllText(logPath, line + Environment.NewLine);
            Console.WriteLine(line);
        }

        try
        {
            File.WriteAllText(logPath, ""); // Clear log
            Log("Starting Asteriq Diagnostics...");
            Log($"Working directory: {Environment.CurrentDirectory}");
            Log($"Exe location: {AppContext.BaseDirectory}");

            // Check if SDL2.dll exists
            var sdlPath = Path.Combine(AppContext.BaseDirectory, "SDL2.dll");
            Log($"SDL2.dll exists at root: {File.Exists(sdlPath)}");

            var sdlRuntimePath = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "SDL2.dll");
            Log($"SDL2.dll exists in runtimes: {File.Exists(sdlRuntimePath)}");

            Log("Creating InputService...");
            var inputService = new InputService();

            Log("Initializing SDL2...");
            if (!inputService.Initialize())
            {
                Log("Failed to initialize SDL2!");
                Log("Check log at: " + logPath);
                MessageBox.Show($"Failed to initialize SDL2!\nSee log: {logPath}", "Error");
                return;
            }
            Log("SDL2 initialized successfully.");

            Log("Enumerating devices...");
            var devices = inputService.EnumerateDevices();
            Log($"Found {devices.Count} device(s):");
            foreach (var device in devices)
            {
                Log($"  [{device.DeviceIndex}] {device.Name} - Axes: {device.AxisCount}, Buttons: {device.ButtonCount}, Hats: {device.HatCount}");
            }

            if (devices.Count == 0)
            {
                Log("No devices found.");
                MessageBox.Show($"No devices found.\nSee log: {logPath}", "Info");
                inputService.Dispose();
                return;
            }

            Log("Starting input polling...");

            // Track state for each device on separate lines
            var deviceLines = new Dictionary<int, int>();
            int nextLine = Console.CursorTop;
            foreach (var dev in devices)
            {
                deviceLines[dev.DeviceIndex] = nextLine++;
                Console.WriteLine($"[{dev.DeviceIndex}] {dev.Name,-30} waiting...");
            }
            int endLine = nextLine;

            inputService.InputReceived += (sender, state) =>
            {
                if (!deviceLines.TryGetValue(state.DeviceIndex, out int line))
                    return;

                var axes = string.Join(" ", state.Axes.Take(6).Select((v, i) => $"A{i}:{v:+0.00;-0.00}"));
                var pressedBtns = state.Buttons.Select((p, i) => p ? (i + 1).ToString() : null).Where(b => b != null);
                var buttons = string.Join(",", pressedBtns);

                // Move to device's line and overwrite (with bounds check)
                try
                {
                    if (line >= 0 && line < Console.BufferHeight)
                    {
                        Console.SetCursorPosition(0, line);
                        Console.Write($"[{state.DeviceIndex}] {axes} | Btn: {(buttons.Length > 0 ? buttons : "-"),-20}");
                    }
                }
                catch (ArgumentOutOfRangeException) { /* Console resized */ }
            };

            inputService.StartPolling(100); // 100Hz

            // Move cursor below device lines (with bounds check)
            try
            {
                if (endLine >= 0 && endLine < Console.BufferHeight)
                    Console.SetCursorPosition(0, endLine);
            }
            catch (ArgumentOutOfRangeException) { }
            Log("Polling... Press any key to stop.");
            Console.ReadKey(true);
            Log("");

            Log("Stopping...");
            inputService.StopPolling();
            inputService.Dispose();
            Log("Done.");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            Log(ex.StackTrace ?? "No stack trace");
            MessageBox.Show($"Error: {ex.Message}\nSee log: {logPath}", "Error");
            Environment.Exit(1);
        }
    }

    private static void RunPassthrough(string[] args)
    {
        Console.WriteLine("=== Asteriq Passthrough Test ===\n");

        // Parse arguments: --passthrough <physicalIndex> <vjoyId>
        int physicalIndex = 1; // Default to device 1 (first VPC stick)
        uint vjoyId = 1;       // Default to vJoy device 1

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--passthrough" || args[i] == "-p")
            {
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out int pIdx))
                    physicalIndex = pIdx;
                if (i + 2 < args.Length && uint.TryParse(args[i + 2], out uint vId))
                    vjoyId = vId;
            }
        }

        Console.WriteLine($"Physical device index: {physicalIndex}");
        Console.WriteLine($"vJoy target device: {vjoyId}\n");

        // Initialize input
        var inputService = new InputService();
        if (!inputService.Initialize())
        {
            Console.WriteLine("Failed to initialize SDL2!");
            Console.ReadKey();
            return;
        }
        Console.WriteLine("SDL2 initialized.");

        // Enumerate physical devices
        var devices = inputService.EnumerateDevices();
        Console.WriteLine($"Found {devices.Count} physical device(s):");
        foreach (var dev in devices)
        {
            string marker = dev.DeviceIndex == physicalIndex ? " <-- SOURCE" : "";
            Console.WriteLine($"  [{dev.DeviceIndex}] {dev.Name}{marker}");
        }

        var sourceDevice = devices.FirstOrDefault(d => d.DeviceIndex == physicalIndex);
        if (sourceDevice == null)
        {
            Console.WriteLine($"\nDevice index {physicalIndex} not found!");
            Console.ReadKey();
            return;
        }

        // Initialize vJoy
        var vjoyService = new VJoyService();
        if (!vjoyService.Initialize())
        {
            Console.WriteLine("Failed to initialize vJoy!");
            Console.ReadKey();
            return;
        }
        Console.WriteLine("vJoy initialized.");

        // Get vJoy device info
        var vjoyInfo = vjoyService.GetDeviceInfo(vjoyId);
        if (!vjoyInfo.Exists)
        {
            Console.WriteLine($"vJoy device {vjoyId} does not exist!");
            Console.ReadKey();
            return;
        }
        Console.WriteLine($"vJoy device {vjoyId}: {vjoyInfo.ButtonCount} buttons, Status={vjoyInfo.Status}");

        // Acquire vJoy device
        if (!vjoyService.AcquireDevice(vjoyId))
        {
            Console.WriteLine($"Failed to acquire vJoy device {vjoyId}!");
            Console.ReadKey();
            return;
        }
        Console.WriteLine($"Acquired vJoy device {vjoyId}.\n");

        // Map axes (physical index to vJoy axis)
        var axisMap = new[] { HID_USAGES.X, HID_USAGES.Y, HID_USAGES.Z, HID_USAGES.RX, HID_USAGES.RY, HID_USAGES.RZ, HID_USAGES.SL0, HID_USAGES.SL1 };

        Console.WriteLine("Starting passthrough... Press any key to stop.\n");

        int lineStart = Console.CursorTop;

        inputService.InputReceived += (sender, state) =>
        {
            if (state.DeviceIndex != physicalIndex)
                return;

            // Pass axes
            for (int i = 0; i < Math.Min(state.Axes.Length, axisMap.Length); i++)
            {
                if (vjoyService.GetDeviceInfo(vjoyId).HasAxisX || i > 0) // Check exists
                    vjoyService.SetAxis(vjoyId, axisMap[i], state.Axes[i]);
            }

            // Pass buttons (1-indexed for vJoy)
            for (int i = 0; i < state.Buttons.Length; i++)
            {
                vjoyService.SetButton(vjoyId, i + 1, state.Buttons[i]);
            }

            // Display status (sticky line)
            Console.SetCursorPosition(0, lineStart);
            var axes = string.Join(" ", state.Axes.Take(4).Select((v, i) => $"{axisMap[i]}:{v:+0.00;-0.00}"));
            var btns = string.Join(",", state.Buttons.Select((p, i) => p ? (i + 1).ToString() : null).Where(b => b != null));
            Console.Write($"IN:  {axes} | Btn: {(btns.Length > 0 ? btns : "-"),-20}");
            Console.SetCursorPosition(0, lineStart + 1);
            Console.Write($"OUT: vJoy {vjoyId} receiving input...                    ");
        };

        inputService.StartPolling(100);

        Console.SetCursorPosition(0, lineStart + 3);
        Console.ReadKey(true);

        Console.WriteLine("\n\nStopping...");
        inputService.StopPolling();
        vjoyService.ResetDevice(vjoyId);
        vjoyService.ReleaseDevice(vjoyId);
        inputService.Dispose();
        vjoyService.Dispose();
        Console.WriteLine("Done.");
        Environment.Exit(0);
    }

    private static void RunHidHideDiag()
    {
        Console.WriteLine("=== Asteriq HidHide Diagnostics ===\n");

        var hidHide = new HidHideService();

        if (!hidHide.IsAvailable())
        {
            Console.WriteLine("ERROR: HidHide CLI not found at expected path.");
            Console.WriteLine("Please install HidHide from: https://github.com/nefarius/HidHide");
            Console.ReadKey();
            return;
        }

        Console.WriteLine("HidHide CLI: Available");
        Console.WriteLine($"Cloaking: {(hidHide.IsCloakingEnabled() ? "ENABLED" : "DISABLED")}\n");

        // List gaming devices
        Console.WriteLine("=== Gaming Devices ===");
        var devices = hidHide.GetGamingDevices();
        foreach (var group in devices)
        {
            Console.WriteLine($"\n{group.FriendlyName}");
            foreach (var dev in group.Devices)
            {
                var status = dev.IsGamingDevice ? "[Gaming]" : "[Other]";
                Console.WriteLine($"  {status} {dev.Usage}");
                Console.WriteLine($"    Path: {dev.DeviceInstancePath}");
            }
        }

        // List hidden devices
        Console.WriteLine("\n=== Hidden Devices ===");
        var hidden = hidHide.GetHiddenDevices();
        if (hidden.Count == 0)
        {
            Console.WriteLine("  (none)");
        }
        else
        {
            foreach (var path in hidden)
            {
                Console.WriteLine($"  {path}");
            }
        }

        // List whitelisted apps
        Console.WriteLine("\n=== Whitelisted Applications ===");
        var apps = hidHide.GetWhitelistedApps();
        if (apps.Count == 0)
        {
            Console.WriteLine("  (none)");
        }
        else
        {
            foreach (var app in apps)
            {
                Console.WriteLine($"  {app}");
            }
        }

        Console.WriteLine("\n(Press Enter to continue...)");
        Console.ReadLine();
    }

    private static void RunDeviceMatching()
    {
        Console.WriteLine("=== Asteriq Device Matching ===\n");

        // Initialize SDL
        var inputService = new InputService();
        if (!inputService.Initialize())
        {
            Console.WriteLine("ERROR: Failed to initialize SDL2");
            Console.ReadLine();
            return;
        }

        // Initialize HidHide
        var hidHide = new HidHideService();
        if (!hidHide.IsAvailable())
        {
            Console.WriteLine("ERROR: HidHide not available");
            inputService.Dispose();
            Console.ReadLine();
            return;
        }

        // Get SDL devices
        var sdlDevices = inputService.EnumerateDevices();
        Console.WriteLine($"Found {sdlDevices.Count} SDL device(s)\n");

        // Match devices
        var matcher = new DeviceMatchingService(hidHide);
        var correlations = matcher.GetAllCorrelations(sdlDevices);

        foreach (var corr in correlations)
        {
            var vidPid = corr.Vid > 0 ? $"VID_{corr.Vid:X4} PID_{corr.Pid:X4}" : "(no VID/PID)";
            var type = corr.IsVJoy ? "[vJoy]" : "[Physical]";

            Console.WriteLine($"{type} [{corr.SdlDevice.DeviceIndex}] {corr.SdlDevice.Name}");
            Console.WriteLine($"  SDL GUID: {corr.SdlDevice.InstanceGuid}");
            Console.WriteLine($"  {vidPid}");

            if (corr.HidDevices.Count > 0)
            {
                Console.WriteLine($"  HID Matches ({corr.HidDevices.Count}):");
                foreach (var hid in corr.HidDevices)
                {
                    var gaming = hid.IsGamingDevice ? "[Gaming]" : "[Other]";
                    Console.WriteLine($"    {gaming} {hid.DeviceInstancePath}");
                }
            }
            else if (!corr.IsVJoy)
            {
                Console.WriteLine("  HID Matches: NONE FOUND");
            }
            Console.WriteLine();
        }

        // Summary
        var physicalDevices = correlations.Where(c => !c.IsVJoy).ToList();
        var matchedDevices = physicalDevices.Where(c => c.HidDevices.Count > 0).ToList();

        Console.WriteLine("=== Summary ===");
        Console.WriteLine($"Physical devices: {physicalDevices.Count}");
        Console.WriteLine($"Matched to HID: {matchedDevices.Count}");

        if (matchedDevices.Count > 0)
        {
            Console.WriteLine("\nDevice paths to hide:");
            foreach (var dev in matchedDevices)
            {
                var primaryPath = dev.PrimaryDevicePath;
                if (primaryPath != null)
                {
                    Console.WriteLine($"  {primaryPath}");
                }
            }
        }

        inputService.Dispose();

        Console.WriteLine("\n(Press Enter to continue...)");
    }

    private static void RunWhitelist()
    {
        Console.WriteLine("=== Asteriq HidHide Whitelist ===\n");

        var hidHide = new HidHideService();

        if (!hidHide.IsAvailable())
        {
            Console.WriteLine("ERROR: HidHide not available");
            return;
        }

        // Get current exe path
        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath))
        {
            Console.WriteLine("ERROR: Could not determine executable path");
            return;
        }

        Console.WriteLine($"Executable: {exePath}");

        // Check if already whitelisted
        var whitelisted = hidHide.GetWhitelistedApps();
        bool alreadyWhitelisted = whitelisted.Any(w =>
            w.Equals(exePath, StringComparison.OrdinalIgnoreCase));

        if (alreadyWhitelisted)
        {
            Console.WriteLine("Status: Already whitelisted");
        }
        else
        {
            Console.WriteLine("Status: Not whitelisted, adding...");
            if (hidHide.WhitelistApp(exePath))
            {
                Console.WriteLine("Result: Successfully added to whitelist");
            }
            else
            {
                Console.WriteLine("Result: FAILED to add to whitelist (may need admin rights)");
            }
        }

        Console.WriteLine("\n(Press Enter to continue...)");
    }
}