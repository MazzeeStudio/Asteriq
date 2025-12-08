using Asteriq.VJoy;

namespace Asteriq.Services;

/// <summary>
/// Information about a vJoy device slot
/// </summary>
public class VJoyDeviceInfo
{
    public uint Id { get; init; }
    public bool Exists { get; init; }
    public VjdStat Status { get; init; }
    public int ButtonCount { get; init; }
    public int DiscPovCount { get; init; }
    public int ContPovCount { get; init; }
    public bool HasAxisX { get; init; }
    public bool HasAxisY { get; init; }
    public bool HasAxisZ { get; init; }
    public bool HasAxisRX { get; init; }
    public bool HasAxisRY { get; init; }
    public bool HasAxisRZ { get; init; }
    public bool HasSlider0 { get; init; }
    public bool HasSlider1 { get; init; }
}

/// <summary>
/// Service for managing vJoy virtual devices
/// </summary>
public class VJoyService : IDisposable
{
    private readonly HashSet<uint> _acquiredDevices = new();
    private bool _isInitialized;

    // vJoy axis range (0 to 32767, center at 16384)
    public const int AxisMin = 0;
    public const int AxisMax = 32767;
    public const int AxisCenter = 16384;

    /// <summary>
    /// Initialize vJoy and check driver availability
    /// </summary>
    public bool Initialize()
    {
        if (_isInitialized) return true;

        try
        {
            if (!VJoyInterop.vJoyEnabled())
            {
                Console.WriteLine("vJoy driver not enabled");
                return false;
            }

            short version = VJoyInterop.GetvJoyVersion();
            Console.WriteLine($"vJoy version: {version}");

            uint dllVer = 0, drvVer = 0;
            if (!VJoyInterop.DriverMatch(ref dllVer, ref drvVer))
            {
                Console.WriteLine($"vJoy version mismatch: DLL={dllVer}, Driver={drvVer}");
                // Continue anyway, might still work
            }

            _isInitialized = true;
            return true;
        }
        catch (DllNotFoundException)
        {
            Console.WriteLine("vJoyInterface.dll not found");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"vJoy init error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Get information about all vJoy device slots (1-16)
    /// </summary>
    public List<VJoyDeviceInfo> EnumerateDevices()
    {
        var devices = new List<VJoyDeviceInfo>();

        for (uint id = 1; id <= 16; id++)
        {
            var info = GetDeviceInfo(id);
            if (info.Exists)
                devices.Add(info);
        }

        return devices;
    }

    /// <summary>
    /// Get information about a specific vJoy device
    /// </summary>
    public VJoyDeviceInfo GetDeviceInfo(uint deviceId)
    {
        bool exists = VJoyInterop.isVJDExists(deviceId);

        return new VJoyDeviceInfo
        {
            Id = deviceId,
            Exists = exists,
            Status = exists ? VJoyInterop.GetVJDStatusEnum(deviceId) : VjdStat.Miss,
            ButtonCount = exists ? VJoyInterop.GetVJDButtonNumber(deviceId) : 0,
            DiscPovCount = exists ? VJoyInterop.GetVJDDiscPovNumber(deviceId) : 0,
            ContPovCount = exists ? VJoyInterop.GetVJDContPovNumber(deviceId) : 0,
            HasAxisX = exists && VJoyInterop.AxisExists(deviceId, HID_USAGES.X),
            HasAxisY = exists && VJoyInterop.AxisExists(deviceId, HID_USAGES.Y),
            HasAxisZ = exists && VJoyInterop.AxisExists(deviceId, HID_USAGES.Z),
            HasAxisRX = exists && VJoyInterop.AxisExists(deviceId, HID_USAGES.RX),
            HasAxisRY = exists && VJoyInterop.AxisExists(deviceId, HID_USAGES.RY),
            HasAxisRZ = exists && VJoyInterop.AxisExists(deviceId, HID_USAGES.RZ),
            HasSlider0 = exists && VJoyInterop.AxisExists(deviceId, HID_USAGES.SL0),
            HasSlider1 = exists && VJoyInterop.AxisExists(deviceId, HID_USAGES.SL1),
        };
    }

    /// <summary>
    /// Acquire exclusive access to a vJoy device
    /// </summary>
    public bool AcquireDevice(uint deviceId)
    {
        if (_acquiredDevices.Contains(deviceId))
            return true;

        var status = VJoyInterop.GetVJDStatusEnum(deviceId);

        if (status == VjdStat.Own)
        {
            _acquiredDevices.Add(deviceId);
            return true;
        }

        if (status != VjdStat.Free)
        {
            Console.WriteLine($"vJoy device {deviceId} not available: {status}");
            return false;
        }

        if (!VJoyInterop.AcquireVJD(deviceId))
        {
            Console.WriteLine($"Failed to acquire vJoy device {deviceId}");
            return false;
        }

        _acquiredDevices.Add(deviceId);
        VJoyInterop.ResetVJD(deviceId);
        return true;
    }

    /// <summary>
    /// Release a vJoy device
    /// </summary>
    public void ReleaseDevice(uint deviceId)
    {
        if (_acquiredDevices.Contains(deviceId))
        {
            VJoyInterop.RelinquishVJD(deviceId);
            _acquiredDevices.Remove(deviceId);
        }
    }

    /// <summary>
    /// Set axis value (normalized -1.0 to 1.0)
    /// </summary>
    public bool SetAxis(uint deviceId, HID_USAGES axis, float value)
    {
        if (!_acquiredDevices.Contains(deviceId))
            return false;

        // Convert -1.0..1.0 to 0..32767
        int rawValue = (int)((value + 1.0f) * 0.5f * AxisMax);
        rawValue = Math.Clamp(rawValue, AxisMin, AxisMax);

        return VJoyInterop.SetAxis(rawValue, deviceId, axis);
    }

    /// <summary>
    /// Set button state (1-indexed)
    /// </summary>
    public bool SetButton(uint deviceId, int button, bool pressed)
    {
        if (!_acquiredDevices.Contains(deviceId))
            return false;

        return VJoyInterop.SetBtn(pressed, deviceId, (byte)button);
    }

    /// <summary>
    /// Set discrete POV hat (0-3 = directions, -1 = neutral)
    /// </summary>
    public bool SetDiscretePov(uint deviceId, uint povIndex, int direction)
    {
        if (!_acquiredDevices.Contains(deviceId))
            return false;

        return VJoyInterop.SetDiscPov(direction, deviceId, povIndex);
    }

    /// <summary>
    /// Set continuous POV hat (angle in degrees, -1 = neutral)
    /// </summary>
    public bool SetContinuousPov(uint deviceId, uint povIndex, int angle)
    {
        if (!_acquiredDevices.Contains(deviceId))
            return false;

        // vJoy expects angle * 100 (e.g., 90° = 9000)
        int value = angle >= 0 ? angle * 100 : -1;
        return VJoyInterop.SetContPov(value, deviceId, povIndex);
    }

    /// <summary>
    /// Reset all controls on a device to neutral
    /// </summary>
    public bool ResetDevice(uint deviceId)
    {
        if (!_acquiredDevices.Contains(deviceId))
            return false;

        return VJoyInterop.ResetVJD(deviceId);
    }

    public void Dispose()
    {
        foreach (var deviceId in _acquiredDevices.ToList())
        {
            ReleaseDevice(deviceId);
        }
    }
}
