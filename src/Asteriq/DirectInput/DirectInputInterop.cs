using System.Runtime.InteropServices;

namespace Asteriq.DirectInput;

/// <summary>
/// P/Invoke declarations for DirectInput8
/// </summary>
internal static class DirectInputInterop
{
    public const uint DIRECTINPUT_VERSION = 0x0800;
    public const uint DIEDFL_ALLDEVICES = 0x00000000;
    public const uint DIEDFL_ATTACHEDONLY = 0x00000001;
    public const uint DI8DEVCLASS_GAMECTRL = 4;
    public const uint DIDFT_AXIS = 0x00000003;
    public const uint DIDFT_ABSAXIS = 0x00000002;

    // Device type for joysticks
    public const uint DI8DEVTYPE_JOYSTICK = 0x14;
    public const uint DI8DEVTYPE_GAMEPAD = 0x15;
    public const uint DI8DEVTYPE_DRIVING = 0x16;
    public const uint DI8DEVTYPE_FLIGHT = 0x17;

    // Cooperative level flags
    public const uint DISCL_EXCLUSIVE = 0x00000001;
    public const uint DISCL_NONEXCLUSIVE = 0x00000002;
    public const uint DISCL_FOREGROUND = 0x00000004;
    public const uint DISCL_BACKGROUND = 0x00000008;

    // Axis GUIDs - these identify the semantic type of each axis
    public static readonly Guid GUID_XAxis = new("A36D02E0-C9F3-11CF-BFC7-444553540000");
    public static readonly Guid GUID_YAxis = new("A36D02E1-C9F3-11CF-BFC7-444553540000");
    public static readonly Guid GUID_ZAxis = new("A36D02E2-C9F3-11CF-BFC7-444553540000");
    public static readonly Guid GUID_RxAxis = new("A36D02F4-C9F3-11CF-BFC7-444553540000");
    public static readonly Guid GUID_RyAxis = new("A36D02F5-C9F3-11CF-BFC7-444553540000");
    public static readonly Guid GUID_RzAxis = new("A36D02E3-C9F3-11CF-BFC7-444553540000");
    public static readonly Guid GUID_Slider = new("A36D02E4-C9F3-11CF-BFC7-444553540000");

    // POV GUID
    public static readonly Guid GUID_POV = new("A36D02F2-C9F3-11CF-BFC7-444553540000");

    // Button GUID
    public static readonly Guid GUID_Button = new("A36D02F0-C9F3-11CF-BFC7-444553540000");

    // IDirectInput8 interface GUID
    public static readonly Guid IID_IDirectInput8W = new("BF798031-483A-4DA2-AA99-5D64ED369700");

    [DllImport("dinput8.dll", EntryPoint = "DirectInput8Create", CallingConvention = CallingConvention.StdCall)]
    public static extern int DirectInput8Create(
        IntPtr hinst,
        uint dwVersion,
        ref Guid riidltf,
        out IntPtr ppvOut,
        IntPtr punkOuter);

    // IDirectInput8 vtable indices
    public const int IDirectInput8_EnumDevices = 4;
    public const int IDirectInput8_CreateDevice = 3;

    // IDirectInputDevice8 vtable indices
    public const int IDirectInputDevice8_Release = 2;
    public const int IDirectInputDevice8_GetCapabilities = 4;
    public const int IDirectInputDevice8_SetDataFormat = 7;
    public const int IDirectInputDevice8_SetCooperativeLevel = 13;
    public const int IDirectInputDevice8_Acquire = 8;
    public const int IDirectInputDevice8_Unacquire = 9;
    public const int IDirectInputDevice8_GetDeviceState = 10;
    public const int IDirectInputDevice8_Poll = 6;
    public const int IDirectInputDevice8_EnumObjects = 12;

    // Data format for joystick (c_dfDIJoystick2)
    // We'll define a simplified DIJOYSTATE2 structure
    public const int DIJOFS_X = 0;
    public const int DIJOFS_Y = 4;
    public const int DIJOFS_Z = 8;
    public const int DIJOFS_RX = 12;
    public const int DIJOFS_RY = 16;
    public const int DIJOFS_RZ = 20;
    public const int DIJOFS_SLIDER0 = 24;
    public const int DIJOFS_SLIDER1 = 28;
    public const int DIJOFS_POV0 = 32;
    public const int DIJOFS_POV1 = 36;
    public const int DIJOFS_POV2 = 40;
    public const int DIJOFS_POV3 = 44;
    public const int DIJOFS_BUTTON0 = 48;

