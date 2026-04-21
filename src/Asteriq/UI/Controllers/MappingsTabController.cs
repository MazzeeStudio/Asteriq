using System.Runtime.InteropServices;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

/// <summary>
/// Mappings tab controller - handles all mapping UI, drawing, and interaction.
/// </summary>
public partial class MappingsTabController : ITabController
{
    private readonly TabContext _ctx;
    private readonly SCExportProfileService? _scExportProfileService;

    // Mapping category tabs (M1 = Buttons, M2 = Axes)
    private int _mappingCategory = 0;  // 0 = Buttons, 1 = Axes
    private int _hoveredMappingCategory = -1;
    private SKRect _mappingCategoryButtonsBounds;
    private SKRect _mappingCategoryAxesBounds;

    // Mappings tab UI state - Left panel (output list)
    private int _selectedMappingRow = -1;
    private bool _selectionIsExplicit = false; // true only when user explicitly clicked a row
    private int _hoveredMappingRow = -1;

    // Maps visual axis row index → actual vJoy axis index (0-7).
    // Only contains indices for axes the vJoy device actually has configured.
    private List<int> _visibleAxisIndices = new();

    private SKRect _vjoyPrevButtonBounds;
    private SKRect _vjoyNextButtonBounds;
    private bool _vjoyPrevHovered;
    private bool _vjoyNextHovered;
    private List<SKRect> _mappingRowBounds = new();
    private List<SKRect> _mappingAddButtonBounds = new();
    private List<SKRect> _mappingRemoveButtonBounds = new();
    private int _hoveredAddButton = -1;
    private int _hoveredRemoveButton = -1;

    // Mappings tab UI state - Right panel (mapping editor)
    private bool _mappingEditorOpen = false;
    private int _editingRowIndex = -1;
    private bool _isEditingAxis = false;
    private InputDetectionService? _inputDetectionService;

    private const int KeyCaptureTimeoutMs = 10000; // 10 second timeout for key capture
    private const int ModifierWaitMs = 300; // Wait for combo key after modifier press

    // Input listening timeout
    private const int InputListeningTimeoutMs = 15000; // 15 second timeout for input listening

    // Right panel - input sources and actions
    private SKRect _addInputButtonBounds;
    private SKRect _clearAllButtonBounds;
    private List<SKRect> _inputSourceRemoveBounds = new();
    private bool _addInputButtonHovered;
    private bool _clearAllButtonHovered;
    private int _hoveredInputSourceRemove = -1;

    // Merge operation selector (for axes with multiple inputs)
    private readonly MergeModeDropdownState _merge = new();

    // Curve and deadzone preset button hover
    private int _hoveredCurvePreset = -1;
    private int _hoveredDeadzonePreset = -1;

    // Mapping editor - action buttons
    private SKRect _saveButtonBounds;
    private SKRect _cancelButtonBounds;
    private bool _saveButtonHovered = false;
    private bool _cancelButtonHovered = false;

    private const int HighlightDurationMs = 1500; // How long the attention highlight lasts (1.5 seconds)
    private const int HighlightDebounceCooldownMs = 500; // Minimum time between highlights for same button

    // ── State objects ─────────────────────────────────────────────────────────
    private readonly CurveEditorState _curve = new();
    private readonly DeadzoneEditorState _deadzone = new();
    private readonly ButtonModeState _buttonMode = new();
    private readonly KeyboardOutputState _keyboardOutput = new();
    private readonly InputDetectionState _inputDetection = new();
    private readonly MappingHighlightState _highlight = new();
    private readonly AutoScrollState _autoScroll = new();
    private readonly NetworkSwitchState _netSwitch = new();
    private readonly ListScrollState _listScroll = new();
    private readonly ThresholdEditorState _threshold = new();

    // Legacy compatibility — computed alias kept for use in axis logic
    private float _axisDeadzone
    {
        get => Math.Max(Math.Abs(_deadzone.CenterMin), Math.Abs(_deadzone.CenterMax));
        set
        {
            _deadzone.CenterMin = -Math.Abs(value);
            _deadzone.CenterMax = Math.Abs(value);
        }
    }

    // Keyboard interop delegated to KeyboardHelper

    /// <summary>True if a curve control point is being dragged.</summary>
    public bool IsDraggingCurve => _curve.DraggingPoint >= 0;

    /// <summary>True if a deadzone handle is being dragged.</summary>
    public bool IsDraggingDeadzone => _deadzone.DraggingHandle >= 0;

    /// <summary>True if a duration slider is being dragged.</summary>
    public bool IsDraggingDuration => _buttonMode.DraggingPulse || _buttonMode.DraggingHold;

    /// <summary>True if a threshold or hysteresis slider is being dragged.</summary>
    public bool IsDraggingThreshold => _threshold.DraggingAboveThreshold || _threshold.DraggingAboveHysteresis
        || _threshold.DraggingBelowThreshold || _threshold.DraggingBelowHysteresis;

    /// <summary>True if the keyboard key capture mode is active.</summary>
    public bool IsCapturingKey => _keyboardOutput.IsCapturing || _threshold.AboveCapturing || _threshold.BelowCapturing;

    /// <summary>True if listening for physical input detection.</summary>
    public bool IsListeningForInput => _inputDetection.IsListening;

    private static string? GetKeyNameFromKeys(Keys keys)
    {
        var (keyName, _) = GetKeyNameAndModifiersFromKeys(keys);
        return keyName;
    }

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

    public MappingsTabController(
        TabContext ctx,
        SCExportProfileService? scExportProfileService = null)
    {
        _ctx = ctx;
        _scExportProfileService = scExportProfileService;
    }
    public void Draw(SKCanvas canvas, SKRect bounds, float padLeft, float contentTop, float contentBottom)
    {
        DrawMappingsTabContent(canvas, bounds, padLeft, contentTop, contentBottom);
    }

