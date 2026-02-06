using Asteriq.Models;

namespace Asteriq.Services.Abstractions;

/// <summary>
/// Interface for processing input mappings and outputs to vJoy/keyboard
/// </summary>
public interface IMappingEngine : IDisposable
{
    /// <summary>
    /// Currently active profile
    /// </summary>
    MappingProfile? ActiveProfile { get; }

    /// <summary>
    /// Whether the engine is processing mappings
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Load and activate a mapping profile
    /// </summary>
    void LoadProfile(MappingProfile profile);

    /// <summary>
    /// Start processing mappings
    /// </summary>
    /// <param name="initialStates">Optional initial device states for synchronization.
    /// If provided, vJoy outputs will be set to match current hardware positions.</param>
    bool Start(IEnumerable<DeviceInputState>? initialStates = null);

    /// <summary>
    /// Stop processing mappings
    /// </summary>
    void Stop();

    /// <summary>
    /// Process input state and apply mappings
    /// </summary>
    void ProcessInput(DeviceInputState state);
}
