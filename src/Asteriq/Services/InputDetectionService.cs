using Asteriq.Models;
using System.IO;

namespace Asteriq.Services;

// Simple file logger for input detection debugging
internal static class InputDetectionLog
{
    private static readonly string s_logPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Asteriq", "input_detection.log");

    static InputDetectionLog()
    {
        var dir = Path.GetDirectoryName(s_logPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    public static void Log(string message)
    {
        try
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            Console.WriteLine(line);
            File.AppendAllText(s_logPath, line + Environment.NewLine);
        }
        catch (Exception)
        {
            // Debug logging should never crash the app - silently ignore file write failures
        }
    }

    public static void Clear()
    {
        try
        {
            File.WriteAllText(s_logPath, "");
        }
        catch (Exception)
        {
            // Debug log clear failure is non-critical - silently ignore
        }
    }
}

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
///
/// Axis detection follows JoystickGremlinEx approach:
/// 1. Collect samples during warmup to establish stable baseline per axis
/// 2. Track "stable value" per axis - updated only when axis is at rest (small jitter)
/// 3. Require intentional movement: large delta from stable baseline AND sustained over multiple samples
/// 4. Noise filtering: ignore small fluctuations (jitter threshold ~0.02 = 2% of range)
/// </summary>
public class InputDetectionService : IDisposable
{
    private readonly InputService _inputService;
    private readonly object _lock = new();

    private TaskCompletionSource<DetectedInput?>? _currentDetection;
    private CancellationTokenSource? _cancellationSource;
    private InputDetectionFilter _filter = InputDetectionFilter.All;
    private float _axisThreshold = 0.5f;

    // Stable axis baseline (computed from warmup samples, represents "at rest" position)
    private Dictionary<Guid, float[]> _stableAxisBaseline = new();

    // Button/hat state for transition detection
    private Dictionary<Guid, bool[]> _initialButtons = new();
    private Dictionary<Guid, int[]> _initialHats = new();
    private Dictionary<Guid, bool[]> _previousButtons = new();
    private Dictionary<Guid, int[]> _previousHats = new();

    // Axis stabilization - collect samples during warmup to establish stable baseline
    private Dictionary<Guid, List<float[]>> _axisWarmupSamples = new();

    // Track axis movement confirmation - need sustained movement to confirm intentional input
    private Dictionary<Guid, int[]> _axisMovementConfirmCount = new();
    private const int RequiredConfirmationFrames = 3; // Reduced from 8 - quality sticks don't need much confirmation

    // Jitter threshold - movements smaller than this are considered noise (5% of axis range)
    // Reduced from 8% to allow more sensitive detection
    private const float JitterThreshold = 0.05f;

    // High-variance threshold - axes with stdDev above this are completely ignored (noisy/phantom axes)
    // Increased from 0.03 to 0.15 - VirPil and other quality sticks can have natural jitter
    // Only truly broken axes should be filtered (15% standard deviation = definitely broken)
    private const float HighVarianceThreshold = 0.15f;

    // Track which axes are too noisy to use
    private Dictionary<Guid, bool[]> _noisyAxes = new();

    // Warmup configuration - reduced for faster response
    private const int RequiredWarmupSamples = 10; // Reduced from 30 - fewer samples needed for baseline
    private const int RequiredWarmupPolls = 5;    // Reduced from 10 - less settle time needed

    // Per-device warmup tracking (each device needs its own warmup)
    private Dictionary<Guid, int> _deviceWarmupPolls = new();
    private Dictionary<Guid, bool> _deviceBaselineCaptured = new();

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
    public bool IsWaiting => _currentDetection is not null;

    /// <summary>
    /// Start waiting for an input. Returns when user presses a button or moves an axis.
    /// </summary>
    /// <param name="filter">Filter which input types to detect</param>
    /// <param name="axisThreshold">How far axis must move from stable baseline to register (0-1)</param>
    /// <param name="timeoutMs">Optional timeout in milliseconds</param>
    /// <param name="ct">Cancellation token for external cancellation</param>
    /// <returns>The detected input, or null if cancelled/timed out</returns>
    public async Task<DetectedInput?> WaitForInputAsync(
        InputDetectionFilter filter = InputDetectionFilter.All,
        float axisThreshold = 0.5f,  // 50% threshold - intentional movement required
        int? timeoutMs = null,
        CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_currentDetection is not null)
                throw new InvalidOperationException("Already waiting for input");

