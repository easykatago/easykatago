using Launcher.Core.Models;

namespace Launcher.Core.Abstractions;

public interface IInstallService
{
    Task<string> InstallAsync(ComponentModel component, string archivePath, string installRoot, CancellationToken cancellationToken = default);
}
