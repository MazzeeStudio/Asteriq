using Asteriq.Models;
using Asteriq.Services;
using Asteriq.VJoy;
using Microsoft.Extensions.Logging.Abstractions;

namespace Asteriq.CLI;

/// <summary>
/// CLI commands for profile management (--profiles, --profile-save/load/delete/export/import/run)
/// </summary>
internal static class ProfileCommands
{
    private static ProfileRepository CreateRepository() =>
        new(NullLogger<ProfileRepository>.Instance);

    private static ApplicationSettingsService CreateAppSettings() =>
        new(NullLogger<ApplicationSettingsService>.Instance);

    internal static void RunProfileList()
    {
        Console.WriteLine("=== Asteriq Saved Profiles ===\n");

        var repository = CreateRepository();
        var appSettings = CreateAppSettings();
        var profiles = repository.ListProfiles();

        if (profiles.Count == 0)
        {
            Console.WriteLine("No profiles saved yet.");
            Console.WriteLine($"\nProfiles directory: {repository.ProfilesDirectory}");
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

        Console.WriteLine($"Profiles directory: {repository.ProfilesDirectory}");

        // Show last profile setting
        if (appSettings.LastProfileId.HasValue)
        {
            var lastProfile = profiles.FirstOrDefault(p => p.Id == appSettings.LastProfileId.Value);
            if (lastProfile is not null)
                Console.WriteLine($"Last used: {lastProfile.Name}");
        }

        Console.WriteLine("\n(Press Enter to continue...)");
    }

    internal static void RunProfileSave(string[] args)
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

        if (sourceDevice is null)
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
        var repository = CreateRepository();
        var appSettings = CreateAppSettings();
        repository.SaveProfile(profile);
        appSettings.LastProfileId = profile.Id;

        Console.WriteLine($"Profile created successfully!");
        Console.WriteLine($"  ID: {profile.Id}");
        Console.WriteLine($"  Name: {profile.Name}");
        Console.WriteLine($"  Mappings: {profile.AxisMappings.Count} axes, {profile.ButtonMappings.Count} buttons");
        Console.WriteLine($"\nRun with: Asteriq.exe --profile-run {profile.Id}");
        Console.WriteLine("\n(Press Enter to continue...)");
    }

    internal static void RunProfileLoad(string[] args)
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

        var repository = CreateRepository();
        var profile = repository.LoadProfile(profileId.Value);

        if (profile is null)
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

    internal static void RunProfileDelete(string[] args)
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

        var repository = CreateRepository();
        var appSettings = CreateAppSettings();
        var profile = repository.LoadProfile(profileId.Value);

        if (profile is null)
        {
            Console.WriteLine($"ERROR: Profile {profileId} not found.");
            return;
        }

        Console.WriteLine($"Deleting profile: {profile.Name}");

        if (repository.DeleteProfile(profileId.Value))
        {
            Console.WriteLine("Profile deleted successfully.");

            // Clear last profile if it was this one
            if (appSettings.LastProfileId == profileId)
                appSettings.LastProfileId = null;
        }
        else
        {
            Console.WriteLine("ERROR: Failed to delete profile.");
        }

        Console.WriteLine("\n(Press Enter to continue...)");
    }

    internal static void RunProfileExport(string[] args)
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

        var repository = CreateRepository();

        if (repository.ExportProfile(profileId.Value, exportPath))
        {
            Console.WriteLine($"Profile exported to: {exportPath}");
        }
        else
        {
            Console.WriteLine("ERROR: Failed to export profile.");
        }

        Console.WriteLine("\n(Press Enter to continue...)");
    }

    internal static void RunProfileImport(string[] args)
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

        var repository = CreateRepository();
        var profile = repository.ImportProfile(importPath);

        if (profile is not null)
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

    internal static void RunProfileExecute(string[] args)
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

        var repository = CreateRepository();
        var appSettings = CreateAppSettings();
        var profile = repository.LoadProfile(profileId.Value);

        if (profile is null)
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

        var vjoyService = new VJoyService(NullLogger<VJoyService>.Instance);
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
            if (device is not null)
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
        appSettings.LastProfileId = profile.Id;

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
            catch (ArgumentOutOfRangeException)
            {
                // Console was resized - cursor position is now out of bounds
            }
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
}
