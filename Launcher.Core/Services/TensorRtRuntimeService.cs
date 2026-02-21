using System.IO;
using System.Text.Json;

namespace Launcher.Core.Services;

public static class TensorRtRuntimeService
{
    private const string RuntimeMarkerFileName = "easykatago-tensorrt-runtime.json";

    public static bool IsTensorRtBackend(string? backend, string? katagoPath = null)
    {
        if (string.Equals(backend, "tensorrt", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(backend, "trt", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(katagoPath))
        {
            return false;
        }

        var normalized = katagoPath.Replace('\\', '/');
        return normalized.Contains("/tensorrt/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("-trt", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> DiscoverRuntimeDirectories(
        string katagoPath,
        params string[] extraRoots)
    {
        var katagoDir = Path.GetDirectoryName(katagoPath);
        if (!string.IsNullOrWhiteSpace(katagoDir) &&
            TryReadRuntimeMarker(katagoDir, out var marker))
        {
            var markerDirs = DiscoverRuntimeDirectoriesFromRoot(marker.RuntimeRoot);
            if (ContainsTensorRtDll(katagoDir))
            {
                return MergeAndOrderRuntimeDirs([katagoDir], markerDirs);
            }

            if (markerDirs.Count > 0)
            {
                return markerDirs;
            }
        }

        if (!string.IsNullOrWhiteSpace(katagoDir) && ContainsTensorRtDll(katagoDir))
        {
            // If katago directory already contains TensorRT DLLs, avoid searching other versions.
            return [Path.GetFullPath(katagoDir)];
        }

        var selectedBundle = SelectBestRuntimeBundle(katagoPath, extraRoots);
        if (selectedBundle is null)
        {
            return [];
        }

        var dirs = DiscoverRuntimeDirectoriesFromRoot(selectedBundle.BundleRoot);
        return dirs;
    }

    public static IReadOnlyList<string> DiscoverRuntimeDirectoriesFromRoot(string runtimeRoot)
    {
        if (string.IsNullOrWhiteSpace(runtimeRoot) || !Directory.Exists(runtimeRoot))
        {
            return [];
        }

        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dll in EnumerateFilesSafe(runtimeRoot, "nvinfer*.dll", SearchOption.AllDirectories))
        {
            var dir = Path.GetDirectoryName(dll);
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            directories.Add(Path.GetFullPath(dir));
            var parent = Directory.GetParent(dir)?.FullName;
            if (string.IsNullOrWhiteSpace(parent))
            {
                continue;
            }

            var siblingBin = Path.Combine(parent, "bin");
            if (ContainsAnyDll(siblingBin))
            {
                directories.Add(Path.GetFullPath(siblingBin));
            }

            var siblingLib = Path.Combine(parent, "lib");
            if (ContainsAnyDll(siblingLib))
            {
                directories.Add(Path.GetFullPath(siblingLib));
            }
        }

        return MergeAndOrderRuntimeDirs([], directories);
    }

    public static bool PrependProcessPath(IEnumerable<string> runtimeDirs, out string message)
    {
        try
        {
            var normalized = NormalizePathEntries(runtimeDirs);
            if (normalized.Count == 0)
            {
                message = "未发现可用 TensorRT 目录，跳过进程 PATH 注入。";
                return false;
            }

            var current = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process) ?? string.Empty;
            var merged = MergePath(current, normalized);
            if (string.Equals(current, merged, StringComparison.Ordinal))
            {
                message = "TensorRT 目录已存在于当前进程 PATH。";
                return true;
            }

            Environment.SetEnvironmentVariable("PATH", merged, EnvironmentVariableTarget.Process);
            message = $"已注入当前进程 PATH（TensorRT 目录 {normalized.Count} 个）。";
            return true;
        }
        catch (Exception ex)
        {
            message = $"注入进程 PATH 失败: {ex.Message}";
            return false;
        }
    }

    public static bool EnsureUserPath(IEnumerable<string> runtimeDirs, out string message)
    {
        try
        {
            var normalized = NormalizePathEntries(runtimeDirs);
            if (normalized.Count == 0)
            {
                message = "未发现可用 TensorRT 目录，跳过用户 PATH 写入。";
                return false;
            }

            var current = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? string.Empty;
            var merged = MergePath(current, normalized);
            if (string.Equals(current, merged, StringComparison.Ordinal))
            {
                message = "TensorRT 目录已存在于用户 PATH。";
                return true;
            }

            Environment.SetEnvironmentVariable("PATH", merged, EnvironmentVariableTarget.User);
            message = $"已写入用户 PATH（TensorRT 目录 {normalized.Count} 个）。";
            return true;
        }
        catch (Exception ex)
        {
            message = $"写入用户 PATH 失败: {ex.Message}";
            return false;
        }
    }

    public static bool EnsureMachinePath(IEnumerable<string> runtimeDirs, out string message)
    {
        try
        {
            var normalized = NormalizePathEntries(runtimeDirs);
            if (normalized.Count == 0)
            {
                message = "未发现可用 TensorRT 目录，跳过系统 PATH 写入。";
                return false;
            }

            var current = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? string.Empty;
            var merged = MergePath(current, normalized);
            if (string.Equals(current, merged, StringComparison.Ordinal))
            {
                message = "TensorRT 目录已存在于系统 PATH。";
                return true;
            }

            Environment.SetEnvironmentVariable("PATH", merged, EnvironmentVariableTarget.Machine);
            message = $"已写入系统 PATH（TensorRT 目录 {normalized.Count} 个）。";
            return true;
        }
        catch (Exception ex)
        {
            message = $"写入系统 PATH 失败: {ex.Message}";
            return false;
        }
    }

    public static bool WriteRuntimeMarker(
        string katagoPath,
        string runtimeRoot,
        string runtimeId,
        string trtVersion,
        string cudaVersion,
        out string message)
    {
        try
        {
            var katagoDir = Path.GetDirectoryName(katagoPath);
            if (string.IsNullOrWhiteSpace(katagoDir) || !Directory.Exists(katagoDir))
            {
                message = "写入 TensorRT 运行时标记失败：未找到 katago 目录。";
                return false;
            }

            var marker = new RuntimeMarkerModel
            {
                RuntimeId = runtimeId,
                RuntimeRoot = Path.GetFullPath(runtimeRoot),
                TrtVersion = trtVersion,
                CudaVersion = cudaVersion,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var path = Path.Combine(katagoDir, RuntimeMarkerFileName);
            var json = JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            message = $"已写入 TensorRT 运行时标记: {path}";
            return true;
        }
        catch (Exception ex)
        {
            message = $"写入 TensorRT 运行时标记失败: {ex.Message}";
            return false;
        }
    }

    private static IReadOnlyList<string> EnumerateCandidateRoots(string katagoPath, IReadOnlyList<string> extraRoots)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in extraRoots.Where(static v => !string.IsNullOrWhiteSpace(v)))
        {
            TryAddDirectory(Path.GetFullPath(root), roots);
        }

        var resolvedFromKatago = TryResolveInstallRootFromKatagoPath(katagoPath);
        if (!string.IsNullOrWhiteSpace(resolvedFromKatago))
        {
            TryAddDirectory(resolvedFromKatago, roots);
        }

        return roots.ToList();
    }

