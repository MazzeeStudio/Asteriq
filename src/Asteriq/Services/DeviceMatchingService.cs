using Asteriq.Models;

namespace Asteriq.Services;

/// <summary>
/// Service to match SDL devices with HidHide device paths
/// </summary>
public class DeviceMatchingService
{
    private readonly HidHideService _hidHide;

    public DeviceMatchingService(HidHideService hidHide)
    {
        _hidHide = hidHide;
    }

    /// <summary>
    /// Extract VID and PID from an SDL GUID
    /// SDL GUID string format: 00000003-VVVV-0000-PPPP-000000000000
    /// where VVVV is VID and PPPP is PID (both displayed as big-endian in string)
    /// </summary>
    public static (ushort vid, ushort pid) ExtractVidPidFromSdlGuid(Guid sdlGuid)
    {
        // Parse from the string representation which is more predictable
        // Format: 00000003-3344-0000-d540-000000000000
        //         ^^^^^^^^ ^^^^ ^^^^ ^^^^
        //         bustype  VID  zero PID (byte-swapped)
        string guidStr = sdlGuid.ToString();
        string[] parts = guidStr.Split('-');

        if (parts.Length < 4)
            return (0, 0);

        // VID is in parts[1] (e.g., "3344")
        // PID is in parts[3] (e.g., "d540" which is 40D5 byte-swapped)
        if (!ushort.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out ushort vid))
            vid = 0;

        // PID needs byte swap: "d540" -> 0x40D5
        if (ushort.TryParse(parts[3], System.Globalization.NumberStyles.HexNumber, null, out ushort pidRaw))
        {
            // Swap bytes: 0xd540 -> 0x40d5
            ushort pid = (ushort)(((pidRaw & 0xFF) << 8) | ((pidRaw >> 8) & 0xFF));
            return (vid, pid);
        }

        return (vid, 0);
    }

    /// <summary>
    /// Find HidHide device paths that match an SDL device
    /// </summary>
    public List<HidHideDeviceInfo> FindMatchingHidDevices(PhysicalDeviceInfo sdlDevice)
    {
        var (vid, pid) = ExtractVidPidFromSdlGuid(sdlDevice.InstanceGuid);

        if (vid == 0 && pid == 0)
        {
            // Couldn't extract VID/PID, try name matching
            return FindByNameMatch(sdlDevice.Name);
        }

        return FindByVidPid(vid, pid);
    }

    /// <summary>
    /// Find HidHide devices by VID/PID
    /// </summary>
    public List<HidHideDeviceInfo> FindByVidPid(ushort vid, ushort pid)
    {
        var matches = new List<HidHideDeviceInfo>();
        var vidHex = vid.ToString("X4");
        var pidHex = pid.ToString("X4");

        // Pattern: HID\VID_XXXX&PID_XXXX
        var pattern = $"VID_{vidHex}&PID_{pidHex}";

        var allDevices = _hidHide.GetGamingDevices();
        foreach (var group in allDevices)
        {
            foreach (var device in group.Devices)
            {
                if (device.DeviceInstancePath.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(device);
                }
            }
        }

        return matches;
    }

    /// <summary>
    /// Find HidHide devices by name matching (fallback)
    /// </summary>
    public List<HidHideDeviceInfo> FindByNameMatch(string sdlName)
    {
        var matches = new List<HidHideDeviceInfo>();

        // Normalize name for comparison
        var normalizedSdlName = NormalizeName(sdlName);

        var allDevices = _hidHide.GetGamingDevices();
        foreach (var group in allDevices)
        {
            var normalizedGroupName = NormalizeName(group.FriendlyName);

            // Check if names have significant overlap
            if (NamesMatch(normalizedSdlName, normalizedGroupName))
            {
                matches.AddRange(group.Devices);
            }
        }

        return matches;
    }

    /// <summary>
    /// Normalize device name for comparison
    /// </summary>
    private static string NormalizeName(string name)
    {
        // Remove common prefixes/suffixes and normalize
        return name
            .Replace("RIGHT ", "R-", StringComparison.OrdinalIgnoreCase)
            .Replace("LEFT ", "L-", StringComparison.OrdinalIgnoreCase)
            .Replace("R-VPC", "VPC", StringComparison.OrdinalIgnoreCase)
            .Replace("L-VPC", "VPC", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant()
            .Trim();
    }

    /// <summary>
    /// Check if two normalized names match
    /// </summary>
    private static bool NamesMatch(string name1, string name2)
    {
        // Exact match
        if (name1 == name2)
            return true;

        // Check if one contains the other
        if (name1.Contains(name2) || name2.Contains(name1))
            return true;

        // Check for key word overlap (e.g., "WarBRD", "VPC", "Stick")
        var keywords = new[] { "warbrd", "vpc", "virpil", "stick", "throttle", "pedals" };
        foreach (var keyword in keywords)
        {
            if (name1.Contains(keyword) && name2.Contains(keyword))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Get all device correlations between SDL and HidHide
    /// </summary>
    public List<DeviceCorrelation> GetAllCorrelations(List<PhysicalDeviceInfo> sdlDevices)
    {
        var correlations = new List<DeviceCorrelation>();

        foreach (var sdlDevice in sdlDevices)
        {
            var (vid, pid) = ExtractVidPidFromSdlGuid(sdlDevice.InstanceGuid);
            var hidMatches = FindMatchingHidDevices(sdlDevice);

            correlations.Add(new DeviceCorrelation
            {
                SdlDevice = sdlDevice,
                Vid = vid,
                Pid = pid,
                HidDevices = hidMatches
            });
        }

        return correlations;
    }
}

/// <summary>
/// Represents a correlation between an SDL device and HidHide devices
/// </summary>
public class DeviceCorrelation
{
    public PhysicalDeviceInfo SdlDevice { get; init; } = null!;
    public ushort Vid { get; init; }
    public ushort Pid { get; init; }
    public List<HidHideDeviceInfo> HidDevices { get; init; } = new();

    /// <summary>
    /// Get the primary gaming device path (MI_00 interface)
    /// </summary>
    public string? PrimaryDevicePath => HidDevices
        .FirstOrDefault(d => d.IsGamingDevice && d.DeviceInstancePath.Contains("MI_00"))
        ?.DeviceInstancePath;

    /// <summary>
    /// Get all device paths for hiding
    /// </summary>
    public IEnumerable<string> AllDevicePaths => HidDevices.Select(d => d.DeviceInstancePath);

    public bool IsVJoy => SdlDevice.Name.Contains("vJoy", StringComparison.OrdinalIgnoreCase);
}
