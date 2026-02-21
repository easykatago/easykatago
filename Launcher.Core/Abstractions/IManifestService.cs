using Launcher.Core.Models;

namespace Launcher.Core.Abstractions;

public interface IManifestService
{
    Task<ManifestModel> LoadAsync(CancellationToken cancellationToken = default);
    IReadOnlyList<ComponentModel> ListComponents(ManifestModel manifest, string kind);
    DefaultSelectionModel GetDefaults(ManifestModel manifest);
}
