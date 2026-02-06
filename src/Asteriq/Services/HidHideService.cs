using System.Diagnostics;
using System.Text.Json;
using Asteriq.Models;
using Asteriq.Services.Abstractions;

namespace Asteriq.Services;

/// <summary>
/// Information about a HID device from HidHide
/// </summary>
public class HidHideDeviceInfo
{
    public string DeviceInstancePath { get; init; } = "";
    public string SymbolicLink { get; init; } = "";
    public string Vendor { get; init; } = "";
    public string Product { get; init; } = "";
    public string SerialNumber { get; init; } = "";
    public string Usage { get; init; } = "";
    public string Description { get; init; } = "";
    public bool IsPresent { get; init; }
    public bool IsGamingDevice { get; init; }
    public string BaseContainerDeviceInstancePath { get; init; } = "";

    /// <summary>
    /// Friendly display name combining vendor and product
    /// </summary>
    public string FriendlyName => $"{Vendor} {Product}".Trim();
}

/// <summary>
/// Group of HID devices sharing a friendly name (e.g., same physical device with multiple interfaces)
/// </summary>
public class HidHideDeviceGroup
{
    public string FriendlyName { get; init; } = "";
    public List<HidHideDeviceInfo> Devices { get; init; } = new();
}

/// <summary>
/// Service for managing HidHide device hiding
/// </summary>
public class HidHideService : IHidHideService
{
    private readonly string _cliPath;

    /// <summary>
    /// Creates a new HidHideService with default CLI path
    /// </summary>
    public HidHideService()
        : this(new HidHideSettings())
    {
    }

    /// <summary>
    /// Creates a new HidHideService with the specified settings
    /// </summary>
    public HidHideService(HidHideSettings settings)
    {
        _cliPath = settings.CliPath;
    }

    /// <summary>
    /// Creates a new HidHideService with a custom CLI path
    /// </summary>
    public HidHideService(string cliPath)
    {
        _cliPath = cliPath;
    }

    /// <summary>
    /// Check if HidHide CLI is available
    /// </summary>
    public bool IsAvailable()
    {
        return File.Exists(_cliPath);
    }

    /// <summary>
    /// Get all gaming HID devices
    /// </summary>
    public List<HidHideDeviceGroup> GetGamingDevices()
    {
        var output = RunCommand("--dev-gaming");
        return ParseDeviceJson(output);
    }

    /// <summary>
    /// Get all HID devices
    /// </summary>
    public List<HidHideDeviceGroup> GetAllDevices()
    {
        var output = RunCommand("--dev-all");
        return ParseDeviceJson(output);
    }

    /// <summary>
    /// Get list of hidden device instance paths
    /// </summary>
    public List<string> GetHiddenDevices()
    {
        var output = RunCommand("--dev-list");
        var paths = new List<string>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // Format: --dev-hide "HID\VID_3344&PID_80D4&MI_00\..."
            var trimmed = line.Trim();
            if (trimmed.StartsWith("--dev-hide"))
            {
                var start = trimmed.IndexOf('"');
                var end = trimmed.LastIndexOf('"');
                if (start >= 0 && end > start)
                {
                    paths.Add(trimmed.Substring(start + 1, end - start - 1));
                }
            }
        }

