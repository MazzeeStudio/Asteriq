using Microsoft.Win32;

namespace Asteriq.Services;

/// <summary>
/// Manages detection and installation of required drivers (vJoy)
/// </summary>
public class DriverSetupManager
{
    private const string VJOY_RELEASES_URL = "https://github.com/jshafer817/vJoy/releases";
    private const string VJOY_DOWNLOAD_URL = "https://github.com/jshafer817/vJoy/releases/download/v2.1.9.1/vJoySetup.exe";
    private const string HIDHIDE_RELEASES_URL = "https://github.com/nefarius/HidHide/releases";
    private const string HIDHIDE_DOWNLOAD_URL = "https://github.com/nefarius/HidHide/releases/download/v1.5.230/HidHide_1.5.230_x64.exe";

    private readonly string _downloadPath;
    private readonly HttpClient _httpClient;

    public DriverSetupManager()
    {
        _downloadPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Asteriq",
            "Downloads");

        Directory.CreateDirectory(_downloadPath);

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Asteriq/1.0");
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
                        return true;
                    }
                }
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Get vJoy installation path
    /// </summary>
    public string? GetVJoyInstallPath()
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
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Get path to vJoyConfig.exe
    /// </summary>
    public string? GetVJoyConfigPath()
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
        var hidHide = new HidHideService();
        return hidHide.IsAvailable();
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
            using var response = await _httpClient.GetAsync(VJOY_DOWNLOAD_URL, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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

            return filePath;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Download HidHide installer
    /// </summary>
    public async Task<string?> DownloadHidHideInstallerAsync(IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var fileName = "HidHideSetup.exe";
        var filePath = Path.Combine(_downloadPath, fileName);

        try
        {
            using var response = await _httpClient.GetAsync(HIDHIDE_DOWNLOAD_URL, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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

            return filePath;
        }
        catch (Exception)
        {
            return null;
        }
    }

    #endregion

    #region Driver Installation

    /// <summary>
    /// Launch vJoy installer (requires user interaction)
    /// </summary>
    public bool LaunchVJoyInstaller(string installerPath)
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
                Verb = "runas" // Request elevation
            });

            return process is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Launch HidHide installer (requires user interaction)
    /// </summary>
    public bool LaunchHidHideInstaller(string installerPath)
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
                Verb = "runas" // Request elevation
            });

            return process is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    #endregion

    #region URLs

    /// <summary>
    /// Get vJoy releases URL for manual download
    /// </summary>
    public string GetVJoyReleasesUrl() => VJOY_RELEASES_URL;

    /// <summary>
    /// Get HidHide releases URL for manual download
    /// </summary>
    public string GetHidHideReleasesUrl() => HIDHIDE_RELEASES_URL;

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

    public bool IsComplete => VJoyInstalled; // Only vJoy is required
    public bool CanConfigureVJoy => !string.IsNullOrEmpty(VJoyConfigPath);
}
