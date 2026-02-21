namespace Launcher.Core.Abstractions;

public interface ILaunchService
{
    Task LaunchAsync(string executablePath, string? arguments = null, string? workingDirectory = null, CancellationToken cancellationToken = default);
}
