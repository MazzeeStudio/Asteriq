namespace Asteriq.Models;

/// <summary>
/// Represents the current input state of a physical device
/// </summary>
public class DeviceInputState
{
    public int DeviceIndex { get; init; }
    public string DeviceName { get; init; } = string.Empty;
    public Guid InstanceGuid { get; init; }
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// Axis values normalized to -1.0 to 1.0
    /// </summary>
    public float[] Axes { get; init; } = Array.Empty<float>();

    /// <summary>
    /// Button states - true = pressed
    /// </summary>
    public bool[] Buttons { get; init; } = Array.Empty<bool>();

    /// <summary>
    /// Hat/POV values in degrees (0-360) or -1 for centered
    /// </summary>
    public int[] Hats { get; init; } = Array.Empty<int>();
}

/// <summary>
/// Information about a detected physical device
/// </summary>
public class PhysicalDeviceInfo
{
    public int DeviceIndex { get; init; }
    public string Name { get; init; } = string.Empty;
    public Guid InstanceGuid { get; init; }
    public int AxisCount { get; init; }
    public int ButtonCount { get; init; }
    public int HatCount { get; init; }

    /// <summary>
    /// Whether this is a virtual device (vJoy, vXBox, etc.)
    /// </summary>
    public bool IsVirtual { get; init; }

    public override string ToString() => $"{Name} (Axes:{AxisCount}, Buttons:{ButtonCount}, Hats:{HatCount})";
}