    [StructLayout(LayoutKind.Sequential)]
    public struct DIDEVICEINSTANCEW
    {
        public uint dwSize;
        public Guid guidInstance;
        public Guid guidProduct;
        public uint dwDevType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string tszInstanceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string tszProductName;
        public Guid guidFFDriver;
        public ushort wUsagePage;
        public ushort wUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DIDEVICEOBJECTINSTANCEW
    {
        public uint dwSize;
        public Guid guidType;
        public uint dwOfs;
        public uint dwType;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string tszName;
        public uint dwFFMaxForce;
        public uint dwFFForceResolution;
        public ushort wCollectionNumber;
        public ushort wDesignatorIndex;
        public ushort wUsagePage;
        public ushort wUsage;
        public uint dwDimension;
        public ushort wExponent;
        public ushort wReportId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DIDEVCAPS
    {
        public uint dwSize;
        public uint dwFlags;
        public uint dwDevType;
        public uint dwAxes;
        public uint dwButtons;
        public uint dwPOVs;
        public uint dwFFSamplePeriod;
        public uint dwFFMinTimeResolution;
        public uint dwFirmwareRevision;
        public uint dwHardwareRevision;
        public uint dwFFDriverVersion;
    }

    // Delegate for device enumeration callback
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int EnumDevicesCallback(ref DIDEVICEINSTANCEW lpddi, IntPtr pvRef);

    // Delegate for object enumeration callback
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int EnumObjectsCallback(ref DIDEVICEOBJECTINSTANCEW lpddoi, IntPtr pvRef);

    public const int DIENUM_CONTINUE = 1;
    public const int DIENUM_STOP = 0;
    public const int DI_OK = 0;
    public const int DIERR_INPUTLOST = unchecked((int)0x8007001E);
    public const int DIERR_NOTACQUIRED = unchecked((int)0x8007000C);

    // DIJOYSTATE2 - Extended joystick state (matches c_dfDIJoystick2)
    [StructLayout(LayoutKind.Sequential)]
    public struct DIJOYSTATE2
    {
        public int lX;                    // X axis
        public int lY;                    // Y axis
        public int lZ;                    // Z axis
        public int lRx;                   // X rotation
        public int lRy;                   // Y rotation
        public int lRz;                   // Z rotation
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public int[] rglSlider;           // 2 sliders
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] rgdwPOV;            // 4 POV hats
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
        public byte[] rgbButtons;         // 128 buttons
        public int lVX;                   // X velocity
        public int lVY;                   // Y velocity
        public int lVZ;                   // Z velocity
        public int lVRx;                  // X rotation velocity
        public int lVRy;                  // Y rotation velocity
        public int lVRz;                  // Z rotation velocity
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public int[] rglVSlider;          // 2 slider velocities
        public int lAX;                   // X acceleration
        public int lAY;                   // Y acceleration
        public int lAZ;                   // Z acceleration
        public int lARx;                  // X rotation acceleration
        public int lARy;                  // Y rotation acceleration
        public int lARz;                  // Z rotation acceleration
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public int[] rglASlider;          // 2 slider accelerations
        public int lFX;                   // X force
        public int lFY;                   // Y force
        public int lFZ;                   // Z force
        public int lFRx;                  // X rotation force
        public int lFRy;                  // Y rotation force
        public int lFRz;                  // Z rotation force
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public int[] rglFSlider;          // 2 slider forces

        public static DIJOYSTATE2 Create()
        {
            return new DIJOYSTATE2
            {
                rglSlider = new int[2],
                rgdwPOV = new uint[4] { 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF },
                rgbButtons = new byte[128],
                rglVSlider = new int[2],
                rglASlider = new int[2],
                rglFSlider = new int[2]
            };
        }
    }

    // DIOBJECTDATAFORMAT - Describes a single object in a data format
    [StructLayout(LayoutKind.Sequential)]
    public struct DIOBJECTDATAFORMAT
    {
        public IntPtr pguid;     // Pointer to GUID (can be null)
        public uint dwOfs;       // Offset in data packet
        public uint dwType;      // Type of object (axis, button, etc.)
        public uint dwFlags;     // Flags
    }

    // DIDATAFORMAT - Describes the data format for a device
    [StructLayout(LayoutKind.Sequential)]
    public struct DIDATAFORMAT
    {
        public uint dwSize;      // Size of this structure
        public uint dwObjSize;   // Size of DIOBJECTDATAFORMAT
        public uint dwFlags;     // DIDF_ABSAXIS or DIDF_RELAXIS
        public uint dwDataSize;  // Size of data packet
        public uint dwNumObjs;   // Number of objects
        public IntPtr rgodf;     // Pointer to array of DIOBJECTDATAFORMAT
    }

    // Data format flags
    public const uint DIDF_ABSAXIS = 0x00000001;
    public const uint DIDF_RELAXIS = 0x00000002;

    // POV centered value
    public const uint POV_CENTERED = 0xFFFFFFFF;
}

/// <summary>
/// Represents an axis type as reported by DirectInput
/// </summary>
public enum DirectInputAxisType
{
    Unknown = 0,
    X = 1,
    Y = 2,
    Z = 3,
    RX = 4,
    RY = 5,
    RZ = 6,
    Slider = 7
}

/// <summary>
/// Information about a device axis from DirectInput
/// </summary>
public class DirectInputAxisInfo
{
    public int Index { get; init; }
    public DirectInputAxisType Type { get; init; }
    public string Name { get; init; } = string.Empty;
    public Guid TypeGuid { get; init; }
}

/// <summary>
/// DirectInput device information including axis types
/// </summary>
public class DirectInputDeviceInfo
{
    public Guid InstanceGuid { get; init; }
    public Guid ProductGuid { get; init; }
    public string InstanceName { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public List<DirectInputAxisInfo> Axes { get; init; } = new();
    public int ButtonCount { get; init; }
    public int PovCount { get; init; }
}
