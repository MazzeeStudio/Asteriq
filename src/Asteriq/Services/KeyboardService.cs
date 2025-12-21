using System.Runtime.InteropServices;

namespace Asteriq.Services;

/// <summary>
/// Service for sending keyboard input using Windows SendInput API
/// </summary>
public class KeyboardService : IDisposable
{
    // Track key states to avoid repeated press/release
    private readonly Dictionary<int, bool> _keyStates = new();
    private readonly object _lock = new();

    #region Windows API

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    // MapVirtualKey types
    private const uint MAPVK_VK_TO_VSC = 0;
    private const uint MAPVK_VK_TO_VSC_EX = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    #endregion

    /// <summary>
    /// Set a key's pressed state using virtual key code
    /// </summary>
    public void SetKey(int virtualKeyCode, bool pressed, int[]? modifiers = null)
    {
        lock (_lock)
        {
            // Check if state actually changed
            _keyStates.TryGetValue(virtualKeyCode, out bool currentState);
            if (currentState == pressed)
                return;

            _keyStates[virtualKeyCode] = pressed;

            if (pressed)
            {
                // Press modifiers first
                if (modifiers is not null)
                {
                    foreach (var mod in modifiers)
                        SendKey(mod, true);
                }

                SendKey(virtualKeyCode, true);
            }
            else
            {
                SendKey(virtualKeyCode, false);

                // Release modifiers
                if (modifiers is not null)
                {
                    foreach (var mod in modifiers.Reverse())
                        SendKey(mod, false);
                }
            }
        }
    }

    /// <summary>
    /// Set a key's pressed state using key name and modifier names
    /// </summary>
    public void SetKey(string? keyName, bool pressed, List<string>? modifierNames = null)
    {
        if (string.IsNullOrEmpty(keyName))
            return;

        var keyCode = GetKeyCode(keyName);
        if (!keyCode.HasValue)
            return;

        int[]? modifierCodes = null;
        if (modifierNames is not null && modifierNames.Count > 0)
        {
            modifierCodes = modifierNames
                .Select(GetKeyCode)
                .Where(c => c.HasValue)
                .Select(c => c!.Value)
                .ToArray();
        }

        SetKey(keyCode.Value, pressed, modifierCodes);
    }

    /// <summary>
    /// Send a single key press or release
    /// </summary>
    private void SendKey(int virtualKeyCode, bool press)
    {
        // Get the scan code for this virtual key
        // Use MAPVK_VK_TO_VSC_EX to get extended scan code info
        uint scanCodeEx = MapVirtualKey((uint)virtualKeyCode, MAPVK_VK_TO_VSC_EX);
        ushort scanCode = (ushort)(scanCodeEx & 0xFF);
        bool isExtended = IsExtendedKey(virtualKeyCode) || (scanCodeEx & 0xE000) != 0;

        uint flags = press ? 0u : KEYEVENTF_KEYUP;
        if (isExtended)
        {
            flags |= KEYEVENTF_EXTENDEDKEY;
        }

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)virtualKeyCode,
                    wScan = scanCode,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Check if a key is an extended key
    /// </summary>
    private static bool IsExtendedKey(int vk)
    {
        return vk == VK_RCONTROL || vk == VK_RMENU || vk == VK_RSHIFT ||
               vk == VK_INSERT || vk == VK_DELETE || vk == VK_HOME ||
               vk == VK_END || vk == VK_PRIOR || vk == VK_NEXT ||
               vk == VK_LEFT || vk == VK_RIGHT || vk == VK_UP || vk == VK_DOWN ||
               vk == VK_NUMLOCK || vk == VK_DIVIDE;
    }

    /// <summary>
    /// Release all currently pressed keys
    /// </summary>
    public void ReleaseAll()
    {
        lock (_lock)
        {
            foreach (var kvp in _keyStates.Where(k => k.Value))
            {
                SendKey(kvp.Key, false);
            }
            _keyStates.Clear();
        }
    }

    public void Dispose()
    {
        ReleaseAll();
    }

    #region Virtual Key Codes

