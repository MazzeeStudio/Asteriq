using System.Runtime.InteropServices;

namespace Asteriq.VJoy;

/// <summary>
/// HID usage codes for vJoy axes
/// </summary>
public enum HID_USAGES : uint
{
    X = 0x30,
    Y = 0x31,
    Z = 0x32,
    RX = 0x33,
    RY = 0x34,
    RZ = 0x35,
    SL0 = 0x36,  // Slider 0
    SL1 = 0x37,  // Slider 1
    WHL = 0x38,  // Wheel
    POV = 0x39,  // POV Hat
}

/// <summary>
/// vJoy device status
/// </summary>
public enum VjdStat
{
    Own,    // Device is owned by this application
    Free,   // Device is not owned by any application
    Busy,   // Device is owned by another application
    Miss,   // Device does not exist or driver is down
    Unknown
}

/// <summary>
/// P/Invoke wrapper for vJoyInterface.dll
/// Based on official vJoy SDK wrapper
/// </summary>
public static class VJoyInterop
{
    private const string DllName = "vJoyInterface.dll";

    // General driver data
    [DllImport(DllName, EntryPoint = "GetvJoyVersion")]
    public static extern short GetvJoyVersion();

    [DllImport(DllName, EntryPoint = "vJoyEnabled")]
    public static extern bool vJoyEnabled();

    [DllImport(DllName, EntryPoint = "DriverMatch")]
    public static extern bool DriverMatch(ref uint DllVer, ref uint DrvVer);

    // vJoy Device properties
    [DllImport(DllName, EntryPoint = "GetVJDButtonNumber")]
    public static extern int GetVJDButtonNumber(uint rID);

    [DllImport(DllName, EntryPoint = "GetVJDDiscPovNumber")]
    public static extern int GetVJDDiscPovNumber(uint rID);

    [DllImport(DllName, EntryPoint = "GetVJDContPovNumber")]
    public static extern int GetVJDContPovNumber(uint rID);

    [DllImport(DllName, EntryPoint = "GetVJDAxisExist")]
    public static extern uint GetVJDAxisExist(uint rID, uint Axis);

    [DllImport(DllName, EntryPoint = "GetVJDAxisMax")]
    public static extern bool GetVJDAxisMax(uint rID, uint Axis, ref long Max);

    [DllImport(DllName, EntryPoint = "GetVJDAxisMin")]
    public static extern bool GetVJDAxisMin(uint rID, uint Axis, ref long Min);

    [DllImport(DllName, EntryPoint = "isVJDExists")]
    public static extern bool isVJDExists(uint rID);

    [DllImport(DllName, EntryPoint = "GetOwnerPid")]
    public static extern int GetOwnerPid(uint rID);

    // Write access to vJoy Device
    [DllImport(DllName, EntryPoint = "AcquireVJD")]
    public static extern bool AcquireVJD(uint rID);

    [DllImport(DllName, EntryPoint = "RelinquishVJD")]
    public static extern void RelinquishVJD(uint rID);

    [DllImport(DllName, EntryPoint = "GetVJDStatus")]
    public static extern int GetVJDStatus(uint rID);

    // Reset functions
    [DllImport(DllName, EntryPoint = "ResetVJD")]
    public static extern bool ResetVJD(uint rID);

    [DllImport(DllName, EntryPoint = "ResetAll")]
    public static extern bool ResetAll();

    [DllImport(DllName, EntryPoint = "ResetButtons")]
    public static extern bool ResetButtons(uint rID);

    [DllImport(DllName, EntryPoint = "ResetPovs")]
    public static extern bool ResetPovs(uint rID);

    // Write data
    [DllImport(DllName, EntryPoint = "SetAxis")]
    public static extern bool SetAxis(int Value, uint rID, HID_USAGES Axis);

    [DllImport(DllName, EntryPoint = "SetBtn")]
    public static extern bool SetBtn(bool Value, uint rID, byte nBtn);

    [DllImport(DllName, EntryPoint = "SetDiscPov")]
    public static extern bool SetDiscPov(int Value, uint rID, uint nPov);

    [DllImport(DllName, EntryPoint = "SetContPov")]
    public static extern bool SetContPov(int Value, uint rID, uint nPov);

    // Helper to get status as enum
    public static VjdStat GetVJDStatusEnum(uint rID)
    {
        int status = GetVJDStatus(rID);
        return status switch
        {
            0 => VjdStat.Own,
            1 => VjdStat.Free,
            2 => VjdStat.Busy,
            3 => VjdStat.Miss,
            _ => VjdStat.Unknown
        };
    }

    // Helper to check if axis exists
    public static bool AxisExists(uint rID, HID_USAGES axis)
    {
        return GetVJDAxisExist(rID, (uint)axis) == 1;
    }
}
