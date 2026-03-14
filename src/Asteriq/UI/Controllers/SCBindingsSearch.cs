using Asteriq.Models;
using Asteriq.Services;

namespace Asteriq.UI.Controllers;

/// <summary>
/// Pure search-matching logic for the SC Bindings tab.
/// Extracted as a static class so it can be unit-tested without a WinForms controller.
/// </summary>
public static class SCBindingsSearch
{
    /// <summary>
    /// Returns true if <paramref name="action"/> matches a free-text search string.
    /// Checks action name, formatted name, category, actionmap, and all user/default bindings
    /// using case-insensitive substring matching.
    /// </summary>
    /// <param name="action">The action to test.</param>
    /// <param name="bindings">All user bindings in the export profile.</param>
    /// <param name="searchLower">Search term, already lower-cased by the caller.</param>
    public static bool MatchesTextSearch(SCAction action, IEnumerable<SCActionBinding> bindings, string searchLower)
    {
        if (action.ActionName.ToLowerInvariant().Contains(searchLower))
            return true;

        if (SCCategoryMapper.FormatActionName(action.ActionName).ToLowerInvariant().Contains(searchLower))
            return true;

        if (SCCategoryMapper.GetCategoryName(action.ActionMap).ToLowerInvariant().Contains(searchLower))
            return true;

        if (action.ActionMap.ToLowerInvariant().Contains(searchLower))
            return true;

        foreach (var b in bindings.Where(b => b.ActionMap == action.ActionMap && b.ActionName == action.ActionName))
        {
            if (b.InputName.ToLowerInvariant().Contains(searchLower))
                return true;

            foreach (var modifier in b.Modifiers)
            {
                if (modifier.ToLowerInvariant().Contains(searchLower))
                    return true;
            }

            // Also check the composed "rctrl+button3" form so a modifier+button search works.
            if (b.Modifiers.Count > 0)
            {
                string full = (string.Join("+", b.Modifiers) + "+" + b.InputName).ToLowerInvariant();
                if (full.Contains(searchLower))
                    return true;
            }
        }

        foreach (var binding in action.DefaultBindings)
        {
            if (binding.Input.ToLowerInvariant().Contains(searchLower))
                return true;

            if (binding.FullInput.ToLowerInvariant().Contains(searchLower))
                return true;

            foreach (var modifier in binding.Modifiers)
            {
                if (modifier.ToLowerInvariant().Contains(searchLower))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true if <paramref name="action"/> has a user binding that exactly matches
    /// the captured button on the specified device column.
    ///
    /// Unlike <see cref="MatchesTextSearch"/>, this method:
    /// <list type="bullet">
    ///   <item>Uses exact (not substring) InputName comparison — "button3" will NOT match "button30".</item>
    ///   <item>Restricts the search to the column that produced the capture, so pressing a button on
    ///     JS1 only shows JS1 bindings.</item>
    ///   <item>Requires the modifier set to match exactly — plain button3 will not match rctrl+button3.</item>
    /// </list>
    /// </summary>
    /// <param name="action">The action to test.</param>
    /// <param name="bindings">All user bindings in the export profile.</param>
    /// <param name="capturedInput">Raw input name, e.g. "button3" or "hat1_up" (no modifier prefix).</param>
    /// <param name="capturedModifier">Modifier string if one was held, e.g. "rctrl"; null for no modifier.</param>
    /// <param name="vjoyDeviceId">vJoy device ID of the highlighted column, or null if the column is not
    ///   a vJoy joystick (physical / keyboard / mouse / no column selected).</param>
    /// <param name="physicalDeviceId">HID device path of the highlighted physical column, or null.</param>
    public static bool MatchesButtonCapture(
        SCAction action,
        IEnumerable<SCActionBinding> bindings,
        string capturedInput,
        string? capturedModifier,
        uint? vjoyDeviceId,
        string? physicalDeviceId)
    {
        foreach (var b in bindings.Where(b => b.ActionMap == action.ActionMap && b.ActionName == action.ActionName))
        {
            // ── Column filter ───────────────────────────────────────────────
            if (vjoyDeviceId.HasValue)
            {
                // vJoy column: must match this specific vJoy slot (not a physical binding)
                if (b.DeviceType != SCDeviceType.Joystick
                    || b.PhysicalDeviceId is not null
                    || b.VJoyDevice != vjoyDeviceId.Value)
                    continue;
            }
            else if (physicalDeviceId is not null)
            {
                // Physical column: must match this specific HID path
                if (b.DeviceType != SCDeviceType.Joystick || b.PhysicalDeviceId != physicalDeviceId)
                    continue;
            }
            else
            {
                // No column constraint (device not matched to a column) — accept any joystick binding
                if (b.DeviceType != SCDeviceType.Joystick)
                    continue;
            }

            // ── Exact input name match ──────────────────────────────────────
            if (!b.InputName.Equals(capturedInput, StringComparison.OrdinalIgnoreCase))
                continue;

            // ── Exact modifier match ────────────────────────────────────────
            if (capturedModifier is null)
            {
                // No modifier captured — only match bindings that also have no modifier
                if (b.Modifiers.Count > 0)
                    continue;
            }
            else
            {
                // Modifier captured — binding must contain exactly this modifier
                if (!b.Modifiers.Any(m => m.Equals(capturedModifier, StringComparison.OrdinalIgnoreCase)))
                    continue;
            }

            return true;
        }

        return false;
    }
}
