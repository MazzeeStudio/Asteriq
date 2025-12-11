using HidSharp;
using Asteriq.Models;

namespace Asteriq.Services;

/// <summary>
/// Service for getting unique device identification and axis type information using HidSharp.
/// This provides more reliable device matching than SDL2's GUIDs which are not unique per device.
/// </summary>
public class HidDeviceService
{
    // HID Usage Page and Usage IDs
    private const uint HID_USAGE_PAGE_GENERIC = 0x01;
    private const uint HID_USAGE_JOYSTICK = 0x04;
    private const uint HID_USAGE_GAMEPAD = 0x05;
    private const uint HID_USAGE_MULTI_AXIS_CONTROLLER = 0x08;

    // HID axis usage IDs (within Generic Desktop page)
    private const uint HID_USAGE_X = 0x30;
    private const uint HID_USAGE_Y = 0x31;
    private const uint HID_USAGE_Z = 0x32;
    private const uint HID_USAGE_RX = 0x33;
    private const uint HID_USAGE_RY = 0x34;
    private const uint HID_USAGE_RZ = 0x35;
    private const uint HID_USAGE_SLIDER = 0x36;
    private const uint HID_USAGE_DIAL = 0x37;
    private const uint HID_USAGE_WHEEL = 0x38;

    // vJoy virtual device identifiers
    private const int VJOY_VENDOR_ID = 0x1234;
    private const int VJOY_PRODUCT_ID = 0xBEAD;

    /// <summary>
    /// Information about a HID device including unique path and axis types
    /// </summary>
    public class HidDeviceInfo
    {
        public string DevicePath { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public int VendorId { get; set; }
        public int ProductId { get; set; }
        public string SerialNumber { get; set; } = string.Empty;
        public List<AxisInfo> Axes { get; set; } = new();
        public int ButtonCount { get; set; }
        public int HatCount { get; set; }
    }

    /// <summary>
    /// Enumerate all joystick/gamepad HID devices with their axis type information
    /// </summary>
    public List<HidDeviceInfo> EnumerateDevices()
    {
        var result = new List<HidDeviceInfo>();
        var hidDevices = DeviceList.Local.GetHidDevices();

        foreach (var device in hidDevices)
        {
            try
            {
                if (!IsJoystickDevice(device))
                    continue;

                // Skip vJoy virtual devices
                if (device.VendorID == VJOY_VENDOR_ID && device.ProductID == VJOY_PRODUCT_ID)
                    continue;

                var deviceInfo = new HidDeviceInfo
                {
                    DevicePath = device.DevicePath,
                    ProductName = GetProductName(device),
                    VendorId = device.VendorID,
                    ProductId = device.ProductID,
                    SerialNumber = GetSerialNumber(device)
                };

                // Parse HID report descriptor to get axis types
                PopulateAxisInfo(device, deviceInfo);

                result.Add(deviceInfo);
            }
            catch
            {
                // Skip devices that fail to enumerate
            }
        }

        return result;
    }

