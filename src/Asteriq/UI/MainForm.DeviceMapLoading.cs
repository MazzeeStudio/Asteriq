using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using Asteriq.UI.Controllers;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using Svg.Skia;

namespace Asteriq.UI;

public partial class MainForm
{
    private void LoadSvgAssets()
    {
        var imagesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Devices");

        var joystickPath = Path.Combine(imagesDir, "joystick.svg");
        if (File.Exists(joystickPath))
        {
            _joystickSvg = new SKSvg();
            _joystickSvg.Load(joystickPath);
            ParseControlBounds(joystickPath);
        }

        var throttlePath = Path.Combine(imagesDir, "throttle.svg");
        if (File.Exists(throttlePath))
        {
            _throttleSvg = new SKSvg();
            _throttleSvg.Load(throttlePath);
        }

        // Load default device map (will be updated when device is selected)
        LoadDeviceMapForDevice(null);
    }

    /// <summary>
    /// Load the appropriate device map based on device info.
    /// Searches for maps matching VID:PID first, then device name, falls back to device type, then generic joystick.json.
    /// Also detects left-hand devices by "LEFT" prefix and sets mirror flag.
    /// </summary>
    private void LoadDeviceMapForDevice(PhysicalDeviceInfo? device)
    {
        var mapsDir = GetMapsDir();

        DeviceMap? resolved = device is { IsVirtual: true }
            ? ResolveVirtualDeviceMap(device, mapsDir)
            : LoadDeviceMapForDeviceInfo(device);

        _deviceMap = resolved ?? DeviceMap.Load(Path.Combine(mapsDir, "joystick.json"));
        SyncDeviceMapToTabContext();
    }

    private DeviceMap? ResolveVirtualDeviceMap(PhysicalDeviceInfo device, string mapsDir)
    {
        uint vjoyId = DeriveVJoyId(device);
        if (vjoyId == 0)
            return null;

        if (TryLoadOverrideMap(vjoyId, mapsDir) is { } overrideMap)
            return overrideMap;

        var physicalDevice = FindPhysicalDeviceForVJoy(vjoyId);
        return physicalDevice is not null ? LoadDeviceMapForDeviceInfo(physicalDevice) : null;
    }

