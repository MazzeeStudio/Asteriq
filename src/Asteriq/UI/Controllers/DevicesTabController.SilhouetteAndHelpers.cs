using System.Text.RegularExpressions;
using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class DevicesTabController
{
    private (string Label, bool HasPrev, bool HasNext) GetSilhouettePickerState()
    {
        uint vjoyId = GetSelectedVJoyId();
        var maps = _ctx.AvailableDeviceMaps;

        string? currentKey = vjoyId > 0 ? _ctx.AppSettings.GetVJoySilhouetteOverride(vjoyId) : null;

        int currentIndex = string.IsNullOrEmpty(currentKey)
            ? 0
            : maps.FindIndex(m => m.Key == currentKey) + 1;
        if (currentIndex < 0) currentIndex = 0;

        string label = currentIndex == 0
            ? $"Auto ({_ctx.DeviceMap?.Device ?? "none"})"
            : maps[currentIndex - 1].DisplayName;

        bool canNav = vjoyId > 0;
        int total = maps.Count + 1;
        return (label, canNav && currentIndex > 0, canNav && currentIndex < total - 1);
    }

    private void StepSilhouette(int delta)
    {
        uint vjoyId = GetSelectedVJoyId();
        if (vjoyId == 0) return;

        var currentKey = _ctx.AppSettings.GetVJoySilhouetteOverride(vjoyId);
        var maps = _ctx.AvailableDeviceMaps;

        int currentIndex = string.IsNullOrEmpty(currentKey)
            ? 0
            : maps.FindIndex(m => m.Key == currentKey) + 1;
        if (currentIndex < 0) currentIndex = 0;

        int newIndex = Math.Clamp(currentIndex + delta, 0, maps.Count);
        string? newKey = newIndex == 0 ? null : maps[newIndex - 1].Key;

        _ctx.AppSettings.SetVJoySilhouetteOverride(vjoyId, newKey);
        _ctx.LoadDeviceMapForDevice(_ctx.Devices[_ctx.SelectedDevice]);
        _ctx.UpdateMappingsPrimaryDeviceMap();
        _ctx.InvalidateCanvas();
    }



    private string GetVJoyAssignmentForDevice(PhysicalDeviceInfo device)
    {
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return "";

        string deviceId = device.InstanceGuid.ToString();

        var vjoyIds = new HashSet<uint>();

        foreach (var mapping in profile.AxisMappings)
        {
            if (mapping.Inputs.Any(i => i.DeviceId == deviceId) && mapping.Output.VJoyDevice > 0)
                vjoyIds.Add(mapping.Output.VJoyDevice);
        }
        foreach (var mapping in profile.ButtonMappings)
        {
            if (mapping.Inputs.Any(i => i.DeviceId == deviceId) && mapping.Output.VJoyDevice > 0)
                vjoyIds.Add(mapping.Output.VJoyDevice);
        }
        foreach (var mapping in profile.HatMappings)
        {
            if (mapping.Inputs.Any(i => i.DeviceId == deviceId) && mapping.Output.VJoyDevice > 0)
                vjoyIds.Add(mapping.Output.VJoyDevice);
        }

        if (vjoyIds.Count == 0) return "";
        if (vjoyIds.Count == 1) return $"VJOY:{vjoyIds.First()}";

        // Find which vJoy slot considers this device its primary (most mappings)
        uint primaryVJoy = 0;
        foreach (var (vjoyId, guid) in profile.VJoyPrimaryDevices)
        {
            if (guid.Equals(deviceId, StringComparison.OrdinalIgnoreCase) && vjoyIds.Contains(vjoyId))
            {
                primaryVJoy = vjoyId;
                break;
            }
        }

        // Primary first, then remaining sorted
        var ordered = primaryVJoy > 0
            ? vjoyIds.Where(id => id == primaryVJoy)
                .Concat(vjoyIds.Where(id => id != primaryVJoy).OrderBy(x => x))
            : vjoyIds.OrderBy(x => x);
        return $"VJOY:{string.Join(",", ordered)}";
    }

    private string GetPrimaryDeviceForVJoyDevice(PhysicalDeviceInfo vjoyDevice)
    {
        if (!vjoyDevice.IsVirtual) return "";

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return "";

        uint vjoyId = 0;
        var match = s_vjoySlotNumberRegex.Match(vjoyDevice.Name);
        if (match.Success && uint.TryParse(match.Value, out uint parsedId))
        {
            vjoyId = parsedId;
        }

        if (vjoyId == 0) return "";

        var primaryGuid = profile.GetPrimaryDeviceForVJoy(vjoyId);
        if (string.IsNullOrEmpty(primaryGuid)) return "";

        var primaryDevice = _ctx.Devices.FirstOrDefault(d =>
            !d.IsVirtual && d.InstanceGuid.ToString().Equals(primaryGuid, StringComparison.OrdinalIgnoreCase));

        if (primaryDevice is null) return "";

        string name = primaryDevice.Name;
        if (name.Length > 20)
            name = string.Concat(name.AsSpan(0, 17), "...");

        return $"\u2192 {name}";
    }


    // ─────────────────────────────────────────────────────────────────────────
    // State classes — each groups logically-related fields for one sub-feature.
    // All fields are public so caller code can access them via the instance.
    // ─────────────────────────────────────────────────────────────────────────

}