    private static RuntimeBundleCandidate? SelectBestRuntimeBundle(string katagoPath, IReadOnlyList<string> extraRoots)
    {
        var bundles = new Dictionary<string, RuntimeBundleCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in EnumerateCandidateRoots(katagoPath, extraRoots))
        {
            var runtimeRoot = Path.Combine(root, "components", "tensorrt");
            if (!Directory.Exists(runtimeRoot))
            {
                continue;
            }

            foreach (var dll in EnumerateFilesSafe(runtimeRoot, "nvinfer*.dll", SearchOption.AllDirectories))
            {
                var bundleRoot = TryGetBundleRoot(dll);
                if (string.IsNullOrWhiteSpace(bundleRoot))
                {
                    continue;
                }

                if (!bundles.TryGetValue(bundleRoot, out var current))
                {
                    bundles[bundleRoot] = CreateBundleCandidate(bundleRoot);
                    continue;
                }

                var refresh = CreateBundleCandidate(bundleRoot);
                if (refresh.Score > current.Score)
                {
                    bundles[bundleRoot] = refresh;
                }
            }
        }

        return bundles.Values
            .OrderByDescending(static x => x.Score)
            .FirstOrDefault();
    }

    private static RuntimeBundleCandidate CreateBundleCandidate(string bundleRoot)
    {
        var score = 0L;
        if (bundleRoot.Contains("tensorrt-runtime-", StringComparison.OrdinalIgnoreCase))
        {
            score += 1_000_000_000_000L;
        }

        try
        {
            score += Directory.GetLastWriteTimeUtc(bundleRoot).Ticks;
        }
        catch
        {
            // best effort
        }

        return new RuntimeBundleCandidate(bundleRoot, score);
    }

    private static string? TryGetBundleRoot(string dllPath)
    {
        try
        {
            var full = Path.GetFullPath(dllPath);
            var normalized = full.Replace('\\', '/');
            var marker = "/components/tensorrt/";
            var idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return null;
            }

            var tail = normalized[(idx + marker.Length)..];
            var parts = tail.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return null;
            }

            var prefix = normalized[..(idx + marker.Length)];
            var bundleRoot = $"{prefix}{parts[0]}/{parts[1]}";
            return bundleRoot.Replace('/', Path.DirectorySeparatorChar);
        }
        catch
        {
            return null;
        }
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

    private static IReadOnlyList<string> NormalizePathEntries(IEnumerable<string> entries)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            string full;
            try
            {
                full = Path.GetFullPath(entry.Trim());
            }
            catch
            {
                continue;
            }

            if (!Directory.Exists(full))
            {
                continue;
            }

            if (seen.Add(full))
            {
                result.Add(full);
            }
        }

        return result;
    }

    private static string MergePath(string current, IReadOnlyList<string> prependEntries)
    {
        var existing = (current ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<string>();

        foreach (var add in prependEntries)
        {
            if (seen.Add(add))
            {
                merged.Add(add);
            }
        }

        foreach (var path in existing)
        {
            if (seen.Add(path))
            {
                merged.Add(path);
            }
        }

        return string.Join(';', merged);
    }

    private static bool TryReadRuntimeMarker(string katagoDir, out RuntimeMarkerModel marker)
    {
        marker = default!;
        try
        {
            var path = Path.Combine(katagoDir, RuntimeMarkerFileName);
            if (!File.Exists(path))
            {
                return false;
            }

            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<RuntimeMarkerModel>(json);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.RuntimeRoot))
            {
                return false;
            }

            if (!Directory.Exists(parsed.RuntimeRoot))
            {
                return false;
            }

            marker = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> MergeAndOrderRuntimeDirs(
        IEnumerable<string> first,
        IEnumerable<string> second)
    {
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in first.Concat(second))
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            try
            {
                var full = Path.GetFullPath(dir);
                if (!Directory.Exists(full))
                {
                    continue;
                }

                merged.Add(full);
            }
            catch
            {
                // best effort
            }
        }

        return merged
            .OrderBy(dir => dir.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(dir => dir, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void TryAddDirectory(string directory, HashSet<string> output)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        output.Add(directory);
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, string pattern, SearchOption searchOption)
    {
        try
        {
            return Directory.EnumerateFiles(root, pattern, searchOption);
        }
        catch
        {
            return [];
        }
    }

    private static bool ContainsTensorRtDll(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        return EnumerateFilesSafe(directory, "nvinfer*.dll", SearchOption.TopDirectoryOnly).Any();
    }

    private static bool ContainsAnyDll(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        return EnumerateFilesSafe(directory, "*.dll", SearchOption.TopDirectoryOnly).Any();
    }

    private sealed record RuntimeBundleCandidate(string BundleRoot, long Score);

    private sealed record RuntimeMarkerModel
    {
        public required string RuntimeId { get; init; }
        public required string RuntimeRoot { get; init; }
        public required string TrtVersion { get; init; }
        public required string CudaVersion { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }
    }
}