    private uint DeriveVJoyId(PhysicalDeviceInfo device)
    {
        if (_vjoyDevices.Count > 0)
        {
            var virtualDevices = _devices.Where(d => d.IsVirtual).ToList();
            int virtualIndex = virtualDevices.IndexOf(device);
            if (virtualIndex >= 0 && virtualIndex < _vjoyDevices.Count)
                return _vjoyDevices[virtualIndex].Id;
        }

        var vMatch = System.Text.RegularExpressions.Regex.Match(
            device.Name, @"\d+", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
        return vMatch.Success && uint.TryParse(vMatch.Value, out uint parsedId) ? parsedId : 0u;
    }

    private DeviceMap? TryLoadOverrideMap(uint vjoyId, string mapsDir)
    {
        var overrideKey = _appSettings.GetVJoySilhouetteOverride(vjoyId);
        if (string.IsNullOrEmpty(overrideKey))
            return null;

        var overridePath = Path.Combine(mapsDir, $"{overrideKey}.json");
        return File.Exists(overridePath) ? DeviceMap.Load(overridePath) : null;
    }

    private PhysicalDeviceInfo? FindPhysicalDeviceForVJoy(uint vjoyId)
    {
        var profile = _profileManager.ActiveProfile;
        if (profile is null)
            return null;

        var assignment = profile.GetAssignmentForVJoy(vjoyId);
        var primaryGuid = profile.GetPrimaryDeviceForVJoy(vjoyId);
        if (string.IsNullOrEmpty(primaryGuid) && !string.IsNullOrEmpty(assignment?.PhysicalDevice.Guid))
            primaryGuid = assignment.PhysicalDevice.Guid;

        if (!string.IsNullOrEmpty(primaryGuid))
        {
            var byGuid = _devices.FirstOrDefault(d =>
                !d.IsVirtual && d.InstanceGuid.ToString().Equals(primaryGuid, StringComparison.OrdinalIgnoreCase));
            if (byGuid is not null)
                return byGuid;
        }

        if (!string.IsNullOrEmpty(assignment?.PhysicalDevice.Name))
        {
            return _devices.FirstOrDefault(d =>
                !d.IsVirtual && d.Name.Equals(assignment.PhysicalDevice.Name, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    /// <summary>
    /// Syncs the current _deviceMap to TabContext so SyncFromTabContext doesn't clobber it.
    /// Called after LoadDeviceMapForDevice modifies _deviceMap via callback.
    /// </summary>
    private void SyncDeviceMapToTabContext()
    {
        if (_tabContext is not null)
            _tabContext.DeviceMap = _deviceMap;
    }

    /// <summary>
    /// Load device map for a device and return it (doesn't modify _deviceMap field).
    /// Match order: VID:PID, device name (manufacturer-stripped), device type, generic joystick (mirrored for left-hand).
    /// </summary>
    private static DeviceMap? LoadDeviceMapForDeviceInfo(PhysicalDeviceInfo? device)
    {
        var mapsDir = GetMapsDir();
        if (device is null || string.IsNullOrEmpty(device.Name))
            return DeviceMap.Load(Path.Combine(mapsDir, "joystick.json"));

        var allMaps = LoadAllDeviceMaps(mapsDir);
        return MatchByVidPid(allMaps, device.InstanceGuid)
            ?? MatchByName(allMaps, device.Name)
            ?? MatchByType(allMaps, device.Name)
            ?? LoadDefaultMapForDevice(device.Name, mapsDir);
    }

    private static string GetMapsDir() =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Devices", "Maps");

    private static List<DeviceMap> LoadAllDeviceMaps(string mapsDir)
    {
        var result = new List<DeviceMap>();
        foreach (var mapFile in Directory.GetFiles(mapsDir, "*.json"))
        {
            if (Path.GetFileName(mapFile).Equals("device-control-map.schema.json", StringComparison.OrdinalIgnoreCase))
                continue;
            var map = DeviceMap.Load(mapFile);
            if (map is not null)
                result.Add(map);
        }
        return result;
    }

    private static DeviceMap? MatchByVidPid(List<DeviceMap> maps, Guid instanceGuid)
    {
        var (vid, pid) = Services.DeviceMatchingService.ExtractVidPidFromSdlGuid(instanceGuid);
        if (vid == 0)
            return null;
        string vidPid = $"{vid:X4}:{pid:X4}";
        return maps.FirstOrDefault(m =>
            !string.IsNullOrEmpty(m.VidPid) &&
            m.VidPid.Equals(vidPid, StringComparison.OrdinalIgnoreCase));
    }

    private static DeviceMap? MatchByName(List<DeviceMap> maps, string deviceName)
    {
        // Strip manufacturer prefixes so map display names (e.g. "MongoosT-50CM3 Throttle")
        // still match SDL-reported names (e.g. "VPC MongoosT-50CM3").
        string normDevice = StripMfr(deviceName);
        return maps.FirstOrDefault(m =>
            !string.IsNullOrEmpty(m.Device) &&
            !m.Device.StartsWith("Generic", StringComparison.OrdinalIgnoreCase) &&
            NamesOverlap(normDevice, StripMfr(m.Device)));
    }

    private static bool NamesOverlap(string a, string b) =>
        a.Contains(b, StringComparison.OrdinalIgnoreCase) ||
        b.Contains(a, StringComparison.OrdinalIgnoreCase);

    private static string StripMfr(string s) => s
        .Replace("VPC ", "", StringComparison.OrdinalIgnoreCase)
        .Replace("VKB ", "", StringComparison.OrdinalIgnoreCase)
        .Trim();

    private static DeviceMap? MatchByType(List<DeviceMap> maps, string deviceName)
    {
        string detectedType = DetectDeviceType(deviceName);
        if (detectedType == "Joystick")
            return null;
        return maps.FirstOrDefault(m =>
            m.DeviceType.Equals(detectedType, StringComparison.OrdinalIgnoreCase));
    }

    private static DeviceMap? LoadDefaultMapForDevice(string deviceName, string mapsDir)
    {
        var map = DeviceMap.Load(Path.Combine(mapsDir, "joystick.json"));
        if (map is not null && IsLeftHandName(deviceName))
            map.Mirror = true;
        return map;
    }

    private static bool IsLeftHandName(string deviceName) =>
        deviceName.StartsWith("LEFT", StringComparison.OrdinalIgnoreCase) ||
        deviceName.Contains("- L", StringComparison.OrdinalIgnoreCase) ||
        deviceName.EndsWith(" L", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Get the appropriate SVG for a given device map.
    /// Returns null if the map references a non-SVG image â€” call GetBitmapForDeviceMap instead.
    /// Falls back to the generic joystick/throttle SVG when no per-device file is found.
    /// </summary>
    private SKSvg? GetSvgForDeviceMap(DeviceMap? map)
    {
        if (map is null)
            return _joystickSvg;

        var imageFile = map.SvgFile;
        if (!string.IsNullOrEmpty(imageFile))
        {
            var ext = Path.GetExtension(imageFile).ToLowerInvariant();
            if (ext != ".svg")
                return null; // bitmap â€” caller should use GetBitmapForDeviceMap

            var svg = LoadSvgFromCache(imageFile);
            if (svg is not null) return svg;
        }

        // Generic fallback
        var lower = (imageFile ?? "").ToLowerInvariant();
        return lower.Contains("throttle") ? _throttleSvg ?? _joystickSvg : _joystickSvg;
    }

    /// <summary>
    /// Get the bitmap for a given device map, if its image file is a raster format.
    /// Returns null for SVG maps or maps with no image file.
    /// </summary>
    private SKBitmap? GetBitmapForDeviceMap(DeviceMap? map)
    {
        if (map is null) return null;

        var imageFile = map.SvgFile;
        if (string.IsNullOrEmpty(imageFile)) return null;

        var ext = Path.GetExtension(imageFile).ToLowerInvariant();
        if (ext == ".svg") return null;

        return LoadBitmapFromCache(imageFile);
    }

    private static SKSvg? LoadLogoSvg()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "AsteriqLogo.svg");
        if (!File.Exists(path)) return null;
        var svg = new SKSvg();
        if (svg.Load(path) is null) { svg.Dispose(); return null; }
        return svg;
    }

    private SKSvg? LoadSvgFromCache(string imageFile)
    {
        if (_svgCache.TryGetValue(imageFile, out var cached)) return cached;

        var imagesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Devices");
        var path = Path.Combine(imagesDir, imageFile);
        if (!File.Exists(path)) return null;

        var svg = new SKSvg();
        if (svg.Load(path) is null) { svg.Dispose(); return null; }
        _svgCache[imageFile] = svg;
        return svg;
    }

    private SKBitmap? LoadBitmapFromCache(string imageFile)
    {
        if (_bitmapCache.TryGetValue(imageFile, out var cached)) return cached;

        var imagesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Devices");
        var path = Path.Combine(imagesDir, imageFile);
        if (!File.Exists(path)) return null;

        var bitmap = SKBitmap.Decode(path);
        if (bitmap is null) return null;
        _bitmapCache[imageFile] = bitmap;
        return bitmap;
    }

    /// <summary>
    /// Enumerates all device map JSON files in the Maps directory and returns them as
    /// (Key, DisplayName) pairs. Called once at startup and stored in TabContext.
    /// </summary>
    private static List<(string Key, string DisplayName)> LoadAvailableDeviceMaps()
    {
        var mapsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Devices", "Maps");
        var result = new List<(string Key, string DisplayName)>();
        if (!Directory.Exists(mapsDir)) return result;

        foreach (var mapFile in Directory.GetFiles(mapsDir, "*.json").OrderBy(f => f))
        {
            if (Path.GetFileName(mapFile).Equals("device-control-map.schema.json", StringComparison.OrdinalIgnoreCase))
                continue;
            var map = DeviceMap.Load(mapFile);
            if (map is null) continue;
            string key = Path.GetFileNameWithoutExtension(mapFile);
            string displayName = string.IsNullOrEmpty(map.Device) ? key : map.Device;
            result.Add((key, displayName));
        }
        return result;
    }

    /// <summary>
    /// Update the device map used for Mappings tab visualization based on vJoy primary device.
    /// Respects the user's visual identity override if set; falls back to auto-detection from
    /// the primary physical device assigned/mapped to this vJoy slot.
    /// </summary>
    private void UpdateMappingsPrimaryDeviceMap()
    {
        _mappingsPrimaryDeviceMap = null;

        // Ensure vJoy devices are populated
        if (_vjoyDevices.Count == 0 && _vjoyService.IsInitialized)
        {
            _vjoyDevices = _vjoyService.EnumerateDevices();
        }

        if (_vjoyDevices.Count == 0)
            return;

        // Use context index when available â€” tab controllers may update it before SyncFromTabContext runs
        int effectiveIndex = _tabContext?.SelectedVJoyDeviceIndex ?? _selectedVJoyDeviceIndex;
        if (effectiveIndex >= _vjoyDevices.Count)
            return;

        var vjoyDevice = _vjoyDevices[effectiveIndex];

        // Check for a user-specified silhouette override first
        var overrideKey = _appSettings.GetVJoySilhouetteOverride(vjoyDevice.Id);
        if (!string.IsNullOrEmpty(overrideKey))
        {
            var mapsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "Devices", "Maps");
            var overridePath = Path.Combine(mapsDir, $"{overrideKey}.json");
            if (File.Exists(overridePath))
            {
                _mappingsPrimaryDeviceMap = DeviceMap.Load(overridePath);
                return;
            }
        }

        // Auto: derive from the primary physical device mapped to this vJoy slot
        var profile = _profileManager.ActiveProfile;
        if (profile is null)
            return;

        var primaryGuid = profile.GetPrimaryDeviceForVJoy(vjoyDevice.Id);

        // Fallback: use device assignment when no primary detected from mappings
        if (string.IsNullOrEmpty(primaryGuid))
        {
            var assignment = profile.GetAssignmentForVJoy(vjoyDevice.Id);
            if (assignment is not null && !string.IsNullOrEmpty(assignment.PhysicalDevice.Guid))
                primaryGuid = assignment.PhysicalDevice.Guid;
        }

        if (string.IsNullOrEmpty(primaryGuid))
            return;

        var primaryDevice = _devices.FirstOrDefault(d =>
            d.InstanceGuid.ToString().Equals(primaryGuid, StringComparison.OrdinalIgnoreCase));

        // Fallback: match by device name if GUID doesn't resolve (e.g. SDL GUID shifted between sessions)
        if (primaryDevice is null)
        {
            var assignment = profile.GetAssignmentForVJoy(vjoyDevice.Id);
            if (assignment is not null && !string.IsNullOrEmpty(assignment.PhysicalDevice.Name))
                primaryDevice = _devices.FirstOrDefault(d =>
                    !d.IsVirtual && d.Name.Equals(assignment.PhysicalDevice.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (primaryDevice is not null)
            _mappingsPrimaryDeviceMap = LoadDeviceMapForDeviceInfo(primaryDevice);
    }

    /// <summary>
    /// Called when mappings are created or removed to update primary device detection
    /// and hot-reload the mapping engine if forwarding is active.
    /// </summary>
    private void OnMappingsChanged()
    {
        var profile = _profileManager.ActiveProfile;
        if (profile is null)
            return;

        // Re-detect primary devices for all vJoy slots based on current mappings
        profile.UpdateAllPrimaryDevices();

        // Refresh the visualization map for current vJoy device
        UpdateMappingsPrimaryDeviceMap();

        // Hot-reload engine if forwarding is active so new/changed mappings take effect
        if (_isForwarding && _mappingEngine.IsRunning)
        {
            _mappingEngine.Stop();
            _mappingEngine.LoadProfile(profile);
            _mappingEngine.Start();
        }
    }

    /// <summary>
    /// Detect device type from device name using common keywords
    /// </summary>
    internal static string DetectDeviceType(string deviceName)
    {
        var name = deviceName.ToUpperInvariant();

        // Throttle keywords - VPC throttles (MongoosT-50CM, CM2, CM3), TWCS, Warthog, etc.
        if (name.Contains("THROTTLE") || name.Contains("-50CM") || name.Contains("50CM") ||
            name.Contains("TM50") || name.Contains("TWCS") || name.Contains("MONGOOST") ||
            name.Contains("MONGOOSE") || name.Contains("CM2") || name.Contains("CM3"))
        {
            System.Diagnostics.Debug.WriteLine($"DetectDeviceType: '{deviceName}' -> Throttle");
            return "Throttle";
        }

        // Pedals keywords
        if (name.Contains("PEDAL") || name.Contains("RUDDER") || name.Contains("TPR") ||
            name.Contains("MFG") || name.Contains("CROSSWIND"))
        {
            System.Diagnostics.Debug.WriteLine($"DetectDeviceType: '{deviceName}' -> Pedals");
            return "Pedals";
        }

        // Default to joystick
        System.Diagnostics.Debug.WriteLine($"DetectDeviceType: '{deviceName}' -> Joystick (default)");
        return "Joystick";
    }

    private SKSvg? GetActiveSvg() => GetSvgForDeviceMap(_deviceMap);

    private SKBitmap? GetActiveBitmap() => GetBitmapForDeviceMap(_deviceMap);

}