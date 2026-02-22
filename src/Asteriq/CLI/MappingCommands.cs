using Asteriq.Models;
using Asteriq.Services;
using Asteriq.VJoy;
using Microsoft.Extensions.Logging.Abstractions;

namespace Asteriq.CLI;

/// <summary>
/// CLI commands for mapping engine and keyboard output testing (--maptest, --keytest)
/// </summary>
internal static class MappingCommands
{
    internal static void RunMappingTest(string[] args)
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

        var vjoyService = new VJoyService(NullLogger<VJoyService>.Instance);
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
        if (sourceDevice is null)
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
                    var btns = string.Join(",", state.Buttons.Select((p, i) => p ? (i + 1).ToString() : null).Where(b => b is not null));
                    Console.Write($"IN:  {axes} | Btn: {(btns.Length > 0 ? btns : "-"),-20}");
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                // Console was resized - cursor position is now out of bounds
            }
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

    internal static void RunKeyboardTest(string[] args)
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
        if (sourceDevice is null)
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
            catch (ArgumentOutOfRangeException)
            {
                // Console was resized - cursor position is now out of bounds
            }
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
}