    public void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            HandleRightClick(e);
            return;
        }

        if (HandleCategoryTabClick()) return;
        if (HandleAutoScrollClick(e)) return;
        if (HandleRightPanelClick(e)) return;
        if (HandleThresholdClick(e)) return;
        if (HandleAxisSettingsClick(e)) return;
        if (HandleNetSwitchClick(e)) return;
        if (HandleVJoyNavigationClick(e)) return;
        HandleMappingRowClick();
    }

    private void HandleRightClick(MouseEventArgs e)
    {
        if (_mappingCategory == 1 && _curve.SelectedType == CurveType.Custom)
        {
            var pt = new SKPoint(e.X, e.Y);
            if (_curve.Bounds.Contains(pt))
            {
                int pointIndex = FindCurvePointAt(pt, _curve.Bounds);
                if (pointIndex >= 0)
                    RemoveCurveControlPoint(pointIndex);
            }
        }
    }

    private bool HandleCategoryTabClick()
    {
        if (_hoveredMappingCategory < 0) return false;
        _mappingCategory = _hoveredMappingCategory;
        _selectedMappingRow = -1;
        _selectionIsExplicit = false;
        _listScroll.ScrollOffset = 0;
        CancelInputListening();
        SelectFirstRowIfUnselected();
        return true;
    }

    private bool HandleAutoScrollClick(MouseEventArgs e)
    {
        if (!_autoScroll.CheckboxBounds.Contains(e.X, e.Y)) return false;
        _autoScroll.Enabled = !_autoScroll.Enabled;
        _ctx.MarkDirty();
        return true;
    }

    private bool HandleRightPanelClick(MouseEventArgs e)
    {
        bool hasSelection = _selectedMappingRow >= 0 && _selectionIsExplicit;

        // Add input button — toggles listening
        if (_addInputButtonBounds.Contains(e.X, e.Y) && hasSelection)
        {
            if (_inputDetection.IsListening) CancelInputListening();
            else StartInputListening(_selectedMappingRow);
            return true;
        }

        // Remove input source
        if (_selectionIsExplicit)
        {
            for (int i = 0; i < _inputSourceRemoveBounds.Count; i++)
            {
                if (_inputSourceRemoveBounds[i].Contains(e.X, e.Y))
                { RemoveInputSourceAtIndex(i); return true; }
            }
        }

        // Merge operation dropdown (axis category, 2+ inputs)
        if (_mappingCategory == 1 && hasSelection)
        {
            // Panel open: click inside selects; click outside closes (and swallows)
            if (_merge.DropdownOpen)
            {
                if (!_merge.DropdownBounds.IsEmpty && _merge.DropdownBounds.Contains(e.X, e.Y))
                {
                    const float itemHeight = 28f;
                    int idx = (int)((e.Y - _merge.DropdownBounds.Top - 2f) / itemHeight);
                    if (idx >= 0 && idx < s_mergeOps.Length)
                    {
                        UpdateMergeOperationForSelected(s_mergeOps[idx]);
                    }
                    _merge.DropdownOpen = false;
                    _merge.HoveredIndex = -1;
                    return true;
                }

                if (!_merge.SelectorBounds.Contains(e.X, e.Y))
                {
                    _merge.DropdownOpen = false;
                    _merge.HoveredIndex = -1;
                    return true;
                }
            }

            if (_merge.SelectorBounds.Contains(e.X, e.Y))
            {
                _merge.DropdownOpen = !_merge.DropdownOpen;
                if (!_merge.DropdownOpen) _merge.HoveredIndex = -1;
                return true;
            }
        }

        // Button mode selection — blocked for modifier keys
        bool selectedIsModifier = _keyboardOutput.IsKeyboard && IsModifierKeyName(_keyboardOutput.SelectedKeyName);
        if (_mappingCategory == 0 && hasSelection && !selectedIsModifier)
        {
            for (int i = 0; i < _buttonMode.ModeBounds.Length; i++)
            {
                if (_buttonMode.ModeBounds[i].Contains(e.X, e.Y))
                {
                    _buttonMode.SelectedMode = (ButtonMode)i;
                    UpdateButtonModeForSelected();
                    return true;
                }
            }
        }

        // Pulse duration slider
        if (_mappingCategory == 0 && hasSelection && _buttonMode.SelectedMode == ButtonMode.Pulse
            && _buttonMode.PulseSliderBounds.Contains(e.X, e.Y))
        {
            _buttonMode.DraggingPulse = true;
            UpdatePulseDurationFromMouse(e.X);
            return true;
        }

        // Hold duration slider
        if (_mappingCategory == 0 && hasSelection && _buttonMode.SelectedMode == ButtonMode.HoldToActivate
            && _buttonMode.HoldSliderBounds.Contains(e.X, e.Y))
        {
            _buttonMode.DraggingHold = true;
            UpdateHoldDurationFromMouse(e.X);
            return true;
        }

        // Output type selection
        if (_mappingCategory == 0 && hasSelection)
        {
            if (_keyboardOutput.BtnBounds.Contains(e.X, e.Y))
            {
                _keyboardOutput.IsKeyboard = false;
                _keyboardOutput.SelectedKeyName = "";
                UpdateOutputTypeForSelected();
                return true;
            }
            if (_keyboardOutput.KeyBounds.Contains(e.X, e.Y))
            {
                _keyboardOutput.IsKeyboard = true;
                UpdateOutputTypeForSelected();
                return true;
            }
        }

        // Key clear button
        if (_mappingCategory == 0 && hasSelection && _keyboardOutput.IsKeyboard
            && _keyboardOutput.ClearBounds.HitTest(e.X, e.Y))
        { ClearKeyboardBinding(); return true; }

        // Key capture field
        if (_mappingCategory == 0 && hasSelection && _keyboardOutput.IsKeyboard
            && _keyboardOutput.CaptureBounds.Contains(e.X, e.Y))
        {
            _keyboardOutput.IsCapturing = true;
            _keyboardOutput.CaptureStartTicks = Environment.TickCount64;
            return true;
        }

        // Clear Mapping button
        if (_mappingCategory == 0 && hasSelection && _clearAllButtonBounds.Contains(e.X, e.Y))
        { ClearSelectedButtonMapping(); return true; }

        return false;
    }

    private bool HandleThresholdClick(MouseEventArgs e)
    {
        if (_mappingCategory != 1 || _selectedMappingRow < 0)
            return false;

        // Output mode toggle
        if (_threshold.AxisModeBounds.Contains(e.X, e.Y) && _threshold.IsThresholdMode)
        {
            SwitchToAxisMode();
            return true;
        }
        if (_threshold.ThresholdModeBounds.Contains(e.X, e.Y) && !_threshold.IsThresholdMode)
        {
            // Block switch to threshold when merge mode (2+ inputs)
            var inputs = GetInputsForSelectedOutput();
            if (inputs.Count >= 2) return true;
            SwitchToThresholdMode();
            return true;
        }

        if (!_threshold.IsThresholdMode) return false;

        // Direction checkboxes
        if (_threshold.AboveBounds.Contains(e.X, e.Y))
        {
            _threshold.AboveEnabled = !_threshold.AboveEnabled;
            if (!_threshold.AboveEnabled && !_threshold.BelowEnabled)
                _threshold.BelowEnabled = true; // At least one must be active
            SaveThresholdSettingsForRow();
            _ctx.MarkDirty();
            return true;
        }
        if (_threshold.BelowBounds.Contains(e.X, e.Y))
        {
            _threshold.BelowEnabled = !_threshold.BelowEnabled;
            if (!_threshold.AboveEnabled && !_threshold.BelowEnabled)
                _threshold.AboveEnabled = true; // At least one must be active
            SaveThresholdSettingsForRow();
            _ctx.MarkDirty();
            return true;
        }

        // Above threshold controls (clear before capture — clear bounds are inside capture bounds)
        if (_threshold.AboveEnabled)
        {
            if (_threshold.AboveSliderBounds.Contains(e.X, e.Y))
            {
                _threshold.DraggingAboveThreshold = true;
                UpdateThresholdFromMouse(e.X, _threshold.AboveSliderBounds, out _threshold.AboveThreshold);
                _ctx.MarkDirty();
                return true;
            }
            if (_threshold.AboveHystSliderBounds.Contains(e.X, e.Y))
            {
                _threshold.DraggingAboveHysteresis = true;
                UpdateHysteresisFromMouse(e.X, _threshold.AboveHystSliderBounds, out _threshold.AboveHysteresis);
                _ctx.MarkDirty();
                return true;
            }
            if (_threshold.AboveClearBounds.HitTest(e.X, e.Y) && !string.IsNullOrEmpty(_threshold.AboveKeyName))
            {
                _threshold.AboveKeyName = "";
                _threshold.AboveModifiers = null;
                SaveThresholdSettingsForRow();
                _ctx.MarkDirty();
                return true;
            }
            if (_threshold.AboveCaptureBounds.Contains(e.X, e.Y))
            {
                _threshold.AboveCapturing = true;
                _threshold.AboveCaptureStartTicks = Environment.TickCount64;
                _ctx.MarkDirty();
                return true;
            }
        }

        // Below threshold controls (clear before capture — clear bounds are inside capture bounds)
        if (_threshold.BelowEnabled)
        {
            if (_threshold.BelowSliderBounds.Contains(e.X, e.Y))
            {
                _threshold.DraggingBelowThreshold = true;
                UpdateThresholdFromMouse(e.X, _threshold.BelowSliderBounds, out _threshold.BelowThreshold);
                _ctx.MarkDirty();
                return true;
            }
            if (_threshold.BelowHystSliderBounds.Contains(e.X, e.Y))
            {
                _threshold.DraggingBelowHysteresis = true;
                UpdateHysteresisFromMouse(e.X, _threshold.BelowHystSliderBounds, out _threshold.BelowHysteresis);
                _ctx.MarkDirty();
                return true;
            }
            if (_threshold.BelowClearBounds.HitTest(e.X, e.Y) && !string.IsNullOrEmpty(_threshold.BelowKeyName))
            {
                _threshold.BelowKeyName = "";
                _threshold.BelowModifiers = null;
                SaveThresholdSettingsForRow();
                _ctx.MarkDirty();
                return true;
            }
            if (_threshold.BelowCaptureBounds.Contains(e.X, e.Y))
            {
                _threshold.BelowCapturing = true;
                _threshold.BelowCaptureStartTicks = Environment.TickCount64;
                _ctx.MarkDirty();
                return true;
            }
        }

        return false;
    }

    private static void UpdateThresholdFromMouse(float mouseX, SKRect bounds, out float threshold)
    {
        if (bounds.Width <= 0) { threshold = 0; return; }
        float norm = Math.Clamp((mouseX - bounds.Left) / bounds.Width, 0f, 1f);
        threshold = (norm * 2f) - 1f; // Map 0..1 to -1..1
    }

    private static void UpdateHysteresisFromMouse(float mouseX, SKRect bounds, out float hysteresis)
    {
        if (bounds.Width <= 0) { hysteresis = 0.05f; return; }
        float norm = Math.Clamp((mouseX - bounds.Left) / bounds.Width, 0f, 1f);
        hysteresis = norm * 0.25f; // Map 0..1 to 0..0.25
    }

    private bool HandleAxisSettingsClick(MouseEventArgs e)
    {
        if (_mappingCategory != 1 || _selectedMappingRow < 0 || !_selectionIsExplicit)
            return false;

        var pt = new SKPoint(e.X, e.Y);

        if (HandleCurvePresetClick(pt))
            return true;

        // Curve control point drag start
        if (_curve.SelectedType == CurveType.Custom && _curve.Bounds.Contains(pt))
        {
            int pointIndex = FindCurvePointAt(pt, _curve.Bounds);
            if (pointIndex >= 0)
            {
                _curve.DraggingPoint = pointIndex;
                return true;
            }

            var graphPt = CurveScreenToGraph(pt, _curve.Bounds);
            AddCurveControlPoint(graphPt);
            return true;
        }

        // Deadzone handle click
        int dzHandle = FindDeadzoneHandleAt(pt);
        if (dzHandle >= 0)
        {
            _deadzone.SelectedHandle = dzHandle;
            _deadzone.DraggingHandle = dzHandle;
            _ctx.MarkDirty();
            return true;
        }

        // Click on slider background — deselect
        if (_deadzone.SliderBounds.Contains(pt))
        {
            _deadzone.SelectedHandle = -1;
            _ctx.MarkDirty();
            return true;
        }

        return false;
    }

    private bool HandleNetSwitchClick(MouseEventArgs e)
    {
        if (_netSwitch.ActionBounds.HitTest(e.X, e.Y))
        { AssignSwitchButtonForSelectedRow(); return true; }

        if (_netSwitch.BadgeXBounds.HitTest(e.X, e.Y))
        { ClearNetworkSwitchButton(); return true; }

        return false;
    }

    private bool HandleVJoyNavigationClick(MouseEventArgs e)
    {
        if (_vjoyPrevButtonBounds.Contains(e.X, e.Y) && _ctx.SelectedVJoyDeviceIndex > 0)
        {
            _ctx.SelectedVJoyDeviceIndex--;
            ResetMappingSelectionState();
            return true;
        }

        if (_vjoyNextButtonBounds.Contains(e.X, e.Y) && _ctx.SelectedVJoyDeviceIndex < _ctx.VJoyDevices.Count - 1)
        {
            _ctx.SelectedVJoyDeviceIndex++;
            ResetMappingSelectionState();
            return true;
        }

        return false;
    }

    private void ResetMappingSelectionState()
    {
        _selectedMappingRow = -1;
        _selectionIsExplicit = false;
        _listScroll.ScrollOffset = 0;
        _highlight.ControlDef = null;
        CancelInputListening();
        _ctx.UpdateMappingsPrimaryDeviceMap();
        SelectFirstRowIfUnselected();
        _ctx.MarkDirty();
    }

    private void HandleMappingRowClick()
    {
        if (_hoveredMappingRow < 0) return;

        if (_hoveredMappingRow != _selectedMappingRow)
        {
            CancelInputListening();
            _selectedMappingRow = _hoveredMappingRow;
            LoadOutputTypeStateForRow();
            LoadAxisSettingsForRow();
        }
        else
        {
            _selectedMappingRow = _hoveredMappingRow;
        }

        _selectionIsExplicit = true;
        _highlight.ControlDef = GetControlForRow(_selectedMappingRow);
        _highlight.ControlHighlightTicks = Environment.TickCount64;
    }

    public void OnMouseMove(MouseEventArgs e)
    {
        ResetMoveHoverStates();

        if (UpdateMappingDragState(e)) return;
        _ctx.OwnerForm.Cursor = Cursors.Default;
        if (UpdateRightPanelHover(e)) return;
        if (UpdateAxisEditorHover(e)) return;
        if (UpdateNetSwitchHover(e)) return;
        if (UpdateVJoyNavigationHover(e)) return;
        if (UpdateMappingRowHover(e)) return;
        UpdateCategoryTabHover(e);
    }

    private void ResetMoveHoverStates()
    {
        _vjoyPrevHovered = false;
        _vjoyNextHovered = false;
        _hoveredMappingRow = -1;
        _hoveredAddButton = -1;
        _hoveredRemoveButton = -1;
        _buttonMode.HoveredMode = -1;
        _keyboardOutput.HoveredOutputType = -1;
        _keyboardOutput.CaptureHovered = false;
        _keyboardOutput.ClearHovered = false;
        _addInputButtonHovered = false;
        _clearAllButtonHovered = false;
        _hoveredInputSourceRemove = -1;
        _merge.HoveredIndex = -1;
        _merge.SelectorHovered = false;
        _hoveredCurvePreset = -1;
        _hoveredDeadzonePreset = -1;
        _threshold.HoveredOutputMode = -1;
        _threshold.HoveredDirection = -1;
        _threshold.AboveCaptureHovered = false;
        _threshold.AboveClearHovered = false;
        _threshold.BelowCaptureHovered = false;
        _threshold.BelowClearHovered = false;
        _hoveredMappingCategory = -1;
        _autoScroll.CheckboxHovered = false;
    }

    private bool UpdateMappingDragState(MouseEventArgs e)
    {
        if (_buttonMode.DraggingPulse)
        { UpdatePulseDurationFromMouse(e.X); _ctx.MarkDirty(); return true; }

        if (_buttonMode.DraggingHold)
        { UpdateHoldDurationFromMouse(e.X); _ctx.MarkDirty(); return true; }

        if (_threshold.DraggingAboveThreshold)
        { UpdateThresholdFromMouse(e.X, _threshold.AboveSliderBounds, out _threshold.AboveThreshold); _ctx.MarkDirty(); return true; }
        if (_threshold.DraggingBelowThreshold)
        { UpdateThresholdFromMouse(e.X, _threshold.BelowSliderBounds, out _threshold.BelowThreshold); _ctx.MarkDirty(); return true; }
        if (_threshold.DraggingAboveHysteresis)
        { UpdateHysteresisFromMouse(e.X, _threshold.AboveHystSliderBounds, out _threshold.AboveHysteresis); _ctx.MarkDirty(); return true; }
        if (_threshold.DraggingBelowHysteresis)
        { UpdateHysteresisFromMouse(e.X, _threshold.BelowHystSliderBounds, out _threshold.BelowHysteresis); _ctx.MarkDirty(); return true; }

        return false;
    }

    private bool UpdateRightPanelHover(MouseEventArgs e)
    {
        if (_autoScroll.CheckboxBounds.Contains(e.X, e.Y))
        { _autoScroll.CheckboxHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }

        if (_addInputButtonBounds.Contains(e.X, e.Y))
        { _addInputButtonHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }

        for (int i = 0; i < _inputSourceRemoveBounds.Count; i++)
        {
            if (_inputSourceRemoveBounds[i].Contains(e.X, e.Y))
            { _hoveredInputSourceRemove = i; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
        }

        // Merge operation dropdown (axis category)
        if (_mappingCategory == 1 && _selectedMappingRow >= 0)
        {
            if (_merge.DropdownOpen && !_merge.DropdownBounds.IsEmpty
                && _merge.DropdownBounds.Contains(e.X, e.Y))
            {
                const float itemHeight = 28f;
                int idx = (int)((e.Y - _merge.DropdownBounds.Top - 2f) / itemHeight);
                _merge.HoveredIndex = (idx >= 0 && idx < s_mergeOps.Length) ? idx : -1;
                _ctx.OwnerForm.Cursor = Cursors.Hand;
                return true;
            }

            if (_merge.SelectorBounds.Contains(e.X, e.Y))
            {
                _merge.SelectorHovered = true;
                _merge.HoveredIndex = -1;
                _ctx.OwnerForm.Cursor = Cursors.Hand;
                return true;
            }
        }

        // Axis output mode toggle and threshold controls (axis category)
        if (_mappingCategory == 1 && _selectedMappingRow >= 0)
        {
            if (_threshold.AxisModeBounds.Contains(e.X, e.Y))
            { _threshold.HoveredOutputMode = 0; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
            if (_threshold.ThresholdModeBounds.Contains(e.X, e.Y))
            {
                // Don't show hand cursor when disabled (merge mode)
                bool mergeActive = !_threshold.IsThresholdMode && GetInputsForSelectedOutput().Count >= 2;
                if (!mergeActive) _ctx.OwnerForm.Cursor = Cursors.Hand;
                _threshold.HoveredOutputMode = 1;
                return true;
            }

            if (_threshold.IsThresholdMode)
            {
                if (_threshold.AboveBounds.Contains(e.X, e.Y))
                { _threshold.HoveredDirection = 0; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
                if (_threshold.BelowBounds.Contains(e.X, e.Y))
                { _threshold.HoveredDirection = 1; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }

                if (_threshold.AboveEnabled)
                {
                    if (_threshold.AboveClearBounds.HitTest(e.X, e.Y))
                    { _threshold.AboveClearHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
                    if (_threshold.AboveCaptureBounds.Contains(e.X, e.Y))
                    { _threshold.AboveCaptureHovered = true; _ctx.OwnerForm.Cursor = Cursors.IBeam; return true; }
                }
                if (_threshold.BelowEnabled)
                {
                    if (_threshold.BelowClearBounds.HitTest(e.X, e.Y))
                    { _threshold.BelowClearHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
                    if (_threshold.BelowCaptureBounds.Contains(e.X, e.Y))
                    { _threshold.BelowCaptureHovered = true; _ctx.OwnerForm.Cursor = Cursors.IBeam; return true; }
                }
            }
        }

        // Button mode controls (button category)
        if (_mappingCategory == 0 && _selectedMappingRow >= 0)
        {
            for (int i = 0; i < _buttonMode.ModeBounds.Length; i++)
            {
                if (_buttonMode.ModeBounds[i].Contains(e.X, e.Y))
                { _buttonMode.HoveredMode = i; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
            }

            if (_keyboardOutput.BtnBounds.Contains(e.X, e.Y))
            { _keyboardOutput.HoveredOutputType = 0; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
            if (_keyboardOutput.KeyBounds.Contains(e.X, e.Y))
            { _keyboardOutput.HoveredOutputType = 1; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }

            if (_keyboardOutput.IsKeyboard && _keyboardOutput.ClearBounds.HitTest(e.X, e.Y))
            { _keyboardOutput.ClearHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
            if (_keyboardOutput.IsKeyboard && _keyboardOutput.CaptureBounds.Contains(e.X, e.Y))
            { _keyboardOutput.CaptureHovered = true; _ctx.OwnerForm.Cursor = Cursors.IBeam; return true; }

            if (_clearAllButtonBounds.Contains(e.X, e.Y))
            { _clearAllButtonHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
        }

        return false;
    }

    private bool UpdateAxisEditorHover(MouseEventArgs e)
    {
        if (_mappingCategory != 1 || _selectedMappingRow < 0) return false;

        var pt = new SKPoint(e.X, e.Y);

        if (_curve.DraggingPoint >= 0)
        { UpdateDraggedCurvePoint(pt); _ctx.MarkDirty(); return true; }

        if (_deadzone.DraggingHandle >= 0)
        { UpdateDraggedDeadzoneHandle(pt); _ctx.MarkDirty(); return true; }

        // Curve preset buttons (LINEAR, S-CURVE, EXPO, CUSTOM)
        for (int i = 0; i < _curve.PresetBounds.Length; i++)
        {
            if (_curve.PresetBounds[i].Contains(pt))
            { _hoveredCurvePreset = i; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
        }

        // Deadzone preset buttons (0%, 2%, 5%, 10%)
        if (_deadzone.SelectedHandle >= 0)
        {
            for (int i = 0; i < _deadzone.PresetBounds.Length; i++)
            {
                if (_deadzone.PresetBounds[i].Contains(pt))
                { _hoveredDeadzonePreset = i; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
            }
        }

        if (_curve.SelectedType == CurveType.Custom && _curve.Bounds.Contains(pt))
        {
            int newHovered = FindCurvePointAt(pt, _curve.Bounds);
            if (newHovered != _curve.HoveredPoint)
            {
                _curve.HoveredPoint = newHovered;
                _ctx.OwnerForm.Cursor = newHovered >= 0 ? Cursors.Hand : Cursors.Cross;
                _ctx.MarkDirty();
            }
            return true;
        }

        if (_curve.HoveredPoint >= 0)
        {
            _curve.HoveredPoint = -1;
            _ctx.MarkDirty();
        }

        return false;
    }

    private bool UpdateNetSwitchHover(MouseEventArgs e)
    {
        if (_netSwitch.ActionBounds.HitTest(e.X, e.Y))
        { _netSwitch.ActionHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }

        if (_netSwitch.BadgeXBounds.HitTest(e.X, e.Y))
        { _netSwitch.BadgeXHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }

        return false;
    }

    private bool UpdateVJoyNavigationHover(MouseEventArgs e)
    {
        if (_vjoyPrevButtonBounds.Contains(e.X, e.Y) && _ctx.SelectedVJoyDeviceIndex > 0)
        { _vjoyPrevHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }

        if (_vjoyNextButtonBounds.Contains(e.X, e.Y) && _ctx.SelectedVJoyDeviceIndex < _ctx.VJoyDevices.Count - 1)
        { _vjoyNextHovered = true; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }

        return false;
    }

    private bool UpdateMappingRowHover(MouseEventArgs e)
    {
        for (int i = 0; i < _mappingRowBounds.Count; i++)
        {
            if (_mappingRowBounds[i].Contains(e.X, e.Y))
            { _hoveredMappingRow = i; _ctx.OwnerForm.Cursor = Cursors.Hand; return true; }
        }
        return false;
    }

    private void UpdateCategoryTabHover(MouseEventArgs e)
    {
        if (_mappingCategoryButtonsBounds.Contains(e.X, e.Y))
        { _hoveredMappingCategory = 0; _ctx.OwnerForm.Cursor = Cursors.Hand; }
        else if (_mappingCategoryAxesBounds.Contains(e.X, e.Y))
        { _hoveredMappingCategory = 1; _ctx.OwnerForm.Cursor = Cursors.Hand; }
    }

    public void OnMouseUp(MouseEventArgs e)
    {
        if (_curve.DraggingPoint >= 0 || _deadzone.DraggingHandle >= 0)
        {
            _curve.DraggingPoint = -1;
            _deadzone.DraggingHandle = -1;
            SaveAxisSettingsForRow();  // Persist curve/deadzone changes
            _ctx.MarkDirty();
        }

        // Release duration slider dragging
        if (_buttonMode.DraggingPulse || _buttonMode.DraggingHold)
        {
            _buttonMode.DraggingPulse = false;
            _buttonMode.DraggingHold = false;
            UpdateDurationForSelectedMapping();
            _ctx.MarkDirty();
        }

        // Release threshold/hysteresis slider dragging
        if (IsDraggingThreshold)
        {
            _threshold.DraggingAboveThreshold = false;
            _threshold.DraggingAboveHysteresis = false;
            _threshold.DraggingBelowThreshold = false;
            _threshold.DraggingBelowHysteresis = false;
            SaveThresholdSettingsForRow();
            _ctx.MarkDirty();
        }
    }

    public void OnMouseWheel(MouseEventArgs e)
    {
        // Handle scroll when mouse is over the bindings list
        if (_listScroll.ListBounds.Contains(e.X, e.Y))
        {
            float scrollAmount = -e.Delta / 4f; // Delta is usually ±120, divide for smooth scrolling
            float maxScroll = Math.Max(0, _listScroll.ContentHeight - _listScroll.ListBounds.Height);

            _listScroll.ScrollOffset = Math.Clamp(_listScroll.ScrollOffset + scrollAmount, 0, maxScroll);
            _ctx.MarkDirty();
        }
    }

    public bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Handle key capture for keyboard output mapping
        if (_keyboardOutput.IsCapturing)
        {
            if (HandleKeyCaptureEvent(keyData,
                ref _keyboardOutput.PendingModifierName, ref _keyboardOutput.PendingModifierTicks,
                out var kbKeyName, out var kbModifiers))
            {
                _keyboardOutput.SelectedKeyName = kbKeyName;
                _keyboardOutput.SelectedModifiers = kbModifiers;
                _keyboardOutput.IsKeyboard = true;
                _keyboardOutput.IsCapturing = false;
                _keyboardOutput.PendingModifierName = null;
                UpdateKeyNameForSelected();
            }
            return true;
        }

        // Handle key capture for threshold keyboard output (above)
        if (_threshold.AboveCapturing)
        {
            if (HandleKeyCaptureEvent(keyData,
                ref _threshold.AbovePendingModifier, ref _threshold.AbovePendingModifierTicks,
                out var abKeyName, out var abModifiers))
            {
                _threshold.AboveKeyName = abKeyName;
                _threshold.AboveModifiers = abModifiers;
                _threshold.AboveCapturing = false;
                _threshold.AbovePendingModifier = null;
                SaveThresholdSettingsForRow();
            }
            return true;
        }

        // Handle key capture for threshold keyboard output (below)
        if (_threshold.BelowCapturing)
        {
            if (HandleKeyCaptureEvent(keyData,
                ref _threshold.BelowPendingModifier, ref _threshold.BelowPendingModifierTicks,
                out var blKeyName, out var blModifiers))
            {
                _threshold.BelowKeyName = blKeyName;
                _threshold.BelowModifiers = blModifiers;
                _threshold.BelowCapturing = false;
                _threshold.BelowPendingModifier = null;
                SaveThresholdSettingsForRow();
            }
            return true;
        }

        // Cancel key capture / input listening with Escape
        if (keyData == Keys.Escape)
        {
            if (_keyboardOutput.IsCapturing)
            {
                _keyboardOutput.IsCapturing = false;
                _keyboardOutput.PendingModifierName = null;
                return true;
            }
            if (_threshold.AboveCapturing)
            {
                _threshold.AboveCapturing = false;
                _threshold.AbovePendingModifier = null;
                return true;
            }
            if (_threshold.BelowCapturing)
            {
                _threshold.BelowCapturing = false;
                _threshold.BelowPendingModifier = null;
                return true;
            }
            if (_inputDetection.IsListening)
            {
                CancelInputListening();
                return true;
            }
        }

        return false;
    }

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
            // Save as pending — don't finalize yet, wait for possible combo key
            pendingModifier = GetModifierKeyName(baseKey);
            pendingModifierTicks = Environment.TickCount64;
            return false;
        }

        // Non-modifier key — finalize with any held modifiers
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

    public void OnMouseLeave()
    {
        _curve.DraggingPoint = -1;
        _deadzone.DraggingHandle = -1;
        _buttonMode.DraggingPulse = false;
        _buttonMode.DraggingHold = false;
        _autoScroll.CheckboxHovered = false;
    }

    public void OnTick()
    {
        if (_highlight.ControlDef is not null)
        {
            float elapsed = (Environment.TickCount64 - _highlight.ControlHighlightTicks) / 1000f;
            if (elapsed < 3f)
                _ctx.MarkDirty();
            else
                _highlight.ControlDef = null;
        }

        if (_highlight.FlashText is not null)
        {
            float elapsed = (Environment.TickCount64 - _highlight.FlashTicks) / 1000f;
            if (elapsed < 2.5f)
                _ctx.MarkDirty();
            else
                _highlight.FlashText = null;
        }

        // Finalize pending modifier-only captures after timeout
        if (_keyboardOutput.IsCapturing &&
            CheckPendingModifierTimeout(ref _keyboardOutput.PendingModifierName, ref _keyboardOutput.PendingModifierTicks, out var kbMod))
        {
            _keyboardOutput.SelectedKeyName = kbMod;
            _keyboardOutput.SelectedModifiers = null;
            _keyboardOutput.IsKeyboard = true;
            _keyboardOutput.IsCapturing = false;
            UpdateKeyNameForSelected();
            _ctx.MarkDirty();
        }
        if (_threshold.AboveCapturing &&
            CheckPendingModifierTimeout(ref _threshold.AbovePendingModifier, ref _threshold.AbovePendingModifierTicks, out var abMod))
        {
            _threshold.AboveKeyName = abMod;
            _threshold.AboveModifiers = null;
            _threshold.AboveCapturing = false;
            SaveThresholdSettingsForRow();
            _ctx.MarkDirty();
        }
        if (_threshold.BelowCapturing &&
            CheckPendingModifierTimeout(ref _threshold.BelowPendingModifier, ref _threshold.BelowPendingModifierTicks, out var blMod))
        {
            _threshold.BelowKeyName = blMod;
            _threshold.BelowModifiers = null;
            _threshold.BelowCapturing = false;
            SaveThresholdSettingsForRow();
            _ctx.MarkDirty();
        }
    }

    public void OnActivated()
    {
        _highlight.PrevButtonState.Clear();
        _highlight.Debounce.Clear();
        _highlight.Row = -1;
        _ctx.UpdateMappingsPrimaryDeviceMap();
        SelectFirstRowIfUnselected();
    }

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

    public void OnDeactivated()
    {
        _buttonMode.DraggingPulse = false;
        _buttonMode.DraggingHold = false;
        _threshold.DraggingAboveThreshold = false;
        _threshold.DraggingAboveHysteresis = false;
        _threshold.DraggingBelowThreshold = false;
        _threshold.DraggingBelowHysteresis = false;
    }

    /// <summary>Public entry point for cross-tab callback.</summary>
    public void CreateOneToOneMappingsPublic() => CreateOneToOneMappings();

    /// <summary>Public entry point for cross-tab callback.</summary>
    public void ClearDeviceMappingsPublic() => ClearDeviceMappings();

    /// <summary>Public entry point for cross-tab callback.</summary>
    public void RemoveDisconnectedDevicePublic() => RemoveDisconnectedDevice();

    /// <summary>Public entry point for cross-tab callback.</summary>
    public void OpenMappingDialogForControlPublic(string controlId) => OpenMappingDialogForControl(controlId);

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
                    // SDL2 button index i → device-map binding "button{i+1}"
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
                _highlight.FlashText = $"BUTTON {i + 1} — NO MAPPING";
                _highlight.FlashTicks = Environment.TickCount64;
                _highlight.Debounce[debounceKey] = Environment.TickCount64; // Debounce the no-mapping flash too
            }
        }

        // Store current button state for next frame comparison
        _highlight.PrevButtonState[state.DeviceName] = (bool[])state.Buttons.Clone();
    }

    /// <summary>
    /// Returns the current highlighted mapping row index, or -1 if none.
    /// Used by MainForm to detect highlight changes.
    /// </summary>
    public int HighlightedMappingRow => _highlight.Row;

    /// <summary>
    /// Returns the vJoy device ID of the highlighted row.
    /// </summary>
    public uint HighlightedVJoyDevice => _highlight.VJoyDevice;

    // ─────────────────────────────────────────────────────────────────────────
    // State classes — each groups logically-related fields for one sub-feature.
    // All fields are public so partial files can access them via the instance.
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class CurveEditorState
    {
        public SKRect Bounds;
        public List<SKPoint> ControlPoints = [new(0, 0), new(1, 1)];
        public int HoveredPoint = -1;
        public int DraggingPoint = -1;
        public CurveType SelectedType = CurveType.Linear;
        public bool Symmetrical;
        public SKRect CheckboxBounds;
        public SKRect[] PresetBounds = new SKRect[4];
    }

    private sealed class DeadzoneEditorState
    {
        public float Min = -1.0f;
        public float CenterMin;
        public float CenterMax;
        public float Max = 1.0f;
        public bool CenterEnabled;
        public SKRect SliderBounds;
        public SKRect CenterCheckboxBounds;
        public SKRect[] PresetBounds = new SKRect[4];
        public int DraggingHandle = -1;
        public int SelectedHandle = -1;
        public SKRect InvertToggleBounds;
        public bool AxisInverted;
    }

    private sealed class ButtonModeState
    {
        public ButtonMode SelectedMode = ButtonMode.Normal;
        public SKRect[] ModeBounds = new SKRect[4];
        public int HoveredMode = -1;
        public int PulseDurationMs = 100;
        public int HoldDurationMs = 500;
        public SKRect PulseSliderBounds;
        public SKRect HoldSliderBounds;
        public bool DraggingPulse;
        public bool DraggingHold;
    }

    private sealed class KeyboardOutputState
    {
        public bool IsKeyboard;
        public SKRect BtnBounds;
        public SKRect KeyBounds;
        public int HoveredOutputType = -1;
        public string SelectedKeyName = "";
        public List<string>? SelectedModifiers;
        public SKRect CaptureBounds;
        public bool CaptureHovered;
        public SKRect ClearBounds;
        public bool ClearHovered;
        public bool IsCapturing;
        public long CaptureStartTicks;
        public string? PendingModifierName;
        public long PendingModifierTicks;
        public string? PendingKey;
        public List<string>? PendingModifiers;
        public int PendingOutputIndex = -1;
        public uint PendingVJoyDevice;
    }

    private sealed class InputDetectionState
    {
        public bool IsListening;
        public SKRect FieldBounds;
        public DetectedInput? PendingInput;
        public long ListeningStartTicks;
        public bool ManualEntryMode;
        public SKRect ManualEntryBounds;
        public int SelectedSourceDevice;
        public int SelectedSourceControl;
        public SKRect DeviceDropdownBounds;
        public SKRect ControlDropdownBounds;
        public bool DeviceDropdownOpen;
        public bool ControlDropdownOpen;
        public int HoveredDeviceIndex = -1;
        public int HoveredControlIndex = -1;
    }

    private sealed class MergeModeDropdownState
    {
        public SKRect SelectorBounds;
        public SKRect DropdownBounds;
        public bool DropdownOpen;
        public int HoveredIndex = -1;
        public bool SelectorHovered;
    }

    private sealed class MappingHighlightState
    {
        public int Row = -1;
        public uint VJoyDevice;
        public long StartTicks;
        public Dictionary<string, bool[]> PrevButtonState = new();
        public Dictionary<string, long> Debounce = new();
        public ControlDefinition? ControlDef;
        public long ControlHighlightTicks;
        public string? FlashText;
        public long FlashTicks;
    }

    private sealed class AutoScrollState
    {
        public bool Enabled;
        public SKRect CheckboxBounds;
        public bool CheckboxHovered;
    }

    private sealed class NetworkSwitchState
    {
        public SKRect ActionBounds;
        public bool ActionHovered;
        public SKRect BadgeBounds;
        public SKRect BadgeXBounds;
        public bool BadgeXHovered;
    }

    private sealed class ListScrollState
    {
        public float ScrollOffset;
        public float ContentHeight;
        public SKRect ListBounds;
    }

    private sealed class ThresholdEditorState
    {
        // Output mode toggle
        public bool IsThresholdMode;
        public SKRect AxisModeBounds;
        public SKRect ThresholdModeBounds;
        public int HoveredOutputMode = -1;

        // Direction toggle (multi-select: both can be active)
        public bool AboveEnabled;
        public bool BelowEnabled;
        public SKRect AboveBounds;
        public SKRect BelowBounds;
        public int HoveredDirection = -1;

        // Above threshold config
        public float AboveThreshold = 0.5f;
        public float AboveHysteresis = 0.05f;
        public string AboveKeyName = "";
        public List<string>? AboveModifiers;
        public SKRect AboveSliderBounds;
        public SKRect AboveHystSliderBounds;
        public SKRect AboveCaptureBounds;
        public SKRect AboveClearBounds;
        public bool AboveCaptureHovered;
        public bool AboveClearHovered;
        public bool AboveCapturing;
        public long AboveCaptureStartTicks;
        public string? AbovePendingModifier;
        public long AbovePendingModifierTicks;
        public bool DraggingAboveThreshold;
        public bool DraggingAboveHysteresis;

        // Below threshold config
        public float BelowThreshold = -0.5f;
        public float BelowHysteresis = 0.05f;
        public string BelowKeyName = "";
        public List<string>? BelowModifiers;
        public SKRect BelowSliderBounds;
        public SKRect BelowHystSliderBounds;
        public SKRect BelowCaptureBounds;
        public SKRect BelowClearBounds;
        public bool BelowCaptureHovered;
        public bool BelowClearHovered;
        public bool BelowCapturing;
        public long BelowCaptureStartTicks;
        public string? BelowPendingModifier;
        public long BelowPendingModifierTicks;
        public bool DraggingBelowThreshold;
        public bool DraggingBelowHysteresis;
    }
}
