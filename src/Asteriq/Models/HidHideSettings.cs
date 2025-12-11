namespace Asteriq.Models;

/// <summary>
/// Settings for HidHide integration
/// </summary>
public class HidHideSettings
{
    /// <summary>
    /// Default installation path for HidHide CLI
    /// </summary>
    public const string DefaultCliPath = @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe";

    /// <summary>
    /// Path to the HidHide CLI executable
    /// </summary>
    public string CliPath { get; set; } = DefaultCliPath;
}
