using Asteriq.Models;
using Asteriq.UI;

namespace Asteriq.Services.Abstractions;

/// <summary>
/// Interface for UI theme and visual settings
/// </summary>
public interface IUIThemeService
{
    /// <summary>
    /// Theme setting
    /// </summary>
    FUITheme Theme { get; set; }

    /// <summary>
    /// Event fired when theme setting changes
    /// </summary>
    event EventHandler<FUITheme>? ThemeChanged;

    /// <summary>
    /// Load background settings
    /// </summary>
    (int gridStrength, int glowIntensity, int noiseIntensity, int scanlineIntensity, int vignetteStrength) LoadBackgroundSettings();

    /// <summary>
    /// Save background settings
    /// </summary>
    void SaveBackgroundSettings(int gridStrength, int glowIntensity, int noiseIntensity, int scanlineIntensity, int vignetteStrength);
}
