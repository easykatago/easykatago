namespace Launcher.Core.Abstractions;

public interface IDiagnosticsService
{
    Task<IReadOnlyList<HealthCheckItem>> RunHealthChecksAsync(CancellationToken cancellationToken = default);
    Task<string> ExportZipAsync(string outputFolder, CancellationToken cancellationToken = default);
}

public sealed record HealthCheckItem(string Name, bool IsSuccess, string Message);
