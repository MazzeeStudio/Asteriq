using HidSharp;
using HidSharp.Reports;
using HidSharp.Reports.Input;
using Asteriq.Models;

namespace Asteriq.Services;

/// <summary>
/// Reads raw HID input from physical joystick devices for SC Bindings input detection.
/// This bypasses SDL2 which sometimes misreports sliders as buttons.
/// Based on SCVirtStick's InputReader implementation.
/// </summary>
public class HidInputReader : IDisposable
{
    // HID Usage IDs for parsing
    private const int HID_USAGE_PAGE_GENERIC = 0x01;
    private const int HID_USAGE_PAGE_BUTTON = 0x09;
    private const int HID_USAGE_X = 0x30;
    private const int HID_USAGE_Y = 0x31;
    private const int HID_USAGE_Z = 0x32;
    private const int HID_USAGE_RX = 0x33;
    private const int HID_USAGE_RY = 0x34;
    private const int HID_USAGE_RZ = 0x35;
    private const int HID_USAGE_SLIDER = 0x36;
    private const int HID_USAGE_DIAL = 0x37;
    private const int HID_USAGE_HAT_SWITCH = 0x39;

    private readonly Dictionary<string, DeviceReader> _readers = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Current axis states for all devices. Key: HID device path, Value: 8 axis values (0.0-1.0)
    /// </summary>
    public Dictionary<string, float[]> AxisStates { get; } = new();

    /// <summary>
    /// Current button states for all devices. Key: HID device path, Value: button pressed states
    /// </summary>
    public Dictionary<string, bool[]> ButtonStates { get; } = new();

    /// <summary>
    /// Current POV hat states for all devices. Key: HID device path, Value: POV angles (-1 = centered)
    /// </summary>
    public Dictionary<string, int[]> PovStates { get; } = new();

    /// <summary>
    /// Fired when input is received from any device
    /// </summary>
    public event EventHandler<HidInputEventArgs>? InputReceived;

