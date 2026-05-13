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
    private void RefreshVJoyDevicesInternal()
    {
        // vJoyConfig.exe reconfigures the driver, invalidating all kernel handles.
        // Release stale acquisitions before re-enumerating so the engine can
        // cleanly re-acquire devices when forwarding restarts.
        bool wasForwarding = _isForwarding;
        if (wasForwarding)
            StopForwarding();

        _vjoyService.ReleaseAllDevices();
        _vjoyDevices = _vjoyService.EnumerateDevices();

        // Immediately sync to context so callers can read the updated list before the next SyncTabContext()
        if (_tabContext is not null)
            _tabContext.VJoyDevices = _vjoyDevices;

        // Clean up mappings that reference axes no longer present on vJoy devices
        CleanupStaleMappings();

        // Restart forwarding if it was active â€” the engine will re-acquire devices
        if (wasForwarding)
            StartForwarding();

        // NOTE: callers that add a device should explicitly call RefreshDevices() afterwards
        // so the new SDL2 virtual joystick appears in the Devices tab.
        // Callers that remove a device should NOT call RefreshDevices() immediately,
        // because SDL2 still reports the device until the OS sends a device-removed notification.
        _canvas.Invalidate();
    }

    private void CleanupStaleMappings()
    {
        var profile = _profileManager.ActiveProfile;
        if (profile is null) return;

        bool changed = false;
        foreach (var vjoy in _vjoyDevices)
        {
            var validAxes = new HashSet<int>();
            if (vjoy.HasAxisX) validAxes.Add(0);
            if (vjoy.HasAxisY) validAxes.Add(1);
            if (vjoy.HasAxisZ) validAxes.Add(2);
            if (vjoy.HasAxisRX) validAxes.Add(3);
            if (vjoy.HasAxisRY) validAxes.Add(4);
            if (vjoy.HasAxisRZ) validAxes.Add(5);
            if (vjoy.HasSlider0) validAxes.Add(6);
            if (vjoy.HasSlider1) validAxes.Add(7);

            changed |= profile.AxisMappings.RemoveAll(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoy.Id &&
                !validAxes.Contains(m.Output.Index)) > 0;

            changed |= profile.AxisToButtonMappings.RemoveAll(m =>
                m.SourceVJoyDevice == vjoy.Id &&
                !validAxes.Contains(m.SourceAxisIndex)) > 0;
        }

        if (changed)
        {
            profile.ModifiedAt = DateTime.UtcNow;
            _profileManager.SaveActiveProfile();
        }
    }

    private void RefreshDevices()
    {
        var connectedDevices = _inputService.EnumerateDevices();

        // Mark all connected devices
        foreach (var device in connectedDevices)
        {
            device.IsConnected = true;
        }

        // Add disconnected devices that aren't currently connected
        // Only show disconnected physical devices (not virtual)
        var disconnectedToShow = _disconnectedDevices
            .Where(d => !d.IsVirtual && !connectedDevices.Any(c =>
                c.InstanceGuid == d.InstanceGuid ||
                (c.Name == d.Name && c.AxisCount == d.AxisCount && c.ButtonCount == d.ButtonCount)))
            .ToList();

        // Combine connected and disconnected devices
        _devices = connectedDevices.Concat(disconnectedToShow).ToList();

        // Apply user-defined device order from profile
        ApplyDeviceOrder();

        // Auto-select first device in current category if nothing selected
        if (_selectedDevice < 0 && _devices.Count > 0)
        {
            SelectFirstDeviceInCategory();
        }
    }

    private void SelectFirstDeviceInCategory()
    {
        // Use the controller's active device category (0 = physical, 1 = virtual)
        bool selectVirtual = _devicesController is not null && _devicesController.DeviceCategory == 1;
        var filteredDevices = _devices.Where(d => d.IsVirtual == selectVirtual).ToList();

        if (filteredDevices.Count > 0)
        {
            _selectedDevice = _devices.IndexOf(filteredDevices[0]);
            // Keep context in sync so SyncFromTabContext doesn't clobber the new selection
            if (_tabContext is not null)
                _tabContext.SelectedDevice = _selectedDevice;
            if (_selectedDevice >= 0)
            {
                LoadDeviceMapForDevice(_devices[_selectedDevice]);
            }
        }
    }

    private void OnInputReceived(object? sender, DeviceInputState state)
    {
        // â”€â”€ Network switch button detection (highest priority, rising edge) â”€â”€
        var switchCfg = _profileManager.ActiveProfile?.NetworkSwitchButton;
        if (_appSettings.NetworkEnabled && switchCfg is not null)
        {
            bool buttonPressed = state.DeviceIndex == switchCfg.DeviceIndex
                && switchCfg.ButtonIndex < state.Buttons.Length
                && state.Buttons[switchCfg.ButtonIndex];

            if (buttonPressed && !_lastSwitchButtonState)
            {
                // Rising edge â€” debounce then cycle through peers
                var nowTick = Environment.TickCount64;
                if (nowTick - _lastSwitchButtonTick < SwitchDebounceMs)
                {
                    _logger.LogDebug("[NetToggle] Rising edge DEBOUNCED ({Ms}ms since last)", nowTick - _lastSwitchButtonTick);
                }
                else
                {
                _lastSwitchButtonTick = nowTick;
                var peers = _networkDiscovery.KnownPeers.Values.ToList();
                _logger.LogDebug("[NetToggle] Rising edge | mode={Mode} connectedIp={ConnectedIp} peers={PeerCount} connecting={Connecting}",
                    _networkMode, _tabContext.ConnectedPeerIp ?? "none", peers.Count, _isNetworkConnecting);

                if (_networkMode == NetworkInputMode.Local)
                {
                    if (peers.Count > 0)
                    {
                        _logger.LogDebug("[NetToggle] Disconnected â†’ connecting to peers[0]={Peer}", peers[0].IpAddress);
                        _ = ConnectAsMasterAsync(peers[0]);
                    }
                    else
                    {
                        _logger.LogDebug("[NetToggle] No peers discovered, ignoring");
                    }
                }
                else
                {
                    int cur = peers.FindIndex(p => p.IpAddress == _tabContext.ConnectedPeerIp);
                    int next = cur + 1;
                    _logger.LogDebug("[NetToggle] Connected | curIdx={Cur} nextIdx={Next} peerCount={Count}", cur, next, peers.Count);
                    if (next < peers.Count)
                    {
                        _logger.LogDebug("[NetToggle] Switching â†’ peers[{Next}]={Peer}", next, peers[next].IpAddress);
                        _ = ConnectAsMasterAsync(peers[next]);
                    }
                    else
                    {
                        _logger.LogDebug("[NetToggle] Last peer reached â†’ disconnecting");
                        _ = SwitchToLocalAsync();
                    }
                }
                } // end debounce else
            }
            _lastSwitchButtonState = buttonPressed;
            // Do NOT return â€” input must still reach the forwarding / local-vJoy path below.
        }

        // â”€â”€ Master mode: run MappingEngine in capture mode, send snapshot â”€â”€â”€â”€
        // ForwardingMode is set exclusively by ConnectAsMasterAsync â€” no role setting required.
        // SuppressForwarding is true while SC Bindings button-capture is active â€” skip ProcessInput
        // entirely so the captured button press never reaches the snapshot or the remote machine.
        if (_networkMode == NetworkInputMode.Remote && _networkVjoy.ForwardingMode)
        {
            if (_mappingEngine.IsRunning && !_tabContext.SuppressForwarding)
                _mappingEngine.ProcessInput(state);

            // Do NOT send here â€” the 20 Hz heartbeat handles transmission.
            // Sending on every SDL2 event would flood the connection with joystick noise.
            return;
        }

        // â”€â”€ Local forwarding â€” process through MappingEngine â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (_isForwarding && _mappingEngine.IsRunning && !_tabContext.SuppressForwarding)
        {
            _mappingEngine.ProcessInput(state);
        }

        if (_selectedDevice >= 0 && _selectedDevice < _devices.Count &&
            state.DeviceIndex == _devices[_selectedDevice].DeviceIndex)
        {
            _currentInputState = state;

            // Track input activity for dynamic lead-lines
            TrackInputActivity(state);
        }

        // Check for button presses to highlight corresponding mapping in Mappings tab
        if (_activeTab == 1 && _profileManager.ActiveProfile is not null)
        {
            int prevHighlightRow = _mappingsController.HighlightedMappingRow;
            uint prevHighlightDevice = _mappingsController.HighlightedVJoyDevice;

            _mappingsController.CheckForMappingHighlight(state);

            // Invalidate canvas if highlight changed to show the shimmer effect
            if (_mappingsController.HighlightedMappingRow != prevHighlightRow ||
                _mappingsController.HighlightedVJoyDevice != prevHighlightDevice)
            {
                MarkDirty();
            }
        }
    }

    private void TrackInputActivity(DeviceInputState state)
    {
        if (_deviceMap is null) return;

        // Track axis changes
        for (int i = 0; i < state.Axes.Length; i++)
        {
            string binding = GetAxisBindingName(i);
            var control = _deviceMap.FindControlByBinding(binding);
            _activeInputTracker.Update(binding, state.Axes[i], isAxis: true, control);
        }

        // Track button changes
        for (int i = 0; i < state.Buttons.Length; i++)
        {
            string binding = $"button{i + 1}";
            var control = _deviceMap.FindControlByBinding(binding);
            _activeInputTracker.Update(binding, state.Buttons[i] ? 1f : 0f, isAxis: false, control);
        }
    }

    private static string GetAxisBindingName(int axisIndex)
    {
        return axisIndex switch
        {
            0 => "x",
            1 => "y",
            2 => "z",
            3 => "rx",
            4 => "ry",
            5 => "rz",
            6 => "slider1",
            7 => "slider2",
            _ => $"axis{axisIndex}"
        };
    }

    private void OnDeviceConnected(object? sender, PhysicalDeviceInfo newDevice)
    {
        BeginInvoke(() =>
        {
            // Remember currently selected device by identity
            Guid? selectedGuid = null;
            string? selectedName = null;
            if (_selectedDevice >= 0 && _selectedDevice < _devices.Count)
            {
                selectedGuid = _devices[_selectedDevice].InstanceGuid;
                selectedName = _devices[_selectedDevice].Name;
            }

            // Check if this device was previously disconnected
            var disconnected = _disconnectedDevices.FirstOrDefault(d =>
                d.InstanceGuid == newDevice.InstanceGuid ||
                (d.Name == newDevice.Name && d.AxisCount == newDevice.AxisCount && d.ButtonCount == newDevice.ButtonCount));

            if (disconnected is not null)
            {
                // Device reconnected - remove from disconnected list
                _disconnectedDevices.Remove(disconnected);
                SaveDisconnectedDevices();
            }

            RefreshDevices();

            // Restore selection by identity
            RestoreDeviceSelection(selectedGuid, selectedName);

            // First physical device connected â€” bump poll rate for responsive input
            if (_networkMode != NetworkInputMode.Receiving)
            {
                int physCount = _devices.Count(d => !d.IsVirtual && d.IsConnected);
                if (physCount > 0)
                    _inputService.SetPollRate(500);
            }

            MarkDirty();
        });
    }

    private void OnDeviceDisconnected(object? sender, int deviceIndex)
    {
        BeginInvoke(() =>
        {
            // Remember currently selected device by identity
            Guid? selectedGuid = null;
            string? selectedName = null;
            if (_selectedDevice >= 0 && _selectedDevice < _devices.Count)
            {
                selectedGuid = _devices[_selectedDevice].InstanceGuid;
                selectedName = _devices[_selectedDevice].Name;
            }

            // Find the device that was disconnected before we refresh
            var disconnectedDevice = _devices.FirstOrDefault(d => d.DeviceIndex == deviceIndex);

            if (disconnectedDevice is not null && !disconnectedDevice.IsVirtual)
            {
                // Always track physical devices when they disconnect
                // Mark as disconnected and add to tracked list
                disconnectedDevice.IsConnected = false;
                disconnectedDevice.DeviceIndex = -1; // No longer valid

                // Check if we already track this device
                if (!_disconnectedDevices.Any(d => d.InstanceGuid == disconnectedDevice.InstanceGuid))
                {
                    _disconnectedDevices.Add(disconnectedDevice);
                    SaveDisconnectedDevices();
                }
            }

            RefreshDevices();

            // Restore selection by identity
            RestoreDeviceSelection(selectedGuid, selectedName);

            // All physical devices gone â€” drop to low-rate hot-plug detection
            if (_networkMode != NetworkInputMode.Receiving)
            {
                int physCount = _devices.Count(d => !d.IsVirtual && d.IsConnected);
                if (physCount == 0)
                    _inputService.SetPollRate(10);
            }

            MarkDirty();
        });
    }

    private void RestoreDeviceSelection(Guid? selectedGuid, string? selectedName)
    {
        if (selectedGuid is null && selectedName is null)
            return;

        // Try to find the device by GUID first, then by name
        int newIndex = -1;
        for (int i = 0; i < _devices.Count; i++)
        {
            if (_devices[i].InstanceGuid == selectedGuid ||
                (selectedName is not null && _devices[i].Name == selectedName))
            {
                newIndex = i;
                break;
            }
        }

        if (newIndex >= 0)
        {
            _selectedDevice = newIndex;
        }
        else if (_selectedDevice >= _devices.Count)
        {
            _selectedDevice = Math.Max(0, _devices.Count - 1);
        }

        // Load device map for the selected device
        if (_selectedDevice >= 0 && _selectedDevice < _devices.Count)
        {
            LoadDeviceMapForDevice(_devices[_selectedDevice]);
        }
    }

}