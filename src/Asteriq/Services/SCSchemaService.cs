using System.Xml;
using Asteriq.Models;

namespace Asteriq.Services;

/// <summary>
/// Parses SC's defaultProfile.xml to extract action definitions and detect schema changes.
/// </summary>
public class SCSchemaService
{
    /// <summary>
    /// Device attribute names in defaultProfile.xml
    /// </summary>
    private static readonly string[] DeviceAttributes = { "keyboard", "mouse", "gamepad", "joystick" };

    /// <summary>
    /// Parses defaultProfile.xml and extracts all action definitions
    /// </summary>
    public List<SCAction> ParseActions(XmlDocument doc)
    {
        var actions = new List<SCAction>();

        if (doc.DocumentElement is null)
            return actions;

        var actionMaps = doc.DocumentElement.SelectNodes("//actionmap");
        if (actionMaps is null)
            return actions;

        foreach (XmlElement actionMap in actionMaps)
        {
            var mapName = actionMap.GetAttribute("name");
            if (string.IsNullOrEmpty(mapName))
                continue;

            var category = SCCategoryMapper.GetCategoryName(mapName);
            var actionNodes = actionMap.SelectNodes("action");
            if (actionNodes is null)
                continue;

            foreach (XmlElement actionNode in actionNodes)
            {
                var actionName = actionNode.GetAttribute("name");
                if (string.IsNullOrEmpty(actionName))
                    continue;

                var action = new SCAction
                {
                    ActionMap = mapName,
                    ActionName = actionName,
                    Category = category,
                    InputType = InferInputType(actionName),
                    DefaultBindings = ParseDefaultBindings(actionNode)
                };

                actions.Add(action);
            }
        }

        System.Diagnostics.Debug.WriteLine($"[SCSchemaService] Parsed {actions.Count} actions from defaultProfile.xml");
        return actions;
    }

    /// <summary>
    /// Parses default bindings from an action element
    /// </summary>
    private List<SCDefaultBinding> ParseDefaultBindings(XmlElement actionNode)
    {
        var bindings = new List<SCDefaultBinding>();

        // Parse bindings from attributes (keyboard="w", joystick="button1", etc.)
        foreach (var deviceAttr in DeviceAttributes)
        {
            var inputValue = actionNode.GetAttribute(deviceAttr)?.Trim();
            if (string.IsNullOrWhiteSpace(inputValue))
                continue;

            var prefix = GetDevicePrefix(deviceAttr);
            var (mainInput, modifiers) = ParseInputWithModifiers(inputValue);

            // Handle mouse inputs that SC sometimes puts in keyboard attribute
            if (deviceAttr == "keyboard" && IsMouseInput(mainInput))
            {
                prefix = "mo1";
            }

            bindings.Add(new SCDefaultBinding
            {
                DevicePrefix = prefix,
                Input = mainInput,
                Modifiers = modifiers
            });
        }

        // Parse child elements (<keyboard input="..."/>, <joystick input="..."/>, etc.)
        foreach (var deviceAttr in DeviceAttributes)
        {
            var deviceElements = actionNode.SelectNodes(deviceAttr);
            if (deviceElements is null)
                continue;

            foreach (XmlElement deviceElem in deviceElements)
            {
                var inputValue = deviceElem.GetAttribute("input")?.Trim();
                if (string.IsNullOrWhiteSpace(inputValue))
                    continue;

                var prefix = GetDevicePrefix(deviceAttr);
                var (mainInput, modifiers) = ParseInputWithModifiers(inputValue);

                // Handle mouse inputs that SC sometimes puts in keyboard element
                if (deviceAttr == "keyboard" && IsMouseInput(mainInput))
                {
                    prefix = "mo1";
                }

                bindings.Add(new SCDefaultBinding
                {
                    DevicePrefix = prefix,
                    Input = mainInput,
                    Inverted = deviceElem.GetAttribute("invert") == "1",
                    ActivationMode = SCActivationModeExtensions.ParseFromXml(deviceElem.GetAttribute("activationMode")),
                    Modifiers = modifiers
                });
            }
        }

        // Parse <rebind> elements (used in user profiles)
        var rebinds = actionNode.SelectNodes("rebind");
        if (rebinds is not null)
        {
            foreach (XmlElement rebind in rebinds)
            {
                var input = rebind.GetAttribute("input")?.Trim();
                if (string.IsNullOrWhiteSpace(input))
                    continue;

                var (prefix, inputName) = ParseFullInput(input);
                var (mainInput, modifiers) = ParseInputWithModifiers(inputName);

                bindings.Add(new SCDefaultBinding
                {
                    DevicePrefix = prefix,
                    Input = mainInput,
                    Inverted = rebind.GetAttribute("invert") == "1",
                    ActivationMode = SCActivationModeExtensions.ParseFromXml(rebind.GetAttribute("activationMode")),
                    Modifiers = modifiers
                });
            }
        }

        return bindings;
    }

