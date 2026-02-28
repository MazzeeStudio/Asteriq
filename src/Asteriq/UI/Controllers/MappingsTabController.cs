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

    // Modifier key names as stored in OutputTarget.KeyName (case-insensitive).
    private static readonly HashSet<string> s_scModifierKeyNames =
        new(StringComparer.OrdinalIgnoreCase) { "RCtrl", "LCtrl", "RShift", "LShift", "RAlt", "LAlt" };

    /// <summary>Returns true when the given key name (as stored in OutputTarget.KeyName) is a modifier key.</summary>
    private static bool IsModifierKeyName(string? keyName) =>
        keyName is not null && s_scModifierKeyNames.Contains(keyName);

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

        // Right panel: Button mode selection (button category) ÔÇö blocked for modifier keys
        bool selectedIsModifier = _outputTypeIsKeyboard && IsModifierKeyName(_selectedKeyName);
        if (_mappingCategory == 0 && _selectedMappingRow >= 0 && _hoveredButtonMode >= 0 && !selectedIsModifier)
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
            float scrollAmount = -e.Delta / 4f; // Delta is usually ┬▒120, divide for smooth scrolling
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
                    // SDL2 button index i ÔåÆ device-map binding "button{i+1}"
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
                _noMappingFlashText = $"BUTTON {i + 1} ÔÇö NO MAPPING";
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

}