        return paths;
    }

    /// <summary>
    /// Hide a device by its instance path
    /// </summary>
    public bool HideDevice(string deviceInstancePath)
    {
        var result = RunCommand($"--dev-hide \"{deviceInstancePath}\"");
        return !result.Contains("error", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Unhide a device by its instance path
    /// </summary>
    public bool UnhideDevice(string deviceInstancePath)
    {
        var result = RunCommand($"--dev-unhide \"{deviceInstancePath}\"");
        return !result.Contains("error", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Get current cloaking state
    /// </summary>
    public bool IsCloakingEnabled()
    {
        var output = RunCommand("--cloak-state");
        return output.Contains("--cloak-on", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Enable cloaking (hiding becomes active)
    /// </summary>
    public bool EnableCloaking()
    {
        RunCommand("--cloak-on");
        return IsCloakingEnabled();
    }

    /// <summary>
    /// Disable cloaking (all devices visible)
    /// </summary>
    public bool DisableCloaking()
    {
        RunCommand("--cloak-off");
        return !IsCloakingEnabled();
    }

    /// <summary>
    /// Check if inverse application cloak is enabled.
    /// In inverse mode, whitelisted apps are BLOCKED from seeing hidden devices.
    /// In normal mode, whitelisted apps CAN see hidden devices.
    /// </summary>
    public bool IsInverseMode()
    {
        var output = RunCommand("--inv-state");
        return output.Contains("--inv-on", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Enable inverse application cloak mode
    /// </summary>
    public bool EnableInverseMode()
    {
        RunCommand("--inv-on");
        return IsInverseMode();
    }

    /// <summary>
    /// Disable inverse application cloak mode
    /// </summary>
    public bool DisableInverseMode()
    {
        RunCommand("--inv-off");
        return !IsInverseMode();
    }

    /// <summary>
    /// Get list of whitelisted application paths
    /// </summary>
    public List<string> GetWhitelistedApps()
    {
        var output = RunCommand("--app-list");
        var apps = new List<string>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // Format: --app-reg "C:\Path\To\App.exe"
            var trimmed = line.Trim();
            if (trimmed.StartsWith("--app-reg"))
            {
                var start = trimmed.IndexOf('"');
                var end = trimmed.LastIndexOf('"');
                if (start >= 0 && end > start)
                {
                    apps.Add(trimmed.Substring(start + 1, end - start - 1));
                }
            }
        }

        return apps;
    }

    /// <summary>
    /// Add an application to the whitelist (can see hidden devices)
    /// </summary>
    public bool WhitelistApp(string appPath)
    {
        var result = RunCommand($"--app-reg \"{appPath}\"");
        return !result.Contains("error", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Remove an application from the whitelist
    /// </summary>
    public bool UnwhitelistApp(string appPath)
    {
        var result = RunCommand($"--app-unreg \"{appPath}\"");
        return !result.Contains("error", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensure Asteriq can see hidden devices based on current mode.
    /// In normal mode: adds to whitelist.
    /// In inverse mode: removes from whitelist.
    /// </summary>
    public bool EnsureSelfCanSeeDevices()
    {
        var selfPath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(selfPath))
            return false;

        var whitelisted = GetWhitelistedApps();
        bool isWhitelisted = whitelisted.Any(w => string.Equals(w, selfPath, StringComparison.OrdinalIgnoreCase));
        bool isInverse = IsInverseMode();

        if (isInverse)
        {
            // In inverse mode, whitelisted apps are BLOCKED
            // So we need to REMOVE ourselves from whitelist to see devices
            if (isWhitelisted)
            {
                return UnwhitelistApp(selfPath);
            }
            return true; // Already not whitelisted, can see devices
        }
        else
        {
            // In normal mode, whitelisted apps CAN see hidden devices
            // So we need to ADD ourselves to whitelist
            if (!isWhitelisted)
            {
                return WhitelistApp(selfPath);
            }
            return true; // Already whitelisted
        }
    }

    /// <summary>
    /// Ensure Asteriq is whitelisted so it can see hidden devices.
    /// DEPRECATED: Use EnsureSelfCanSeeDevices() which handles inverse mode.
    /// </summary>
    public bool EnsureSelfWhitelisted()
    {
        var selfPath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(selfPath))
            return false;

        var whitelisted = GetWhitelistedApps();
        if (!whitelisted.Any(w => string.Equals(w, selfPath, StringComparison.OrdinalIgnoreCase)))
        {
            return WhitelistApp(selfPath);
        }
        return true;
    }

    /// <summary>
    /// Hide all gaming devices except vJoy devices
    /// </summary>
    public int HideAllPhysicalDevices()
    {
        int hiddenCount = 0;
        var devices = GetGamingDevices();

        foreach (var group in devices)
        {
            // Skip vJoy devices
            if (group.FriendlyName.Contains("vJoy", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var device in group.Devices)
            {
                if (device.IsGamingDevice && HideDevice(device.DeviceInstancePath))
                {
                    hiddenCount++;
                    Console.WriteLine($"Hidden: {device.DeviceInstancePath}");
                }
            }
        }

        return hiddenCount;
    }

    /// <summary>
    /// Unhide all currently hidden devices
    /// </summary>
    public int UnhideAllDevices()
    {
        int unhiddenCount = 0;
        var hidden = GetHiddenDevices();

        foreach (var path in hidden)
        {
            if (UnhideDevice(path))
            {
                unhiddenCount++;
                Console.WriteLine($"Unhidden: {path}");
            }
        }

        return unhiddenCount;
    }

    private string RunCommand(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _cliPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return "";

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return output + error;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"HidHide CLI command failed. Command: '{arguments}', " +
                              $"CLI path: '{_cliPath}', Error type: {ex.GetType().Name}, Details: {ex.Message}");
            return "";
        }
    }

    private List<HidHideDeviceGroup> ParseDeviceJson(string json)
    {
        var groups = new List<HidHideDeviceGroup>();

        if (string.IsNullOrWhiteSpace(json))
            return groups;

        try
        {
            using var doc = JsonDocument.Parse(json);

            foreach (var groupElement in doc.RootElement.EnumerateArray())
            {
                var friendlyName = groupElement.GetProperty("friendlyName").GetString() ?? "";
                var devices = new List<HidHideDeviceInfo>();

                foreach (var deviceElement in groupElement.GetProperty("devices").EnumerateArray())
                {
                    devices.Add(new HidHideDeviceInfo
                    {
                        DeviceInstancePath = deviceElement.GetProperty("deviceInstancePath").GetString() ?? "",
                        SymbolicLink = deviceElement.GetProperty("symbolicLink").GetString() ?? "",
                        Vendor = deviceElement.GetProperty("vendor").GetString() ?? "",
                        Product = deviceElement.GetProperty("product").GetString() ?? "",
                        SerialNumber = deviceElement.GetProperty("serialNumber").GetString() ?? "",
                        Usage = deviceElement.GetProperty("usage").GetString() ?? "",
                        Description = deviceElement.GetProperty("description").GetString() ?? "",
                        IsPresent = deviceElement.GetProperty("present").GetBoolean(),
                        IsGamingDevice = deviceElement.GetProperty("gamingDevice").GetBoolean(),
                        BaseContainerDeviceInstancePath = deviceElement.GetProperty("baseContainerDeviceInstancePath").GetString() ?? ""
                    });
                }

                groups.Add(new HidHideDeviceGroup
                {
                    FriendlyName = friendlyName,
                    Devices = devices
                });
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to parse HidHide JSON: {ex.Message}");
        }

        return groups;
    }
}
