namespace Asteriq.Models;

/// <summary>
/// Identifies the physical button that toggles input forwarding between local and remote.
/// The button is consumed — its input never reaches vJoy or MappingEngine.
/// </summary>
public class NetworkSwitchConfig
{
    /// <summary>
    /// SDL2 device instance index (0-based) on the physical device list.
    /// Matches the index used by InputService.
    /// </summary>
    public int DeviceIndex { get; set; }

    /// <summary>
    /// Button index on the device (0-based).
    /// </summary>
    public int ButtonIndex { get; set; }

    /// <summary>
    /// Human-readable label shown in the NET SWITCH badge and Mappings row.
    /// e.g. "VPC WarBRD Base Button 12"
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Device ID (VID:PID or GUID) used to re-identify the device across sessions.
    /// </summary>
    public string DeviceId { get; set; } = "";
}
