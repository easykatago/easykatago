using System.IO.Compression;
using System.Text;
using Launcher.Core.Abstractions;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class ArchiveInstallService : IInstallService
{
    private static readonly Encoding GbkZipEncoding = CreateGbkZipEncoding();

    public Task<string> InstallAsync(ComponentModel component, string archivePath, string installRoot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var lowerName = component.Name.ToLowerInvariant();
        var type = (component.Type ?? string.Empty).ToLowerInvariant();
        string targetDir = lowerName switch
        {
            "katago" => Path.Combine(installRoot, "components", "katago", component.Version, component.Backend ?? "opencl"),
            "lizzieyzy" => Path.Combine(installRoot, "components", "lizzieyzy", component.Version),
            _ when type == "bin.gz" => Path.Combine(installRoot, "components", "networks", component.Id, component.Version),
            _ when type == "cfg" => Path.Combine(installRoot, "components", "configs", component.Id, component.Version),
            _ => Path.Combine(installRoot, "components", lowerName, component.Id, component.Version)
        };
        ResetDirectory(targetDir);

        var fileName = Path.GetFileName(archivePath);
        if (type == "zip" || archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ExtractZip(component, archivePath, targetDir);
            var entryPath = TryResolveEntryPath(component, targetDir);
            return Task.FromResult(entryPath ?? targetDir);
        }

        // Keep .bin.gz and .cfg as-is.
        var targetFileName = fileName;
        if (type == "bin.gz")
        {
            targetFileName = "model.bin.gz";
        }
        else if (type == "cfg")
        {
            targetFileName = "config.cfg";
        }

        var targetPath = Path.Combine(targetDir, targetFileName);
        File.Copy(archivePath, targetPath, overwrite: true);
        return Task.FromResult(targetPath);
    }

    private static void ResetDirectory(string targetDir)
    {
        if (Directory.Exists(targetDir))
        {
            Directory.Delete(targetDir, recursive: true);
        }

        Directory.CreateDirectory(targetDir);
    }

    private static void ExtractZip(ComponentModel component, string archivePath, string targetDir)
    {
        if (string.Equals(component.Name, "LizzieYzy", StringComparison.OrdinalIgnoreCase))
        {
            // LizzieYzy release zip often uses GBK filenames.
            ZipFile.ExtractToDirectory(archivePath, targetDir, GbkZipEncoding, overwriteFiles: true);
            return;
        }

        ZipFile.ExtractToDirectory(archivePath, targetDir, overwriteFiles: true);
    }

    private static string? TryResolveEntryPath(ComponentModel component, string targetDir)
    {
        var entry = (component.Entry ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(entry))
        {
            var direct = Path.Combine(targetDir, entry);
            if (File.Exists(direct))
            {
                return direct;
            }

            var byName = FindFileByName(targetDir, Path.GetFileName(entry));
            if (byName is not null)
            {
                return byName;
            }
        }

        if ((component.Entry ?? string.Empty).EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(component.Name, "LizzieYzy", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(component.Name, "KataGo", StringComparison.OrdinalIgnoreCase))
        {
            return FindBestExecutable(targetDir, component.Entry, component.Name);
        }

        return null;
    }

    private static string? FindFileByName(string root, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindBestExecutable(string root, string? expectedEntry, string? componentName)
    {
        var expectedFileName = Path.GetFileName(expectedEntry ?? string.Empty);
        var candidates = Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var best = candidates
            .Select(path => new
            {
                Path = path,
                Score = ScoreExecutable(path, expectedFileName, componentName)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Path.Length)
            .First();

        return best.Path;
    }

    private static int ScoreExecutable(string fullPath, string expectedFileName, string? componentName)
    {
        var score = 0;
        var fileName = Path.GetFileName(fullPath);
        var normalizedPath = fullPath.Replace('\\', '/');

        if (!string.IsNullOrWhiteSpace(expectedFileName) &&
            string.Equals(fileName, expectedFileName, StringComparison.OrdinalIgnoreCase))
        {
            score += 1000;
        }

        if (!string.IsNullOrWhiteSpace(componentName) &&
            fileName.Contains(componentName, StringComparison.OrdinalIgnoreCase))
        {
            score += 200;
        }

        if (fileName.Contains("lizzie", StringComparison.OrdinalIgnoreCase))
        {
            score += 500;
        }

        if (fileName.Contains("yzy", StringComparison.OrdinalIgnoreCase))
        {
            score += 450;
        }

        if (fileName.Contains("katago", StringComparison.OrdinalIgnoreCase))
        {
            score += 500;
        }

        if (normalizedPath.Contains("/jre/", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains("/jdk/", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains("/runtime/", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains("/jcef", StringComparison.OrdinalIgnoreCase))
        {
            score -= 300;
        }

        if (fileName.StartsWith("unins", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("updater", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("update", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("crashpad", StringComparison.OrdinalIgnoreCase))
        {
            score -= 400;
        }

        score -= normalizedPath.Count(c => c == '/');
        return score;
    }

    private static Encoding CreateGbkZipEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("GB18030");
    }
}
