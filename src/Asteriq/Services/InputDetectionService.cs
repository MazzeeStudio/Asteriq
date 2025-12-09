using Asteriq.Models;

namespace Asteriq.Services;

/// <summary>
/// Result of detecting an input
/// </summary>
public class DetectedInput
{
    /// <summary>Device GUID</summary>
    public Guid DeviceGuid { get; init; }

    /// <summary>Device name</summary>
    public string DeviceName { get; init; } = "";

    /// <summary>Type of input detected</summary>
    public InputType Type { get; init; }

    /// <summary>Index of the input on the device (0-based)</summary>
    public int Index { get; init; }

    /// <summary>Current value (for axes: -1 to 1, for buttons: 0 or 1)</summary>
    public float Value { get; init; }

    /// <summary>Create an InputSource from this detection</summary>
    public InputSource ToInputSource()
    {
        return new InputSource
        {
            DeviceId = DeviceGuid.ToString(),
            DeviceName = DeviceName,
            Type = Type,
            Index = Index
        };
    }

    public override string ToString()
    {
        var typeStr = Type switch
        {
            InputType.Axis => $"Axis {Index}",
            InputType.Button => $"Button {Index + 1}",
            InputType.Hat => $"Hat {Index + 1}",
            _ => $"{Type} {Index}"
        };
        return $"{DeviceName} - {typeStr}";
    }
}

/// <summary>
/// Filter options for input detection
/// </summary>
[Flags]
public enum InputDetectionFilter
{
    None = 0,
    Buttons = 1,
    Axes = 2,
    Hats = 4,
    All = Buttons | Axes | Hats
}

/// <summary>
/// Service for detecting single input events from physical devices.
/// Used when creating mappings - "press a button or move an axis".
/// </summary>
public class InputDetectionService : IDisposable
{
    private readonly InputService _inputService;
    private readonly object _lock = new();

    private TaskCompletionSource<DetectedInput?>? _currentDetection;
    private CancellationTokenSource? _cancellationSource;
    private InputDetectionFilter _filter = InputDetectionFilter.All;
    private float _axisThreshold = 0.5f;

    // Initial baseline state (captured at start, used for axis detection)
    private Dictionary<Guid, float[]> _initialAxes = new();
    private Dictionary<Guid, bool[]> _initialButtons = new();
    private Dictionary<Guid, int[]> _initialHats = new();

    // Previous frame state (updated each poll, used for button transition detection)
    private Dictionary<Guid, bool[]> _previousButtons = new();
    private Dictionary<Guid, int[]> _previousHats = new();

    private bool _initialCaptured;
    private int _warmupPolls = 0;
    private const int RequiredWarmupPolls = 3; // Skip first few polls to let state settle

    /// <summary>
    /// Event fired when input is detected during waiting
    /// </summary>
    public event EventHandler<DetectedInput>? InputDetected;

    public InputDetectionService(InputService inputService)
    {
        _inputService = inputService;
    }

    /// <summary>
    /// Whether currently waiting for input
    /// </summary>
    public bool IsWaiting => _currentDetection != null;

    /// <summary>
    /// Start waiting for an input. Returns when user presses a button or moves an axis.
    /// </summary>
    /// <param name="filter">Filter which input types to detect</param>
    /// <param name="axisThreshold">How far axis must move from baseline to register (0-1)</param>
    /// <param name="timeout">Optional timeout in milliseconds</param>
    /// <returns>The detected input, or null if cancelled/timed out</returns>
    public async Task<DetectedInput?> WaitForInputAsync(
        InputDetectionFilter filter = InputDetectionFilter.All,
        float axisThreshold = 0.15f,  // 15% threshold like SCVirtStick
        int? timeoutMs = null)
    {
        lock (_lock)
        {
            if (_currentDetection != null)
                throw new InvalidOperationException("Already waiting for input");

            _filter = filter;
            _axisThreshold = axisThreshold;

            // Reset all state for new detection session
            _initialCaptured = false;
            _warmupPolls = 0;
            _initialAxes.Clear();
            _initialButtons.Clear();
            _initialHats.Clear();
            _previousButtons.Clear();
            _previousHats.Clear();

            _currentDetection = new TaskCompletionSource<DetectedInput?>();
            _cancellationSource = new CancellationTokenSource();
        }

        // Subscribe to input events
        _inputService.InputReceived += OnInputReceived;

        try
        {
            if (timeoutMs.HasValue)
            {
                _cancellationSource.CancelAfter(timeoutMs.Value);
            }

            // Wait for detection or cancellation
            using var registration = _cancellationSource.Token.Register(() =>
            {
                _currentDetection?.TrySetResult(null);
            });

            return await _currentDetection.Task;
        }
        finally
        {
            _inputService.InputReceived -= OnInputReceived;

            lock (_lock)
            {
                _currentDetection = null;
                _cancellationSource?.Dispose();
                _cancellationSource = null;
            }
        }
    }

