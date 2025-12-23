namespace Asteriq.Services;

/// <summary>
/// Maps SC actionmap names to user-friendly category names and controls sorting.
/// Replicates the behavior from SCVirtStick's CategoryMapper.
/// </summary>
public static class SCCategoryMapper
{
    /// <summary>
    /// Action-level overrides for specific actions that should be in a different category
    /// than their actionmap would suggest (e.g., emergency actions)
    /// </summary>
    private static readonly Dictionary<string, (string Category, int SortOrder)> s_actionOverrides = new()
    {
        { "v_eject", ("Emergency", 100) },
        { "v_self_destruct", ("Emergency", 100) },
        { "v_eject_cinematic", ("Emergency", 100) },
    };

    /// <summary>
    /// Mapping from raw actionmap names to user-friendly category names.
    /// Based on actual SC defaultProfile.xml actionmap names.
    /// </summary>
    private static readonly Dictionary<string, (string Category, int SortOrder)> s_actionMapCategories = new()
    {
        // Flight Control
        { "spaceship_movement", ("Flight Control", 1) },
        { "spaceship_quantum", ("Flight Control", 1) },
        { "ifcs_controls", ("Flight Control", 1) },

        // Weapons
        { "spaceship_weapons", ("Weapons", 2) },

        // Targeting
        { "spaceship_targeting", ("Targeting", 3) },
        { "spaceship_targeting_advanced", ("Targeting", 3) },
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

        // Ship Systems
        { "spaceship_general", ("Ship Systems", 8) },
        { "spaceship_docking", ("Ship Systems", 8) },
        { "spaceship_auto_weapons", ("Ship Systems", 8) },
        { "lights_controller", ("Ship Systems", 8) },
        { "spaceship_headtracking", ("Ship Systems", 8) },
        { "seat_general", ("Ship Systems", 8) },

        // Ground Vehicles
        { "vehicle_general", ("Ground Vehicles", 9) },
        { "vehicle_driver", ("Ground Vehicles", 9) },
        { "vehicle_mfd", ("Ground Vehicles", 9) },
        { "vehicle_mobiglas", ("Ground Vehicles", 9) },

        // On Foot
        { "player", ("On Foot", 10) },
        { "player_choice", ("On Foot", 10) },
        { "player_emotes", ("On Foot", 10) },
        { "player_input_optical_tracking", ("On Foot", 10) },
        { "prone", ("On Foot", 10) },
        { "incapacitated", ("On Foot", 10) },

        // EVA
        { "eva", ("EVA", 11) },
        { "zero_gravity_eva", ("EVA", 11) },
        { "zero_gravity_traversal", ("EVA", 11) },

        // Turrets
        { "turret_main", ("Turrets", 12) },
        { "turret_movement", ("Turrets", 12) },
        { "turret_advanced", ("Turrets", 12) },
        { "manned_turret", ("Turrets", 12) },
        { "turret_remote", ("Turrets", 12) },

        // Mining
        { "mining", ("Mining", 13) },
        { "mining_turret", ("Mining", 13) },
        { "spaceship_mining", ("Mining", 13) },

        // Interface (includes HUD)
        { "spaceship_hud", ("Interface", 14) },
        { "ui_textfield", ("Interface", 14) },
        { "ui_notification", ("Interface", 14) },
        { "starmap", ("Interface", 14) },
        { "mobiglas", ("Interface", 14) },
        { "visor_menu", ("Interface", 14) },
        { "flycam", ("Interface", 14) },
        { "fixed_camera", ("Interface", 14) },
        { "selectable_camera", ("Interface", 14) },
        { "mapui", ("Interface", 14) },
        { "hacking", ("Interface", 14) },
        { "character_customizer", ("Interface", 14) },
        { "default", ("Interface", 14) },

        // Communication
        { "social", ("Communication", 15) },
        { "voip", ("Communication", 15) },
        { "foip", ("Communication", 15) },

        // Salvage
        { "salvage", ("Salvage", 16) },
        { "spaceship_salvage", ("Salvage", 16) },

        // Tractor Beam
        { "tractor_beam", ("Tractor Beam", 17) },

        // Server/Debug (usually hidden)
        { "server_operator", ("Other", 99) },
        { "server_renderer", ("Other", 99) },
        { "debug", ("Other", 99) },
        { "stopwatch", ("Other", 99) },
        { "remoterigidentitycontroller", ("Other", 99) },
    };

