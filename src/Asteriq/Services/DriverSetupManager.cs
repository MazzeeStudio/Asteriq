using Asteriq.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Asteriq.Services;

/// <summary>
/// Manages detection and installation of required drivers (vJoy and HidHide).
/// Installation is handled via Windows Package Manager (winget).
/// </summary>
public class DriverSetupManager
{
    private const string VJOY_RELEASES_URL = "https://github.com/jshafer817/vJoy/releases";
    private const string HIDHIDE_RELEASES_URL = "https://github.com/nefarius/HidHide/releases";

    public const string VJoyWinGetId = "ShaulEizikovich.vJoyDeviceDriver";
    public const string HidHideWinGetId = "Nefarius.HidHide";

    private readonly ILogger<DriverSetupManager> _logger;
    private readonly IHidHideService _hidHideService;

    public DriverSetupManager(ILogger<DriverSetupManager> logger, IHidHideService hidHideService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hidHideService = hidHideService ?? throw new ArgumentNullException(nameof(hidHideService));
    }

    #region vJoy Detection

    /// <summary>
    /// Check if vJoy driver is installed
    /// </summary>
    public bool IsVJoyInstalled()
    {
        try
        {
            // Check registry for vJoy service
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\vjoy");
            if (key is not null)
            {
                _logger.LogInformation("vJoy detected: service key found at HKLM\\SYSTEM\\CurrentControlSet\\Services\\vjoy");
                return true;
            }

            // Alternative: Check if vJoy device exists
            using var devKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\ROOT\HIDCLASS");
            if (devKey is not null)
            {
                foreach (var subKeyName in devKey.GetSubKeyNames())
                {
                    if (subKeyName.Contains("VID_1234&PID_BEAD", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("vJoy detected: device found in HIDCLASS registry: {DeviceKey}", subKeyName);
                        return true;
                    }
                }
            }

            _logger.LogInformation("vJoy not detected in registry");
            return false;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _logger.LogWarning(ex, "Failed to check vJoy installation status");
            return false;
        }
    }

    /// <summary>
    /// Get vJoy installation path
    /// </summary>
    public static string? GetVJoyInstallPath()
    {
        try
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var vJoyPath = Path.Combine(programFiles, "vJoy");

            if (Directory.Exists(vJoyPath))
                return vJoyPath;

            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var vJoyPathX86 = Path.Combine(programFilesX86, "vJoy");

            if (Directory.Exists(vJoyPathX86))
                return vJoyPathX86;

            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{8E31F76F-74C3-47F1-9550-E041EEDC5FBB}_is1");
            if (key is not null)
            {
                var installLocation = key.GetValue("InstallLocation") as string;
                if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                    return installLocation;
            }

            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Get path to vJoyConfig.exe
    /// </summary>
    public static string? GetVJoyConfigPath()
    {
        var installPath = GetVJoyInstallPath();
        if (string.IsNullOrEmpty(installPath))
            return null;

        var configPathX64 = Path.Combine(installPath, "x64", "vJoyConfig.exe");
        if (File.Exists(configPathX64))
            return configPathX64;

        var configPathX86 = Path.Combine(installPath, "x86", "vJoyConfig.exe");
        if (File.Exists(configPathX86))
            return configPathX86;

        var configPath = Path.Combine(installPath, "vJoyConfig.exe");
        if (File.Exists(configPath))
            return configPath;

        return null;
    }

    #endregion

    #region HidHide Detection

    /// <summary>
    /// Check if HidHide is installed
    /// </summary>
    public bool IsHidHideInstalled()
    {
        var isAvailable = _hidHideService.IsAvailable();
        _logger.LogTrace("HidHide availability: {IsAvailable}", isAvailable);
        return isAvailable;
    }

    #endregion

    #region WinGet

    /// <summary>
    /// Returns true if the winget CLI is available on this machine.
    /// </summary>
    public static bool IsWinGetAvailable()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Installs a package via winget. Streams output lines to <paramref name="log"/>.
    /// winget handles UAC elevation internally for packages that require it.
    /// </summary>
    public async Task<bool> InstallViaWinGetAsync(string packageId, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Installing {PackageId} via winget", packageId);
        log?.Invoke($"Running: winget install --id {packageId}");

        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "winget",
                Arguments = $"install --id {packageId} --silent --accept-package-agreements --accept-source-agreements",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            process.OutputDataReceived += (_, e) => { if (e.Data is not null) log?.Invoke(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) log?.Invoke(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            var success = process.ExitCode == 0;
            _logger.LogInformation("winget install {PackageId} exited with code {ExitCode}", packageId, process.ExitCode);
            log?.Invoke(success ? "Installation complete." : $"winget exited with code {process.ExitCode}.");
            return success;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException or TaskCanceledException)
        {
            _logger.LogError(ex, "winget install failed for {PackageId}", packageId);
            log?.Invoke($"Error: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region URLs

    public static string GetVJoyReleasesUrl() => VJOY_RELEASES_URL;
    public static string GetHidHideReleasesUrl() => HIDHIDE_RELEASES_URL;

    #endregion

    #region Setup Status

    /// <summary>
    /// Get overall driver setup status
    /// </summary>
    public DriverSetupStatus GetSetupStatus()
    {
        return new DriverSetupStatus
        {
            VJoyInstalled = IsVJoyInstalled(),
            VJoyInstallPath = GetVJoyInstallPath(),
            VJoyConfigPath = GetVJoyConfigPath(),
            HidHideInstalled = IsHidHideInstalled()
        };
    }

    #endregion
}

/// <summary>
/// Status of driver installation
/// </summary>
public class DriverSetupStatus
{
    public bool VJoyInstalled { get; set; }
    public string? VJoyInstallPath { get; set; }
    public string? VJoyConfigPath { get; set; }
    public bool HidHideInstalled { get; set; }

    public bool IsComplete => VJoyInstalled;
    public bool CanConfigureVJoy => !string.IsNullOrEmpty(VJoyConfigPath);
}
