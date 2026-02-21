using System.IO.Compression;
using System.Text.Json;
using Launcher.Core.Abstractions;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class BasicDiagnosticsService(
    string appRoot,
    string logsPath,
    string profilesPath,
    string manifestPath) : IDiagnosticsService
{
    private static readonly JsonSerializerOptions ProfileJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Task<IReadOnlyList<HealthCheckItem>> RunHealthChecksAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<HealthCheckItem>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return BuildHealthChecks(appRoot, logsPath, profilesPath, manifestPath, cancellationToken);
        }, cancellationToken);
    }

    public Task<string> ExportZipAsync(string outputFolder, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ExportZipInternal(outputFolder, logsPath, profilesPath, manifestPath, cancellationToken);
        }, cancellationToken);
    }

    private static IReadOnlyList<HealthCheckItem> BuildHealthChecks(
        string appRoot,
        string logsPath,
        string profilesPath,
        string manifestPath,
        CancellationToken cancellationToken)
    {
        var checks = new List<HealthCheckItem>
        {
            new("应用目录可访问", Directory.Exists(appRoot), appRoot),
            new("日志目录存在", Directory.Exists(logsPath), logsPath),
            new("profiles.json 存在", File.Exists(profilesPath), profilesPath),
            new("manifest.json 存在", File.Exists(manifestPath), manifestPath)
        };

        if (!File.Exists(profilesPath))
        {
            return checks;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = File.ReadAllText(profilesPath);
            var profiles = JsonSerializer.Deserialize<ProfilesDocument>(json, ProfileJsonOptions);
            var profile = profiles?.Profiles.FirstOrDefault(p => p.ProfileId == profiles.DefaultProfileId) ??
                          profiles?.Profiles.FirstOrDefault();
            if (profile is null)
            {
                checks.Add(new("默认档案可读取", false, "profiles.json 中未找到可用档案"));
                return checks;
            }

            var katagoPath = ResolvePath(appRoot, profile.Katago.Path);
            var configPath = ResolvePath(appRoot, profile.Config.Path);
            var modelPath = ResolvePath(appRoot, profile.Network.Path);

            checks.Add(new("KataGo 可执行存在", File.Exists(katagoPath), katagoPath));
            checks.Add(new("配置文件存在", File.Exists(configPath), configPath));

            var modelExists = File.Exists(modelPath);
            var modelMessage = modelPath;
            if (!modelExists &&
                TryResolveExistingModelPath(appRoot, profile.Network, out var resolvedModelPath))
            {
                modelExists = true;
                modelMessage = $"{resolvedModelPath} (已兼容迁移路径)";
            }

            checks.Add(new("权重文件存在", modelExists, modelMessage));
        }
        catch (Exception ex)
        {
            checks.Add(new("profiles.json 可解析", false, ex.Message));
        }

        return checks;
    }

    private static string ExportZipInternal(
        string outputFolder,
        string logsPath,
        string profilesPath,
        string manifestPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputFolder);
        var zipName = $"diagnostics-{DateTimeOffset.Now:yyyyMMddHHmmss}.zip";
        var zipPath = Path.Combine(outputFolder, zipName);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        cancellationToken.ThrowIfCancellationRequested();
        if (Directory.Exists(logsPath))
        {
            AddDirectory(archive, logsPath, "logs", cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(profilesPath))
        {
            archive.CreateEntryFromFile(profilesPath, "profiles.json");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(manifestPath))
        {
            archive.CreateEntryFromFile(manifestPath, "manifest.snapshot.json");
        }

        return zipPath;
    }

    private static void AddDirectory(ZipArchive archive, string source, string targetPrefix, CancellationToken cancellationToken)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(source, file);
            var entryName = Path.Combine(targetPrefix, rel).Replace("\\", "/", StringComparison.Ordinal);
            archive.CreateEntryFromFile(file, entryName);
        }
    }

    private static string ResolvePath(string root, string path)
    {
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(root, path));
    }

    private static bool TryResolveExistingModelPath(string appRoot, NetworkProfile network, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        var expectedPath = ResolvePath(appRoot, network.Path);
        if (File.Exists(expectedPath))
        {
            resolvedPath = expectedPath;
            return true;
        }

        var fileName = Path.GetFileName(expectedPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "model.bin.gz";
        }

        if (string.Equals(network.Source, "katagotraining", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(network.Id, "kata1-latest", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(network.Id, "kata1-strongest", StringComparison.OrdinalIgnoreCase)))
        {
            var flatPath = Path.Combine(appRoot, "components", "networks", SanitizePathSegment(network.Name), fileName);
            if (File.Exists(flatPath))
            {
                resolvedPath = flatPath;
                return true;
            }
        }

        var networksRoot = Path.Combine(appRoot, "components", "networks");
        if (!Directory.Exists(networksRoot))
        {
            return false;
        }

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pattern in GetCandidatePatterns(fileName))
        {
            foreach (var file in EnumerateFilesSafe(networksRoot, pattern))
            {
                candidates.Add(file);
            }
        }

        var picked = candidates
            .Select(path => new
            {
                Path = path,
                Score = ScoreNetworkPath(path, network.Id, network.Name, fileName)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Path.Length)
            .Select(x => x.Path)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(picked) || !File.Exists(picked))
        {
            return false;
        }

        resolvedPath = picked;
        return true;
    }

    private static IEnumerable<string> GetCandidatePatterns(string fileName)
    {
        if (string.Equals(fileName, "model.bin.gz", StringComparison.OrdinalIgnoreCase))
        {
            return ["model.bin.gz", "*.bin.gz"];
        }

        return [fileName, "model.bin.gz", "*.bin.gz"];
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories);
        }
        catch
        {
            return [];
        }
    }

    private static int ScoreNetworkPath(string path, string? networkId, string? networkName, string fileNameHint)
    {
        var score = 0;
        var fileName = Path.GetFileName(path);
        var normalized = path.Replace('\\', '/');

        if (string.Equals(fileName, fileNameHint, StringComparison.OrdinalIgnoreCase))
        {
            score += 1200;
        }

        if (string.Equals(fileName, "model.bin.gz", StringComparison.OrdinalIgnoreCase))
        {
            score += 1000;
        }

        if (!string.IsNullOrWhiteSpace(networkId) &&
            normalized.Contains(networkId, StringComparison.OrdinalIgnoreCase))
        {
            score += 260;
        }

        if (!string.IsNullOrWhiteSpace(networkName) &&
            normalized.Contains(networkName, StringComparison.OrdinalIgnoreCase))
        {
            score += 220;
        }

        if (normalized.Contains("/components/networks/", StringComparison.OrdinalIgnoreCase))
        {
            score += 120;
        }

        score -= normalized.Count(c => c == '/');
        return score;
    }

    private static string SanitizePathSegment(string? value)
    {
        var segment = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(segment))
        {
            return "network";
        }

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            segment = segment.Replace(c, '-');
        }

        return segment.Trim();
    }
}
