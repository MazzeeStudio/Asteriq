using System.Runtime.InteropServices;
using Asteriq.DirectInput;
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
    private readonly DirectInputService? _directInputService;

    // Mapping category tabs (M1 = Buttons, M2 = Axes)
    private int _mappingCategory = 0;  // 0 = Buttons, 1 = Axes
    private int _hoveredMappingCategory = -1;
    private SKRect _mappingCategoryButtonsBounds;
    private SKRect _mappingCategoryAxesBounds;

    // Mappings tab UI state - Left panel (output list)
    private int _selectedMappingRow = -1;
    private bool _selectionIsExplicit = false; // true only when user explicitly clicked a row
    private int _hoveredMappingRow = -1;

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
    private SKRect[] _mergeOpButtonBounds = new SKRect[4]; // Average, Maximum, Minimum, Sum
    private int _hoveredMergeOpButton = -1;

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
    private readonly DeviceOrderState _deviceOrder = new();

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

    // Windows API for detecting held keys
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    // Virtual key codes for left/right modifiers
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;  // Left Alt
    private const int VK_RMENU = 0xA5;  // Right Alt

    private static bool IsKeyHeld(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>True if a curve control point is being dragged.</summary>
    public bool IsDraggingCurve => _curve.DraggingPoint >= 0;

    /// <summary>True if a deadzone handle is being dragged.</summary>
    public bool IsDraggingDeadzone => _deadzone.DraggingHandle >= 0;

    /// <summary>True if a duration slider is being dragged.</summary>
    public bool IsDraggingDuration => _buttonMode.DraggingPulse || _buttonMode.DraggingHold;

    /// <summary>True if the keyboard key capture mode is active.</summary>
    public bool IsCapturingKey => _keyboardOutput.IsCapturing;

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

        bool isAltGr = IsKeyHeld(VK_RMENU) && IsKeyHeld(VK_LCONTROL) && !IsKeyHeld(VK_RCONTROL);

        if (isAltGr)
        {
            modifiers.Add("AltGr");
        }
        else
        {
            if ((keys & Keys.Control) == Keys.Control)
            {
                if (IsKeyHeld(VK_RCONTROL))
                    modifiers.Add("RCtrl");
                else if (IsKeyHeld(VK_LCONTROL))
                    modifiers.Add("LCtrl");
            }
            if ((keys & Keys.Alt) == Keys.Alt)
            {
                if (IsKeyHeld(VK_RMENU))
                    modifiers.Add("RAlt");
                else if (IsKeyHeld(VK_LMENU))
                    modifiers.Add("LAlt");
            }
        }

        if ((keys & Keys.Shift) == Keys.Shift)
        {
            if (IsKeyHeld(VK_RSHIFT))
                modifiers.Add("RShift");
            else
                modifiers.Add("LShift");
        }

        var baseKey = keys & ~Keys.Modifiers;

        if (baseKey == Keys.ControlKey || baseKey == Keys.ShiftKey ||
            baseKey == Keys.Menu || baseKey == Keys.LWin || baseKey == Keys.RWin ||
            baseKey == Keys.LControlKey || baseKey == Keys.RControlKey ||
            baseKey == Keys.LShiftKey || baseKey == Keys.RShiftKey ||
            baseKey == Keys.LMenu || baseKey == Keys.RMenu)
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

    private static string GetModifierKeyName(Keys key)
    {
        if (key == Keys.ControlKey || key == Keys.Control)
        {
            if (IsKeyHeld(VK_RCONTROL)) return "RCtrl";
            return "LCtrl";
        }
        if (key == Keys.LControlKey) return "LCtrl";
        if (key == Keys.RControlKey) return "RCtrl";

        if (key == Keys.ShiftKey || key == Keys.Shift)
        {
            if (IsKeyHeld(VK_RSHIFT)) return "RShift";
            return "LShift";
        }
        if (key == Keys.LShiftKey) return "LShift";
        if (key == Keys.RShiftKey) return "RShift";

        if (key == Keys.Menu || key == Keys.Alt)
        {
            if (IsKeyHeld(VK_RMENU)) return "RAlt";
            return "LAlt";
        }
        if (key == Keys.LMenu) return "LAlt";
        if (key == Keys.RMenu) return "RAlt";

        return key.ToString();
    }

    // Modifier key names as stored in OutputTarget.KeyName (case-insensitive).
    private static readonly HashSet<string> s_scModifierKeyNames =
        new(StringComparer.OrdinalIgnoreCase) { "RCtrl", "LCtrl", "RShift", "LShift", "RAlt", "LAlt" };

    /// <summary>Returns true when the given key name (as stored in OutputTarget.KeyName) is a modifier key.</summary>
    private static bool IsModifierKeyName(string? keyName) =>
        keyName is not null && s_scModifierKeyNames.Contains(keyName);

    public MappingsTabController(
        TabContext ctx,
        SCExportProfileService? scExportProfileService = null,
        DirectInputService? directInputService = null)
    {
        _ctx = ctx;
        _scExportProfileService = scExportProfileService;
        _directInputService = directInputService;
    }
    public void Draw(SKCanvas canvas, SKRect bounds, float padLeft, float contentTop, float contentBottom)
    {
        DrawMappingsTabContent(canvas, bounds, padLeft, contentTop, contentBottom);
    }

    public void OnMouseDown(MouseEventArgs e)
    {
        // Right-click handling
        if (e.Button == MouseButtons.Right)
        {
            // Right-click on curve control points
            if (_mappingCategory == 1 && _curve.SelectedType == CurveType.Custom)
            {
                var pt = new SKPoint(e.X, e.Y);
                if (_curve.Bounds.Contains(pt))
                {
                    int pointIndex = FindCurvePointAt(pt, _curve.Bounds);
                    if (pointIndex >= 0)
                    {
                        RemoveCurveControlPoint(pointIndex);
                        return;
                    }
                }
            }
            return;
        }

        // Left-click handling

        // Mapping category tab clicks (M1 Buttons / M2 Axes)
        if (_hoveredMappingCategory >= 0)
        {
            _mappingCategory = _hoveredMappingCategory;
            _selectedMappingRow = -1;
            _selectionIsExplicit = false;
            _listScroll.ScrollOffset = 0;
            CancelInputListening();
            SelectFirstRowIfUnselected();
            return;
        }

        // Center panel: Auto-scroll checkbox toggle
        if (_autoScroll.CheckboxBounds.Contains(e.X, e.Y))
        {
            _autoScroll.Enabled = !_autoScroll.Enabled;
            _ctx.MarkDirty();
            return;
        }

        // Right panel: Add input button - toggles listening
        if (_addInputButtonHovered && _selectedMappingRow >= 0 && _selectionIsExplicit)
        {
            if (_inputDetection.IsListening)
            {
                CancelInputListening();
            }
            else
            {
                StartInputListening(_selectedMappingRow);
            }
            return;
        }

        // Right panel: Remove input source
        if (_hoveredInputSourceRemove >= 0 && _selectionIsExplicit)
        {
            RemoveInputSourceAtIndex(_hoveredInputSourceRemove);
            return;
        }

        // Right panel: Merge operation selection (axis category with 2+ inputs)
        if (_mappingCategory == 1 && _selectedMappingRow >= 0 && _selectionIsExplicit && _hoveredMergeOpButton >= 0)
        {
            UpdateMergeOperationForSelected(_hoveredMergeOpButton);
            return;
        }

        // Right panel: Button mode selection (button category) — blocked for modifier keys
        bool selectedIsModifier = _keyboardOutput.IsKeyboard && IsModifierKeyName(_keyboardOutput.SelectedKeyName);
        if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _selectionIsExplicit && _buttonMode.HoveredMode >= 0 && !selectedIsModifier)
        {
            _buttonMode.SelectedMode = (ButtonMode)_buttonMode.HoveredMode;
            UpdateButtonModeForSelected();
            return;
        }

        // Right panel: Pulse duration slider (button category, Pulse mode)
        if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _selectionIsExplicit && _buttonMode.SelectedMode == ButtonMode.Pulse)
        {
            var pt = new SKPoint(e.X, e.Y);
            if (_buttonMode.PulseSliderBounds.Contains(pt))
            {
                _buttonMode.DraggingPulse = true;
                UpdatePulseDurationFromMouse(e.X);
                return;
            }
        }

        // Right panel: Hold duration slider (button category, HoldToActivate mode)
        if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _selectionIsExplicit && _buttonMode.SelectedMode == ButtonMode.HoldToActivate)
        {
            var pt = new SKPoint(e.X, e.Y);
            if (_buttonMode.HoldSliderBounds.Contains(pt))
            {
                _buttonMode.DraggingHold = true;
                UpdateHoldDurationFromMouse(e.X);
                return;
            }
        }

        // Right panel: Output type selection (button category)
        if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _selectionIsExplicit && _keyboardOutput.HoveredOutputType >= 0)
        {
            _keyboardOutput.IsKeyboard = (_keyboardOutput.HoveredOutputType == 1);
            if (!_keyboardOutput.IsKeyboard)
            {
                _keyboardOutput.SelectedKeyName = ""; // Clear key when switching to Button mode
            }
            UpdateOutputTypeForSelected();
            return;
        }

        // Right panel: Key clear button (button category)
        if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _selectionIsExplicit && _keyboardOutput.IsKeyboard && _keyboardOutput.ClearHovered)
        {
            ClearKeyboardBinding();
            return;
        }

        // Right panel: Key capture field (button category)
        if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _selectionIsExplicit && _keyboardOutput.IsKeyboard && _keyboardOutput.CaptureHovered)
        {
            _keyboardOutput.IsCapturing = true;
            _keyboardOutput.CaptureStartTime = DateTime.Now;
            return;
        }

        // Right panel: Clear Mapping button (button category)
        if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _selectionIsExplicit && _clearAllButtonHovered)
        {
            ClearSelectedButtonMapping();
            return;
        }

        // Right panel: Axis settings - curve type selection (axis category)
        if (_mappingCategory == 1 && _selectedMappingRow >= 0 && _selectionIsExplicit)
        {
            // Check curve preset clicks
            var pt = new SKPoint(e.X, e.Y);
            if (HandleCurvePresetClick(pt))
                return;

            // Check for curve control point drag start
            if (_curve.SelectedType == CurveType.Custom && _curve.Bounds.Contains(pt))
            {
                int pointIndex = FindCurvePointAt(pt, _curve.Bounds);
                if (pointIndex >= 0)
                {
                    _curve.DraggingPoint = pointIndex;
                    return;
                }
                else
                {
                    // Click in curve area but not on point - add new point
                    var graphPt = CurveScreenToGraph(pt, _curve.Bounds);
                    AddCurveControlPoint(graphPt);
                    return;
                }
            }

            // Check deadzone handle click - select and start drag
            int dzHandle = FindDeadzoneHandleAt(pt);
            if (dzHandle >= 0)
            {
                _deadzone.SelectedHandle = dzHandle;
                _deadzone.DraggingHandle = dzHandle;
                _ctx.MarkDirty();
                return;
            }

            // Click on slider background (not on handle) - deselect
            if (_deadzone.SliderBounds.Contains(pt))
            {
                _deadzone.SelectedHandle = -1;
                _ctx.MarkDirty();
                return;
            }
        }

        // Mapping Settings / Device Order accordion — header clicks toggle which is expanded
        // When Device Order is expanded, clicking the Mapping Settings collapsed header collapses Device Order
        // (Mapping Settings bounds are set by DrawMappingSettingsPanel — the collapsed header fills the full bounds)
        if (_deviceOrder.IsExpanded)
        {
            // Check if the click is in the collapsed Mapping Settings header area (top 52px of right panel)
            // We don't store separate bounds for this — check if click is above the Device Order header
            if (!_deviceOrder.HeaderBounds.IsEmpty && e.Y < _deviceOrder.HeaderBounds.Top)
            {
                _deviceOrder.IsExpanded = false;
                _deviceOrder.OpenRow = -1;
                _ctx.MarkDirty();
                return;
            }
        }

        // Device Order panel — header click to expand/collapse
        if (!_deviceOrder.HeaderBounds.IsEmpty && _deviceOrder.HeaderBounds.Contains(e.X, e.Y))
        {
            _deviceOrder.IsExpanded = !_deviceOrder.IsExpanded;
            _deviceOrder.OpenRow = -1;
            _ctx.MarkDirty();
            return;
        }

        // Device Order panel — dropdown open/close
        if (_deviceOrder.OpenRow >= 0)
        {
            if (!_deviceOrder.DropdownBounds.IsEmpty && _deviceOrder.DropdownBounds.Contains(e.X, e.Y))
            {
                float itemH = 28f;
                int idx = (int)((e.Y - _deviceOrder.DropdownBounds.Top) / itemH);
                var existingSlots = _ctx.VJoyDevices.Where(v => v.Exists).OrderBy(v => v.Id).ToList();
                var profile = _ctx.GetActiveSCExportProfile?.Invoke();
                if (idx >= 0 && idx < existingSlots.Count && profile is not null)
                    AssignDeviceOrderSlot(profile, idx + 1, existingSlots[idx].Id);
                _deviceOrder.OpenRow = -1;
                _deviceOrder.HoveredIndex = -1;
                _ctx.MarkDirty();
                return;
            }
            else
            {
                _deviceOrder.OpenRow = -1;
                _deviceOrder.HoveredIndex = -1;
                _ctx.MarkDirty();
                for (int i = 0; i < _deviceOrder.SelectorBounds.Length; i++)
                {
                    if (!_deviceOrder.SelectorBounds[i].IsEmpty && _deviceOrder.SelectorBounds[i].Contains(e.X, e.Y))
                        return;
                }
            }
        }

        // Device Order — auto-detect button (only when expanded)
        if (_deviceOrder.IsExpanded && !_deviceOrder.AutoDetectBounds.IsEmpty && _deviceOrder.AutoDetectBounds.Contains(e.X, e.Y)
            && _directInputService is not null)
        {
            RunDeviceOrderAutoDetect();
            return;
        }

        // Device Order — row selector clicks (only when expanded)
        if (_deviceOrder.IsExpanded)
        for (int row = 0; row < _deviceOrder.SelectorBounds.Length; row++)
        {
            if (!_deviceOrder.SelectorBounds[row].IsEmpty && _deviceOrder.SelectorBounds[row].Contains(e.X, e.Y))
            {
                int vjoyCount = _ctx.VJoyDevices.Count(v => v.Exists);
                if (vjoyCount > 1)
                {
                    _deviceOrder.OpenRow = _deviceOrder.OpenRow == row ? -1 : row;
                    _deviceOrder.HoveredIndex = -1;
                    _ctx.MarkDirty();
                }
                return;
            }
        }

        // Left panel: NET SWITCH action button — assign switch button for selected row
        if (!_netSwitch.ActionBounds.IsEmpty && _netSwitch.ActionBounds.Contains(e.X, e.Y))
        {
            AssignSwitchButtonForSelectedRow();
            return;
        }

        // Left panel: NET SWITCH badge × — clear the switch button
        if (!_netSwitch.BadgeXBounds.IsEmpty && _netSwitch.BadgeXBounds.Contains(e.X, e.Y))
        {
            ClearNetworkSwitchButton();
            return;
        }

        // Left panel: vJoy device navigation
        if (_vjoyPrevHovered && _ctx.SelectedVJoyDeviceIndex > 0)
        {
            _ctx.SelectedVJoyDeviceIndex--;
            _selectedMappingRow = -1;
            _selectionIsExplicit = false;
            _listScroll.ScrollOffset = 0;
            _highlight.ControlDef = null;
            CancelInputListening();
            _ctx.UpdateMappingsPrimaryDeviceMap();
            SelectFirstRowIfUnselected();
            _ctx.MarkDirty();
            return;
        }
        if (_vjoyNextHovered && _ctx.SelectedVJoyDeviceIndex < _ctx.VJoyDevices.Count - 1)
        {
            _ctx.SelectedVJoyDeviceIndex++;
            _selectionIsExplicit = false;
            _listScroll.ScrollOffset = 0;
            _highlight.ControlDef = null;
            CancelInputListening();
            _ctx.UpdateMappingsPrimaryDeviceMap();
            SelectFirstRowIfUnselected();
            _ctx.MarkDirty();
            return;
        }

        // Left panel: Output row clicked - select it
        if (_hoveredMappingRow >= 0)
        {
            if (_hoveredMappingRow != _selectedMappingRow)
            {
                // Selecting a different row - cancel listening
                CancelInputListening();
                _selectedMappingRow = _hoveredMappingRow;
                // Load settings for the selected row
                LoadOutputTypeStateForRow();  // For buttons
                LoadAxisSettingsForRow();     // For axes
            }
            else
            {
                _selectedMappingRow = _hoveredMappingRow;
            }
            _selectionIsExplicit = true;
            // Trigger silhouette highlight for the selected row
            _highlight.ControlDef = GetControlForRow(_selectedMappingRow);
            _highlight.ControlHighlightTime = DateTime.Now;
            return;
        }
    }

    public void OnMouseMove(MouseEventArgs e)
    {
        // Reset hover states
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
        _hoveredMergeOpButton = -1;
        _hoveredMappingCategory = -1;
        _autoScroll.CheckboxHovered = false;

        // Center panel: Auto-scroll checkbox hover
        if (_autoScroll.CheckboxBounds.Contains(e.X, e.Y))
        {
            _autoScroll.CheckboxHovered = true;
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }

        // Right panel: Add input button
        if (_addInputButtonBounds.Contains(e.X, e.Y))
        {
            _addInputButtonHovered = true;
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }

        // Right panel: Input source remove buttons
        for (int i = 0; i < _inputSourceRemoveBounds.Count; i++)
        {
            if (_inputSourceRemoveBounds[i].Contains(e.X, e.Y))
            {
                _hoveredInputSourceRemove = i;
                _ctx.OwnerForm.Cursor = Cursors.Hand;
                return;
            }
        }

        // Right panel: Merge operation buttons (for axis category with 2+ inputs)
        if (_mappingCategory == 1 && _selectedMappingRow >= 0)
        {
            for (int i = 0; i < _mergeOpButtonBounds.Length; i++)
            {
                if (!_mergeOpButtonBounds[i].IsEmpty && _mergeOpButtonBounds[i].Contains(e.X, e.Y))
                {
                    _hoveredMergeOpButton = i;
                    _ctx.OwnerForm.Cursor = Cursors.Hand;
                    return;
                }
            }
        }

        // Right panel: Button mode buttons (for button category)
        if (_mappingCategory == 0 && _selectedMappingRow >= 0)
        {
            // Handle duration slider dragging
            if (_buttonMode.DraggingPulse)
            {
                UpdatePulseDurationFromMouse(e.X);
                _ctx.MarkDirty();
                return;
            }
            if (_buttonMode.DraggingHold)
            {
                UpdateHoldDurationFromMouse(e.X);
                _ctx.MarkDirty();
                return;
            }

            for (int i = 0; i < _buttonMode.ModeBounds.Length; i++)
            {
                if (_buttonMode.ModeBounds[i].Contains(e.X, e.Y))
                {
                    _buttonMode.HoveredMode = i;
                    _ctx.OwnerForm.Cursor = Cursors.Hand;
                    return;
                }
            }

            // Output type buttons
            if (_keyboardOutput.BtnBounds.Contains(e.X, e.Y))
            {
                _keyboardOutput.HoveredOutputType = 0;
                _ctx.OwnerForm.Cursor = Cursors.Hand;
                return;
            }
            if (_keyboardOutput.KeyBounds.Contains(e.X, e.Y))
            {
                _keyboardOutput.HoveredOutputType = 1;
                _ctx.OwnerForm.Cursor = Cursors.Hand;
                return;
            }

            // Key clear button (check before key capture field so it takes precedence)
            if (_keyboardOutput.IsKeyboard && !_keyboardOutput.ClearBounds.IsEmpty && _keyboardOutput.ClearBounds.Contains(e.X, e.Y))
            {
                _keyboardOutput.ClearHovered = true;
                _ctx.OwnerForm.Cursor = Cursors.Hand;
                return;
            }

            // Key capture field
            if (_keyboardOutput.IsKeyboard && _keyboardOutput.CaptureBounds.Contains(e.X, e.Y))
            {
                _keyboardOutput.CaptureHovered = true;
                _ctx.OwnerForm.Cursor = Cursors.IBeam;
                return;
            }

            // Clear Mapping button
            if (_clearAllButtonBounds.Contains(e.X, e.Y))
            {
                _clearAllButtonHovered = true;
                _ctx.OwnerForm.Cursor = Cursors.Hand;
                return;
            }
        }

        // Right panel: Curve editor handling (for axis category)
        if (_mappingCategory == 1 && _selectedMappingRow >= 0)
        {
            var pt = new SKPoint(e.X, e.Y);

            // Handle dragging operations
            if (_curve.DraggingPoint >= 0)
            {
                UpdateDraggedCurvePoint(pt);
                _ctx.MarkDirty();
                return;
            }
            if (_deadzone.DraggingHandle >= 0)
            {
                UpdateDraggedDeadzoneHandle(pt);
                _ctx.MarkDirty();
                return;
            }
            // Check curve point hover
            if (_curve.SelectedType == CurveType.Custom && _curve.Bounds.Contains(pt))
            {
                int newHovered = FindCurvePointAt(pt, _curve.Bounds);
                if (newHovered != _curve.HoveredPoint)
                {
                    _curve.HoveredPoint = newHovered;
                    _ctx.OwnerForm.Cursor = newHovered >= 0 ? Cursors.Hand : Cursors.Cross;
                    _ctx.MarkDirty();
                }
                return;
            }
            else if (_curve.HoveredPoint >= 0)
            {
                _curve.HoveredPoint = -1;
                _ctx.MarkDirty();
            }
        }

        // Device Order: header hover
        if (!_deviceOrder.HeaderBounds.IsEmpty && _deviceOrder.HeaderBounds.Contains(e.X, e.Y))
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }

        // Device Order: auto-detect button hover
        _deviceOrder.AutoDetectHovered = !_deviceOrder.AutoDetectBounds.IsEmpty
            && _deviceOrder.AutoDetectBounds.Contains(e.X, e.Y);
        if (_deviceOrder.AutoDetectHovered)
        {
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }

        // Device Order: row selector hover
        for (int i = 0; i < _deviceOrder.SelectorBounds.Length; i++)
        {
            if (!_deviceOrder.SelectorBounds[i].IsEmpty && _deviceOrder.SelectorBounds[i].Contains(e.X, e.Y))
            {
                _ctx.OwnerForm.Cursor = Cursors.Hand;
                return;
            }
        }

        // Device Order: open dropdown hover
        if (_deviceOrder.OpenRow >= 0 && !_deviceOrder.DropdownBounds.IsEmpty
            && _deviceOrder.DropdownBounds.Contains(e.X, e.Y))
        {
            float itemH = 28f;
            int idx = (int)((e.Y - _deviceOrder.DropdownBounds.Top) / itemH);
            int vjoyCount = _ctx.VJoyDevices.Count(v => v.Exists);
            int newHovered = idx >= 0 && idx < vjoyCount ? idx : -1;
            if (newHovered != _deviceOrder.HoveredIndex)
            {
                _deviceOrder.HoveredIndex = newHovered;
                _ctx.MarkDirty();
            }
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }
        else if (_deviceOrder.OpenRow >= 0 && _deviceOrder.HoveredIndex >= 0)
        {
            _deviceOrder.HoveredIndex = -1;
            _ctx.MarkDirty();
        }

        // Left panel: NET SWITCH action button
        if (!_netSwitch.ActionBounds.IsEmpty && _netSwitch.ActionBounds.Contains(e.X, e.Y))
        {
            _netSwitch.ActionHovered = true;
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }

        // Left panel: NET SWITCH badge × button
        if (!_netSwitch.BadgeXBounds.IsEmpty && _netSwitch.BadgeXBounds.Contains(e.X, e.Y))
        {
            _netSwitch.BadgeXHovered = true;
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }

        // Left panel: vJoy device selector buttons
        if (_vjoyPrevButtonBounds.Contains(e.X, e.Y) && _ctx.SelectedVJoyDeviceIndex > 0)
        {
            _vjoyPrevHovered = true;
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }
        if (_vjoyNextButtonBounds.Contains(e.X, e.Y) && _ctx.SelectedVJoyDeviceIndex < _ctx.VJoyDevices.Count - 1)
        {
            _vjoyNextHovered = true;
            _ctx.OwnerForm.Cursor = Cursors.Hand;
            return;
        }

        // Left panel: Mapping row hover
        for (int i = 0; i < _mappingRowBounds.Count; i++)
        {
            if (_mappingRowBounds[i].Contains(e.X, e.Y))
            {
                _hoveredMappingRow = i;
                _ctx.OwnerForm.Cursor = Cursors.Hand;
                return;
            }
        }

        // Mapping category tabs hover detection
        if (_mappingCategoryButtonsBounds.Contains(e.X, e.Y))
        {
            _hoveredMappingCategory = 0;
            _ctx.OwnerForm.Cursor = Cursors.Hand;
        }
        else if (_mappingCategoryAxesBounds.Contains(e.X, e.Y))
        {
            _hoveredMappingCategory = 1;
            _ctx.OwnerForm.Cursor = Cursors.Hand;
        }
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
            var baseKey = keyData & Keys.KeyCode;

            bool isModifierOnly = baseKey == Keys.ControlKey || baseKey == Keys.ShiftKey ||
                baseKey == Keys.Menu || baseKey == Keys.Control ||
                baseKey == Keys.Shift || baseKey == Keys.Alt ||
                baseKey == Keys.LControlKey || baseKey == Keys.RControlKey ||
                baseKey == Keys.LShiftKey || baseKey == Keys.RShiftKey ||
                baseKey == Keys.LMenu || baseKey == Keys.RMenu;

            if (isModifierOnly)
            {
                _keyboardOutput.SelectedKeyName = GetModifierKeyName(baseKey);
                _keyboardOutput.SelectedModifiers = null;
                _keyboardOutput.IsKeyboard = true;
                _keyboardOutput.IsCapturing = false;
                UpdateKeyNameForSelected();
                return true;
            }

            var (keyName, modifiers) = GetKeyNameAndModifiersFromKeys(keyData);
            if (!string.IsNullOrEmpty(keyName))
            {
                _keyboardOutput.SelectedKeyName = keyName;
                _keyboardOutput.SelectedModifiers = modifiers.Count > 0 ? modifiers : null;
                _keyboardOutput.IsKeyboard = true;
                _keyboardOutput.IsCapturing = false;
                UpdateKeyNameForSelected();
            }
            return true;
        }

        // Cancel key capture / input listening with Escape
        if (keyData == Keys.Escape)
        {
            if (_keyboardOutput.IsCapturing)
            {
                _keyboardOutput.IsCapturing = false;
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
            float elapsed = (float)(DateTime.Now - _highlight.ControlHighlightTime).TotalSeconds;
            if (elapsed < 3f)
                _ctx.MarkDirty();
            else
                _highlight.ControlDef = null;
        }

        if (_highlight.FlashText is not null)
        {
            float elapsed = (float)(DateTime.Now - _highlight.FlashTime).TotalSeconds;
            if (elapsed < 2.5f)
                _ctx.MarkDirty();
            else
                _highlight.FlashText = null;
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
        _selectionIsExplicit = false; // auto-selected — user has not explicitly chosen this row
        LoadOutputTypeStateForRow();
        LoadAxisSettingsForRow();
    }

    public void OnDeactivated()
    {
        _buttonMode.DraggingPulse = false;
        _buttonMode.DraggingHold = false;
    }

    // ── Device Order Logic ────────────────────────────────────────────────────

    private void AssignDeviceOrderSlot(SCExportProfile profile, int scInst, uint newVJoySlotId)
    {
        var existingSlots = _ctx.VJoyDevices.Where(v => v.Exists).ToList();
        if (existingSlots.Count == 0) return;

        uint? prevSlotId = null;
        foreach (var slot in existingSlots)
        {
            if (profile.GetSCInstance(slot.Id) == scInst)
            {
                prevSlotId = slot.Id;
                break;
            }
        }

        if (prevSlotId == newVJoySlotId) return;

        int newSlotCurrentInst = profile.GetSCInstance(newVJoySlotId);
        profile.SetSCInstance(newVJoySlotId, scInst);
        if (prevSlotId.HasValue)
            profile.SetSCInstance(prevSlotId.Value, newSlotCurrentInst);

        if (!string.IsNullOrEmpty(profile.ProfileName))
            _scExportProfileService?.SaveProfile(profile);
        _ctx.MarkDirty();
    }

    private void RunDeviceOrderAutoDetect()
    {
        if (_directInputService is null) return;
        var profile = _ctx.GetActiveSCExportProfile?.Invoke();
        if (profile is null) return;

        try
        {
            var diDevices = _directInputService.EnumerateDevices();
            var vjoySlots = _ctx.VJoyDevices.Where(v => v.Exists);
            var mapping = VJoyDirectInputOrderService.DetectVJoyDiOrder(vjoySlots, diDevices);

            foreach (var (vjoyId, scInstance) in mapping)
                profile.SetSCInstance(vjoyId, scInstance);

            if (!string.IsNullOrEmpty(profile.ProfileName))
                _scExportProfileService?.SaveProfile(profile);
            _ctx.MarkDirty();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Auto-detect failed — silently ignore (no status bar in Mappings tab)
        }
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
                var elapsed = (DateTime.Now - lastTime).TotalMilliseconds;
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
                _highlight.StartTime = DateTime.Now;
                _highlight.Debounce[debounceKey] = DateTime.Now; // Record highlight time for debounce

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
                        _highlight.ControlHighlightTime = DateTime.Now;
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
                _highlight.FlashTime = DateTime.Now;
                _highlight.Debounce[debounceKey] = DateTime.Now; // Debounce the no-mapping flash too
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
        public DateTime CaptureStartTime = DateTime.MinValue;
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
        public DateTime ListeningStartTime = DateTime.MinValue;
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

    private sealed class MappingHighlightState
    {
        public int Row = -1;
        public uint VJoyDevice;
        public DateTime StartTime = DateTime.MinValue;
        public Dictionary<string, bool[]> PrevButtonState = new();
        public Dictionary<string, DateTime> Debounce = new();
        public ControlDefinition? ControlDef;
        public DateTime ControlHighlightTime;
        public string? FlashText;
        public DateTime FlashTime;
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

    private sealed class DeviceOrderState
    {
        public int OpenRow = -1;
        public SKRect[] SelectorBounds = Array.Empty<SKRect>();
        public SKRect DropdownBounds = SKRect.Empty;
        public int HoveredIndex = -1;
        public SKRect AutoDetectBounds = SKRect.Empty;
        public bool AutoDetectHovered;
        public bool IsExpanded;
        public SKRect HeaderBounds;
    }
}
