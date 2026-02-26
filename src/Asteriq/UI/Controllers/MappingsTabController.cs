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
public class MappingsTabController : ITabController
{
    private readonly TabContext _ctx;

    // Mapping category tabs (M1 = Buttons, M2 = Axes)
    private int _mappingCategory = 0;  // 0 = Buttons, 1 = Axes
    private int _hoveredMappingCategory = -1;
    private SKRect _mappingCategoryButtonsBounds;
    private SKRect _mappingCategoryAxesBounds;

    // Mappings tab UI state - Left panel (output list)
    private int _selectedMappingRow = -1;
    private int _hoveredMappingRow = -1;

    // Silhouette highlight: shown when a mapping row is selected
    private ControlDefinition? _mappingHighlightControl;
    private DateTime _mappingHighlightTime;
    private SKRect _vjoyPrevButtonBounds;
    private SKRect _vjoyNextButtonBounds;
    private bool _vjoyPrevHovered;
    private bool _vjoyNextHovered;
    private List<SKRect> _mappingRowBounds = new();
    private List<SKRect> _mappingAddButtonBounds = new();
    private List<SKRect> _mappingRemoveButtonBounds = new();
    private int _hoveredAddButton = -1;
    private int _hoveredRemoveButton = -1;
    private float _bindingsScrollOffset = 0;
    private float _bindingsContentHeight = 0;
    private SKRect _bindingsListBounds;

    // Mappings tab UI state - Right panel (mapping editor)
    private bool _mappingEditorOpen = false;
    private int _editingRowIndex = -1;
    private bool _isEditingAxis = false;
    private InputDetectionService? _inputDetectionService;

    // Mapping editor - input detection
    private bool _isListeningForInput = false;
    private SKRect _inputFieldBounds;
    private bool _inputFieldHovered = false;
    private DetectedInput? _pendingInput;
    private const int DoubleClickThresholdMs = 400;

    // Mapping editor - manual entry
    private bool _manualEntryMode = false;
    private SKRect _manualEntryButtonBounds;
    private bool _manualEntryButtonHovered = false;
    private int _selectedSourceDevice = 0;
    private int _selectedSourceControl = 0;
    private SKRect _deviceDropdownBounds;
    private SKRect _controlDropdownBounds;
    private bool _deviceDropdownOpen = false;
    private bool _controlDropdownOpen = false;
    private int _hoveredDeviceIndex = -1;
    private int _hoveredControlIndex = -1;

    // Mapping editor - button modes
    private ButtonMode _selectedButtonMode = ButtonMode.Normal;
    private SKRect[] _buttonModeBounds = new SKRect[4];
    private int _hoveredButtonMode = -1;

    // Button mode duration settings
    private int _pulseDurationMs = 100;      // Duration for Pulse mode (100-1000ms)
    private int _holdDurationMs = 500;       // Duration for HoldToActivate mode (200-2000ms)
    private SKRect _pulseDurationSliderBounds;
    private SKRect _holdDurationSliderBounds;
    private bool _draggingPulseDuration = false;
    private bool _draggingHoldDuration = false;

    // Mapping editor - output type (Button vs Keyboard)
    private bool _outputTypeIsKeyboard = false;
    private SKRect _outputTypeBtnBounds;
    private SKRect _outputTypeKeyBounds;
    private int _hoveredOutputType = -1; // 0=Button, 1=Keyboard
    private string _selectedKeyName = "";
    private List<string>? _selectedModifiers = null;
    private SKRect _keyCaptureBounds;
    private bool _keyCaptureBoundsHovered;
    private SKRect _keyClearButtonBounds;
    private bool _keyClearButtonHovered;
    private bool _isCapturingKey = false;
    private DateTime _keyCaptureStartTime = DateTime.MinValue;
    private const int KeyCaptureTimeoutMs = 10000; // 10 second timeout for key capture

    // Input listening timeout
    private DateTime _inputListeningStartTime = DateTime.MinValue;
    private const int InputListeningTimeoutMs = 15000; // 15 second timeout for input listening

    // Pending keyboard binding - when user assigns keyboard key to empty slot
    private string? _pendingKeyboardKey;
    private List<string>? _pendingKeyboardModifiers;
    private int _pendingKeyboardOutputIndex = -1;
    private uint _pendingKeyboardVJoyDevice = 0;

    // Double-click detection for binding rows
    private DateTime _lastRowClickTime = DateTime.MinValue;
    private const int DoubleClickMs = 400;

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

    // Input-to-mapping highlight (attention effect when physical input is pressed)
    private int _highlightedMappingRow = -1;  // Which row to highlight (-1 = none)
    private uint _highlightedVJoyDevice = 0;  // Which vJoy device the highlighted row belongs to
    private DateTime _highlightStartTime = DateTime.MinValue;
    private const int HighlightDurationMs = 1500; // How long the attention highlight lasts (1.5 seconds)
    private Dictionary<string, bool[]> _highlightPrevButtonState = new(); // Previous frame button states (for rising-edge detection)
    private Dictionary<string, DateTime> _highlightDebounce = new(); // Debounce: last highlight time per button
    private const int HighlightDebounceCooldownMs = 500; // Minimum time between highlights for same button

    // Auto-scroll on input (center panel checkbox)
    private bool _autoScrollEnabled;
    private SKRect _autoScrollCheckboxBounds;
    private bool _autoScrollCheckboxHovered;
    private string? _noMappingFlashText;
    private DateTime _noMappingFlashTime;

    // Curve editor state
    private SKRect _curveEditorBounds;
    private List<SKPoint> _curveControlPoints = new() { new(0, 0), new(1, 1) };
    private int _hoveredCurvePoint = -1;
    private int _draggingCurvePoint = -1;
    private CurveType _selectedCurveType = CurveType.Linear;
    private bool _curveSymmetrical = false;  // When true, curve points mirror around center
    private SKRect _curveSymmetricalCheckboxBounds;

    // Deadzone state (4-parameter model like JoystickGremlinEx)
    private float _deadzoneMin = -1.0f;        // Left edge (start)
    private float _deadzoneCenterMin = 0.0f;   // Center left (start of center deadzone)
    private float _deadzoneCenterMax = 0.0f;   // Center right (end of center deadzone)
    private float _deadzoneMax = 1.0f;         // Right edge (end)
    private bool _deadzoneCenterEnabled = false; // Whether center deadzone handles are shown

    // Deadzone UI bounds
    private SKRect _deadzoneSliderBounds;
    private SKRect _deadzoneCenterCheckboxBounds; // "Centre" toggle checkbox
    private SKRect[] _deadzonePresetBounds = new SKRect[4]; // Presets: 0%, 2%, 5%, 10%
    private int _draggingDeadzoneHandle = -1; // 0=min, 1=centerMin, 2=centerMax, 3=max
    private int _selectedDeadzoneHandle = -1; // Currently selected handle for preset application

    // Legacy compatibility
    private float _axisDeadzone
    {
        get => Math.Max(Math.Abs(_deadzoneCenterMin), Math.Abs(_deadzoneCenterMax));
        set
        {
            _deadzoneCenterMin = -Math.Abs(value);
            _deadzoneCenterMax = Math.Abs(value);
        }
    }

    private SKRect[] _curvePresetBounds = new SKRect[4]; // Bounds for Linear, S-Curve, Expo, Custom buttons
    private SKRect _invertToggleBounds;
    private bool _axisInverted = false;

    // Windows API for detecting held keys
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    // Virtual key codes for left/right modifiers
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;  // Left Alt
    private const int VK_RMENU = 0xA5;  // Right Alt

    private static bool IsKeyHeld(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>True if a curve control point is being dragged.</summary>
    public bool IsDraggingCurve => _draggingCurvePoint >= 0;

    /// <summary>True if a deadzone handle is being dragged.</summary>
    public bool IsDraggingDeadzone => _draggingDeadzoneHandle >= 0;

    /// <summary>True if a duration slider is being dragged.</summary>
    public bool IsDraggingDuration => _draggingPulseDuration || _draggingHoldDuration;

    /// <summary>True if the keyboard key capture mode is active.</summary>
    public bool IsCapturingKey => _isCapturingKey;

    /// <summary>True if listening for physical input detection.</summary>
    public bool IsListeningForInput => _isListeningForInput;

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

    public MappingsTabController(TabContext ctx)
    {
        _ctx = ctx;
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
            if (_mappingCategory == 1 && _selectedCurveType == CurveType.Custom)
            {
                var pt = new SKPoint(e.X, e.Y);
                if (_curveEditorBounds.Contains(pt))
                {
                    int pointIndex = FindCurvePointAt(pt, _curveEditorBounds);
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
            _selectedMappingRow = -1; // Reset selection when switching categories
            _bindingsScrollOffset = 0; // Reset scroll when switching categories
            CancelInputListening();
            return;
        }

        // Center panel: Auto-scroll checkbox toggle
        if (_autoScrollCheckboxBounds.Contains(e.X, e.Y))
        {
            _autoScrollEnabled = !_autoScrollEnabled;
            _ctx.MarkDirty();
            return;
        }

        // Right panel: Add input button - toggles listening
        if (_addInputButtonHovered && _selectedMappingRow >= 0)
        {
            if (_isListeningForInput)
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
        if (_hoveredInputSourceRemove >= 0)
        {
            RemoveInputSourceAtIndex(_hoveredInputSourceRemove);
            return;
        }

        // Right panel: Merge operation selection (axis category with 2+ inputs)
        if (_mappingCategory == 1 && _selectedMappingRow >= 0 && _hoveredMergeOpButton >= 0)
        {
            UpdateMergeOperationForSelected(_hoveredMergeOpButton);
            return;
        }

        // Right panel: Button mode selection (button category)
        if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _hoveredButtonMode >= 0)
        {
            _selectedButtonMode = (ButtonMode)_hoveredButtonMode;
            UpdateButtonModeForSelected();
            return;
        }

        // Right panel: Pulse duration slider (button category, Pulse mode)
        if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _selectedButtonMode == ButtonMode.Pulse)
        {
            var pt = new SKPoint(e.X, e.Y);
            if (_pulseDurationSliderBounds.Contains(pt))
            {
                _draggingPulseDuration = true;
                UpdatePulseDurationFromMouse(e.X);
                return;
            }
        }

        // Right panel: Hold duration slider (button category, HoldToActivate mode)
        if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _selectedButtonMode == ButtonMode.HoldToActivate)
        {
            var pt = new SKPoint(e.X, e.Y);
            if (_holdDurationSliderBounds.Contains(pt))
            {
                _draggingHoldDuration = true;
                UpdateHoldDurationFromMouse(e.X);
                return;
            }
        }

        // Right panel: Output type selection (button category)
        if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _hoveredOutputType >= 0)
        {
            _outputTypeIsKeyboard = (_hoveredOutputType == 1);
            if (!_outputTypeIsKeyboard)
            {
                _selectedKeyName = ""; // Clear key when switching to Button mode
            }
            UpdateOutputTypeForSelected();
            return;
        }

