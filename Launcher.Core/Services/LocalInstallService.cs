using Launcher.Core.Abstractions;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class LocalInstallService : IInstallService
{
    public Task<string> InstallAsync(ComponentModel component, string archivePath, string installRoot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var folder = Path.Combine(installRoot, "components", component.Name.ToLowerInvariant(), component.Version);
        Directory.CreateDirectory(folder);
        var fileName = Path.GetFileName(archivePath);
        var targetFile = Path.Combine(folder, fileName);
        File.Copy(archivePath, targetFile, overwrite: true);
        return Task.FromResult(targetFile);
    }
}
