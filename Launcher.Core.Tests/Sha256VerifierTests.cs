using Launcher.Core.Services;

namespace Launcher.Core.Tests;

public class Sha256VerifierTests
{
    [Fact]
    public void Verify_ShouldMatchKnownHash()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "abc");
            Assert.True(Sha256Verifier.Verify(path, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
