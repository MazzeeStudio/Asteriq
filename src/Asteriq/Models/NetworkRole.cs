namespace Asteriq.Models;

/// <summary>
/// Indicates whether this Asteriq instance acts as a master (sends post-mapped vJoy state)
/// or client (receives and applies it), or has no network role assigned.
/// </summary>
public enum NetworkRole
{
    /// <summary>No network role configured — networking may still be enabled for discovery.</summary>
    None,

    /// <summary>
    /// This machine runs the full mapping engine and sends post-mapped vJoy snapshots to the client.
    /// </summary>
    Master,

    /// <summary>
    /// This machine receives vJoy snapshots from the master and applies them to its local vJoy slot.
    /// Devices and Mappings tabs are locked while connected.
    /// </summary>
    Client
}
