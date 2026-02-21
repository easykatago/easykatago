using System.IO;
using Launcher.Core.Abstractions;
using Launcher.Core.Models;
using Launcher.Core.Services;

namespace Launcher.App.Services;

public sealed class LaunchWorkflowService
{
    private readonly SettingsStoreService _settingsStoreService = new();
    private readonly ILizzieConfigService _lizzieConfigService = new LizzieConfigService();
    private readonly ILaunchService _launchService = new ProcessLaunchService();
    private readonly DefaultCommandBuilder _commandBuilder = new();

    public async Task<LaunchWorkflowResult> LaunchDefaultAsync(string appRoot, CancellationToken cancellationToken = default)
    {
        try
        {
            var bootstrap = new BootstrapService(appRoot);
            await bootstrap.EnsureDefaultsAsync(cancellationToken);

            var profileService = new JsonProfileService(bootstrap.ProfilesPath);
            var profiles = await profileService.LoadAsync(cancellationToken);
            var selected = profileService.GetDefault(profiles);
            if (selected is null)
            {
                return Fail("Default profile not found. Run initialization in Install Wizard first.");
            }

            var installRoot = await ResolveInstallRootAsync(bootstrap, cancellationToken);
            var candidateRoots = BuildCandidateRoots(bootstrap.AppRoot, installRoot);

            var katagoPath = ResolvePath(bootstrap.AppRoot, selected.Katago.Path);
            var modelPath = ResolvePath(bootstrap.AppRoot, selected.Network.Path);
            var configPath = ResolvePath(bootstrap.AppRoot, selected.Config.Path);
            var lizziePath = ResolvePath(bootstrap.AppRoot, selected.Lizzieyzy.Path);

            var profileChanged = false;

            if (!File.Exists(katagoPath))
            {
                if (!TryFindKatagoExe(candidateRoots, selected.Katago.Version, selected.Katago.Backend, out var found))
                {
                    return Fail($"KataGo executable not found: {katagoPath}");
                }

                katagoPath = found;
                selected = selected with
                {
                    Katago = selected.Katago with { Path = NormalizeProfilePath(bootstrap.AppRoot, katagoPath) }
                };
                profileChanged = true;
                AppLogService.Warn($"Auto-corrected KataGo path: {katagoPath}");
            }

            if (!File.Exists(modelPath))
            {
                if (!TryFindNetworkModel(candidateRoots, selected.Network.Id, selected.Network.Name, out var found))
                {
                    return Fail($"Network file not found: {modelPath}");
                }

                modelPath = found;
                selected = selected with
                {
                    Network = selected.Network with { Path = NormalizeProfilePath(bootstrap.AppRoot, modelPath) }
                };
                profileChanged = true;
                AppLogService.Warn($"Auto-corrected network path: {modelPath}");
            }

            if (!File.Exists(configPath))
            {
                if (!TryFindConfigFile(candidateRoots, selected.Config.Id, out var found))
                {
                    return Fail($"Config file not found: {configPath}");
                }

                configPath = found;
                selected = selected with
                {
                    Config = selected.Config with { Path = NormalizeProfilePath(bootstrap.AppRoot, configPath) }
                };
                profileChanged = true;
                AppLogService.Warn($"Auto-corrected config path: {configPath}");
            }

            if (!File.Exists(lizziePath))
            {
                if (!TryFindLizzieExe(candidateRoots, selected.Lizzieyzy.Version, out var found))
                {
                    return Fail($"LizzieYzy executable not found: {lizziePath}");
                }

                lizziePath = found;
                selected = selected with
                {
                    Lizzieyzy = selected.Lizzieyzy with
                    {
                        Path = NormalizeProfilePath(bootstrap.AppRoot, lizziePath),
                        Workdir = NormalizeProfilePath(bootstrap.AppRoot, Path.GetDirectoryName(lizziePath) ?? bootstrap.AppRoot)
                    }
                };
                profileChanged = true;
                AppLogService.Warn($"Auto-corrected LizzieYzy path: {lizziePath}");
            }

            var lizzieWorkdir = ResolvePath(bootstrap.AppRoot, selected.Lizzieyzy.Workdir);
            if (!Directory.Exists(lizzieWorkdir))
            {
                lizzieWorkdir = Path.GetDirectoryName(lizziePath) ?? bootstrap.AppRoot;
                selected = selected with
                {
                    Lizzieyzy = selected.Lizzieyzy with
                    {
                        Workdir = NormalizeProfilePath(bootstrap.AppRoot, lizzieWorkdir)
                    }
                };
                profileChanged = true;
                AppLogService.Warn($"Auto-corrected LizzieYzy workdir: {lizzieWorkdir}");
            }

            if (profileChanged)
            {
                await SaveUpdatedProfileAsync(profileService, profiles, selected, cancellationToken);
            }

            var gtpCommand = _commandBuilder.BuildGtpCommand(katagoPath, modelPath, configPath);
            var configResult = await _lizzieConfigService.TryWriteEngineAsync(lizziePath, gtpCommand, cancellationToken);
            if (configResult.Success)
            {
                AppLogService.Info($"Auto engine config write success: {configResult.Message}");
            }
            else
            {
                AppLogService.Warn($"Auto engine config write failed: {configResult.Message}");
            }

            if (selected.Tuning.RecommendedThreads is int threads && threads >= 1 && threads <= 1024)
            {
                var prefResult = await _lizzieConfigService.TryWriteKataThreadPreferenceAsync(
                    lizziePath,
                    threads,
                    autoLoad: true,
                    cancellationToken: cancellationToken);
                if (prefResult.Success)
                {
                    AppLogService.Info($"Synced Lizzie thread preference: {threads}; {prefResult.Message}");
                }
                else
                {
                    AppLogService.Warn($"Failed to sync Lizzie thread preference: {prefResult.Message}");
                }
            }

            if (TensorRtRuntimeService.IsTensorRtBackend(selected.Katago.Backend, katagoPath))
            {
                var runtimeDirs = TensorRtRuntimeService.DiscoverRuntimeDirectories(katagoPath, bootstrap.AppRoot, installRoot);
                if (runtimeDirs.Count > 0)
                {
                    if (TensorRtRuntimeService.PrependProcessPath(runtimeDirs, out var message))
                    {
                        AppLogService.Info(message);
                    }
                    else
                    {
                        AppLogService.Warn(message);
                    }
                }
                else
                {
                    AppLogService.Warn("当前 Profile 为 TensorRT 后端，但未发现 TensorRT 运行库目录。");
                }
            }
            else if (CudaRuntimeService.IsCudaBackend(selected.Katago.Backend, katagoPath))
            {
                var runtimeDirs = CudaRuntimeService.DiscoverRuntimeDirectories(katagoPath, bootstrap.AppRoot, installRoot);
                if (runtimeDirs.Count > 0)
                {
                    if (TensorRtRuntimeService.PrependProcessPath(runtimeDirs, out var message))
                    {
                        AppLogService.Info(message);
                    }
                    else
                    {
                        AppLogService.Warn(message);
                    }
                }

                if (!CudaRuntimeService.LooksReadyForCuda(katagoPath, runtimeDirs))
                {
                    var inspected = runtimeDirs.Count == 0
                        ? "(none)"
                        : string.Join(" | ", runtimeDirs.Take(6));
                    AppLogService.Warn($"CUDA runtime probe failed. inspected={inspected}");
                    return Fail("CUDA 后端缺少运行库（cublas/cudnn）。请安装对应 CUDA + cuDNN，或先切换 OpenCL / TensorRT。");
                }
            }

            await _launchService.LaunchAsync(
                executablePath: lizziePath,
                workingDirectory: lizzieWorkdir,
                cancellationToken: cancellationToken);
            AppLogService.Info($"Launched LizzieYzy: {lizziePath}");

            if (configResult.Success)
            {
                var message = string.IsNullOrWhiteSpace(configResult.Message)
                    ? "LizzieYzy started."
                    : $"LizzieYzy started. {configResult.Message}";
                return new LaunchWorkflowResult(true, message, null, null);
            }

            var guide = string.IsNullOrWhiteSpace(configResult.ManualGuideText)
                ? "Automatic engine config write failed. You can copy the command and configure in LizzieYzy manually."
                : configResult.ManualGuideText;
            return new LaunchWorkflowResult(
                true,
                $"LizzieYzy started, but auto-config failed: {configResult.Message ?? "Unknown reason"}",
                gtpCommand,
                guide);
        }
        catch (Exception ex)
        {
            AppLogService.Error($"One-click launch exception: {ex}");
            return Fail($"Launch exception: {ex.Message}");
        }
    }

