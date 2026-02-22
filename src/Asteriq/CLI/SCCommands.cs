using Asteriq.Models;
using Asteriq.Services;

namespace Asteriq.CLI;

/// <summary>
/// CLI commands for Star Citizen integration (--scdetect, --scextract, --scschema, --scexport)
/// </summary>
internal static class SCCommands
{
    internal static void RunSCDetection()
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
            if (preferred is not null)
            {
                Console.WriteLine($"Preferred installation: {preferred.DisplayName}");
            }
        }

        // Check if SC is running
        Console.WriteLine($"\nStar Citizen running: {SCInstallationService.IsStarCitizenRunning()}");

        Console.WriteLine("\n(Press Enter to continue...)");
        Console.ReadLine();
    }

    internal static void RunSCExtraction(string[] args)
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
        SCInstallation? target;
        if (!string.IsNullOrEmpty(targetEnv))
        {
            target = scService.GetInstallation(targetEnv);
            if (target is null)
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
            if (target is null)
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

        if (profile is null)
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
        if (actionmaps is not null && actionmaps.Count > 0)
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

    internal static void RunSCSchema(string[] args)
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
        SCInstallation? target;
        if (!string.IsNullOrEmpty(targetEnv))
        {
            target = scService.GetInstallation(targetEnv);
            if (target is null)
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
            if (target is null)
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

        if (profile is null)
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
            var buttons = mapActions.Count(a => a.InputType == SCInputType.Button);
            var axes = mapActions.Count(a => a.InputType == SCInputType.Axis);
            var hats = mapActions.Count(a => a.InputType == SCInputType.Hat);

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

    internal static void RunSCExport(string[] args)
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
        SCInstallation? target;
        if (!string.IsNullOrEmpty(targetEnv))
        {
            target = scService.GetInstallation(targetEnv);
            if (target is null)
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
            if (target is null)
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

        var exportProfile = new SCExportProfile
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
            new SCActionBinding
            {
                ActionMap = "spaceship_movement",
                ActionName = "v_strafe_forward",
                VJoyDevice = 1,
                InputName = "y",
                InputType = SCInputType.Axis
            },
            new SCActionBinding
            {
                ActionMap = "spaceship_movement",
                ActionName = "v_strafe_lateral",
                VJoyDevice = 1,
                InputName = "x",
                InputType = SCInputType.Axis
            },
            new SCActionBinding
            {
                ActionMap = "spaceship_movement",
                ActionName = "v_strafe_vertical",
                VJoyDevice = 1,
                InputName = "z",
                InputType = SCInputType.Axis
            },
            // Flight rotation
            new SCActionBinding
            {
                ActionMap = "spaceship_movement",
                ActionName = "v_pitch",
                VJoyDevice = 2,
                InputName = "y",
                InputType = SCInputType.Axis,
                Inverted = true
            },
            new SCActionBinding
            {
                ActionMap = "spaceship_movement",
                ActionName = "v_yaw",
                VJoyDevice = 2,
                InputName = "x",
                InputType = SCInputType.Axis
            },
            new SCActionBinding
            {
                ActionMap = "spaceship_movement",
                ActionName = "v_roll",
                VJoyDevice = 2,
                InputName = "z",
                InputType = SCInputType.Axis
            },
            // Weapon buttons
            new SCActionBinding
            {
                ActionMap = "spaceship_weapons",
                ActionName = "v_attack1",
                VJoyDevice = 2,
                InputName = "button1",
                InputType = SCInputType.Button
            },
            new SCActionBinding
            {
                ActionMap = "spaceship_weapons",
                ActionName = "v_attack2",
                VJoyDevice = 2,
                InputName = "button2",
                InputType = SCInputType.Button
            },
            // Targeting
            new SCActionBinding
            {
                ActionMap = "spaceship_targeting",
                ActionName = "v_target_cycle_hostile_fwd",
                VJoyDevice = 1,
                InputName = "button5",
                InputType = SCInputType.Button,
                ActivationMode = SCActivationMode.Press
            },
            new SCActionBinding
            {
                ActionMap = "spaceship_targeting",
                ActionName = "v_target_cycle_hostile_back",
                VJoyDevice = 1,
                InputName = "button5",
                InputType = SCInputType.Button,
                ActivationMode = SCActivationMode.DoubleTap
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
