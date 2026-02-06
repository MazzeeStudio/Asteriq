namespace Asteriq.Services.Abstractions;

/// <summary>
/// Interface for persisting window state (size and position)
/// </summary>
public interface IWindowStateManager
{
    /// <summary>
    /// Load window state (size and position)
    /// </summary>
    (int width, int height, int x, int y) LoadWindowState();

    /// <summary>
    /// Save window state (size and position)
    /// </summary>
    void SaveWindowState(int width, int height, int x, int y);
}
