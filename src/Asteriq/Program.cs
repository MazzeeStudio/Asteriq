using Asteriq.Diagnostics;
using Asteriq.Services;
using System.Runtime.InteropServices;

namespace Asteriq;

static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--diag") || args.Contains("-d"))
        {
            // Try to attach to parent console, or create new one
            if (!AttachConsole(-1)) // -1 = parent process
                AllocConsole();

            RunDiagnostics();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
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

                // Move to device's line and overwrite
                Console.SetCursorPosition(0, line);
                Console.Write($"[{state.DeviceIndex}] {axes} | Btn: {(buttons.Length > 0 ? buttons : "-"),-20}");
            };

            inputService.StartPolling(100); // 100Hz

            Console.SetCursorPosition(0, endLine);
            Log("Polling... Press any key to stop.");
            Console.ReadKey(true);
            Log("");

            Log("Stopping...");
            inputService.StopPolling();
            inputService.Dispose();
            Log("Done.");
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            Log(ex.StackTrace ?? "No stack trace");
            MessageBox.Show($"Error: {ex.Message}\nSee log: {logPath}", "Error");
        }
    }
}