using Asteriq.DirectInput;
using Asteriq.Models;

namespace Asteriq.Services;

/// <summary>
/// Detects the DirectInput enumeration order of vJoy virtual devices and builds
/// the vJoy slot → SC joystick instance mapping.
///
/// Star Citizen numbers joysticks (JS1, JS2, JS3) based on DirectInput enumeration
/// order — the same order shown in joy.cpl. This order may differ from vJoy slot
/// numbers, causing wrong bindings if a 1:1 slot→instance assumption is used.
/// </summary>
public static class VJoyDirectInputOrderService
{
    /// <summary>
    /// vJoy ProductGuid — all vJoy virtual devices share this GUID.
    /// "BEAD1234-0000-0000-0000-504944564944"
    /// </summary>
    private static readonly Guid VJoyProductGuid = new("bead1234-0000-0000-0000-504944564944");

    /// <summary>
    /// Detects the DirectInput enumeration order of vJoy devices and returns a mapping
    /// from vJoy slot ID to SC joystick instance number (1-based).
    ///
    /// Algorithm:
    /// 1. Filters <paramref name="diDevices"/> to vJoy devices by ProductGuid,
    ///    preserving DI enumeration order.
    /// 2. Extracts the vJoy slot number from each DI device's InstanceGuid
    ///    (encoded in Data4[1] — e.g., xxxxxxxx-xxxx-xxxx-8002-... = slot 2).
    /// 3. Maps each slot to its 1-based DI position among vJoy devices.
    /// </summary>
    /// <param name="vjoySlots">vJoy device slots to include (Exists = true slots only).</param>
    /// <param name="diDevices">DirectInput game controllers in enumeration order.</param>
    /// <returns>Dictionary mapping vJoy slot ID → SC joystick instance (1-based).</returns>
    public static Dictionary<uint, int> DetectVJoyDiOrder(
        IEnumerable<VJoyDeviceInfo> vjoySlots,
        IEnumerable<DirectInputDeviceInfo> diDevices)
    {
        var result = new Dictionary<uint, int>();

        var slots = vjoySlots.Where(v => v.Exists).OrderBy(v => v.Id).ToList();
        if (slots.Count == 0)
            return result;

        // Filter to vJoy DI devices by ProductGuid, preserving enumeration order.
        // Fallback: also accept devices with "vjoy" in the name (in case ProductGuid varies).
        var vjoyDiDevices = diDevices
            .Where(d => d.ProductGuid == VJoyProductGuid
                || d.ProductName.Contains("vjoy", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (vjoyDiDevices.Count == 0)
        {
            // DirectInput returned no vJoy devices — fall back to identity mapping
            foreach (var slot in slots)
                result[slot.Id] = (int)slot.Id;
            return result;
        }

        // Extract slot number from InstanceGuid and map to DI position.
        // vJoy encodes the slot number in InstanceGuid Data4[1]:
        //   xxxxxxxx-xxxx-xxxx-80NN-444553540000 where NN = slot number.
        var slotIds = new HashSet<uint>(slots.Select(s => s.Id));
        int scInstance = 0;

        foreach (var diDev in vjoyDiDevices)
        {
            scInstance++;
            uint slotId = ExtractVJoySlotFromGuid(diDev.InstanceGuid);

            if (slotId > 0 && slotIds.Contains(slotId) && !result.ContainsKey(slotId))
            {
                result[slotId] = scInstance;
            }
        }

        // Any slots not found in DI get identity mapping
        foreach (var slot in slots)
        {
            if (!result.ContainsKey(slot.Id))
                result[slot.Id] = (int)slot.Id;
        }

        return result;
    }

    /// <summary>
    /// Extracts the vJoy slot number from a DirectInput InstanceGuid.
    /// vJoy uses the format: xxxxxxxx-xxxx-xxxx-80NN-444553540000
    /// where NN (Data4[1]) is the 1-based slot number.
    /// Returns 0 if the GUID doesn't match the expected pattern.
    /// </summary>
    public static uint ExtractVJoySlotFromGuid(Guid instanceGuid)
    {
        var bytes = instanceGuid.ToByteArray();
        // Data4 starts at byte index 8. Data4[0]=bytes[8], Data4[1]=bytes[9].
        return bytes[9];
    }

    /// <summary>
    /// Returns the vJoyConfig.exe axis flags (e.g., ["X","Y","RX","RY","SL0","Z"])
    /// matching the physical device's actual axis types.
    /// Tries HID data first (AxisInfos), falls back to isolated DI query, then sequential defaults.
    /// </summary>
    public static List<string> GetVJoyAxisFlagsForDevice(PhysicalDeviceInfo physical)
    {
        // 1. Try HID axis data (populated at startup by InputService.PopulateAxisTypes)
        if (physical.AxisInfos.Count > 0)
        {
            var flags = AxisTypesToFlags(physical.AxisInfos.Select(a => a.Type));
            if (flags.Count > 0) return flags;
        }

        // 2. Fallback: isolated DI query
        var diAllAxes = DirectInput.DirectInputService.QueryAllAxisTypesIsolated();
        if (diAllAxes.TryGetValue(physical.Name, out var diAxes) && diAxes.Count > 0)
        {
            var diTypes = diAxes.Select(a => a.Type switch
            {
                DirectInput.DirectInputAxisType.X => AxisType.X,
                DirectInput.DirectInputAxisType.Y => AxisType.Y,
                DirectInput.DirectInputAxisType.Z => AxisType.Z,
                DirectInput.DirectInputAxisType.RX => AxisType.RX,
                DirectInput.DirectInputAxisType.RY => AxisType.RY,
                DirectInput.DirectInputAxisType.RZ => AxisType.RZ,
                DirectInput.DirectInputAxisType.Slider => AxisType.Slider,
                _ => AxisType.Unknown
            });
            var flags = AxisTypesToFlags(diTypes);
            if (flags.Count > 0) return flags;
        }

        // 3. Fallback: sequential defaults
        string[] defaultAxes = { "X", "Y", "Z", "RX", "RY", "RZ", "SL0", "SL1" };
        int count = Math.Min(physical.AxisCount, defaultAxes.Length);
        return defaultAxes.Take(count).ToList();
    }

    private static List<string> AxisTypesToFlags(IEnumerable<AxisType> types)
    {
        var flags = new List<string>();
        foreach (var type in types)
        {
            string? flag = type switch
            {
                AxisType.X => "X",
                AxisType.Y => "Y",
                AxisType.Z => "Z",
                AxisType.RX => "RX",
                AxisType.RY => "RY",
                AxisType.RZ => "RZ",
                AxisType.Slider => "SL0",
                _ => null
            };
            if (flag is not null && !flags.Contains(flag))
                flags.Add(flag);
        }
        return flags;
    }
}
