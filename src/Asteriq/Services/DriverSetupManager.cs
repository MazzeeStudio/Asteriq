using System.Net.Http.Json;
using Asteriq.Services.Abstractions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Asteriq.Services;

/// <summary>
/// Manages detection and installation of required drivers (vJoy and HidHide)
/// </summary>
public class DriverSetupManager
{
    private const string VJOY_RELEASES_URL = "https://github.com/jshafer817/vJoy/releases";
    private const string VJOY_DOWNLOAD_URL = "https://github.com/jshafer817/vJoy/releases/download/v2.1.9.1/vJoySetup.exe";
    private const string HIDHIDE_RELEASES_URL = "https://github.com/nefarius/HidHide/releases";
    private const string HIDHIDE_API_URL = "https://api.github.com/repos/nefarius/HidHide/releases/latest";

    public const string VJoyWinGetId = "ShaulEizikovich.vJoyDeviceDriver";
    public const string HidHideWinGetId = "Nefarius.HidHide";

    private readonly ILogger<DriverSetupManager> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHidHideService _hidHideService;
    private readonly string _downloadPath;

    /// <summary>
    /// Constructor with dependency injection
    /// </summary>
    public DriverSetupManager(
        ILogger<DriverSetupManager> logger,
        IHttpClientFactory httpClientFactory,
        IHidHideService hidHideService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _hidHideService = hidHideService ?? throw new ArgumentNullException(nameof(hidHideService));

        _downloadPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Asteriq",
            "Downloads");

        Directory.CreateDirectory(_downloadPath);
        _logger.LogDebug("DriverSetupManager initialized. Download path: {DownloadPath}", _downloadPath);
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
                _logger.LogTrace("vJoy service found in registry");
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
                        _logger.LogTrace("vJoy device found in registry: {DeviceKey}", subKeyName);
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
            // Check common installation paths
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var vJoyPath = Path.Combine(programFiles, "vJoy");

            if (Directory.Exists(vJoyPath))
                return vJoyPath;

            // Try x86 path
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var vJoyPathX86 = Path.Combine(programFilesX86, "vJoy");

            if (Directory.Exists(vJoyPathX86))
                return vJoyPathX86;

            // Try registry
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

        // Check x64 folder first
        var configPathX64 = Path.Combine(installPath, "x64", "vJoyConfig.exe");
        if (File.Exists(configPathX64))
            return configPathX64;

        // Fall back to x86
        var configPathX86 = Path.Combine(installPath, "x86", "vJoyConfig.exe");
        if (File.Exists(configPathX86))
            return configPathX86;

        // Check root
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

    #region Driver Download

    /// <summary>
    /// Download vJoy installer
    /// </summary>
    public async Task<string?> DownloadVJoyInstallerAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var fileName = "vJoySetup.exe";
        var filePath = Path.Combine(_downloadPath, fileName);

        try
        {
            using var httpClient = _httpClientFactory.CreateClient("Asteriq");
            using var response = await httpClient.GetAsync(VJOY_DOWNLOAD_URL, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            var totalBytesRead = 0L;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalBytesRead += bytesRead;

                if (totalBytes > 0 && progress is not null)
                {
                    var percentComplete = (int)((totalBytesRead * 100) / totalBytes);
                    progress.Report(percentComplete);
                }
            }

            _logger.LogInformation("vJoy installer downloaded successfully to {FilePath}", filePath);
            return filePath;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            _logger.LogError(ex, "Failed to download vJoy installer from {Url}", VJOY_DOWNLOAD_URL);
            return null;
        }
    }

    /// <summary>
    /// Download HidHide installer (resolves latest release asset from GitHub API)
    /// </summary>
    public async Task<string?> DownloadHidHideInstallerAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var fileName = "HidHideSetup.exe";
        var filePath = Path.Combine(_downloadPath, fileName);

        try
        {
            using var httpClient = _httpClientFactory.CreateClient("Asteriq");

            // Resolve the latest release asset URL dynamically so a version bump never breaks the download
            _logger.LogInformation("Resolving latest HidHide release from {Url}", HIDHIDE_API_URL);
            var release = await httpClient.GetFromJsonAsync<GitHubRelease>(HIDHIDE_API_URL, cancellationToken);
            var downloadUrl = release?.Assets
                ?.FirstOrDefault(a => a.Name.EndsWith("_x64.exe", StringComparison.OrdinalIgnoreCase))
                ?.BrowserDownloadUrl;

            if (string.IsNullOrEmpty(downloadUrl))
            {
                _logger.LogError("Could not find a HidHide x64 installer asset in the latest release");
                return null;
            }

            _logger.LogInformation("Downloading HidHide installer from {Url}", downloadUrl);

            using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            _logger.LogDebug("HidHide installer size: {TotalBytes} bytes", totalBytes);

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            var totalBytesRead = 0L;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalBytesRead += bytesRead;

                if (totalBytes > 0 && progress is not null)
                {
                    var percentComplete = (int)((totalBytesRead * 100) / totalBytes);
                    progress.Report(percentComplete);
                }
            }

            _logger.LogInformation("HidHide installer downloaded successfully to {FilePath}", filePath);
            return filePath;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            _logger.LogError(ex, "Failed to download HidHide installer from {Url}", HIDHIDE_API_URL);
            return null;
        }
    }

    #endregion

    #region Driver Installation

    /// <summary>
    /// Launch vJoy installer (requires user interaction and elevation)
    /// </summary>
    public bool LaunchVJoyInstaller(string installerPath)
    {
        try
        {
            _logger.LogInformation("Launching vJoy installer: {InstallerPath}", installerPath);

            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
                Verb = "runas" // Request elevation
            });

            var success = process is not null;
            _logger.LogInformation("vJoy installer launch {Result}", success ? "succeeded" : "failed");
            return success;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to launch vJoy installer at {InstallerPath}", installerPath);
            return false;
        }
    }

    /// <summary>
    /// Launch HidHide installer (requires user interaction and elevation)
    /// </summary>
    public bool LaunchHidHideInstaller(string installerPath)
    {
        try
        {
            _logger.LogInformation("Launching HidHide installer: {InstallerPath}", installerPath);

            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
                Verb = "runas" // Request elevation
            });

            var success = process is not null;
            _logger.LogInformation("HidHide installer launch {Result}", success ? "succeeded" : "failed");
            return success;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to launch HidHide installer at {InstallerPath}", installerPath);
            return false;
        }
    }

    #endregion

    #region URLs

    /// <summary>
    /// Get vJoy releases URL for manual download
    /// </summary>
    public static string GetVJoyReleasesUrl() => VJOY_RELEASES_URL;

    /// <summary>
    /// Get HidHide releases URL for manual download
    /// </summary>
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

    // Minimal GitHub API response shapes
    private sealed record GitHubRelease(
        [property: System.Text.Json.Serialization.JsonPropertyName("tag_name")] string TagName,
        [property: System.Text.Json.Serialization.JsonPropertyName("assets")] List<GitHubAsset>? Assets);

    private sealed record GitHubAsset(
        [property: System.Text.Json.Serialization.JsonPropertyName("name")] string Name,
        [property: System.Text.Json.Serialization.JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
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

    public bool IsComplete => VJoyInstalled; // Only vJoy is required
    public bool CanConfigureVJoy => !string.IsNullOrEmpty(VJoyConfigPath);
}
