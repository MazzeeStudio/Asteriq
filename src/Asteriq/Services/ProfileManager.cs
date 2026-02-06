using Asteriq.Models;
using Asteriq.Services.Abstractions;

namespace Asteriq.Services;

/// <summary>
/// Manages the currently active mapping profile
/// </summary>
public class ProfileManager : IProfileManager
{
    private readonly IProfileRepository _repository;
    private readonly IApplicationSettingsService _settings;
    private MappingProfile? _activeProfile;

    public event EventHandler<ProfileChangedEventArgs>? ProfileChanged;

    public MappingProfile? ActiveProfile
    {
        get => _activeProfile;
        private set
        {
            var oldProfile = _activeProfile;
            _activeProfile = value;
            if (oldProfile != value)
            {
                ProfileChanged?.Invoke(this, new ProfileChangedEventArgs(oldProfile, value));
            }
        }
    }

    public bool HasActiveProfile => _activeProfile is not null;

    public ProfileManager(IProfileRepository repository, IApplicationSettingsService settings)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public bool ActivateProfile(Guid profileId)
    {
        var profile = _repository.LoadProfile(profileId);
        if (profile is null)
            return false;

        ActiveProfile = profile;
        _settings.LastProfileId = profileId;
        return true;
    }

    public void ActivateProfile(MappingProfile profile)
    {
        ActiveProfile = profile;
        _settings.LastProfileId = profile.Id;
    }

    public void DeactivateProfile()
    {
        ActiveProfile = null;
    }

    public void SaveActiveProfile()
    {
        if (ActiveProfile is not null)
        {
            _repository.SaveProfile(ActiveProfile);
        }
    }

    public MappingProfile CreateAndActivateProfile(string name, string description = "")
    {
        var profile = _repository.CreateProfile(name, description);
        ActivateProfile(profile);
        return profile;
    }

    public void Initialize()
    {
        if (!_settings.AutoLoadLastProfile || _settings.LastProfileId is null)
            return;

        var profile = _repository.LoadProfile(_settings.LastProfileId.Value);
        if (profile is not null)
        {
            ActiveProfile = profile;
        }
    }
}