    /// <summary>
    /// Open a device for reading by its HID device path
    /// </summary>
    public bool OpenDevice(string hidDevicePath, string displayName)
    {
        lock (_lock)
        {
            if (_readers.ContainsKey(hidDevicePath))
                return true;

            try
            {
                var hidDevice = DeviceList.Local.GetHidDevices()
                    .FirstOrDefault(d => d.DevicePath == hidDevicePath);

                if (hidDevice is null)
                {
                    System.Diagnostics.Debug.WriteLine($"[HidInputReader] Device not found: {hidDevicePath}");
                    return false;
                }

                var reader = new DeviceReader(hidDevice, hidDevicePath, displayName, this);
                if (!reader.Start())
                {
                    System.Diagnostics.Debug.WriteLine($"[HidInputReader] Failed to start reader: {displayName}");
                    return false;
                }

                _readers[hidDevicePath] = reader;

                // Initialize state arrays
                AxisStates[hidDevicePath] = new float[8];
                ButtonStates[hidDevicePath] = new bool[128];
                PovStates[hidDevicePath] = new int[4] { -1, -1, -1, -1 };

                System.Diagnostics.Debug.WriteLine($"[HidInputReader] Opened device: {displayName}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HidInputReader] Failed to open device: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Close a device
    /// </summary>
    public void CloseDevice(string hidDevicePath)
    {
        lock (_lock)
        {
            if (_readers.TryGetValue(hidDevicePath, out var reader))
            {
                reader.Stop();
                reader.Dispose();
                _readers.Remove(hidDevicePath);
                AxisStates.Remove(hidDevicePath);
                ButtonStates.Remove(hidDevicePath);
                PovStates.Remove(hidDevicePath);
            }
        }
    }

    /// <summary>
    /// Close all devices
    /// </summary>
    public void CloseAllDevices()
    {
        lock (_lock)
        {
            foreach (var reader in _readers.Values)
            {
                reader.Stop();
                reader.Dispose();
            }
            _readers.Clear();
            AxisStates.Clear();
            ButtonStates.Clear();
            PovStates.Clear();
        }
    }

    internal void OnInputReceived(string devicePath, float[] axes, bool[] buttons, int[] povs)
    {
        lock (_lock)
        {
            if (AxisStates.TryGetValue(devicePath, out var axisState))
                Array.Copy(axes, axisState, Math.Min(axes.Length, 8));
            if (ButtonStates.TryGetValue(devicePath, out var buttonState))
                Array.Copy(buttons, buttonState, Math.Min(buttons.Length, 128));
            if (PovStates.TryGetValue(devicePath, out var povState))
                Array.Copy(povs, povState, Math.Min(povs.Length, 4));
        }

        InputReceived?.Invoke(this, new HidInputEventArgs(devicePath, axes, buttons, povs));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseAllDevices();
    }

    /// <summary>
    /// Internal class that manages reading from a single HID device
    /// </summary>
    private class DeviceReader : IDisposable
    {
        private readonly HidDevice _hidDevice;
        private readonly string _devicePath;
        private readonly string _displayName;
        private readonly HidInputReader _parent;
        private HidStream? _stream;
        private DeviceItemInputParser? _inputParser;
        private Report? _inputReport;
        private CancellationTokenSource? _cts;
        private Task? _readTask;
        private bool _disposed;

        // Axis mapping: usage ID -> index in our array
        private readonly Dictionary<uint, int> _axisMap = new();
        private readonly Dictionary<uint, int> _povMap = new();

        // State buffers
        private readonly float[] _axes = new float[8];
        private readonly bool[] _buttons = new bool[128];
        private readonly int[] _povs = new int[4] { -1, -1, -1, -1 };

        public DeviceReader(HidDevice hidDevice, string devicePath, string displayName, HidInputReader parent)
        {
            _hidDevice = hidDevice;
            _devicePath = devicePath;
            _displayName = displayName;
            _parent = parent;
        }

        public bool Start()
        {
            try
            {
                _stream = _hidDevice.Open();
                _stream.ReadTimeout = Timeout.Infinite;

                var reportDescriptor = _hidDevice.GetReportDescriptor();
                BuildInputMappings(reportDescriptor);

                _cts = new CancellationTokenSource();
                _readTask = Task.Run(ReadLoop, _cts.Token);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HidInputReader] Failed to start: {ex.Message}");
                return false;
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            try
            {
                _readTask?.Wait(500);
            }
            catch { /* ignore */ }
        }

        private void BuildInputMappings(ReportDescriptor reportDescriptor)
        {
            int povIndex = 0;
            int sliderIndex = 0;

            foreach (var deviceItem in reportDescriptor.DeviceItems)
            {
                _inputParser = deviceItem.CreateDeviceItemInputParser();

                foreach (var report in deviceItem.InputReports)
                {
                    _inputReport ??= report;

                    foreach (var dataItem in report.DataItems)
                    {
                        var usages = dataItem.Usages.GetAllValues().ToList();

                        foreach (var usage in usages)
                        {
                            var usagePage = (usage >> 16) & 0xFFFF;
                            var usageId = usage & 0xFFFF;

                            if (usagePage == HID_USAGE_PAGE_GENERIC)
                            {
                                int axisIndex = GetAxisIndexFromUsage(usageId, ref sliderIndex);
                                if (axisIndex >= 0 && axisIndex < 8)
                                {
                                    _axisMap[usage] = axisIndex;
                                    System.Diagnostics.Debug.WriteLine($"[HidInputReader] {_displayName}: Usage 0x{usageId:X2} -> Axis {axisIndex} ({GetAxisName(axisIndex)})");
                                }
                                else if (usageId == HID_USAGE_HAT_SWITCH && povIndex < 4)
                                {
                                    _povMap[usage] = povIndex++;
                                }
                            }
                        }
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"[HidInputReader] {_displayName}: {_axisMap.Count} axes, {_povMap.Count} POVs");
        }

        private static int GetAxisIndexFromUsage(uint usageId, ref int sliderIndex)
        {
            return usageId switch
            {
                HID_USAGE_X => 0,
                HID_USAGE_Y => 1,
                HID_USAGE_Z => 2,
                HID_USAGE_RX => 3,
                HID_USAGE_RY => 4,
                HID_USAGE_RZ => 5,
                HID_USAGE_SLIDER => sliderIndex++ == 0 ? 6 : 7,
                HID_USAGE_DIAL => sliderIndex++ == 0 ? 6 : 7,
                _ => -1
            };
        }

        private static string GetAxisName(int index) => index switch
        {
            0 => "X", 1 => "Y", 2 => "Z",
            3 => "RX", 4 => "RY", 5 => "RZ",
            6 => "Slider1", 7 => "Slider2",
            _ => "Unknown"
        };

        private async Task ReadLoop()
        {
            if (_stream is null) return;

            var buffer = new byte[_hidDevice.GetMaxInputReportLength()];

            while (_cts?.Token.IsCancellationRequested == false)
            {
                try
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, _cts?.Token ?? CancellationToken.None);
                    if (bytesRead > 0)
                    {
                        ParseReport(buffer);
                        _parent.OnInputReceived(_devicePath, _axes, _buttons, _povs);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (_cts?.Token.IsCancellationRequested != true && !_disposed)
                    {
                        System.Diagnostics.Debug.WriteLine($"[HidInputReader] Read error: {ex.Message}");
                    }
                    break;
                }
            }
        }

        private void ParseReport(byte[] buffer)
        {
            if (_inputParser is null || _inputReport is null) return;

            try
            {
                if (_inputParser.TryParseReport(buffer, 0, _inputReport))
                {
                    int currentButtonInDataItem = 0;
                    DataItem? lastButtonDataItem = null;
                    int currentAxisInDataItem = 0;
                    DataItem? lastAxisDataItem = null;

                    for (int i = 0; i < _inputParser.ValueCount; i++)
                    {
                        var dataValue = _inputParser.GetValue(i);
                        ProcessDataValue(dataValue, ref currentButtonInDataItem, ref lastButtonDataItem,
                                        ref currentAxisInDataItem, ref lastAxisDataItem);
                    }
                }
            }
            catch { /* ignore parse errors */ }
        }

        private void ProcessDataValue(DataValue dataValue, ref int currentButtonInDataItem, ref DataItem? lastButtonDataItem,
                                      ref int currentAxisInDataItem, ref DataItem? lastAxisDataItem)
        {
            var dataItem = dataValue.DataItem;
            var usages = dataItem.Usages.GetAllValues().ToList();
            if (usages.Count == 0) return;

            var firstUsage = usages[0];
            var usagePage = (firstUsage >> 16) & 0xFFFF;

            if (usagePage == HID_USAGE_PAGE_GENERIC)
            {
                // Axis or POV
                if (lastAxisDataItem != dataItem)
                {
                    currentAxisInDataItem = 0;
                    lastAxisDataItem = dataItem;
                }

                int valueIndex = currentAxisInDataItem;
                uint actualUsage = firstUsage;

                if (usages.Count > 1 && valueIndex < usages.Count)
                    actualUsage = usages[valueIndex];

                currentAxisInDataItem++;

                if (_axisMap.TryGetValue(actualUsage, out int axisIndex))
                {
                    var logicalMin = dataItem.LogicalMinimum;
                    var logicalMax = dataItem.LogicalMaximum;
                    var rawValue = dataValue.GetLogicalValue();

                    if (logicalMax > logicalMin)
                    {
                        _axes[axisIndex] = (float)(rawValue - logicalMin) / (logicalMax - logicalMin);
                    }
                }
                else if (_povMap.TryGetValue(actualUsage, out int povIndex))
                {
                    var rawValue = dataValue.GetLogicalValue();
                    var logicalMin = dataItem.LogicalMinimum;
                    var logicalMax = dataItem.LogicalMaximum;

                    if (rawValue < logicalMin || rawValue > logicalMax)
                    {
                        _povs[povIndex] = -1;
                    }
                    else
                    {
                        int directions = (int)(logicalMax - logicalMin + 1);
                        if (directions > 0)
                            _povs[povIndex] = (int)((rawValue - logicalMin) * 360 / directions);
                    }
                }
            }
            else if (usagePage == HID_USAGE_PAGE_BUTTON)
            {
                // Button
                if (lastButtonDataItem != dataItem)
                {
                    currentButtonInDataItem = 0;
                    lastButtonDataItem = dataItem;
                }

                int buttonNumber = currentButtonInDataItem < usages.Count
                    ? (int)(usages[currentButtonInDataItem] & 0xFFFF)
                    : (int)(usages[0] & 0xFFFF) + currentButtonInDataItem;

                int buttonIndex = buttonNumber - 1;
                if (buttonIndex >= 0 && buttonIndex < 128)
                {
                    _buttons[buttonIndex] = dataValue.GetLogicalValue() != 0;
                }

                currentButtonInDataItem++;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            _stream?.Dispose();
        }
    }
}

/// <summary>
/// Event args for HID input events
/// </summary>
public class HidInputEventArgs : EventArgs
{
    public string DevicePath { get; }
    public float[] Axes { get; }
    public bool[] Buttons { get; }
    public int[] Povs { get; }

    public HidInputEventArgs(string devicePath, float[] axes, bool[] buttons, int[] povs)
    {
        DevicePath = devicePath;
        Axes = (float[])axes.Clone();
        Buttons = (bool[])buttons.Clone();
        Povs = (int[])povs.Clone();
    }
}