        // Right panel: Key clear button (button category)
        if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _outputTypeIsKeyboard && _keyClearButtonHovered)
        {
            ClearKeyboardBinding();
            return;
        }

        // Right panel: Key capture field (button category)
        if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _outputTypeIsKeyboard && _keyCaptureBoundsHovered)
        {
            _isCapturingKey = true;
            _keyCaptureStartTime = DateTime.Now;
            return;
        }

        // Right panel: Clear Mapping button (button category)
        if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _clearAllButtonHovered)
        {
            ClearSelectedButtonMapping();
            return;
        }

        // Right panel: Axis settings - curve type selection (axis category)
        if (_mappingCategory == 1 && _selectedMappingRow >= 0)
        {
            // Check curve preset clicks
            var pt = new SKPoint(e.X, e.Y);
            if (HandleCurvePresetClick(pt))
                return;

            // Check for curve control point drag start
            if (_selectedCurveType == CurveType.Custom && _curveEditorBounds.Contains(pt))
            {
                int pointIndex = FindCurvePointAt(pt, _curveEditorBounds);
                if (pointIndex >= 0)
                {
                    _draggingCurvePoint = pointIndex;
                    return;
                }
                else
                {
                    // Click in curve area but not on point - add new point
                    var graphPt = CurveScreenToGraph(pt, _curveEditorBounds);
                    AddCurveControlPoint(graphPt);
                    return;
                }
            }

            // Check deadzone handle click - select and start drag
            int dzHandle = FindDeadzoneHandleAt(pt);
            if (dzHandle >= 0)
            {
                _selectedDeadzoneHandle = dzHandle;
                _draggingDeadzoneHandle = dzHandle;
                _ctx.MarkDirty();
                return;
            }

            // Click on slider background (not on handle) - deselect
            if (_deadzoneSliderBounds.Contains(pt))
            {
                _selectedDeadzoneHandle = -1;
                _ctx.MarkDirty();
                return;
            }
        }

        // Left panel: vJoy device navigation
        if (_vjoyPrevHovered && _ctx.SelectedVJoyDeviceIndex > 0)
        {
            _ctx.SelectedVJoyDeviceIndex--;
            _selectedMappingRow = -1;
            _bindingsScrollOffset = 0; // Reset scroll when changing device
            _mappingHighlightControl = null; // Clear silhouette lead line for previous device
            CancelInputListening();
            _ctx.UpdateMappingsPrimaryDeviceMap();
            _ctx.MarkDirty();
            return;
        }
        if (_vjoyNextHovered && _ctx.SelectedVJoyDeviceIndex < _ctx.VJoyDevices.Count - 1)
        {
            _ctx.SelectedVJoyDeviceIndex++;
            _selectedMappingRow = -1;
            _bindingsScrollOffset = 0; // Reset scroll when changing device
            _mappingHighlightControl = null; // Clear silhouette lead line for previous device
            CancelInputListening();
            _ctx.UpdateMappingsPrimaryDeviceMap();
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
            // Trigger silhouette highlight for the selected row
            _mappingHighlightControl = GetControlForRow(_selectedMappingRow);
            _mappingHighlightTime = DateTime.Now;
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
        _hoveredButtonMode = -1;
        _hoveredOutputType = -1;
        _keyCaptureBoundsHovered = false;
        _keyClearButtonHovered = false;
        _addInputButtonHovered = false;
        _clearAllButtonHovered = false;
        _hoveredInputSourceRemove = -1;
        _hoveredMergeOpButton = -1;
        _hoveredMappingCategory = -1;
        _autoScrollCheckboxHovered = false;

        // Center panel: Auto-scroll checkbox hover
        if (_autoScrollCheckboxBounds.Contains(e.X, e.Y))
        {
            _autoScrollCheckboxHovered = true;
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
            if (_draggingPulseDuration)
            {
                UpdatePulseDurationFromMouse(e.X);
                _ctx.MarkDirty();
                return;
            }
            if (_draggingHoldDuration)
            {
                UpdateHoldDurationFromMouse(e.X);
                _ctx.MarkDirty();
                return;
            }

            for (int i = 0; i < _buttonModeBounds.Length; i++)
            {
                if (_buttonModeBounds[i].Contains(e.X, e.Y))
                {
                    _hoveredButtonMode = i;
                    _ctx.OwnerForm.Cursor = Cursors.Hand;
                    return;
                }
            }

            // Output type buttons
            if (_outputTypeBtnBounds.Contains(e.X, e.Y))
            {
                _hoveredOutputType = 0;
                _ctx.OwnerForm.Cursor = Cursors.Hand;
                return;
            }
            if (_outputTypeKeyBounds.Contains(e.X, e.Y))
            {
                _hoveredOutputType = 1;
                _ctx.OwnerForm.Cursor = Cursors.Hand;
                return;
            }

            // Key clear button (check before key capture field so it takes precedence)
            if (_outputTypeIsKeyboard && !_keyClearButtonBounds.IsEmpty && _keyClearButtonBounds.Contains(e.X, e.Y))
            {
                _keyClearButtonHovered = true;
                _ctx.OwnerForm.Cursor = Cursors.Hand;
                return;
            }

            // Key capture field
            if (_outputTypeIsKeyboard && _keyCaptureBounds.Contains(e.X, e.Y))
            {
                _keyCaptureBoundsHovered = true;
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
            if (_draggingCurvePoint >= 0)
            {
                UpdateDraggedCurvePoint(pt);
                _ctx.MarkDirty();
                return;
            }
            if (_draggingDeadzoneHandle >= 0)
            {
                UpdateDraggedDeadzoneHandle(pt);
                _ctx.MarkDirty();
                return;
            }
            // Check curve point hover
            if (_selectedCurveType == CurveType.Custom && _curveEditorBounds.Contains(pt))
            {
                int newHovered = FindCurvePointAt(pt, _curveEditorBounds);
                if (newHovered != _hoveredCurvePoint)
                {
                    _hoveredCurvePoint = newHovered;
                    _ctx.OwnerForm.Cursor = newHovered >= 0 ? Cursors.Hand : Cursors.Cross;
                    _ctx.MarkDirty();
                }
                return;
            }
            else if (_hoveredCurvePoint >= 0)
            {
                _hoveredCurvePoint = -1;
                _ctx.MarkDirty();
            }
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
        if (_draggingCurvePoint >= 0 || _draggingDeadzoneHandle >= 0)
        {
            _draggingCurvePoint = -1;
            _draggingDeadzoneHandle = -1;
            SaveAxisSettingsForRow();  // Persist curve/deadzone changes
            _ctx.MarkDirty();
        }

        // Release duration slider dragging
        if (_draggingPulseDuration || _draggingHoldDuration)
        {
            _draggingPulseDuration = false;
            _draggingHoldDuration = false;
            UpdateDurationForSelectedMapping();
            _ctx.MarkDirty();
        }
    }

    public void OnMouseWheel(MouseEventArgs e)
    {
        // Handle scroll when mouse is over the bindings list
        if (_bindingsListBounds.Contains(e.X, e.Y))
        {
            float scrollAmount = -e.Delta / 4f; // Delta is usually ±120, divide for smooth scrolling
            float maxScroll = Math.Max(0, _bindingsContentHeight - _bindingsListBounds.Height);

            _bindingsScrollOffset = Math.Clamp(_bindingsScrollOffset + scrollAmount, 0, maxScroll);
            _ctx.MarkDirty();
        }
    }

    public bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Handle key capture for keyboard output mapping
        if (_isCapturingKey)
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
                _selectedKeyName = GetModifierKeyName(baseKey);
                _selectedModifiers = null;
                _outputTypeIsKeyboard = true;
                _isCapturingKey = false;
                UpdateKeyNameForSelected();
                return true;
            }

            var (keyName, modifiers) = GetKeyNameAndModifiersFromKeys(keyData);
            if (!string.IsNullOrEmpty(keyName))
            {
                _selectedKeyName = keyName;
                _selectedModifiers = modifiers.Count > 0 ? modifiers : null;
                _outputTypeIsKeyboard = true;
                _isCapturingKey = false;
                UpdateKeyNameForSelected();
            }
            return true;
        }

        // Cancel key capture / input listening with Escape
        if (keyData == Keys.Escape)
        {
            if (_isCapturingKey)
            {
                _isCapturingKey = false;
                return true;
            }
            if (_isListeningForInput)
            {
                CancelInputListening();
                return true;
            }
        }

        return false;
    }

    public void OnMouseLeave()
    {
        _draggingCurvePoint = -1;
        _draggingDeadzoneHandle = -1;
        _draggingPulseDuration = false;
        _draggingHoldDuration = false;
        _autoScrollCheckboxHovered = false;
    }

    public void OnTick()
    {
        if (_mappingHighlightControl is not null)
        {
            float elapsed = (float)(DateTime.Now - _mappingHighlightTime).TotalSeconds;
            if (elapsed < 3f)
                _ctx.MarkDirty();
            else
                _mappingHighlightControl = null;
        }

        if (_noMappingFlashText is not null)
        {
            float elapsed = (float)(DateTime.Now - _noMappingFlashTime).TotalSeconds;
            if (elapsed < 2.5f)
                _ctx.MarkDirty();
            else
                _noMappingFlashText = null;
        }
    }

    public void OnActivated()
    {
        _highlightPrevButtonState.Clear();
        _highlightDebounce.Clear();
        _highlightedMappingRow = -1;
        _ctx.UpdateMappingsPrimaryDeviceMap();
    }

    public void OnDeactivated()
    {
        _draggingPulseDuration = false;
        _draggingHoldDuration = false;
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
        _highlightPrevButtonState.TryGetValue(state.DeviceName, out var prevButtons);

        // Check button presses - only trigger on rising edge (was NOT pressed, now IS pressed)
        for (int i = 0; i < state.Buttons.Length; i++)
        {
            bool isPressed = state.Buttons[i];
            bool wasPressed = prevButtons is not null && i < prevButtons.Length && prevButtons[i];

            // Only trigger on rising edge (transition from not-pressed to pressed)
            if (!isPressed || wasPressed) continue;

            // Check debounce - don't re-highlight the same button too quickly
            string debounceKey = $"{state.DeviceName}:{i}";
            if (_highlightDebounce.TryGetValue(debounceKey, out var lastTime))
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
                _highlightedMappingRow = mapping.Output.Index;
                _highlightedVJoyDevice = mapping.Output.VJoyDevice;
                _highlightStartTime = DateTime.Now;
                _highlightDebounce[debounceKey] = DateTime.Now; // Record highlight time for debounce

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
                        _mappingHighlightControl = control;
                        _mappingHighlightTime = DateTime.Now;
                    }
                }

                // Auto-scroll to bring the mapped row into view (Buttons category only)
                if (_autoScrollEnabled && _mappingCategory == 0)
                {
                    bool hasVJoy = _ctx.VJoyDevices.Count > 0 && _ctx.SelectedVJoyDeviceIndex < _ctx.VJoyDevices.Count;
                    if (hasVJoy && _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex].Id == _highlightedVJoyDevice)
                    {
                        const float rowHeight = 32f;
                        const float rowGap = 4f;
                        float rowTop = mapping.Output.Index * (rowHeight + rowGap);
                        // Center the row in the visible list area
                        float targetScroll = rowTop - _bindingsListBounds.Height / 2f + rowHeight / 2f;
                        float maxScroll = Math.Max(0, _bindingsContentHeight - _bindingsListBounds.Height);
                        _bindingsScrollOffset = Math.Clamp(targetScroll, 0, maxScroll);
                    }
                }

                _noMappingFlashText = null; // Clear any "no mapping" indicator
                break;
            }
            else if (_autoScrollEnabled)
            {
                // Button pressed with no mapping - flash an indicator
                _noMappingFlashText = $"BUTTON {i + 1} — NO MAPPING";
                _noMappingFlashTime = DateTime.Now;
                _highlightDebounce[debounceKey] = DateTime.Now; // Debounce the no-mapping flash too
            }
        }

        // Store current button state for next frame comparison
        _highlightPrevButtonState[state.DeviceName] = (bool[])state.Buttons.Clone();
    }

    /// <summary>
    /// Returns the current highlighted mapping row index, or -1 if none.
    /// Used by MainForm to detect highlight changes.
    /// </summary>
    public int HighlightedMappingRow => _highlightedMappingRow;

    /// <summary>
    /// Returns the vJoy device ID of the highlighted row.
    /// </summary>
    public uint HighlightedVJoyDevice => _highlightedVJoyDevice;

    private void DrawVerticalSideTab(SKCanvas canvas, SKRect bounds, string label, bool isSelected, bool isHovered)
        => FUIWidgets.DrawVerticalSideTab(canvas, bounds, label, isSelected, isHovered);

    private void DrawSvgInBounds(SKCanvas canvas, SKSvg svg, SKRect bounds, bool mirror = false)
    {
        if (svg.Picture is null) return;

        var svgBounds = svg.Picture.CullRect;
        if (svgBounds.Width <= 0 || svgBounds.Height <= 0) return;

        float scaleX = bounds.Width / svgBounds.Width;
        float scaleY = bounds.Height / svgBounds.Height;
        float scale = Math.Min(scaleX, scaleY) * 0.95f;

        float scaledWidth = svgBounds.Width * scale;
        float scaledHeight = svgBounds.Height * scale;

        float offsetX = bounds.Left + (bounds.Width - scaledWidth) / 2 - svgBounds.Left * scale;
        float offsetY = bounds.Top + (bounds.Height - scaledHeight) / 2 - svgBounds.Top * scale;

        _ctx.SvgScale = scale;
        _ctx.SvgOffset = new SKPoint(offsetX, offsetY);
        _ctx.SvgMirrored = mirror;

        canvas.Save();
        canvas.Translate(offsetX, offsetY);

        if (mirror)
        {
            canvas.Translate(scaledWidth, 0);
            canvas.Scale(-scale, scale);
        }
        else
        {
            canvas.Scale(scale);
        }

        canvas.DrawPicture(svg.Picture);
        canvas.Restore();
    }

    #region Mappings Tab Drawing
    private void DrawMappingsTabContent(SKCanvas canvas, SKRect bounds, float sideTabPad, float contentTop, float contentBottom)
    {
        float frameInset = 5f;
        float pad = FUIRenderer.SpaceLG;  // Standard padding for right side
        var contentBounds = new SKRect(sideTabPad, contentTop, bounds.Right - pad, contentBottom);

        // Three-panel layout: Left (bindings list) | Center (device view) | Right (settings)
        var layout = FUIRenderer.CalculateLayout(contentBounds.Width, minLeftPanel: 360f, minRightPanel: 280f, maxSidePanel: 500f);
        float gap = layout.Gutter;

        var leftBounds = new SKRect(contentBounds.Left, contentBounds.Top,
            contentBounds.Left + layout.LeftPanelWidth, contentBounds.Bottom);
        var centerBounds = new SKRect(leftBounds.Right + gap, contentBounds.Top,
            leftBounds.Right + gap + layout.CenterWidth, contentBounds.Bottom);
        var rightBounds = new SKRect(centerBounds.Right + gap, contentBounds.Top,
            contentBounds.Right, contentBounds.Bottom);

        // Refresh vJoy devices list
        if (_ctx.VJoyDevices.Count == 0)
        {
            _ctx.VJoyDevices = _ctx.VJoyService.EnumerateDevices();
        }

        // LEFT PANEL - Bindings List
        DrawBindingsPanel(canvas, leftBounds, frameInset);

        // CENTER PANEL - Device Visualization
        DrawDeviceVisualizationPanel(canvas, centerBounds, frameInset);

        // RIGHT PANEL - Settings
        DrawMappingSettingsPanel(canvas, rightBounds, frameInset);
    }

    private void DrawBindingsPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Vertical side tabs width
        float sideTabWidth = 28f;

        // Panel shadow
        FUIRenderer.DrawPanelShadow(canvas, bounds, 3f, 3f, 10f);

        // Panel background (shifted right to make room for side tabs)
        var contentBounds = new SKRect(bounds.Left + frameInset + sideTabWidth, bounds.Top + frameInset,
                                        bounds.Right - frameInset, bounds.Bottom - frameInset);
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(140),
            IsAntialias = true
        };
        canvas.DrawRect(contentBounds, bgPaint);

        // Draw vertical side tabs (M1 Axes, M2 Buttons)
        DrawMappingCategorySideTabs(canvas, bounds.Left + frameInset, bounds.Top + frameInset,
            sideTabWidth, bounds.Height - frameInset * 2);

        // L-corner frame (adjusted for side tabs)
        var frameBounds = new SKRect(bounds.Left + sideTabWidth, bounds.Top, bounds.Right, bounds.Bottom);
        FUIRenderer.DrawLCornerFrame(canvas, frameBounds, FUIColors.Frame, 40f, 10f);

        float y = contentBounds.Top + 10;
        float leftMargin = contentBounds.Left + 10;
        float rightMargin = contentBounds.Right - 10;

        // Header with category code
        string categoryCode = _mappingCategory == 0 ? "M1" : "M2";
        string categoryName = "VJOY MAPPINGS";
        FUIRenderer.DrawText(canvas, categoryCode, new SKPoint(leftMargin, y + 12), FUIColors.Active, 15f);
        FUIRenderer.DrawText(canvas, categoryName, new SKPoint(leftMargin + 30, y + 12), FUIColors.TextBright, 17f, true);
        y += 30;

        // vJoy device selector: [<] vJoy Device 1 [>]
        float arrowButtonSize = 28f;
        _vjoyPrevButtonBounds = new SKRect(leftMargin, y, leftMargin + arrowButtonSize, y + arrowButtonSize);
        DrawArrowButton(canvas, _vjoyPrevButtonBounds, "<", _vjoyPrevHovered, _ctx.SelectedVJoyDeviceIndex > 0);

        string deviceName = _ctx.VJoyDevices.Count > 0 && _ctx.SelectedVJoyDeviceIndex < _ctx.VJoyDevices.Count
            ? $"vJoy Device {_ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex].Id}"
            : "No vJoy Devices";
        // Center the device name between the two arrow buttons
        var labelBounds = new SKRect(leftMargin + arrowButtonSize, y, rightMargin - arrowButtonSize, y + arrowButtonSize);
        FUIRenderer.DrawTextCentered(canvas, deviceName, labelBounds, FUIColors.TextBright, 15f);

        _vjoyNextButtonBounds = new SKRect(rightMargin - arrowButtonSize, y, rightMargin, y + arrowButtonSize);
        DrawArrowButton(canvas, _vjoyNextButtonBounds, ">", _vjoyNextHovered, _ctx.SelectedVJoyDeviceIndex < _ctx.VJoyDevices.Count - 1);
        y += arrowButtonSize + 6;

        // Scrollable binding rows (filtered by category)
        float listBottom = contentBounds.Bottom - 10;
        DrawBindingsList(canvas, new SKRect(leftMargin - 5, y, rightMargin + 5, listBottom));
    }

    private void DrawMappingCategorySideTabs(SKCanvas canvas, float x, float y, float width, float height)
    {
        // Style matching Device category tabs: narrow vertical tabs with text reading bottom-to-top
        float tabHeight = 80f;
        float tabGap = 4f;

        // Calculate total tabs height and start from bottom of available space
        float totalTabsHeight = tabHeight * 2 + tabGap;
        float startY = y + height - totalTabsHeight - 10f;

        // M1 Buttons tab (bottom)
        var buttonsBounds = new SKRect(x, startY + tabHeight + tabGap, x + width, startY + tabHeight * 2 + tabGap);
        _mappingCategoryButtonsBounds = buttonsBounds;
        DrawVerticalSideTab(canvas, buttonsBounds, "BUTTONS_01", _mappingCategory == 0, _hoveredMappingCategory == 0);

        // M2 Axes tab (above M1)
        var axesBounds = new SKRect(x, startY, x + width, startY + tabHeight);
        _mappingCategoryAxesBounds = axesBounds;
        DrawVerticalSideTab(canvas, axesBounds, "AXES_02", _mappingCategory == 1, _hoveredMappingCategory == 1);
    }

    private void DrawBindingsList(SKCanvas canvas, SKRect bounds)
    {
        _mappingRowBounds.Clear();
        _mappingAddButtonBounds.Clear();
        _mappingRemoveButtonBounds.Clear();
        _bindingsListBounds = bounds;

        var profile = _ctx.ProfileManager.ActiveProfile;

        bool hasVJoy = _ctx.VJoyDevices.Count > 0 && _ctx.SelectedVJoyDeviceIndex < _ctx.VJoyDevices.Count;
        VJoyDeviceInfo? vjoyDevice = hasVJoy ? _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex] : null;

        float rowHeight = 32f;  // Compact rows
        float rowGap = 4f;

        // Get counts based on current category
        string[] axisNames = { "X Axis", "Y Axis", "Z Axis", "RX Axis", "RY Axis", "RZ Axis", "Slider 1", "Slider 2" };
        int axisCount = hasVJoy ? Math.Min(axisNames.Length, 8) : 0;
        int buttonCount = vjoyDevice?.ButtonCount ?? 0;

        // Calculate content height based on selected category (no section headers when filtered)
        // Category 0 = Buttons, Category 1 = Axes
        int itemCount = _mappingCategory == 0 ? buttonCount : axisCount;
        _bindingsContentHeight = itemCount * (rowHeight + rowGap);

        // Clamp scroll offset
        float maxScroll = Math.Max(0, _bindingsContentHeight - bounds.Height);
        _bindingsScrollOffset = Math.Clamp(_bindingsScrollOffset, 0, maxScroll);

        // Set up clipping
        canvas.Save();
        canvas.ClipRect(bounds);

        float y = bounds.Top - _bindingsScrollOffset;
        int rowIndex = 0;

        // Show BUTTONS when category is 0
        if (_mappingCategory == 0 && hasVJoy && buttonCount > 0)
        {
            for (int i = 0; i < buttonCount; i++)
            {
                float rowTop = y;
                float rowBottom = y + rowHeight;

                // Only draw if visible
                if (rowBottom > bounds.Top && rowTop < bounds.Bottom)
                {
                    var rowBounds = new SKRect(bounds.Left, rowTop, bounds.Right, rowBottom);
                    string binding = GetButtonBindingText(profile, vjoyDevice!.Id, i);
                    var keyParts = GetButtonKeyParts(profile, vjoyDevice!.Id, i);
                    bool isSelected = rowIndex == _selectedMappingRow;
                    bool isHovered = rowIndex == _hoveredMappingRow;

                    DrawChunkyBindingRow(canvas, rowBounds, $"Button {i + 1}", binding, isSelected, isHovered, rowIndex, keyParts);
                    _mappingRowBounds.Add(rowBounds);
                }
                else
                {
                    _mappingRowBounds.Add(new SKRect(bounds.Left, rowTop, bounds.Right, rowBottom));
                }

                y += rowHeight + rowGap;
                rowIndex++;
            }
        }

        // Show AXES when category is 1
        if (_mappingCategory == 1 && hasVJoy && axisCount > 0)
        {
            for (int i = 0; i < axisCount; i++)
            {
                float rowTop = y;
                float rowBottom = y + rowHeight;

                // Only draw if visible
                if (rowBottom > bounds.Top && rowTop < bounds.Bottom)
                {
                    var rowBounds = new SKRect(bounds.Left, rowTop, bounds.Right, rowBottom);
                    string binding = GetAxisBindingText(profile, vjoyDevice!.Id, i);
                    bool isSelected = rowIndex == _selectedMappingRow;
                    bool isHovered = rowIndex == _hoveredMappingRow;

                    DrawChunkyBindingRow(canvas, rowBounds, axisNames[i], binding, isSelected, isHovered, rowIndex);
                    _mappingRowBounds.Add(rowBounds);
                }
                else
                {
                    // Add placeholder bounds for hit testing even when not visible
                    _mappingRowBounds.Add(new SKRect(bounds.Left, rowTop, bounds.Right, rowBottom));
                }

                y += rowHeight + rowGap;
                rowIndex++;
            }
        }

        canvas.Restore();

        // Draw scroll indicator if content overflows
        if (_bindingsContentHeight > bounds.Height)
        {
            DrawScrollIndicator(canvas, bounds, _bindingsScrollOffset, _bindingsContentHeight);
        }
    }

    /// <summary>
    /// Get the keyboard key parts for a button mapping (modifiers + key as separate items)
    /// </summary>
    private List<string>? GetKeyboardMappingParts(ButtonMapping mapping)
    {
        var output = mapping.Output;
        if (string.IsNullOrEmpty(output.KeyName)) return null;

        var parts = new List<string>();
        if (output.Modifiers is not null && output.Modifiers.Count > 0)
        {
            parts.AddRange(output.Modifiers);
        }
        parts.Add(output.KeyName);
        return parts;
    }

    /// <summary>
    /// Get the keyboard key parts for a button slot (if it outputs to keyboard)
    /// Returns list of key parts (e.g., ["LCtrl", "LShift", "A"]) for drawing as separate keycaps
    /// </summary>
    private List<string>? GetButtonKeyParts(MappingProfile? profile, uint vjoyId, int buttonIndex)
    {
        if (profile is null) return null;

        // Find mapping for this button slot that has keyboard output
        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyId &&
            m.Output.Index == buttonIndex &&
            !string.IsNullOrEmpty(m.Output.KeyName));

        if (mapping is null) return null;

        return GetKeyboardMappingParts(mapping);
    }

    private void DrawScrollIndicator(SKCanvas canvas, SKRect bounds, float scrollOffset, float contentHeight)
    {
        float trackHeight = bounds.Height - 20;
        float thumbRatio = bounds.Height / contentHeight;
        float thumbHeight = Math.Max(20, trackHeight * thumbRatio);
        float thumbOffset = (scrollOffset / (contentHeight - bounds.Height)) * (trackHeight - thumbHeight);

        // Position track outside the list bounds, aligned with corner frame edge
        float trackX = bounds.Right + 8; // Outside cells, inline with frame
        float trackTop = bounds.Top + 10;
        float trackWidth = 3f;

        // Track (subtle)
        using var trackPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Frame.WithAlpha(40)
        };
        canvas.DrawRoundRect(new SKRect(trackX, trackTop, trackX + trackWidth, trackTop + trackHeight), 1.5f, 1.5f, trackPaint);

        // Thumb
        using var thumbPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Primary.WithAlpha(200)
        };
        canvas.DrawRoundRect(new SKRect(trackX, trackTop + thumbOffset, trackX + trackWidth, trackTop + thumbOffset + thumbHeight), 1.5f, 1.5f, thumbPaint);
    }

    private void DrawChunkyBindingRow(SKCanvas canvas, SKRect bounds, string outputName, string binding,
        bool isSelected, bool isHovered, int rowIndex, List<string>? keyParts = null)
    {
        bool hasBinding = !string.IsNullOrEmpty(binding) && binding != "—";
        bool hasKeyParts = keyParts is not null && keyParts.Count > 0;

        // Check for attention highlight (physical input was pressed that maps to this output)
        bool hasAttentionHighlight = false;
        float attentionIntensity = 0f;
        if (_highlightedMappingRow >= 0 &&
            _ctx.VJoyDevices.Count > 0 && _ctx.SelectedVJoyDeviceIndex < _ctx.VJoyDevices.Count)
        {
            var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
            // Parse output index from the outputName (e.g., "Button 5" -> 4, "Axis 0" -> 0)
            int outputIndex = -1;
            if (outputName.StartsWith("Button ") && int.TryParse(outputName.Substring(7), out int btnNum))
                outputIndex = btnNum - 1; // Buttons are 1-indexed in display
            else if (outputName.StartsWith("Axis ") && int.TryParse(outputName.Substring(5), out int axisNum))
                outputIndex = axisNum;

            if (outputIndex == _highlightedMappingRow && vjoyDevice.Id == _highlightedVJoyDevice)
            {
                var elapsed = (DateTime.Now - _highlightStartTime).TotalMilliseconds;
                if (elapsed < HighlightDurationMs)
                {
                    hasAttentionHighlight = true;
                    // Ease-out fade: starts bright and fades slowly, then accelerates fade at end
                    // Using cubic ease-in for the FADE (so highlight fades slowly at first, faster at end)
                    float t = (float)(elapsed / HighlightDurationMs); // 0 to 1
                    float easeIn = t * t * t; // Cubic ease-in: 0 to 1, starts slow, ends fast
                    attentionIntensity = 1f - easeIn; // 1 to 0, fades slowly at first, faster at end
                }
                else
                {
                    _highlightedMappingRow = -1; // Clear expired highlight
                }
            }
        }

        // Background - selection state is independent of attention highlight
        SKColor bgColor;
        if (isSelected)
            bgColor = FUIColors.Active.WithAlpha(50);
        else if (isHovered)
            bgColor = FUIColors.Primary.WithAlpha(35);
        else
            bgColor = FUIColors.Background2.WithAlpha(100);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRoundRect(bounds, 4, 4, bgPaint);

        // Draw attention highlight as overlay (additive, doesn't replace selection)
        if (hasAttentionHighlight)
        {
            // Pulsing glow effect that fades out - use theme active color
            byte glowAlpha = (byte)(100 * attentionIntensity);
            using var glowPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = FUIColors.Active.WithAlpha(glowAlpha)
            };
            canvas.DrawRoundRect(bounds, 4, 4, glowPaint);
        }

        // Frame
        SKColor frameColor;
        float frameWidth;
        if (hasAttentionHighlight)
        {
            // Attention frame pulses with the highlight - use theme active color
            frameColor = FUIColors.Active.WithAlpha((byte)(200 * attentionIntensity + 55));
            frameWidth = 2f + attentionIntensity; // Slightly thicker when fresh
        }
        else if (isSelected)
        {
            frameColor = FUIColors.Active;
            frameWidth = 2f;
        }
        else
        {
            frameColor = isHovered ? FUIColors.FrameBright : FUIColors.Frame.WithAlpha(100);
            frameWidth = 1f;
        }

        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = frameColor,
            StrokeWidth = frameWidth
        };
        canvas.DrawRoundRect(bounds, 4, 4, framePaint);

        // Output name (centered vertically)
        float leftTextX = bounds.Left + 12;
        FUIRenderer.DrawText(canvas, outputName, new SKPoint(leftTextX, bounds.MidY + 5),
            isSelected ? FUIColors.Active : FUIColors.TextPrimary, 15f, true);

        // Right side indicator: keyboard keycaps or binding dot
        if (hasKeyParts)
        {
            // Draw keycaps right-aligned within available space
            float keycapHeight = 16f;
            float keycapGap = 2f;
            float keycapPadding = 6f;  // Padding inside each keycap (left + right)
            float fontSize = 11f;  // Slightly smaller font for compact display
            float scaledFontSize = fontSize;
            float keycapRight = bounds.Right - 8;
            float keycapTop = bounds.MidY - keycapHeight / 2;

            // Use same font settings as DrawTextCentered for accurate measurement
            using var measurePaint = new SKPaint
            {
                TextSize = scaledFontSize,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal)
            };

            // Draw keycaps from right to left (key rightmost, then modifiers)
            for (int i = keyParts!.Count - 1; i >= 0; i--)
            {
                string keyText = keyParts[i].ToUpperInvariant();
                float textWidth = measurePaint.MeasureText(keyText);
                float keycapWidth = textWidth + keycapPadding * 2;
                float keycapLeft = keycapRight - keycapWidth;

                var keycapBounds = new SKRect(keycapLeft, keycapTop, keycapRight, keycapTop + keycapHeight);

                // Keycap background
                using var keycapBgPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = FUIColors.TextPrimary.WithAlpha(20),
                    IsAntialias = true
                };
                canvas.DrawRoundRect(keycapBounds, 3, 3, keycapBgPaint);

                // Keycap frame
                using var keycapFramePaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = FUIColors.TextPrimary.WithAlpha(100),
                    StrokeWidth = 1f,
                    IsAntialias = true
                };
                canvas.DrawRoundRect(keycapBounds, 3, 3, keycapFramePaint);

                // Keycap text - draw manually centered to ensure padding is respected
                float textX = keycapLeft + keycapPadding;
                float textY = keycapBounds.MidY + scaledFontSize / 3;
                using var textPaint = new SKPaint
                {
                    Color = FUIColors.TextPrimary,
                    TextSize = scaledFontSize,
                    IsAntialias = true,
                    Typeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal)
                };
                canvas.DrawText(keyText, textX, textY, textPaint);

                // Move left for next keycap
                keycapRight = keycapLeft - keycapGap;
            }
        }
        else if (hasBinding)
        {
            // Binding indicator dot on the right
            float dotX = bounds.Right - 20;
            float dotY = bounds.MidY;
            using var dotPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = FUIColors.Active,
                IsAntialias = true
            };
            canvas.DrawCircle(dotX, dotY, 5f, dotPaint);
        }
    }

    private void DrawDeviceVisualizationPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(100),
            IsAntialias = true
        };
        canvas.DrawRect(new SKRect(bounds.Left + frameInset, bounds.Top + frameInset,
            bounds.Right - frameInset, bounds.Bottom - frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame.WithAlpha(150), 30f, 8f);

        // Show device silhouette - use primary device's map if available
        float centerX = bounds.MidX;

        // Get the appropriate SVG based on primary device map
        var svg = _ctx.GetSvgForDeviceMap?.Invoke(_ctx.MappingsPrimaryDeviceMap) ?? _ctx.JoystickSvg;
        bool shouldMirror = _ctx.MappingsPrimaryDeviceMap?.Mirror ?? false;

        // Device name label at top of panel
        float labelRowHeight = 20f;
        float labelY = bounds.Top + frameInset + labelRowHeight;
        string deviceLabel = _ctx.MappingsPrimaryDeviceMap?.Device ?? "—";
        FUIRenderer.DrawTextCentered(canvas, deviceLabel,
            new SKRect(bounds.Left, bounds.Top + frameInset, bounds.Right, labelY),
            FUIColors.TextDim, 13f);

        // Reserve space at the bottom for the auto-scroll checkbox row
        float checkboxRowHeight = 26f;
        float checkboxAreaTop = bounds.Bottom - frameInset - checkboxRowHeight;

        if (svg?.Picture is not null)
        {
            // Limit size to 900px max and apply same rendering as device tab
            float maxSize = 900f;
            float maxWidth = Math.Min(bounds.Width - 40, maxSize);
            float maxHeight = Math.Min(bounds.Height - 40 - checkboxRowHeight - labelRowHeight, maxSize);

            // Create constrained bounds centered in the available area (below label, above checkbox row)
            float constrainedWidth = Math.Min(maxWidth, maxHeight); // Keep square-ish
            float constrainedHeight = constrainedWidth;
            float availableCenterY = labelY + (checkboxAreaTop - labelY) / 2f;
            var constrainedBounds = new SKRect(
                centerX - constrainedWidth / 2,
                availableCenterY - constrainedHeight / 2,
                centerX + constrainedWidth / 2,
                availableCenterY + constrainedHeight / 2
            );

            _ctx.SilhouetteBounds = constrainedBounds;
            DrawSvgInBounds(canvas, svg, constrainedBounds, shouldMirror);
            DrawMappingHighlightLeadLine(canvas, constrainedBounds);
        }
        else
        {
            _ctx.SilhouetteBounds = SKRect.Empty;
            FUIRenderer.DrawTextCentered(canvas, "Device Preview",
                new SKRect(bounds.Left, labelY, bounds.Right, checkboxAreaTop),
                FUIColors.TextDim, 14f);
        }

        // Auto-scroll checkbox at bottom of panel
        float leftMargin = bounds.Left + frameInset + 12;
        float checkboxSize = 12f;
        float checkboxY = checkboxAreaTop + (checkboxRowHeight - checkboxSize) / 2f;
        _autoScrollCheckboxBounds = new SKRect(leftMargin, checkboxY, leftMargin + checkboxSize, checkboxY + checkboxSize);
        DrawCheckbox(canvas, _autoScrollCheckboxBounds, _autoScrollEnabled);

        var labelColor = _autoScrollCheckboxHovered ? FUIColors.TextBright : FUIColors.TextDim;
        FUIRenderer.DrawText(canvas, "AUTO-SCROLL TO MAPPING",
            new SKPoint(leftMargin + checkboxSize + 7, checkboxY + checkboxSize - 1),
            labelColor, 13f);

        // "No mapping" flash indicator — centered above the checkbox row, fades out
        if (_noMappingFlashText is not null)
        {
            float elapsed = (float)(DateTime.Now - _noMappingFlashTime).TotalSeconds;
            float opacity = elapsed < 1f ? 1f : Math.Max(0f, 1f - (elapsed - 1f) / 1.5f);
            if (opacity > 0.01f)
            {
                var noMapColor = FUIColors.Warning.WithAlpha((byte)(opacity * 220));
                FUIRenderer.DrawTextCentered(canvas, _noMappingFlashText,
                    new SKRect(bounds.Left, checkboxAreaTop - 22, bounds.Right, checkboxAreaTop),
                    noMapColor, 13f);
            }
            else
            {
                _noMappingFlashText = null;
            }
        }
    }

    private void DrawMappingSettingsPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(140),
            IsAntialias = true
        };
        canvas.DrawRect(new SKRect(bounds.Left + frameInset, bounds.Top + frameInset,
            bounds.Right - frameInset, bounds.Bottom - frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Frame, 30f, 8f);

        float y = bounds.Top + frameInset + 10;
        float leftMargin = bounds.Left + frameInset + 16;
        float rightMargin = bounds.Right - frameInset - 16;

        // Title
        FUIRenderer.DrawText(canvas, "MAPPING SETTINGS", new SKPoint(leftMargin, y + 12), FUIColors.TextBright, 17f, true);
        y += 36;

        // Show settings for selected row
        if (_selectedMappingRow < 0)
        {
            FUIRenderer.DrawText(canvas, "Select an output to configure",
                new SKPoint(leftMargin, y + 32), FUIColors.TextDim, 15f);
            return;
        }

        // Determine if axis or button based on current category
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        string outputName = GetSelectedOutputName();

        FUIRenderer.DrawText(canvas, outputName, new SKPoint(leftMargin, y + 16), FUIColors.Active, 16f, true);
        y += 36;

        // INPUT SOURCES section - shows mapped inputs with add/remove
        y = DrawInputSourcesSection(canvas, leftMargin, rightMargin, y);

        float bottomMargin = bounds.Bottom - frameInset - 10;

        if (isAxis)
        {
            DrawAxisSettings(canvas, leftMargin, rightMargin, y, bottomMargin);
        }
        else
        {
            DrawButtonSettings(canvas, leftMargin, rightMargin, y, bottomMargin);
        }
    }

    private float DrawInputSourcesSection(SKCanvas canvas, float leftMargin, float rightMargin, float y)
    {
        _inputSourceRemoveBounds.Clear();

        FUIRenderer.DrawText(canvas, "INPUT SOURCES", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        y += 18;

        // Get current mappings for selected output
        var inputs = GetInputsForSelectedOutput();
        bool isListening = _isListeningForInput;

        float rowHeight = 40f;  // Two-line layout
        float rowGap = 4f;

        if (inputs.Count == 0 && !isListening)
        {
            // No inputs - show "None" with dashed border
            var emptyBounds = new SKRect(leftMargin, y, rightMargin, y + 28);
            using var emptyBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1.WithAlpha(100) };
            canvas.DrawRoundRect(emptyBounds, 3, 3, emptyBgPaint);

            using var emptyFramePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.FrameDim,
                StrokeWidth = 1f,
                PathEffect = SKPathEffect.CreateDash(new float[] { 4, 2 }, 0)
            };
            canvas.DrawRoundRect(emptyBounds, 3, 3, emptyFramePaint);

            FUIRenderer.DrawText(canvas, "No input mapped", new SKPoint(leftMargin + 10, emptyBounds.MidY + 4), FUIColors.TextDisabled, 14f);
            y += 28 + rowGap;
        }
        else
        {
            // Draw each input source row (two-line layout)
            for (int i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                var rowBounds = new SKRect(leftMargin, y, rightMargin - 30, y + rowHeight);

                // Row background
                using var rowBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1 };
                canvas.DrawRoundRect(rowBounds, 3, 3, rowBgPaint);

                using var rowFramePaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = FUIColors.Frame,
                    StrokeWidth = 1f
                };
                canvas.DrawRoundRect(rowBounds, 3, 3, rowFramePaint);

                // Line 1: Input type and index (e.g., "Button 5") - vertically centered in top half
                string inputTypeText = input.Type == InputType.Button
                    ? $"Button {input.Index + 1}"
                    : $"{input.Type} {input.Index}";
                FUIRenderer.DrawText(canvas, inputTypeText, new SKPoint(leftMargin + 8, y + 16), FUIColors.TextPrimary, 14f);

                // Line 2: Device name (smaller, dimmer) - vertically centered in bottom half
                FUIRenderer.DrawText(canvas, input.DeviceName, new SKPoint(leftMargin + 8, y + 32), FUIColors.TextDim, 12f);

                // Remove [×] button (full height of row)
                var removeBounds = new SKRect(rightMargin - 26, y, rightMargin, y + rowHeight);
                bool removeHovered = _hoveredInputSourceRemove == i;

                using var removeBgPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = removeHovered ? FUIColors.Warning.WithAlpha(40) : FUIColors.Background2
                };
                canvas.DrawRoundRect(removeBounds, 3, 3, removeBgPaint);

                using var removeFramePaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = removeHovered ? FUIColors.Warning : FUIColors.Frame,
                    StrokeWidth = 1f
                };
                canvas.DrawRoundRect(removeBounds, 3, 3, removeFramePaint);

                FUIRenderer.DrawTextCentered(canvas, "×", removeBounds,
                    removeHovered ? FUIColors.Warning : FUIColors.TextDim, 14f);

                _inputSourceRemoveBounds.Add(removeBounds);
                y += rowHeight + rowGap;
            }
        }

        // Listening indicator with timeout bar
        if (isListening)
        {
            // Check for timeout
            var elapsed = (DateTime.Now - _inputListeningStartTime).TotalMilliseconds;
            if (elapsed >= InputListeningTimeoutMs)
            {
                CancelInputListening(); // Timeout - cancel listening
            }
            else
            {
                var listenBounds = new SKRect(leftMargin, y, rightMargin, y + rowHeight);
                byte alpha = (byte)(180 + MathF.Sin(_ctx.PulsePhase * 3) * 60);

                using var listenBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(40) };
                canvas.DrawRoundRect(listenBounds, 3, 3, listenBgPaint);

                // Draw timeout progress bar
                float progress = Math.Min(1f, (float)(elapsed / InputListeningTimeoutMs));
                float remaining = 1f - progress;
                float progressWidth = (listenBounds.Width - 6) * remaining;
                if (progressWidth > 0)
                {
                    var progressRect = new SKRect(
                        listenBounds.Left + 3,
                        listenBounds.Top + 3,
                        listenBounds.Left + 3 + progressWidth,
                        listenBounds.Bottom - 3);
                    using var progressPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Fill,
                        Color = FUIColors.Active.WithAlpha(80)
                    };
                    canvas.DrawRoundRect(progressRect, 2, 2, progressPaint);
                }

                using var listenFramePaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = FUIColors.Active.WithAlpha(alpha),
                    StrokeWidth = 2f
                };
                canvas.DrawRoundRect(listenBounds, 3, 3, listenFramePaint);

                FUIRenderer.DrawText(canvas, "Press input...", new SKPoint(leftMargin + 10, y + 18),
                    FUIColors.Active.WithAlpha(alpha), 14f);
                y += rowHeight + rowGap;
            }
        }

        // Add input button [+]
        var addBounds = new SKRect(leftMargin, y, rightMargin, y + 28);
        _addInputButtonBounds = addBounds;
        bool addHovered = _addInputButtonHovered;

        using var addBgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = addHovered ? FUIColors.Active.WithAlpha(40) : FUIColors.Background2
        };
        canvas.DrawRoundRect(addBounds, 3, 3, addBgPaint);

        using var addFramePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = addHovered ? FUIColors.Active : FUIColors.Frame,
            StrokeWidth = addHovered ? 2f : 1f,
            PathEffect = isListening ? null : SKPathEffect.CreateDash(new float[] { 4, 2 }, 0)
        };
        canvas.DrawRoundRect(addBounds, 3, 3, addFramePaint);

        string addText = isListening ? "Cancel" : "+ Add Input";
        FUIRenderer.DrawTextCentered(canvas, addText, addBounds,
            addHovered ? FUIColors.Active : FUIColors.TextPrimary, 14f);
        y += 28 + 8;  // Button height + small gap

        // Merge operation selector (only for axes with 2+ inputs)
        bool isAxis = _mappingCategory == 1;
        if (isAxis && inputs.Count >= 2)
        {
            y = DrawMergeOperationSelector(canvas, leftMargin, rightMargin, y);
        }
        else
        {
            // Clear merge button bounds when not shown
            for (int i = 0; i < _mergeOpButtonBounds.Length; i++)
                _mergeOpButtonBounds[i] = SKRect.Empty;
            y += 8;  // Extra spacing when no merge selector
        }

        return y;
    }

    private float DrawMergeOperationSelector(SKCanvas canvas, float leftMargin, float rightMargin, float y)
    {
        var axisMapping = GetCurrentAxisMapping();
        if (axisMapping is null) return y;

        // Section header with top margin
        y += 12;  // Space before section
        FUIRenderer.DrawText(canvas, "MERGE MODE", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        y += 16;

        // Four merge operation buttons in a row
        string[] labels = { "AVG", "MAX", "MIN", "SUM" };
        MergeOperation[] ops = { MergeOperation.Average, MergeOperation.Maximum, MergeOperation.Minimum, MergeOperation.Sum };

        float width = rightMargin - leftMargin;
        float buttonWidth = (width - 12) / 4; // 3 gaps of 4px each
        float buttonHeight = 28f;  // 4px aligned, meets minimum touch target

        for (int i = 0; i < 4; i++)
        {
            var btnBounds = new SKRect(
                leftMargin + i * (buttonWidth + 4), y,
                leftMargin + i * (buttonWidth + 4) + buttonWidth, y + buttonHeight);
            _mergeOpButtonBounds[i] = btnBounds;

            bool isActive = axisMapping.MergeOp == ops[i];
            bool isHovered = _hoveredMergeOpButton == i;

            var bgColor = isActive ? FUIColors.Active.WithAlpha(60) : (isHovered ? FUIColors.Primary.WithAlpha(40) : FUIColors.Background2);
            var frameColor = isActive ? FUIColors.Active : (isHovered ? FUIColors.FrameBright : FUIColors.Frame);
            var textColor = isActive ? FUIColors.TextBright : (isHovered ? FUIColors.TextPrimary : FUIColors.TextDim);

            using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
            canvas.DrawRoundRect(btnBounds, 3, 3, bgPaint);

            using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = frameColor, StrokeWidth = isActive ? 2f : 1f };
            canvas.DrawRoundRect(btnBounds, 3, 3, framePaint);

            FUIRenderer.DrawTextCentered(canvas, labels[i], btnBounds, textColor, 13f);
        }

        y += buttonHeight + 16;  // Move well below buttons (4px aligned)

        // Description of current merge mode
        string description = axisMapping.MergeOp switch
        {
            MergeOperation.Average => "Averages all input values",
            MergeOperation.Maximum => "Uses highest input value",
            MergeOperation.Minimum => "Uses lowest input value",
            MergeOperation.Sum => "Adds values (clamped -1 to 1)",
            _ => ""
        };
        FUIRenderer.DrawText(canvas, description, new SKPoint(leftMargin, y), FUIColors.TextDisabled, 12f);
        y += 16;  // Space after description before next section

        return y;
    }

    private List<InputSource> GetInputsForSelectedOutput()
    {
        var inputs = new List<InputSource>();
        if (_selectedMappingRow < 0) return inputs;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return inputs;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return inputs;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        int outputIndex = _selectedMappingRow;

        if (isAxis)
        {
            var mapping = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);
            if (mapping is not null)
                inputs.AddRange(mapping.Inputs);
        }
        else
        {
            // For button rows, find mapping for this vJoy button slot
            // Check both VJoyButton and Keyboard output types (both map to button slots)
            var mapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);
            if (mapping is not null)
                inputs.AddRange(mapping.Inputs);
        }

        return inputs;
    }

    private string GetSelectedOutputName()
    {
        if (_selectedMappingRow < 0) return "";

        // Category 0 = Buttons, Category 1 = Axes
        if (_mappingCategory == 1)
        {
            // Axes
            string[] axisNames = { "X Axis", "Y Axis", "Z Axis", "RX Axis", "RY Axis", "RZ Axis", "Slider 1", "Slider 2" };
            return _selectedMappingRow < axisNames.Length ? axisNames[_selectedMappingRow] : $"Axis {_selectedMappingRow}";
        }
        else
        {
            // Buttons
            return $"Button {_selectedMappingRow + 1}";
        }
    }

    /// <summary>
    /// Gets the current axis mapping for the selected output, or null if not an axis or not found.
    /// </summary>
    private AxisMapping? GetCurrentAxisMapping()
    {
        if (_selectedMappingRow < 0 || _mappingCategory != 1) return null;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return null;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return null;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        return profile.AxisMappings.FirstOrDefault(m =>
            m.Output.Type == OutputType.VJoyAxis &&
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);
    }

    private void DrawAxisSettings(SKCanvas canvas, float leftMargin, float rightMargin, float y, float bottom)
    {
        float width = rightMargin - leftMargin;

        // Response Curve header (with top margin for section separation)
        y += 8;  // Section separation
        FUIRenderer.DrawText(canvas, "RESPONSE CURVE", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        y += 16f;

        // Symmetrical, Centre, and Invert checkboxes on their own row
        // Symmetrical on left, Centre and Invert on right
        float checkboxSize = 12f;
        float rowHeight = 16f;
        float checkboxY = y + (rowHeight - checkboxSize) / 2; // Center checkbox in row
        float fontSize = 12f;
        float scaledFontSize = fontSize;
        float textY = y + (rowHeight / 2) + (scaledFontSize / 3); // Center text baseline

        // Measure label widths for positioning
        using var labelPaint = FUIRenderer.CreateTextPaint(FUIColors.TextDim, scaledFontSize);
        float invertLabelWidth = labelPaint.MeasureText("Invert");
        float centreLabelWidth = labelPaint.MeasureText("Centre");
        float symmetricalLabelWidth = labelPaint.MeasureText("Symmetrical");
        float labelGap = 4f;
        float checkboxGap = 12f;

        // Symmetrical checkbox (leftmost) - checkbox then label
        _curveSymmetricalCheckboxBounds = new SKRect(leftMargin, checkboxY, leftMargin + checkboxSize, checkboxY + checkboxSize);
        DrawCheckbox(canvas, _curveSymmetricalCheckboxBounds, _curveSymmetrical);
        FUIRenderer.DrawText(canvas, "Symmetrical", new SKPoint(leftMargin + checkboxSize + labelGap, textY), FUIColors.TextDim, fontSize);

        // Invert checkbox (rightmost) - label then checkbox
        float invertCheckX = rightMargin - checkboxSize;
        _invertToggleBounds = new SKRect(invertCheckX, checkboxY, invertCheckX + checkboxSize, checkboxY + checkboxSize);
        DrawCheckbox(canvas, _invertToggleBounds, _axisInverted);
        FUIRenderer.DrawText(canvas, "Invert", new SKPoint(invertCheckX - invertLabelWidth - labelGap, textY), FUIColors.TextDim, fontSize);

        // Centre checkbox (left of Invert) - label then checkbox
        float centreCheckX = invertCheckX - invertLabelWidth - labelGap - checkboxGap - checkboxSize;
        _deadzoneCenterCheckboxBounds = new SKRect(centreCheckX, checkboxY, centreCheckX + checkboxSize, checkboxY + checkboxSize);
        DrawCheckbox(canvas, _deadzoneCenterCheckboxBounds, _deadzoneCenterEnabled);
        FUIRenderer.DrawText(canvas, "Centre", new SKPoint(centreCheckX - centreLabelWidth - labelGap, textY), FUIColors.TextDim, fontSize);

        y += rowHeight + 6f;

        // Curve preset buttons - store bounds for click handling
        string[] presets = { "LINEAR", "S-CURVE", "EXPO", "CUSTOM" };
        float buttonWidth = (width - 12) / presets.Length; // 3 gaps of 4px each
        float buttonHeight = 24f;  // 4px aligned minimum

        for (int i = 0; i < presets.Length; i++)
        {
            var presetBounds = new SKRect(
                leftMargin + i * (buttonWidth + 4), y,
                leftMargin + i * (buttonWidth + 4) + buttonWidth, y + buttonHeight);

            // Store bounds for click detection
            _curvePresetBounds[i] = presetBounds;

            CurveType presetType = i switch
            {
                0 => CurveType.Linear,
                1 => CurveType.SCurve,
                2 => CurveType.Exponential,
                _ => CurveType.Custom
            };

            bool isActive = _selectedCurveType == presetType;
            bool isHovered = presetBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

            var bgColor = isActive
                ? FUIColors.Active.WithAlpha(60)
                : (isHovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background2);
            var frameColor = isActive
                ? FUIColors.Active
                : (isHovered ? FUIColors.FrameBright : FUIColors.Frame);
            var textColor = isActive ? FUIColors.TextBright : FUIColors.TextDim;

            using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
            canvas.DrawRect(presetBounds, bgPaint);

            using var framePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = frameColor, StrokeWidth = 1f };
            canvas.DrawRect(presetBounds, framePaint);

            FUIRenderer.DrawTextCentered(canvas, presets[i], presetBounds, textColor, 12f);
        }
        y += buttonHeight + 6f;

        // Curve editor visualization
        float curveHeight = 140f;
        _curveEditorBounds = new SKRect(leftMargin, y, rightMargin, y + curveHeight);
        DrawCurveVisualization(canvas, _curveEditorBounds);
        y += curveHeight + 6f;

        // Live axis movement indicator
        var axisMapping = GetCurrentAxisMapping();
        if (axisMapping is not null)
        {
            float indicatorHeight = DrawAxisMovementIndicator(canvas, leftMargin, rightMargin, y, axisMapping);
            y += indicatorHeight + 6f;
        }
        y += 4f;

        // Deadzone section
        if (y + 100 < bottom)
        {
            // Header row: "DEADZONE" label + preset buttons + selected handle indicator
            FUIRenderer.DrawText(canvas, "DEADZONE", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);

            // Preset buttons - always visible, apply to selected handle
            string[] presetLabels = { "0%", "2%", "5%", "10%" };
            float presetBtnWidth = 32f;
            float presetStartX = rightMargin - (presetBtnWidth * 4 + 9);

            for (int col = 0; col < 4; col++)
            {
                var btnBounds = new SKRect(
                    presetStartX + col * (presetBtnWidth + 3), y - 2,
                    presetStartX + col * (presetBtnWidth + 3) + presetBtnWidth, y + 14);
                _deadzonePresetBounds[col] = btnBounds;

                // Dim buttons if no handle selected
                bool enabled = _selectedDeadzoneHandle >= 0;
                bool isHovered = enabled && btnBounds.Contains(_ctx.MousePosition.X, _ctx.MousePosition.Y);

                var bgColor = enabled
                    ? (isHovered ? FUIColors.Background2.WithAlpha(200) : FUIColors.Background2)
                    : FUIColors.Background2;
                var frameColor = enabled
                    ? (isHovered ? FUIColors.FrameBright : FUIColors.Frame)
                    : FUIColors.Frame.WithAlpha(100);

                using var btnBg = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
                canvas.DrawRect(btnBounds, btnBg);
                using var btnFrame = new SKPaint { Style = SKPaintStyle.Stroke, Color = frameColor, StrokeWidth = 1f };
                canvas.DrawRect(btnBounds, btnFrame);
                FUIRenderer.DrawTextCentered(canvas, presetLabels[col], btnBounds, enabled ? FUIColors.TextDim : FUIColors.TextDim.WithAlpha(100), 12f);
            }

            // Show which handle is selected (if any)
            if (_selectedDeadzoneHandle >= 0)
            {
                string[] handleNames = { "Start", "Ctr-", "Ctr+", "End" };
                string selectedName = handleNames[_selectedDeadzoneHandle];
                FUIRenderer.DrawText(canvas, $"[{selectedName}]", new SKPoint(presetStartX - 45, y), FUIColors.Active, 12f);
            }
            y += 20f;

            // Dual deadzone slider (always shows min/max, optionally shows center handles)
            float sliderHeight = 24f;
            _deadzoneSliderBounds = new SKRect(leftMargin, y, rightMargin, y + sliderHeight);
            DrawDualDeadzoneSlider(canvas, _deadzoneSliderBounds);
            y += sliderHeight + 6f;

            // Value labels - fixed positions at track edges (prevents collision)
            if (_deadzoneCenterEnabled)
            {
                // Two-track layout - fixed positions at each track edge
                float gap = 24f;
                float centerX = _deadzoneSliderBounds.MidX;
                float leftTrackRight = centerX - gap / 2;
                float rightTrackLeft = centerX + gap / 2;

                // Min at left edge, CtrMin at right edge of left track
                // CtrMax at left edge of right track, Max at right edge
                FUIRenderer.DrawText(canvas, $"{_deadzoneMin:F2}", new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, $"{_deadzoneCenterMin:F2}", new SKPoint(leftTrackRight - 24, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, $"{_deadzoneCenterMax:F2}", new SKPoint(rightTrackLeft, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, $"{_deadzoneMax:F2}", new SKPoint(rightMargin - 20, y), FUIColors.TextDim, 12f);
            }
            else
            {
                // Single track - just show start and end at edges
                FUIRenderer.DrawText(canvas, $"{_deadzoneMin:F2}", new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, $"{_deadzoneMax:F2}", new SKPoint(rightMargin - 20, y), FUIColors.TextDim, 12f);
            }
        }
    }

    private void DrawDualDeadzoneSlider(SKCanvas canvas, SKRect bounds)
    {
        // Convert -1..1 values to 0..1 for display
        float minPos = (_deadzoneMin + 1f) / 2f;
        float centerMinPos = (_deadzoneCenterMin + 1f) / 2f;
        float centerMaxPos = (_deadzoneCenterMax + 1f) / 2f;
        float maxPos = (_deadzoneMax + 1f) / 2f;

        float handleRadius = 8f;
        float trackHeight = 8f;
        float trackY = bounds.MidY - trackHeight / 2;

        using var trackBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background2 };
        using var trackFramePaint = new SKPaint { Style = SKPaintStyle.Stroke, Color = FUIColors.Frame, StrokeWidth = 1f };
        using var activePaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(150) };

        if (_deadzoneCenterEnabled)
        {
            // Two physically separate tracks like JoystickGremlinEx
            // Gap must be > 2 * handleRadius so handles never overlap when both at center
            float gap = 24f;
            float centerX = bounds.MidX;

            // Left track: from bounds.Left to centerX - gap/2
            var leftTrack = new SKRect(bounds.Left, trackY, centerX - gap / 2, trackY + trackHeight);
            canvas.DrawRoundRect(leftTrack, 4, 4, trackBgPaint);
            canvas.DrawRoundRect(leftTrack, 4, 4, trackFramePaint);

            // Right track: from centerX + gap/2 to bounds.Right
            var rightTrack = new SKRect(centerX + gap / 2, trackY, bounds.Right, trackY + trackHeight);
            canvas.DrawRoundRect(rightTrack, 4, 4, trackBgPaint);
            canvas.DrawRoundRect(rightTrack, 4, 4, trackFramePaint);

            // Active fill on left track (from min handle to center-min handle)
            float leftTrackWidth = leftTrack.Width;
            float minPosInLeft = (minPos - 0f) / 0.5f; // Map 0..0.5 to 0..1 for left track
            float ctrMinPosInLeft = (centerMinPos - 0f) / 0.5f;
            minPosInLeft = Math.Clamp(minPosInLeft, 0f, 1f);
            ctrMinPosInLeft = Math.Clamp(ctrMinPosInLeft, 0f, 1f);

            float leftFillStart = leftTrack.Left + minPosInLeft * leftTrackWidth;
            float leftFillEnd = leftTrack.Left + ctrMinPosInLeft * leftTrackWidth;
            if (leftFillEnd > leftFillStart + 1)
            {
                var leftFill = new SKRect(leftFillStart, trackY + 1, leftFillEnd, trackY + trackHeight - 1);
                canvas.DrawRoundRect(leftFill, 3, 3, activePaint);
            }

            // Active fill on right track (from center-max handle to max handle)
            float rightTrackWidth = rightTrack.Width;
            float ctrMaxPosInRight = (centerMaxPos - 0.5f) / 0.5f; // Map 0.5..1 to 0..1 for right track
            float maxPosInRight = (maxPos - 0.5f) / 0.5f;
            ctrMaxPosInRight = Math.Clamp(ctrMaxPosInRight, 0f, 1f);
            maxPosInRight = Math.Clamp(maxPosInRight, 0f, 1f);

            float rightFillStart = rightTrack.Left + ctrMaxPosInRight * rightTrackWidth;
            float rightFillEnd = rightTrack.Left + maxPosInRight * rightTrackWidth;
            if (rightFillEnd > rightFillStart + 1)
            {
                var rightFill = new SKRect(rightFillStart, trackY + 1, rightFillEnd, trackY + trackHeight - 1);
                canvas.DrawRoundRect(rightFill, 3, 3, activePaint);
            }

            // Draw handles - all same size
            // Min handle on left edge of left track
            float minHandleX = leftTrack.Left + minPosInLeft * leftTrackWidth;
            DrawDeadzoneHandle(canvas, bounds.MidY, minHandleX, 0, FUIColors.Active, handleRadius);

            // CtrMin handle on right edge of left track
            float ctrMinHandleX = leftTrack.Left + ctrMinPosInLeft * leftTrackWidth;
            DrawDeadzoneHandle(canvas, bounds.MidY, ctrMinHandleX, 1, FUIColors.Active, handleRadius);

            // CtrMax handle on left edge of right track
            float ctrMaxHandleX = rightTrack.Left + ctrMaxPosInRight * rightTrackWidth;
            DrawDeadzoneHandle(canvas, bounds.MidY, ctrMaxHandleX, 2, FUIColors.Active, handleRadius);

            // Max handle on right edge of right track
            float maxHandleX = rightTrack.Left + maxPosInRight * rightTrackWidth;
            DrawDeadzoneHandle(canvas, bounds.MidY, maxHandleX, 3, FUIColors.Active, handleRadius);
        }
        else
        {
            // Single track spanning full width
            var track = new SKRect(bounds.Left, trackY, bounds.Right, trackY + trackHeight);
            canvas.DrawRoundRect(track, 4, 4, trackBgPaint);
            canvas.DrawRoundRect(track, 4, 4, trackFramePaint);

            // Active fill from min to max
            float fillStart = bounds.Left + minPos * bounds.Width;
            float fillEnd = bounds.Left + maxPos * bounds.Width;
            if (fillEnd > fillStart + 1)
            {
                var fill = new SKRect(fillStart, trackY + 1, fillEnd, trackY + trackHeight - 1);
                canvas.DrawRoundRect(fill, 3, 3, activePaint);
            }

            // Draw handles - same size
            float minHandleX = bounds.Left + minPos * bounds.Width;
            float maxHandleX = bounds.Left + maxPos * bounds.Width;
            DrawDeadzoneHandle(canvas, bounds.MidY, minHandleX, 0, FUIColors.Active, handleRadius);
            DrawDeadzoneHandle(canvas, bounds.MidY, maxHandleX, 3, FUIColors.Active, handleRadius);
        }
    }

    private void DrawDeadzoneHandle(SKCanvas canvas, float centerY, float x, int handleIndex, SKColor color, float radius)
    {
        bool isDragging = _draggingDeadzoneHandle == handleIndex;
        bool isSelected = _selectedDeadzoneHandle == handleIndex;
        float drawRadius = isDragging ? radius + 2f : radius;

        // Selected handles get a highlighted fill
        SKColor fillColor = isDragging ? color : (isSelected ? color.WithAlpha(200) : FUIColors.TextPrimary);

        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = fillColor,
            IsAntialias = true
        };
        canvas.DrawCircle(x, centerY, drawRadius, fillPaint);

        using var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = color,
            StrokeWidth = isSelected ? 2.5f : 1.5f,
            IsAntialias = true
        };
        canvas.DrawCircle(x, centerY, drawRadius, strokePaint);
    }

    private void DrawCheckbox(SKCanvas canvas, SKRect bounds, bool isChecked)
        => FUIWidgets.DrawCheckbox(canvas, bounds, isChecked, _ctx.MousePosition);

    private void DrawInteractiveSlider(SKCanvas canvas, SKRect bounds, float value, SKColor color, bool dragging)
        => FUIWidgets.DrawInteractiveSlider(canvas, bounds, value, color, dragging);

    private void DrawDurationSlider(SKCanvas canvas, SKRect bounds, float value, bool dragging)
        => FUIWidgets.DrawDurationSlider(canvas, bounds, value, dragging);

    private void DrawButtonSettings(SKCanvas canvas, float leftMargin, float rightMargin, float y, float bottom)
    {
        float width = rightMargin - leftMargin;

        // OUTPUT TYPE section - vJoy Button vs Keyboard
        FUIRenderer.DrawText(canvas, "OUTPUT TYPE", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        y += 20;

        // Output type tabs
        string[] outputTypes = { "Button", "Keyboard" };
        float typeButtonWidth = (width - 5) / 2;
        float typeButtonHeight = 28f;

        for (int i = 0; i < 2; i++)
        {
            var typeBounds = new SKRect(leftMargin + i * (typeButtonWidth + 5), y,
                leftMargin + i * (typeButtonWidth + 5) + typeButtonWidth, y + typeButtonHeight);

            if (i == 0) _outputTypeBtnBounds = typeBounds;
            else _outputTypeKeyBounds = typeBounds;

            bool selected = (i == 0 && !_outputTypeIsKeyboard) || (i == 1 && _outputTypeIsKeyboard);
            bool hovered = _hoveredOutputType == i;

            var bgColor = selected
                ? FUIColors.Active.WithAlpha(60)
                : (hovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);
            var textColor = selected ? FUIColors.Active : (hovered ? FUIColors.TextPrimary : FUIColors.TextDim);

            using var typeBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
            canvas.DrawRoundRect(typeBounds, 3, 3, typeBgPaint);

            using var typeFramePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = selected ? FUIColors.Active : FUIColors.Frame,
                StrokeWidth = selected ? 2f : 1f
            };
            canvas.DrawRoundRect(typeBounds, 3, 3, typeFramePaint);

            FUIRenderer.DrawTextCentered(canvas, outputTypes[i], typeBounds, textColor, 14f);
        }
        y += typeButtonHeight + 16;

        // KEY COMBO section (only when Keyboard is selected)
        if (_outputTypeIsKeyboard)
        {
            FUIRenderer.DrawText(canvas, "KEY COMBO", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
            y += 20;

            float keyFieldHeight = 32f;
            _keyCaptureBounds = new SKRect(leftMargin, y, rightMargin, y + keyFieldHeight);

            // Check for key capture timeout
            if (_isCapturingKey)
            {
                var elapsed = (DateTime.Now - _keyCaptureStartTime).TotalMilliseconds;
                if (elapsed >= KeyCaptureTimeoutMs)
                {
                    _isCapturingKey = false; // Timeout - cancel capture
                }
            }

            // Draw key capture field background
            var keyBgColor = _isCapturingKey
                ? FUIColors.Active.WithAlpha(40)
                : (_keyCaptureBoundsHovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);

            using var keyBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = keyBgColor };
            canvas.DrawRoundRect(_keyCaptureBounds, 3, 3, keyBgPaint);

            // Draw timeout progress bar when capturing
            if (_isCapturingKey)
            {
                var elapsed = (DateTime.Now - _keyCaptureStartTime).TotalMilliseconds;
                float progress = Math.Min(1f, (float)(elapsed / KeyCaptureTimeoutMs));
                float remaining = 1f - progress;

                // Progress bar fills the field and shrinks from right to left
                float progressWidth = (_keyCaptureBounds.Width - 6) * remaining;
                if (progressWidth > 0)
                {
                    var progressRect = new SKRect(
                        _keyCaptureBounds.Left + 3,
                        _keyCaptureBounds.Top + 3,
                        _keyCaptureBounds.Left + 3 + progressWidth,
                        _keyCaptureBounds.Bottom - 3);
                    using var progressPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Fill,
                        Color = FUIColors.Active.WithAlpha(80)
                    };
                    canvas.DrawRoundRect(progressRect, 2, 2, progressPaint);
                }
            }

            var keyFrameColor = _isCapturingKey
                ? FUIColors.Active
                : (_keyCaptureBoundsHovered ? FUIColors.Primary : FUIColors.Frame);

            using var keyFramePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = keyFrameColor,
                StrokeWidth = _isCapturingKey ? 2f : 1f
            };
            canvas.DrawRoundRect(_keyCaptureBounds, 3, 3, keyFramePaint);

            // Display key combo or prompt
            if (_isCapturingKey)
            {
                byte alpha = (byte)(180 + MathF.Sin(_ctx.PulsePhase * 3) * 60);
                FUIRenderer.DrawTextCentered(canvas, "Press key combo...", _keyCaptureBounds, FUIColors.Warning.WithAlpha(alpha), 14f);
            }
            else if (!string.IsNullOrEmpty(_selectedKeyName))
            {
                // Draw keycaps centered in the field
                DrawKeycapsInBounds(canvas, _keyCaptureBounds, _selectedKeyName, _selectedModifiers);
            }
            else
            {
                FUIRenderer.DrawTextCentered(canvas, "Click to capture key", _keyCaptureBounds, FUIColors.TextDim, 14f);
            }
            y += keyFieldHeight + 16;
        }

        // Button Mode section
        FUIRenderer.DrawText(canvas, "BUTTON MODE", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        y += 20;

        // Mode buttons - all on one row
        string[] modes = { "Normal", "Toggle", "Pulse", "Hold" };
        float buttonHeight = 28f;  // 4px aligned, meets minimum touch target
        float buttonGap = 4f;
        float totalGap = buttonGap * (modes.Length - 1);
        float buttonWidth = (width - totalGap) / modes.Length;

        for (int i = 0; i < modes.Length; i++)
        {
            float buttonX = leftMargin + i * (buttonWidth + buttonGap);
            var modeBounds = new SKRect(buttonX, y, buttonX + buttonWidth, y + buttonHeight);
            bool selected = i == (int)_selectedButtonMode;
            bool hovered = i == _hoveredButtonMode;

            SKColor bgColor = selected ? FUIColors.Active.WithAlpha(60) :
                (hovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);

            using var modeBgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
            canvas.DrawRoundRect(modeBounds, 3, 3, modeBgPaint);

            using var modeFramePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = selected ? FUIColors.Active : FUIColors.Frame,
                StrokeWidth = selected ? 2f : 1f
            };
            canvas.DrawRoundRect(modeBounds, 3, 3, modeFramePaint);

            FUIRenderer.DrawTextCentered(canvas, modes[i], modeBounds,
                selected ? FUIColors.Active : FUIColors.TextPrimary, 12f);

            _buttonModeBounds[i] = modeBounds;
        }
        y += buttonHeight + 12;

        // Duration slider for Pulse mode
        if (_selectedButtonMode == ButtonMode.Pulse && y + 50 < bottom)
        {
            FUIRenderer.DrawText(canvas, "PULSE DURATION", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
            y += 18;

            float sliderHeight = 24f;
            _pulseDurationSliderBounds = new SKRect(leftMargin, y, rightMargin - 50, y + sliderHeight);

            // Normalize value: 100-1000ms mapped to 0-1
            float normalizedPulse = (_pulseDurationMs - 100f) / 900f;
            DrawDurationSlider(canvas, _pulseDurationSliderBounds, normalizedPulse, _draggingPulseDuration);

            // Value label
            FUIRenderer.DrawText(canvas, $"{_pulseDurationMs}ms",
                new SKPoint(rightMargin - 45, y + sliderHeight / 2 + 4), FUIColors.TextPrimary, 13f);

            y += sliderHeight + 12;
        }

        // Duration slider for Hold mode
        if (_selectedButtonMode == ButtonMode.HoldToActivate && y + 50 < bottom)
        {
            FUIRenderer.DrawText(canvas, "HOLD DURATION", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
            y += 18;

            float sliderHeight = 24f;
            _holdDurationSliderBounds = new SKRect(leftMargin, y, rightMargin - 50, y + sliderHeight);

            // Normalize value: 200-2000ms mapped to 0-1
            float normalizedHold = (_holdDurationMs - 200f) / 1800f;
            DrawDurationSlider(canvas, _holdDurationSliderBounds, normalizedHold, _draggingHoldDuration);

            // Value label
            FUIRenderer.DrawText(canvas, $"{_holdDurationMs}ms",
                new SKPoint(rightMargin - 45, y + sliderHeight / 2 + 4), FUIColors.TextPrimary, 13f);

            y += sliderHeight + 12;
        }

        // Clear binding button
        if (y + 40 < bottom)
        {
            var clearBounds = new SKRect(leftMargin, y, rightMargin, y + 32);
            _clearAllButtonBounds = clearBounds;

            var state = _clearAllButtonHovered ? FUIRenderer.ButtonState.Hover : FUIRenderer.ButtonState.Normal;
            FUIRenderer.DrawButton(canvas, clearBounds, "CLEAR MAPPING", state, FUIColors.Danger);
        }
    }

    /// <summary>
    /// Format key combo for display as simple text (used in mapping names)
    /// </summary>
    private string FormatKeyComboForDisplay(string keyName, List<string>? modifiers)
    {
        if (string.IsNullOrEmpty(keyName)) return "";

        var parts = new List<string>();
        if (modifiers is not null && modifiers.Count > 0)
        {
            parts.AddRange(modifiers);
        }
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    /// <summary>
    /// Draw keycaps centered within given bounds
    /// </summary>
    private void DrawKeycapsInBounds(SKCanvas canvas, SKRect bounds, string keyName, List<string>? modifiers)
        => FUIWidgets.DrawKeycapsInBounds(canvas, bounds, keyName, modifiers);

    private void DrawCurveVisualization(SKCanvas canvas, SKRect bounds)
    {
        // Background - darker than the panel
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background0 };
        canvas.DrawRect(bounds, bgPaint);

        // Grid lines (10% increments) - visible but subtle
        using var gridPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(60, 70, 80), // Visible gray grid lines
            StrokeWidth = 1f
        };

        for (float t = 0.1f; t < 1f; t += 0.1f)
        {
            // Skip 50% line - we'll draw it brighter
            if (Math.Abs(t - 0.5f) < 0.01f) continue;

            float x = bounds.Left + t * bounds.Width;
            float y = bounds.Bottom - t * bounds.Height;
            canvas.DrawLine(x, bounds.Top, x, bounds.Bottom, gridPaint);
            canvas.DrawLine(bounds.Left, y, bounds.Right, y, gridPaint);
        }

        // Center lines (brighter, 50% mark)
        using var centerPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(80, 95, 110), // More visible center lines
            StrokeWidth = 1f
        };
        canvas.DrawLine(bounds.MidX, bounds.Top, bounds.MidX, bounds.Bottom, centerPaint);
        canvas.DrawLine(bounds.Left, bounds.MidY, bounds.Right, bounds.MidY, centerPaint);

        // Reference linear line (dashed diagonal)
        using var refPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame.WithAlpha(50),
            StrokeWidth = 1f,
            PathEffect = SKPathEffect.CreateDash(new[] { 4f, 4f }, 0)
        };
        canvas.DrawLine(bounds.Left, bounds.Bottom, bounds.Right, bounds.Top, refPaint);

        // Draw the curve
        DrawCurvePath(canvas, bounds);

        // Draw control points (only for custom curve)
        if (_selectedCurveType == CurveType.Custom)
        {
            DrawCurveControlPoints(canvas, bounds);
        }

        // Frame
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame,
            StrokeWidth = 1f
        };
        canvas.DrawRect(bounds, framePaint);

        // Tick marks and labels on edges
        using var tickPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Frame.WithAlpha(150),
            StrokeWidth = 1f
        };

        float tickLen = 4f;
        float labelOffset = 3f;

        // Draw tick marks at 0%, 50%, 100% on bottom edge (IN axis)
        float[] tickPositions = { 0f, 0.5f, 1f };
        string[] tickLabels = { "0", "50", "100" };

        for (int i = 0; i < tickPositions.Length; i++)
        {
            float t = tickPositions[i];
            float x = bounds.Left + t * bounds.Width;

            // Bottom tick
            canvas.DrawLine(x, bounds.Bottom, x, bounds.Bottom + tickLen, tickPaint);

            // Label below tick
            float labelX = x - (t == 0 ? 0 : (t == 1 ? 12 : 6));
            FUIRenderer.DrawText(canvas, tickLabels[i], new SKPoint(labelX, bounds.Bottom + tickLen + labelOffset + 7), FUIColors.TextDim, 12f);
        }

        // Draw tick marks at 0%, 50%, 100% on left edge (OUT axis)
        for (int i = 0; i < tickPositions.Length; i++)
        {
            float t = tickPositions[i];
            float y = bounds.Bottom - t * bounds.Height;

            // Left tick
            canvas.DrawLine(bounds.Left - tickLen, y, bounds.Left, y, tickPaint);

            // Label left of tick
            float labelY = y + (t == 0 ? 3 : (t == 1 ? 7 : 3));
            float labelX = bounds.Left - tickLen - labelOffset - (tickLabels[i].Length > 1 ? 12 : 6);
            FUIRenderer.DrawText(canvas, tickLabels[i], new SKPoint(labelX, labelY), FUIColors.TextDim, 12f);
        }

        // Axis labels
        FUIRenderer.DrawText(canvas, "IN", new SKPoint(bounds.MidX - 6, bounds.Bottom + 22), FUIColors.TextDim, 12f);

        // Rotated "OUT" label
        canvas.Save();
        canvas.Translate(bounds.Left - 24, bounds.MidY + 8);
        canvas.RotateDegrees(-90);
        FUIRenderer.DrawText(canvas, "OUT", new SKPoint(0, 0), FUIColors.TextDim, 12f);
        canvas.Restore();
    }

    private float DrawAxisMovementIndicator(SKCanvas canvas, float leftMargin, float rightMargin, float y, AxisMapping axisMapping)
    {
        float width = rightMargin - leftMargin;
        float startY = y;

        // Get current raw input values for all input sources
        float rawInput = 0f;
        bool hasInput = false;

        if (axisMapping.Inputs.Count > 0)
        {
            var inputValues = new List<float>();

            foreach (var input in axisMapping.Inputs)
            {
                // Find the physical device
                var device = _ctx.Devices.FirstOrDefault(d => d.InstanceGuid.ToString() == input.DeviceId);
                if (device is null) continue;

                // Get the device state from InputService
                var state = _ctx.InputService.GetDeviceState(device.DeviceIndex);
                if (state is null || input.Index >= state.Axes.Length) continue;

                inputValues.Add(state.Axes[input.Index]);
                hasInput = true;
            }

            // Merge multiple inputs according to merge operation
            if (inputValues.Count > 0)
            {
                rawInput = axisMapping.MergeOp switch
                {
                    MergeOperation.Average => inputValues.Average(),
                    MergeOperation.Maximum => inputValues.Max(),
                    MergeOperation.Minimum => inputValues.Min(),
                    MergeOperation.Sum => Math.Clamp(inputValues.Sum(), -1f, 1f),
                    _ => inputValues[0]
                };
            }
        }

        // Apply the curve to get processed output
        float processedOutput = hasInput ? axisMapping.Curve.Apply(rawInput) : 0f;

        // Check if this is a centered axis (joystick) or end-only (throttle/slider)
        // Auto-detect based on output axis type if mode is set to default Centered
        bool isCentered;
        if (axisMapping.Curve.DeadzoneMode == DeadzoneMode.Centered)
        {
            // Auto-detect: Z axis and sliders are typically end-only (throttles)
            // X, Y, RX, RY, RZ are typically centered (joysticks)
            int outputIndex = axisMapping.Output.Index;
            isCentered = outputIndex switch
            {
                2 => false,  // Z axis - throttle
                6 => false,  // Slider1
                7 => false,  // Slider2
                _ => true    // X, Y, RX, RY, RZ - joystick axes
            };
        }
        else
        {
            isCentered = axisMapping.Curve.DeadzoneMode == DeadzoneMode.Centered;
        }

        // Convert to percentages for display
        float rawPercent, outPercent;
        if (isCentered)
        {
            // Centered: -100% to +100%
            rawPercent = rawInput * 100f;
            outPercent = processedOutput * 100f;
        }
        else
        {
            // End-only: 0% to 100% (convert from -1..1 to 0..100)
            rawPercent = (rawInput + 1f) * 50f;
            outPercent = (processedOutput + 1f) * 50f;
        }

        // Draw section header with live values
        string headerText = hasInput
            ? (isCentered
                ? $"LIVE INPUT: {rawPercent:+0;-0;0}%  →  OUTPUT: {outPercent:+0;-0;0}%"
                : $"LIVE INPUT: {rawPercent:0}%  →  OUTPUT: {outPercent:0}%")
            : "LIVE INPUT: (no signal)";

        var headerColor = hasInput ? FUIColors.Active : FUIColors.TextDim.WithAlpha(150);
        FUIRenderer.DrawText(canvas, headerText, new SKPoint(leftMargin, y), headerColor, 12f);
        y += 16f;

        if (hasInput)
        {
            // Draw a visual bar indicator for the processed output
            float barHeight = 8f;
            var barBounds = new SKRect(leftMargin, y, rightMargin, y + barHeight);

            // Background
            using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background0 };
            canvas.DrawRect(barBounds, bgPaint);

            // Convert output value to bar position (0..1)
            float normalizedValue = (processedOutput + 1f) / 2f;
            float barX = barBounds.Left + normalizedValue * barBounds.Width;

            if (isCentered)
            {
                // Center line for centered axes
                using var centerPaint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = FUIColors.Frame,
                    StrokeWidth = 1f
                };
                canvas.DrawLine(barBounds.MidX, barBounds.Top, barBounds.MidX, barBounds.Bottom, centerPaint);

                // Fill from center to current position
                var fillBounds = processedOutput >= 0
                    ? new SKRect(barBounds.MidX, barBounds.Top, barX, barBounds.Bottom)
                    : new SKRect(barX, barBounds.Top, barBounds.MidX, barBounds.Bottom);

                using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(180) };
                canvas.DrawRect(fillBounds, fillPaint);
            }
            else
            {
                // Fill from left edge to current position (for sliders/throttles)
                var fillBounds = new SKRect(barBounds.Left, barBounds.Top, barX, barBounds.Bottom);
                using var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Active.WithAlpha(180) };
                canvas.DrawRect(fillBounds, fillPaint);
            }

            // Position indicator (vertical line)
            using var indicatorPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.Active,
                StrokeWidth = 2f
            };
            canvas.DrawLine(barX, barBounds.Top, barX, barBounds.Bottom, indicatorPaint);

            // Frame
            using var framePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = FUIColors.Frame,
                StrokeWidth = 1f
            };
            canvas.DrawRect(barBounds, framePaint);

            y += barHeight + 2f;

            // Labels below bar - different for centered vs end-only
            if (isCentered)
            {
                FUIRenderer.DrawText(canvas, "-100%", new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, "0%", new SKPoint(barBounds.MidX - 8, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, "+100%", new SKPoint(rightMargin - 28, y), FUIColors.TextDim, 12f);
            }
            else
            {
                FUIRenderer.DrawText(canvas, "0%", new SKPoint(leftMargin, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, "50%", new SKPoint(barBounds.MidX - 8, y), FUIColors.TextDim, 12f);
                FUIRenderer.DrawText(canvas, "100%", new SKPoint(rightMargin - 20, y), FUIColors.TextDim, 12f);
            }
            y += 12f;
        }

        return y - startY;
    }

    private void DrawCurvePath(SKCanvas canvas, SKRect bounds)
    {
        using var path = new SKPath();
        bool first = true;

        // Sample the curve at many points
        for (float t = 0; t <= 1.001f; t += 0.01f)
        {
            float input = Math.Min(t, 1f);
            float output = ComputeCurveValue(input);

            float x = bounds.Left + input * bounds.Width;
            float y = bounds.Bottom - output * bounds.Height;

            if (first)
            {
                path.MoveTo(x, y);
                first = false;
            }
            else
            {
                path.LineTo(x, y);
            }
        }

        // Glow
        using var glowPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active.WithAlpha(50),
            StrokeWidth = 5f,
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateBlur(4f, 4f)
        };
        canvas.DrawPath(path, glowPaint);

        // Main line
        using var linePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Active,
            StrokeWidth = 2f,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };
        canvas.DrawPath(path, linePaint);
    }

    private float ComputeCurveValue(float input)
    {
        // Apply curve type only - deadzone is handled separately
        float output = _selectedCurveType switch
        {
            CurveType.Linear => input,
            CurveType.SCurve => ApplySCurve(input),
            CurveType.Exponential => ApplyExponential(input),
            CurveType.Custom => InterpolateControlPoints(input),
            _ => input
        };

        output = Math.Clamp(output, 0f, 1f);

        // Apply inversion
        if (_axisInverted)
            output = 1f - output;

        return output;
    }

    private float ApplySCurve(float x)
    {
        // S-curve using smoothstep-like function
        return x * x * (3f - 2f * x);
    }

    private float ApplyExponential(float x)
    {
        // Exponential curve (steeper at the end)
        return x * x;
    }

    private float InterpolateControlPoints(float x)
    {
        if (_curveControlPoints.Count < 2) return x;

        // Find segment containing x
        for (int i = 0; i < _curveControlPoints.Count - 1; i++)
        {
            var p1 = _curveControlPoints[i];
            var p2 = _curveControlPoints[i + 1];

            if (x >= p1.X && x <= p2.X)
            {
                if (Math.Abs(p2.X - p1.X) < 0.001f) return p1.Y;
                float t = (x - p1.X) / (p2.X - p1.X);

                // Use Catmull-Rom spline interpolation for smooth curves
                // Need 4 points: p0, p1, p2, p3
                var p0 = i > 0 ? _curveControlPoints[i - 1] : new SKPoint(p1.X - (p2.X - p1.X), p1.Y - (p2.Y - p1.Y));
                var p3 = i < _curveControlPoints.Count - 2 ? _curveControlPoints[i + 2] : new SKPoint(p2.X + (p2.X - p1.X), p2.Y + (p2.Y - p1.Y));

                return CatmullRomInterpolate(p0.Y, p1.Y, p2.Y, p3.Y, t);
            }
        }

        // Extrapolate
        return x < _curveControlPoints[0].X ? _curveControlPoints[0].Y : _curveControlPoints[^1].Y;
    }

    /// <summary>
    /// Catmull-Rom spline interpolation for smooth curves through control points.
    /// t ranges from 0 to 1, output is between p1 and p2.
    /// </summary>
    private static float CatmullRomInterpolate(float p0, float p1, float p2, float p3, float t)
    {
        // Catmull-Rom spline formula with tension = 0.5 (centripetal)
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    private void DrawCurveControlPoints(SKCanvas canvas, SKRect bounds)
    {
        const float PointRadius = 7f;
        const float CenterPointRadius = 3.5f; // Half size for center point

        for (int i = 0; i < _curveControlPoints.Count; i++)
        {
            var pt = _curveControlPoints[i];
            float x = bounds.Left + pt.X * bounds.Width;

            // Apply inversion to display Y position to match the curve
            float displayY = _axisInverted ? (1f - pt.Y) : pt.Y;
            float y = bounds.Bottom - displayY * bounds.Height;

            bool isHovered = i == _hoveredCurvePoint;
            bool isDragging = i == _draggingCurvePoint;
            bool isEndpoint = i == 0 || i == _curveControlPoints.Count - 1;
            bool isCenterPoint = Math.Abs(pt.X - 0.5f) < 0.01f && Math.Abs(pt.Y - 0.5f) < 0.01f;

            // Center point is smaller and not interactive
            float baseRadius = isCenterPoint ? CenterPointRadius : PointRadius;
            float radius = (isHovered || isDragging) && !isCenterPoint ? baseRadius + 2 : baseRadius;
            var color = isDragging ? FUIColors.Warning : (isHovered && !isCenterPoint ? FUIColors.TextBright : FUIColors.Active);

            // Glow (skip for center point)
            if (!isCenterPoint)
            {
                using var glowPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = color.WithAlpha(40),
                    IsAntialias = true,
                    ImageFilter = SKImageFilter.CreateBlur(5f, 5f)
                };
                canvas.DrawCircle(x, y, radius + 4, glowPaint);
            }

            // Fill
            using var fillPaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = isEndpoint || isCenterPoint ? FUIColors.Background1 : color.WithAlpha(60),
                IsAntialias = true
            };
            canvas.DrawCircle(x, y, radius, fillPaint);

            // Stroke
            using var strokePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = isCenterPoint ? FUIColors.Frame : color,
                StrokeWidth = isEndpoint ? 2f : (isCenterPoint ? 1f : 1.5f),
                IsAntialias = true
            };
            canvas.DrawCircle(x, y, radius, strokePaint);

            // Value label when hovered/dragged (not for center point)
            if ((isHovered || isDragging) && !isCenterPoint)
            {
                string label = $"({pt.X:F2}, {pt.Y:F2})";
                float labelY = y - radius - 10;
                if (labelY < bounds.Top + 10)
                    labelY = y + radius + 14;

                FUIRenderer.DrawText(canvas, label, new SKPoint(x - 22, labelY), FUIColors.TextBright, 12f);
            }
        }
    }

    private SKPoint CurveScreenToGraph(SKPoint screenPt, SKRect bounds)
    {
        float x = (screenPt.X - bounds.Left) / bounds.Width;
        float y = (bounds.Bottom - screenPt.Y) / bounds.Height;

        // If inverted, convert screen Y back to graph Y (uninvert)
        if (_axisInverted)
            y = 1f - y;

        return new SKPoint(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
    }

    private int FindCurvePointAt(SKPoint screenPt, SKRect bounds)
    {
        const float HitRadius = 12f;

        for (int i = 0; i < _curveControlPoints.Count; i++)
        {
            var pt = _curveControlPoints[i];

            // Skip center point - it's not selectable
            bool isCenterPoint = Math.Abs(pt.X - 0.5f) < 0.01f && Math.Abs(pt.Y - 0.5f) < 0.01f;
            if (isCenterPoint)
                continue;

            float x = bounds.Left + pt.X * bounds.Width;

            // Apply inversion to display Y position to match the visual
            float displayY = _axisInverted ? (1f - pt.Y) : pt.Y;
            float y = bounds.Bottom - displayY * bounds.Height;

            float dist = MathF.Sqrt(MathF.Pow(screenPt.X - x, 2) + MathF.Pow(screenPt.Y - y, 2));
            if (dist <= HitRadius)
                return i;
        }
        return -1;
    }

    private int FindDeadzoneHandleAt(SKPoint screenPt)
    {
        const float HitRadius = 12f;
        var bounds = _deadzoneSliderBounds;
        if (bounds.Width <= 0) return -1;

        // Convert deadzone values to 0..1 range
        float minPos = (_deadzoneMin + 1f) / 2f;
        float centerMinPos = (_deadzoneCenterMin + 1f) / 2f;
        float centerMaxPos = (_deadzoneCenterMax + 1f) / 2f;
        float maxPos = (_deadzoneMax + 1f) / 2f;

        if (_deadzoneCenterEnabled)
        {
            // Two separate tracks - must calculate handle positions on each track
            // Gap must match DrawDualDeadzoneSlider
            float gap = 24f;
            float centerX = bounds.MidX;

            // Left track: from bounds.Left to centerX - gap/2
            float leftTrackLeft = bounds.Left;
            float leftTrackRight = centerX - gap / 2;
            float leftTrackWidth = leftTrackRight - leftTrackLeft;

            // Right track: from centerX + gap/2 to bounds.Right
            float rightTrackLeft = centerX + gap / 2;
            float rightTrackRight = bounds.Right;
            float rightTrackWidth = rightTrackRight - rightTrackLeft;

            // Map positions to track coordinates
            float minPosInLeft = Math.Clamp((minPos - 0f) / 0.5f, 0f, 1f);
            float ctrMinPosInLeft = Math.Clamp((centerMinPos - 0f) / 0.5f, 0f, 1f);
            float ctrMaxPosInRight = Math.Clamp((centerMaxPos - 0.5f) / 0.5f, 0f, 1f);
            float maxPosInRight = Math.Clamp((maxPos - 0.5f) / 0.5f, 0f, 1f);

            // Calculate screen positions for each handle
            float minHandleX = leftTrackLeft + minPosInLeft * leftTrackWidth;
            float ctrMinHandleX = leftTrackLeft + ctrMinPosInLeft * leftTrackWidth;
            float ctrMaxHandleX = rightTrackLeft + ctrMaxPosInRight * rightTrackWidth;
            float maxHandleX = rightTrackLeft + maxPosInRight * rightTrackWidth;

            // Check each handle (check all 4)
            float[] handleXs = { minHandleX, ctrMinHandleX, ctrMaxHandleX, maxHandleX };
            for (int i = 0; i < 4; i++)
            {
                float dist = MathF.Sqrt(MathF.Pow(screenPt.X - handleXs[i], 2) + MathF.Pow(screenPt.Y - bounds.MidY, 2));
                if (dist <= HitRadius)
                    return i;
            }
        }
        else
        {
            // Single track - only min (0) and max (3) handles
            float minHandleX = bounds.Left + minPos * bounds.Width;
            float maxHandleX = bounds.Left + maxPos * bounds.Width;

            float distMin = MathF.Sqrt(MathF.Pow(screenPt.X - minHandleX, 2) + MathF.Pow(screenPt.Y - bounds.MidY, 2));
            if (distMin <= HitRadius) return 0;

            float distMax = MathF.Sqrt(MathF.Pow(screenPt.X - maxHandleX, 2) + MathF.Pow(screenPt.Y - bounds.MidY, 2));
            if (distMax <= HitRadius) return 3;
        }

        return -1;
    }

    private void UpdateDraggedDeadzoneHandle(SKPoint screenPt)
    {
        if (_draggingDeadzoneHandle < 0) return;
        var bounds = _deadzoneSliderBounds;
        if (bounds.Width <= 0) return;

        float value;

        if (_deadzoneCenterEnabled)
        {
            // Two-track layout - convert screen position to value based on which track
            // Gap must match DrawDualDeadzoneSlider
            float gap = 24f;
            float centerX = bounds.MidX;

            // Left track: maps to -1..0 (handles 0 and 1)
            float leftTrackLeft = bounds.Left;
            float leftTrackRight = centerX - gap / 2;
            float leftTrackWidth = leftTrackRight - leftTrackLeft;

            // Right track: maps to 0..1 (handles 2 and 3)
            float rightTrackLeft = centerX + gap / 2;
            float rightTrackRight = bounds.Right;
            float rightTrackWidth = rightTrackRight - rightTrackLeft;

            switch (_draggingDeadzoneHandle)
            {
                case 0: // Min handle on left track
                    float normLeft0 = Math.Clamp((screenPt.X - leftTrackLeft) / leftTrackWidth, 0f, 1f);
                    value = normLeft0 - 1f; // Maps 0..1 to -1..0
                    value = Math.Clamp(value, -1f, _deadzoneCenterMin - 0.02f);
                    _deadzoneMin = value;
                    break;
                case 1: // CenterMin handle on left track (right edge)
                    float normLeft1 = Math.Clamp((screenPt.X - leftTrackLeft) / leftTrackWidth, 0f, 1f);
                    value = normLeft1 - 1f; // Maps 0..1 to -1..0
                    value = Math.Clamp(value, _deadzoneMin + 0.02f, 0f);
                    _deadzoneCenterMin = value;
                    break;
                case 2: // CenterMax handle on right track (left edge)
                    float normRight2 = Math.Clamp((screenPt.X - rightTrackLeft) / rightTrackWidth, 0f, 1f);
                    value = normRight2; // Maps 0..1 to 0..1
                    value = Math.Clamp(value, 0f, _deadzoneMax - 0.02f);
                    _deadzoneCenterMax = value;
                    break;
                case 3: // Max handle on right track
                    float normRight3 = Math.Clamp((screenPt.X - rightTrackLeft) / rightTrackWidth, 0f, 1f);
                    value = normRight3; // Maps 0..1 to 0..1
                    value = Math.Clamp(value, _deadzoneCenterMax + 0.02f, 1f);
                    _deadzoneMax = value;
                    break;
            }
        }
        else
        {
            // Single track layout - convert screen X to -1..1 range
            float normalized = (screenPt.X - bounds.Left) / bounds.Width;
            value = normalized * 2f - 1f;

            switch (_draggingDeadzoneHandle)
            {
                case 0: // Min handle
                    value = Math.Clamp(value, -1f, _deadzoneMax - 0.1f);
                    _deadzoneMin = value;
                    break;
                case 3: // Max handle
                    value = Math.Clamp(value, _deadzoneMin + 0.1f, 1f);
                    _deadzoneMax = value;
                    break;
            }
        }
    }

    private void UpdateDraggedCurvePoint(SKPoint screenPt)
    {
        if (_draggingCurvePoint < 0 || _draggingCurvePoint >= _curveControlPoints.Count)
            return;

        var graphPt = CurveScreenToGraph(screenPt, _curveEditorBounds);

        // Constrain endpoints to X edges
        if (_draggingCurvePoint == 0)
            graphPt.X = 0;
        else if (_draggingCurvePoint == _curveControlPoints.Count - 1)
            graphPt.X = 1;
        else
        {
            // Interior points: constrain X between neighbors
            float minX = _curveControlPoints[_draggingCurvePoint - 1].X + 0.02f;
            float maxX = _curveControlPoints[_draggingCurvePoint + 1].X - 0.02f;
            // Ensure minX <= maxX (neighbors might be very close)
            if (minX > maxX)
            {
                float midX = (_curveControlPoints[_draggingCurvePoint - 1].X + _curveControlPoints[_draggingCurvePoint + 1].X) / 2f;
                graphPt.X = midX;
            }
            else
            {
                graphPt.X = Math.Clamp(graphPt.X, minX, maxX);
            }
        }

        _curveControlPoints[_draggingCurvePoint] = graphPt;

        // If symmetrical mode is enabled, mirror the change
        if (_curveSymmetrical)
        {
            UpdateSymmetricalPoint(_draggingCurvePoint, graphPt);
        }
    }

    /// <summary>
    /// Update the symmetrical counterpart of a curve point.
    /// Points are mirrored around the center (0.5, 0.5).
    /// </summary>
    private void UpdateSymmetricalPoint(int pointIndex, SKPoint graphPt)
    {
        // Mirror point: (x, y) -> (1-x, 1-y)
        float mirrorX = 1f - graphPt.X;
        float mirrorY = 1f - graphPt.Y;
        var mirrorPt = new SKPoint(mirrorX, mirrorY);

        // Find the corresponding mirror point in the list
        // Points are stored sorted by X, so we need to find the one with matching mirror X
        int mirrorIndex = FindMirrorPointIndex(pointIndex, mirrorX);

        if (mirrorIndex >= 0 && mirrorIndex != pointIndex)
        {
            // Update mirror point, but constrain to valid range
            if (mirrorIndex > 0 && mirrorIndex < _curveControlPoints.Count - 1)
            {
                // Interior point - constrain X between neighbors
                float minX = _curveControlPoints[mirrorIndex - 1].X + 0.02f;
                float maxX = _curveControlPoints[mirrorIndex + 1].X - 0.02f;
                mirrorPt = new SKPoint(Math.Clamp(mirrorPt.X, minX, maxX), mirrorPt.Y);
            }
            else if (mirrorIndex == 0)
            {
                mirrorPt = new SKPoint(0, mirrorPt.Y);
            }
            else if (mirrorIndex == _curveControlPoints.Count - 1)
            {
                mirrorPt = new SKPoint(1, mirrorPt.Y);
            }

            _curveControlPoints[mirrorIndex] = mirrorPt;
        }
    }

    /// <summary>
    /// Find the index of the mirror point for symmetry.
    /// Returns -1 if no suitable mirror point exists.
    /// </summary>
    private int FindMirrorPointIndex(int sourceIndex, float targetX)
    {
        // Special cases for endpoints
        if (sourceIndex == 0) return _curveControlPoints.Count - 1;
        if (sourceIndex == _curveControlPoints.Count - 1) return 0;

        // For interior points, find the one closest to the mirror X position
        int bestIndex = -1;
        float bestDist = float.MaxValue;

        for (int i = 0; i < _curveControlPoints.Count; i++)
        {
            if (i == sourceIndex) continue;

            float dist = Math.Abs(_curveControlPoints[i].X - targetX);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    /// <summary>
    /// Make the current curve points symmetrical around the center.
    /// </summary>
    private void MakeCurveSymmetrical()
    {
        if (_curveControlPoints.Count < 2) return;

        // Create a new symmetrical set of points
        var newPoints = new List<SKPoint>();

        // Always include start point
        newPoints.Add(new SKPoint(0, 0));

        // For each point in the left half (X < 0.5), create its mirror
        var leftHalf = _curveControlPoints
            .Where(p => p.X > 0 && p.X < 0.5f)
            .OrderBy(p => p.X)
            .ToList();

        foreach (var pt in leftHalf)
        {
            newPoints.Add(pt);
        }

        // Add center point if there's one
        var centerPoint = _curveControlPoints.FirstOrDefault(p => Math.Abs(p.X - 0.5f) < 0.02f);
        if (centerPoint.X > 0.4f && centerPoint.X < 0.6f)
        {
            newPoints.Add(new SKPoint(0.5f, 0.5f)); // Center is always (0.5, 0.5) for perfect symmetry
        }
        else if (leftHalf.Count > 0)
        {
            // Add a center point if we have left half points
            newPoints.Add(new SKPoint(0.5f, 0.5f));
        }

        // Add mirrored points from left half (in reverse order for right half)
        for (int i = leftHalf.Count - 1; i >= 0; i--)
        {
            var pt = leftHalf[i];
            newPoints.Add(new SKPoint(1f - pt.X, 1f - pt.Y));
        }

        // Always include end point
        newPoints.Add(new SKPoint(1, 1));

        _curveControlPoints = newPoints;
    }

    private void AddCurveControlPoint(SKPoint graphPt)
    {
        // Don't add points at exact endpoints
        if (graphPt.X <= 0.01f || graphPt.X >= 0.99f) return;

        // Find insertion position (maintain sorted order by X)
        int insertIndex = 0;
        for (int i = 0; i < _curveControlPoints.Count; i++)
        {
            if (_curveControlPoints[i].X < graphPt.X)
                insertIndex = i + 1;
        }

        _curveControlPoints.Insert(insertIndex, graphPt);

        // If symmetrical mode is enabled, also add the mirror point
        if (_curveSymmetrical)
        {
            float mirrorX = 1f - graphPt.X;
            float mirrorY = 1f - graphPt.Y;

            // Don't add if mirror point would be too close to existing point
            bool tooClose = _curveControlPoints.Any(p => Math.Abs(p.X - mirrorX) < 0.04f);
            if (!tooClose && mirrorX > 0.01f && mirrorX < 0.99f)
            {
                // Find insertion position for mirror point
                int mirrorInsertIndex = 0;
                for (int i = 0; i < _curveControlPoints.Count; i++)
                {
                    if (_curveControlPoints[i].X < mirrorX)
                        mirrorInsertIndex = i + 1;
                }

                _curveControlPoints.Insert(mirrorInsertIndex, new SKPoint(mirrorX, mirrorY));
            }
        }

        _selectedCurveType = CurveType.Custom;
        _ctx.InvalidateCanvas();
    }

    private void RemoveCurveControlPoint(int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= _curveControlPoints.Count)
            return;

        var pt = _curveControlPoints[pointIndex];

        // Don't remove endpoints (0,0) or (1,1)
        bool isEndpoint = pointIndex == 0 || pointIndex == _curveControlPoints.Count - 1;
        if (isEndpoint)
            return;

        // Don't remove center point (0.5, 0.5)
        bool isCenterPoint = Math.Abs(pt.X - 0.5f) < 0.01f && Math.Abs(pt.Y - 0.5f) < 0.01f;
        if (isCenterPoint)
            return;

        // Remove the point
        _curveControlPoints.RemoveAt(pointIndex);

        // If symmetrical mode is enabled, also remove the mirror point
        if (_curveSymmetrical)
        {
            float mirrorX = 1f - pt.X;

            // Find and remove the mirror point
            for (int i = _curveControlPoints.Count - 1; i >= 0; i--)
            {
                var mirrorPt = _curveControlPoints[i];
                // Skip endpoints and center
                if (i == 0 || i == _curveControlPoints.Count - 1)
                    continue;
                if (Math.Abs(mirrorPt.X - 0.5f) < 0.01f && Math.Abs(mirrorPt.Y - 0.5f) < 0.01f)
                    continue;

                if (Math.Abs(mirrorPt.X - mirrorX) < 0.02f)
                {
                    _curveControlPoints.RemoveAt(i);
                    break;
                }
            }
        }

        _ctx.InvalidateCanvas();
    }

    private bool HandleCurvePresetClick(SKPoint pt)
    {
        // Check each stored preset button bound
        for (int i = 0; i < _curvePresetBounds.Length; i++)
        {
            if (_curvePresetBounds[i].Contains(pt))
            {
                _selectedCurveType = i switch
                {
                    0 => CurveType.Linear,
                    1 => CurveType.SCurve,
                    2 => CurveType.Exponential,
                    _ => CurveType.Custom
                };

                // Reset control points when switching to custom
                if (_selectedCurveType == CurveType.Custom && _curveControlPoints.Count == 2)
                {
                    // Add a middle point for custom curve
                    _curveControlPoints = new List<SKPoint>
                    {
                        new(0, 0),
                        new(0.5f, 0.5f),
                        new(1, 1)
                    };
                }

                SaveAxisSettingsForRow();  // Persist curve type change
                _ctx.InvalidateCanvas();
                return true;
            }
        }

        // Check invert checkbox
        if (_invertToggleBounds.Contains(pt))
        {
            _axisInverted = !_axisInverted;
            SaveAxisSettingsForRow();  // Persist invert change
            _ctx.InvalidateCanvas();
            return true;
        }

        // Check symmetrical checkbox (only for Custom curve)
        if (!_curveSymmetricalCheckboxBounds.IsEmpty && _curveSymmetricalCheckboxBounds.Contains(pt))
        {
            _curveSymmetrical = !_curveSymmetrical;
            if (_curveSymmetrical)
            {
                // When enabling symmetry, mirror existing points around center
                MakeCurveSymmetrical();
            }
            SaveAxisSettingsForRow();  // Persist symmetry change
            _ctx.InvalidateCanvas();
            return true;
        }

        // Check centre checkbox and deadzone presets
        if (HandleDeadzonePresetClick(pt))
            return true;

        return false;
    }

    private bool HandleDeadzonePresetClick(SKPoint pt)
    {
        // Centre checkbox click
        if (_deadzoneCenterCheckboxBounds.Contains(pt))
        {
            _deadzoneCenterEnabled = !_deadzoneCenterEnabled;
            // When disabling center, reset center values and clear selection if center handle was selected
            if (!_deadzoneCenterEnabled)
            {
                _deadzoneCenterMin = 0.0f;
                _deadzoneCenterMax = 0.0f;
                if (_selectedDeadzoneHandle == 1 || _selectedDeadzoneHandle == 2)
                    _selectedDeadzoneHandle = -1;
            }
            SaveAxisSettingsForRow();  // Persist center deadzone change
            _ctx.InvalidateCanvas();
            return true;
        }

        // Preset buttons - apply to selected handle
        if (_selectedDeadzoneHandle >= 0)
        {
            // Preset values: 0%, 2%, 5%, 10%
            float[] presetValues = { 0.0f, 0.02f, 0.05f, 0.10f };

            for (int i = 0; i < _deadzonePresetBounds.Length; i++)
            {
                if (!_deadzonePresetBounds[i].IsEmpty && _deadzonePresetBounds[i].Contains(pt))
                {
                    float presetVal = presetValues[i];

                    switch (_selectedDeadzoneHandle)
                    {
                        case 0: // Min (Start) - set distance from -1
                            _deadzoneMin = -1.0f + presetVal;
                            break;
                        case 1: // CenterMin - set negative offset from 0
                            _deadzoneCenterMin = -presetVal;
                            break;
                        case 2: // CenterMax - set positive offset from 0
                            _deadzoneCenterMax = presetVal;
                            break;
                        case 3: // Max (End) - set distance from 1
                            _deadzoneMax = 1.0f - presetVal;
                            break;
                    }
                    SaveAxisSettingsForRow();  // Persist deadzone preset change
                    _ctx.InvalidateCanvas();
                    return true;
                }
            }
        }
        return false;
    }

    private void DrawSlider(SKCanvas canvas, SKRect bounds, float value)
        => FUIWidgets.DrawSlider(canvas, bounds, value);

    private void DrawToggleSwitch(SKCanvas canvas, SKRect bounds, bool on)
        => FUIWidgets.DrawToggleSwitch(canvas, bounds, on, _ctx.MousePosition);

    private void DrawSettingsSlider(SKCanvas canvas, SKRect bounds, int value, int maxValue)
        => FUIWidgets.DrawSettingsSlider(canvas, bounds, value, maxValue);

    private void DrawMappingEditorPanel(SKCanvas canvas, SKRect bounds, float frameInset)
    {
        // Panel background
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = FUIColors.Background1.WithAlpha(160),
            IsAntialias = true
        };
        canvas.DrawRect(bounds.Inset(frameInset, frameInset), bgPaint);
        FUIRenderer.DrawLCornerFrame(canvas, bounds, FUIColors.Active, 30f, 8f);

        float y = bounds.Top + frameInset + 16;
        float leftMargin = bounds.Left + frameInset + 16;
        float rightMargin = bounds.Right - frameInset - 16;

        // Title
        string outputName = GetEditingOutputName();
        FUIRenderer.DrawText(canvas, $"EDIT: {outputName}", new SKPoint(leftMargin, y),
            FUIColors.Active, 14f, true);
        y += 30;

        // INPUT SOURCE section
        FUIRenderer.DrawText(canvas, "INPUT SOURCE", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
        y += 20;

        // Input field - double-click to listen for input
        float inputFieldHeight = 36f;
        _inputFieldBounds = new SKRect(leftMargin, y, rightMargin, y + inputFieldHeight);
        DrawInputField(canvas, _inputFieldBounds);
        y += inputFieldHeight + 10;

        // Manual entry toggle button
        _manualEntryButtonBounds = new SKRect(leftMargin, y, leftMargin + 120, y + 24);
        DrawToggleButton(canvas, _manualEntryButtonBounds, "Manual Entry", _manualEntryMode, _manualEntryButtonHovered);
        y += 34;

        // Manual entry dropdowns (if enabled)
        if (_manualEntryMode)
        {
            y = DrawManualEntrySection(canvas, bounds, y, leftMargin, rightMargin);
        }

        // Output type and button mode section (only for button outputs)
        if (!_isEditingAxis)
        {
            // Output type selector (Button vs Keyboard)
            y += 10;
            FUIRenderer.DrawText(canvas, "OUTPUT TYPE", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
            y += 20;
            DrawOutputTypeSelector(canvas, leftMargin, y, rightMargin - leftMargin);
            y += 38;

            // Key capture field (only when Keyboard is selected)
            if (_outputTypeIsKeyboard)
            {
                FUIRenderer.DrawText(canvas, "KEY", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
                y += 20;
                float keyFieldHeight = 32f;
                _keyCaptureBounds = new SKRect(leftMargin, y, rightMargin, y + keyFieldHeight);
                DrawKeyCapture(canvas, _keyCaptureBounds);
                y += keyFieldHeight + 10;
            }

            // Button mode selector
            y += 10;
            FUIRenderer.DrawText(canvas, "BUTTON MODE", new SKPoint(leftMargin, y), FUIColors.TextDim, 13f);
            y += 20;
            DrawButtonModeSelector(canvas, leftMargin, y, rightMargin - leftMargin);
            y += 40;
        }

        // Action buttons at bottom
        float buttonWidth = 80f;
        float buttonHeight = 32f;
        float buttonY = bounds.Bottom - frameInset - buttonHeight - 16;

        _cancelButtonBounds = new SKRect(rightMargin - buttonWidth * 2 - 10, buttonY,
            rightMargin - buttonWidth - 10, buttonY + buttonHeight);
        _saveButtonBounds = new SKRect(rightMargin - buttonWidth, buttonY,
            rightMargin, buttonY + buttonHeight);

        DrawActionButton(canvas, _cancelButtonBounds, "Cancel", _cancelButtonHovered, false);
        DrawActionButton(canvas, _saveButtonBounds, "Save", _saveButtonHovered, true);
    }

    private string GetEditingOutputName()
    {
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count)
            return "Unknown";

        if (_isEditingAxis)
        {
            string[] axisNames = { "X Axis", "Y Axis", "Z Axis", "RX Axis", "RY Axis", "RZ Axis", "Slider 1", "Slider 2" };
            int axisIndex = _editingRowIndex;
            return axisIndex < axisNames.Length ? axisNames[axisIndex] : $"Axis {axisIndex}";
        }
        else
        {
            int buttonIndex = _editingRowIndex - 8;
            return $"Button {buttonIndex + 1}";
        }
    }

    private void DrawInputField(SKCanvas canvas, SKRect bounds)
    {
        // Background
        var bgColor = _isListeningForInput
            ? FUIColors.Warning.WithAlpha(40)
            : (_inputFieldHovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        // Frame
        var frameColor = _isListeningForInput
            ? FUIColors.Warning
            : (_inputFieldHovered ? FUIColors.Primary : FUIColors.Frame);
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = frameColor,
            StrokeWidth = _isListeningForInput ? 2f : 1f
        };
        canvas.DrawRect(bounds, framePaint);

        // Text content
        float textY = bounds.MidY + 5;
        if (_isListeningForInput)
        {
            byte alpha = (byte)(180 + MathF.Sin(_ctx.PulsePhase * 3) * 60);
            FUIRenderer.DrawText(canvas, "Press a button or move an axis...",
                new SKPoint(bounds.Left + 10, textY), FUIColors.Warning.WithAlpha(alpha), 15f);
        }
        else if (_pendingInput is not null)
        {
            FUIRenderer.DrawText(canvas, _pendingInput.ToString(),
                new SKPoint(bounds.Left + 10, textY), FUIColors.TextBright, 15f);
        }
        else
        {
            FUIRenderer.DrawText(canvas, "Double-click to detect input",
                new SKPoint(bounds.Left + 10, textY), FUIColors.TextDisabled, 15f);
        }

        // Clear button if there's input
        if (_pendingInput is not null && !_isListeningForInput)
        {
            var clearBounds = new SKRect(bounds.Right - 28, bounds.Top + 6, bounds.Right - 6, bounds.Bottom - 6);
            DrawSmallIconButton(canvas, clearBounds, "×", false, true);
        }
    }

    private void DrawToggleButton(SKCanvas canvas, SKRect bounds, string text, bool active, bool hovered)
        => FUIWidgets.DrawToggleButton(canvas, bounds, text, active, hovered);

    private float DrawManualEntrySection(SKCanvas canvas, SKRect bounds, float y, float leftMargin, float rightMargin)
    {
        // Device dropdown
        FUIRenderer.DrawText(canvas, "Device:", new SKPoint(leftMargin, y + 12), FUIColors.TextDim, 13f);
        float dropdownX = leftMargin + 55;
        _deviceDropdownBounds = new SKRect(dropdownX, y, rightMargin, y + 28);
        string deviceText = _ctx.Devices.Count > 0 && _selectedSourceDevice < _ctx.Devices.Count
            ? _ctx.Devices[_selectedSourceDevice].Name
            : "No devices";
        DrawDropdown(canvas, _deviceDropdownBounds, deviceText, _deviceDropdownOpen);
        y += 36;

        // Control dropdown
        string controlLabel = _isEditingAxis ? "Axis:" : "Button:";
        FUIRenderer.DrawText(canvas, controlLabel, new SKPoint(leftMargin, y + 12), FUIColors.TextDim, 13f);
        _controlDropdownBounds = new SKRect(dropdownX, y, rightMargin, y + 28);
        string controlText = GetControlDropdownText();
        DrawDropdown(canvas, _controlDropdownBounds, controlText, _controlDropdownOpen);
        y += 36;

        // Draw dropdown lists if open
        if (_deviceDropdownOpen)
        {
            DrawDeviceDropdownList(canvas, _deviceDropdownBounds);
        }
        else if (_controlDropdownOpen)
        {
            DrawControlDropdownList(canvas, _controlDropdownBounds);
        }

        return y;
    }

    private string GetControlDropdownText()
    {
        if (_ctx.Devices.Count == 0 || _selectedSourceDevice >= _ctx.Devices.Count)
            return "—";

        var device = _ctx.Devices[_selectedSourceDevice];
        if (_isEditingAxis)
        {
            int axisCount = 8; // Typical axis count
            if (_selectedSourceControl < axisCount)
                return $"Axis {_selectedSourceControl}";
        }
        else
        {
            if (_selectedSourceControl < 128)
                return $"Button {_selectedSourceControl + 1}";
        }
        return "—";
    }

    private void DrawDropdown(SKCanvas canvas, SKRect bounds, string text, bool open)
        => FUIWidgets.DrawDropdown(canvas, bounds, text, open);

    private void DrawDeviceDropdownList(SKCanvas canvas, SKRect anchorBounds)
    {
        float itemHeight = 28f;  // 4px aligned
        float listHeight = Math.Min(_ctx.Devices.Count * itemHeight, 200);
        var listBounds = new SKRect(anchorBounds.Left, anchorBounds.Bottom + 2,
            anchorBounds.Right, anchorBounds.Bottom + 2 + listHeight);

        // Draw shadow/backdrop for visual separation
        using var shadowPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.Black.WithAlpha(120)
        };
        var shadowBounds = new SKRect(listBounds.Left - 1, listBounds.Top - 1, listBounds.Right + 5, listBounds.Bottom + 5);
        canvas.DrawRect(shadowBounds, shadowPaint);

        // Solid opaque background
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1 };
        canvas.DrawRect(listBounds, bgPaint);

        // Draw items
        float y = listBounds.Top;
        for (int i = 0; i < _ctx.Devices.Count && y < listBounds.Bottom; i++)
        {
            var itemBounds = new SKRect(listBounds.Left, y, listBounds.Right, y + itemHeight);
            bool hovered = i == _hoveredDeviceIndex;

            if (hovered)
            {
                using var hoverPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Primary.WithAlpha(60) };
                canvas.DrawRect(itemBounds, hoverPaint);
            }

            FUIRenderer.DrawText(canvas, _ctx.Devices[i].Name, new SKPoint(itemBounds.Left + 8, itemBounds.MidY + 4),
                hovered ? FUIColors.TextBright : FUIColors.TextPrimary, 14f);
            y += itemHeight;
        }

        // Frame on top
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Primary,
            StrokeWidth = 1f
        };
        canvas.DrawRect(listBounds, framePaint);
    }

    private void DrawControlDropdownList(SKCanvas canvas, SKRect anchorBounds)
    {
        int controlCount = _isEditingAxis ? 8 : 32; // Show first 8 axes or 32 buttons
        float itemHeight = 24f;
        float listHeight = Math.Min(controlCount * itemHeight, 200);
        var listBounds = new SKRect(anchorBounds.Left, anchorBounds.Bottom + 2,
            anchorBounds.Right, anchorBounds.Bottom + 2 + listHeight);

        // Draw shadow/backdrop for visual separation
        using var shadowPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.Black.WithAlpha(120)
        };
        var shadowBounds = new SKRect(listBounds.Left - 1, listBounds.Top - 1, listBounds.Right + 5, listBounds.Bottom + 5);
        canvas.DrawRect(shadowBounds, shadowPaint);

        // Solid opaque background
        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Background1 };
        canvas.DrawRect(listBounds, bgPaint);

        // Draw items
        float y = listBounds.Top;
        for (int i = 0; i < controlCount && y < listBounds.Bottom; i++)
        {
            var itemBounds = new SKRect(listBounds.Left, y, listBounds.Right, y + itemHeight);
            bool hovered = i == _hoveredControlIndex;

            if (hovered)
            {
                using var hoverPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = FUIColors.Primary.WithAlpha(60) };
                canvas.DrawRect(itemBounds, hoverPaint);
            }

            string name = _isEditingAxis ? $"Axis {i}" : $"Button {i + 1}";
            FUIRenderer.DrawText(canvas, name, new SKPoint(itemBounds.Left + 8, itemBounds.MidY + 4),
                hovered ? FUIColors.TextBright : FUIColors.TextPrimary, 14f);
            y += itemHeight;
        }

        // Frame on top
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = FUIColors.Primary,
            StrokeWidth = 1f
        };
        canvas.DrawRect(listBounds, framePaint);
    }

    private void DrawButtonModeSelector(SKCanvas canvas, float x, float y, float width)
    {
        ButtonMode[] modes = { ButtonMode.Normal, ButtonMode.Toggle, ButtonMode.Pulse, ButtonMode.HoldToActivate };
        string[] labels = { "Normal", "Toggle", "Pulse", "Hold" };
        float buttonWidth = (width - 16) / 4;
        float buttonHeight = 28f;

        for (int i = 0; i < modes.Length; i++)
        {
            var modeBounds = new SKRect(x + i * (buttonWidth + 5), y, x + i * (buttonWidth + 5) + buttonWidth, y + buttonHeight);
            _buttonModeBounds[i] = modeBounds;

            bool selected = _selectedButtonMode == modes[i];
            bool hovered = _hoveredButtonMode == i;

            var bgColor = selected
                ? FUIColors.Active.WithAlpha(60)
                : (hovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);
            var textColor = selected ? FUIColors.Active : (hovered ? FUIColors.TextPrimary : FUIColors.TextDim);

            using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
            canvas.DrawRect(modeBounds, bgPaint);

            using var framePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = selected ? FUIColors.Active : FUIColors.Frame,
                StrokeWidth = selected ? 2f : 1f
            };
            canvas.DrawRect(modeBounds, framePaint);

            FUIRenderer.DrawTextCentered(canvas, labels[i], modeBounds, textColor, 13f);
        }
    }

    private void DrawOutputTypeSelector(SKCanvas canvas, float x, float y, float width)
    {
        string[] labels = { "Button", "Keyboard" };
        float buttonWidth = (width - 5) / 2;
        float buttonHeight = 28f;

        for (int i = 0; i < 2; i++)
        {
            var typeBounds = new SKRect(x + i * (buttonWidth + 5), y, x + i * (buttonWidth + 5) + buttonWidth, y + buttonHeight);
            if (i == 0) _outputTypeBtnBounds = typeBounds;
            else _outputTypeKeyBounds = typeBounds;

            bool selected = (i == 0 && !_outputTypeIsKeyboard) || (i == 1 && _outputTypeIsKeyboard);
            bool hovered = _hoveredOutputType == i;

            var bgColor = selected
                ? FUIColors.Active.WithAlpha(60)
                : (hovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);
            var textColor = selected ? FUIColors.Active : (hovered ? FUIColors.TextPrimary : FUIColors.TextDim);

            using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
            canvas.DrawRect(typeBounds, bgPaint);

            using var framePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = selected ? FUIColors.Active : FUIColors.Frame,
                StrokeWidth = selected ? 2f : 1f
            };
            canvas.DrawRect(typeBounds, framePaint);

            FUIRenderer.DrawTextCentered(canvas, labels[i], typeBounds, textColor, 14f);
        }
    }

    private void DrawKeyCapture(SKCanvas canvas, SKRect bounds)
    {
        // Background
        var bgColor = _isCapturingKey
            ? FUIColors.Warning.WithAlpha(40)
            : (_keyCaptureBoundsHovered ? FUIColors.Primary.WithAlpha(30) : FUIColors.Background2);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        // Frame
        var frameColor = _isCapturingKey
            ? FUIColors.Warning
            : (_keyCaptureBoundsHovered ? FUIColors.Primary : FUIColors.Frame);
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = frameColor,
            StrokeWidth = _isCapturingKey ? 2f : 1f
        };
        canvas.DrawRect(bounds, framePaint);

        // Text content
        float textY = bounds.MidY + 5;
        if (_isCapturingKey)
        {
            byte alpha = (byte)(180 + MathF.Sin(_ctx.PulsePhase * 3) * 60);
            FUIRenderer.DrawText(canvas, "Press a key...",
                new SKPoint(bounds.Left + 10, textY), FUIColors.Warning.WithAlpha(alpha), 15f);
        }
        else if (!string.IsNullOrEmpty(_selectedKeyName))
        {
            FUIRenderer.DrawText(canvas, _selectedKeyName,
                new SKPoint(bounds.Left + 10, textY), FUIColors.TextBright, 15f);
        }
        else
        {
            FUIRenderer.DrawText(canvas, "Click to capture key",
                new SKPoint(bounds.Left + 10, textY), FUIColors.TextDisabled, 15f);
        }

        // Clear button if there's a key
        if (!string.IsNullOrEmpty(_selectedKeyName) && !_isCapturingKey)
        {
            _keyClearButtonBounds = new SKRect(bounds.Right - 28, bounds.Top + 6, bounds.Right - 6, bounds.Bottom - 6);
            DrawSmallIconButton(canvas, _keyClearButtonBounds, "×", _keyClearButtonHovered, true);
        }
        else
        {
            _keyClearButtonBounds = SKRect.Empty;
        }
    }

    private void DrawActionButton(SKCanvas canvas, SKRect bounds, string text, bool hovered, bool isPrimary)
        => FUIWidgets.DrawActionButton(canvas, bounds, text, hovered, isPrimary);

    private void DrawArrowButton(SKCanvas canvas, SKRect bounds, string arrow, bool hovered, bool enabled)
        => FUIWidgets.DrawArrowButton(canvas, bounds, arrow, hovered, enabled);

    private void DrawOutputMappingList(SKCanvas canvas, SKRect bounds)
    {
        _mappingRowBounds.Clear();
        _mappingAddButtonBounds.Clear();
        _mappingRemoveButtonBounds.Clear();

        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count)
        {
            FUIRenderer.DrawText(canvas, "No vJoy devices available",
                new SKPoint(bounds.Left + 20, bounds.Top + 20), FUIColors.TextDim, 15f);
            FUIRenderer.DrawText(canvas, "Install vJoy driver to create mappings",
                new SKPoint(bounds.Left + 20, bounds.Top + 40), FUIColors.TextDisabled, 14f);
            return;
        }

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        var profile = _ctx.ProfileManager.ActiveProfile;

        float rowHeight = 32f;
        float rowGap = 4f;
        float y = bounds.Top;
        int rowIndex = 0;

        // Section: AXES
        FUIRenderer.DrawText(canvas, "AXES", new SKPoint(bounds.Left + 5, y + 14), FUIColors.Active, 14f);
        y += 20;

        string[] axisNames = { "X Axis", "Y Axis", "Z Axis", "RX Axis", "RY Axis", "RZ Axis", "Slider 1", "Slider 2" };
        for (int i = 0; i < Math.Min(axisNames.Length, 8); i++)
        {
            if (y + rowHeight > bounds.Bottom) break;

            var rowBounds = new SKRect(bounds.Left, y, bounds.Right, y + rowHeight);
            string binding = GetAxisBindingText(profile, vjoyDevice.Id, i);
            bool isSelected = rowIndex == _selectedMappingRow;
            bool isHovered = rowIndex == _hoveredMappingRow;
            bool isEditing = _mappingEditorOpen && rowIndex == _editingRowIndex;

            DrawMappingRow(canvas, rowBounds, axisNames[i], binding, isSelected, isHovered, isEditing, rowIndex, !string.IsNullOrEmpty(binding) && binding != "—");

            _mappingRowBounds.Add(rowBounds);
            y += rowHeight + rowGap;
            rowIndex++;
        }

        // Section: BUTTONS
        y += 10;
        if (y + 20 < bounds.Bottom)
        {
            FUIRenderer.DrawText(canvas, "BUTTONS", new SKPoint(bounds.Left + 5, y + 14), FUIColors.Active, 14f);
            y += 20;
        }

        for (int i = 0; i < vjoyDevice.ButtonCount && y + rowHeight <= bounds.Bottom; i++)
        {
            var rowBounds = new SKRect(bounds.Left, y, bounds.Right, y + rowHeight);
            string binding = GetButtonBindingText(profile, vjoyDevice.Id, i);
            bool isSelected = rowIndex == _selectedMappingRow;
            bool isHovered = rowIndex == _hoveredMappingRow;
            bool isEditing = _mappingEditorOpen && rowIndex == _editingRowIndex;

            DrawMappingRow(canvas, rowBounds, $"Button {i + 1}", binding, isSelected, isHovered, isEditing, rowIndex, !string.IsNullOrEmpty(binding) && binding != "—");

            _mappingRowBounds.Add(rowBounds);
            y += rowHeight + rowGap;
            rowIndex++;
        }
    }

    private string GetAxisBindingText(MappingProfile? profile, uint vjoyId, int axisIndex)
    {
        if (profile is null) return "—";

        var mapping = profile.AxisMappings.FirstOrDefault(m =>
            m.Output.Type == OutputType.VJoyAxis &&
            m.Output.VJoyDevice == vjoyId &&
            m.Output.Index == axisIndex);

        if (mapping is null || mapping.Inputs.Count == 0) return "—";

        var input = mapping.Inputs[0];
        return $"{input.DeviceName} - Axis {input.Index}";
    }

    private string GetButtonBindingText(MappingProfile? profile, uint vjoyId, int buttonIndex)
    {
        if (profile is null) return "—";

        // Find mapping for this button slot (either VJoyButton or Keyboard output type)
        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyId &&
            m.Output.Index == buttonIndex);

        if (mapping is null || mapping.Inputs.Count == 0) return "—";

        var input = mapping.Inputs[0];
        if (input.Type == InputType.Button)
            return $"{input.DeviceName} - Button {input.Index + 1}";
        return $"{input.DeviceName} - {input.Type} {input.Index}";
    }

    private void DrawMappingRow(SKCanvas canvas, SKRect bounds, string outputName, string binding,
        bool isSelected, bool isHovered, bool isEditing, int rowIndex, bool hasBind)
    {
        // Background
        SKColor bgColor;
        if (isEditing)
            bgColor = FUIColors.Active.WithAlpha(60);
        else if (isSelected)
            bgColor = FUIColors.Active.WithAlpha(40);
        else if (isHovered)
            bgColor = FUIColors.Primary.WithAlpha(30);
        else
            bgColor = FUIColors.Background2.WithAlpha(60);

        using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, Color = bgColor };
        canvas.DrawRect(bounds, bgPaint);

        // Frame
        using var framePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = isEditing ? FUIColors.Active : (isSelected ? FUIColors.Active.WithAlpha(150) : (isHovered ? FUIColors.FrameBright : FUIColors.Frame.WithAlpha(80))),
            StrokeWidth = isEditing ? 2f : (isSelected ? 1.5f : 1f)
        };
        canvas.DrawRect(bounds, framePaint);

        // Output name (left)
        float textY = bounds.MidY + 5;
        FUIRenderer.DrawText(canvas, outputName, new SKPoint(bounds.Left + 10, textY),
            isEditing ? FUIColors.Active : FUIColors.TextPrimary, 15f);

        // Binding (center)
        float bindingX = bounds.Left + 100;
        var bindColor = binding == "—" ? FUIColors.TextDisabled : FUIColors.TextDim;
        FUIRenderer.DrawText(canvas, binding, new SKPoint(bindingX, textY), bindColor, 14f);

        // [+] button (Edit/Add)
        float buttonSize = 24f;
        float buttonY = bounds.MidY - buttonSize / 2;
        float addButtonX = bounds.Right - (hasBind ? 60 : 36);
        var addBounds = new SKRect(addButtonX, buttonY, addButtonX + buttonSize, buttonY + buttonSize);
        _mappingAddButtonBounds.Add(addBounds);

        bool addHovered = rowIndex == _hoveredAddButton;
        string addIcon = hasBind ? "✎" : "+";  // Pencil for edit, plus for add
        DrawSmallIconButton(canvas, addBounds, addIcon, addHovered);

        // [×] button (only if bound)
        if (hasBind)
        {
            float removeButtonX = bounds.Right - 32;
            var removeBounds = new SKRect(removeButtonX, buttonY, removeButtonX + buttonSize, buttonY + buttonSize);
            _mappingRemoveButtonBounds.Add(removeBounds);

            bool removeHovered = rowIndex == _hoveredRemoveButton;
            DrawSmallIconButton(canvas, removeBounds, "×", removeHovered, true);
        }
        else
        {
            _mappingRemoveButtonBounds.Add(SKRect.Empty);
        }
    }

    private void DrawSmallIconButton(SKCanvas canvas, SKRect bounds, string icon, bool hovered, bool isDanger = false)
        => FUIWidgets.DrawSmallIconButton(canvas, bounds, icon, hovered, isDanger);

    private void OpenMappingEditor(int rowIndex)
    {
        if (!_ctx.ProfileManager.HasActiveProfile)
        {
            _ctx.CreateNewProfilePrompt!();
            if (!_ctx.ProfileManager.HasActiveProfile) return;
        }
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;

        // Cancel any existing listening
        CancelInputListening();

        _mappingEditorOpen = true;
        _editingRowIndex = rowIndex;
        _selectedMappingRow = rowIndex;
        _isEditingAxis = rowIndex < 8;
        _pendingInput = null;
        _manualEntryMode = false;
        _selectedButtonMode = ButtonMode.Normal;
        _selectedSourceDevice = 0;
        _selectedSourceControl = 0;

        // Load existing binding if present
        LoadExistingBinding(rowIndex);
    }

    private void LoadExistingBinding(int rowIndex)
    {
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        bool isAxis = rowIndex < 8;
        int outputIndex = isAxis ? rowIndex : rowIndex - 8;

        if (isAxis)
        {
            var mapping = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (mapping is not null && mapping.Inputs.Count > 0)
            {
                var input = mapping.Inputs[0];
                _pendingInput = new DetectedInput
                {
                    DeviceGuid = Guid.TryParse(input.DeviceId, out var guid) ? guid : Guid.Empty,
                    DeviceName = input.DeviceName,
                    Type = input.Type,
                    Index = input.Index,
                    Value = 0
                };

                // Set selected device in dropdown
                for (int i = 0; i < _ctx.Devices.Count; i++)
                {
                    if (_ctx.Devices[i].InstanceGuid.ToString() == input.DeviceId)
                    {
                        _selectedSourceDevice = i;
                        break;
                    }
                }
                _selectedSourceControl = input.Index;
            }
        }
        else
        {
            var mapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyButton &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (mapping is not null && mapping.Inputs.Count > 0)
            {
                var input = mapping.Inputs[0];
                _pendingInput = new DetectedInput
                {
                    DeviceGuid = Guid.TryParse(input.DeviceId, out var guid) ? guid : Guid.Empty,
                    DeviceName = input.DeviceName,
                    Type = input.Type,
                    Index = input.Index,
                    Value = 0
                };
                _selectedButtonMode = mapping.Mode;

                // Set selected device in dropdown
                for (int i = 0; i < _ctx.Devices.Count; i++)
                {
                    if (_ctx.Devices[i].InstanceGuid.ToString() == input.DeviceId)
                    {
                        _selectedSourceDevice = i;
                        break;
                    }
                }
                _selectedSourceControl = input.Index;
            }
        }
    }

    private void CloseMappingEditor()
    {
        CancelInputListening();
        _mappingEditorOpen = false;
        _editingRowIndex = -1;
        _pendingInput = null;
        _deviceDropdownOpen = false;
        _controlDropdownOpen = false;
    }

    /// <summary>
    /// Starts listening for input. Fire-and-forget from UI.
    /// All exceptions are handled internally.
    /// </summary>
    private void StartListeningForInput()
    {
        // Fire-and-forget async operation with internal exception handling
        _ = StartListeningForInputAsync();
    }

    private async Task StartListeningForInputAsync()
    {
        if (_isListeningForInput) return;
        if (!_mappingEditorOpen) return;

        _isListeningForInput = true;
        _inputListeningStartTime = DateTime.Now;
        _pendingInput = null;

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
                _pendingInput = detected;

                // Update manual entry dropdowns to match detected input
                PhysicalDeviceInfo? sourceDevice = null;
                for (int i = 0; i < _ctx.Devices.Count; i++)
                {
                    if (_ctx.Devices[i].InstanceGuid == detected.DeviceGuid)
                    {
                        _selectedSourceDevice = i;
                        sourceDevice = _ctx.Devices[i];
                        break;
                    }
                }
                _selectedSourceControl = detected.Index;

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
            _isListeningForInput = false;
        }
    }

    private void SaveMapping()
    {
        if (!_mappingEditorOpen || _pendingInput is null) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int outputIndex = _isEditingAxis ? _editingRowIndex : _editingRowIndex - 8;

        // Remove existing binding
        RemoveBindingAtRow(_editingRowIndex, save: false);

        if (_isEditingAxis)
        {
            var mapping = new AxisMapping
            {
                Name = $"{_pendingInput.DeviceName} Axis {_pendingInput.Index} -> vJoy {vjoyDevice.Id} Axis {outputIndex}",
                Inputs = new List<InputSource> { _pendingInput.ToInputSource() },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyAxis,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                },
                Curve = new AxisCurve()
            };
            profile.AxisMappings.Add(mapping);
        }
        else
        {
            var mapping = new ButtonMapping
            {
                Name = $"{_pendingInput.DeviceName} Button {_pendingInput.Index + 1} -> vJoy {vjoyDevice.Id} Button {outputIndex + 1}",
                Inputs = new List<InputSource> { _pendingInput.ToInputSource() },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyButton,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                },
                Mode = _selectedButtonMode
            };
            profile.ButtonMappings.Add(mapping);
        }

        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.OnMappingsChanged();
        CloseMappingEditor();
    }

    private void CreateBindingFromManualEntry()
    {
        if (!_manualEntryMode || _ctx.Devices.Count == 0 || _selectedSourceDevice >= _ctx.Devices.Count) return;

        var device = _ctx.Devices[_selectedSourceDevice];
        _pendingInput = new DetectedInput
        {
            DeviceGuid = device.InstanceGuid,
            DeviceName = device.Name,
            Type = _isEditingAxis ? InputType.Axis : InputType.Button,
            Index = _selectedSourceControl,
            Value = 0
        };
    }

    /// <summary>
    /// Create 1:1 mappings from the selected physical device to a user-selected vJoy device.
    /// Maps all axes, buttons, and hats directly without any curves or modifications.
    /// </summary>
    private void CreateOneToOneMappings()
    {
        // Validate selection
        if (_ctx.SelectedDevice < 0 || _ctx.SelectedDevice >= _ctx.Devices.Count) return;

        var physicalDevice = _ctx.Devices[_ctx.SelectedDevice];
        if (physicalDevice.IsVirtual) return; // Only map physical devices

        // Ensure vJoy devices are loaded
        if (_ctx.VJoyDevices.Count == 0)
        {
            _ctx.VJoyDevices = _ctx.VJoyService.EnumerateDevices();
        }

        // Check if we have any vJoy devices
        if (_ctx.VJoyDevices.Count == 0)
        {
            ShowVJoyConfigurationHelp(physicalDevice, noDevices: true);
            return;
        }

        // Show vJoy device selection dialog
        var vjoyDevice = ShowVJoyDeviceSelectionDialog(physicalDevice);
        if (vjoyDevice is null) return; // User cancelled

        // Update selected vJoy device index
        _ctx.SelectedVJoyDeviceIndex = _ctx.VJoyDevices.IndexOf(vjoyDevice);

        // Ensure we have an active profile
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null)
        {
            profile = _ctx.ProfileManager.CreateAndActivateProfile($"1:1 - {physicalDevice.Name}");
        }

        // Build device ID for InputSource (using GUID)
        string deviceId = physicalDevice.InstanceGuid.ToString();

        // Remove any existing mappings from this device to this vJoy device
        profile.AxisMappings.RemoveAll(m =>
            m.Inputs.Any(i => i.DeviceId == deviceId) &&
            m.Output.VJoyDevice == vjoyDevice.Id);
        profile.ButtonMappings.RemoveAll(m =>
            m.Inputs.Any(i => i.DeviceId == deviceId) &&
            m.Output.VJoyDevice == vjoyDevice.Id);
        profile.HatMappings.RemoveAll(m =>
            m.Inputs.Any(i => i.DeviceId == deviceId) &&
            m.Output.VJoyDevice == vjoyDevice.Id);

        // Create axis mappings using simple sequential mapping
        // Maps physical axis 0 -> vJoy axis 0, axis 1 -> vJoy axis 1, etc.
        // This is predictable and consistent with manual mapping behavior.
        var vjoyAxisIndices = GetVJoyAxisIndices(vjoyDevice);

        LogMapping($"=== 1:1 Mapping for {physicalDevice.Name} ===");
        LogMapping($"Device: {physicalDevice.Name}, AxisCount: {physicalDevice.AxisCount}");

        int axesToMap = Math.Min(physicalDevice.AxisCount, vjoyAxisIndices.Count);
        for (int i = 0; i < axesToMap; i++)
        {
            int vjoyAxisIndex = vjoyAxisIndices[i];
            string vjoyAxisName = GetVJoyAxisName(vjoyAxisIndex);
            string physicalAxisName = $"Axis {i}";

            LogMapping($"Mapping axis {i} -> vJoy axis {vjoyAxisIndex} ({vjoyAxisName})");

            var mapping = new AxisMapping
            {
                Name = $"{physicalDevice.Name} {physicalAxisName} -> vJoy {vjoyDevice.Id} {vjoyAxisName}",
                Inputs = new List<InputSource>
                {
                    new InputSource
                    {
                        DeviceId = deviceId,
                        DeviceName = physicalDevice.Name,
                        Type = InputType.Axis,
                        Index = i
                    }
                },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyAxis,
                    VJoyDevice = vjoyDevice.Id,
                    Index = vjoyAxisIndex
                },
                Curve = new AxisCurve { Type = CurveType.Linear }
            };
            profile.AxisMappings.Add(mapping);
        }

        // Create button mappings
        int buttonsToMap = Math.Min(physicalDevice.ButtonCount, vjoyDevice.ButtonCount);

        for (int i = 0; i < buttonsToMap; i++)
        {
            var mapping = new ButtonMapping
            {
                Name = $"{physicalDevice.Name} Btn {i + 1} -> vJoy {vjoyDevice.Id} Btn {i + 1}",
                Inputs = new List<InputSource>
                {
                    new InputSource
                    {
                        DeviceId = deviceId,
                        DeviceName = physicalDevice.Name,
                        Type = InputType.Button,
                        Index = i
                    }
                },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyButton,
                    VJoyDevice = vjoyDevice.Id,
                    Index = i
                },
                Mode = ButtonMode.Normal
            };
            profile.ButtonMappings.Add(mapping);
        }

        // Create hat/POV mappings
        int hatsToMap = Math.Min(physicalDevice.HatCount, vjoyDevice.ContPovCount + vjoyDevice.DiscPovCount);

        for (int i = 0; i < hatsToMap; i++)
        {
            var mapping = new HatMapping
            {
                Name = $"{physicalDevice.Name} Hat {i} -> vJoy {vjoyDevice.Id} POV {i}",
                Inputs = new List<InputSource>
                {
                    new InputSource
                    {
                        DeviceId = deviceId,
                        DeviceName = physicalDevice.Name,
                        Type = InputType.Hat,
                        Index = i
                    }
                },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyPov,
                    VJoyDevice = vjoyDevice.Id,
                    Index = i
                },
                UseContinuous = vjoyDevice.ContPovCount > i
            };
            profile.HatMappings.Add(mapping);
        }

        // Save the profile
        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.OnMappingsChanged();

        // Refresh profiles list
        _ctx.Profiles = _ctx.ProfileRepository.ListProfiles();

        // Note: Tab switch to Mappings is handled by the caller (DevicesTabController)
        _ctx.InvalidateCanvas();
    }

    /// <summary>
    /// Count the number of axes configured on a vJoy device
    /// </summary>
    private int CountVJoyAxes(VJoyDeviceInfo vjoy)
    {
        int count = 0;
        if (vjoy.HasAxisX) count++;
        if (vjoy.HasAxisY) count++;
        if (vjoy.HasAxisZ) count++;
        if (vjoy.HasAxisRX) count++;
        if (vjoy.HasAxisRY) count++;
        if (vjoy.HasAxisRZ) count++;
        if (vjoy.HasSlider0) count++;
        if (vjoy.HasSlider1) count++;
        return count;
    }

    /// <summary>
    /// Get the list of available vJoy axis indices in standard order.
    /// Returns indices 0-7 corresponding to X, Y, Z, RX, RY, RZ, Slider0, Slider1.
    /// </summary>
    private List<int> GetVJoyAxisIndices(VJoyDeviceInfo vjoy)
    {
        var indices = new List<int>();
        if (vjoy.HasAxisX) indices.Add(0);   // X
        if (vjoy.HasAxisY) indices.Add(1);   // Y
        if (vjoy.HasAxisZ) indices.Add(2);   // Z
        if (vjoy.HasAxisRX) indices.Add(3);  // RX
        if (vjoy.HasAxisRY) indices.Add(4);  // RY
        if (vjoy.HasAxisRZ) indices.Add(5);  // RZ
        if (vjoy.HasSlider0) indices.Add(6); // Slider0
        if (vjoy.HasSlider1) indices.Add(7); // Slider1
        return indices;
    }

    /// <summary>
    /// Get a human-readable name for a vJoy axis index.
    /// </summary>
    private string GetVJoyAxisName(int index)
    {
        return index switch
        {
            0 => "X",
            1 => "Y",
            2 => "Z",
            3 => "RX",
            4 => "RY",
            5 => "RZ",
            6 => "Slider1",
            7 => "Slider2",
            _ => $"Axis{index}"
        };
    }

    /// <summary>
    /// Find the best vJoy device that can accommodate all controls from the physical device
    /// </summary>
    private VJoyDeviceInfo? FindBestVJoyDevice(PhysicalDeviceInfo physical)
    {
        VJoyDeviceInfo? best = null;
        int bestScore = -1;

        foreach (var vjoy in _ctx.VJoyDevices)
        {
            int axes = CountVJoyAxes(vjoy);
            int buttons = vjoy.ButtonCount;
            int povs = vjoy.ContPovCount + vjoy.DiscPovCount;

            // Check if this vJoy can accommodate all controls
            if (axes >= physical.AxisCount &&
                buttons >= physical.ButtonCount &&
                povs >= physical.HatCount)
            {
                // Score based on how close the match is (lower excess = better)
                int excess = (axes - physical.AxisCount) +
                            (buttons - physical.ButtonCount) +
                            (povs - physical.HatCount);
                int score = 1000 - excess; // Higher score = better match

                if (score > bestScore)
                {
                    bestScore = score;
                    best = vjoy;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Show help dialog for vJoy configuration with recommended settings
    /// </summary>
    private void ShowVJoyConfigurationHelp(PhysicalDeviceInfo physical, bool noDevices)
    {
        string message;
        if (noDevices)
        {
            message = "No vJoy devices are configured.\n\n";
        }
        else
        {
            message = "No vJoy device has enough capacity for this physical device.\n\n";
        }

        message += $"To create a 1:1 mapping for {physical.Name}, configure a vJoy device with:\n\n" +
                   $"  Axes: {physical.AxisCount} (X, Y, Z, Rx, Ry, Rz, Slider, Dial)\n" +
                   $"  Buttons: {physical.ButtonCount}\n" +
                   $"  POV Hats: {physical.HatCount} (Continuous recommended)\n\n" +
                   "Would you like to open the vJoy Configuration utility?";

        var result = FUIMessageBox.ShowQuestion(_ctx.OwnerForm, message, "vJoy Configuration Required");

        if (result)
        {
            LaunchVJoyConfigurator();
        }
    }

    /// <summary>
    /// Attempt to launch the vJoy configuration utility
    /// </summary>
    private void LaunchVJoyConfigurator()
    {
        // Common vJoy installation paths
        string[] possiblePaths = new[]
        {
            @"C:\Program Files\vJoy\x64\vJoyConf.exe",
            @"C:\Program Files\vJoy\x86\vJoyConf.exe",
            @"C:\Program Files (x86)\vJoy\x64\vJoyConf.exe",
            @"C:\Program Files (x86)\vJoy\x86\vJoyConf.exe"
        };

        string? vjoyConfPath = possiblePaths.FirstOrDefault(File.Exists);

        if (vjoyConfPath is not null)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = vjoyConfPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
            {
                FUIMessageBox.ShowError(_ctx.OwnerForm,
                    $"Failed to launch vJoy Configurator:\n{ex.Message}",
                    "Launch Failed");
            }
        }
        else
        {
            FUIMessageBox.ShowWarning(_ctx.OwnerForm,
                "vJoy Configuration utility (vJoyConf.exe) was not found.\n\n" +
                "Please install vJoy from:\nhttps://github.com/jshafer817/vJoy/releases\n\n" +
                "Or manually run vJoyConf.exe from your vJoy installation folder.",
                "vJoy Not Found");
        }
    }

    /// <summary>
    /// Show a dialog to select a vJoy device for 1:1 mapping.
    /// Returns the selected device or null if cancelled.
    /// </summary>
    private VJoyDeviceInfo? ShowVJoyDeviceSelectionDialog(PhysicalDeviceInfo physicalDevice)
    {
        var items = new List<FUISelectionDialog.SelectionItem>();

        // Add vJoy devices to list
        foreach (var vjoy in _ctx.VJoyDevices)
        {
            int axes = CountVJoyAxes(vjoy);
            int buttons = vjoy.ButtonCount;
            int povs = vjoy.ContPovCount + vjoy.DiscPovCount;

            string status;
            if (axes >= physicalDevice.AxisCount &&
                buttons >= physicalDevice.ButtonCount &&
                povs >= physicalDevice.HatCount)
            {
                status = "[OK]";
            }
            else
            {
                status = "[partial]";
            }

            items.Add(new FUISelectionDialog.SelectionItem
            {
                Text = $"vJoy #{vjoy.Id}: {axes} axes, {buttons} buttons, {povs} POVs",
                Status = status,
                Tag = vjoy
            });
        }

        // Add option to configure new vJoy device
        items.Add(new FUISelectionDialog.SelectionItem
        {
            Text = "+ Configure new vJoy device...",
            IsAction = true
        });

        string description = $"Select a vJoy device to map {physicalDevice.Name}:\n" +
                           $"({physicalDevice.AxisCount} axes, {physicalDevice.ButtonCount} buttons, {physicalDevice.HatCount} hats)";

        int selectedIndex = FUISelectionDialog.Show(_ctx.OwnerForm, "Select vJoy Device", description, items, "Map 1:1", "Cancel");

        if (selectedIndex < 0)
            return null;

        // Check if user selected "Configure new vJoy device"
        if (selectedIndex == _ctx.VJoyDevices.Count)
        {
            ShowVJoyConfigurationHelp(physicalDevice, noDevices: false);
            return null;
        }

        if (selectedIndex >= 0 && selectedIndex < _ctx.VJoyDevices.Count)
        {
            var selectedVJoy = _ctx.VJoyDevices[selectedIndex];

            // Warn about partial mappings if necessary
            int axes = CountVJoyAxes(selectedVJoy);
            int buttons = selectedVJoy.ButtonCount;
            int povs = selectedVJoy.ContPovCount + selectedVJoy.DiscPovCount;

            if (axes < physicalDevice.AxisCount ||
                buttons < physicalDevice.ButtonCount ||
                povs < physicalDevice.HatCount)
            {
                var result = FUIMessageBox.ShowQuestion(_ctx.OwnerForm,
                    $"vJoy #{selectedVJoy.Id} doesn't have enough capacity.\n\n" +
                    $"Physical device: {physicalDevice.AxisCount} axes, {physicalDevice.ButtonCount} buttons, {physicalDevice.HatCount} hats\n" +
                    $"vJoy #{selectedVJoy.Id}: {axes} axes, {buttons} buttons, {povs} POVs\n\n" +
                    "Some controls will not be mapped. Continue?",
                    "Partial Mapping");

                if (!result)
                    return null;
            }

            return selectedVJoy;
        }

        return null;
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private static void LogMapping(string message)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Asteriq", "axis_types.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] [Mapping] {message}\n");
    }

    /// <summary>
    /// Clear all mappings for the selected physical device.
    /// </summary>
    private void ClearDeviceMappings()
    {
        if (_ctx.SelectedDevice < 0 || _ctx.SelectedDevice >= _ctx.Devices.Count) return;

        var physicalDevice = _ctx.Devices[_ctx.SelectedDevice];
        if (physicalDevice.IsVirtual) return;

        var result = FUIMessageBox.ShowQuestion(_ctx.OwnerForm,
            $"Remove all mappings for {physicalDevice.Name}?\n\nThis will remove axis, button, and hat mappings from all vJoy devices.",
            "Clear Mappings");

        if (!result) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        string deviceId = physicalDevice.InstanceGuid.ToString();

        // Remove all mappings from this device
        int axisRemoved = profile.AxisMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));
        int buttonRemoved = profile.ButtonMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));
        int hatRemoved = profile.HatMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));

        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.OnMappingsChanged();

        FUIMessageBox.ShowInfo(_ctx.OwnerForm,
            $"Removed {axisRemoved} axis, {buttonRemoved} button, and {hatRemoved} hat mappings.",
            "Mappings Cleared");

        _ctx.InvalidateCanvas();
    }

    /// <summary>
    /// Remove a disconnected device completely from the app's data.
    /// This clears all mappings and removes it from the disconnected devices list.
    /// </summary>
    private void RemoveDisconnectedDevice()
    {
        if (_ctx.SelectedDevice < 0 || _ctx.SelectedDevice >= _ctx.Devices.Count) return;

        var device = _ctx.Devices[_ctx.SelectedDevice];
        if (device.IsConnected || device.IsVirtual) return; // Only works for disconnected physical devices

        var result = FUIMessageBox.ShowQuestion(_ctx.OwnerForm,
            $"Permanently remove {device.Name}?\n\n" +
            "This will:\n" +
            "• Clear all axis, button, and hat mappings\n" +
            "• Remove the device from the disconnected list\n\n" +
            "This cannot be undone.",
            "Remove Device");

        if (!result) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        string deviceId = device.InstanceGuid.ToString();

        // Remove all mappings from this device
        int axisRemoved = 0, buttonRemoved = 0, hatRemoved = 0;
        if (profile is not null)
        {
            axisRemoved = profile.AxisMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));
            buttonRemoved = profile.ButtonMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));
            hatRemoved = profile.HatMappings.RemoveAll(m => m.Inputs.Any(i => i.DeviceId == deviceId));
            _ctx.ProfileManager.SaveActiveProfile();
            _ctx.OnMappingsChanged();
        }

        // Remove from disconnected devices list
        _ctx.DisconnectedDevices.RemoveAll(d => d.InstanceGuid == device.InstanceGuid);
        _ctx.SaveDisconnectedDevices!();

        // Refresh and update selection
        _ctx.RefreshDevices();
        if (_ctx.SelectedDevice >= _ctx.Devices.Count)
        {
            _ctx.SelectedDevice = Math.Max(0, _ctx.Devices.Count - 1);
        }

        FUIMessageBox.ShowInfo(_ctx.OwnerForm,
            $"Device removed.\n\nCleared {axisRemoved} axis, {buttonRemoved} button, and {hatRemoved} hat mappings.",
            "Device Removed");

        _ctx.InvalidateCanvas();
    }

    private void CreateBindingForRow(int rowIndex, DetectedInput input)
    {
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        // Use current mapping category to determine axis vs button
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        // rowIndex is already the correct index within the current category
        int outputIndex = rowIndex;

        // Remove existing binding for this output
        RemoveBindingAtRow(rowIndex, save: false);

        if (isAxis)
        {
            var mapping = new AxisMapping
            {
                Name = $"{input.DeviceName} Axis {input.Index} -> vJoy {vjoyDevice.Id} Axis {outputIndex}",
                Inputs = new List<InputSource> { input.ToInputSource() },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyAxis,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                },
                Curve = new AxisCurve()
            };
            profile.AxisMappings.Add(mapping);
        }
        else
        {
            var mapping = new ButtonMapping
            {
                Name = $"{input.DeviceName} Button {input.Index + 1} -> vJoy {vjoyDevice.Id} Button {outputIndex + 1}",
                Inputs = new List<InputSource> { input.ToInputSource() },
                Output = new OutputTarget
                {
                    Type = OutputType.VJoyButton,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                },
                Mode = ButtonMode.Normal
            };
            profile.ButtonMappings.Add(mapping);
        }

        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.OnMappingsChanged();
    }

    private void RemoveBindingAtRow(int rowIndex, bool save = true)
    {
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        // Use current mapping category to determine axis vs button
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        // rowIndex is already the correct index within the current category
        int outputIndex = rowIndex;

        if (isAxis)
        {
            var existing = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);
            if (existing is not null)
            {
                profile.AxisMappings.Remove(existing);
            }
        }
        else
        {
            var existing = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyButton &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);
            if (existing is not null)
            {
                profile.ButtonMappings.Remove(existing);
            }
        }

        if (save)
        {
            _ctx.ProfileManager.SaveActiveProfile();
            _ctx.OnMappingsChanged();
        }
    }

    private void CancelInputListening()
    {
        if (_isListeningForInput)
        {
            _inputDetectionService?.Cancel();
            _isListeningForInput = false;
        }
    }

    /// <summary>
    /// Check if a physical input is already mapped anywhere in the profile.
    /// Returns the mapping name if found, null otherwise.
    /// </summary>
    private string? FindExistingMappingForInput(MappingProfile profile, InputSource inputToCheck)
    {
        // Check axis mappings
        foreach (var mapping in profile.AxisMappings)
        {
            foreach (var input in mapping.Inputs)
            {
                if (input.DeviceId == inputToCheck.DeviceId &&
                    input.Type == inputToCheck.Type &&
                    input.Index == inputToCheck.Index)
                {
                    return mapping.Name;
                }
            }
        }

        // Check button mappings
        foreach (var mapping in profile.ButtonMappings)
        {
            foreach (var input in mapping.Inputs)
            {
                if (input.DeviceId == inputToCheck.DeviceId &&
                    input.Type == inputToCheck.Type &&
                    input.Index == inputToCheck.Index)
                {
                    return mapping.Name;
                }
            }
        }

        // Check hat mappings
        foreach (var mapping in profile.HatMappings)
        {
            foreach (var input in mapping.Inputs)
            {
                if (input.DeviceId == inputToCheck.DeviceId &&
                    input.Type == inputToCheck.Type &&
                    input.Index == inputToCheck.Index)
                {
                    return mapping.Name;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Show a confirmation dialog when a duplicate mapping is detected.
    /// Returns true if the user wants to proceed and replace the existing mapping.
    /// </summary>
    private bool ConfirmDuplicateMapping(string existingMappingName, string newMappingTarget)
    {
        using var dialog = new FUIConfirmDialog(
            "Duplicate Mapping",
            $"This input is already mapped to:\n\n{existingMappingName}\n\nRemove existing and create new mapping for {newMappingTarget}?",
            "Replace",
            "Cancel");
        return dialog.ShowDialog(_ctx.OwnerForm) == DialogResult.Yes;
    }

    /// <summary>
    /// Remove any existing mappings that use the specified input source.
    /// </summary>
    private void RemoveExistingMappingsForInput(MappingProfile profile, InputSource inputToRemove)
    {
        // Remove from axis mappings
        foreach (var mapping in profile.AxisMappings.ToList())
        {
            mapping.Inputs.RemoveAll(i =>
                i.DeviceId == inputToRemove.DeviceId &&
                i.Type == inputToRemove.Type &&
                i.Index == inputToRemove.Index);

            // If no inputs remain, remove the mapping entirely
            if (mapping.Inputs.Count == 0)
            {
                profile.AxisMappings.Remove(mapping);
            }
        }

        // Remove from button mappings
        foreach (var mapping in profile.ButtonMappings.ToList())
        {
            mapping.Inputs.RemoveAll(i =>
                i.DeviceId == inputToRemove.DeviceId &&
                i.Type == inputToRemove.Type &&
                i.Index == inputToRemove.Index);

            if (mapping.Inputs.Count == 0)
            {
                profile.ButtonMappings.Remove(mapping);
            }
        }

        // Remove from hat mappings
        foreach (var mapping in profile.HatMappings.ToList())
        {
            mapping.Inputs.RemoveAll(i =>
                i.DeviceId == inputToRemove.DeviceId &&
                i.Type == inputToRemove.Type &&
                i.Index == inputToRemove.Index);

            if (mapping.Inputs.Count == 0)
            {
                profile.HatMappings.Remove(mapping);
            }
        }
    }

    /// <summary>
    /// Starts listening for input on a specific row. Fire-and-forget from UI.
    /// All exceptions are handled internally.
    /// </summary>
    private void StartInputListening(int rowIndex)
    {
        // Fire-and-forget async operation with internal exception handling
        _ = StartInputListeningAsync(rowIndex);
    }

    private async Task StartInputListeningAsync(int rowIndex)
    {
        if (_isListeningForInput) return;
        if (rowIndex < 0) return;

        _isListeningForInput = true;
        _inputListeningStartTime = DateTime.Now;
        _pendingInput = null;

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
                _pendingInput = detected;
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
                        string newTarget = isAxis ? $"vJoy Axis {targetRowIndex}" : $"vJoy Button {targetRowIndex + 1}";
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
            _isListeningForInput = false;
        }
    }

    /// <summary>
    /// Start input listening when user has assigned a keyboard key to an empty button slot.
    /// When physical input is detected, creates a new mapping with the pending keyboard output.
    /// </summary>
    private async Task StartPendingKeyboardInputListeningAsync()
    {
        if (_isListeningForInput) return;
        if (_pendingKeyboardKey is null) return;

        _isListeningForInput = true;
        _inputListeningStartTime = DateTime.Now;
        _pendingInput = null;

        _inputDetectionService ??= new InputDetectionService(_ctx.InputService);

        try
        {
            // Small delay to let user release any currently pressed buttons
            await Task.Delay(200);

            var detected = await _inputDetectionService.WaitForInputAsync(InputDetectionFilter.Buttons, 0.15f, 15000);

            if (detected is not null && _pendingKeyboardKey is not null)
            {
                var profile = _ctx.ProfileManager.ActiveProfile;
                if (profile is null) return;

                var newInputSource = detected.ToInputSource();

                // Check for duplicate mapping
                var existingMapping = FindExistingMappingForInput(profile, newInputSource);
                if (existingMapping is not null)
                {
                    string newTarget = $"Keyboard: {FormatKeyComboForDisplay(_pendingKeyboardKey, _pendingKeyboardModifiers)}";
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
                    Name = $"{detected.DeviceName} Button {detected.Index + 1} -> {FormatKeyComboForDisplay(_pendingKeyboardKey, _pendingKeyboardModifiers)}",
                    Inputs = new List<InputSource> { newInputSource },
                    Output = new OutputTarget
                    {
                        Type = OutputType.Keyboard,
                        VJoyDevice = _pendingKeyboardVJoyDevice,
                        Index = _pendingKeyboardOutputIndex,
                        KeyName = _pendingKeyboardKey,
                        Modifiers = _pendingKeyboardModifiers
                    },
                    Mode = _selectedButtonMode
                };
                profile.ButtonMappings.Add(mapping);
                profile.ModifiedAt = DateTime.UtcNow;
                _ctx.ProfileManager.SaveActiveProfile();
                _ctx.OnMappingsChanged();

                // Update the pending input so UI can show it
                _pendingInput = detected;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"[MainForm] Pending keyboard input listening cancelled or failed: {ex.Message}");
        }
        finally
        {
            _isListeningForInput = false;
            ClearPendingKeyboardState();
        }
    }

    private void ClearPendingKeyboardState()
    {
        _pendingKeyboardKey = null;
        _pendingKeyboardModifiers = null;
        _pendingKeyboardOutputIndex = -1;
        _pendingKeyboardVJoyDevice = 0;
    }

    private void SaveMappingForRow(int rowIndex, DetectedInput input, bool isAxis)
    {
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        // rowIndex is already the correct index within the current category (axes or buttons)
        int outputIndex = rowIndex;
        var newInputSource = input.ToInputSource();

        if (isAxis)
        {
            // Find existing mapping or create new one
            var existingMapping = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (existingMapping is not null)
            {
                // Add input to existing mapping (support multiple inputs)
                existingMapping.Inputs.Add(newInputSource);
                existingMapping.Name = $"vJoy {vjoyDevice.Id} Axis {outputIndex} ({existingMapping.Inputs.Count} inputs)";
            }
            else
            {
                // Create new mapping
                var mapping = new AxisMapping
                {
                    Name = $"{input.DeviceName} Axis {input.Index} -> vJoy {vjoyDevice.Id} Axis {outputIndex}",
                    Inputs = new List<InputSource> { newInputSource },
                    Output = new OutputTarget
                    {
                        Type = OutputType.VJoyAxis,
                        VJoyDevice = vjoyDevice.Id,
                        Index = outputIndex
                    },
                    Curve = new AxisCurve()
                };
                profile.AxisMappings.Add(mapping);
            }
        }
        else
        {
            // Find existing mapping for this button slot (regardless of output type)
            var existingMapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (existingMapping is not null)
            {
                // Add input to existing mapping (support multiple inputs)
                existingMapping.Inputs.Add(newInputSource);

                // Update with current panel settings
                existingMapping.Output.Type = _outputTypeIsKeyboard ? OutputType.Keyboard : OutputType.VJoyButton;
                if (_outputTypeIsKeyboard)
                {
                    existingMapping.Output.KeyName = _selectedKeyName;
                    existingMapping.Output.Modifiers = _selectedModifiers?.ToList();
                }
                else
                {
                    existingMapping.Output.KeyName = null;
                    existingMapping.Output.Modifiers = null;
                }
                existingMapping.Mode = _selectedButtonMode;
                existingMapping.Name = $"vJoy {vjoyDevice.Id} Button {outputIndex + 1} ({existingMapping.Inputs.Count} inputs)";
            }
            else
            {
                // Create new mapping using current panel settings
                var outputType = _outputTypeIsKeyboard ? OutputType.Keyboard : OutputType.VJoyButton;
                var outputTarget = new OutputTarget
                {
                    Type = outputType,
                    VJoyDevice = vjoyDevice.Id,
                    Index = outputIndex
                };

                if (_outputTypeIsKeyboard)
                {
                    outputTarget.KeyName = _selectedKeyName;
                    outputTarget.Modifiers = _selectedModifiers?.ToList();
                }

                string mappingName = _outputTypeIsKeyboard && !string.IsNullOrEmpty(_selectedKeyName)
                    ? $"{input.DeviceName} Button {input.Index + 1} -> {FormatKeyComboForDisplay(_selectedKeyName, _selectedModifiers)}"
                    : $"{input.DeviceName} Button {input.Index + 1} -> vJoy {vjoyDevice.Id} Button {outputIndex + 1}";

                var mapping = new ButtonMapping
                {
                    Name = mappingName,
                    Inputs = new List<InputSource> { newInputSource },
                    Output = outputTarget,
                    Mode = _selectedButtonMode
                };
                profile.ButtonMappings.Add(mapping);
            }
        }

        profile.ModifiedAt = DateTime.UtcNow;
        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.OnMappingsChanged();
        _pendingInput = null;
    }

    private void RemoveInputSourceAtIndex(int inputIndex)
    {
        if (_selectedMappingRow < 0) return;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        // Category 0 = Buttons, Category 1 = Axes
        bool isAxis = _mappingCategory == 1;
        int outputIndex = _selectedMappingRow;

        if (isAxis)
        {
            var mapping = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyAxis &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (mapping is not null && inputIndex >= 0 && inputIndex < mapping.Inputs.Count)
            {
                mapping.Inputs.RemoveAt(inputIndex);
                if (mapping.Inputs.Count == 0)
                {
                    // Remove the entire mapping if no inputs left
                    profile.AxisMappings.Remove(mapping);
                }
            }
        }
        else
        {
            var mapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.Type == OutputType.VJoyButton &&
                m.Output.VJoyDevice == vjoyDevice.Id &&
                m.Output.Index == outputIndex);

            if (mapping is not null && inputIndex >= 0 && inputIndex < mapping.Inputs.Count)
            {
                mapping.Inputs.RemoveAt(inputIndex);
                if (mapping.Inputs.Count == 0)
                {
                    // Remove the entire mapping if no inputs left
                    profile.ButtonMappings.Remove(mapping);
                }
            }
        }

        profile.ModifiedAt = DateTime.UtcNow;
        _ctx.ProfileManager.SaveActiveProfile();
        _ctx.OnMappingsChanged();
    }

    private void UpdateMergeOperationForSelected(int mergeOpIndex)
    {
        var axisMapping = GetCurrentAxisMapping();
        if (axisMapping is null) return;

        MergeOperation[] ops = { MergeOperation.Average, MergeOperation.Maximum, MergeOperation.Minimum, MergeOperation.Sum };
        if (mergeOpIndex < 0 || mergeOpIndex >= ops.Length) return;

        axisMapping.MergeOp = ops[mergeOpIndex];

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is not null)
        {
            profile.ModifiedAt = DateTime.UtcNow;
            _ctx.ProfileManager.SaveActiveProfile();
        }
        _ctx.OnMappingsChanged();
    }

    private void LoadAxisSettingsForRow()
    {
        // Reset to defaults
        _selectedCurveType = CurveType.Linear;
        _curveControlPoints = new List<SKPoint> { new(0, 0), new(1, 1) };
        _curveSymmetrical = false;
        _deadzoneMin = -1.0f;
        _deadzoneCenterMin = 0.0f;
        _deadzoneCenterMax = 0.0f;
        _deadzoneMax = 1.0f;
        _deadzoneCenterEnabled = false;
        _axisInverted = false;

        // Only for axis category
        if (_mappingCategory != 1) return;
        if (_selectedMappingRow < 0) return;

        var axisMapping = GetCurrentAxisMapping();
        if (axisMapping is null) return;

        // Load curve settings from mapping
        var curve = axisMapping.Curve;
        _selectedCurveType = curve.Type;
        _curveSymmetrical = curve.Symmetrical;
        _axisInverted = axisMapping.Invert;

        // Load deadzone settings
        _deadzoneMin = curve.DeadzoneLow;
        _deadzoneCenterMin = curve.DeadzoneCenterLow;
        _deadzoneCenterMax = curve.DeadzoneCenterHigh;
        _deadzoneMax = curve.DeadzoneHigh;
        _deadzoneCenterEnabled = curve.DeadzoneCenterLow != 0 || curve.DeadzoneCenterHigh != 0;

        // Load control points for custom curve
        if (curve.Type == CurveType.Custom && curve.ControlPoints is not null && curve.ControlPoints.Count >= 2)
        {
            _curveControlPoints = curve.ControlPoints.Select(p => new SKPoint(p.Input, p.Output)).ToList();
        }
        else
        {
            // Generate default control points based on curve type
            _curveControlPoints = curve.Type switch
            {
                CurveType.SCurve => new List<SKPoint> { new(0, 0), new(0.25f, 0.1f), new(0.75f, 0.9f), new(1, 1) },
                CurveType.Exponential => new List<SKPoint> { new(0, 0), new(0.5f, 0.25f), new(1, 1) },
                _ => new List<SKPoint> { new(0, 0), new(1, 1) }
            };
        }
    }

    private void SaveAxisSettingsForRow()
    {
        // Only for axis category
        if (_mappingCategory != 1) return;
        if (_selectedMappingRow < 0) return;

        var axisMapping = GetCurrentAxisMapping();
        if (axisMapping is null) return;

        // Save curve settings to mapping
        axisMapping.Curve.Type = _selectedCurveType;
        axisMapping.Curve.Symmetrical = _curveSymmetrical;
        axisMapping.Invert = _axisInverted;

        // Save deadzone settings
        axisMapping.Curve.DeadzoneLow = _deadzoneMin;
        axisMapping.Curve.DeadzoneCenterLow = _deadzoneCenterEnabled ? _deadzoneCenterMin : 0f;
        axisMapping.Curve.DeadzoneCenterHigh = _deadzoneCenterEnabled ? _deadzoneCenterMax : 0f;
        axisMapping.Curve.DeadzoneHigh = _deadzoneMax;

        // Save control points for custom curve
        if (_selectedCurveType == CurveType.Custom)
        {
            axisMapping.Curve.ControlPoints = _curveControlPoints
                .Select(p => new CurvePoint(p.X, p.Y))
                .ToList();
        }

        // Persist
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is not null)
        {
            profile.ModifiedAt = DateTime.UtcNow;
            _ctx.ProfileManager.SaveActiveProfile();
        }
    }

    private void LoadOutputTypeStateForRow()
    {
        // Reset state
        _outputTypeIsKeyboard = false;
        _selectedKeyName = "";
        _selectedModifiers = null;
        _isCapturingKey = false;
        _selectedButtonMode = ButtonMode.Normal;
        _pulseDurationMs = 100;
        _holdDurationMs = 500;

        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping is not null)
        {
            _outputTypeIsKeyboard = mapping.Output.Type == OutputType.Keyboard;
            _selectedKeyName = mapping.Output.KeyName ?? "";
            _selectedModifiers = mapping.Output.Modifiers?.ToList();
            _selectedButtonMode = mapping.Mode;
            _pulseDurationMs = mapping.PulseDurationMs;
            _holdDurationMs = mapping.HoldDurationMs;
        }
    }

    private void UpdateButtonModeForSelected()
    {
        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        // Find mapping for this button slot (either VJoyButton or Keyboard output)
        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping is not null)
        {
            mapping.Mode = _selectedButtonMode;
            profile.ModifiedAt = DateTime.UtcNow;
            _ctx.ProfileManager.SaveActiveProfile();
        }
    }

    private void UpdateOutputTypeForSelected()
    {
        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        // Find mapping for this button slot
        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping is not null)
        {
            // Update output type and clear/set key name
            mapping.Output.Type = _outputTypeIsKeyboard ? OutputType.Keyboard : OutputType.VJoyButton;
            if (!_outputTypeIsKeyboard)
            {
                mapping.Output.KeyName = null;
                mapping.Output.Modifiers = null;
            }
            else if (!string.IsNullOrEmpty(_selectedKeyName))
            {
                mapping.Output.KeyName = _selectedKeyName;
            }
            profile.ModifiedAt = DateTime.UtcNow;
            _ctx.ProfileManager.SaveActiveProfile();
        }
    }

    private void UpdateKeyNameForSelected()
    {
        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping is null && !string.IsNullOrEmpty(_selectedKeyName))
        {
            // No existing mapping - need to capture a physical input first
            // Store the keyboard key and start listening for physical input
            _pendingKeyboardKey = _selectedKeyName;
            _pendingKeyboardModifiers = _selectedModifiers?.ToList();
            _pendingKeyboardOutputIndex = outputIndex;
            _pendingKeyboardVJoyDevice = vjoyDevice.Id;

            // Start async input detection for pending keyboard binding
            _ = StartPendingKeyboardInputListeningAsync();
            return;
        }

        if (mapping is not null)
        {
            // Update existing mapping
            if (!string.IsNullOrEmpty(_selectedKeyName))
            {
                mapping.Output.Type = OutputType.Keyboard;
            }
            mapping.Output.KeyName = _selectedKeyName;
            mapping.Output.Modifiers = _selectedModifiers?.ToList();
            profile.ModifiedAt = DateTime.UtcNow;
            _ctx.ProfileManager.SaveActiveProfile();
        }
    }

    /// <summary>
    /// Clear all bindings (keyboard and input sources) for the selected button mapping
    /// </summary>
    private void ClearSelectedButtonMapping()
    {
        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        // Find and remove the mapping
        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping is not null)
        {
            profile.ButtonMappings.Remove(mapping);
            profile.ModifiedAt = DateTime.UtcNow;
            _ctx.ProfileManager.SaveActiveProfile();
            _ctx.OnMappingsChanged();

            // Reset UI state
            _selectedKeyName = "";
            _selectedModifiers = null;
            _outputTypeIsKeyboard = false;
            _selectedButtonMode = ButtonMode.Normal;
        }
    }

    /// <summary>
    /// Clear just the keyboard binding for the selected button mapping (keeps physical inputs)
    /// </summary>
    private void ClearKeyboardBinding()
    {
        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping is not null)
        {
            // Clear keyboard binding but keep mapping
            mapping.Output.Type = OutputType.VJoyButton;
            mapping.Output.KeyName = null;
            mapping.Output.Modifiers = null;
            profile.ModifiedAt = DateTime.UtcNow;
            _ctx.ProfileManager.SaveActiveProfile();

            // Update UI state
            _selectedKeyName = "";
            _selectedModifiers = null;
            _outputTypeIsKeyboard = false;
        }
    }

    private void UpdatePulseDurationFromMouse(float mouseX)
    {
        if (_pulseDurationSliderBounds.Width <= 0) return;

        float normalized = (mouseX - _pulseDurationSliderBounds.Left) / _pulseDurationSliderBounds.Width;
        normalized = Math.Clamp(normalized, 0f, 1f);

        // Map 0-1 to 100-1000ms
        _pulseDurationMs = (int)(100f + normalized * 900f);
    }

    private void UpdateHoldDurationFromMouse(float mouseX)
    {
        if (_holdDurationSliderBounds.Width <= 0) return;

        float normalized = (mouseX - _holdDurationSliderBounds.Left) / _holdDurationSliderBounds.Width;
        normalized = Math.Clamp(normalized, 0f, 1f);

        // Map 0-1 to 200-2000ms
        _holdDurationMs = (int)(200f + normalized * 1800f);
    }

    private void UpdateDurationForSelectedMapping()
    {
        // Only for button category
        if (_mappingCategory != 0) return;
        if (_selectedMappingRow < 0) return;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        int outputIndex = _selectedMappingRow;

        var mapping = profile.ButtonMappings.FirstOrDefault(m =>
            m.Output.VJoyDevice == vjoyDevice.Id &&
            m.Output.Index == outputIndex);

        if (mapping is not null)
        {
            mapping.PulseDurationMs = _pulseDurationMs;
            mapping.HoldDurationMs = _holdDurationMs;
            profile.ModifiedAt = DateTime.UtcNow;
            _ctx.ProfileManager.SaveActiveProfile();
        }
    }

    private void DrawAddMappingButton(SKCanvas canvas, SKRect bounds, bool hovered)
        => FUIWidgets.DrawAddMappingButton(canvas, bounds, hovered);

    private void DrawMappingList(SKCanvas canvas, SKRect bounds)
    {
        float itemHeight = 50f;
        float itemGap = 8f;
        float y = bounds.Top;

        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null)
        {
            FUIRenderer.DrawText(canvas, "No profile selected",
                new SKPoint(bounds.Left + 20, y + 20), FUIColors.TextDim, 15f);
            FUIRenderer.DrawText(canvas, "Select or create a profile to add mappings",
                new SKPoint(bounds.Left + 20, y + 40), FUIColors.TextDisabled, 14f);
            return;
        }

        var allMappings = new List<(string source, string target, string type, bool enabled)>();

        // Collect all mappings
        foreach (var m in profile.ButtonMappings)
        {
            string source = m.Inputs.Count > 0 ? $"{m.Inputs[0].DeviceName} Btn {m.Inputs[0].Index + 1}" : "Unknown";
            string target = m.Output.Type == OutputType.VJoyButton
                ? $"vJoy {m.Output.VJoyDevice} Btn {m.Output.Index}"
                : $"Key {m.Output.Index}";
            allMappings.Add((source, target, "BUTTON", m.Enabled));
        }

        foreach (var m in profile.AxisMappings)
        {
            string source = m.Inputs.Count > 0 ? $"{m.Inputs[0].DeviceName} Axis {m.Inputs[0].Index}" : "Unknown";
            string target = $"vJoy {m.Output.VJoyDevice} Axis {m.Output.Index}";
            allMappings.Add((source, target, "AXIS", m.Enabled));
        }

        if (allMappings.Count == 0)
        {
            FUIRenderer.DrawText(canvas, "No mappings configured",
                new SKPoint(bounds.Left + 20, y + 20), FUIColors.TextDim, 15f);
            FUIRenderer.DrawText(canvas, "Click '+ ADD MAPPING' to create your first mapping",
                new SKPoint(bounds.Left + 20, y + 40), FUIColors.TextDisabled, 14f);
            return;
        }

        // Draw mapping items
        foreach (var (source, target, type, enabled) in allMappings)
        {
            if (y + itemHeight > bounds.Bottom) break;

            var itemBounds = new SKRect(bounds.Left, y, bounds.Right, y + itemHeight);
            DrawMappingItem(canvas, itemBounds, source, target, type, enabled);
            y += itemHeight + itemGap;
        }
    }

    private void DrawMappingItem(SKCanvas canvas, SKRect bounds, string source, string target, string type, bool enabled)
        => FUIWidgets.DrawMappingItem(canvas, bounds, source, target, type, enabled);

    private void OpenAddMappingDialog()
    {
        // Ensure we have an active profile
        if (!_ctx.ProfileManager.HasActiveProfile)
        {
            _ctx.CreateNewProfilePrompt!();
            if (!_ctx.ProfileManager.HasActiveProfile) return;
        }

        using var dialog = new MappingDialog(_ctx.InputService, _ctx.VJoyService);
        if (dialog.ShowDialog(_ctx.OwnerForm) == DialogResult.OK && dialog.Result.Success)
        {
            var result = dialog.Result;

            // Create the mapping based on detected input type
            if (result.Input!.Type == InputType.Button)
            {
                var mapping = new ButtonMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    Mode = result.ButtonMode
                };
                _ctx.ProfileManager.ActiveProfile!.ButtonMappings.Add(mapping);
            }
            else if (result.Input.Type == InputType.Axis)
            {
                var mapping = new AxisMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    Curve = result.AxisCurve ?? new AxisCurve()
                };
                _ctx.ProfileManager.ActiveProfile!.AxisMappings.Add(mapping);
            }
            else if (result.Input.Type == InputType.Hat)
            {
                var mapping = new HatMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    UseContinuous = true // Default to continuous POV
                };
                _ctx.ProfileManager.ActiveProfile!.HatMappings.Add(mapping);
            }

            // Save the profile
            _ctx.ProfileManager.SaveActiveProfile();
            _ctx.OnMappingsChanged();
        }
    }

    private void OpenMappingDialogForControl(string controlId)
    {
        // Need device map, selected device, and control info
        if (_ctx.DeviceMap is null || _ctx.SelectedDevice < 0 || _ctx.SelectedDevice >= _ctx.Devices.Count)
            return;

        // Find the control definition in the device map
        if (!_ctx.DeviceMap.Controls.TryGetValue(controlId, out var control))
            return;

        // Get the binding from the control (e.g., "button0", "x", "hat0")
        if (control.Bindings is null || control.Bindings.Count == 0)
            return;

        var device = _ctx.Devices[_ctx.SelectedDevice];
        var binding = control.Bindings[0];

        // Parse the binding to determine input type and index
        var (inputType, inputIndex) = ParseBinding(binding, control.Type);
        if (inputType is null)
            return;

        // Ensure we have an active profile
        if (!_ctx.ProfileManager.HasActiveProfile)
        {
            _ctx.CreateNewProfilePrompt!();
            if (!_ctx.ProfileManager.HasActiveProfile) return;
        }

        // Create a pre-selected DetectedInput
        var preSelectedInput = new DetectedInput
        {
            DeviceGuid = device.InstanceGuid,
            DeviceName = device.Name,
            Type = inputType.Value,
            Index = inputIndex,
            Value = 0
        };

        // Open dialog with pre-selected input (skips "wait for input" phase)
        using var dialog = new MappingDialog(_ctx.InputService, _ctx.VJoyService, preSelectedInput);
        if (dialog.ShowDialog(_ctx.OwnerForm) == DialogResult.OK && dialog.Result.Success)
        {
            var result = dialog.Result;

            // Create the mapping based on detected input type
            if (result.Input!.Type == InputType.Button)
            {
                var mapping = new ButtonMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    Mode = result.ButtonMode
                };
                _ctx.ProfileManager.ActiveProfile!.ButtonMappings.Add(mapping);
            }
            else if (result.Input.Type == InputType.Axis)
            {
                var mapping = new AxisMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    Curve = result.AxisCurve ?? new AxisCurve()
                };
                _ctx.ProfileManager.ActiveProfile!.AxisMappings.Add(mapping);
            }
            else if (result.Input.Type == InputType.Hat)
            {
                var mapping = new HatMapping
                {
                    Name = result.MappingName,
                    Inputs = new List<InputSource> { result.Input.ToInputSource() },
                    Output = result.Output!,
                    UseContinuous = true
                };
                _ctx.ProfileManager.ActiveProfile!.HatMappings.Add(mapping);
            }

            // Save the profile
            _ctx.ProfileManager.SaveActiveProfile();
            _ctx.OnMappingsChanged();
        }
    }

    private (InputType? type, int index) ParseBinding(string binding, string controlType)
    {
        // Handle button bindings: "button0", "button1", etc.
        if (binding.StartsWith("button", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(binding.Substring(6), out int buttonIndex))
                return (InputType.Button, buttonIndex);
        }

        // Handle axis bindings: "x", "y", "z", "rx", "ry", "rz", "slider0", "slider1"
        var axisMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "x", 0 }, { "y", 1 }, { "z", 2 },
            { "rx", 3 }, { "ry", 4 }, { "rz", 5 },
            { "slider0", 6 }, { "slider1", 7 }
        };
        if (axisMap.TryGetValue(binding, out int axisIndex))
            return (InputType.Axis, axisIndex);

        // Handle hat bindings: "hat0", "hat1", etc.
        if (binding.StartsWith("hat", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(binding.Substring(3), out int hatIndex))
                return (InputType.Hat, hatIndex);
        }

        // Fall back to control type if binding doesn't parse
        return controlType.ToUpperInvariant() switch
        {
            "BUTTON" => (InputType.Button, 0),
            "AXIS" => (InputType.Axis, 0),
            "HAT" or "POV" => (InputType.Hat, 0),
            _ => (null, 0)
        };
    }

    #endregion

    #region Silhouette Lead Line

    /// <summary>
    /// Returns the binding string for an SDL2 axis index (e.g. 0 → "x", 3 → "rx").
    /// Must match MainForm.GetAxisBindingName.
    /// </summary>
    private static string GetAxisBindingName(int axisIndex) => axisIndex switch
    {
        0 => "x",  1 => "y",  2 => "z",
        3 => "rx", 4 => "ry", 5 => "rz",
        6 => "slider1", 7 => "slider2",
        _ => $"axis{axisIndex}"
    };

    /// <summary>
    /// Finds the DeviceMap control for the given mapping row index.
    /// Row index is relative to the current category (Buttons or Axes), starting at 0.
    /// Category 0 (Buttons): row i = button output index i.
    /// Category 1 (Axes): row i = axis output index i.
    /// Returns null if no mapping or no device map anchor.
    /// </summary>
    private ControlDefinition? GetControlForRow(int rowIndex)
    {
        var deviceMap = _ctx.MappingsPrimaryDeviceMap;
        if (deviceMap is null) return null;
        if (_ctx.VJoyDevices.Count == 0 || _ctx.SelectedVJoyDeviceIndex >= _ctx.VJoyDevices.Count) return null;

        var vjoyDevice = _ctx.VJoyDevices[_ctx.SelectedVJoyDeviceIndex];
        var profile = _ctx.ProfileManager.ActiveProfile;
        if (profile is null) return null;

        string? binding;
        if (_mappingCategory == 1)
        {
            // Axes category: row i = axis output index i
            var mapping = profile.AxisMappings.FirstOrDefault(m =>
                m.Output.VJoyDevice == vjoyDevice.Id && m.Output.Index == rowIndex);
            binding = mapping?.Inputs.Count > 0 ? GetAxisBindingName(mapping.Inputs[0].Index) : null;
        }
        else
        {
            // Buttons category: row i = button output index i
            var mapping = profile.ButtonMappings.FirstOrDefault(m =>
                m.Output.VJoyDevice == vjoyDevice.Id && m.Output.Index == rowIndex);
            binding = mapping?.Inputs.Count > 0 ? $"button{mapping.Inputs[0].Index + 1}" : null;
        }

        return binding is not null ? deviceMap.FindControlByBinding(binding) : null;
    }

    /// <summary>
    /// Converts a device-map viewBox coordinate to canvas screen coordinates,
    /// using the scale/offset set by the most recent DrawSvgInBounds call.
    /// </summary>
    private SKPoint ViewBoxToScreen(float viewBoxX, float viewBoxY)
    {
        var svg = _ctx.GetSvgForDeviceMap?.Invoke(_ctx.MappingsPrimaryDeviceMap) ?? _ctx.JoystickSvg;
        if (svg?.Picture is null)
            return new SKPoint(viewBoxX, viewBoxY);

        float screenX = _ctx.SvgMirrored
            ? _ctx.SvgOffset.X + svg.Picture.CullRect.Width * _ctx.SvgScale - viewBoxX * _ctx.SvgScale
            : _ctx.SvgOffset.X + viewBoxX * _ctx.SvgScale;

        float screenY = _ctx.SvgOffset.Y + viewBoxY * _ctx.SvgScale;
        return new SKPoint(screenX, screenY);
    }

    /// <summary>
    /// Draws a lead line from the highlighted control anchor to its label position.
    /// Fades over 1 second hold + 2 seconds fade.
    /// </summary>
    private void DrawMappingHighlightLeadLine(SKCanvas canvas, SKRect panelBounds)
    {
        if (_mappingHighlightControl?.Anchor is null) return;

        float elapsed = (float)(DateTime.Now - _mappingHighlightTime).TotalSeconds;
        float opacity = elapsed < 1f ? 1f : Math.Max(0f, 1f - (elapsed - 1f) / 2f);
        if (opacity < 0.01f) return;

        SKPoint anchorScreen = ViewBoxToScreen(_mappingHighlightControl.Anchor.X, _mappingHighlightControl.Anchor.Y);

        float labelX, labelY;
        bool goesRight = true;

        if (_mappingHighlightControl.LabelOffset is not null)
        {
            var labelScreen = ViewBoxToScreen(
                _mappingHighlightControl.Anchor.X + _mappingHighlightControl.LabelOffset.X,
                _mappingHighlightControl.Anchor.Y + _mappingHighlightControl.LabelOffset.Y);
            labelX = labelScreen.X;
            labelY = labelScreen.Y;
            bool offsetGoesRight = _mappingHighlightControl.LabelOffset.X >= 0;
            goesRight = _ctx.SvgMirrored ? !offsetGoesRight : offsetGoesRight;
        }
        else
        {
            labelY = panelBounds.Top + 80;
            labelX = _ctx.SilhouetteBounds.Right + 20;
        }

        var fakeInput = new ActiveInputState
        {
            Binding = _mappingHighlightControl.Label,
            Value = 1f,
            IsAxis = false,
            Control = _mappingHighlightControl,
            LastActivity = _mappingHighlightTime,
            AppearProgress = 1f
        };

        DeviceLeadLineRenderer.DrawInputLeadLine(
            canvas, anchorScreen, new SKPoint(labelX, labelY),
            goesRight, opacity, fakeInput, _ctx.SvgMirrored, _ctx.SvgScale);
    }

    #endregion

}