    /// <summary>
    /// Cancel the current detection wait
    /// </summary>
    public void Cancel()
    {
        _cancellationSource?.Cancel();
    }

    private void OnInputReceived(object? sender, DeviceInputState state)
    {
        lock (_lock)
        {
            if (_currentDetection == null)
                return;

            var deviceGuid = state.InstanceGuid;

            // Warmup phase - let device state settle before capturing baseline
            if (_warmupPolls < RequiredWarmupPolls)
            {
                _warmupPolls++;
                // Just update previous state during warmup
                _previousButtons[deviceGuid] = (bool[])state.Buttons.Clone();
                _previousHats[deviceGuid] = (int[])state.Hats.Clone();
                return;
            }

            // Capture initial baseline after warmup (for axis detection)
            if (!_initialCaptured)
            {
                CaptureInitialState(state);
                return;
            }

            // Check for input changes
            var detected = DetectInputChange(state);
            if (detected != null)
            {
                InputDetected?.Invoke(this, detected);
                _currentDetection.TrySetResult(detected);
            }
            else
            {
                // Update previous state for next frame's transition detection
                _previousButtons[deviceGuid] = (bool[])state.Buttons.Clone();
                _previousHats[deviceGuid] = (int[])state.Hats.Clone();
            }
        }
    }

    private void CaptureInitialState(DeviceInputState state)
    {
        var guid = state.InstanceGuid;

        // Capture current state as initial baseline (for axis detection)
        _initialAxes[guid] = (float[])state.Axes.Clone();
        _initialButtons[guid] = (bool[])state.Buttons.Clone();
        _initialHats[guid] = (int[])state.Hats.Clone();

        // Also set as previous state for button/hat transition detection
        _previousButtons[guid] = (bool[])state.Buttons.Clone();
        _previousHats[guid] = (int[])state.Hats.Clone();

        // Mark initial capture complete
        _initialCaptured = true;
    }

    private DetectedInput? DetectInputChange(DeviceInputState state)
    {
        var guid = state.InstanceGuid;

        // Get initial baseline for this device (for axis detection - compare to start)
        if (!_initialAxes.TryGetValue(guid, out var initialAxes))
            initialAxes = new float[state.Axes.Length];

        // Get previous frame state for this device (for button/hat transition detection)
        if (!_previousButtons.TryGetValue(guid, out var prevButtons))
            prevButtons = new bool[state.Buttons.Length];
        if (!_previousHats.TryGetValue(guid, out var prevHats))
            prevHats = new int[state.Hats.Length];

        // Check buttons first (most common for mapping)
        // Use PREVIOUS state for transition detection: button must have been released last frame
        if (_filter.HasFlag(InputDetectionFilter.Buttons))
        {
            for (int i = 0; i < state.Buttons.Length && i < prevButtons.Length; i++)
            {
                // Detect button press TRANSITION (was not pressed LAST FRAME, now is pressed)
                if (state.Buttons[i] && !prevButtons[i])
                {
                    return new DetectedInput
                    {
                        DeviceGuid = guid,
                        DeviceName = state.DeviceName,
                        Type = InputType.Button,
                        Index = i,
                        Value = 1f
                    };
                }
            }
        }

        // Check axes - use INITIAL state for threshold comparison
        // Axis must move away from where it started, not just change frame-to-frame
        if (_filter.HasFlag(InputDetectionFilter.Axes))
        {
            for (int i = 0; i < state.Axes.Length && i < initialAxes.Length; i++)
            {
                float delta = Math.Abs(state.Axes[i] - initialAxes[i]);
                if (delta >= _axisThreshold)
                {
                    return new DetectedInput
                    {
                        DeviceGuid = guid,
                        DeviceName = state.DeviceName,
                        Type = InputType.Axis,
                        Index = i,
                        Value = state.Axes[i]
                    };
                }
            }
        }

        // Check hats - use PREVIOUS state for transition detection
        if (_filter.HasFlag(InputDetectionFilter.Hats))
        {
            for (int i = 0; i < state.Hats.Length && i < prevHats.Length; i++)
            {
                // Detect hat movement transition (was centered last frame, now has direction)
                if (state.Hats[i] >= 0 && prevHats[i] < 0)
                {
                    return new DetectedInput
                    {
                        DeviceGuid = guid,
                        DeviceName = state.DeviceName,
                        Type = InputType.Hat,
                        Index = i,
                        Value = state.Hats[i] / 360f
                    };
                }
            }
        }

        return null;
    }

    public void Dispose()
    {
        Cancel();
    }
}
