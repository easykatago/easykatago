using Launcher.Core.Abstractions;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class LocalManifestService(string manifestPath) : IManifestService
{
    public Task<ManifestModel> LoadAsync(CancellationToken cancellationToken = default)
    {
        return JsonFileStore.LoadAsync<ManifestModel>(manifestPath, cancellationToken);
    }

    public IReadOnlyList<ComponentModel> ListComponents(ManifestModel manifest, string kind)
    {
        return kind.ToLowerInvariant() switch
        {
            "katago" => manifest.Components.Katago,
            "lizzieyzy" => manifest.Components.Lizzieyzy,
            "networks" => manifest.Components.Networks,
            "configs" => manifest.Components.Configs,
            "jre" => manifest.Components.Jre,
            _ => []
        };
    }

    public DefaultSelectionModel GetDefaults(ManifestModel manifest) => manifest.Defaults;
}
