using System.Net;
using Launcher.Core.Abstractions;

namespace Launcher.Core.Services;

public sealed class HttpDownloadService(HttpClient httpClient) : IDownloadService
{
    public async Task DownloadAsync(
        Uri source,
        string targetPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? ".");

        long existingLength = 0;
        if (File.Exists(targetPath))
        {
            existingLength = new FileInfo(targetPath).Length;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        if (existingLength > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingLength, null);
        }

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.OK && existingLength > 0)
        {
            existingLength = 0;
        }

        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength.HasValue ? response.Content.Headers.ContentLength + existingLength : null;

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(
            targetPath,
            existingLength > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);

        var buffer = new byte[81920];
        long received = existingLength;
        while (true)
        {
            var read = await responseStream.ReadAsync(buffer, cancellationToken);
            if (read <= 0)
            {
                break;
            }

            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            double? percentage = total.HasValue && total > 0 ? (double)received / total.Value * 100 : null;
            progress?.Report(new DownloadProgress(received, total, percentage));
        }
    }
}
