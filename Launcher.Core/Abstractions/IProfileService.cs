using Launcher.Core.Models;

namespace Launcher.Core.Abstractions;

public interface IProfileService
{
    Task<ProfilesDocument> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ProfilesDocument profiles, CancellationToken cancellationToken = default);
    ProfileModel? GetDefault(ProfilesDocument profiles);
    ProfilesDocument Upsert(ProfilesDocument profiles, ProfileModel profile, bool setAsDefault);
    ProfilesDocument Remove(ProfilesDocument profiles, string profileId);
}
