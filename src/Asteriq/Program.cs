using Asteriq.Diagnostics;
using Asteriq.Models;
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

        if (args.Contains("--maptest"))
        {
            if (!AttachConsole(-1))
                AllocConsole();
            RunMappingTest(args);
            return;
        }

        if (args.Contains("--profiles"))
        {
            if (!AttachConsole(-1))
                AllocConsole();
            RunProfileList();
            return;
        }

        if (args.Contains("--profile-save"))
        {
            if (!AttachConsole(-1))
                AllocConsole();
            RunProfileSave(args);
            return;
        }

        if (args.Contains("--profile-load"))
        {
            if (!AttachConsole(-1))
                AllocConsole();
            RunProfileLoad(args);
            return;
        }

        if (args.Contains("--profile-delete"))
        {
            if (!AttachConsole(-1))
                AllocConsole();
            RunProfileDelete(args);
            return;
        }

        if (args.Contains("--profile-export"))
        {
            if (!AttachConsole(-1))
                AllocConsole();
            RunProfileExport(args);
            return;
        }

        if (args.Contains("--profile-import"))
        {
            if (!AttachConsole(-1))
                AllocConsole();
            RunProfileImport(args);
            return;
        }

        if (args.Contains("--profile-run"))
        {
            if (!AttachConsole(-1))
                AllocConsole();
            RunProfileExecute(args);
            return;
        }

        if (args.Contains("--keytest"))
        {
            if (!AttachConsole(-1))
                AllocConsole();
            RunKeyboardTest(args);
            return;
        }

        if (args.Contains("--scdetect"))
        {
            if (!AttachConsole(-1))
                AllocConsole();
            RunSCDetection();
            return;
        }

        if (args.Contains("--scextract"))
        {
            if (!AttachConsole(-1))
                AllocConsole();
            RunSCExtraction(args);
            return;
        }

        if (args.Contains("--scschema"))
        {
            if (!AttachConsole(-1))
                AllocConsole();
            RunSCSchema(args);
            return;
        }

        if (args.Contains("--scexport"))
        {
            if (!AttachConsole(-1))
                AllocConsole();
            RunSCExport(args);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new UI.MainForm());
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

  --maptest <device> <vjoy>
                      Test mapping engine with curve applied
                      Passes device through with S-curve and deadzone.

Profile Management:
  --profiles          List all saved profiles

  --profile-save <name> <device> <vjoy>
                      Create and save a new passthrough profile
                      Example: Asteriq.exe --profile-save ""My Stick"" 1 1

  --profile-load <id> Show details of a specific profile

  --profile-delete <id>
                      Delete a profile by ID

  --profile-export <id> <path>
                      Export a profile to a JSON file

  --profile-import <path>
                      Import a profile from a JSON file

  --profile-run <id>  Run a saved profile (maps inputs to outputs)

  --keytest           Debug mode: shows all button presses from all devices
                      Use this first to find device/button numbers

  --keytest <device> <button> <key>
                      Test keyboard output from joystick button
                      Maps a button to a keyboard key
                      Example: Asteriq.exe --keytest 1 31 RCTRL
                      Key names: A-Z, 0-9, F1-F12, CTRL, RCTRL, SHIFT,
                                 ALT, SPACE, ENTER, ESC, TAB, etc.

Star Citizen Integration:
  --scdetect          Detect Star Citizen installations
                      Scans for SC versions (LIVE, PTU, EPTU) and displays
                      installation paths, BuildIds, and mappings directories.

  --scextract [env]   Extract defaultProfile.xml from Data.p4k
                      Extracts and caches the default profile from the specified
                      SC environment (LIVE, PTU, etc.). Uses preferred if not specified.
                      Example: Asteriq.exe --scextract LIVE

  --scschema [env]    Parse SC action schema from defaultProfile.xml
                      Shows all action maps and actions available for binding.
                      Use --scschema LIVE --filter spaceship to filter results.

  --scexport [env]    Test SC XML export functionality
                      Generates a sample export file showing the output format.

