namespace Asteriq.Services;

/// <summary>
/// Maps SC actionmap names to user-friendly category names and controls sorting.
/// Replicates the behavior from SCVirtStick's CategoryMapper.
/// </summary>
public static class SCCategoryMapper
{
    /// <summary>
    /// Mapping from raw actionmap names to user-friendly category names
    /// </summary>
    private static readonly Dictionary<string, (string Category, int SortOrder)> ActionMapCategories = new()
    {
        // Flight Control
        { "spaceship_movement", ("Flight Control", 1) },
        { "spaceship_quantum", ("Flight Control", 1) },

        // Weapons
        { "spaceship_weapons", ("Weapons", 2) },

        // Targeting
        { "spaceship_targeting", ("Targeting", 3) },
        { "spaceship_target_hailing", ("Targeting", 3) },
        { "spaceship_scanning", ("Targeting", 3) },
        { "spaceship_ping", ("Targeting", 3) },
        { "spaceship_radar", ("Targeting", 3) },

        // Missiles
        { "spaceship_missiles", ("Missiles", 4) },

        // Defensive
        { "spaceship_defensive", ("Defensive", 5) },
        { "spaceship_shields", ("Defensive", 5) },

        // Power Management
        { "spaceship_power", ("Power Management", 6) },

        // View & Camera
        { "spaceship_view", ("View & Camera", 7) },
        { "view_director_mode", ("View & Camera", 7) },
        { "spectator", ("View & Camera", 7) },
        { "default", ("View & Camera", 7) },

        // Ship Systems
        { "spaceship_general", ("Ship Systems", 8) },
        { "spaceship_hud", ("Ship Systems", 8) },
        { "spaceship_docking", ("Ship Systems", 8) },
        { "spaceship_auto_weapons", ("Ship Systems", 8) },
        { "lights_controller", ("Ship Systems", 8) },
        { "spaceship_headtracking", ("Ship Systems", 8) },

        // Ground Vehicles
        { "vehicle_general", ("Ground Vehicles", 9) },
        { "vehicle_driver", ("Ground Vehicles", 9) },

        // On Foot
        { "player", ("On Foot", 10) },
        { "player_choice", ("On Foot", 10) },
        { "player_emotes", ("On Foot", 10) },
        { "player_input_optical_tracking", ("On Foot", 10) },
        { "zero_gravity_eva", ("On Foot", 10) },
        { "prone", ("On Foot", 10) },

        // EVA
        { "eva", ("EVA", 11) },

        // Turrets
        { "turret_main", ("Turrets", 12) },
        { "turret_movement", ("Turrets", 12) },
        { "turret_advanced", ("Turrets", 12) },
        { "manned_turret", ("Turrets", 12) },
        { "turret_remote", ("Turrets", 12) },

        // Mining
        { "mining", ("Mining", 13) },
        { "mining_turret", ("Mining", 13) },

        // Salvage
        { "salvage", ("Salvage", 14) },

        // Tractor Beam
        { "tractor_beam", ("Tractor Beam", 15) },

        // Interface
        { "ui_textfield", ("Interface", 16) },
        { "ui_notification", ("Interface", 16) },
        { "starmap", ("Interface", 16) },
        { "mobiglas", ("Interface", 16) },
        { "visor_menu", ("Interface", 16) },
        { "flycam", ("Interface", 16) },
        { "fixed_camera", ("Interface", 16) },
        { "selectable_camera", ("Interface", 16) },

        // Communication
        { "social", ("Communication", 17) },
        { "voip", ("Communication", 17) },
        { "foip", ("Communication", 17) },

        // Server Operator
        { "server_operator", ("Server Admin", 18) },
    };

    /// <summary>
    /// Gets the user-friendly category name for an actionmap
    /// </summary>
    public static string GetCategoryName(string actionMap)
    {
        if (string.IsNullOrEmpty(actionMap))
            return "Other";

        var lowerMap = actionMap.ToLowerInvariant();

        if (ActionMapCategories.TryGetValue(lowerMap, out var info))
            return info.Category;

        // Infer from name patterns if not in mapping
        return InferCategoryFromName(lowerMap);
    }

    /// <summary>
    /// Gets the sort order for an actionmap (lower = earlier)
    /// </summary>
    public static int GetSortOrder(string actionMap)
    {
        if (string.IsNullOrEmpty(actionMap))
            return 99;

        var lowerMap = actionMap.ToLowerInvariant();

        if (ActionMapCategories.TryGetValue(lowerMap, out var info))
            return info.SortOrder;

        return 99; // Unknown categories at end
    }

    /// <summary>
    /// Gets all unique categories in sorted order
    /// </summary>
    public static IEnumerable<string> GetSortedCategories(IEnumerable<string> actionMaps)
    {
        return actionMaps
            .Select(m => (Category: GetCategoryName(m), Order: GetSortOrder(m)))
            .Distinct()
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Category)
            .Select(x => x.Category);
    }

    /// <summary>
    /// Infers category from action map name patterns
    /// </summary>
    private static string InferCategoryFromName(string actionMap)
    {
        if (actionMap.Contains("spaceship") || actionMap.Contains("ship"))
        {
            if (actionMap.Contains("weapon")) return "Weapons";
            if (actionMap.Contains("target")) return "Targeting";
            if (actionMap.Contains("missile")) return "Missiles";
            if (actionMap.Contains("defensive") || actionMap.Contains("shield")) return "Defensive";
            if (actionMap.Contains("power")) return "Power Management";
            if (actionMap.Contains("view") || actionMap.Contains("camera")) return "View & Camera";
            return "Flight Control";
        }

        if (actionMap.Contains("vehicle")) return "Ground Vehicles";
        if (actionMap.Contains("player") || actionMap.Contains("character")) return "On Foot";
        if (actionMap.Contains("eva")) return "EVA";
        if (actionMap.Contains("turret")) return "Turrets";
        if (actionMap.Contains("ui") || actionMap.Contains("menu") || actionMap.Contains("hud")) return "Interface";
        if (actionMap.Contains("social") || actionMap.Contains("chat") || actionMap.Contains("voice")) return "Communication";
        if (actionMap.Contains("mining")) return "Mining";
        if (actionMap.Contains("salvage")) return "Salvage";
        if (actionMap.Contains("tractor")) return "Tractor Beam";

        return "Other";
    }

    /// <summary>
    /// Formats an action name for display (removes prefixes, adds spaces)
    /// </summary>
    public static string FormatActionName(string actionName)
    {
        if (string.IsNullOrEmpty(actionName))
            return actionName;

        // Remove common prefixes
        var display = actionName;
        if (display.StartsWith("v_"))
            display = display.Substring(2);
        if (display.StartsWith("foip_"))
            display = display.Substring(5);
        if (display.StartsWith("ui_"))
            display = display.Substring(3);

        // Replace underscores with spaces and title case
        display = string.Join(" ", display
            .Split('_')
            .Select(word => word.Length > 0
                ? char.ToUpper(word[0]) + word.Substring(1).ToLower()
                : word));

        return display;
    }
}