    /// <summary>
    /// Parses a full input string (e.g., "js1_button5") into prefix and input name
    /// </summary>
    private static (string prefix, string inputName) ParseFullInput(string fullInput)
    {
        var underscoreIndex = fullInput.IndexOf('_');
        if (underscoreIndex <= 0)
            return ("", fullInput);

        return (fullInput[..underscoreIndex], fullInput[(underscoreIndex + 1)..]);
    }

    /// <summary>
    /// Parses an input value that may contain modifiers (e.g., "lshift+w")
    /// </summary>
    private static (string mainInput, List<string> modifiers) ParseInputWithModifiers(string inputValue)
    {
        var modifiers = new List<string>();

        if (!inputValue.Contains('+'))
            return (inputValue, modifiers);

        var parts = inputValue.Split('+');
        var mainParts = new List<string>();

        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
                continue;

            if (IsModifierKey(part))
            {
                modifiers.Add(part);
            }
            else
            {
                mainParts.Add(part);
            }
        }

        var mainInput = mainParts.Count > 0 ? string.Join("+", mainParts) : inputValue;
        return (mainInput, modifiers);
    }

    /// <summary>
    /// Converts device attribute name to SC device prefix
    /// </summary>
    private static string GetDevicePrefix(string deviceAttr)
    {
        return deviceAttr.ToLower() switch
        {
            "keyboard" => "kb1",
            "mouse" => "mo1",
            "joystick" => "js1",
            "gamepad" => "gp1",
            _ => "unk"
        };
    }

    /// <summary>
    /// Checks if an input is a mouse input
    /// </summary>
    private static bool IsMouseInput(string input)
    {
        var lower = input.ToLower();
        return lower.StartsWith("mouse") ||
               lower.StartsWith("maxis_") ||
               lower.StartsWith("mwheel");
    }

    /// <summary>
    /// Checks if a key is a modifier key
    /// </summary>
    private static bool IsModifierKey(string keyName)
    {
        var lower = keyName.ToLower();
        return lower is "lshift" or "rshift" or "lctrl" or "rctrl" or "lalt" or "ralt"
            or "shift" or "ctrl" or "alt";
    }

    /// <summary>
    /// Infers the input type from an action name
    /// </summary>
    private static SCInputType InferInputType(string actionName)
    {
        var lower = actionName.ToLower();

        // Axis-like actions
        if (lower.Contains("strafe") || lower.Contains("throttle") ||
            lower.Contains("pitch") || lower.Contains("yaw") || lower.Contains("roll") ||
            lower.Contains("_abs") || lower.Contains("_rel") ||
            lower.Contains("slider") || lower.Contains("zoom"))
        {
            return SCInputType.Axis;
        }

        // Hat/POV-like actions
        if (lower.Contains("hat") || lower.Contains("pov") || lower.Contains("dpad"))
        {
            return SCInputType.Hat;
        }

        return SCInputType.Button;
    }

    /// <summary>
    /// Compares two action lists and returns changes
    /// </summary>
    public SchemaChangeReport CompareSchemas(List<SCAction> oldActions, List<SCAction> newActions)
    {
        var report = new SchemaChangeReport();

        var oldKeys = new HashSet<string>(oldActions.Select(a => a.Key));
        var newKeys = new HashSet<string>(newActions.Select(a => a.Key));

        // Find added actions
        foreach (var action in newActions)
        {
            if (!oldKeys.Contains(action.Key))
            {
                report.AddedActions.Add(action);
            }
        }

        // Find removed actions
        foreach (var action in oldActions)
        {
            if (!newKeys.Contains(action.Key))
            {
                report.RemovedActions.Add(action);
            }
        }

        // Find potentially renamed actions (same actionmap, different name but similar)
        // This is a heuristic - we look for actions in the same map that were removed/added
        var removedByMap = report.RemovedActions.GroupBy(a => a.ActionMap).ToDictionary(g => g.Key, g => g.ToList());
        var addedByMap = report.AddedActions.GroupBy(a => a.ActionMap).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var mapName in removedByMap.Keys.Intersect(addedByMap.Keys))
        {
            var removed = removedByMap[mapName];
            var added = addedByMap[mapName];

            // Simple heuristic: if same number removed/added in a map, might be renames
            if (removed.Count == added.Count && removed.Count <= 3)
            {
                for (int i = 0; i < removed.Count; i++)
                {
                    report.PossibleRenames.Add((removed[i], added[i]));
                }
            }
        }

        return report;
    }

    /// <summary>
    /// Gets action maps grouped for UI display
    /// </summary>
    public Dictionary<string, List<SCAction>> GroupByActionMap(List<SCAction> actions)
    {
        return actions
            .GroupBy(a => a.ActionMap)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.ActionName).ToList());
    }

    /// <summary>
    /// Gets actions grouped by category for UI display
    /// </summary>
    public Dictionary<string, List<SCAction>> GroupByCategory(List<SCAction> actions)
    {
        return actions
            .GroupBy(a => a.Category)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.ActionName).ToList());
    }

    /// <summary>
    /// Filters actions to only joystick-relevant ones.
    /// Includes all actions since any button/axis action can be bound to a joystick.
    /// Previously filtered to only show actions with existing JS bindings, but this
    /// excluded actions like VTOL that users want to bind to their joystick.
    /// </summary>
    public List<SCAction> FilterJoystickActions(List<SCAction> actions)
    {
        // Include all actions - users should be able to bind any action to their joystick
        // The old filter excluded actions like v_vtol_toggle that have keyboard defaults
        // but no default joystick binding
        return actions.ToList();
    }
}

/// <summary>
/// Report of changes between two SC schema versions
/// </summary>
public class SchemaChangeReport
{
    /// <summary>
    /// Actions that were added in the new version
    /// </summary>
    public List<SCAction> AddedActions { get; set; } = new();

    /// <summary>
    /// Actions that were removed in the new version
    /// </summary>
    public List<SCAction> RemovedActions { get; set; } = new();

    /// <summary>
    /// Actions that may have been renamed (old, new)
    /// </summary>
    public List<(SCAction Old, SCAction New)> PossibleRenames { get; set; } = new();

    /// <summary>
    /// Whether there are any changes
    /// </summary>
    public bool HasChanges => AddedActions.Count > 0 || RemovedActions.Count > 0;

    /// <summary>
    /// Summary of changes
    /// </summary>
    public string Summary
    {
        get
        {
            if (!HasChanges)
                return "No changes detected";

            var parts = new List<string>();
            if (AddedActions.Count > 0)
                parts.Add($"{AddedActions.Count} added");
            if (RemovedActions.Count > 0)
                parts.Add($"{RemovedActions.Count} removed");
            if (PossibleRenames.Count > 0)
                parts.Add($"{PossibleRenames.Count} possibly renamed");

            return string.Join(", ", parts);
        }
    }
}
