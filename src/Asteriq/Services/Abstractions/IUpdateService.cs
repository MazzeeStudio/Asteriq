namespace Asteriq.Services.Abstractions;

public enum UpdateStatus
{
    Unknown,
    Checking,
    UpToDate,
    UpdateAvailable,
    Error
}

public interface IUpdateService
{
    UpdateStatus Status { get; }
    string? LatestVersion { get; }
    string? DownloadUrl { get; }

    Task CheckAsync(CancellationToken ct = default);
    Task DownloadAndInstallAsync(IProgress<int>? progress = null, CancellationToken ct = default);
}
