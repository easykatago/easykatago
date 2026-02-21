namespace Launcher.Core.Abstractions;

public interface IVerifyService
{
    Task<bool> VerifySha256Async(string filePath, string expectedSha256, CancellationToken cancellationToken = default);
    bool VerifySize(string filePath, long expectedBytes);
}
