using System.Runtime.InteropServices;
using static Asteriq.DirectInput.DirectInputInterop;

namespace Asteriq.DirectInput;

/// <summary>
/// Reads input state from DirectInput devices.
/// This provides an alternative to SDL2 for more reliable input reading,
/// especially for dual-role controls (slider+button) and complex devices.
/// </summary>
public class DirectInputReader : IDisposable
{
    private IntPtr _directInput;
    private readonly Dictionary<Guid, DeviceHandle> _openDevices = new();
    private readonly object _lock = new();
    private bool _disposed;
    private IntPtr _dataFormatPtr;

    // Cached delegates to prevent GC collection
    private readonly EnumDevicesCallback _enumCallback;

    private class DeviceHandle
    {
        public IntPtr Device;
        public Guid InstanceGuid;
        public DIJOYSTATE2 LastState;
        public bool IsAcquired;
        public int AxisCount;
        public int ButtonCount;
        public int PovCount;

        // Vtable delegates - cached per device
        public PollDelegate? Poll;
        public GetDeviceStateDelegate? GetDeviceState;
        public AcquireDelegate? Acquire;
        public UnacquireDelegate? Unacquire;
    }

    // Delegate types for device vtable calls
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PollDelegate(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDeviceStateDelegate(IntPtr self, uint cbData, IntPtr lpvData);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int AcquireDelegate(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int UnacquireDelegate(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetDataFormatDelegate(IntPtr self, IntPtr lpdf);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetCooperativeLevelDelegate(IntPtr self, IntPtr hwnd, uint dwFlags);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateDeviceDelegate(IntPtr self, ref Guid rguid, out IntPtr lplpDirectInputDevice, IntPtr pUnkOuter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetCapabilitiesDelegate(IntPtr self, ref DIDEVCAPS lpDIDevCaps);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseDelegate(IntPtr self);

    public DirectInputReader()
    {
        _enumCallback = EnumDevicesCallbackImpl;
        Initialize();
        BuildDataFormat();
    }

    private void Initialize()
    {
        var iid = IID_IDirectInput8W;
        int hr = DirectInput8Create(
            Marshal.GetHINSTANCE(typeof(DirectInputReader).Module),
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
    /// Build the c_dfDIJoystick2 data format structure
    /// </summary>
    private void BuildDataFormat()
    {
        // We use a pre-allocated DIJOYSTATE2 structure
        // DirectInput has a predefined c_dfDIJoystick2, but we need to build it manually
        // For simplicity, we'll set data format during device open using the structure size
        _dataFormatPtr = IntPtr.Zero; // We'll use device-specific format
    }

    /// <summary>
    /// Get list of available DirectInput game controller devices
    /// </summary>
    public List<DirectInputDeviceInfo> EnumerateDevices()
    {
        var devices = new List<DirectInputDeviceInfo>();

        var enumDevicesPtr = GetVTableMethod(_directInput, IDirectInput8_EnumDevices);
        var enumDevices = Marshal.GetDelegateForFunctionPointer<EnumDevicesDelegate>(enumDevicesPtr);

        // Store devices list in a GCHandle so callback can access it
        var handle = GCHandle.Alloc(devices);
        try
        {
            enumDevices(_directInput, DI8DEVCLASS_GAMECTRL, _enumCallback, GCHandle.ToIntPtr(handle), DIEDFL_ATTACHEDONLY);
        }
        finally
        {
            handle.Free();
        }

        return devices;
    }

    private int EnumDevicesCallbackImpl(ref DIDEVICEINSTANCEW deviceInstance, IntPtr pvRef)
    {
        var handle = GCHandle.FromIntPtr(pvRef);
        var devices = (List<DirectInputDeviceInfo>)handle.Target!;

        devices.Add(new DirectInputDeviceInfo
        {
            InstanceGuid = deviceInstance.guidInstance,
            ProductGuid = deviceInstance.guidProduct,
            InstanceName = deviceInstance.tszInstanceName ?? "",
            ProductName = deviceInstance.tszProductName ?? ""
        });

        return DIENUM_CONTINUE;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumDevicesDelegate(IntPtr self, uint dwDevType, EnumDevicesCallback lpCallback, IntPtr pvRef, uint dwFlags);

    /// <summary>
    /// Open a device for polling. Must be called before reading input.
    /// </summary>
    public bool OpenDevice(Guid instanceGuid, IntPtr? windowHandle = null)
    {
        lock (_lock)
        {
            if (_openDevices.ContainsKey(instanceGuid))
                return true; // Already open

            IntPtr device = IntPtr.Zero;
            try
            {
                // CreateDevice
                var createDevicePtr = GetVTableMethod(_directInput, IDirectInput8_CreateDevice);
                var createDevice = Marshal.GetDelegateForFunctionPointer<CreateDeviceDelegate>(createDevicePtr);

                int hr = createDevice(_directInput, ref instanceGuid, out device, IntPtr.Zero);
                if (hr != DI_OK || device == IntPtr.Zero)
                    return false;

                // Get capabilities
                var caps = new DIDEVCAPS { dwSize = (uint)Marshal.SizeOf<DIDEVCAPS>() };
                var getCapsPtr = GetVTableMethod(device, IDirectInputDevice8_GetCapabilities);
                var getCaps = Marshal.GetDelegateForFunctionPointer<GetCapabilitiesDelegate>(getCapsPtr);
                getCaps(device, ref caps);

                // Set cooperative level (non-exclusive, background)
                var setCoopPtr = GetVTableMethod(device, IDirectInputDevice8_SetCooperativeLevel);
                var setCoop = Marshal.GetDelegateForFunctionPointer<SetCooperativeLevelDelegate>(setCoopPtr);
                // Use desktop window if no window provided
                IntPtr hwnd = windowHandle ?? GetDesktopWindow();
                hr = setCoop(device, hwnd, DISCL_NONEXCLUSIVE | DISCL_BACKGROUND);
                if (hr != DI_OK)
                {
                    ReleaseDevice(device);
                    return false;
                }

                // Set data format to c_dfDIJoystick2
                // We need to use the predefined format - get it from dinput8.dll
                var setDataFormatPtr = GetVTableMethod(device, IDirectInputDevice8_SetDataFormat);
                var setDataFormat = Marshal.GetDelegateForFunctionPointer<SetDataFormatDelegate>(setDataFormatPtr);

                // Get the predefined c_dfDIJoystick2 from dinput8.dll
                IntPtr dfPtr = GetPredefinedDataFormat();
                if (dfPtr == IntPtr.Zero)
                {
                    ReleaseDevice(device);
                    return false;
                }

                hr = setDataFormat(device, dfPtr);
                if (hr != DI_OK)
                {
                    ReleaseDevice(device);
                    return false;
                }

                // Acquire the device
                var acquirePtr = GetVTableMethod(device, IDirectInputDevice8_Acquire);
                var acquire = Marshal.GetDelegateForFunctionPointer<AcquireDelegate>(acquirePtr);
                hr = acquire(device);
                // Acquire can fail if device is in use, but we'll try anyway

                // Cache vtable delegates
                var handle = new DeviceHandle
                {
                    Device = device,
                    InstanceGuid = instanceGuid,
                    LastState = DIJOYSTATE2.Create(),
                    IsAcquired = hr == DI_OK,
                    AxisCount = (int)caps.dwAxes,
                    ButtonCount = (int)caps.dwButtons,
                    PovCount = (int)caps.dwPOVs,
                    Poll = Marshal.GetDelegateForFunctionPointer<PollDelegate>(GetVTableMethod(device, IDirectInputDevice8_Poll)),
                    GetDeviceState = Marshal.GetDelegateForFunctionPointer<GetDeviceStateDelegate>(GetVTableMethod(device, IDirectInputDevice8_GetDeviceState)),
                    Acquire = acquire,
                    Unacquire = Marshal.GetDelegateForFunctionPointer<UnacquireDelegate>(GetVTableMethod(device, IDirectInputDevice8_Unacquire))
                };

                _openDevices[instanceGuid] = handle;
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or EntryPointNotFoundException)
            {
                if (device != IntPtr.Zero)
                    ReleaseDevice(device);
                return false;
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("dinput8.dll")]
    private static extern IntPtr GetdfDIJoystick();

    private static IntPtr GetPredefinedDataFormat()
    {
        // c_dfDIJoystick2 is exported from dinput8.dll
        // We need to load it dynamically
        IntPtr dinput8 = LoadLibrary("dinput8.dll");
        if (dinput8 == IntPtr.Zero)
            return IntPtr.Zero;

        // The symbol is "c_dfDIJoystick2"
        IntPtr formatPtr = GetProcAddress(dinput8, "c_dfDIJoystick2");
        return formatPtr;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    /// <summary>
    /// Close a device
    /// </summary>
    public void CloseDevice(Guid instanceGuid)
    {
        lock (_lock)
        {
            if (_openDevices.TryGetValue(instanceGuid, out var handle))
            {
                handle.Unacquire?.Invoke(handle.Device);
                ReleaseDevice(handle.Device);
                _openDevices.Remove(instanceGuid);
            }
        }
    }

    private void ReleaseDevice(IntPtr device)
    {
        var releasePtr = GetVTableMethod(device, IDirectInputDevice8_Release);
        var release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(releasePtr);
        release(device);
    }

    /// <summary>
    /// Poll a device and update its state. Call this before reading values.
    /// </summary>
    public bool PollDevice(Guid instanceGuid)
    {
        lock (_lock)
        {
            if (!_openDevices.TryGetValue(instanceGuid, out var handle))
                return false;

            // Poll the device
            int hr = handle.Poll!(handle.Device);

            // If device was lost, try to reacquire
            if (hr == DIERR_INPUTLOST || hr == DIERR_NOTACQUIRED)
            {
                hr = handle.Acquire!(handle.Device);
                if (hr != DI_OK)
                {
                    handle.IsAcquired = false;
                    return false;
                }
                handle.IsAcquired = true;
                hr = handle.Poll(handle.Device);
            }

            // Get device state
            int stateSize = Marshal.SizeOf<DIJOYSTATE2>();
            IntPtr statePtr = Marshal.AllocHGlobal(stateSize);
            try
            {
                hr = handle.GetDeviceState!(handle.Device, (uint)stateSize, statePtr);
                if (hr == DI_OK)
                {
                    handle.LastState = Marshal.PtrToStructure<DIJOYSTATE2>(statePtr);
                    return true;
                }
                else if (hr == DIERR_INPUTLOST || hr == DIERR_NOTACQUIRED)
                {
                    handle.Acquire!(handle.Device);
                }
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(statePtr);
            }
        }
    }

    /// <summary>
    /// Get axis value normalized to -1.0 to 1.0 range.
    /// </summary>
    /// <param name="instanceGuid">Device GUID</param>
    /// <param name="axisIndex">Axis index (0=X, 1=Y, 2=Z, 3=RX, 4=RY, 5=RZ, 6=Slider0, 7=Slider1)</param>
    public float GetAxis(Guid instanceGuid, int axisIndex)
    {
        lock (_lock)
        {
            if (!_openDevices.TryGetValue(instanceGuid, out var handle))
                return 0f;

            int rawValue = axisIndex switch
            {
                0 => handle.LastState.lX,
                1 => handle.LastState.lY,
                2 => handle.LastState.lZ,
                3 => handle.LastState.lRx,
                4 => handle.LastState.lRy,
                5 => handle.LastState.lRz,
                6 => handle.LastState.rglSlider?[0] ?? 0,
                7 => handle.LastState.rglSlider?[1] ?? 0,
                _ => 0
            };

            // DirectInput axis range is 0-65535, center at 32767
            // Normalize to -1.0 to 1.0
            return (rawValue - 32767) / 32767f;
        }
    }

    /// <summary>
    /// Get raw axis value (0-65535 range, center at 32767)
    /// </summary>
    public int GetAxisRaw(Guid instanceGuid, int axisIndex)
    {
        lock (_lock)
        {
            if (!_openDevices.TryGetValue(instanceGuid, out var handle))
                return 32767;

            return axisIndex switch
            {
                0 => handle.LastState.lX,
                1 => handle.LastState.lY,
                2 => handle.LastState.lZ,
                3 => handle.LastState.lRx,
                4 => handle.LastState.lRy,
                5 => handle.LastState.lRz,
                6 => handle.LastState.rglSlider?[0] ?? 32767,
                7 => handle.LastState.rglSlider?[1] ?? 32767,
                _ => 32767
            };
        }
    }

    /// <summary>
    /// Get button state (true = pressed)
    /// </summary>
    /// <param name="instanceGuid">Device GUID</param>
    /// <param name="buttonIndex">Button index (0-127)</param>
    public bool GetButton(Guid instanceGuid, int buttonIndex)
    {
        lock (_lock)
        {
            if (!_openDevices.TryGetValue(instanceGuid, out var handle))
                return false;

            if (handle.LastState.rgbButtons is null || buttonIndex < 0 || buttonIndex >= 128)
                return false;

            // Button is pressed if high bit (0x80) is set
            return (handle.LastState.rgbButtons[buttonIndex] & 0x80) != 0;
        }
    }

    /// <summary>
    /// Get POV/hat value in degrees (0-359) or -1 for centered
    /// </summary>
    /// <param name="instanceGuid">Device GUID</param>
    /// <param name="povIndex">POV index (0-3)</param>
    public int GetPov(Guid instanceGuid, int povIndex)
    {
        lock (_lock)
        {
            if (!_openDevices.TryGetValue(instanceGuid, out var handle))
                return -1;

            if (handle.LastState.rgdwPOV is null || povIndex < 0 || povIndex >= 4)
                return -1;

            uint pov = handle.LastState.rgdwPOV[povIndex];

            // POV_CENTERED (0xFFFFFFFF) means centered
            if (pov == POV_CENTERED || (pov & 0xFFFF) == 0xFFFF)
                return -1;

            // POV value is in hundredths of degrees (0-35999)
            return (int)(pov / 100);
        }
    }

    /// <summary>
    /// Check if a device is currently open
    /// </summary>
    public bool IsDeviceOpen(Guid instanceGuid)
    {
        lock (_lock)
        {
            return _openDevices.ContainsKey(instanceGuid);
        }
    }

    /// <summary>
    /// Get the number of axes on a device
    /// </summary>
    public int GetAxisCount(Guid instanceGuid)
    {
        lock (_lock)
        {
            return _openDevices.TryGetValue(instanceGuid, out var handle) ? handle.AxisCount : 0;
        }
    }

    /// <summary>
    /// Get the number of buttons on a device
    /// </summary>
    public int GetButtonCount(Guid instanceGuid)
    {
        lock (_lock)
        {
            return _openDevices.TryGetValue(instanceGuid, out var handle) ? handle.ButtonCount : 0;
        }
    }

    /// <summary>
    /// Get the number of POV hats on a device
    /// </summary>
    public int GetPovCount(Guid instanceGuid)
    {
        lock (_lock)
        {
            return _openDevices.TryGetValue(instanceGuid, out var handle) ? handle.PovCount : 0;
        }
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
            lock (_lock)
            {
                foreach (var handle in _openDevices.Values)
                {
                    try
                    {
                        handle.Unacquire?.Invoke(handle.Device);
                        ReleaseDevice(handle.Device);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or EntryPointNotFoundException)
                    {
                        // Ignore errors during cleanup
                    }
                }
                _openDevices.Clear();
            }

            if (_directInput != IntPtr.Zero)
            {
                var releasePtr = GetVTableMethod(_directInput, 2);
                var release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(releasePtr);
                release(_directInput);
                _directInput = IntPtr.Zero;
            }

            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    ~DirectInputReader()
    {
        Dispose();
    }
}
