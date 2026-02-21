using Launcher.Core.Abstractions;

namespace Launcher.Core.Services;

public sealed class DefaultVerifyService : IVerifyService
{
    public Task<bool> VerifySha256Async(string filePath, string expectedSha256, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Sha256Verifier.Verify(filePath, expectedSha256));
    }

    public bool VerifySize(string filePath, long expectedBytes)
    {
        if (expectedBytes <= 0 || !File.Exists(filePath))
        {
            return false;
        }

        return new FileInfo(filePath).Length == expectedBytes;
    }
}
