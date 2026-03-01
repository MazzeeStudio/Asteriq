using Asteriq.VJoy;

namespace Asteriq.Services;

/// <summary>
/// Maps 0-based axis indices to vJoy HID_USAGES constants.
/// Single definition shared by MappingEngine and NetworkInputService.
/// </summary>
public static class VJoyAxisHelper
{
    /// <summary>
    /// Convert a 0-based axis index to the corresponding vJoy HID_USAGES value.
    /// Indices: 0=X, 1=Y, 2=Z, 3=RX, 4=RY, 5=RZ, 6=SL0, 7=SL1.
    /// Out-of-range indices fall back to HID_USAGES.X.
    /// </summary>
    public static HID_USAGES IndexToHidUsage(int index) => index switch
    {
        0 => HID_USAGES.X,
        1 => HID_USAGES.Y,
        2 => HID_USAGES.Z,
        3 => HID_USAGES.RX,
        4 => HID_USAGES.RY,
        5 => HID_USAGES.RZ,
        6 => HID_USAGES.SL0,
        7 => HID_USAGES.SL1,
        _ => HID_USAGES.X
    };
}
