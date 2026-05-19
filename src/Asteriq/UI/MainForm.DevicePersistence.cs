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
    private void LoadDisconnectedDevices()
    {
        // Load disconnected devices from settings
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Asteriq", "disconnected_devices.json");

        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var devices = System.Text.Json.JsonSerializer.Deserialize<List<DisconnectedDeviceInfo>>(json);
                if (devices is not null)
                {
                    _disconnectedDevices = devices.Select(d => new PhysicalDeviceInfo
                    {
                        DeviceIndex = -1,
                        Name = d.Name,
                        InstanceGuid = d.InstanceGuid,
                        AxisCount = d.AxisCount,
                        ButtonCount = d.ButtonCount,
                        HatCount = d.HatCount,
                        IsVirtual = false,
                        IsConnected = false
                    }).ToList();
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // Ignore errors loading disconnected devices
            }
        }
    }

    private void SaveDisconnectedDevices()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Asteriq", "disconnected_devices.json");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var devices = _disconnectedDevices.Select(d => new DisconnectedDeviceInfo
            {
                Name = d.Name,
                InstanceGuid = d.InstanceGuid,
                AxisCount = d.AxisCount,
                ButtonCount = d.ButtonCount,
                HatCount = d.HatCount
            }).ToList();

            var json = System.Text.Json.JsonSerializer.Serialize(devices,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Ignore errors saving disconnected devices
        }
    }

    private sealed record DisconnectedDeviceInfo
    {
        public string Name { get; init; } = string.Empty;
        public Guid InstanceGuid { get; init; }
        public int AxisCount { get; init; }
        public int ButtonCount { get; init; }
        public int HatCount { get; init; }
    }



    /// <summary>
    /// Save the current device order to the active profile and reassign vJoy slots
    /// Device order = vJoy order (first device -> vJoy 1, second -> vJoy 2, etc.)
    /// </summary>
    private void SaveDeviceOrder()
    {
        if (_profileManager.ActiveProfile is null)
            return;

        var profile = _profileManager.ActiveProfile;

        // Get physical devices only (that's what we reorder)
        var physicalDevices = _devices.Where(d => !d.IsVirtual).ToList();

        // Save the order as list of instance GUIDs
        profile.DeviceOrder = physicalDevices
            .Select(d => d.InstanceGuid.ToString())
            .ToList();

        // Build mapping from device GUID to old assignment info
        var oldAssignmentsByGuid = new Dictionary<string, DeviceAssignment>();
        foreach (var assignment in profile.DeviceAssignments)
        {
            oldAssignmentsByGuid[assignment.PhysicalDevice.Guid] = assignment;
        }

        // Build mapping from old vJoy ID to new vJoy ID based on new order
        var oldToNewVJoy = new Dictionary<uint, uint>();

        // Clear and rebuild device assignments based on new order
        profile.DeviceAssignments.Clear();
        for (int i = 0; i < physicalDevices.Count; i++)
        {
            var device = physicalDevices[i];
            uint newVJoyId = (uint)(i + 1); // vJoy is 1-indexed
            string deviceGuid = device.InstanceGuid.ToString();

            // Track old -> new mapping for updating mappings
            if (oldAssignmentsByGuid.TryGetValue(deviceGuid, out var oldAssignment))
            {
                oldToNewVJoy[oldAssignment.VJoyDevice] = newVJoyId;
            }

            // Get existing VidPid if available from old assignments
            string vidPid = oldAssignment?.PhysicalDevice.VidPid ?? "";

            // Create new assignment
            profile.DeviceAssignments.Add(new DeviceAssignment
            {
                PhysicalDevice = new PhysicalDeviceRef
                {
                    Name = device.Name,
                    Guid = deviceGuid,
                    VidPid = vidPid
                },
                VJoyDevice = newVJoyId
            });
        }

        // Update all mappings to use new vJoy IDs
        foreach (var mapping in profile.AxisMappings)
        {
            if (oldToNewVJoy.TryGetValue(mapping.Output.VJoyDevice, out uint newId))
            {
                mapping.Output.VJoyDevice = newId;
            }
        }
        foreach (var mapping in profile.ButtonMappings)
        {
            if (oldToNewVJoy.TryGetValue(mapping.Output.VJoyDevice, out uint newId))
            {
                mapping.Output.VJoyDevice = newId;
            }
        }
        foreach (var mapping in profile.HatMappings)
        {
            if (oldToNewVJoy.TryGetValue(mapping.Output.VJoyDevice, out uint newId))
            {
                mapping.Output.VJoyDevice = newId;
            }
        }
        foreach (var mapping in profile.AxisToButtonMappings)
        {
            if (oldToNewVJoy.TryGetValue(mapping.Output.VJoyDevice, out uint newId))
            {
                mapping.Output.VJoyDevice = newId;
            }
        }
        foreach (var mapping in profile.ButtonToAxisMappings)
        {
            if (oldToNewVJoy.TryGetValue(mapping.Output.VJoyDevice, out uint newId))
            {
                mapping.Output.VJoyDevice = newId;
            }
        }

        _profileManager.SaveActiveProfile();
        OnMappingsChanged();

        Console.WriteLine($"Device order saved. vJoy assignments updated:");
        for (int i = 0; i < physicalDevices.Count; i++)
        {
            Console.WriteLine($"  {i + 1}: {physicalDevices[i].Name} -> vJoy {i + 1}");
        }
    }

    /// <summary>
    /// Apply saved device order from the active profile
    /// </summary>
    private void ApplyDeviceOrder()
    {
        if (_profileManager.ActiveProfile is null)
            return;

        var savedOrder = _profileManager.ActiveProfile.DeviceOrder;
        if (savedOrder is null || savedOrder.Count == 0)
            return;

        // Separate physical and virtual devices
        var physicalDevices = _devices.Where(d => !d.IsVirtual).ToList();
        var virtualDevices = _devices.Where(d => d.IsVirtual).ToList();

        // Sort physical devices by saved order
        var orderedPhysical = new List<PhysicalDeviceInfo>();
        var unorderedPhysical = new List<PhysicalDeviceInfo>(physicalDevices);

        foreach (var guid in savedOrder)
        {
            var device = unorderedPhysical.FirstOrDefault(d =>
                d.InstanceGuid.ToString().Equals(guid, StringComparison.OrdinalIgnoreCase));
            if (device is not null)
            {
                orderedPhysical.Add(device);
                unorderedPhysical.Remove(device);
            }
        }

        // Add any new devices (not in saved order) at the end
        orderedPhysical.AddRange(unorderedPhysical);

        // Rebuild devices list with physical first, then virtual
        _devices = orderedPhysical.Concat(virtualDevices).ToList();
    }



}