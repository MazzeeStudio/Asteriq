using System.Text.RegularExpressions;
using Asteriq.Models;
using Asteriq.Services;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class DevicesTabController
{
    private void ToggleHideFromView()
    {
        if (_ctx.SelectedDevice < 0 || _ctx.SelectedDevice >= _ctx.Devices.Count) return;
        var device = _ctx.Devices[_ctx.SelectedDevice];
        string guid = device.InstanceGuid.ToString();
        bool currentlyHidden = _ctx.AppSettings.IsDeviceHidden(guid);

        if (currentlyHidden)
        {
            _ctx.AppSettings.SetDeviceHidden(guid, false);
            _pendingHideGuid = null;
        }
        else
        {
            // Mark pending — device stays visible in both D1 and D3 until delay elapses
            _pendingHideGuid = guid;
            _pendingHideTicks = Environment.TickCount64;
        }
        _ctx.MarkDirty();
    }

    /// <summary>
    /// Ensures a visible physical device is selected whenever the visible list is non-empty.
    /// Clears selection only when the visible list is genuinely empty.
    /// </summary>
    private void EnsureVisibleDeviceSelected()
    {
        var visible = _ctx.Devices
            .Where(d => !d.IsVirtual &&
                (_showHiddenDevices || !_ctx.AppSettings.IsDeviceHidden(d.InstanceGuid.ToString())))
            .ToList();

        if (visible.Count == 0)
        {
            _ctx.SelectedDevice = -1;
            return;
        }

        // If current selection is still in the visible list, keep it
        if (_ctx.SelectedDevice >= 0 && _ctx.SelectedDevice < _ctx.Devices.Count)
        {
            var current = _ctx.Devices[_ctx.SelectedDevice];
            if (visible.Contains(current)) return;
        }

        // Otherwise pick the first visible device
        var first = visible[0];
        _ctx.SelectedDevice = _ctx.Devices.IndexOf(first);
        _ctx.LoadDeviceMapForDevice(first);
    }

    /// <summary>
    /// Toggles HidHide driver hiding for the given physical device.
    /// Called from both the Devices tab and the Settings → HidHide panel via TabContext callback.
    /// When hiding: enables cloaking, ensures Asteriq can see devices, whitelists SC executables.
    /// </summary>
    private void ToggleHidHideForDevice(PhysicalDeviceInfo device)
    {
        if (_ctx.HidHide is null || _ctx.DeviceMatching is null) return;
        var (vid, pid) = DeviceMatchingService.ExtractVidPidFromSdlGuid(device.InstanceGuid);
        var vidPidPattern = vid > 0 ? $"VID_{vid:X4}&PID_{pid:X4}" : null;

        // Determine current state directly from the service (no cached state needed)
        var hiddenPaths = _ctx.HidHide.GetHiddenDevices();
        bool isCurrentlyHidden = vidPidPattern is not null &&
            hiddenPaths.Any(p => p.Contains(vidPidPattern, StringComparison.OrdinalIgnoreCase));

        if (isCurrentlyHidden)
        {
            var toUnhide = vidPidPattern is not null
                ? hiddenPaths.Where(p => p.Contains(vidPidPattern, StringComparison.OrdinalIgnoreCase)).ToList()
                : new List<string>();
            foreach (var path in toUnhide)
                _ctx.HidHide.UnhideDevice(path);
            UpdateSCWhitelist(isHiding: false);
        }
        else
        {
            var matches = _ctx.DeviceMatching.FindMatchingHidDevices(device);
            if (matches.Count == 0) return;
            foreach (var match in matches)
                _ctx.HidHide.HideDevice(match.DeviceInstancePath);
            if (!_ctx.HidHide.IsCloakingEnabled())
                _ctx.HidHide.EnableCloaking();
            _ctx.HidHide.EnsureSelfCanSeeDevices();
            UpdateSCWhitelist(isHiding: true);
        }

        _ctx.MarkDirty();
    }

    /// <summary>
    /// Updates the HidHide whitelist for all detected SC game executables based on the current mode.
    ///
    /// Normal mode  — whitelisted apps CAN see hidden devices.
    ///   Hiding:   REMOVE SC from whitelist so it cannot see the hidden physical device.
    ///   Unhiding: no-op (SC was never whitelisted; devices visible to all again).
    ///
    /// Inverse mode — whitelisted apps are BLOCKED from hidden devices.
    ///   Hiding:   ADD SC to whitelist so it is blocked from the hidden physical device.
    ///   Unhiding: REMOVE SC from whitelist (no longer need to block it).
    /// </summary>
    private void UpdateSCWhitelist(bool isHiding)
    {
        if (_ctx.HidHide is null || _ctx.SCInstallation is null) return;

        // On unhide: only modify the SC whitelist if no hidden devices remain.
        // If other joysticks are still hidden, the SC restriction must stay in place.
        if (!isHiding && _ctx.HidHide.GetHiddenDevices().Count > 0) return;

        bool isInverse = _ctx.HidHide.IsInverseMode();

        foreach (var installation in _ctx.SCInstallation.Installations)
        {
            var exePath = Path.Combine(installation.InstallPath, "Bin64", "StarCitizen.exe");
            if (!File.Exists(exePath)) continue;

            if (isInverse)
            {
                // Inverse: whitelist = blocked. Add on hide so SC can't see device; remove on unhide.
                if (isHiding) _ctx.HidHide.WhitelistApp(exePath);
                else _ctx.HidHide.UnwhitelistApp(exePath);
            }
            else
            {
                // Normal: whitelist = allowed. Remove SC so it can't bypass the hide.
                // On unhide there's nothing to undo — SC was never in the whitelist.
                if (isHiding) _ctx.HidHide.UnwhitelistApp(exePath);
            }
        }
    }

    /// <summary>
    /// Removes a vJoy device using vJoyConfig.exe CLI, then refreshes the device lists.
    /// </summary>
}