Examples:
  Asteriq.exe                     Launch GUI
  Asteriq.exe --diag              Test input devices
  Asteriq.exe --passthrough 1 1   Map device 1 to vJoy 1
  Asteriq.exe --hidhide           Show HidHide status
  Asteriq.exe --profiles          List saved profiles
  Asteriq.exe --profile-run <id>  Run a profile by ID
  Asteriq.exe --keytest 1 31 RCTRL  Map button 31 to Right-Ctrl
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
        Console.WriteLine($"Cloaking: {(hidHide.IsCloakingEnabled() ? "ENABLED" : "DISABLED")}");
        Console.WriteLine($"Inverse Mode: {(hidHide.IsInverseMode() ? "ENABLED (whitelist = BLOCK)" : "DISABLED (whitelist = ALLOW)")}\n");

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

    private static void RunMappingTest(string[] args)
    {
        Console.WriteLine("=== Asteriq Mapping Engine Test ===\n");

        // Parse arguments
        int physicalIndex = 1;
        uint vjoyId = 1;

        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--maptest")
            {
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out int pIdx))
                    physicalIndex = pIdx;
                if (i + 2 < args.Length && uint.TryParse(args[i + 2], out uint vId))
                    vjoyId = vId;
            }
        }

        // Initialize services
        var inputService = new InputService();
        if (!inputService.Initialize())
        {
            Console.WriteLine("ERROR: Failed to initialize SDL2");
            return;
        }

        var vjoyService = new VJoyService();
        if (!vjoyService.Initialize())
        {
            Console.WriteLine("ERROR: Failed to initialize vJoy");
            inputService.Dispose();
            return;
        }

        // Enumerate devices
        var devices = inputService.EnumerateDevices();
        Console.WriteLine($"Found {devices.Count} device(s):");
        foreach (var dev in devices)
        {
            string marker = dev.DeviceIndex == physicalIndex ? " <-- SOURCE" : "";
            Console.WriteLine($"  [{dev.DeviceIndex}] {dev.Name}{marker}");
        }

        var sourceDevice = devices.FirstOrDefault(d => d.DeviceIndex == physicalIndex);
        if (sourceDevice == null)
        {
            Console.WriteLine($"\nDevice index {physicalIndex} not found!");
            inputService.Dispose();
            vjoyService.Dispose();
            return;
        }

        // Create a test profile with curves
        var profile = new MappingProfile
        {
            Name = "Test Profile",
            Description = "Passthrough with S-curve"
        };

        // Map all axes with S-curve and 5% deadzone
        for (int i = 0; i < Math.Min(sourceDevice.AxisCount, 8); i++)
        {
            profile.AxisMappings.Add(new AxisMapping
            {
                Name = $"Axis {i}",
                Inputs = new List<InputSource>
                {
                    new InputSource
                    {
                        DeviceId = sourceDevice.InstanceGuid.ToString(),
                        DeviceName = sourceDevice.Name,
                        Type = InputType.Axis,
                        Index = i
                    }
                },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyAxis,
                    VJoyDevice = vjoyId,
                    Index = i
                },
                Curve = new AxisCurve
                {
                    Type = CurveType.SCurve,
                    Curvature = 0.3f,  // Moderate S-curve
                    Deadzone = 0.05f,  // 5% deadzone
                    Saturation = 1.0f
                }
            });
        }

        // Map all buttons (normal mode)
        for (int i = 0; i < sourceDevice.ButtonCount; i++)
        {
            profile.ButtonMappings.Add(new ButtonMapping
            {
                Name = $"Button {i + 1}",
                Inputs = new List<InputSource>
                {
                    new InputSource
                    {
                        DeviceId = sourceDevice.InstanceGuid.ToString(),
                        DeviceName = sourceDevice.Name,
                        Type = InputType.Button,
                        Index = i
                    }
                },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyButton,
                    VJoyDevice = vjoyId,
                    Index = i + 1  // vJoy buttons are 1-indexed
                },
                Mode = ButtonMode.Normal
            });
        }

        Console.WriteLine($"\nCreated profile with {profile.AxisMappings.Count} axis mappings and {profile.ButtonMappings.Count} button mappings");
        Console.WriteLine("Curve: S-Curve (0.3 curvature, 5% deadzone)\n");

        // Initialize mapping engine
        var mappingEngine = new MappingEngine(vjoyService);
        mappingEngine.LoadProfile(profile);

        if (!mappingEngine.Start())
        {
            Console.WriteLine("ERROR: Failed to start mapping engine");
            inputService.Dispose();
            vjoyService.Dispose();
            return;
        }

        Console.WriteLine($"Mapping {sourceDevice.Name} -> vJoy {vjoyId}");
        Console.WriteLine("Press any key to stop...\n");

        int lineStart = Console.CursorTop;

        // Process input
        inputService.InputReceived += (sender, state) =>
        {
            if (state.DeviceIndex != physicalIndex)
                return;

            // Feed to mapping engine
            mappingEngine.ProcessInput(state);

            // Display status
            try
            {
                if (lineStart >= 0 && lineStart < Console.BufferHeight)
                {
                    Console.SetCursorPosition(0, lineStart);
                    var axes = string.Join(" ", state.Axes.Take(4).Select((v, i) => $"A{i}:{v:+0.00;-0.00}"));
                    var btns = string.Join(",", state.Buttons.Select((p, i) => p ? (i + 1).ToString() : null).Where(b => b != null));
                    Console.Write($"IN:  {axes} | Btn: {(btns.Length > 0 ? btns : "-"),-20}");
                }
            }
            catch { }
        };

        inputService.StartPolling(500); // 500Hz for responsive mapping

        Console.ReadKey(true);

        Console.WriteLine("\n\nStopping...");
        inputService.StopPolling();
        mappingEngine.Stop();
        inputService.Dispose();
        vjoyService.Dispose();
        mappingEngine.Dispose();
        Console.WriteLine("Done.");
    }

    private static void RunProfileList()
    {
        Console.WriteLine("=== Asteriq Saved Profiles ===\n");

        var profileService = new ProfileService();
        var profiles = profileService.ListProfiles();

        if (profiles.Count == 0)
        {
            Console.WriteLine("No profiles saved yet.");
            Console.WriteLine($"\nProfiles directory: {profileService.ProfilesDirectory}");
            Console.WriteLine("\nUse --profile-save to create a profile.");
            return;
        }

        Console.WriteLine($"Found {profiles.Count} profile(s):\n");

        foreach (var profile in profiles)
        {
            Console.WriteLine($"  ID: {profile.Id}");
            Console.WriteLine($"  Name: {profile.Name}");
            if (!string.IsNullOrEmpty(profile.Description))
                Console.WriteLine($"  Description: {profile.Description}");
            Console.WriteLine($"  Mappings: {profile.AxisMappingCount} axes, {profile.ButtonMappingCount} buttons");
            Console.WriteLine($"  Modified: {profile.ModifiedAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine();
        }

        Console.WriteLine($"Profiles directory: {profileService.ProfilesDirectory}");

        // Show last profile setting
        if (profileService.LastProfileId.HasValue)
        {
            var lastProfile = profiles.FirstOrDefault(p => p.Id == profileService.LastProfileId.Value);
            if (lastProfile != null)
                Console.WriteLine($"Last used: {lastProfile.Name}");
        }

        Console.WriteLine("\n(Press Enter to continue...)");
    }

    private static void RunProfileSave(string[] args)
    {
        Console.WriteLine("=== Create Profile ===\n");

        // Parse arguments: --profile-save <name> <device> <vjoy>
        string? profileName = null;
        int physicalIndex = 1;
        uint vjoyId = 1;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--profile-save" && i + 1 < args.Length)
            {
                profileName = args[i + 1];
                if (i + 2 < args.Length && int.TryParse(args[i + 2], out int pIdx))
                    physicalIndex = pIdx;
                if (i + 3 < args.Length && uint.TryParse(args[i + 3], out uint vId))
                    vjoyId = vId;
            }
        }

        if (string.IsNullOrEmpty(profileName))
        {
            Console.WriteLine("ERROR: Profile name required.");
            Console.WriteLine("Usage: --profile-save <name> <device> <vjoy>");
            return;
        }

        // Initialize input service to get device info
        var inputService = new InputService();
        if (!inputService.Initialize())
        {
            Console.WriteLine("ERROR: Failed to initialize SDL2");
            return;
        }

        var devices = inputService.EnumerateDevices();
        var sourceDevice = devices.FirstOrDefault(d => d.DeviceIndex == physicalIndex);

        if (sourceDevice == null)
        {
            Console.WriteLine($"ERROR: Device index {physicalIndex} not found.");
            Console.WriteLine("Available devices:");
            foreach (var dev in devices)
                Console.WriteLine($"  [{dev.DeviceIndex}] {dev.Name}");
            inputService.Dispose();
            return;
        }

        // Create profile
        var profile = new MappingProfile
        {
            Name = profileName,
            Description = $"Passthrough: {sourceDevice.Name} -> vJoy {vjoyId}"
        };

        // Map all axes
        for (int i = 0; i < Math.Min(sourceDevice.AxisCount, 8); i++)
        {
            profile.AxisMappings.Add(new AxisMapping
            {
                Name = $"Axis {i}",
                Inputs = new List<InputSource>
                {
                    new InputSource
                    {
                        DeviceId = sourceDevice.InstanceGuid.ToString(),
                        DeviceName = sourceDevice.Name,
                        Type = InputType.Axis,
                        Index = i
                    }
                },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyAxis,
                    VJoyDevice = vjoyId,
                    Index = i
                }
            });
        }

        // Map all buttons
        for (int i = 0; i < sourceDevice.ButtonCount; i++)
        {
            profile.ButtonMappings.Add(new ButtonMapping
            {
                Name = $"Button {i + 1}",
                Inputs = new List<InputSource>
                {
                    new InputSource
                    {
                        DeviceId = sourceDevice.InstanceGuid.ToString(),
                        DeviceName = sourceDevice.Name,
                        Type = InputType.Button,
                        Index = i
                    }
                },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyButton,
                    VJoyDevice = vjoyId,
                    Index = i + 1
                }
            });
        }

        inputService.Dispose();

        // Save profile
        var profileService = new ProfileService();
        profileService.SaveProfile(profile);
        profileService.LastProfileId = profile.Id;

        Console.WriteLine($"Profile created successfully!");
        Console.WriteLine($"  ID: {profile.Id}");
        Console.WriteLine($"  Name: {profile.Name}");
        Console.WriteLine($"  Mappings: {profile.AxisMappings.Count} axes, {profile.ButtonMappings.Count} buttons");
        Console.WriteLine($"\nRun with: Asteriq.exe --profile-run {profile.Id}");
        Console.WriteLine("\n(Press Enter to continue...)");
    }

    private static void RunProfileLoad(string[] args)
    {
        Console.WriteLine("=== Profile Details ===\n");

        // Parse profile ID
        Guid? profileId = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--profile-load" && i + 1 < args.Length)
            {
                if (Guid.TryParse(args[i + 1], out Guid id))
                    profileId = id;
            }
        }

        if (!profileId.HasValue)
        {
            Console.WriteLine("ERROR: Profile ID required.");
            Console.WriteLine("Usage: --profile-load <id>");
            Console.WriteLine("\nUse --profiles to list available profiles.");
            return;
        }

        var profileService = new ProfileService();
        var profile = profileService.LoadProfile(profileId.Value);

        if (profile == null)
        {
            Console.WriteLine($"ERROR: Profile {profileId} not found.");
            return;
        }

        Console.WriteLine($"ID: {profile.Id}");
        Console.WriteLine($"Name: {profile.Name}");
        Console.WriteLine($"Description: {profile.Description}");
        Console.WriteLine($"Created: {profile.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"Modified: {profile.ModifiedAt:yyyy-MM-dd HH:mm:ss}");

        Console.WriteLine($"\nAxis Mappings ({profile.AxisMappings.Count}):");
        foreach (var mapping in profile.AxisMappings)
        {
            var input = mapping.Inputs.FirstOrDefault();
            Console.WriteLine($"  {mapping.Name}: {input?.DeviceName} Axis {input?.Index} -> {mapping.Output}");
            if (mapping.Curve.Type != CurveType.Linear || mapping.Curve.Deadzone > 0)
                Console.WriteLine($"    Curve: {mapping.Curve.Type}, Deadzone: {mapping.Curve.Deadzone:P0}");
        }

        Console.WriteLine($"\nButton Mappings ({profile.ButtonMappings.Count}):");
        foreach (var mapping in profile.ButtonMappings)
        {
            var input = mapping.Inputs.FirstOrDefault();
            Console.WriteLine($"  {mapping.Name}: {input?.DeviceName} Btn {input?.Index} -> {mapping.Output}");
            if (mapping.Mode != ButtonMode.Normal)
                Console.WriteLine($"    Mode: {mapping.Mode}");
        }

        Console.WriteLine("\n(Press Enter to continue...)");
    }

    private static void RunProfileDelete(string[] args)
    {
        Console.WriteLine("=== Delete Profile ===\n");

        Guid? profileId = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--profile-delete" && i + 1 < args.Length)
            {
                if (Guid.TryParse(args[i + 1], out Guid id))
                    profileId = id;
            }
        }

        if (!profileId.HasValue)
        {
            Console.WriteLine("ERROR: Profile ID required.");
            Console.WriteLine("Usage: --profile-delete <id>");
            return;
        }

        var profileService = new ProfileService();
        var profile = profileService.LoadProfile(profileId.Value);

        if (profile == null)
        {
            Console.WriteLine($"ERROR: Profile {profileId} not found.");
            return;
        }

        Console.WriteLine($"Deleting profile: {profile.Name}");

        if (profileService.DeleteProfile(profileId.Value))
        {
            Console.WriteLine("Profile deleted successfully.");

            // Clear last profile if it was this one
            if (profileService.LastProfileId == profileId)
                profileService.LastProfileId = null;
        }
        else
        {
            Console.WriteLine("ERROR: Failed to delete profile.");
        }

        Console.WriteLine("\n(Press Enter to continue...)");
    }

    private static void RunProfileExport(string[] args)
    {
        Console.WriteLine("=== Export Profile ===\n");

        Guid? profileId = null;
        string? exportPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--profile-export" && i + 1 < args.Length)
            {
                if (Guid.TryParse(args[i + 1], out Guid id))
                {
                    profileId = id;
                    if (i + 2 < args.Length)
                        exportPath = args[i + 2];
                }
            }
        }

        if (!profileId.HasValue || string.IsNullOrEmpty(exportPath))
        {
            Console.WriteLine("ERROR: Profile ID and export path required.");
            Console.WriteLine("Usage: --profile-export <id> <path>");
            return;
        }

        var profileService = new ProfileService();

        if (profileService.ExportProfile(profileId.Value, exportPath))
        {
            Console.WriteLine($"Profile exported to: {exportPath}");
        }
        else
        {
            Console.WriteLine("ERROR: Failed to export profile.");
        }

        Console.WriteLine("\n(Press Enter to continue...)");
    }

    private static void RunProfileImport(string[] args)
    {
        Console.WriteLine("=== Import Profile ===\n");

        string? importPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--profile-import" && i + 1 < args.Length)
            {
                importPath = args[i + 1];
            }
        }

        if (string.IsNullOrEmpty(importPath))
        {
            Console.WriteLine("ERROR: Import path required.");
            Console.WriteLine("Usage: --profile-import <path>");
            return;
        }

        if (!File.Exists(importPath))
        {
            Console.WriteLine($"ERROR: File not found: {importPath}");
            return;
        }

        var profileService = new ProfileService();
        var profile = profileService.ImportProfile(importPath);

        if (profile != null)
        {
            Console.WriteLine($"Profile imported successfully!");
            Console.WriteLine($"  ID: {profile.Id}");
            Console.WriteLine($"  Name: {profile.Name}");
            Console.WriteLine($"  Mappings: {profile.AxisMappings.Count} axes, {profile.ButtonMappings.Count} buttons");
        }
        else
        {
            Console.WriteLine("ERROR: Failed to import profile. Check file format.");
        }

        Console.WriteLine("\n(Press Enter to continue...)");
    }

    private static void RunProfileExecute(string[] args)
    {
        Console.WriteLine("=== Run Profile ===\n");

        Guid? profileId = null;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--profile-run" && i + 1 < args.Length)
            {
                if (Guid.TryParse(args[i + 1], out Guid id))
                    profileId = id;
            }
        }

        if (!profileId.HasValue)
        {
            Console.WriteLine("ERROR: Profile ID required.");
            Console.WriteLine("Usage: --profile-run <id>");
            return;
        }

        var profileService = new ProfileService();
        var profile = profileService.LoadProfile(profileId.Value);

        if (profile == null)
        {
            Console.WriteLine($"ERROR: Profile {profileId} not found.");
            return;
        }

        Console.WriteLine($"Loading profile: {profile.Name}");
        Console.WriteLine($"  {profile.AxisMappings.Count} axis mappings");
        Console.WriteLine($"  {profile.ButtonMappings.Count} button mappings\n");

        // Initialize services
        var inputService = new InputService();
        if (!inputService.Initialize())
        {
            Console.WriteLine("ERROR: Failed to initialize SDL2");
            return;
        }

        var vjoyService = new VJoyService();
        if (!vjoyService.Initialize())
        {
            Console.WriteLine("ERROR: Failed to initialize vJoy");
            inputService.Dispose();
            return;
        }

        // Get unique devices from profile
        var deviceIds = profile.AxisMappings
            .SelectMany(m => m.Inputs)
            .Concat(profile.ButtonMappings.SelectMany(m => m.Inputs))
            .Select(i => i.DeviceId)
            .Distinct()
            .ToList();

        Console.WriteLine($"Required devices: {deviceIds.Count}");

        // Enumerate devices
        var devices = inputService.EnumerateDevices();
        foreach (var deviceId in deviceIds)
        {
            var device = devices.FirstOrDefault(d => d.InstanceGuid.ToString() == deviceId);
            if (device != null)
                Console.WriteLine($"  [OK] {device.Name}");
            else
                Console.WriteLine($"  [MISSING] Device {deviceId}");
        }

        // Initialize mapping engine
        var mappingEngine = new MappingEngine(vjoyService);
        mappingEngine.LoadProfile(profile);

        if (!mappingEngine.Start())
        {
            Console.WriteLine("\nERROR: Failed to start mapping engine");
            inputService.Dispose();
            vjoyService.Dispose();
            return;
        }

        // Update last used profile
        profileService.LastProfileId = profile.Id;

        Console.WriteLine("\nProfile running. Press any key to stop...\n");

        int lineStart = Console.CursorTop;

        // Process input
        inputService.InputReceived += (sender, state) =>
        {
            mappingEngine.ProcessInput(state);

            // Display status
            try
            {
                if (lineStart >= 0 && lineStart < Console.BufferHeight)
                {
                    Console.SetCursorPosition(0, lineStart);
                    var axes = string.Join(" ", state.Axes.Take(4).Select((v, i) => $"A{i}:{v:+0.00;-0.00}"));
                    Console.Write($"[{state.DeviceName.Substring(0, Math.Min(15, state.DeviceName.Length)),-15}] {axes}   ");
                }
            }
            catch { }
        };

        inputService.StartPolling(500);

        Console.ReadKey(true);

        Console.WriteLine("\n\nStopping...");
        inputService.StopPolling();
        mappingEngine.Stop();
        inputService.Dispose();
        vjoyService.Dispose();
        mappingEngine.Dispose();
        Console.WriteLine("Done.");
    }

    private static void RunKeyboardTest(string[] args)
    {
        Console.WriteLine("=== Asteriq Keyboard Output Test ===\n");

        // Parse arguments: --keytest <device> <button> <key>
        int physicalIndex = -1; // -1 means "any device" (debug mode)
        int buttonIndex = -1;   // -1 means "any button" (debug mode)
        string keyName = "RCTRL";

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--keytest")
            {
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out int pIdx))
                    physicalIndex = pIdx;
                if (i + 2 < args.Length && int.TryParse(args[i + 2], out int bIdx))
                    buttonIndex = bIdx - 1; // Convert from 1-indexed to 0-indexed
                if (i + 3 < args.Length)
                    keyName = args[i + 3];
            }
        }

        // Resolve key code
        var keyCode = KeyboardService.GetKeyCode(keyName);
        if (!keyCode.HasValue)
        {
            Console.WriteLine($"ERROR: Unknown key name: {keyName}");
            Console.WriteLine("Valid keys: A-Z, 0-9, F1-F12, CTRL, RCTRL, LCTRL, SHIFT, RSHIFT,");
            Console.WriteLine("            ALT, RALT, SPACE, ENTER, ESC, TAB, etc.");
            Console.WriteLine("\n(Press Enter to continue...)");
            Console.ReadLine();
            return;
        }

        // Auto-configure HidHide so we can see physical devices
        var hidHide = new HidHideService();
        if (hidHide.IsAvailable())
        {
            bool isInverse = hidHide.IsInverseMode();
            Console.WriteLine($"HidHide: {(isInverse ? "Inverse mode" : "Normal mode")}");

            if (hidHide.EnsureSelfCanSeeDevices())
            {
                Console.WriteLine("HidHide: Configured to see physical devices");
            }
            else
            {
                Console.WriteLine("WARNING: Could not configure HidHide (may need admin rights)");
            }
        }

        // Initialize input service
        var inputService = new InputService();
        if (!inputService.Initialize())
        {
            Console.WriteLine("ERROR: Failed to initialize SDL2");
            Console.WriteLine("\n(Press Enter to continue...)");
            Console.ReadLine();
            return;
        }
        Console.WriteLine("SDL2 initialized.");

        // Enumerate devices
        var devices = inputService.EnumerateDevices();
        Console.WriteLine($"Found {devices.Count} device(s):");
        foreach (var dev in devices)
        {
            string marker = dev.DeviceIndex == physicalIndex ? " <-- SOURCE" : "";
            Console.WriteLine($"  [{dev.DeviceIndex}] {dev.Name} ({dev.ButtonCount} buttons){marker}");
        }

        // Debug mode: if no device specified, show all button activity
        bool debugMode = physicalIndex < 0 || buttonIndex < 0;

        if (debugMode)
        {
            Console.WriteLine("\n*** DEBUG MODE: Showing button press/release from all devices ***");
            Console.WriteLine("Press buttons on your joysticks to see which device/button they are.");
            Console.WriteLine("Then run again with: --keytest <device> <button> <key>");
            Console.WriteLine("\nPress any keyboard key to stop...\n");

            // Track previous button states per device to only show changes
            var previousStates = new Dictionary<int, bool[]>();

            inputService.InputReceived += (sender, state) =>
            {
                // Get or create previous state for this device
                if (!previousStates.TryGetValue(state.DeviceIndex, out var prevButtons))
                {
                    prevButtons = new bool[state.Buttons.Length];
                    previousStates[state.DeviceIndex] = prevButtons;
                }

                // Find buttons that changed state
                for (int i = 0; i < state.Buttons.Length && i < prevButtons.Length; i++)
                {
                    if (state.Buttons[i] != prevButtons[i])
                    {
                        string action = state.Buttons[i] ? "PRESSED" : "released";
                        Console.WriteLine($"[{state.DeviceIndex}] {state.DeviceName,-30} Button {i + 1}: {action}");
                        prevButtons[i] = state.Buttons[i];
                    }
                }

                // Handle array size changes
                if (state.Buttons.Length != prevButtons.Length)
                {
                    previousStates[state.DeviceIndex] = (bool[])state.Buttons.Clone();
                }
            };

            inputService.StartPolling(100); // 100Hz for debug
            Console.ReadKey(true);

            Console.WriteLine("\nStopping...");
            inputService.StopPolling();
            inputService.Dispose();
            Console.WriteLine("Done.");
            Console.WriteLine("\n(Press Enter to continue...)");
            Console.ReadLine();
            return;
        }

        // Normal mode: map specific button to key
        var sourceDevice = devices.FirstOrDefault(d => d.DeviceIndex == physicalIndex);
        if (sourceDevice == null)
        {
            Console.WriteLine($"\nERROR: Device index {physicalIndex} not found!");
            inputService.Dispose();
            Console.WriteLine("\n(Press Enter to continue...)");
            Console.ReadLine();
            return;
        }

        if (buttonIndex >= sourceDevice.ButtonCount)
        {
            Console.WriteLine($"\nERROR: Button {buttonIndex + 1} doesn't exist on {sourceDevice.Name} (has {sourceDevice.ButtonCount} buttons)");
            inputService.Dispose();
            Console.WriteLine("\n(Press Enter to continue...)");
            Console.ReadLine();
            return;
        }

        Console.WriteLine($"\nDevice index: {physicalIndex}");
        Console.WriteLine($"Button: {buttonIndex + 1} (0-indexed: {buttonIndex})");
        Console.WriteLine($"Key: {keyName} (VK: 0x{keyCode.Value:X2})");

        // Initialize keyboard service
        var keyboardService = new KeyboardService();
        Console.WriteLine("Keyboard service initialized.\n");

        Console.WriteLine($"Mapping: {sourceDevice.Name} Button {buttonIndex + 1} -> {KeyboardService.GetKeyName(keyCode.Value)}");
        Console.WriteLine("Press the joystick button to send keyboard input.");
        Console.WriteLine("Press any keyboard key in this window to stop...\n");

        int lineStart = Console.CursorTop;
        bool lastButtonState = false;
        int pressCount = 0;

        inputService.InputReceived += (sender, state) =>
        {
            if (state.DeviceIndex != physicalIndex)
                return;

            if (buttonIndex >= state.Buttons.Length)
                return;

            bool currentState = state.Buttons[buttonIndex];

            // Update keyboard output
            if (currentState != lastButtonState)
            {
                keyboardService.SetKey(keyCode.Value, currentState);

                if (currentState)
                    pressCount++;

                lastButtonState = currentState;
            }

            // Display status
            try
            {
                if (lineStart >= 0 && lineStart < Console.BufferHeight)
                {
                    Console.SetCursorPosition(0, lineStart);
                    string btnState = currentState ? "PRESSED" : "released";
                    string keyState = currentState ? $"{KeyboardService.GetKeyName(keyCode.Value)} DOWN" : $"{KeyboardService.GetKeyName(keyCode.Value)} up";
                    Console.Write($"Button {buttonIndex + 1}: {btnState,-10} -> {keyState,-15} | Press count: {pressCount}   ");
                }
            }
            catch { }
        };

        inputService.StartPolling(500); // 500Hz

        Console.ReadKey(true);

        Console.WriteLine("\n\nStopping...");
        inputService.StopPolling();
        keyboardService.ReleaseAll();
        keyboardService.Dispose();
        inputService.Dispose();
        Console.WriteLine("Done.");
        Console.WriteLine("\n(Press Enter to continue...)");
        Console.ReadLine();
    }

    private static void RunSCDetection()
    {
        Console.WriteLine("=== Asteriq Star Citizen Detection ===\n");

        var scService = new SCInstallationService();

        Console.WriteLine("Scanning for Star Citizen installations...\n");

        var installations = scService.Installations;

        if (installations.Count == 0)
        {
            Console.WriteLine("No Star Citizen installations found.");
            Console.WriteLine("\nSearched locations:");
            Console.WriteLine("  - Program Files\\Roberts Space Industries\\StarCitizen\\");
            Console.WriteLine("  - All fixed drives (root, Games, Program Files)");
            Console.WriteLine("\nIf SC is installed elsewhere, use the Settings tab to configure a custom path.");
        }
        else
        {
            Console.WriteLine($"Found {installations.Count} installation(s):\n");

            foreach (var inst in installations)
            {
                Console.WriteLine($"=== {inst.DisplayName} ===");
                Console.WriteLine($"  Environment: {inst.Environment}");
                Console.WriteLine($"  BuildId: {inst.BuildId ?? "(not found)"}");
                Console.WriteLine($"  Install Path: {inst.InstallPath}");
                Console.WriteLine($"  Data.p4k: {inst.DataP4kPath}");
                Console.WriteLine($"    Size: {inst.DataP4kSize / (1024.0 * 1024 * 1024):F2} GB");
                Console.WriteLine($"    Modified: {inst.DataP4kLastModified:yyyy-MM-dd HH:mm:ss} UTC");
                Console.WriteLine($"  Mappings Path: {inst.MappingsPath}");
                Console.WriteLine($"    Exists: {Directory.Exists(inst.MappingsPath)}");
                Console.WriteLine($"  ActionMaps Path: {inst.ActionMapsPath}");
                Console.WriteLine($"    Exists: {File.Exists(inst.ActionMapsPath)}");
                Console.WriteLine($"  Cache Key: {inst.GetCacheKey()}");
                Console.WriteLine($"  Valid: {inst.IsValid}");
                Console.WriteLine();
            }

            // Show preferred installation
            var preferred = scService.GetPreferredInstallation();
            if (preferred != null)
            {
                Console.WriteLine($"Preferred installation: {preferred.DisplayName}");
            }
        }

        // Check if SC is running
        Console.WriteLine($"\nStar Citizen running: {SCInstallationService.IsStarCitizenRunning()}");

        Console.WriteLine("\n(Press Enter to continue...)");
        Console.ReadLine();
    }

    private static void RunSCExtraction(string[] args)
    {
        Console.WriteLine("=== Asteriq SC Profile Extraction ===\n");

        // Parse optional environment argument
        string? targetEnv = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--scextract" && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
            {
                targetEnv = args[i + 1].ToUpperInvariant();
            }
        }

        var scService = new SCInstallationService();
        var cacheService = new SCProfileCacheService();

        Console.WriteLine("Scanning for Star Citizen installations...\n");

        var installations = scService.Installations;

        if (installations.Count == 0)
        {
            Console.WriteLine("No Star Citizen installations found.");
            Console.WriteLine("\n(Press Enter to continue...)");
            Console.ReadLine();
            return;
        }

        Console.WriteLine($"Found {installations.Count} installation(s):");
        foreach (var inst in installations)
        {
            Console.WriteLine($"  - {inst.DisplayName}");
        }

        // Select target installation
        Models.SCInstallation? target;
        if (!string.IsNullOrEmpty(targetEnv))
        {
            target = scService.GetInstallation(targetEnv);
            if (target == null)
            {
                Console.WriteLine($"\nERROR: Installation '{targetEnv}' not found.");
                Console.WriteLine("\n(Press Enter to continue...)");
                Console.ReadLine();
                return;
            }
        }
        else
        {
            target = scService.GetPreferredInstallation();
            if (target == null)
            {
                Console.WriteLine("\nERROR: No preferred installation found.");
                Console.WriteLine("\n(Press Enter to continue...)");
                Console.ReadLine();
                return;
            }
        }

        Console.WriteLine($"\nTarget: {target.DisplayName}");
        Console.WriteLine($"  Path: {target.InstallPath}");
        Console.WriteLine($"  Data.p4k: {target.DataP4kPath}");
        Console.WriteLine($"  Cache key: {target.GetCacheKey()}");

        // Check cache
        var cacheInfo = cacheService.GetCacheInfo();
        Console.WriteLine($"\nCache directory: {cacheInfo.CacheDirectory}");
        Console.WriteLine($"Cached profiles: {cacheInfo.CachedProfileCount} ({cacheInfo.FormattedSize})");

        if (cacheService.HasCachedProfile(target))
        {
            Console.WriteLine($"  -> {target.Environment} profile is already cached");
        }

        Console.WriteLine("\nExtracting defaultProfile.xml...");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var profile = cacheService.GetOrExtractProfile(target, (msg) =>
        {
            Console.WriteLine($"  {msg}");
        });

        stopwatch.Stop();

        if (profile == null)
        {
            Console.WriteLine("\nERROR: Failed to extract profile.");
            Console.WriteLine("\n(Press Enter to continue...)");
            Console.ReadLine();
            return;
        }

        Console.WriteLine($"\nExtraction completed in {stopwatch.ElapsedMilliseconds:N0}ms");
        Console.WriteLine($"Root element: {profile.DocumentElement?.Name}");

        // Count some basic info from the profile
        var actionmaps = profile.SelectNodes("//actionmap");
        var actions = profile.SelectNodes("//action");
        Console.WriteLine($"Action maps: {actionmaps?.Count ?? 0}");
        Console.WriteLine($"Actions: {actions?.Count ?? 0}");

        // Show a sample of action maps
        if (actionmaps != null && actionmaps.Count > 0)
        {
            Console.WriteLine("\nSample action maps:");
            int count = 0;
            foreach (System.Xml.XmlNode map in actionmaps)
            {
                var mapName = map.Attributes?["name"]?.Value ?? "unnamed";
                var actionCount = map.SelectNodes("action")?.Count ?? 0;
                Console.WriteLine($"  - {mapName} ({actionCount} actions)");
                if (++count >= 10) break;
            }
            if (actionmaps.Count > 10)
            {
                Console.WriteLine($"  ... and {actionmaps.Count - 10} more");
            }
        }

        // Update cache info
        cacheInfo = cacheService.GetCacheInfo();
        Console.WriteLine($"\nUpdated cache: {cacheInfo.CachedProfileCount} profiles ({cacheInfo.FormattedSize})");

        Console.WriteLine("\n(Press Enter to continue...)");
        Console.ReadLine();
    }

    private static void RunSCSchema(string[] args)
    {
        Console.WriteLine("=== Asteriq SC Schema Parser ===\n");

        // Parse optional environment and filter arguments
        string? targetEnv = null;
        string? filter = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--scschema" && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
            {
                targetEnv = args[i + 1].ToUpperInvariant();
            }
            if (args[i] == "--filter" && i + 1 < args.Length)
            {
                filter = args[i + 1].ToLowerInvariant();
            }
        }

        var scService = new SCInstallationService();
        var cacheService = new SCProfileCacheService();
        var schemaService = new SCSchemaService();

        Console.WriteLine("Scanning for Star Citizen installations...\n");

        var installations = scService.Installations;

        if (installations.Count == 0)
        {
            Console.WriteLine("No Star Citizen installations found.");
            Console.WriteLine("\n(Press Enter to continue...)");
            Console.ReadLine();
            return;
        }

        // Select target installation
        Models.SCInstallation? target;
        if (!string.IsNullOrEmpty(targetEnv))
        {
            target = scService.GetInstallation(targetEnv);
            if (target == null)
            {
                Console.WriteLine($"ERROR: Installation '{targetEnv}' not found.");
                Console.WriteLine("\n(Press Enter to continue...)");
                Console.ReadLine();
                return;
            }
        }
        else
        {
            target = scService.GetPreferredInstallation();
            if (target == null)
            {
                Console.WriteLine("ERROR: No preferred installation found.");
                Console.WriteLine("\n(Press Enter to continue...)");
                Console.ReadLine();
                return;
            }
        }

        Console.WriteLine($"Target: {target.DisplayName}");
        Console.WriteLine($"  BuildId: {target.BuildId}");

        // Get or extract profile
        Console.WriteLine("\nLoading defaultProfile.xml...");
        var profile = cacheService.GetOrExtractProfile(target);

        if (profile == null)
        {
            Console.WriteLine("ERROR: Failed to load profile.");
            Console.WriteLine("\n(Press Enter to continue...)");
            Console.ReadLine();
            return;
        }

        // Parse actions
        Console.WriteLine("Parsing action schema...\n");
        var actions = schemaService.ParseActions(profile);

        Console.WriteLine($"Total actions: {actions.Count}");

        // Apply filter if specified
        if (!string.IsNullOrEmpty(filter))
        {
            actions = actions.Where(a =>
                a.ActionMap.ToLower().Contains(filter) ||
                a.ActionName.ToLower().Contains(filter) ||
                a.Category.ToLower().Contains(filter)).ToList();
            Console.WriteLine($"Filtered to: {actions.Count} actions (filter: {filter})");
        }

        // Group by action map
        var byMap = schemaService.GroupByActionMap(actions);

        Console.WriteLine($"\n=== Action Maps ({byMap.Count}) ===\n");

        foreach (var kvp in byMap)
        {
            var mapName = kvp.Key;
            var mapActions = kvp.Value;

            // Count by type
            var buttons = mapActions.Count(a => a.InputType == Models.SCInputType.Button);
            var axes = mapActions.Count(a => a.InputType == Models.SCInputType.Axis);
            var hats = mapActions.Count(a => a.InputType == Models.SCInputType.Hat);

            Console.WriteLine($"{mapName}:");
            Console.WriteLine($"  Actions: {mapActions.Count} ({buttons} buttons, {axes} axes, {hats} hats)");

            // Show sample actions
            foreach (var action in mapActions.Take(5))
            {
                var typeStr = action.InputType.ToString().ToLower();
                var defaultStr = action.DefaultBindings.Count > 0
                    ? $" [{string.Join(", ", action.DefaultBindings.Take(2).Select(b => b.FullInput))}]"
                    : "";
                Console.WriteLine($"    - {action.ActionName} ({typeStr}){defaultStr}");
            }

            if (mapActions.Count > 5)
            {
                Console.WriteLine($"    ... and {mapActions.Count - 5} more");
            }
            Console.WriteLine();
        }

        // Filter to joystick actions
        var joystickActions = schemaService.FilterJoystickActions(actions);
        Console.WriteLine($"Joystick-relevant actions: {joystickActions.Count}");

        Console.WriteLine("\n(Press Enter to continue...)");
        Console.ReadLine();
    }

    private static void RunSCExport(string[] args)
    {
        Console.WriteLine("=== Asteriq SC Export Test ===\n");

        // Parse optional environment argument
        string? targetEnv = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--scexport" && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
            {
                targetEnv = args[i + 1].ToUpperInvariant();
            }
        }

        var scService = new SCInstallationService();
        var cacheService = new SCProfileCacheService();
        var schemaService = new SCSchemaService();
        var exportService = new SCXmlExportService();

        Console.WriteLine("Scanning for Star Citizen installations...\n");

        var installations = scService.Installations;

        if (installations.Count == 0)
        {
            Console.WriteLine("No Star Citizen installations found.");
            Console.WriteLine("\n(Press Enter to continue...)");
            Console.ReadLine();
            return;
        }

        // Select target installation
        Models.SCInstallation? target;
        if (!string.IsNullOrEmpty(targetEnv))
        {
            target = scService.GetInstallation(targetEnv);
            if (target == null)
            {
                Console.WriteLine($"ERROR: Installation '{targetEnv}' not found.");
                Console.WriteLine("\n(Press Enter to continue...)");
                Console.ReadLine();
                return;
            }
        }
        else
        {
            target = scService.GetPreferredInstallation();
            if (target == null)
            {
                Console.WriteLine("ERROR: No preferred installation found.");
                Console.WriteLine("\n(Press Enter to continue...)");
                Console.ReadLine();
                return;
            }
        }

        Console.WriteLine($"Target: {target.DisplayName}");
        Console.WriteLine($"  BuildId: {target.BuildId}");

        // Create a test export profile
        Console.WriteLine("\nCreating test export profile...\n");

        var exportProfile = new Models.SCExportProfile
        {
            ProfileName = "asteriq_test",
            TargetEnvironment = target.Environment,
            TargetBuildId = target.BuildId
        };

        // Map vJoy 1 -> js1, vJoy 2 -> js2
        exportProfile.SetSCInstance(1, 1);
        exportProfile.SetSCInstance(2, 2);

        // Add some sample bindings
        var testBindings = new[]
        {
            // Flight movement axes
            new Models.SCActionBinding
            {
                ActionMap = "spaceship_movement",
                ActionName = "v_strafe_forward",
                VJoyDevice = 1,
                InputName = "y",
                InputType = Models.SCInputType.Axis
            },
            new Models.SCActionBinding
            {
                ActionMap = "spaceship_movement",
                ActionName = "v_strafe_lateral",
                VJoyDevice = 1,
                InputName = "x",
                InputType = Models.SCInputType.Axis
            },
            new Models.SCActionBinding
            {
                ActionMap = "spaceship_movement",
                ActionName = "v_strafe_vertical",
                VJoyDevice = 1,
                InputName = "z",
                InputType = Models.SCInputType.Axis
            },
            // Flight rotation
            new Models.SCActionBinding
            {
                ActionMap = "spaceship_movement",
                ActionName = "v_pitch",
                VJoyDevice = 2,
                InputName = "y",
                InputType = Models.SCInputType.Axis,
                Inverted = true
            },
            new Models.SCActionBinding
            {
                ActionMap = "spaceship_movement",
                ActionName = "v_yaw",
                VJoyDevice = 2,
                InputName = "x",
                InputType = Models.SCInputType.Axis
            },
            new Models.SCActionBinding
            {
                ActionMap = "spaceship_movement",
                ActionName = "v_roll",
                VJoyDevice = 2,
                InputName = "z",
                InputType = Models.SCInputType.Axis
            },
            // Weapon buttons
            new Models.SCActionBinding
            {
                ActionMap = "spaceship_weapons",
                ActionName = "v_attack1",
                VJoyDevice = 2,
                InputName = "button1",
                InputType = Models.SCInputType.Button
            },
            new Models.SCActionBinding
            {
                ActionMap = "spaceship_weapons",
                ActionName = "v_attack2",
                VJoyDevice = 2,
                InputName = "button2",
                InputType = Models.SCInputType.Button
            },
            // Targeting
            new Models.SCActionBinding
            {
                ActionMap = "spaceship_targeting",
                ActionName = "v_target_cycle_hostile_fwd",
                VJoyDevice = 1,
                InputName = "button5",
                InputType = Models.SCInputType.Button,
                ActivationMode = Models.SCActivationMode.Press
            },
            new Models.SCActionBinding
            {
                ActionMap = "spaceship_targeting",
                ActionName = "v_target_cycle_hostile_back",
                VJoyDevice = 1,
                InputName = "button5",
                InputType = Models.SCInputType.Button,
                ActivationMode = Models.SCActivationMode.DoubleTap
            }
        };

        foreach (var binding in testBindings)
        {
            exportProfile.Bindings.Add(binding);
        }

        Console.WriteLine($"Profile: {exportProfile.ProfileName}");
        Console.WriteLine($"vJoy mappings: {string.Join(", ", exportProfile.VJoyToSCInstance.Select(kv => $"vJoy{kv.Key}=js{kv.Value}"))}");
        Console.WriteLine($"Bindings: {exportProfile.Bindings.Count}");

        // Validate
        Console.WriteLine("\nValidating profile...");
        var validation = exportService.Validate(exportProfile);
        Console.WriteLine($"Valid: {validation.IsValid}");
        if (validation.Errors.Count > 0)
        {
            Console.WriteLine("Errors:");
            foreach (var error in validation.Errors)
                Console.WriteLine($"  - {error}");
        }
        if (validation.Warnings.Count > 0)
        {
            Console.WriteLine("Warnings:");
            foreach (var warning in validation.Warnings)
                Console.WriteLine($"  - {warning}");
        }

        // Generate export
        Console.WriteLine("\nGenerating XML...\n");
        var doc = exportService.Export(exportProfile);

        // Display the XML
        Console.WriteLine("=== Generated XML ===\n");
        using (var sw = new System.IO.StringWriter())
        {
            var settings = new System.Xml.XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = true
            };
            using (var xw = System.Xml.XmlWriter.Create(sw, settings))
            {
                doc.WriteTo(xw);
            }
            Console.WriteLine(sw.ToString());
        }

        // Show export path
        var exportPath = exportService.GetExportPath(exportProfile, target);
        Console.WriteLine($"\n=== Export Info ===");
        Console.WriteLine($"Filename: {exportProfile.GetExportFileName()}");
        Console.WriteLine($"Export path: {exportPath}");

        Console.WriteLine("\n(Press Enter to continue...)");
        Console.ReadLine();
    }
}