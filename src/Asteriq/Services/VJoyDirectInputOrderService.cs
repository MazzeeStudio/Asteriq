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
    /// Detects the DirectInput enumeration order of vJoy devices and returns a mapping
    /// from vJoy slot ID to SC joystick instance number (1-based).
    ///
    /// Algorithm:
    /// 1. Filters <paramref name="diDevices"/> to vJoy-named entries (ProductName contains
    ///    "vjoy", case-insensitive), preserving DI enumeration order.
    /// 2. Builds a capability fingerprint (ButtonCount, AxisCount, PovCount) for each
    ///    DI vJoy device and each vJoy slot.
    /// 3. Greedily matches each DI position (in order) to the first unmatched vJoy slot
    ///    whose fingerprint matches.  If no fingerprint match is found, takes the first
    ///    remaining unmatched slot (positional fallback).
    /// 4. Any unmatched slots at the end receive identity mapping (slot N → instance N).
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

        // Filter to vJoy-named DI devices, preserving enumeration order
        var vjoyDiDevices = diDevices
            .Where(d => d.ProductName.Contains("vjoy", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (vjoyDiDevices.Count == 0)
        {
            // DirectInput returned no vJoy devices — fall back to identity mapping
            foreach (var slot in slots)
                result[slot.Id] = (int)slot.Id;
            return result;
        }

        // Build fingerprints: (ButtonCount, AxisCount, PovCount)
        // AxisCount for vJoy slots is derived from the Has* properties.
        var slotFingerprints = slots
            .Select(v => (ButtonCount: v.ButtonCount, AxisCount: CountAxes(v), PovCount: v.DiscPovCount + v.ContPovCount))
            .ToList();

        // unmatched = indices into slots[] still available for assignment
        var unmatched = Enumerable.Range(0, slots.Count).ToList();

        for (int diPos = 0; diPos < vjoyDiDevices.Count && unmatched.Count > 0; diPos++)
        {
            var d = vjoyDiDevices[diPos];
            var diFp = (ButtonCount: d.ButtonCount, AxisCount: d.Axes.Count, PovCount: d.PovCount);

            // Find first unmatched slot whose fingerprint matches the DI device
            int matchedListIdx = -1;
            for (int i = 0; i < unmatched.Count; i++)
            {
                if (slotFingerprints[unmatched[i]] == diFp)
                {
                    matchedListIdx = i;
                    break;
                }
            }

            int slotIdx;
            if (matchedListIdx >= 0)
            {
                slotIdx = unmatched[matchedListIdx];
                unmatched.RemoveAt(matchedListIdx);
            }
            else
            {
                // No fingerprint match — positional fallback: take first remaining slot
                slotIdx = unmatched[0];
                unmatched.RemoveAt(0);
            }

            result[slots[slotIdx].Id] = diPos + 1; // 1-based SC instance
        }

        // Any slots that exceeded the DI device count get identity mapping
        foreach (int slotIdx in unmatched)
            result[slots[slotIdx].Id] = (int)slots[slotIdx].Id;

        return result;
    }

    private static int CountAxes(VJoyDeviceInfo v)
    {
        int count = 0;
        if (v.HasAxisX) count++;
        if (v.HasAxisY) count++;
        if (v.HasAxisZ) count++;
        if (v.HasAxisRX) count++;
        if (v.HasAxisRY) count++;
        if (v.HasAxisRZ) count++;
        if (v.HasSlider0) count++;
        if (v.HasSlider1) count++;
        return count;
    }
}
