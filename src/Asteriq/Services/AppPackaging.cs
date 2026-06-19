using System.Runtime.InteropServices;

namespace Asteriq.Services;

/// <summary>
/// Detects whether Asteriq is running as a packaged app (MSIX / Microsoft Store) or as an
/// unpackaged desktop app (the GitHub .zip / .msi distribution).
///
/// This matters for updates: a Store-installed MSIX is delivered and updated by the Store
/// and lives in the read-only WindowsApps location, so the GitHub self-updater must not run
/// for it. The unpackaged distribution continues to self-update via GitHub releases.
/// </summary>
internal static class AppPackaging
{
    // GetCurrentPackageFullName returns this code when the process has no package identity.
    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);

    private static readonly Lazy<bool> PackagedValue = new(Detect);

    /// <summary>
    /// True when running from a Microsoft Store / MSIX install (the Store manages updates).
    /// False for the unpackaged GitHub distribution (self-updates via GitHub releases).
    /// </summary>
    public static bool IsPackaged => PackagedValue.Value;

    private static bool Detect()
    {
        // Query with a zero-length null buffer: a packaged process returns
        // ERROR_INSUFFICIENT_BUFFER, an unpackaged one returns APPMODEL_ERROR_NO_PACKAGE.
        int length = 0;
        int rc = GetCurrentPackageFullName(ref length, null);
        return rc != APPMODEL_ERROR_NO_PACKAGE;
    }
}