            _filter = filter;
            _axisThreshold = axisThreshold;

            // Reset all state for new detection session
            _deviceWarmupPolls.Clear();
            _deviceBaselineCaptured.Clear();
            _stableAxisBaseline.Clear();
            _initialButtons.Clear();
            _initialHats.Clear();
            _previousButtons.Clear();
            _previousHats.Clear();
            _axisWarmupSamples.Clear();
            _axisMovementConfirmCount.Clear();
            _noisyAxes.Clear();

            _currentDetection = new TaskCompletionSource<DetectedInput?>();
            _cancellationSource = new CancellationTokenSource();

            InputDetectionLog.Clear();
            InputDetectionLog.Log($"Started waiting for input. Filter={filter}, AxisThreshold={axisThreshold:F2}, Timeout={timeoutMs}ms");
            InputDetectionLog.Log($"[InputDetection] Warmup: {RequiredWarmupPolls} settle polls + {RequiredWarmupSamples} samples");
            InputDetectionLog.Log($"[InputDetection] Thresholds: Jitter={JitterThreshold:F2}, HighVariance={HighVarianceThreshold:F2}, Confirmation={RequiredConfirmationFrames} frames");
        }

        // Subscribe to input events
        _inputService.InputReceived += OnInputReceived;

        try
        {
            if (timeoutMs.HasValue)
            {
                _cancellationSource.CancelAfter(timeoutMs.Value);
            }

            // Link external cancellation token to internal source
            using var linkedCts = ct.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(ct, _cancellationSource.Token)
                : null;
            var effectiveToken = linkedCts?.Token ?? _cancellationSource.Token;

            // Wait for detection or cancellation
            using var registration = effectiveToken.Register(() =>
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
            if (_currentDetection is null)
                return;

            var deviceGuid = state.InstanceGuid;

            // Get or initialize per-device warmup counter
            if (!_deviceWarmupPolls.TryGetValue(deviceGuid, out int warmupPolls))
            {
                warmupPolls = 0;
                _deviceWarmupPolls[deviceGuid] = 0;
                InputDetectionLog.Log($"New device detected: {state.DeviceName} ({deviceGuid})");
            }

            // Phase 1: Initial settle - skip first few polls to let device state stabilize
            if (warmupPolls < RequiredWarmupPolls)
            {
                _deviceWarmupPolls[deviceGuid] = warmupPolls + 1;
                _previousButtons[deviceGuid] = (bool[])state.Buttons.Clone();
                _previousHats[deviceGuid] = (int[])state.Hats.Clone();
                return;
            }

            // Phase 2: Collect axis samples for stable baseline computation
            if (warmupPolls < RequiredWarmupPolls + RequiredWarmupSamples)
            {
                _deviceWarmupPolls[deviceGuid] = warmupPolls + 1;
                CollectAxisWarmupSample(state);
                _previousButtons[deviceGuid] = (bool[])state.Buttons.Clone();
                _previousHats[deviceGuid] = (int[])state.Hats.Clone();
                return;
            }

            // Phase 3: Compute stable baseline from warmup samples (once per device)
            if (!_deviceBaselineCaptured.TryGetValue(deviceGuid, out bool captured) || !captured)
            {
                ComputeStableBaseline(state);
                _deviceBaselineCaptured[deviceGuid] = true;
                return;
            }

            // Phase 4: Active detection
            var detected = DetectInputChange(state);
            if (detected is not null)
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

    private void CollectAxisWarmupSample(DeviceInputState state)
    {
        var guid = state.InstanceGuid;

        if (!_axisWarmupSamples.TryGetValue(guid, out var samples))
        {
            samples = new List<float[]>();
            _axisWarmupSamples[guid] = samples;
        }

        samples.Add((float[])state.Axes.Clone());
    }

    private void ComputeStableBaseline(DeviceInputState state)
    {
        var guid = state.InstanceGuid;

        // Compute averaged baseline from warmup samples
        var baseline = new float[state.Axes.Length];
        var noisyFlags = new bool[state.Axes.Length];

        InputDetectionLog.Log($"[InputDetection] Computing baseline for {state.DeviceName} with {state.Axes.Length} axes");

        if (_axisWarmupSamples.TryGetValue(guid, out var samples) && samples.Count > 0)
        {
            for (int axis = 0; axis < state.Axes.Length; axis++)
            {
                // Compute mean, min, and max
                float sum = 0f;
                float minVal = float.MaxValue;
                float maxVal = float.MinValue;
                int count = 0;
                foreach (var sample in samples)
                {
                    if (axis < sample.Length)
                    {
                        float val = sample[axis];
                        sum += val;
                        minVal = Math.Min(minVal, val);
                        maxVal = Math.Max(maxVal, val);
                        count++;
                    }
                }
                float mean = count > 0 ? sum / count : 0f;
                float range = count > 0 ? maxVal - minVal : 0f;

                // Also compute variance to detect noisy axes
                float varianceSum = 0f;
                foreach (var sample in samples)
                {
                    if (axis < sample.Length)
                    {
                        float diff = sample[axis] - mean;
                        varianceSum += diff * diff;
                    }
                }
                float variance = count > 0 ? varianceSum / count : 0f;
                float stdDev = MathF.Sqrt(variance);

                // Mark axis as noisy if standard deviation is very high (phantom/broken axis)
                // Removed the range check - if user moves axis during warmup it shouldn't be marked noisy
                // Only truly broken axes with 15%+ standard deviation are filtered
                bool isNoisy = stdDev > HighVarianceThreshold;

                if (isNoisy)
                {
                    noisyFlags[axis] = true;
                    baseline[axis] = mean;
                    InputDetectionLog.Log($"[InputDetection]   Axis {axis}: *** MARKED NOISY *** stdDev={stdDev:F4}, range={range:F4}");
                }
                else
                {
                    noisyFlags[axis] = false;
                    baseline[axis] = mean;
                    InputDetectionLog.Log($"[InputDetection]   Axis {axis}: mean={mean:F4}, stdDev={stdDev:F4}, range={range:F4}");
                }
            }
        }
        else
        {
            // No samples - use current state
            baseline = (float[])state.Axes.Clone();
            InputDetectionLog.Log($"[InputDetection] No warmup samples, using current state as baseline");
        }

        _stableAxisBaseline[guid] = baseline;
        _noisyAxes[guid] = noisyFlags;

        // Initialize movement confirmation counters
        _axisMovementConfirmCount[guid] = new int[state.Axes.Length];

        // Capture button/hat initial state - these will be IGNORED during detection
        // to prevent "always active" buttons from triggering
        _initialButtons[guid] = (bool[])state.Buttons.Clone();
        _initialHats[guid] = (int[])state.Hats.Clone();
        _previousButtons[guid] = (bool[])state.Buttons.Clone();
        _previousHats[guid] = (int[])state.Hats.Clone();

        // Log which buttons are initially pressed (will be ignored)
        var pressedButtons = new List<int>();
        for (int i = 0; i < state.Buttons.Length; i++)
        {
            if (state.Buttons[i])
                pressedButtons.Add(i);
        }
        if (pressedButtons.Count > 0)
        {
            InputDetectionLog.Log($"[InputDetection] {state.DeviceName}: Ignoring initially-pressed buttons: {string.Join(", ", pressedButtons)}");
        }

        // Log which hats are initially active
        var activeHats = new List<string>();
        for (int i = 0; i < state.Hats.Length; i++)
        {
            if (state.Hats[i] >= 0)
                activeHats.Add($"Hat{i}={state.Hats[i]}°");
        }
        if (activeHats.Count > 0)
        {
            InputDetectionLog.Log($"[InputDetection] {state.DeviceName}: Ignoring initially-active hats: {string.Join(", ", activeHats)}");
        }
    }

    private DetectedInput? DetectInputChange(DeviceInputState state)
    {
        var guid = state.InstanceGuid;

        // Get stable baseline for this device
        if (!_stableAxisBaseline.TryGetValue(guid, out var baseline))
            baseline = new float[state.Axes.Length];

        // Get movement confirmation counters
        if (!_axisMovementConfirmCount.TryGetValue(guid, out var confirmCounts))
        {
            confirmCounts = new int[state.Axes.Length];
            _axisMovementConfirmCount[guid] = confirmCounts;
        }

        // Get previous frame state for button/hat transition detection
        if (!_previousButtons.TryGetValue(guid, out var prevButtons))
            prevButtons = new bool[state.Buttons.Length];
        if (!_previousHats.TryGetValue(guid, out var prevHats))
            prevHats = new int[state.Hats.Length];

        // Check buttons first (most common for mapping)
        if (_filter.HasFlag(InputDetectionFilter.Buttons))
        {
            // Get initial button state (captured at baseline) to filter out already-pressed buttons
            if (!_initialButtons.TryGetValue(guid, out var initialButtons))
                initialButtons = new bool[state.Buttons.Length];

            for (int i = 0; i < state.Buttons.Length && i < prevButtons.Length; i++)
            {
                // CRITICAL: Skip buttons that were already pressed when detection started.
                // This fixes the "always active" button issue for flip triggers and similar
                // dual-position switches that report as continuously pressed buttons.
                // Following JoystickGremlinEx approach: ignore initially-active inputs.
                if (i < initialButtons.Length && initialButtons[i])
                {
                    continue; // Button was pressed at start - ignore it entirely
                }

                // Detect button press TRANSITION (was not pressed LAST FRAME, now is pressed)
                if (state.Buttons[i] && !prevButtons[i])
                {
                    InputDetectionLog.Log($"[InputDetection] *** BUTTON {i} DETECTED *** on {state.DeviceName}");
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

        // Check axes - require sustained intentional movement
        if (_filter.HasFlag(InputDetectionFilter.Axes))
        {
            // Get noisy axis flags for this device
            if (!_noisyAxes.TryGetValue(guid, out var noisyFlags))
                noisyFlags = new bool[state.Axes.Length];

            for (int i = 0; i < state.Axes.Length && i < baseline.Length; i++)
            {
                // Skip axes marked as noisy during calibration
                if (i < noisyFlags.Length && noisyFlags[i])
                {
                    continue; // Completely ignore noisy/phantom axes
                }

                float currentValue = state.Axes[i];
                float baselineValue = baseline[i];
                float delta = Math.Abs(currentValue - baselineValue);

                // Is this movement above the jitter threshold?
                bool isSignificantMovement = delta >= JitterThreshold;

                // Is this movement above the detection threshold?
                bool meetsThreshold = delta >= _axisThreshold;

                if (meetsThreshold && isSignificantMovement)
                {
                    // Increment confirmation counter
                    confirmCounts[i]++;

                    InputDetectionLog.Log($"[InputDetection] Axis {i}: current={currentValue:F4}, baseline={baselineValue:F4}, delta={delta:F4}, confirm={confirmCounts[i]}/{RequiredConfirmationFrames}");

                    // Require sustained movement over multiple frames to confirm
                    if (confirmCounts[i] >= RequiredConfirmationFrames)
                    {
                        InputDetectionLog.Log($"[InputDetection] *** AXIS {i} DETECTED *** on {state.DeviceName}, value={currentValue:F4}, baseline={baselineValue:F4}");
                        return new DetectedInput
                        {
                            DeviceGuid = guid,
                            DeviceName = state.DeviceName,
                            Type = InputType.Axis,
                            Index = i,
                            Value = currentValue
                        };
                    }
                }
                else if (!isSignificantMovement)
                {
                    // Axis returned to near baseline - reset confirmation counter
                    confirmCounts[i] = 0;
                }
                // If significant but not meeting threshold, maintain counter (don't reset)
            }
        }

        // Check hats
        if (_filter.HasFlag(InputDetectionFilter.Hats))
        {
            // Get initial hat state to filter out already-active hats
            if (!_initialHats.TryGetValue(guid, out var initialHats))
                initialHats = new int[state.Hats.Length];

            for (int i = 0; i < state.Hats.Length && i < prevHats.Length; i++)
            {
                // Skip hats that were already active when detection started
                if (i < initialHats.Length && initialHats[i] >= 0)
                {
                    continue; // Hat was active at start - ignore it entirely
                }

                // Detect hat movement transition (was centered last frame, now has direction)
                if (state.Hats[i] >= 0 && prevHats[i] < 0)
                {
                    InputDetectionLog.Log($"[InputDetection] *** HAT {i} DETECTED *** on {state.DeviceName}");
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
