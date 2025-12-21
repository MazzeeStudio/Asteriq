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
/// Axis type as reported by DirectInput (HID usage)
/// </summary>
public enum AxisType
{
    Unknown = 0,
    X = 1,
    Y = 2,
    Z = 3,
    RX = 4,
    RY = 5,
    RZ = 6,
    Slider = 7
}

/// <summary>
/// Information about a single axis on a device
/// </summary>
public class AxisInfo
{
    public int Index { get; init; }
    public AxisType Type { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Get the corresponding vJoy axis index for this axis type.
    /// Returns -1 if the type cannot be mapped to vJoy.
    /// </summary>
    public int ToVJoyAxisIndex()
    {
        return Type switch
        {
            AxisType.X => 0,
            AxisType.Y => 1,
            AxisType.Z => 2,
            AxisType.RX => 3,
            AxisType.RY => 4,
            AxisType.RZ => 5,
            AxisType.Slider => 6, // First slider maps to Slider0
            _ => -1
        };
    }

    /// <summary>
    /// Get a display name for the axis type
    /// </summary>
    public string TypeName => Type switch
    {
        AxisType.X => "X",
        AxisType.Y => "Y",
        AxisType.Z => "Z",
        AxisType.RX => "RX",
        AxisType.RY => "RY",
        AxisType.RZ => "RZ",
        AxisType.Slider => "Slider",
        _ => "Unknown"
    };
}

/// <summary>
/// Information about a detected physical device
/// </summary>
public class PhysicalDeviceInfo
{
    public int DeviceIndex { get; set; }
    public string Name { get; init; } = string.Empty;
    public Guid InstanceGuid { get; init; }
    public int AxisCount { get; init; }
    public int ButtonCount { get; init; }
    public int HatCount { get; init; }

    /// <summary>
    /// Whether this is a virtual device (vJoy, vXBox, etc.)
    /// </summary>
    public bool IsVirtual { get; init; }

    /// <summary>
    /// Whether the device is currently connected.
    /// Disconnected devices are shown but cannot be used until reconnected.
    /// </summary>
    public bool IsConnected { get; set; } = true;

    /// <summary>
    /// Unique HID device path (used for reliable device identification).
    /// This is unique per physical device instance, unlike SDL2's GUID which
    /// identifies device type only.
    /// </summary>
    public string HidDevicePath { get; set; } = string.Empty;

    /// <summary>
    /// DirectInput instance GUID for this device.
    /// Used for DirectInput-based input reading.
    /// </summary>
    public Guid DirectInputGuid { get; set; } = Guid.Empty;

    /// <summary>
    /// Detailed axis information including types (from HID report descriptor).
    /// May be empty if HID info is not available.
    /// </summary>
    public List<AxisInfo> AxisInfos { get; set; } = new();

    /// <summary>
    /// Get the axis type for a given index. Returns Unknown if not available.
    /// </summary>
    public AxisType GetAxisType(int index)
    {
        var axisInfo = AxisInfos.FirstOrDefault(a => a.Index == index);
        return axisInfo?.Type ?? AxisType.Unknown;
    }

    public override string ToString() => $"{Name} (Axes:{AxisCount}, Buttons:{ButtonCount}, Hats:{HatCount})";
}
