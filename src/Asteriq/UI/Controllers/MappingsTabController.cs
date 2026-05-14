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
    private int _mappingCategory;  // 0 = Buttons, 1 = Axes
    private int _hoveredMappingCategory = -1;
    private SKRect _mappingCategoryButtonsBounds;
    private SKRect _mappingCategoryAxesBounds;

    // Mappings tab UI state - Left panel (output list)
    private int _selectedMappingRow = -1;
    private bool _selectionIsExplicit; // true only when user explicitly clicked a row
    private int _hoveredMappingRow = -1;

    // Maps visual axis row index â†’ actual vJoy axis index (0-7).
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
    private bool _mappingEditorOpen;
    private int _editingRowIndex = -1;
    private bool _isEditingAxis;
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

    // "Manage in Keybindings" button shown in the right panel when the selected row has been
    // rerouted by an SC share. Click handler invokes _ctx.OpenSCBindingsWithSearch with the
    // share's vJoy id + input name. Bounds reset every frame in DrawMappingSettingsPanel.
    private SKRect _sharedManageButtonBounds;
    private bool _sharedManageButtonHovered;
    private string? _sharedManageSearchText;
    private uint _sharedManageVJoyDevice;

    // "Go to merged axis" button shown when the selected axis row's natural physical input
    // is being consumed by a merge on another vJoy slot. Click navigates to the merge target.
    // Bounds reset every frame in DrawMappingSettingsPanel.
    private SKRect _mergedAwayManageButtonBounds;
    private bool _mergedAwayManageButtonHovered;
    private (uint vjoyDevice, int axisIndex)? _mergedAwayManageTarget;

    // Merge operation selector (for axes with multiple inputs)
    private readonly MergeModeDropdownState _merge = new();

    // Curve and deadzone preset button hover
    private int _hoveredCurvePreset = -1;
    private int _hoveredDeadzonePreset = -1;

    // Mapping editor - action buttons (hover state not wired; rendering passes false literally)
    private SKRect _saveButtonBounds;
    private SKRect _cancelButtonBounds;

    private const int HighlightDurationMs = 1500; // How long the attention highlight lasts (1.5 seconds)
    private const int HighlightDebounceCooldownMs = 500; // Minimum time between highlights for same button

    // â”€â”€ State objects â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
    // Legacy compatibility â€” computed alias kept for use in axis logic
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
            float scrollAmount = -e.Delta / 4f; // Delta is usually Â±120, divide for smooth scrolling
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
    /// Returns the current highlighted mapping row index, or -1 if none.
    /// Used by MainForm to detect highlight changes.
    /// </summary>
    public int HighlightedMappingRow => _highlight.Row;

    /// <summary>
    /// Returns the vJoy device ID of the highlighted row.
    /// </summary>
    public uint HighlightedVJoyDevice => _highlight.VJoyDevice;

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // State classes â€” each groups logically-related fields for one sub-feature.
    // All fields are public so partial files can access them via the instance.
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

}