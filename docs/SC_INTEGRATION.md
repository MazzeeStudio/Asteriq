# Star Citizen Integration Design

## Overview

This layer handles reading Star Citizen binding data and visualizing it. **Key principle**: Read-only access to SC data; we generate export files but never modify user's actionmaps.xml directly.

## Data Sources

### 1. P4K Archive (Game Defaults)

Location: `StarCitizen\LIVE\Data.p4k` (or PTU/EPTU)

Contains default binding profiles in:
```
Data/Libs/Config/Defaultprofiles/default/actionmaps.xml
```

```csharp
public class P4KReader
{
    // P4K is a zip archive
    public string ReadFile(string p4kPath, string internalPath)
    {
        using var archive = ZipFile.OpenRead(p4kPath);
        var entry = archive.GetEntry(internalPath);
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
```

### 2. User Actionmaps (Custom Bindings)

Location: `%APPDATA%\StarCitizen\{environment}\USER\Client\0\Profiles\default\actionmaps.xml`

Environments: `LIVE`, `PTU`, `EPTU`

## Binding Data Model

```csharp
public class ActionMap
{
    public string Name { get; }              // e.g., "spaceship_movement"
    public List<Action> Actions { get; }
}

public class Action
{
    public string Name { get; }              // e.g., "v_strafe_forward"
    public string Category { get; }          // e.g., "Flight Control"
    public List<Binding> Bindings { get; }
}

public class Binding
{
    public string DeviceType { get; }        // "joystick", "keyboard", "mouse"
    public int DeviceInstance { get; }       // js1, js2, etc.
    public string Input { get; }             // "button1", "x", "hat1_up"
    public List<string> Modifiers { get; }   // Modifier keys/buttons
    public ActivationMode ActivationMode { get; }
}

public enum ActivationMode
{
    Press,
    Hold,
    Toggle,
    DoublePress
}
```

## XML Parsing

Actionmaps XML structure:
```xml
<ActionMaps>
  <actionmap name="spaceship_movement">
    <action name="v_strafe_forward">
      <rebind input="js1_button1" activationMode="press"/>
      <rebind input="kb1_w"/>
    </action>
  </actionmap>
</ActionMaps>
```

```csharp
public class ActionMapsParser
{
    public List<ActionMap> Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        return doc.Descendants("actionmap")
            .Select(ParseActionMap)
            .ToList();
    }

    private ActionMap ParseActionMap(XElement el)
    {
        return new ActionMap
        {
            Name = el.Attribute("name")?.Value,
            Actions = el.Elements("action").Select(ParseAction).ToList()
        };
    }

    private Binding ParseBinding(string input)
    {
        // Parse "js1_button1" → DeviceType=joystick, Instance=1, Input=button1
        // Parse "kb1_w" → DeviceType=keyboard, Instance=1, Input=w
        var match = Regex.Match(input, @"^(js|kb|mo)(\d+)_(.+)$");
        // ...
    }
}
```

## Device Instance Mapping

SC uses `js1`, `js2`, etc. based on Windows device enumeration order. This is problematic because order can change.

**Our solution**: Map physical VID:PID to vJoy slot explicitly, then user assigns `js1` = vJoy slot 1 in our config.

```csharp
public class SCDeviceMapping
{
    public string SCDeviceId { get; }        // "js1", "js2"
    public uint VJoySlot { get; }            // Our vJoy slot assignment
    public string FriendlyName { get; }      // "Left Stick", "Right Stick"
}
```

## Binding Visualization

Display bindings on device silhouette (from SCVirtStick):

```csharp
public class BindingVisualizer
{
    private DeviceControlMap _controlMap;    // Maps physical controls to SVG elements
    private List<Binding> _bindings;         // SC bindings for this device

    public void HighlightControl(string scInput)
    {
        // "button1" → find control in map → highlight SVG element
        var control = _controlMap.FindControlByBinding(scInput);
        if (control != null)
            _highlightedControlIds.Add(control.Id);
    }
}
```

## Export (Binding Generation)

Generate new actionmaps.xml for user to import:

```csharp
public class BindingExporter
{
    public string GenerateActionMaps(ExportConfig config)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<ActionMaps>");

        foreach (var actionMap in config.ActionMaps)
        {
            sb.AppendLine($"  <actionmap name=\"{actionMap.Name}\">");
            foreach (var action in actionMap.Actions)
            {
                foreach (var binding in action.Bindings)
                {
                    sb.AppendLine($"    <action name=\"{action.Name}\">");
                    sb.AppendLine($"      <rebind input=\"{binding.ToSCFormat()}\"/>");
                    sb.AppendLine($"    </action>");
                }
            }
            sb.AppendLine("  </actionmap>");
        }

        sb.AppendLine("</ActionMaps>");
        return sb.ToString();
    }
}
```

**Important**: Export to a new file, never overwrite user's existing actionmaps.xml. Let user manually import/merge.

## Control Map JSON

Maps physical device controls to SC binding names and SVG visualization:

```json
{
  "device": "VPC Constellation Alpha-R",
  "vidPid": "3344:0194",
  "controls": {
    "trigger": {
      "id": "control_trigger",
      "type": "Button",
      "bindings": ["button1", "button2"],
      "label": "Trigger",
      "anchor": { "x": 950, "y": 450 }
    }
  }
}
```

## Existing Code to Reuse

From SCVirtStick:
- `Services/StarCitizenService.cs` - P4K reading, path detection
- `Services/ActionMapsService.cs` - XML parsing (needs cleanup)
- `Models/DeviceControlMap.cs` - Control map model
- `UI/FUI/DeviceSilhouette.cs` - SVG rendering and highlighting (concepts, not code directly)
- `UI/FUI/BindingMatrixControl.cs` - Binding grid display

## What NOT to Do

- Never auto-modify actionmaps.xml
- Never assume device order (js1, js2) is stable
- Never cache P4K data without version check
- Never show stale binding data after SC update