    /// <summary>
    /// Find a HID device that matches the given SDL device name.
    /// Returns null if no match is found.
    /// </summary>
    public HidDeviceInfo? FindMatchingDevice(string sdlDeviceName, HashSet<string>? excludePaths = null)
    {
        var devices = EnumerateDevices();

        return devices.FirstOrDefault(d =>
            (excludePaths == null || !excludePaths.Contains(d.DevicePath)) &&
            d.ProductName.Equals(sdlDeviceName, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsJoystickDevice(HidDevice device)
    {
        try
        {
            var reportDescriptor = device.GetReportDescriptor();

            foreach (var deviceItem in reportDescriptor.DeviceItems)
            {
                foreach (var usage in deviceItem.Usages.GetAllValues())
                {
                    var usagePage = (usage >> 16) & 0xFFFF;
                    var usageId = usage & 0xFFFF;

                    if (usagePage == HID_USAGE_PAGE_GENERIC &&
                        (usageId == HID_USAGE_JOYSTICK ||
                         usageId == HID_USAGE_GAMEPAD ||
                         usageId == HID_USAGE_MULTI_AXIS_CONTROLLER))
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // Fallback: check by name
            return IsJoystickByName(device);
        }

        return false;
    }

    private bool IsJoystickByName(HidDevice device)
    {
        var name = GetProductName(device).ToLowerInvariant();
        var keywords = new[]
        {
            "joystick", "throttle", "stick", "hotas", "hosas",
            "gladiator", "virpil", "vkb", "warthog", "t16000",
            "x52", "x55", "x56", "saitek", "pedals", "rudder",
            "cougar", "defender", "kosmosima", "mfd", "flight", "alpha"
        };

        return keywords.Any(k => name.Contains(k));
    }

    private string GetProductName(HidDevice device)
    {
        try
        {
            var name = device.GetProductName();
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        catch (Exception)
        {
            // Some HID devices don't support GetProductName - fall through to try GetFriendlyName
        }

        try
        {
            var name = device.GetFriendlyName();
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        catch (Exception)
        {
            // Some HID devices don't support GetFriendlyName - fall through to default name
        }

        return $"Unknown Device ({device.VendorID:X4}:{device.ProductID:X4})";
    }

    private string GetSerialNumber(HidDevice device)
    {
        try
        {
            return device.GetSerialNumber() ?? "";
        }
        catch (Exception)
        {
            // Some HID devices don't support serial number retrieval
            return "";
        }
    }

    private void PopulateAxisInfo(HidDevice device, HidDeviceInfo deviceInfo)
    {
        try
        {
            var reportDescriptor = device.GetReportDescriptor();
            var axes = new List<AxisInfo>();
            int axisIndex = 0;
            int buttonCount = 0;
            int hatCount = 0;
            int sliderCount = 0;

            foreach (var deviceItem in reportDescriptor.DeviceItems)
            {
                foreach (var report in deviceItem.InputReports)
                {
                    foreach (var dataItem in report.DataItems)
                    {
                        var usages = dataItem.Usages.GetAllValues().ToList();

                        foreach (var usage in usages)
                        {
                            var usagePage = (usage >> 16) & 0xFFFF;
                            var usageId = usage & 0xFFFF;

                            if (usagePage == HID_USAGE_PAGE_GENERIC)
                            {
                                var axisType = UsageToAxisType(usageId, ref sliderCount);
                                if (axisType != AxisType.Unknown)
                                {
                                    axes.Add(new AxisInfo
                                    {
                                        Index = axisIndex++,
                                        Type = axisType,
                                        Name = axisType.ToString()
                                    });
                                }
                                else if (usageId == 0x39) // Hat switch
                                {
                                    hatCount++;
                                }
                            }
                            else if (usagePage == 0x09) // Button page
                            {
                                buttonCount++;
                            }
                        }

                        // For button arrays
                        if (dataItem.IsArray && usages.Count == 0)
                        {
                            buttonCount += dataItem.ElementCount;
                        }
                    }
                }
            }

            // Set the parsed values
            deviceInfo.Axes = axes;
            deviceInfo.ButtonCount = Math.Min(Math.Max(buttonCount, 0), 128);
            deviceInfo.HatCount = Math.Min(hatCount, 4);
        }
        catch
        {
            // Leave defaults if parsing fails
        }
    }

    private AxisType UsageToAxisType(uint usageId, ref int sliderCount)
    {
        return usageId switch
        {
            HID_USAGE_X => AxisType.X,
            HID_USAGE_Y => AxisType.Y,
            HID_USAGE_Z => AxisType.Z,
            HID_USAGE_RX => AxisType.RX,
            HID_USAGE_RY => AxisType.RY,
            HID_USAGE_RZ => AxisType.RZ,
            HID_USAGE_SLIDER or HID_USAGE_DIAL or HID_USAGE_WHEEL => AxisType.Slider,
            _ => AxisType.Unknown
        };
    }
}
