using Launcher.Core.Abstractions;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class JsonProfileService(string profilePath) : IProfileService
{
    public Task<ProfilesDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        return JsonFileStore.LoadAsync<ProfilesDocument>(profilePath, cancellationToken);
    }

    public Task SaveAsync(ProfilesDocument profiles, CancellationToken cancellationToken = default)
    {
        return JsonFileStore.SaveAsync(profilePath, profiles, cancellationToken);
    }

    public ProfileModel? GetDefault(ProfilesDocument profiles)
    {
        var id = profiles.DefaultProfileId;
        return id is null ? profiles.Profiles.FirstOrDefault() : profiles.Profiles.FirstOrDefault(p => p.ProfileId == id);
    }

    public ProfilesDocument Upsert(ProfilesDocument profiles, ProfileModel profile, bool setAsDefault)
    {
        var nextProfiles = profiles.Profiles.Where(p => p.ProfileId != profile.ProfileId).Append(profile).ToList();
        return profiles with
        {
            Profiles = nextProfiles,
            DefaultProfileId = setAsDefault ? profile.ProfileId : profiles.DefaultProfileId
        };
    }

    public ProfilesDocument Remove(ProfilesDocument profiles, string profileId)
    {
        var nextProfiles = profiles.Profiles.Where(p => p.ProfileId != profileId).ToList();
        var nextDefault = profiles.DefaultProfileId == profileId ? nextProfiles.FirstOrDefault()?.ProfileId : profiles.DefaultProfileId;
        return profiles with
        {
            Profiles = nextProfiles,
            DefaultProfileId = nextDefault
        };
    }
}
