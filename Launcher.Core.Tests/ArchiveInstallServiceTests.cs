using Launcher.Core.Models;
using Launcher.Core.Services;

namespace Launcher.Core.Tests;

public sealed class ArchiveInstallServiceTests
{
    [Fact]
    public async Task InstallAsync_WithTraversalLikeSegments_StaysInsideInstallRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "easykatago-archive-test-" + Guid.NewGuid().ToString("N"));
        var installRoot = Path.Combine(tempRoot, "install");
        var sourceRoot = Path.Combine(tempRoot, "source");
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(sourceRoot);

        var sourcePath = Path.Combine(sourceRoot, "sample.cfg");
        await File.WriteAllTextAsync(sourcePath, "numSearchThreads = 6");

        var component = new ComponentModel
        {
            Id = "..\\..\\escape",
            Name = "Configs",
            Version = "../v1",
            Type = "cfg",
            Urls = ["https://example.invalid/sample.cfg"],
            Entry = "config.cfg"
        };

        try
        {
            var service = new ArchiveInstallService();
            var installedPath = await service.InstallAsync(component, sourcePath, installRoot);

            var fullInstallRoot = Path.GetFullPath(installRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullInstalled = Path.GetFullPath(installedPath);

            Assert.StartsWith(fullInstallRoot, fullInstalled, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(fullInstalled));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
