using System.Text.RegularExpressions;
using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class DevicesTabController
{
    private void StartForwarding()
    {
        if (_ctx.IsForwarding) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null)
        {
            FUIMessageBox.ShowWarning(_ctx.OwnerForm,
                "No active configuration found.\n\nTo create mappings:\n1. Select a physical device\n2. Click 'MAP 1:1 TO VJOY'",
                "Cannot Start Forwarding");
            return;
        }

        if (profile.AxisMappings.Count == 0 && profile.ButtonMappings.Count == 0 && profile.HatMappings.Count == 0)
        {
            FUIMessageBox.ShowWarning(_ctx.OwnerForm,
                $"Configuration '{profile.Name}' has no mappings.\n\nTo create mappings:\n1. Select a physical device\n2. Click 'MAP 1:1 TO VJOY'",
                "Cannot Start Forwarding");
            return;
        }

        _ctx.MappingEngine.LoadProfile(profile);

        if (!_ctx.VJoyService.IsInitialized)
        {
            FUIMessageBox.ShowError(_ctx.OwnerForm,
                "vJoy driver is not initialized.\n\nPlease ensure vJoy is installed correctly.",
                "Cannot Start Forwarding");
            return;
        }

        var requiredDevices = profile.AxisMappings
            .Select(m => m.Output.VJoyDevice)
            .Concat(profile.ButtonMappings.Select(m => m.Output.VJoyDevice))
            .Concat(profile.HatMappings.Select(m => m.Output.VJoyDevice))
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        foreach (var deviceId in requiredDevices)
        {
            var info = _ctx.VJoyService.GetDeviceInfo(deviceId);
            if (!info.Exists)
            {
                FUIMessageBox.ShowError(_ctx.OwnerForm,
                    $"vJoy device {deviceId} does not exist.\n\nPlease configure vJoy device {deviceId} using 'Configure vJoy'.",
                    "Cannot Start Forwarding");
                return;
            }
        }

        if (!_ctx.MappingEngine.Start())
        {
            int ourPid = Environment.ProcessId;
            var statusMessages = requiredDevices
                .Select(id => {
                    var info = _ctx.VJoyService.GetDeviceInfo(id);
                    int ownerPid = VJoy.VJoyInterop.GetOwnerPid(id);
                    return $"vJoy {id}: {info.Status} (Owner PID: {ownerPid}, Our PID: {ourPid})";
                })
                .ToList();

            FUIMessageBox.ShowError(_ctx.OwnerForm,
                $"Failed to acquire vJoy device(s).\n\nDevice status:\n{string.Join("\n", statusMessages)}\n\nIf Owner PID matches Our PID, try restarting the app.\nIf different, another app owns the device.",
                "Cannot Start Forwarding");
            return;
        }

        _ctx.IsForwarding = true;
        _ctx.TrayIcon.SetActive(true);
        _ctx.UpdateTrayMenu?.Invoke();
        System.Diagnostics.Debug.WriteLine($"Started forwarding with profile: {profile.Name}");
    }

    private void StopForwarding()
    {
        if (!_ctx.IsForwarding) return;

        _ctx.MappingEngine.Stop();
        _ctx.IsForwarding = false;
        _ctx.TrayIcon.SetActive(false);
        _ctx.UpdateTrayMenu?.Invoke();
        System.Diagnostics.Debug.WriteLine("Stopped forwarding");
    }

}