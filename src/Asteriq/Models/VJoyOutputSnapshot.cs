namespace Asteriq.Models;

/// <summary>
/// Post-mapped vJoy state captured after the MappingEngine runs.
/// Sent from the master machine to the client over TCP.
/// </summary>
public sealed class VJoyOutputSnapshot
{
    /// <summary>vJoy device ID (1-based).</summary>
    public uint DeviceId { get; set; }

    /// <summary>Axis values, normalised -1.0 to +1.0. Up to 8 axes (X/Y/Z/RX/RY/RZ/SL0/SL1).</summary>
    public float[] Axes { get; set; } = new float[8];

    /// <summary>Button states, 0-indexed. Index 0 = vJoy button 1.</summary>
    public bool[] Buttons { get; set; } = new bool[128];

    /// <summary>Hat/POV values in degrees (0-35900), or -1 for neutral. Up to 4 hats.</summary>
    public int[] Hats { get; set; } = new int[4];

    public int AxisCount { get; set; }
    public int ButtonCount { get; set; }
    public int HatCount { get; set; }
}
