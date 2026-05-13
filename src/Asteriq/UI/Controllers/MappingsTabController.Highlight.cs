using System.Runtime.InteropServices;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
    private void SelectFirstRowIfUnselected()
    {
        if (_selectedMappingRow >= 0) return;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;
        _selectedMappingRow = 0;
        _selectionIsExplicit = true;
        _highlight.ControlDef = GetControlForRow(_selectedMappingRow);
        LoadOutputTypeStateForRow();
        LoadAxisSettingsForRow();
    }

    /// <summary>
    /// Check if any pressed input maps to a vJoy output and highlight it.
    /// Called from MainForm.OnInputReceived when on Mappings tab.
    /// </summary>
    public void CheckForMappingHighlight(DeviceInputState state)
    {
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        // Get previous button state for this device (for rising-edge detection)
        _highlight.PrevButtonState.TryGetValue(state.DeviceName, out var prevButtons);

        // Check button presses - only trigger on rising edge (was NOT pressed, now IS pressed)
        for (int i = 0; i < state.Buttons.Length; i++)
        {
            bool isPressed = state.Buttons[i];
            bool wasPressed = prevButtons is not null && i < prevButtons.Length && prevButtons[i];

            // Only trigger on rising edge (transition from not-pressed to pressed)
            if (!isPressed || wasPressed) continue;

            // Check debounce - don't re-highlight the same button too quickly
            string debounceKey = $"{state.DeviceName}:{i}";
            if (_highlight.Debounce.TryGetValue(debounceKey, out var lastTime))
            {
                var elapsed = Environment.TickCount64 - lastTime;
                if (elapsed < HighlightDebounceCooldownMs)
                    continue; // Skip - too soon since last highlight for this button
            }

            // Look for button mapping from this device/button (match by device name and input)
            var mapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Inputs.Any(input =>
                    input.DeviceName == state.DeviceName &&
                    input.Type == InputType.Button &&
                    input.Index == i));

            if (mapping is not null)
            {
                // Found a mapping - highlight this row
                _highlight.Row = mapping.Output.Index;
                _highlight.VJoyDevice = mapping.Output.VJoyDevice;
                _highlight.StartTicks = Environment.TickCount64;
                _highlight.Debounce[debounceKey] = Environment.TickCount64; // Record highlight time for debounce

                // Show lead line on the silhouette only if this mapping belongs to the currently viewed vJoy device
                bool isCurrentVJoy = _ctx.VJoyDevices.Count > 0 &&
                                     _ctx.SelectedVJoyDeviceIndex < _ctx.VJoyDevices.Count &&
                                     _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex].Id == mapping.Output.VJoyDevice;
                if (isCurrentVJoy && _ctx.MappingsPrimaryDeviceMap is not null)
                {
                    // SDL2 button index i â†’ device-map binding "button{i+1}"
                    string physBinding = $"button{i + 1}";
                    var control = _ctx.MappingsPrimaryDeviceMap.FindControlByBinding(physBinding);
                    if (control is not null)
                    {
                        _highlight.ControlDef = control;
                        _highlight.ControlHighlightTicks = Environment.TickCount64;
                    }
                }

                // Auto-scroll to bring the mapped row into view (Buttons category only)
                if (_autoScroll.Enabled && _mappingCategory == 0)
                {
                    bool hasVJoy = _ctx.VJoyDevices.Count > 0 && _ctx.SelectedVJoyDeviceIndex < _ctx.VJoyDevices.Count;
                    if (hasVJoy && _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex].Id == _highlight.VJoyDevice)
                    {
                        const float rowHeight = 32f;
                        const float rowGap = 4f;
                        float rowTop = mapping.Output.Index * (rowHeight + rowGap);
                        // Center the row in the visible list area
                        float targetScroll = rowTop - _listScroll.ListBounds.Height / 2f + rowHeight / 2f;
                        float maxScroll = Math.Max(0, _listScroll.ContentHeight - _listScroll.ListBounds.Height);
                        _listScroll.ScrollOffset = Math.Clamp(targetScroll, 0, maxScroll);
                    }
                }

                _highlight.FlashText = null; // Clear any "no mapping" indicator
                break;
            }
            else if (_autoScroll.Enabled)
            {
                // Button pressed with no mapping - flash an indicator
                _highlight.FlashText = $"BUTTON {i + 1}: NO MAPPING";
                _highlight.FlashTicks = Environment.TickCount64;
                _highlight.Debounce[debounceKey] = Environment.TickCount64; // Debounce the no-mapping flash too
            }
        }

        // Store current button state for next frame comparison
        _highlight.PrevButtonState[state.DeviceName] = (bool[])state.Buttons.Clone();
    }

}