using System.Runtime.InteropServices;

namespace Asteriq.UI;

/// <summary>
/// Shared keyboard utilities for modifier key detection and naming.
/// Centralises virtual key constants, GetAsyncKeyState interop, and
/// modifier identification so they are not duplicated across controllers and dialogs.
/// </summary>
internal static class KeyboardHelper
{
    // Virtual key codes for left/right modifier variants
    public const int VK_LSHIFT = 0xA0;
    public const int VK_RSHIFT = 0xA1;
    public const int VK_LCONTROL = 0xA2;
    public const int VK_RCONTROL = 0xA3;
    public const int VK_LMENU = 0xA4;   // Left Alt
    public const int VK_RMENU = 0xA5;   // Right Alt

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    /// <summary>
    /// Returns true if the given virtual key is currently held down.
    /// </summary>
    public static bool IsKeyHeld(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>
    /// Returns true when the given <see cref="Keys"/> value is a modifier-only key
    /// (Ctrl, Shift, Alt, Win — any variant).
    /// </summary>
    public static bool IsModifierKey(Keys key) => key is
        Keys.ControlKey or Keys.ShiftKey or Keys.Menu or
        Keys.Control or Keys.Shift or Keys.Alt or
        Keys.LControlKey or Keys.RControlKey or
        Keys.LShiftKey or Keys.RShiftKey or
        Keys.LMenu or Keys.RMenu or
        Keys.LWin or Keys.RWin;

    /// <summary>
    /// Gets the specific modifier key name (left/right variant) by polling
    /// <see cref="GetAsyncKeyState"/> to distinguish L/R when Windows
    /// reports the generic key code.
    /// </summary>
    public static string GetModifierKeyName(Keys key)
    {
        if (key is Keys.ControlKey or Keys.Control)
        {
            if (IsKeyHeld(VK_RCONTROL)) return "RCtrl";
            return "LCtrl";
        }
        if (key == Keys.LControlKey) return "LCtrl";
        if (key == Keys.RControlKey) return "RCtrl";

        if (key is Keys.ShiftKey or Keys.Shift)
        {
            if (IsKeyHeld(VK_RSHIFT)) return "RShift";
            return "LShift";
        }
        if (key == Keys.LShiftKey) return "LShift";
        if (key == Keys.RShiftKey) return "RShift";

        if (key is Keys.Menu or Keys.Alt)
        {
            if (IsKeyHeld(VK_RMENU)) return "RAlt";
            return "LAlt";
        }
        if (key == Keys.LMenu) return "LAlt";
        if (key == Keys.RMenu) return "RAlt";

        return key.ToString();
    }
}
