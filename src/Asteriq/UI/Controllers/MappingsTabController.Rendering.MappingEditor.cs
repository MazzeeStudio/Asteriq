using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
    /// <summary>
    /// Auto-derives whether the (vjoyId, axisIndex) slot is a merged-away secondary slot of
    /// some merge in the profile. For every input of every merge we compute its "natural
    /// slot" — the vJoy slot whose VJoyPrimaryDevices entry matches the input's physical
    /// device, at the same axis index. The input that lands ON the merge target's own slot
    /// is the primary; every other input's natural slot is a merged-away secondary.
    /// Returns the merge target + the input that landed here, or null when the slot
    /// doesn't qualify (no matching merge, or the slot is mapped independently to a
    /// different input — we don't hijack legit mappings).
    /// </summary>
    private static (AxisMapping Target, InputSource SecondaryInput)? GetMergedAwayInfo(MappingProfile? profile, uint vjoyId, int axisIndex)
    {
        if (profile is null) return null;

        var slot = profile.AxisMappings.FirstOrDefault(m =>
            m.Output.Type == OutputType.VJoyAxis &&
            m.Output.VJoyDevice == vjoyId &&
            m.Output.Index == axisIndex);

        foreach (var merge in profile.AxisMappings)
        {
            if (merge.Inputs.Count < 2) continue;
            if (merge.Output.Type != OutputType.VJoyAxis) continue;
            if (merge.Output.VJoyDevice == vjoyId && merge.Output.Index == axisIndex)
                continue; // skip merge target itself

            foreach (var input in merge.Inputs)
            {
                // Natural slot for this physical input = (its primary vJoy device, its axis index).
                uint? naturalVJoy = null;
                foreach (var kv in profile.VJoyPrimaryDevices)
                {
                    if (kv.Value == input.DeviceId) { naturalVJoy = kv.Key; break; }
                }
                if (naturalVJoy is null) continue;

                // Skip the input that IS at the merge target — that one is the primary, not
                // a merged-away secondary. (Robust whether the user added inputs in primary-
                // first order or any other order.)
                if (naturalVJoy == merge.Output.VJoyDevice && input.Index == merge.Output.Index)
                    continue;

                if (naturalVJoy != vjoyId) continue;
                if (input.Index != axisIndex) continue;

                // Slot is the merged-away secondary. Mark it iff the slot is empty OR mapped
                // solo to the same physical input (a parallel single mapping rendered
                // redundant by the merge). Otherwise the slot is doing something independent.
                if (slot is null || slot.Inputs.Count == 0)
                    return (merge, input);
                if (slot.Inputs.Count == 1 &&
                    slot.Inputs[0].DeviceId == input.DeviceId &&
                    slot.Inputs[0].Type == input.Type &&
                    slot.Inputs[0].Index == input.Index)
                    return (merge, input);
                return null;
            }
        }

        return null;
    }

}
