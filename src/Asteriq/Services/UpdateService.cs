using System.Net.Http.Json;
using System.Reflection;
using Asteriq.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Asteriq.Services;

public sealed class UpdateService : IUpdateService
{
    private const string Owner = "MazzeeStudio";
    private const string Repo = "Asteriq";
    private const string AssetName = "Asteriq.exe";
    private const string ApiUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UpdateService> _logger;

    public UpdateStatus Status { get; private set; } = UpdateStatus.Unknown;
    public string? LatestVersion { get; private set; }
    public string? DownloadUrl { get; private set; }

    public UpdateService(IHttpClientFactory httpClientFactory, ILogger<UpdateService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task CheckAsync(CancellationToken ct = default)
    {
        Status = UpdateStatus.Checking;
        try
        {
            var client = _httpClientFactory.CreateClient("Asteriq");
            var release = await client.GetFromJsonAsync<GitHubRelease>(ApiUrl, ct);

            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                Status = UpdateStatus.UpToDate;
                return;
            }

            string latestTag = release.TagName.TrimStart('v');
            string current = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "0.0.0";

            LatestVersion = latestTag;

            if (IsNewer(latestTag, current))
            {
                DownloadUrl = release.Assets
                    ?.FirstOrDefault(a => a.Name.Equals(AssetName, StringComparison.OrdinalIgnoreCase))
                    ?.BrowserDownloadUrl;

                Status = UpdateStatus.UpdateAvailable;
                _logger.LogInformation("Update available: {Latest} (current: {Current})", latestTag, current);
            }
            else
            {
                Status = UpdateStatus.UpToDate;
                _logger.LogDebug("Up to date: {Current}", current);
            }
        }
        catch (HttpRequestException ex)
        {
            // Network unavailable or no releases — fail silently
            _logger.LogDebug("Update check failed (network): {Message}", ex.Message);
            Status = UpdateStatus.Error;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug("Update check failed: {Message}", ex.Message);
            Status = UpdateStatus.Error;
        }
    }

    public async Task DownloadAndInstallAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (DownloadUrl is null)
        {
            Status = UpdateStatus.Error;
            return;
        }

        string? currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe))
        {
            _logger.LogError("Cannot determine current executable path for update.");
            Status = UpdateStatus.Error;
            return;
        }

        Status = UpdateStatus.Checking; // reuse Checking as "downloading" visual state

        try
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "Asteriq_update.exe");

            // Download
            var client = _httpClientFactory.CreateClient("Asteriq");
            using var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var file = File.Create(tempPath);

            var buffer = new byte[81920];
            long downloaded = 0;
            int read;

            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                if (total > 0)
                    progress?.Report((int)(downloaded * 100 / total.Value));
            }

            await file.FlushAsync(ct);
            file.Close();

            // Write swap script and launch it
            string scriptPath = Path.Combine(Path.GetTempPath(), "asteriq_update.ps1");
            string script = $$"""
                $copied = $false
                for ($i = 0; $i -lt 5; $i++) {
                    Start-Sleep -Seconds 2
                    try {
                        Copy-Item -Path '{{tempPath}}' -Destination '{{currentExe}}' -Force -ErrorAction Stop
                        $copied = $true
                        break
                    } catch {
                        # Exe may still be locked, retry
                    }
                }
                if ($copied) {
                    Start-Process -FilePath '{{currentExe}}'
                }
                Remove-Item -Path '{{tempPath}}' -ErrorAction SilentlyContinue
                Remove-Item -Path '{{scriptPath}}' -ErrorAction SilentlyContinue
                """;

            await File.WriteAllTextAsync(scriptPath, script, ct);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            });

            // Exit from the thread pool — safe from any thread unlike Application.Exit()
            Environment.Exit(0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Update download/install failed.");
            Status = UpdateStatus.Error;
        }
    }

    private static bool IsNewer(string latest, string current)
    {
        if (!Version.TryParse(latest, out var l)) return false;
        if (!Version.TryParse(current, out var c)) return false;
        return l > c;
    }

    // Minimal GitHub API response shapes
    private sealed record GitHubRelease(
        [property: System.Text.Json.Serialization.JsonPropertyName("tag_name")] string TagName,
        [property: System.Text.Json.Serialization.JsonPropertyName("assets")] List<GitHubAsset>? Assets);

    private sealed record GitHubAsset(
        [property: System.Text.Json.Serialization.JsonPropertyName("name")] string Name,
        [property: System.Text.Json.Serialization.JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}
