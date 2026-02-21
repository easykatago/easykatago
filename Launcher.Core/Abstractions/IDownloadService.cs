namespace Launcher.Core.Abstractions;

public interface IDownloadService
{
    Task DownloadAsync(
        Uri source,
        string targetPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record DownloadProgress(long BytesReceived, long? TotalBytes, double? Percentage);