    // Common virtual key codes
    public const int VK_LBUTTON = 0x01;
    public const int VK_RBUTTON = 0x02;
    public const int VK_CANCEL = 0x03;
    public const int VK_BACK = 0x08;
    public const int VK_TAB = 0x09;
    public const int VK_RETURN = 0x0D;
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;  // Alt
    public const int VK_PAUSE = 0x13;
    public const int VK_CAPITAL = 0x14;  // Caps Lock
    public const int VK_ESCAPE = 0x1B;
    public const int VK_SPACE = 0x20;
    public const int VK_PRIOR = 0x21;  // Page Up
    public const int VK_NEXT = 0x22;   // Page Down
    public const int VK_END = 0x23;
    public const int VK_HOME = 0x24;
    public const int VK_LEFT = 0x25;
    public const int VK_UP = 0x26;
    public const int VK_RIGHT = 0x27;
    public const int VK_DOWN = 0x28;
    public const int VK_INSERT = 0x2D;
    public const int VK_DELETE = 0x2E;

    // Numbers 0-9: 0x30-0x39
    // Letters A-Z: 0x41-0x5A

    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;

    // Numpad
    public const int VK_NUMPAD0 = 0x60;
    public const int VK_NUMPAD1 = 0x61;
    public const int VK_NUMPAD2 = 0x62;
    public const int VK_NUMPAD3 = 0x63;
    public const int VK_NUMPAD4 = 0x64;
    public const int VK_NUMPAD5 = 0x65;
    public const int VK_NUMPAD6 = 0x66;
    public const int VK_NUMPAD7 = 0x67;
    public const int VK_NUMPAD8 = 0x68;
    public const int VK_NUMPAD9 = 0x69;
    public const int VK_MULTIPLY = 0x6A;
    public const int VK_ADD = 0x6B;
    public const int VK_SUBTRACT = 0x6D;
    public const int VK_DECIMAL = 0x6E;
    public const int VK_DIVIDE = 0x6F;

    // Function keys
    public const int VK_F1 = 0x70;
    public const int VK_F2 = 0x71;
    public const int VK_F3 = 0x72;
    public const int VK_F4 = 0x73;
    public const int VK_F5 = 0x74;
    public const int VK_F6 = 0x75;
    public const int VK_F7 = 0x76;
    public const int VK_F8 = 0x77;
    public const int VK_F9 = 0x78;
    public const int VK_F10 = 0x79;
    public const int VK_F11 = 0x7A;
    public const int VK_F12 = 0x7B;

    public const int VK_NUMLOCK = 0x90;
    public const int VK_SCROLL = 0x91;

    // Left/Right variants
    public const int VK_LSHIFT = 0xA0;
    public const int VK_RSHIFT = 0xA1;
    public const int VK_LCONTROL = 0xA2;
    public const int VK_RCONTROL = 0xA3;
    public const int VK_LMENU = 0xA4;  // Left Alt
    public const int VK_RMENU = 0xA5;  // Right Alt

    // OEM keys
    public const int VK_OEM_1 = 0xBA;      // ;:
    public const int VK_OEM_PLUS = 0xBB;   // =+
    public const int VK_OEM_COMMA = 0xBC;  // ,<
    public const int VK_OEM_MINUS = 0xBD;  // -_
    public const int VK_OEM_PERIOD = 0xBE; // .>
    public const int VK_OEM_2 = 0xBF;      // /?
    public const int VK_OEM_3 = 0xC0;      // `~
    public const int VK_OEM_4 = 0xDB;      // [{
    public const int VK_OEM_5 = 0xDC;      // \|
    public const int VK_OEM_6 = 0xDD;      // ]}
    public const int VK_OEM_7 = 0xDE;      // '"