    /// <summary>
    /// Gets the user-friendly category name for an actionmap
    /// </summary>
    public static string GetCategoryName(string actionMap)
    {
        if (string.IsNullOrEmpty(actionMap))
            return "Other";

        var lowerMap = actionMap.ToLowerInvariant();

        if (s_actionMapCategories.TryGetValue(lowerMap, out var info))
            return info.Category;

        // Infer from name patterns if not in mapping
        return InferCategoryFromName(lowerMap);
    }

    /// <summary>
    /// Gets the user-friendly category name for a specific action.
    /// Checks action-level overrides first (e.g., Emergency for v_eject),
    /// then falls back to actionmap category.
    /// </summary>
    public static string GetCategoryNameForAction(string actionMap, string actionName)
    {
        // Check action-level overrides first
        if (!string.IsNullOrEmpty(actionName) && s_actionOverrides.TryGetValue(actionName, out var actionInfo))
            return actionInfo.Category;

        // Fall back to actionmap category
        return GetCategoryName(actionMap);
    }

    /// <summary>
    /// Gets the sort order for a specific action.
    /// Checks action-level overrides first, then falls back to actionmap/category.
    /// </summary>
    public static int GetSortOrderForAction(string actionMap, string actionName)
    {
        // Check action-level overrides first
        if (!string.IsNullOrEmpty(actionName) && s_actionOverrides.TryGetValue(actionName, out var actionInfo))
            return actionInfo.SortOrder;

        // Fall back to category sort order
        return GetCategorySortOrder(GetCategoryName(actionMap));
    }

    /// <summary>
    /// Gets the sort order for an actionmap (lower = earlier)
    /// </summary>
    public static int GetSortOrder(string actionMap)
    {
        if (string.IsNullOrEmpty(actionMap))
            return 99;

        var lowerMap = actionMap.ToLowerInvariant();

        if (s_actionMapCategories.TryGetValue(lowerMap, out var info))
            return info.SortOrder;

        return 99; // Unknown categories at end
    }

    /// <summary>
    /// Category display name to sort order mapping (matches SCVirtStick order)
    /// </summary>
    private static readonly Dictionary<string, int> s_categorySortOrders = new()
    {
        { "Flight Control", 1 },
        { "Weapons", 2 },
        { "Targeting", 3 },
        { "Missiles", 4 },
        { "Defensive", 5 },
        { "Power Management", 6 },
        { "View & Camera", 7 },
        { "Ship Systems", 8 },
        { "Ground Vehicles", 9 },
        { "On Foot", 10 },
        { "EVA", 11 },
        { "Turrets", 12 },
        { "Mining", 13 },
        { "Interface", 14 },
        { "Communication", 15 },
        { "Salvage", 16 },
        { "Tractor Beam", 17 },
        { "Server Admin", 18 },
        { "Other", 99 },
        { "Emergency", 100 }
    };

    /// <summary>
    /// Gets the sort order for a category NAME (not actionmap).
    /// Use this when sorting actions to ensure all actions with the same
    /// display category are grouped together regardless of raw actionmap.
    /// </summary>
    public static int GetCategorySortOrder(string categoryName)
    {
        if (string.IsNullOrEmpty(categoryName))
            return 99;

        return s_categorySortOrders.TryGetValue(categoryName, out var order) ? order : 99;
    }

    /// <summary>
    /// Gets all unique categories from actions in sorted order.
    /// This considers action-level overrides (e.g., Emergency for v_eject).
    /// </summary>
    public static IEnumerable<string> GetSortedCategoriesFromActions(IEnumerable<(string ActionMap, string ActionName)> actions)
    {
        return actions
            .Select(a => GetCategoryNameForAction(a.ActionMap, a.ActionName))
            .Distinct()
            .Select(c => new { Category = c, Order = GetCategorySortOrder(c) })
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Category)
            .Select(x => x.Category);
    }

    /// <summary>
    /// Gets all unique categories in sorted order (from action maps only, no action-level overrides)
    /// </summary>
    public static IEnumerable<string> GetSortedCategories(IEnumerable<string> actionMaps)
    {
        // Get unique category names first, then determine order
        // Use the minimum order value among all action maps that map to each category
        return actionMaps
            .Select(m => new { Category = GetCategoryName(m), Order = GetSortOrder(m) })
            .GroupBy(x => x.Category)
            .Select(g => new { Category = g.Key, Order = g.Min(x => x.Order) })
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
