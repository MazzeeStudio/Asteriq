using System.Text.Json;
using Asteriq.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Asteriq.Services;

/// <summary>
/// Manages window state persistence (size and position)
/// </summary>
public class WindowStateManager : IWindowStateManager
{
    private readonly ILogger<WindowStateManager> _logger;
    private readonly string _settingsFile;
    private readonly JsonSerializerOptions _jsonOptions;
    private WindowState _cachedState;

    public WindowStateManager(ILogger<WindowStateManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var asteriqDir = Path.Combine(appData, "Asteriq");
        _settingsFile = Path.Combine(asteriqDir, "windowstate.json");

        Directory.CreateDirectory(asteriqDir);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Load and cache window state
        _cachedState = LoadState();
        _logger.LogDebug("WindowStateManager initialized. Window size: {Width}x{Height}", _cachedState.WindowWidth, _cachedState.WindowHeight);
    }

    public (int width, int height, int x, int y) LoadWindowState()
    {
        return (_cachedState.WindowWidth, _cachedState.WindowHeight, _cachedState.WindowX, _cachedState.WindowY);
    }

    public void SaveWindowState(int width, int height, int x, int y)
    {
        _cachedState.WindowWidth = width;
        _cachedState.WindowHeight = height;
        _cachedState.WindowX = x;
        _cachedState.WindowY = y;
        SaveState(_cachedState);
    }

    private WindowState LoadState()
    {
        if (!File.Exists(_settingsFile))
        {
            _logger.LogDebug("Window state file not found, using defaults");
            return new WindowState();
        }

        try
        {
            var json = File.ReadAllText(_settingsFile);
            return JsonSerializer.Deserialize<WindowState>(json, _jsonOptions) ?? new WindowState();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to load window state from {SettingsFile}, using defaults", _settingsFile);
            return new WindowState();
        }
    }

    private void SaveState(WindowState state)
    {
        var directory = Path.GetDirectoryName(_settingsFile);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(state, _jsonOptions);
        File.WriteAllText(_settingsFile, json);
    }

    private class WindowState
    {
        public int WindowWidth { get; set; } = 1280;
        public int WindowHeight { get; set; } = 800;
        public int WindowX { get; set; } = 100;
        public int WindowY { get; set; } = 100;
    }
}
