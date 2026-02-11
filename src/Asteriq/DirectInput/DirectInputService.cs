using System.Runtime.InteropServices;
using static Asteriq.DirectInput.DirectInputInterop;

namespace Asteriq.DirectInput;

/// <summary>
/// Service for querying DirectInput devices to get axis type information.
/// This is needed because SDL2 doesn't expose HID usage codes / axis semantics.
/// </summary>
public class DirectInputService : IDisposable
{
    private IntPtr _directInput;
    private bool _disposed;
    private readonly Dictionary<Guid, DirectInputDeviceInfo> _deviceCache = new();

    public DirectInputService()
    {
        Initialize();
    }

    private void Initialize()
    {
        var iid = IID_IDirectInput8W;
        int hr = DirectInput8Create(
            Marshal.GetHINSTANCE(typeof(DirectInputService).Module),
            DIRECTINPUT_VERSION,
            ref iid,
            out _directInput,
            IntPtr.Zero);

        if (hr != DI_OK || _directInput == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to create DirectInput8. HRESULT: 0x{hr:X8}");
        }
    }

    /// <summary>
    /// Enumerate all game controller devices and get their axis information.
    /// </summary>
    public List<DirectInputDeviceInfo> EnumerateDevices()
    {
        var devices = new List<DirectInputDeviceInfo>();

        EnumDevicesCallback callback = (ref DIDEVICEINSTANCEW deviceInstance, IntPtr pvRef) =>
        {
            try
            {
                var deviceInfo = GetDeviceInfo(deviceInstance.guidInstance, deviceInstance);
                if (deviceInfo is not null)
                {
                    devices.Add(deviceInfo);
                    _deviceCache[deviceInstance.guidInstance] = deviceInfo;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or EntryPointNotFoundException)
            {
                // Skip devices that fail to enumerate
            }
            return DIENUM_CONTINUE;
        };

        // Call EnumDevices via vtable
        var enumDevicesPtr = GetVTableMethod(_directInput, IDirectInput8_EnumDevices);
        var enumDevices = Marshal.GetDelegateForFunctionPointer<EnumDevicesDelegate>(enumDevicesPtr);
        enumDevices(_directInput, DI8DEVCLASS_GAMECTRL, callback, IntPtr.Zero, DIEDFL_ATTACHEDONLY);

        return devices;
    }

    /// <summary>
    /// Get device info by instance GUID. Returns cached info if available.
    /// </summary>
    public DirectInputDeviceInfo? GetDeviceByGuid(Guid instanceGuid)
    {
        if (_deviceCache.TryGetValue(instanceGuid, out var cached))
            return cached;

        // Re-enumerate to find the device
        EnumerateDevices();
        return _deviceCache.GetValueOrDefault(instanceGuid);
    }

    /// <summary>
    /// Get axis types for a device by its instance GUID.
    /// Returns a dictionary mapping axis index to axis type.
    /// </summary>
    public Dictionary<int, DirectInputAxisType> GetAxisTypes(Guid instanceGuid)
    {
        var result = new Dictionary<int, DirectInputAxisType>();
        var deviceInfo = GetDeviceByGuid(instanceGuid);

        if (deviceInfo is not null)
        {
            foreach (var axis in deviceInfo.Axes)
            {
                result[axis.Index] = axis.Type;
            }
        }

        return result;
    }

    private DirectInputDeviceInfo? GetDeviceInfo(Guid instanceGuid, DIDEVICEINSTANCEW deviceInstance)
    {
        IntPtr device = IntPtr.Zero;

        try
        {
            // CreateDevice via vtable
            var createDevicePtr = GetVTableMethod(_directInput, IDirectInput8_CreateDevice);
            var createDevice = Marshal.GetDelegateForFunctionPointer<CreateDeviceDelegate>(createDevicePtr);

            int hr = createDevice(_directInput, ref instanceGuid, out device, IntPtr.Zero);
            if (hr != DI_OK || device == IntPtr.Zero)
                return null;

            // Get capabilities
            var caps = new DIDEVCAPS { dwSize = (uint)Marshal.SizeOf<DIDEVCAPS>() };
            var getCapsPtr = GetVTableMethod(device, IDirectInputDevice8_GetCapabilities);
            var getCaps = Marshal.GetDelegateForFunctionPointer<GetCapabilitiesDelegate>(getCapsPtr);
            hr = getCaps(device, ref caps);

            if (hr != DI_OK)
                return null;

            // Enumerate axes
            var axes = new List<DirectInputAxisInfo>();
            int axisIndex = 0;

            EnumObjectsCallback axisCallback = (ref DIDEVICEOBJECTINSTANCEW objInstance, IntPtr pvRef) =>
            {
                // Check if this is an axis
                if ((objInstance.dwType & DIDFT_AXIS) != 0 || (objInstance.dwType & DIDFT_ABSAXIS) != 0)
                {
                    var axisType = GuidToAxisType(objInstance.guidType);
                    axes.Add(new DirectInputAxisInfo
                    {
                        Index = axisIndex++,
                        Type = axisType,
                        Name = objInstance.tszName ?? $"Axis {axisIndex}",
                        TypeGuid = objInstance.guidType
                    });
                }
                return DIENUM_CONTINUE;
            };

            var enumObjectsPtr = GetVTableMethod(device, IDirectInputDevice8_EnumObjects);
            var enumObjects = Marshal.GetDelegateForFunctionPointer<EnumObjectsDelegate>(enumObjectsPtr);
            enumObjects(device, axisCallback, IntPtr.Zero, DIDFT_AXIS);

            return new DirectInputDeviceInfo
            {
                InstanceGuid = instanceGuid,
                ProductGuid = deviceInstance.guidProduct,
                InstanceName = deviceInstance.tszInstanceName ?? "",
                ProductName = deviceInstance.tszProductName ?? "",
                Axes = axes,
                ButtonCount = (int)caps.dwButtons,
                PovCount = (int)caps.dwPOVs
            };
        }
        finally
        {
            if (device != IntPtr.Zero)
            {
                var releasePtr = GetVTableMethod(device, IDirectInputDevice8_Release);
                var release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(releasePtr);
                release(device);
            }
        }
    }

    private static DirectInputAxisType GuidToAxisType(Guid guid)
    {
        if (guid == GUID_XAxis) return DirectInputAxisType.X;
        if (guid == GUID_YAxis) return DirectInputAxisType.Y;
        if (guid == GUID_ZAxis) return DirectInputAxisType.Z;
        if (guid == GUID_RxAxis) return DirectInputAxisType.RX;
        if (guid == GUID_RyAxis) return DirectInputAxisType.RY;
        if (guid == GUID_RzAxis) return DirectInputAxisType.RZ;
        if (guid == GUID_Slider) return DirectInputAxisType.Slider;
        return DirectInputAxisType.Unknown;
    }

    private static IntPtr GetVTableMethod(IntPtr comObject, int methodIndex)
    {
        IntPtr vtable = Marshal.ReadIntPtr(comObject);
        return Marshal.ReadIntPtr(vtable, methodIndex * IntPtr.Size);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_directInput != IntPtr.Zero)
            {
                // Release IDirectInput8
                var releasePtr = GetVTableMethod(_directInput, 2); // Release is always index 2 in COM
                var release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(releasePtr);
                release(_directInput);
                _directInput = IntPtr.Zero;
            }
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    ~DirectInputService()
    {
        Dispose();
    }

    // Delegate types for COM vtable calls
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumDevicesDelegate(IntPtr self, uint dwDevType, EnumDevicesCallback lpCallback, IntPtr pvRef, uint dwFlags);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateDeviceDelegate(IntPtr self, ref Guid rguid, out IntPtr lplpDirectInputDevice, IntPtr pUnkOuter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetCapabilitiesDelegate(IntPtr self, ref DIDEVCAPS lpDIDevCaps);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumObjectsDelegate(IntPtr self, EnumObjectsCallback lpCallback, IntPtr pvRef, uint dwFlags);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseDelegate(IntPtr self);
}
