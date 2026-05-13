using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
    private bool TryGetSelectedButtonContext(
        out MappingProfile profile,
        out VJoyDeviceInfo vjoyDevice,
        out int outputIndex,
        out ButtonMapping? mapping)
    {
        profile = null!;
        vjoyDevice = null!;
        outputIndex = -1;
        mapping = null;

        if (_mappingCategory != 0) return false;
        if (_selectedMappingRow < 0) return false;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return false;

        var p = _ctx.ProfileManager.ActiveProfile;
        if (p is null) return false;

        var v = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int oi = _selectedMappingRow;

        profile = p;
        vjoyDevice = v;
        outputIndex = oi;

        mapping = p.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == v.Id &&
            m.Output.Index == oi);
        return true;
    }

    private void LoadOutputTypeStateForRow()
    {
        // Reset state
        _keyboardOutput.IsKeyboard = false;
        _keyboardOutput.SelectedKeyName = "";
        _keyboardOutput.SelectedModifiers = null;
        _keyboardOutput.IsCapturing = false;
        _buttonMode.SelectedMode = ButtonMode.Normal;
        _buttonMode.PulseDurationMs = 100;
        _buttonMode.HoldDurationMs = 500;

        if (!TryGetSelectedButtonContext(out _, out _, out _, out var mapping)) return;
        if (mapping is null) return;

        _keyboardOutput.IsKeyboard = mapping.Output.Type == OutputType.Keyboard;
        _keyboardOutput.SelectedKeyName = mapping.Output.KeyName ?? "";
        _keyboardOutput.SelectedModifiers = mapping.Output.Modifiers?.ToList();
        _buttonMode.SelectedMode = mapping.Mode;
        _buttonMode.PulseDurationMs = mapping.PulseDurationMs;
        _buttonMode.HoldDurationMs = mapping.HoldDurationMs;
    }

    private void UpdateButtonModeForSelected()
    {
        if (!TryGetSelectedButtonContext(out var profile, out _, out _, out var mapping)) return;
        if (mapping is null) return;

        mapping.Mode = _buttonMode.SelectedMode;
        profile.ModifiedAt = DateTime.UtcNow;
        _ctx.ProfileManager.SaveActiveProfile();
    }

    private void UpdateOutputTypeForSelected()
    {
        if (!TryGetSelectedButtonContext(out var profile, out _, out _, out var mapping)) return;
        if (mapping is null) return;

        mapping.Output.Type = _keyboardOutput.IsKeyboard ? OutputType.Keyboard : OutputType.VJoyButton;
        if (!_keyboardOutput.IsKeyboard)
        {
            mapping.Output.KeyName = null;
            mapping.Output.Modifiers = null;
        }
        else if (!string.IsNullOrEmpty(_keyboardOutput.SelectedKeyName))
        {
            mapping.Output.KeyName = _keyboardOutput.SelectedKeyName;
        }
        profile.ModifiedAt = DateTime.UtcNow;
        _ctx.ProfileManager.SaveActiveProfile();
    }

    private void UpdateKeyNameForSelected()
    {
        if (!TryGetSelectedButtonContext(out var profile, out var vjoyDevice, out var outputIndex, out var mapping)) return;

        if (mapping is null && !string.IsNullOrEmpty(_keyboardOutput.SelectedKeyName))
        {
            // No existing mapping - need to capture a physical input first
            _keyboardOutput.PendingKey = _keyboardOutput.SelectedKeyName;
            _keyboardOutput.PendingModifiers = _keyboardOutput.SelectedModifiers?.ToList();
            _keyboardOutput.PendingOutputIndex = outputIndex;
            _keyboardOutput.PendingVJoyDevice = vjoyDevice.Id;

            _ = StartPendingKeyboardInputListeningAsync();
            return;
        }

        if (mapping is not null)
        {
            if (!string.IsNullOrEmpty(_keyboardOutput.SelectedKeyName))
            {
                mapping.Output.Type = OutputType.Keyboard;
            }
            mapping.Output.KeyName = _keyboardOutput.SelectedKeyName;
            mapping.Output.Modifiers = _keyboardOutput.SelectedModifiers?.ToList();
            profile.ModifiedAt = DateTime.UtcNow;
            _ctx.ProfileManager.SaveActiveProfile();
        }
    }

    /// <summary>
    /// Clear all bindings (keyboard and input sources) for the selected button mapping
    /// </summary>
    private void ClearSelectedButtonMapping()
    {
        if (!TryGetSelectedButtonContext(out var profile, out _, out _, out var mapping)) return;
        if (mapping is null) return;

        profile.ButtonMappings.Remove(mapping);
        profile.ModifiedAt = DateTime.UtcNow;
        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.OnMappingsChanged();

        _keyboardOutput.SelectedKeyName = "";
        _keyboardOutput.SelectedModifiers = null;
        _keyboardOutput.IsKeyboard = false;
        _buttonMode.SelectedMode = ButtonMode.Normal;
    }

    /// <summary>
    /// Clear just the keyboard binding for the selected button mapping (keeps physical inputs)
    /// </summary>
    private void ClearKeyboardBinding()
    {
        if (!TryGetSelectedButtonContext(out var profile, out _, out _, out var mapping)) return;
        if (mapping is null) return;

        mapping.Output.Type = OutputType.VJoyButton;
        mapping.Output.KeyName = null;
        mapping.Output.Modifiers = null;
        profile.ModifiedAt = DateTime.UtcNow;
        _ctx.ProfileManager.SaveActiveProfile();

        _keyboardOutput.SelectedKeyName = "";
        _keyboardOutput.SelectedModifiers = null;
        _keyboardOutput.IsKeyboard = false;
    }

    private void UpdateDurationForSelectedMapping()
    {
        if (!TryGetSelectedButtonContext(out var profile, out _, out _, out var mapping)) return;
        if (mapping is null) return;

        mapping.PulseDurationMs = _buttonMode.PulseDurationMs;
        mapping.HoldDurationMs = _buttonMode.HoldDurationMs;
        profile.ModifiedAt = DateTime.UtcNow;
        _ctx.ProfileManager.SaveActiveProfile();
    }

    /// <summary>
    /// Make the current curve points symmetrical around the center.
    /// </summary>
}