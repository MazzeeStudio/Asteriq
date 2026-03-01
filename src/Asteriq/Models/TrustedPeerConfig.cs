namespace Asteriq.Models;

/// <summary>
/// Identifies a trusted master machine stored on the client side.
/// When a Hello arrives from this machine with a matching code, the connection
/// is auto-accepted silently without showing a trust dialog.
/// </summary>
public sealed class TrustedPeerConfig
{
    /// <summary>Machine name of the trusted master.</summary>
    public string MachineName { get; set; } = "";

    /// <summary>Permanent 6-digit trust code that the master generated.</summary>
    public string Code { get; set; } = "";

    /// <summary>Last known IP address of the master (for display only).</summary>
    public string LastIp { get; set; } = "";
}
