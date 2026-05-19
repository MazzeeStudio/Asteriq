using System.Runtime.InteropServices;
using Asteriq.Models;
using Asteriq.Services;
using Asteriq.Services.Abstractions;
using SkiaSharp;
using Svg.Skia;

namespace Asteriq.UI.Controllers;

public partial class MappingsTabController
{
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
        public DetectedInput? PendingInput;
        public long ListeningStartTicks;
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