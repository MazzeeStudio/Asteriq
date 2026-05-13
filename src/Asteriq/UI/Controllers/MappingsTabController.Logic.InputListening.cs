using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using SkiaSharp;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
    private void StartListeningForInput()
    {
        // Fire-and-forget async operation with internal exception handling
        _ = StartListeningForInputAsync();
    }

    private async Task StartListeningForInputAsync()
    {
        if (_inputDetection.IsListening) return;
        if (!_mappingEditorOpen) return;

        _inputDetection.IsListening = true;
        _inputDetection.ListeningStartTicks = Environment.TickCount64;
        _inputDetection.PendingInput = null;

        // Determine input type based on what we're editing
        var filter = _isEditingAxis ? InputDetectionFilter.Axes : InputDetectionFilter.Buttons;

        _inputDetectionService ??= new InputDetectionService(_ctx.InputService);

        try
        {
            // Wait for actual input change - use a delay to skip initial state
            await Task.Delay(200); // Small delay to let user release any currently pressed buttons

            var detected = await _inputDetectionService.WaitForInputAsync(filter, 0.15f, 15000);

            if (detected is not null && _mappingEditorOpen)
            {
                _inputDetection.PendingInput = detected;

                // Update manual entry dropdowns to match detected input
                PhysicalDeviceInfo? sourceDevice = null;
                for (int i = 0; i < _ctx.Devices.Count; i++)
                {
                    if (_ctx.Devices[i].InstanceGuid == detected.DeviceGuid)
                    {
                        _inputDetection.SelectedSourceDevice = i;
                        sourceDevice = _ctx.Devices[i];
                        break;
                    }
                }
                _inputDetection.SelectedSourceControl = detected.Index;

                // Note: We intentionally do NOT auto-select vJoy row here.
                // When user explicitly clicks a row to edit, their choice is respected.
                // Type-aware mapping is only used in 1:1 auto-mapping feature.
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] Input listening cancelled or failed: {ex.Message}");
        }
        finally
        {
            _inputDetection.IsListening = false;
        }
    }

    private void CancelInputListening()
    {
        if (_inputDetection.IsListening)
        {
            _inputDetectionService?.Cancel();
            _inputDetection.IsListening = false;
        }
    }

    /// <summary>
    /// Check if a physical input is already mapped anywhere in the profile.
    /// Returns the mapping name if found, null otherwise.
    /// </summary>
    private void StartInputListening(int rowIndex)
    {
        // Fire-and-forget async operation with internal exception handling
        _ = StartInputListeningAsync(rowIndex);
    }

    private async Task StartInputListeningAsync(int rowIndex)
    {
        if (_inputDetection.IsListening) return;
        if (rowIndex < 0) return;

        _inputDetection.IsListening = true;
        _inputDetection.ListeningStartTicks = Environment.TickCount64;
        _inputDetection.PendingInput = null;

        // Determine input type based on current mapping category tab
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        var filter = isAxis ? InputDetectionFilter.Axes : InputDetectionFilter.Buttons;

        _inputDetectionService ??= new InputDetectionService(_ctx.InputService);

        try
        {
            // Small delay to let user release any currently pressed buttons
            await Task.Delay(200);

            var detected = await _inputDetectionService.WaitForInputAsync(filter, 0.15f, 15000);

            if (detected is not null && _selectedMappingRow == rowIndex)
            {
                _inputDetection.PendingInput = detected;
                var inputSource = detected.ToInputSource();

                // Note: We intentionally do NOT auto-select vJoy row here.
                // When user explicitly clicks a row to map, their choice is respected.
                // Type-aware mapping is only used in 1:1 auto-mapping feature.
                int targetRowIndex = rowIndex;

                // Check for duplicate mapping
                var profile = _ctx.ProfileManager.ActiveProfile;
                if (profile is not null)
                {
                    var existingMapping = FindExistingMappingForInput(profile, inputSource);
                    if (existingMapping is not null)
                    {
                        string newTarget = isAxis ? $"vJoy Axis {GetVJoyAxisName(AxisIndexForRow(targetRowIndex))}" : $"vJoy Button {targetRowIndex + 1}";
                        if (!ConfirmDuplicateMapping(existingMapping, newTarget))
                        {
                            // User cancelled, don't create the mapping
                            return;
                        }
                        // User confirmed, remove existing mapping first
                        RemoveExistingMappingsForInput(profile, inputSource);
                    }
                }

                // Save the mapping using current panel settings (output type, key combo, button mode)
                SaveMappingForRow(targetRowIndex, detected, isAxis);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] Input listening for row {rowIndex} cancelled or failed: {ex.Message}");
        }
        finally
        {
            _inputDetection.IsListening = false;
        }
    }

    /// <summary>
    /// Start input listening when user has assigned a keyboard key to an empty button slot.
    /// When physical input is detected, creates a new mapping with the pending keyboard output.
    /// </summary>
    private async Task StartPendingKeyboardInputListeningAsync()
    {
        if (_inputDetection.IsListening) return;
        if (_keyboardOutput.PendingKey is null) return;

        _inputDetection.IsListening = true;
        _inputDetection.ListeningStartTicks = Environment.TickCount64;
        _inputDetection.PendingInput = null;

        _inputDetectionService ??= new InputDetectionService(_ctx.InputService);

        try
        {
            // Small delay to let user release any currently pressed buttons
            await Task.Delay(200);

            var detected = await _inputDetectionService.WaitForInputAsync(InputDetectionFilter.Buttons, 0.15f, 15000);

            if (detected is not null && _keyboardOutput.PendingKey is not null)
            {
                var profile = _ctx.ProfileManager.ActiveProfile;
                if (profile is null) return;

                var newInputSource = detected.ToInputSource();

                // Check for duplicate mapping
                var existingMapping = FindExistingMappingForInput(profile, newInputSource);
                if (existingMapping is not null)
                {
                    string newTarget = $"Keyboard: {FormatKeyComboForDisplay(_keyboardOutput.PendingKey, _keyboardOutput.PendingModifiers)}";
                    if (!ConfirmDuplicateMapping(existingMapping, newTarget))
                    {
                        // User cancelled, clear pending state
                        ClearPendingKeyboardState();
                        return;
                    }
                    // User confirmed, remove existing mapping first
                    RemoveExistingMappingsForInput(profile, newInputSource);
                }

                // Create new button mapping with keyboard output
                var mapping = new ButtonMapping
                {
                    Name = $"{detected.DeviceName} Button {detected.Index + 1} -> {FormatKeyComboForDisplay(_keyboardOutput.PendingKey, _keyboardOutput.PendingModifiers)}",
                    Inputs = new List<InputSource> { newInputSource },
                    Output = new OutputTarget
                    {
                        Type = OutputType.Keyboard,
                        VJoyDevice = _keyboardOutput.PendingVJoyDevice,
                        Index = _keyboardOutput.PendingOutputIndex,
                        KeyName = _keyboardOutput.PendingKey,
                        Modifiers = _keyboardOutput.PendingModifiers
                    },
                    Mode = _buttonMode.SelectedMode
                };
                profile.ButtonMappings.Add(mapping);
                profile.ModifiedAt = DateTime.UtcNow;
                _ctx.ProfileManager.SaveActiveProfile();
                _ctx.OnMappingsChanged();

                // Update the pending input so UI can show it
                _inputDetection.PendingInput = detected;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] Pending keyboard input listening cancelled or failed: {ex.Message}");
        }
        finally
        {
            _inputDetection.IsListening = false;
            ClearPendingKeyboardState();
        }
    }

    private void ClearPendingKeyboardState()
    {
        _keyboardOutput.PendingKey = null;
        _keyboardOutput.PendingModifiers = null;
        _keyboardOutput.PendingOutputIndex = -1;
        _keyboardOutput.PendingVJoyDevice = 0;
    }

}