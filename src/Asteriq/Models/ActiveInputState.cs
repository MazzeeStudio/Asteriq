namespace Asteriq.Models;

/// <summary>
/// Tracks the state of an active input for fade-out animation.
/// Used to display lead-lines from controls when they're active.
/// </summary>
public class ActiveInputState
{
    /// <summary>
    /// The binding identifier (e.g. "button1", "x", "ry")
    /// </summary>
    public string Binding { get; set; } = "";

    /// <summary>
    /// Current value (-1 to 1 for axes, 0/1 for buttons)
    /// </summary>
    public float Value { get; set; }

    /// <summary>
    /// Time when this input was last changed
    /// </summary>
    public DateTime LastActivity { get; set; } = DateTime.Now;

    /// <summary>
    /// Whether this is an axis (true) or button (false)
    /// </summary>
    public bool IsAxis { get; set; }

    /// <summary>
    /// The control definition from the device map (if mapped)
    /// </summary>
    public ControlDefinition? Control { get; set; }

    /// <summary>
    /// Animation phase for lead-line appearance (0 to 1)
    /// </summary>
    public float AppearProgress { get; set; }

    /// <summary>
    /// Calculate opacity based on fade timing.
    /// Returns 1.0 during active period, fades to 0 after delay.
    /// </summary>
    /// <param name="delaySeconds">Seconds before fade starts (default 3)</param>
    /// <param name="fadeSeconds">Duration of fade (default 2)</param>
    public float GetOpacity(float delaySeconds = 3f, float fadeSeconds = 2f)
    {
        var elapsed = (float)(DateTime.Now - LastActivity).TotalSeconds;

        if (elapsed < delaySeconds)
            return 1f;

        var fadeProgress = (elapsed - delaySeconds) / fadeSeconds;
        return Math.Max(0f, 1f - fadeProgress);
    }

    /// <summary>
    /// Whether this input should still be displayed (opacity > 0)
    /// </summary>
    public bool IsVisible(float delaySeconds = 3f, float fadeSeconds = 2f)
    {
        return GetOpacity(delaySeconds, fadeSeconds) > 0.01f;
    }
}

/// <summary>
/// Manages collection of active inputs with automatic cleanup of faded items
/// </summary>
public class ActiveInputTracker
{
    private readonly Dictionary<string, ActiveInputState> _activeInputs = new();
    private readonly Dictionary<string, float> _axisBaselines = new();
    private readonly object _lock = new();

    public float FadeDelay { get; set; } = 0.5f;
    public float FadeDuration { get; set; } = 2.5f;

    /// <summary>
    /// Threshold for axis movement from baseline to be considered "active"
    /// </summary>
    public float AxisActivationThreshold { get; set; } = 0.15f;

    /// <summary>
    /// Update or add an input state
    /// </summary>
    public void Update(string binding, float value, bool isAxis, ControlDefinition? control = null)
    {
        lock (_lock)
        {
            if (isAxis)
            {
                // Track baseline for axes (first value seen becomes baseline)
                if (!_axisBaselines.ContainsKey(binding))
                {
                    _axisBaselines[binding] = value;
                }
            }

            if (_activeInputs.TryGetValue(binding, out var existing))
            {
                // Only update activity time if value actually changed
                bool changed = isAxis
                    ? Math.Abs(existing.Value - value) > 0.01f
                    : existing.Value != value;

                if (changed)
                {
                    existing.Value = value;
                    existing.LastActivity = DateTime.Now;

                    // For buttons, only track when pressed
                    if (!isAxis && value < 0.5f)
                    {
                        // Button released - let it fade, don't extend activity
                    }
                }
            }
            else
            {
                // New input - only add if it's "active"
                bool isActive;
                if (isAxis)
                {
                    // Axis is active if it moved significantly from its baseline
                    float baseline = _axisBaselines.GetValueOrDefault(binding, 0f);
                    isActive = Math.Abs(value - baseline) > AxisActivationThreshold;
                }
                else
                {
                    isActive = value > 0.5f; // Button pressed
                }

                if (isActive)
                {
                    _activeInputs[binding] = new ActiveInputState
                    {
                        Binding = binding,
                        Value = value,
                        IsAxis = isAxis,
                        Control = control,
                        LastActivity = DateTime.Now,
                        AppearProgress = 0f
                    };
                }
            }
        }
    }

    /// <summary>
    /// Get all currently visible inputs (opacity > 0)
    /// </summary>
    public IReadOnlyList<ActiveInputState> GetVisibleInputs()
    {
        lock (_lock)
        {
            // Clean up completely faded items
            var toRemove = _activeInputs
                .Where(kv => !kv.Value.IsVisible(FadeDelay, FadeDuration))
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                _activeInputs.Remove(key);
            }

            return _activeInputs.Values.ToList();
        }
    }

    /// <summary>
    /// Update animation progress for all active inputs
    /// </summary>
    public void UpdateAnimations(float deltaTime)
    {
        lock (_lock)
        {
            foreach (var input in _activeInputs.Values)
            {
                if (input.AppearProgress < 1f)
                {
                    input.AppearProgress = Math.Min(1f, input.AppearProgress + deltaTime * 3f); // 0.33s to appear
                }
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _activeInputs.Clear();
            _axisBaselines.Clear();
        }
    }
}
