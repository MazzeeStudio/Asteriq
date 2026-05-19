using System.Runtime.InteropServices;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
    private static (string? keyName, List<string> modifiers) GetKeyNameAndModifiersFromKeys(Keys keys)
    {
        var modifiers = new List<string>();

        bool isAltGr = KeyboardHelper.IsKeyHeld(KeyboardHelper.VK_RMENU) && KeyboardHelper.IsKeyHeld(KeyboardHelper.VK_LCONTROL) && !KeyboardHelper.IsKeyHeld(KeyboardHelper.VK_RCONTROL);

        if (isAltGr)
        {
            modifiers.Add("AltGr");
        }
        else
        {
            if ((keys & Keys.Control) == Keys.Control)
            {
                if (KeyboardHelper.IsKeyHeld(KeyboardHelper.VK_RCONTROL))
                    modifiers.Add("RCtrl");
                else if (KeyboardHelper.IsKeyHeld(KeyboardHelper.VK_LCONTROL))
                    modifiers.Add("LCtrl");
            }
            if ((keys & Keys.Alt) == Keys.Alt)
            {
                if (KeyboardHelper.IsKeyHeld(KeyboardHelper.VK_RMENU))
                    modifiers.Add("RAlt");
                else if (KeyboardHelper.IsKeyHeld(KeyboardHelper.VK_LMENU))
                    modifiers.Add("LAlt");
            }
        }

        if ((keys & Keys.Shift) == Keys.Shift)
        {
            if (KeyboardHelper.IsKeyHeld(KeyboardHelper.VK_RSHIFT))
                modifiers.Add("RShift");
            else
                modifiers.Add("LShift");
        }

        var baseKey = keys & ~Keys.Modifiers;

        if (IsModifierKey(baseKey))
            return (null, modifiers);

        var keyName = baseKey switch
        {
            Keys.A => "A", Keys.B => "B", Keys.C => "C", Keys.D => "D",
            Keys.E => "E", Keys.F => "F", Keys.G => "G", Keys.H => "H",
            Keys.I => "I", Keys.J => "J", Keys.K => "K", Keys.L => "L",
            Keys.M => "M", Keys.N => "N", Keys.O => "O", Keys.P => "P",
            Keys.Q => "Q", Keys.R => "R", Keys.S => "S", Keys.T => "T",
            Keys.U => "U", Keys.V => "V", Keys.W => "W", Keys.X => "X",
            Keys.Y => "Y", Keys.Z => "Z",
            Keys.D0 => "0", Keys.D1 => "1", Keys.D2 => "2", Keys.D3 => "3",
            Keys.D4 => "4", Keys.D5 => "5", Keys.D6 => "6", Keys.D7 => "7",
            Keys.D8 => "8", Keys.D9 => "9",
            Keys.F1 => "F1", Keys.F2 => "F2", Keys.F3 => "F3", Keys.F4 => "F4",
            Keys.F5 => "F5", Keys.F6 => "F6", Keys.F7 => "F7", Keys.F8 => "F8",
            Keys.F9 => "F9", Keys.F10 => "F10", Keys.F11 => "F11", Keys.F12 => "F12",
            Keys.Space => "Space",
            Keys.Enter => "Enter",
            Keys.Tab => "Tab",
            Keys.Back => "Backspace",
            Keys.Delete => "Delete",
            Keys.Insert => "Insert",
            Keys.Home => "Home",
            Keys.End => "End",
            Keys.PageUp => "PageUp",
            Keys.PageDown => "PageDown",
            Keys.Up => "Up",
            Keys.Down => "Down",
            Keys.Left => "Left",
            Keys.Right => "Right",
            Keys.NumPad0 => "Num0", Keys.NumPad1 => "Num1", Keys.NumPad2 => "Num2",
            Keys.NumPad3 => "Num3", Keys.NumPad4 => "Num4", Keys.NumPad5 => "Num5",
            Keys.NumPad6 => "Num6", Keys.NumPad7 => "Num7", Keys.NumPad8 => "Num8",
            Keys.NumPad9 => "Num9",
            Keys.Multiply => "Num*",
            Keys.Add => "Num+",
            Keys.Subtract => "Num-",
            Keys.Decimal => "Num.",
            Keys.Divide => "Num/",
            _ => null
        };

        return (keyName, modifiers);
    }

    private static string GetModifierKeyName(Keys key) => KeyboardHelper.GetModifierKeyName(key);

    // Modifier key names as stored in OutputTarget.KeyName (case-insensitive).
    private static readonly HashSet<string> s_scModifierKeyNames =
        new(StringComparer.OrdinalIgnoreCase) { "RCtrl", "LCtrl", "RShift", "LShift", "RAlt", "LAlt" };

    /// <summary>Returns true when the given key name (as stored in OutputTarget.KeyName) is a modifier key.</summary>
    private static bool IsModifierKeyName(string? keyName) =>
        keyName is not null && s_scModifierKeyNames.Contains(keyName);

    internal static bool IsModifierKey(Keys key) => KeyboardHelper.IsModifierKey(key);

    /// <summary>
    /// Shared key capture logic. Modifier-only presses are saved as pending (returns false).
    /// Non-modifier key presses finalize immediately with any held modifiers (returns true).
    /// Pending modifiers finalize after ModifierWaitMs via CheckPendingModifierTimeout.
    /// </summary>
    private static bool HandleKeyCaptureEvent(Keys keyData,
        ref string? pendingModifier, ref long pendingModifierTicks,
        out string keyName, out List<string>? modifiers)
    {
        keyName = "";
        modifiers = null;

        var baseKey = keyData & Keys.KeyCode;
        if (IsModifierKey(baseKey))
        {
            // Save as pending â€” don't finalize yet, wait for possible combo key
            pendingModifier = GetModifierKeyName(baseKey);
            pendingModifierTicks = Environment.TickCount64;
            return false;
        }

        // Non-modifier key â€” finalize with any held modifiers
        var (name, mods) = GetKeyNameAndModifiersFromKeys(keyData);
        if (!string.IsNullOrEmpty(name))
        {
            keyName = name;
            modifiers = mods.Count > 0 ? mods : null;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if a pending modifier has timed out (held alone without a combo key).
    /// Called from the render loop. Returns true if a modifier-only capture should finalize.
    /// </summary>
    private static bool CheckPendingModifierTimeout(ref string? pendingModifier, ref long pendingModifierTicks,
        out string keyName)
    {
        keyName = "";
        if (pendingModifier is null) return false;
        if (Environment.TickCount64 - pendingModifierTicks < ModifierWaitMs) return false;

        keyName = pendingModifier;
        pendingModifier = null;
        return true;
    }

}