using System.Collections;
using System.IO;
using System.Text.Json;

namespace Launcher.Core.Services;

public static class CudaRuntimeService
{
    public static bool IsCudaBackend(string? backend, string? katagoPath = null)
    {
        if (string.Equals(backend, "cuda", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(katagoPath))
        {
            return false;
        }

        var normalized = katagoPath.Replace('\\', '/');
        return normalized.Contains("/cuda/", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Contains("/tensorrt/", StringComparison.OrdinalIgnoreCase) &&
               !normalized.Contains("-trt", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> DiscoverRuntimeDirectories(
        string katagoPath,
        params string[] extraRoots)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddDirectory(Path.GetDirectoryName(katagoPath), directories);

        foreach (var root in extraRoots)
        {
            AddRootCandidates(root, directories);
        }

        foreach (var root in ReadManualHintsFromSettings(katagoPath))
        {
            AddRootCandidates(root, directories);
        }

        foreach (var pathEntry in EnumeratePathEntries())
        {
            AddDirectory(pathEntry, directories);
        }

        foreach (var envRoot in EnumerateCudaRelatedEnvironmentRoots())
        {
            AddRootCandidates(envRoot, directories);
        }

        foreach (var root in EnumerateKnownCudaInstallRoots())
        {
            AddRootCandidates(root, directories);
        }

        foreach (var root in EnumerateKnownCudnnInstallRoots())
        {
            AddRootCandidates(root, directories);
        }

        return directories
            .OrderByDescending(dir => DirectoryContainsPattern(dir, "cublas64_*.dll") ? 1 : 0)
            .ThenByDescending(dir => DirectoryContainsPattern(dir, "cudnn64_*.dll") ? 1 : 0)
            .ThenBy(dir => dir.Contains($"{Path.DirectorySeparatorChar}bin", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(dir => dir, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool LooksReadyForCuda(string katagoPath, IReadOnlyList<string> runtimeDirs)
    {
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDirectory(Path.GetDirectoryName(katagoPath), all);

        foreach (var dir in runtimeDirs)
        {
            AddDirectory(dir, all);
        }

        var hasCublas = all.Any(dir => DirectoryContainsPattern(dir, "cublas64_*.dll"));
        var hasCudnn = all.Any(dir => DirectoryContainsPattern(dir, "cudnn64_*.dll"));
        return hasCublas && hasCudnn;
    }

    public static CudaRuntimeProbeResult Probe(string katagoPath, params string[] extraRoots)
    {
        var dirs = DiscoverRuntimeDirectories(katagoPath, extraRoots);
        var cudaDir = dirs.FirstOrDefault(dir => DirectoryContainsPattern(dir, "cublas64_*.dll"));
        var cudnnDir = dirs.FirstOrDefault(dir => DirectoryContainsPattern(dir, "cudnn64_*.dll"));

        var hasCublas = !string.IsNullOrWhiteSpace(cudaDir);
        var hasCudnn = !string.IsNullOrWhiteSpace(cudnnDir);
        var ready = hasCublas && hasCudnn;

        var summary = ready
            ? "已检测到 CUDA/cublas 与 cuDNN 运行库。"
            : hasCublas || hasCudnn
                ? "仅检测到部分 CUDA 运行库，请补充缺失目录。"
                : "未检测到 CUDA 运行库。";

        return new CudaRuntimeProbeResult(
            IsReady: ready,
            CudaDirectory: cudaDir,
            CudnnDirectory: cudnnDir,
            RuntimeDirectories: dirs,
            Summary: summary);
    }

    private static IEnumerable<string> EnumeratePathEntries()
    {
        foreach (var target in EnumerateEnvironmentTargets())
        {
            string? raw;
            try
            {
                raw = Environment.GetEnvironmentVariable("PATH", target);
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(part))
                {
                    yield return part;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateCudaRelatedEnvironmentRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in EnumerateEnvironmentTargets())
        {
            IDictionary vars;
            try
            {
                vars = Environment.GetEnvironmentVariables(target);
            }
            catch
            {
                continue;
            }

            foreach (DictionaryEntry entry in vars)
            {
                var name = entry.Key?.ToString();
                var value = entry.Value?.ToString();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (!name.StartsWith("CUDA_PATH", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("CUDA_HOME", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("CUDA_ROOT", StringComparison.OrdinalIgnoreCase) &&
                    !name.StartsWith("CUDNN_PATH", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (seen.Add(value))
                {
                    yield return value;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateKnownCudaInstallRoots()
    {
        foreach (var root in EnumerateProgramFilesRoots())
        {
            var cudaBase = Path.Combine(root, "NVIDIA GPU Computing Toolkit", "CUDA");
            if (!Directory.Exists(cudaBase))
            {
                continue;
            }

            yield return cudaBase;

            IEnumerable<string> versions;
            try
            {
                versions = Directory.EnumerateDirectories(cudaBase, "v*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var versionDir in versions)
            {
                yield return versionDir;
            }
        }
    }

    private static IEnumerable<string> EnumerateKnownCudnnInstallRoots()
    {
        foreach (var root in EnumerateProgramFilesRoots())
        {
            var cudnnRoot = Path.Combine(root, "NVIDIA", "CUDNN");
            if (Directory.Exists(cudnnRoot))
            {
                yield return cudnnRoot;
            }
        }
    }

    private static IEnumerable<string> EnumerateProgramFilesRoots()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(root))
            {
                yield return root;
            }
        }
    }

    private static void AddRootCandidates(string? rawRoot, HashSet<string> output)
    {
        if (string.IsNullOrWhiteSpace(rawRoot))
        {
            return;
        }

        var root = TryGetFullPath(rawRoot);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return;
        }

        AddDirectory(root, output);
        AddDirectory(Path.Combine(root, "bin"), output);
        AddDirectory(Path.Combine(root, "lib", "x64"), output);

        foreach (var dir in FindDllDirectories(root, "cublas64_*.dll", maxResults: 8))
        {
            AddDirectory(dir, output);
        }

        foreach (var dir in FindDllDirectories(root, "cudnn64_*.dll", maxResults: 12))
        {
            AddDirectory(dir, output);
        }
    }

    private static IEnumerable<string> FindDllDirectories(string root, string pattern, int maxResults)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in EnumerateFilesSafe(root, pattern, SearchOption.AllDirectories))
        {
            var dir = Path.GetDirectoryName(file);
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            var full = TryGetFullPath(dir);
            if (string.IsNullOrWhiteSpace(full) || !found.Add(full))
            {
                continue;
            }

            yield return full;
            if (found.Count >= maxResults)
            {
                yield break;
            }
        }
    }

    private static IEnumerable<string> ReadManualHintsFromSettings(string katagoPath)
    {
        var hints = new List<string>();
        try
        {
            var installRoot = TryResolveInstallRootFromKatagoPath(katagoPath);
            if (string.IsNullOrWhiteSpace(installRoot))
            {
                return hints;
            }

            var settingsPath = Path.Combine(installRoot, "data", "settings.json");
            if (!File.Exists(settingsPath))
            {
                return hints;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (!TryGetPropertyIgnoreCase(doc.RootElement, "cuda", out var cudaNode))
            {
                return hints;
            }

            if (TryGetStringPropertyIgnoreCase(cudaNode, "manualCudaDirectory", out var cudaDir) &&
                !string.IsNullOrWhiteSpace(cudaDir))
            {
                hints.Add(cudaDir);
            }

            if (TryGetStringPropertyIgnoreCase(cudaNode, "manualCudnnDirectory", out var cudnnDir) &&
                !string.IsNullOrWhiteSpace(cudnnDir))
            {
                hints.Add(cudnnDir);
            }
        }
        catch
        {
            // best effort
        }

        return hints;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement root, string propertyName, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetStringPropertyIgnoreCase(JsonElement root, string propertyName, out string? value)
    {
        if (TryGetPropertyIgnoreCase(root, propertyName, out var element) && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString();
            return true;
        }

        value = null;
        return false;
    }

    private static string? TryResolveInstallRootFromKatagoPath(string katagoPath)
    {
        try
        {
            var fullPath = Path.GetFullPath(katagoPath).Replace('\\', '/');
            var marker = "/components/katago/";
            var idx = fullPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx <= 0)
            {
                return null;
            }

            return fullPath[..idx].Replace('/', Path.DirectorySeparatorChar);
        }
        catch
        {
            return null;
        }
    }

    private static void AddDirectory(string? path, HashSet<string> output)
    {
        var full = TryGetFullPath(path);
        if (string.IsNullOrWhiteSpace(full) || !Directory.Exists(full))
        {
            return;
        }

        output.Add(full);
    }

    private static string? TryGetFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch
        {
            return null;
        }
    }

    private static bool DirectoryContainsPattern(string directory, string pattern)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        try
        {
            if (Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).Any())
            {
                return true;
            }
        }
        catch
        {
            // best effort
        }

        var bin = Path.Combine(directory, "bin");
        if (!Directory.Exists(bin))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateFiles(bin, pattern, SearchOption.TopDirectoryOnly).Any();
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, string pattern, SearchOption option)
    {
        try
        {
            return Directory.EnumerateFiles(root, pattern, option);
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<EnvironmentVariableTarget> EnumerateEnvironmentTargets()
    {
        yield return EnvironmentVariableTarget.Process;
        yield return EnvironmentVariableTarget.User;
        yield return EnvironmentVariableTarget.Machine;
    }
}

public sealed record CudaRuntimeProbeResult(
    bool IsReady,
    string? CudaDirectory,
    string? CudnnDirectory,
    IReadOnlyList<string> RuntimeDirectories,
    string Summary);