    private async Task<string> ResolveInstallRootAsync(BootstrapService bootstrap, CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _settingsStoreService.LoadAsync(bootstrap.SettingsPath, cancellationToken);
            var raw = string.IsNullOrWhiteSpace(settings.InstallRoot) ? "." : settings.InstallRoot.Trim();
            return Path.IsPathRooted(raw)
                ? Path.GetFullPath(raw)
                : Path.GetFullPath(Path.Combine(bootstrap.AppRoot, raw));
        }
        catch
        {
            return bootstrap.AppRoot;
        }
    }

    private static IReadOnlyList<string> BuildCandidateRoots(params string[] roots)
    {
        return roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryFindKatagoExe(
        IReadOnlyList<string> roots,
        string? version,
        string? backend,
        out string path)
    {
        foreach (var root in roots)
        {
            var direct = Path.Combine(root, "components", "katago", version ?? string.Empty, backend ?? "opencl", "katago.exe");
            if (File.Exists(direct))
            {
                path = direct;
                return true;
            }
        }

        var candidates = new List<string>();
        foreach (var root in roots)
        {
            var baseDir = Path.Combine(root, "components", "katago");
            if (!Directory.Exists(baseDir))
            {
                continue;
            }

            candidates.AddRange(EnumerateFilesSafe(baseDir, "katago.exe"));
            candidates.AddRange(EnumerateFilesSafe(baseDir, "*.exe"));
        }

        path = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(candidate => new
            {
                Path = candidate,
                Score = ScoreKatago(candidate, version, backend)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Path.Length)
            .Select(x => x.Path)
            .FirstOrDefault() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(path);
    }

    private static bool TryFindNetworkModel(
        IReadOnlyList<string> roots,
        string? networkId,
        string? networkName,
        out string path)
    {
        foreach (var root in roots)
        {
            var directRoot = Path.Combine(root, "components", "networks", networkId ?? string.Empty);
            var direct = Directory.Exists(directRoot)
                ? EnumerateFilesSafe(directRoot, "model.bin.gz").FirstOrDefault()
                : null;
            if (!string.IsNullOrWhiteSpace(direct))
            {
                path = direct;
                return true;
            }
        }

        var candidates = new List<string>();
        foreach (var root in roots)
        {
            var baseDir = Path.Combine(root, "components", "networks");
            if (!Directory.Exists(baseDir))
            {
                continue;
            }

            candidates.AddRange(EnumerateFilesSafe(baseDir, "model.bin.gz"));
            candidates.AddRange(EnumerateFilesSafe(baseDir, "*.bin.gz"));
        }

        path = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(candidate => new
            {
                Path = candidate,
                Score = ScoreNetwork(candidate, networkId, networkName)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Path.Length)
            .Select(x => x.Path)
            .FirstOrDefault() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(path);
    }

    private static bool TryFindConfigFile(IReadOnlyList<string> roots, string? configId, out string path)
    {
        foreach (var root in roots)
        {
            var direct = Path.Combine(root, "components", "configs", configId ?? string.Empty, "1", "config.cfg");
            if (File.Exists(direct))
            {
                path = direct;
                return true;
            }
        }

        var candidates = new List<string>();
        foreach (var root in roots)
        {
            var baseDir = Path.Combine(root, "components", "configs");
            if (!Directory.Exists(baseDir))
            {
                continue;
            }

            candidates.AddRange(EnumerateFilesSafe(baseDir, "config.cfg"));
            candidates.AddRange(EnumerateFilesSafe(baseDir, "*.cfg"));
        }

        path = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(candidate => new
            {
                Path = candidate,
                Score = ScoreConfig(candidate, configId)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Path.Length)
            .Select(x => x.Path)
            .FirstOrDefault() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(path);
    }

    private static bool TryFindLizzieExe(IReadOnlyList<string> roots, string? version, out string path)
    {
        var candidates = new List<string>();
        foreach (var root in roots)
        {
            var baseDir = Path.Combine(root, "components", "lizzieyzy");
            if (!Directory.Exists(baseDir))
            {
                continue;
            }

            candidates.AddRange(EnumerateFilesSafe(baseDir, "*lizzie*.exe"));
            candidates.AddRange(EnumerateFilesSafe(baseDir, "*.exe"));
        }

        path = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(candidate => new
            {
                Path = candidate,
                Score = ScoreLizzie(candidate, version)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Path.Length)
            .Select(x => x.Path)
            .FirstOrDefault() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(path);
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

    private static int ScoreKatago(string path, string? version, string? backend)
    {
        var score = 0;
        var file = Path.GetFileName(path);
        var normalized = path.Replace('\\', '/');

        if (string.Equals(file, "katago.exe", StringComparison.OrdinalIgnoreCase))
        {
            score += 1000;
        }

        if (!string.IsNullOrWhiteSpace(version) &&
            normalized.Contains($"/{version}/", StringComparison.OrdinalIgnoreCase))
        {
            score += 260;
        }

        if (!string.IsNullOrWhiteSpace(backend) &&
            normalized.Contains($"/{backend}/", StringComparison.OrdinalIgnoreCase))
        {
            score += 260;
        }

        if (file.Contains("katago", StringComparison.OrdinalIgnoreCase))
        {
            score += 180;
        }

        score -= normalized.Count(c => c == '/');
        return score;
    }

    private static int ScoreNetwork(string path, string? networkId, string? networkName)
    {
        var score = 0;
        var file = Path.GetFileName(path);
        var normalized = path.Replace('\\', '/');

        if (string.Equals(file, "model.bin.gz", StringComparison.OrdinalIgnoreCase))
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
            score += 200;
        }

        if (normalized.Contains("/components/networks/", StringComparison.OrdinalIgnoreCase))
        {
            score += 120;
        }

        score -= normalized.Count(c => c == '/');
        return score;
    }

    private static int ScoreConfig(string path, string? configId)
    {
        var score = 0;
        var file = Path.GetFileName(path);
        var normalized = path.Replace('\\', '/');

        if (string.Equals(file, "config.cfg", StringComparison.OrdinalIgnoreCase))
        {
            score += 1000;
        }

        if (!string.IsNullOrWhiteSpace(configId) &&
            normalized.Contains(configId, StringComparison.OrdinalIgnoreCase))
        {
            score += 240;
        }

        if (normalized.Contains("/components/configs/", StringComparison.OrdinalIgnoreCase))
        {
            score += 120;
        }

        score -= normalized.Count(c => c == '/');
        return score;
    }

    private static int ScoreLizzie(string path, string? version)
    {
        var score = 0;
        var file = Path.GetFileName(path);
        var normalized = path.Replace('\\', '/');

        if (string.Equals(file, "LizzieYzy.exe", StringComparison.OrdinalIgnoreCase))
        {
            score += 1200;
        }

        if (file.Contains("lizzie", StringComparison.OrdinalIgnoreCase))
        {
            score += 700;
        }

        if (file.Contains("yzy", StringComparison.OrdinalIgnoreCase))
        {
            score += 560;
        }

        if (file.Contains("win64", StringComparison.OrdinalIgnoreCase))
        {
            score += 260;
        }

        if (!string.IsNullOrWhiteSpace(version) &&
            normalized.Contains($"/{version}/", StringComparison.OrdinalIgnoreCase))
        {
            score += 220;
        }

        if (normalized.Contains("/jre/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/jcef", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/runtime/", StringComparison.OrdinalIgnoreCase))
        {
            score -= 420;
        }

        if (file.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
            file.Contains("update", StringComparison.OrdinalIgnoreCase) ||
            file.Contains("updater", StringComparison.OrdinalIgnoreCase) ||
            file.Contains("crashpad", StringComparison.OrdinalIgnoreCase))
        {
            score -= 420;
        }

        score -= normalized.Count(c => c == '/');
        return score;
    }

    private static async Task SaveUpdatedProfileAsync(
        JsonProfileService profileService,
        ProfilesDocument profiles,
        ProfileModel selected,
        CancellationToken cancellationToken)
    {
        var updated = selected with { UpdatedAt = DateTimeOffset.Now };
        var next = profiles with
        {
            DefaultProfileId = string.IsNullOrWhiteSpace(profiles.DefaultProfileId) ? updated.ProfileId : profiles.DefaultProfileId,
            Profiles = profiles.Profiles
                .Select(p => p.ProfileId == updated.ProfileId ? updated : p)
                .ToList()
        };
        await profileService.SaveAsync(next, cancellationToken);
        AppLogService.Info($"已自动修正并保存档案路径: {updated.ProfileId}");
    }

    private static string ResolvePath(string appRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var raw = path.Trim();
        if (!Path.IsPathRooted(raw))
        {
            return Path.GetFullPath(Path.Combine(appRoot, raw));
        }

        var root = Path.GetPathRoot(raw);
        if (string.IsNullOrWhiteSpace(root) ||
            string.Equals(root, Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
            string.Equals(root, Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            // Treat drive-relative rooted paths like "\components\..." as app-relative.
            var trimmed = raw.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(appRoot, trimmed));
        }

        return Path.GetFullPath(raw);
    }

    private static string NormalizeProfilePath(string appRoot, string path)
    {
        var fullRoot = Path.GetFullPath(appRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var fullPath = Path.GetFullPath(path);
        if (IsSubPathOf(fullRoot, fullPath))
        {
            return Path.GetRelativePath(fullRoot, fullPath).Replace('\\', '/');
        }

        return fullPath;
    }

    private static bool IsSubPathOf(string basePath, string childPath)
    {
        var baseWithSep = basePath.EndsWith(Path.DirectorySeparatorChar)
            ? basePath
            : basePath + Path.DirectorySeparatorChar;
        return childPath.StartsWith(baseWithSep, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(childPath, basePath, StringComparison.OrdinalIgnoreCase);
    }

    private static LaunchWorkflowResult Fail(string message)
    {
        return new LaunchWorkflowResult(false, message, null, null);
    }
}

public sealed record LaunchWorkflowResult(
    bool Success,
    string Message,
    string? CopyableCommand,
    string? ManualGuide);
