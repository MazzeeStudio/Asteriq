namespace Asteriq.Services.Abstractions;

public enum UpdateStatus
{
    Unknown,
    Checking,
    UpToDate,
    UpdateAvailable,
    Downloading,
    ReadyToApply,
    Error
}

public interface IUpdateService
{
    UpdateStatus Status { get; }
    string? LatestVersion { get; }
    string? DownloadUrl { get; }
    int DownloadProgress { get; }
    DateTime? LastChecked { get; }

    Task CheckAsync(CancellationToken ct = default);
    Task DownloadAsync(CancellationToken ct = default);
    Task ApplyUpdateAsync(CancellationToken ct = default);
}
