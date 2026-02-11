using System.Text.Json;
using System.Text.Json.Serialization;

namespace Asteriq.Models;

/// <summary>
/// Maps physical device controls to SVG elements for visualization.
/// Loaded from JSON map files in Images/Devices/Maps/
/// </summary>
public class DeviceMap
{
    /// <summary>
    /// Path this map was loaded from (not serialized)
    /// </summary>
    [JsonIgnore]
    public string? LoadedFromPath { get; set; }

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.1";

    [JsonPropertyName("device")]
    public string Device { get; set; } = "";

    [JsonPropertyName("vidPid")]
    public string? VidPid { get; set; }

    [JsonPropertyName("deviceType")]
    public string DeviceType { get; set; } = "Generic";

    [JsonPropertyName("svgFile")]
    public string SvgFile { get; set; } = "";

    [JsonPropertyName("mirror")]
    public bool Mirror { get; set; } = false;

    [JsonPropertyName("viewBox")]
    public ViewBoxDimensions? ViewBox { get; set; }

    [JsonPropertyName("controls")]
    public Dictionary<string, ControlDefinition> Controls { get; set; } = new();

    /// <summary>
    /// Find which control a binding (e.g. "button1", "x") maps to
    /// </summary>
    public ControlDefinition? FindControlByBinding(string binding)
    {
        foreach (var control in Controls.Values)
        {
            if (control.Bindings?.Contains(binding, StringComparer.OrdinalIgnoreCase) == true)
                return control;
        }
        return null;
    }

    /// <summary>
    /// Load a device map from a JSON file
    /// </summary>
    public static DeviceMap? Load(string jsonPath)
    {
        if (!File.Exists(jsonPath)) return null;

        try
        {
            var json = File.ReadAllText(jsonPath);
            var map = JsonSerializer.Deserialize<DeviceMap>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (map is not null)
            {
                map.LoadedFromPath = jsonPath;
            }
            return map;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load device map from '{jsonPath}'. " +
                                               $"Error type: {ex.GetType().Name}, Details: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Save this device map back to its source file
    /// </summary>
    public bool Save()
    {
        if (string.IsNullOrEmpty(LoadedFromPath)) return false;

        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            File.WriteAllText(LoadedFromPath, json);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save device map to '{LoadedFromPath}'. " +
                                               $"Error: {ex.Message}");
            return false;
        }
    }
}

public class ViewBoxDimensions
{
    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }
}

public class ControlDefinition
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "Button";

    [JsonPropertyName("bindings")]
    public List<string>? Bindings { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("anchor")]
    public Point2D? Anchor { get; set; }

    [JsonPropertyName("labelOffset")]
    public Point2D? LabelOffset { get; set; }

    [JsonPropertyName("leadLine")]
    public LeadLineDefinition? LeadLine { get; set; }
}

/// <summary>
/// Defines the shape of a lead-line connecting a control to its label.
/// The line starts at the anchor point with a horizontal shelf,
/// then follows one or more angled segments to reach the label.
/// </summary>
public class LeadLineDefinition
{
    /// <summary>
    /// Direction of the horizontal shelf from the anchor point.
    /// "left" or "right"
    /// </summary>
    [JsonPropertyName("shelfSide")]
    public string ShelfSide { get; set; } = "right";

    /// <summary>
    /// Length of the horizontal shelf in SVG units (viewBox space)
    /// </summary>
    [JsonPropertyName("shelfLength")]
    public float ShelfLength { get; set; } = 80f;

    /// <summary>
    /// Segments after the shelf. Each segment has an angle and length.
    /// Angles: 0=horizontal, 90=up, -90=down, 45=diagonal up, etc.
    /// </summary>
    [JsonPropertyName("segments")]
    public List<LeadLineSegment>? Segments { get; set; }
}

/// <summary>
/// A single segment of a lead-line path
/// </summary>
public class LeadLineSegment
{
    /// <summary>
    /// Angle in degrees. 0=horizontal (continues shelf direction),
    /// 90=straight up, -90=straight down, 45=diagonal up-right (if shelf goes right)
    /// </summary>
    [JsonPropertyName("angle")]
    public float Angle { get; set; }

    /// <summary>
    /// Length of this segment in SVG units (viewBox space)
    /// </summary>
    [JsonPropertyName("length")]
    public float Length { get; set; }
}

public class Point2D
{
    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }
}