    /// <summary>
    /// Get virtual key code from a friendly name
    /// </summary>
    public static int? GetKeyCode(string keyName)
    {
        var name = keyName.ToUpperInvariant().Trim();

        return name switch
        {
            // Modifiers
            "CTRL" or "CONTROL" or "LCTRL" or "LCONTROL" => VK_LCONTROL,
            "RCTRL" or "RCONTROL" => VK_RCONTROL,
            "SHIFT" or "LSHIFT" => VK_LSHIFT,
            "RSHIFT" => VK_RSHIFT,
            "ALT" or "LALT" or "MENU" or "LMENU" => VK_LMENU,
            "RALT" or "RMENU" => VK_RMENU,

            // Common keys
            "ENTER" or "RETURN" => VK_RETURN,
            "ESC" or "ESCAPE" => VK_ESCAPE,
            "SPACE" or "SPACEBAR" => VK_SPACE,
            "TAB" => VK_TAB,
            "BACKSPACE" or "BACK" => VK_BACK,
            "DELETE" or "DEL" => VK_DELETE,
            "INSERT" or "INS" => VK_INSERT,
            "HOME" => VK_HOME,
            "END" => VK_END,
            "PAGEUP" or "PGUP" => VK_PRIOR,
            "PAGEDOWN" or "PGDN" => VK_NEXT,
            "UP" => VK_UP,
            "DOWN" => VK_DOWN,
            "LEFT" => VK_LEFT,
            "RIGHT" => VK_RIGHT,
            "CAPSLOCK" or "CAPS" => VK_CAPITAL,
            "NUMLOCK" => VK_NUMLOCK,
            "SCROLLLOCK" => VK_SCROLL,
            "PAUSE" => VK_PAUSE,

            // Function keys
            "F1" => VK_F1,
            "F2" => VK_F2,
            "F3" => VK_F3,
            "F4" => VK_F4,
            "F5" => VK_F5,
            "F6" => VK_F6,
            "F7" => VK_F7,
            "F8" => VK_F8,
            "F9" => VK_F9,
            "F10" => VK_F10,
            "F11" => VK_F11,
            "F12" => VK_F12,

            // Numpad
            "NUM0" or "NUMPAD0" => VK_NUMPAD0,
            "NUM1" or "NUMPAD1" => VK_NUMPAD1,
            "NUM2" or "NUMPAD2" => VK_NUMPAD2,
            "NUM3" or "NUMPAD3" => VK_NUMPAD3,
            "NUM4" or "NUMPAD4" => VK_NUMPAD4,
            "NUM5" or "NUMPAD5" => VK_NUMPAD5,
            "NUM6" or "NUMPAD6" => VK_NUMPAD6,
            "NUM7" or "NUMPAD7" => VK_NUMPAD7,
            "NUM8" or "NUMPAD8" => VK_NUMPAD8,
            "NUM9" or "NUMPAD9" => VK_NUMPAD9,
            "NUM*" or "MULTIPLY" => VK_MULTIPLY,
            "NUM+" or "ADD" => VK_ADD,
            "NUM-" or "SUBTRACT" => VK_SUBTRACT,
            "NUM." or "DECIMAL" => VK_DECIMAL,
            "NUM/" or "DIVIDE" => VK_DIVIDE,

            // Single letter (A-Z)
            _ when name.Length == 1 && char.IsLetter(name[0]) => name[0],

            // Single digit (0-9)
            _ when name.Length == 1 && char.IsDigit(name[0]) => 0x30 + (name[0] - '0'),

            _ => null
        };
    }

    /// <summary>
    /// Get a friendly name for a virtual key code
    /// </summary>
    public static string GetKeyName(int vk)
    {
        return vk switch
        {
            VK_LCONTROL => "LCtrl",
            VK_RCONTROL => "RCtrl",
            VK_LSHIFT => "LShift",
            VK_RSHIFT => "RShift",
            VK_LMENU => "LAlt",
            VK_RMENU => "RAlt",
            VK_RETURN => "Enter",
            VK_ESCAPE => "Esc",
            VK_SPACE => "Space",
            VK_TAB => "Tab",
            VK_BACK => "Backspace",
            VK_DELETE => "Delete",
            VK_INSERT => "Insert",
            VK_HOME => "Home",
            VK_END => "End",
            VK_PRIOR => "PageUp",
            VK_NEXT => "PageDown",
            VK_UP => "Up",
            VK_DOWN => "Down",
            VK_LEFT => "Left",
            VK_RIGHT => "Right",
            >= VK_F1 and <= VK_F12 => $"F{vk - VK_F1 + 1}",
            >= 0x30 and <= 0x39 => ((char)vk).ToString(),
            >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            _ => $"0x{vk:X2}"
        };
    }

    #endregion
}
