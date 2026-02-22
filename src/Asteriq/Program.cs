using Asteriq.CLI;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
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
#if DEBUG
        if (args.Contains("--map-editor"))
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new UI.DeviceMapEditorForm());
            return;
        }
#endif

        if (args.Contains("--help") || args.Contains("-h") || args.Contains("/?"))
        {
            if (!AttachConsole(-1)) AllocConsole();
            ShowHelp();
            return;
        }

        if (args.Contains("--diag") || args.Contains("-d"))
        {
            if (!AttachConsole(-1)) AllocConsole();
            DeviceCommands.RunDiagnostics();
            return;
        }

        if (args.Contains("--passthrough") || args.Contains("-p"))
        {
            if (!AttachConsole(-1)) AllocConsole();
            DeviceCommands.RunPassthrough(args);
            return;
        }

        if (args.Contains("--hidhide"))
        {
            if (!AttachConsole(-1)) AllocConsole();
            DeviceCommands.RunHidHideDiag();
            return;
        }

        if (args.Contains("--match"))
        {
            if (!AttachConsole(-1)) AllocConsole();
            DeviceCommands.RunDeviceMatching();
            return;
        }

        if (args.Contains("--whitelist"))
        {
            if (!AttachConsole(-1)) AllocConsole();
            DeviceCommands.RunWhitelist();
            return;
        }

        if (args.Contains("--maptest"))
        {
            if (!AttachConsole(-1)) AllocConsole();
            MappingCommands.RunMappingTest(args);
            return;
        }

        if (args.Contains("--profiles"))
        {
            if (!AttachConsole(-1)) AllocConsole();
            ProfileCommands.RunProfileList();
            return;
        }

        if (args.Contains("--profile-save"))
        {
            if (!AttachConsole(-1)) AllocConsole();
            ProfileCommands.RunProfileSave(args);
            return;
        }

        if (args.Contains("--profile-load"))
        {
            if (!AttachConsole(-1)) AllocConsole();
            ProfileCommands.RunProfileLoad(args);
            return;
        }

        if (args.Contains("--profile-delete"))
        {
            if (!AttachConsole(-1)) AllocConsole();
            ProfileCommands.RunProfileDelete(args);
            return;
        }

        if (args.Contains("--profile-export"))
        {
            if (!AttachConsole(-1)) AllocConsole();
            ProfileCommands.RunProfileExport(args);
            return;
        }

        if (args.Contains("--profile-import"))
        {
            if (!AttachConsole(-1)) AllocConsole();
            ProfileCommands.RunProfileImport(args);
            return;
        }

        if (args.Contains("--profile-run"))
        {
            if (!AttachConsole(-1)) AllocConsole();
            ProfileCommands.RunProfileExecute(args);
            return;
        }

        if (args.Contains("--keytest"))
        {
            if (!AttachConsole(-1)) AllocConsole();
            MappingCommands.RunKeyboardTest(args);
            return;
        }

        if (args.Contains("--scdetect"))
        {
            if (!AttachConsole(-1)) AllocConsole();
            SCCommands.RunSCDetection();
            return;
        }

        if (args.Contains("--scextract"))
        {
            if (!AttachConsole(-1)) AllocConsole();
            SCCommands.RunSCExtraction(args);
            return;
        }

        if (args.Contains("--scschema"))
        {
            if (!AttachConsole(-1)) AllocConsole();
            SCCommands.RunSCSchema(args);
            return;
        }

        if (args.Contains("--scexport"))
        {
            if (!AttachConsole(-1)) AllocConsole();
            SCCommands.RunSCExport(args);
            return;
        }

        // Single-instance check - only allow one GUI instance
        using var singleInstance = new SingleInstanceManager();
        if (!singleInstance.IsFirstInstance)
        {
            // Another instance is running - activate it and exit
            singleInstance.ActivateExistingInstance();
            return;
        }

        ApplicationConfiguration.Initialize();

        // Build service provider with dependency injection
        var serviceProvider = BuildServiceProvider();

        // Migrate settings from old unified settings.json to new split files
        var migrationService = serviceProvider.GetRequiredService<Services.SettingsMigrationService>();
        migrationService.MigrateIfNeeded();

        // Check required drivers before creating MainForm
        bool forceDriverSetup = args.Contains("--driver-setup");
        if (!CheckRequiredDrivers(serviceProvider, forceDriverSetup))
        {
            // User cancelled driver setup - exit
            serviceProvider.Dispose();
            return;
        }

        // Create and run MainForm with injected services
        var mainForm = ActivatorUtilities.CreateInstance<UI.MainForm>(serviceProvider);
        Application.Run(mainForm);

        // Cleanup
        serviceProvider.Dispose();
    }

    /// <summary>
    /// Build the dependency injection service provider
    /// </summary>
    private static ServiceProvider BuildServiceProvider()
    {
        // Configure Serilog before building the service provider
        ConfigureSerilog();

        var services = new ServiceCollection();

        // Add logging with Serilog
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });

        // Register all application services
        services.AddAsteriqServices();

        // UI components (Transient - created per request)
        services.AddTransient<UI.MainForm>();

        // SystemTrayIcon factory - needs IApplicationSettingsService to determine icon type
        services.AddTransient<UI.SystemTrayIcon>(sp =>
        {
            var appSettings = sp.GetRequiredService<IApplicationSettingsService>();
            return new UI.SystemTrayIcon("Asteriq", appSettings.TrayIconType);
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Configure Serilog logging with console and file sinks
    /// </summary>
    private static void ConfigureSerilog()
    {
        var logsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Asteriq",
            "Logs");

        Directory.CreateDirectory(logsPath);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                Path.Combine(logsPath, "asteriq-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("Serilog configured - logging to console and {LogPath}", logsPath);
    }

    /// <summary>
    /// Check if required drivers are installed. Returns false if user cancels setup.
    /// Pass forceShow=true to always display the setup modal (useful for testing).
    /// </summary>
    private static bool CheckRequiredDrivers(IServiceProvider serviceProvider, bool forceShow = false)
    {
        var driverSetup = serviceProvider.GetRequiredService<Services.DriverSetupManager>();
        var status = driverSetup.GetSetupStatus();

        if (status.IsComplete && !forceShow)
        {
            return true; // All required drivers are installed
        }

        // Show driver setup form
        using var setupForm = ActivatorUtilities.CreateInstance<UI.DriverSetupForm>(serviceProvider);
        var result = setupForm.ShowDialog();

        if (result != DialogResult.OK || !setupForm.SetupComplete)
            return false; // User closed or cancelled

        if (setupForm.SkippedVJoy)
            return true; // User chose configuration-only mode without vJoy

        // Re-check status after installation
        status = driverSetup.GetSetupStatus();
        return status.IsComplete;
    }

    private static void ShowHelp()
    {
        Console.WriteLine(@"
Asteriq - Unified HOTAS Management

Usage: Asteriq.exe [command] [options]

Commands:
  (none)              Launch the GUI application

  --help, -h, /?      Show this help message

  --driver-setup      Force driver setup modal to appear on startup,
                      even if drivers are already detected. Useful for
                      testing the setup flow without modifying the registry.

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
}
