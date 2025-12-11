using Asteriq.VJoy;

namespace Asteriq.Models;

/// <summary>
/// Information about a vJoy device slot
/// </summary>
public class VJoyDeviceInfo
{
    public uint Id { get; init; }
    public bool Exists { get; init; }
    public VjdStat Status { get; init; }
    public int ButtonCount { get; init; }
    public int DiscPovCount { get; init; }
    public int ContPovCount { get; init; }
    public bool HasAxisX { get; init; }
    public bool HasAxisY { get; init; }
    public bool HasAxisZ { get; init; }
    public bool HasAxisRX { get; init; }
    public bool HasAxisRY { get; init; }
    public bool HasAxisRZ { get; init; }
    public bool HasSlider0 { get; init; }
    public bool HasSlider1 { get; init; }
}
