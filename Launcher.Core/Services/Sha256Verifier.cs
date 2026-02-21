using System.Security.Cryptography;

namespace Launcher.Core.Services;

public static class Sha256Verifier
{
    public static string Compute(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var hasher = SHA256.Create();
        var hash = hasher.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool Verify(string filePath, string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            return false;
        }

        return string.Equals(Compute(filePath), expectedSha256.Trim().ToLowerInvariant(), StringComparison.Ordinal);
    }
}
