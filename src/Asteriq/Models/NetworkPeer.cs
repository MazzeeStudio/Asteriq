namespace Asteriq.Models;

/// <summary>
/// A remote Asteriq instance discovered via UDP broadcast on the LAN.
/// </summary>
public class NetworkPeer
{
    /// <summary>Machine name broadcast by the remote instance.</summary>
    public string MachineName { get; set; } = "";

    /// <summary>IP address of the remote instance.</summary>
    public string IpAddress { get; set; } = "";

    /// <summary>TCP port the remote instance is listening on.</summary>
    public int TcpPort { get; set; }

    /// <summary>Network role advertised by the remote instance (Master, Client, or None if unconfigured).</summary>
    public NetworkRole Role { get; set; } = NetworkRole.None;

    /// <summary>UTC timestamp of the last broadcast received from this peer.</summary>
    public DateTime LastSeen { get; set; }

    /// <summary>True if no broadcast has been received within the stale threshold (15 s).</summary>
    public bool IsStale => (DateTime.UtcNow - LastSeen).TotalSeconds > 15;
}
