namespace Asteriq.Models;

/// <summary>
/// Event args for profile changed events
/// </summary>
public class ProfileChangedEventArgs : EventArgs
{
    public MappingProfile? OldProfile { get; }
    public MappingProfile? NewProfile { get; }

    public ProfileChangedEventArgs(MappingProfile? oldProfile, MappingProfile? newProfile)
    {
        OldProfile = oldProfile;
        NewProfile = newProfile;
    }
}
