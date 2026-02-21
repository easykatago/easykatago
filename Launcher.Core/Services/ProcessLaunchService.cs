using System.Diagnostics;
using Launcher.Core.Abstractions;

namespace Launcher.Core.Services;

public sealed class ProcessLaunchService : ILaunchService
{
    public Task LaunchAsync(string executablePath, string? arguments = null, string? workingDirectory = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? (Path.GetDirectoryName(executablePath) ?? ".")
        };

        Process.Start(psi);
        return Task.CompletedTask;
    }
}
