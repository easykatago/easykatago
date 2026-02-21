using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Launcher.Core.Abstractions;
using Launcher.Core.Models;
using Launcher.Core.Services;
using Launcher.Core.UseCases;

namespace Launcher.App.Services;

public sealed class InstallWorkflowService
{
    private readonly BootstrapService _bootstrapService;
    private readonly SettingsStoreService _settingsStoreService = new();

    public InstallWorkflowService(BootstrapService bootstrapService)
    {
        _bootstrapService = bootstrapService;
    }

    public async Task<InstallWorkflowResult> InitializeDefaultsAsync(CancellationToken cancellationToken = default)
    {
        await _bootstrapService.EnsureDefaultsAsync(cancellationToken);
        var manifestService = new LocalManifestService(_bootstrapService.ManifestSnapshotPath);
        var profileService = new JsonProfileService(_bootstrapService.ProfilesPath);
        var commandBuilder = new DefaultCommandBuilder();
        var orchestrator = new DefaultInstallOrchestrator(manifestService, profileService, commandBuilder);
        var result = await orchestrator.RunAsync(cancellationToken);
        AppLogService.Info($"初始化默认配置: {result.Message}");

        return new InstallWorkflowResult(
            result.Success,
            result.Message,
            _bootstrapService.DataRoot,
            _bootstrapService.ProfilesPath,
            _bootstrapService.SettingsPath,
            _bootstrapService.ManifestSnapshotPath,
            ResolveInstallRoot(_bootstrapService.AppRoot, null));
    }

    public async Task<string?> TryResolveCudaVersionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _bootstrapService.EnsureDefaultsAsync(cancellationToken);
            var settings = await _settingsStoreService.LoadAsync(_bootstrapService.SettingsPath, cancellationToken);
            var manifestService = new LocalManifestService(_bootstrapService.ManifestSnapshotPath);
            var manifest = await manifestService.LoadAsync(cancellationToken);
            var defaults = manifest.Defaults;
            var katago = manifest.Components.Katago.FirstOrDefault(x => x.Id == defaults.KatagoComponentId)
                         ?? manifest.Components.Katago.FirstOrDefault();
            if (katago is null)
            {
                return null;
            }

            using var client = CreateHttpClient(settings, includeKataGoTrainingHeaders: true);
            var resolved = await ResolveComponentIfNeededAsync(
                "katago",
                katago,
                client,
                log: null,
                cancellationToken,
                preferredBackend: "cuda");
            return TryExtractCudaVersion(resolved);
        }
        catch (Exception ex)
        {
            AppLogService.Warn($"解析 CUDA 版本失败: {ex.Message}");
            return null;
        }
    }

    public async Task<InstallWorkflowResult> InstallDefaultsAsync(
        Action<string>? log = null,
        IProgress<InstallProgress>? progress = null,
        string? preferredBackend = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _bootstrapService.EnsureDefaultsAsync(cancellationToken);
            var settings = await _settingsStoreService.LoadAsync(_bootstrapService.SettingsPath, cancellationToken);
            var installRoot = ResolveInstallRoot(_bootstrapService.AppRoot, settings.InstallRoot);
            Directory.CreateDirectory(installRoot);

            var manifestService = new LocalManifestService(_bootstrapService.ManifestSnapshotPath);
            var manifest = await manifestService.LoadAsync(cancellationToken);
            var defaults = manifest.Defaults;

            var katago = manifest.Components.Katago.FirstOrDefault(x => x.Id == defaults.KatagoComponentId);
            var lizzie = manifest.Components.Lizzieyzy.FirstOrDefault(x => x.Id == defaults.LizzieyzyComponentId);
            var network = manifest.Components.Networks.FirstOrDefault(x => x.Id == defaults.NetworkId);
            var config = manifest.Components.Configs.FirstOrDefault(x => x.Id == defaults.ConfigId);
            if (katago is null || lizzie is null || network is null || config is null)
            {
                return Fail("manifest 默认组件不完整，无法安装。", installRoot);
            }

            using var client = CreateHttpClient(settings, includeKataGoTrainingHeaders: true);
            using var dependencyClient = CreateHttpClient(settings, includeKataGoTrainingHeaders: false);
            var resolvedKatago = await ResolveComponentIfNeededAsync("katago", katago, client, log, cancellationToken, preferredBackend);
            var resolvedLizzie = await ResolveComponentIfNeededAsync("lizzieyzy", lizzie, client, log, cancellationToken);
            var resolvedNetwork = await ResolveComponentIfNeededAsync("network", network, client, log, cancellationToken);
            var resolvedConfig = await ResolveComponentIfNeededAsync("config", config, client, log, cancellationToken);

            var items = new List<(string Kind, ComponentModel Component)>
            {
                ("katago", resolvedKatago),
                ("lizzieyzy", resolvedLizzie),
                ("network", resolvedNetwork),
                ("config", resolvedConfig)
            };

            foreach (var item in items)
            {
                var invalidReason = ValidateComponent(item.Component);
                if (invalidReason is not null)
                {
                    return Fail($"组件信息无效（{item.Kind}）：{invalidReason}", installRoot);
                }
            }

            log?.Invoke($"安装根目录: {installRoot}");
            log?.Invoke($"下载并发: {Math.Max(1, settings.Download.Concurrency)}，重试: {Math.Max(0, settings.Download.Retries)}");
            AppLogService.Info($"安装根目录: {installRoot}");

            var downloader = new HttpDownloadService(client);
            var dependencyDownloader = new HttpDownloadService(dependencyClient);
            var verify = new DefaultVerifyService();
            var installer = new ArchiveInstallService();
            var installedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < items.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (kind, component) = items[i];
                var title = $"{kind}:{component.Name} {component.Version}";
                progress?.Report(new InstallProgress(title, i + 1, items.Count, 0));
                log?.Invoke($"开始处理 {title}");
                AppLogService.Info($"开始处理组件 {title}");

                var downloadResult = await AcquireComponentFileAsync(
                    component,
                    title,
                    downloader,
                    Math.Max(0, settings.Download.Retries),
                    preferCache: true,
                    progress,
                    i + 1,
                    items.Count,
                    log,
                    cancellationToken);
                if (!downloadResult.Success || string.IsNullOrWhiteSpace(downloadResult.CachePath))
                {
                    return Fail(downloadResult.Message, installRoot);
                }

                var cachePath = downloadResult.CachePath;
                if (component.Size > 0 && !verify.VerifySize(cachePath, component.Size))
                {
                    TryDelete(cachePath);
                    return Fail($"文件大小校验失败：{cachePath}", installRoot);
                }

                if (!string.IsNullOrWhiteSpace(component.Sha256))
                {
                    if (ContainsPlaceholder(component.Sha256))
                    {
                        return Fail($"组件 SHA256 仍为占位符：{component.Id}", installRoot);
                    }

                    var hashOk = await verify.VerifySha256Async(cachePath, component.Sha256, cancellationToken);
                    if (!hashOk)
                    {
                        TryDelete(cachePath);
                        var redownload = await AcquireComponentFileAsync(
                            component,
                            title,
                            downloader,
                            Math.Max(0, settings.Download.Retries),
                            preferCache: false,
                            progress,
                            i + 1,
                            items.Count,
                            log,
                            cancellationToken);
                        if (!redownload.Success || string.IsNullOrWhiteSpace(redownload.CachePath))
                        {
                            return Fail($"SHA256 校验失败，且重新下载失败：{component.Id}", installRoot);
                        }

                        cachePath = redownload.CachePath;
                        hashOk = await verify.VerifySha256Async(cachePath, component.Sha256, cancellationToken);
                        if (!hashOk)
                        {
                            return Fail($"SHA256 校验失败：{cachePath}", installRoot);
                        }
                    }
                }

                var installedPath = await installer.InstallAsync(component, cachePath, installRoot, cancellationToken);
                installedPaths[component.Id] = installedPath;
                progress?.Report(new InstallProgress(title, i + 1, items.Count, 100));
                log?.Invoke($"安装完成: {installedPath}");
                AppLogService.Info($"安装完成 {installedPath}");
            }

            if (string.Equals(resolvedKatago.Backend, "tensorrt", StringComparison.OrdinalIgnoreCase))
            {
                var runtimeResult = await EnsureTensorRtRuntimeAsync(
                    resolvedKatago,
                    installedPaths,
                    installRoot,
                    dependencyClient,
                    dependencyDownloader,
                    Math.Max(0, settings.Download.Retries),
                    log,
                    cancellationToken);
                if (!runtimeResult.Success)
                {
                    return Fail(runtimeResult.Message, installRoot);
                }

                log?.Invoke(runtimeResult.Message);
                AppLogService.Info(runtimeResult.Message);
            }

            await PersistResolvedManifestSnapshotAsync(
                manifest,
                resolvedKatago,
                resolvedLizzie,
                resolvedNetwork,
                resolvedConfig,
                cancellationToken);

            var profileService = new JsonProfileService(_bootstrapService.ProfilesPath);
            var commandBuilder = new DefaultCommandBuilder();
            var orchestrator = new DefaultInstallOrchestrator(manifestService, profileService, commandBuilder);
            var overrides = BuildProfilePathOverrides(
                _bootstrapService.AppRoot,
                installedPaths,
                resolvedKatago,
                resolvedNetwork,
                resolvedConfig,
                resolvedLizzie);
            var profileResult = await orchestrator.RunAsync(overrides, cancellationToken);
            AppLogService.Info($"默认 Profile 更新结果: {profileResult.Message}");

            var message = profileResult.Success
                ? $"{profileResult.Message} 安装目录：{installRoot}"
                : profileResult.Message;

            return new InstallWorkflowResult(
                profileResult.Success,
                message,
                _bootstrapService.DataRoot,
                _bootstrapService.ProfilesPath,
                _bootstrapService.SettingsPath,
                _bootstrapService.ManifestSnapshotPath,
                installRoot);
        }
        catch (Exception ex)
        {
            AppLogService.Error($"默认组件安装异常: {ex}");
            var settings = await SafeLoadSettingsAsync();
            return Fail($"安装异常：{ex.Message}", ResolveInstallRoot(_bootstrapService.AppRoot, settings?.InstallRoot));
        }
    }

    private async Task<SettingsModel?> SafeLoadSettingsAsync()
    {
        try
        {
            return await _settingsStoreService.LoadAsync(_bootstrapService.SettingsPath);
        }
        catch
        {
            return null;
        }
    }

    private DownloadAcquireResult FailDownload(string message)
    {
        AppLogService.Error(message);
        return new DownloadAcquireResult(false, message, null);
    }

    private InstallWorkflowResult Fail(string message, string installRoot)
    {
        AppLogService.Error(message);
        return new InstallWorkflowResult(
            false,
            message,
            _bootstrapService.DataRoot,
            _bootstrapService.ProfilesPath,
            _bootstrapService.SettingsPath,
            _bootstrapService.ManifestSnapshotPath,
            installRoot);
    }

    private async Task<DownloadAcquireResult> AcquireComponentFileAsync(
        ComponentModel component,
        string title,
        IDownloadService downloader,
        int retries,
        bool preferCache,
        IProgress<InstallProgress>? progress,
        int current,
        int total,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var urls = component.Urls
            .Where(static u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (urls.Count == 0)
        {
            return FailDownload($"组件无下载地址：{title}");
        }

        Directory.CreateDirectory(_bootstrapService.CacheRoot);
        var errors = new List<string>();

        foreach (var rawUrl in urls)
        {
            if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
            {
                errors.Add($"无效地址: {rawUrl}");
                continue;
            }

            var cachePath = BuildCachePath(_bootstrapService.CacheRoot, component.Id, uri);
            if (preferCache && File.Exists(cachePath) && new FileInfo(cachePath).Length > 0)
            {
                log?.Invoke($"复用缓存: {cachePath}");
                return new DownloadAcquireResult(true, $"复用缓存：{cachePath}", cachePath);
            }

            for (var attempt = 1; attempt <= retries + 1; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    TryDelete(cachePath);
                    await downloader.DownloadAsync(
                        uri,
                        cachePath,
                        new Progress<DownloadProgress>(p =>
                        {
                            var percent = p.Percentage ?? 0;
                            progress?.Report(new InstallProgress(title, current, total, percent));
                        }),
                        cancellationToken);
                    log?.Invoke($"下载完成: {cachePath}");
                    return new DownloadAcquireResult(true, $"下载完成：{cachePath}", cachePath);
                }
                catch (Exception ex) when (attempt <= retries)
                {
                    log?.Invoke($"下载失败，重试 {attempt}/{retries}: {uri}");
                    AppLogService.Warn($"下载失败，准备重试: {uri}, {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(6, attempt * 2)), cancellationToken);
                }
                catch (Exception ex)
                {
                    var msg = $"{uri} -> {ex.Message}";
                    AppLogService.Warn($"下载地址失败: {msg}");
                    errors.Add(msg);
                    break;
                }
            }
        }

        var joined = string.Join(" | ", errors);
        return FailDownload($"所有下载地址均失败：{title} {joined}{BuildProxyHint(errors)}{BuildKatagoTrainingHint(errors)}");
    }

    private static string BuildCachePath(string cacheRoot, string componentId, Uri uri)
    {
        var fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"{componentId}.bin";
        }

        return Path.Combine(cacheRoot, $"{componentId}-{fileName}");
    }

    private async Task<TensorRtRuntimeEnsureResult> EnsureTensorRtRuntimeAsync(
        ComponentModel katagoComponent,
        IReadOnlyDictionary<string, string> installedPaths,
        string installRoot,
        HttpClient dependencyClient,
        IDownloadService dependencyDownloader,
        int retries,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        if (!TryParseTensorRtBundleInfo(katagoComponent, out var bundleInfo))
        {
            return new TensorRtRuntimeEnsureResult(
                false,
                "检测到 TensorRT 后端，但无法从 KataGo 资产名识别 TRT/CUDA 版本，请在“配置档案”中检查后端版本并重试。");
        }

        var katagoPath = ResolveInstalledFile(installedPaths, katagoComponent, katagoComponent.Entry ?? "katago.exe");
        if (string.IsNullOrWhiteSpace(katagoPath) || !File.Exists(katagoPath))
        {
            return new TensorRtRuntimeEnsureResult(false, "TensorRT 环境安装中止：未找到已安装的 katago.exe。");
        }

        var runtimeUrls = await ResolveTensorRtRuntimeUrlsAsync(dependencyClient, bundleInfo, cancellationToken);
        if (runtimeUrls.Count == 0)
        {
            return new TensorRtRuntimeEnsureResult(false, $"未找到 TensorRT {bundleInfo.TrtVersion} (CUDA {bundleInfo.CudaVersion}) 对应下载地址。");
        }

        var runtimeComponent = new ComponentModel
        {
            Id = $"tensorrt-runtime-{bundleInfo.TrtVersion}-cuda{bundleInfo.CudaVersion}",
            Name = "TensorRT",
            Version = bundleInfo.TrtVersion,
            Type = "zip",
            Urls = runtimeUrls,
            Sha256 = string.Empty
        };

        log?.Invoke($"开始下载 TensorRT 运行库: TRT {bundleInfo.TrtVersion}, CUDA {bundleInfo.CudaVersion}");
        var downloadResult = await AcquireComponentFileAsync(
            runtimeComponent,
            $"runtime:TensorRT {bundleInfo.TrtVersion}",
            dependencyDownloader,
            retries,
            preferCache: true,
            progress: null,
            current: 1,
            total: 1,
            log,
            cancellationToken);
        if (!downloadResult.Success || string.IsNullOrWhiteSpace(downloadResult.CachePath))
        {
            return new TensorRtRuntimeEnsureResult(
                false,
                $"TensorRT 运行库下载失败：{downloadResult.Message}。可在 NVIDIA 官网手动下载并解压后重试。");
        }

        var installer = new ArchiveInstallService();
        var runtimeInstalled = await installer.InstallAsync(runtimeComponent, downloadResult.CachePath, installRoot, cancellationToken);
        var runtimeRoot = File.Exists(runtimeInstalled)
            ? Path.GetDirectoryName(runtimeInstalled) ?? runtimeInstalled
            : runtimeInstalled;
        if (!Directory.Exists(runtimeRoot))
        {
            return new TensorRtRuntimeEnsureResult(false, "TensorRT 运行库解压失败：未找到安装目录。");
        }

        var runtimeDirs = FindTensorRtRuntimeDirectories(runtimeRoot);
        if (runtimeDirs.Count == 0)
        {
            return new TensorRtRuntimeEnsureResult(
                false,
                $"TensorRT 已下载但未找到 nvinfer*.dll：{runtimeRoot}。请手动确认压缩包内容。");
        }

        var copiedDllCount = CopyRuntimeDllsToKatagoDirectory(runtimeDirs, katagoPath);
        log?.Invoke($"TensorRT 运行库已解压到: {runtimeRoot}");
        log?.Invoke($"TensorRT DLL 已同步到 KataGo 目录: {copiedDllCount} 个");

        if (TensorRtRuntimeService.PrependProcessPath(runtimeDirs, out var processPathMsg))
        {
            log?.Invoke(processPathMsg);
        }
        else
        {
            log?.Invoke($"警告: {processPathMsg}");
        }

        if (TensorRtRuntimeService.EnsureMachinePath(runtimeDirs, out var machinePathMsg))
        {
            log?.Invoke(machinePathMsg);
        }
        else
        {
            log?.Invoke($"警告: {machinePathMsg}");
        }

        if (TensorRtRuntimeService.EnsureUserPath(runtimeDirs, out var userPathMsg))
        {
            log?.Invoke(userPathMsg);
        }
        else
        {
            log?.Invoke($"警告: {userPathMsg}");
        }

        if (TensorRtRuntimeService.WriteRuntimeMarker(
                katagoPath,
                runtimeRoot,
                runtimeComponent.Id,
                bundleInfo.TrtVersion,
                bundleInfo.CudaVersion,
                out var markerMsg))
        {
            log?.Invoke(markerMsg);
        }
        else
        {
            log?.Invoke($"警告: {markerMsg}");
        }

        return new TensorRtRuntimeEnsureResult(
            true,
            $"TensorRT 运行库已就绪（TRT {bundleInfo.TrtVersion} / CUDA {bundleInfo.CudaVersion}）。");
    }

    private static bool TryParseTensorRtBundleInfo(ComponentModel katagoComponent, out TensorRtBundleInfo info)
    {
        var candidateTexts = new List<string>();
        candidateTexts.AddRange(katagoComponent.Urls);
        candidateTexts.Add(katagoComponent.Id);
        candidateTexts.Add(katagoComponent.Version);

        foreach (var text in candidateTexts)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var fileName = Path.GetFileName(text);
            var match = Regex.Match(
                fileName,
                "trt(?<trt>\\d+\\.\\d+\\.\\d+)-cuda(?<cuda>\\d+\\.\\d+)",
                RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                continue;
            }

            info = new TensorRtBundleInfo(
                match.Groups["trt"].Value,
                match.Groups["cuda"].Value);
            return true;
        }

        info = default;
        return false;
    }

    private static string? TryExtractCudaVersion(ComponentModel katagoComponent)
    {
        var candidateTexts = new List<string>();
        candidateTexts.AddRange(katagoComponent.Urls);
        candidateTexts.Add(katagoComponent.Id);
        candidateTexts.Add(katagoComponent.Version);

        foreach (var text in candidateTexts)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var fileName = Path.GetFileName(text);
            var m = Regex.Match(fileName, "cuda(?<major>\\d+)\\.(?<minor>\\d+)", RegexOptions.IgnoreCase);
            if (!m.Success)
            {
                continue;
            }

            return $"{m.Groups["major"].Value}.{m.Groups["minor"].Value}";
        }

        return null;
    }

    private static async Task<IReadOnlyList<string>> ResolveTensorRtRuntimeUrlsAsync(
        HttpClient client,
        TensorRtBundleInfo bundleInfo,
        CancellationToken cancellationToken)
    {
        var urls = new List<string>();
        var (major, minor, patch) = ParseVersion(bundleInfo.TrtVersion);
        var pageCandidates = new[]
        {
            $"https://developer.nvidia.com/downloads/compute/machine-learning/tensorrt/{bundleInfo.TrtVersion}",
            $"https://developer.nvidia.com/downloads/compute/machine-learning/tensorrt/{major}.{minor}.{patch}",
            $"https://developer.nvidia.com/downloads/compute/machine-learning/tensorrt/{major}.{minor}.0"
        }
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        var regex = new Regex(
            "https://developer\\.nvidia\\.com/downloads/compute/machine-learning/tensorrt/[0-9.]+/zip/TensorRT-(?<trt>[0-9.]+)\\.(?<build>[0-9]+)\\.Windows\\.win10\\.cuda-(?<cuda>[0-9.]+)\\.zip",
            RegexOptions.IgnoreCase);

        foreach (var page in pageCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var html = await client.GetStringAsync(page, cancellationToken);
                var matches = regex.Matches(html).Cast<Match>();
                foreach (var match in matches)
                {
                    var trt = match.Groups["trt"].Value;
                    var cuda = match.Groups["cuda"].Value;
                    if (!string.Equals(trt, bundleInfo.TrtVersion, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(cuda, bundleInfo.CudaVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    urls.Add(match.Value);
                }
            }
            catch
            {
                // best effort scraping
            }
        }

        urls.AddRange(BuildTensorRtGuessUrls(bundleInfo));
        return urls
            .Where(static u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> BuildTensorRtGuessUrls(TensorRtBundleInfo bundleInfo)
    {
        var (major, minor, _) = ParseVersion(bundleInfo.TrtVersion);
        var baseSegments = new[]
        {
            bundleInfo.TrtVersion,
            $"{major}.{minor}.0"
        };
        var builds = new[] { 34, 32 };

        var urls = new List<string>();
        foreach (var segment in baseSegments.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var build in builds)
            {
                urls.Add(
                    $"https://developer.nvidia.com/downloads/compute/machine-learning/tensorrt/{segment}/zip/TensorRT-{bundleInfo.TrtVersion}.{build}.Windows.win10.cuda-{bundleInfo.CudaVersion}.zip");
            }
        }

        return urls;
    }

    private static IReadOnlyList<string> FindTensorRtRuntimeDirectories(string runtimeRoot)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in EnumerateFilesSafe(runtimeRoot, "nvinfer*.dll", SearchOption.AllDirectories))
        {
            var dir = Path.GetDirectoryName(file);
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            result.Add(dir);
            var parent = Directory.GetParent(dir)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent))
            {
                var siblingBin = Path.Combine(parent, "bin");
                if (Directory.Exists(siblingBin) &&
                    EnumerateFilesSafe(siblingBin, "*.dll", SearchOption.TopDirectoryOnly).Any())
                {
                    result.Add(siblingBin);
                }

                var siblingLib = Path.Combine(parent, "lib");
                if (Directory.Exists(siblingLib) &&
                    EnumerateFilesSafe(siblingLib, "*.dll", SearchOption.TopDirectoryOnly).Any())
                {
                    result.Add(siblingLib);
                }
            }
        }

        if (result.Count == 0)
        {
            foreach (var dir in EnumerateDirectoriesSafe(runtimeRoot, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(dir);
                if (!name.Equals("bin", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("lib", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (EnumerateFilesSafe(dir, "*.dll", SearchOption.TopDirectoryOnly).Any())
                {
                    result.Add(dir);
                }
            }
        }

        return result
            .OrderBy(dir => dir.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(dir => dir, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int CopyRuntimeDllsToKatagoDirectory(IReadOnlyList<string> runtimeDirs, string katagoPath)
    {
        var katagoDir = Path.GetDirectoryName(katagoPath);
        if (string.IsNullOrWhiteSpace(katagoDir) || !Directory.Exists(katagoDir))
        {
            return 0;
        }

        var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in runtimeDirs)
        {
            foreach (var dll in EnumerateFilesSafe(dir, "*.dll", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(dll);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                var target = Path.Combine(katagoDir, fileName);
                File.Copy(dll, target, overwrite: true);
                copied.Add(fileName);
            }
        }

        return copied.Count;
    }

    private static (int Major, int Minor, int Patch) ParseVersion(string version)
    {
        var parts = (version ?? string.Empty)
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var value) ? value : 0)
            .ToList();
        while (parts.Count < 3)
        {
            parts.Add(0);
        }

        return (parts[0], parts[1], parts[2]);
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

    private static IEnumerable<string> EnumerateDirectoriesSafe(string root, string pattern, SearchOption option)
    {
        try
        {
            return Directory.EnumerateDirectories(root, pattern, option);
        }
        catch
        {
            return [];
        }
    }

    private async Task<ComponentModel> ResolveComponentIfNeededAsync(
        string kind,
        ComponentModel component,
        HttpClient httpClient,
        Action<string>? log,
        CancellationToken cancellationToken,
        string? preferredBackend = null)
    {
        if (kind == "lizzieyzy" && HasLegacyLizzieZipUrl(component))
        {
            log?.Invoke("检测到旧版 LizzieYzy.zip 地址，自动切换为真实 Windows64 资产。");
            return await ResolveLizzieAsync(component, httpClient, cancellationToken);
        }

        if (kind == "katago")
        {
            var backend = NormalizeBackend(preferredBackend);
            if (!string.Equals(component.Backend, backend, StringComparison.OrdinalIgnoreCase))
            {
                log?.Invoke($"按选择后端安装 KataGo: {backend}");
                return await ResolveKataGoAsync(component, httpClient, cancellationToken, backend);
            }
        }

        if (kind == "network" && ShouldRefreshNetworkFromSource(component))
        {
            log?.Invoke("从 KataGoTraining 读取推荐网络（Strongest confidently-rated）。");
            return await ResolveNetworkAsync(component, httpClient, cancellationToken);
        }

        if (!NeedsResolution(component))
        {
            return component;
        }

        log?.Invoke($"检测到占位组件，尝试自动解析: {kind}:{component.Id}");
        return kind switch
        {
            "katago" => await ResolveKataGoAsync(component, httpClient, cancellationToken, NormalizeBackend(preferredBackend)),
            "lizzieyzy" => await ResolveLizzieAsync(component, httpClient, cancellationToken),
            "network" => await ResolveNetworkAsync(component, httpClient, cancellationToken),
            "config" => ResolveConfig(component),
            _ => component
        };
    }

    private static bool NeedsResolution(ComponentModel component)
    {
        if (ContainsPlaceholder(component.Id) || ContainsPlaceholder(component.Version))
        {
            return true;
        }

        if (component.Urls.Count == 0)
        {
            return true;
        }

        return component.Urls.Any(url => ContainsPlaceholder(url) || url.Contains("example.com", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasLegacyLizzieZipUrl(ComponentModel component)
    {
        return component.Urls.Any(url =>
            url.Contains("lizzieyzy", StringComparison.OrdinalIgnoreCase) &&
            url.Contains("LizzieYzy.zip", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldRefreshNetworkFromSource(ComponentModel component)
    {
        if ((component.Source ?? string.Empty).Equals("katagotraining", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if ((component.SourcePage ?? string.Empty).Contains("katagotraining.org/networks", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (component.Urls.Any(url => url.Contains("media.katagotraining.org", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return component.Id.Contains("latest", StringComparison.OrdinalIgnoreCase) ||
               component.Id.Contains("strongest", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildProxyHint(IEnumerable<string> errors)
    {
        var hitLocalProxy = errors.Any(error =>
            error.Contains("127.0.0.1:", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("本地计算机积极拒绝", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("actively refused", StringComparison.OrdinalIgnoreCase));
        if (!hitLocalProxy)
        {
            return string.Empty;
        }

        return "。检测到本地代理连接失败（如 127.0.0.1:7897），请在“设置 -> 代理”改为 none 或先启动代理软件。";
    }

    private static string BuildKatagoTrainingHint(IEnumerable<string> errors)
    {
        var hit = errors.Any(error =>
            error.Contains("media.katagotraining.org", StringComparison.OrdinalIgnoreCase) &&
            (error.Contains("403", StringComparison.OrdinalIgnoreCase) ||
             error.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) ||
             error.Contains("Access denied", StringComparison.OrdinalIgnoreCase)));
        if (!hit)
        {
            return string.Empty;
        }

        return "。检测到 KataGoTraining 媒体地址返回 403/AccessDenied，已自动按推荐网络重取地址；若仍失败，请关闭代理后重试。";
    }

    private static bool ContainsPlaceholder(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Contains("REPLACE", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ValidateComponent(ComponentModel component)
    {
        if (ContainsPlaceholder(component.Id))
        {
            return "ID 包含占位符 REPLACE";
        }

        if (ContainsPlaceholder(component.Version))
        {
            return "version 包含占位符 REPLACE";
        }

        if (component.Urls.Count == 0)
        {
            return "urls 为空";
        }

        if (component.Urls.Any(ContainsPlaceholder))
        {
            return "下载地址包含占位符 REPLACE";
        }

        if (component.Urls.Any(url => url.Contains("example.com", StringComparison.OrdinalIgnoreCase)))
        {
            return "下载地址仍是 example.com 占位域名";
        }

        return null;
    }

    private static HttpClient CreateHttpClient(SettingsModel settings, bool includeKataGoTrainingHeaders)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            MaxConnectionsPerServer = Math.Clamp(Math.Max(1, settings.Download.Concurrency), 1, 16)
        };

        var proxyMode = (settings.Proxy.Mode ?? "system").Trim().ToLowerInvariant();
        switch (proxyMode)
        {
            case "none":
                handler.UseProxy = false;
                break;
            case "manual":
                if (string.IsNullOrWhiteSpace(settings.Proxy.Address) ||
                    !Uri.TryCreate(settings.Proxy.Address, UriKind.Absolute, out var proxyUri))
                {
                    throw new InvalidOperationException("手动代理模式下，代理地址无效。请在设置页填写完整地址。");
                }

                handler.UseProxy = true;
                handler.Proxy = new WebProxy(proxyUri);
                break;
            default:
                handler.UseProxy = true;
                handler.Proxy = WebRequest.DefaultWebProxy;
                break;
        }

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(45)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EasyKataGoLauncher/1.0");
        if (includeKataGoTrainingHeaders)
        {
            client.DefaultRequestHeaders.Referrer = new Uri("https://katagotraining.org/networks/");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://katagotraining.org");
        }

        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        return client;
    }

    private static async Task<ComponentModel> ResolveKataGoAsync(
        ComponentModel original,
        HttpClient client,
        CancellationToken cancellationToken,
        string preferredBackend)
    {
        var candidates = await LoadKataGoAssetsFromApiAsync(client, cancellationToken);
        if (candidates.Count == 0)
        {
            candidates = await LoadKataGoAssetsFromHtmlAsync(client, cancellationToken);
        }

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("未在 KataGo 最新发布页找到 Windows x64 资产。");
        }

        var selected = SelectKatagoAsset(candidates, preferredBackend)
            ?? throw new InvalidOperationException($"未找到后端 {preferredBackend} 的 Windows x64 资产。");
        var version = ResolveKataGoVersion(selected.Tag, selected.File, original.Version);
        var url = selected.Url;
        var backend = NormalizeBackend(preferredBackend);

        var id = ContainsPlaceholder(original.Id)
            ? $"katago-win-x64-{backend}-{version}"
            : original.Id;

        return original with
        {
            Id = id,
            Version = version,
            Backend = backend,
            Urls = [url],
            Sha256 = string.Empty
        };
    }

    private static async Task<IReadOnlyList<KatagoAsset>> LoadKataGoAssetsFromApiAsync(HttpClient client, CancellationToken cancellationToken)
    {
        const string apiUrl = "https://api.github.com/repos/lightvector/KataGo/releases/latest";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagEl)
                ? (tagEl.GetString() ?? string.Empty)
                : string.Empty;
            if (!root.TryGetProperty("assets", out var assetsEl) || assetsEl.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var candidates = new List<KatagoAsset>();
            foreach (var asset in assetsEl.EnumerateArray())
            {
                var file = asset.TryGetProperty("name", out var nameEl)
                    ? (nameEl.GetString() ?? string.Empty)
                    : string.Empty;
                var url = asset.TryGetProperty("browser_download_url", out var urlEl)
                    ? (urlEl.GetString() ?? string.Empty)
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                if (!file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    !file.Contains("windows", StringComparison.OrdinalIgnoreCase) ||
                    !file.Contains("x64", StringComparison.OrdinalIgnoreCase) ||
                    file.Contains("+bs50", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                candidates.Add(new KatagoAsset(
                    Url: url,
                    Tag: tag,
                    File: file));
            }

            return candidates;
        }
        catch
        {
            return [];
        }
    }

    private static async Task<IReadOnlyList<KatagoAsset>> LoadKataGoAssetsFromHtmlAsync(HttpClient client, CancellationToken cancellationToken)
    {
        const string pageUrl = "https://github.com/lightvector/KataGo/releases/latest";
        var html = await client.GetStringAsync(pageUrl, cancellationToken);
        var regex = new Regex(
            "href=\"(?<path>/lightvector/KataGo/releases/download/(?<tag>[^/]+)/(?<file>[^\"\\s]*windows[^\"\\s]*x64[^\"\\s]*\\.zip))\"",
            RegexOptions.IgnoreCase);
        return regex.Matches(html)
            .Cast<Match>()
            .Select(m =>
            {
                var path = WebUtility.HtmlDecode(m.Groups["path"].Value);
                return new KatagoAsset(
                    Url: "https://github.com" + path,
                    Tag: m.Groups["tag"].Value,
                    File: m.Groups["file"].Value);
            })
            .Where(a => !a.File.Contains("+bs50", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string ResolveKataGoVersion(string tag, string fileName, string fallbackVersion)
    {
        var normalizedTag = (tag ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalizedTag))
        {
            return normalizedTag.TrimStart('v', 'V');
        }

        var m = Regex.Match(fileName ?? string.Empty, "katago-v(?<version>\\d+\\.\\d+\\.\\d+)", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            return m.Groups["version"].Value;
        }

        return string.IsNullOrWhiteSpace(fallbackVersion) ? "unknown" : fallbackVersion;
    }

    private static async Task<ComponentModel> ResolveLizzieAsync(ComponentModel original, HttpClient client, CancellationToken cancellationToken)
    {
        const string pageUrl = "https://github.com/yzyray/lizzieyzy/releases/latest";
        var html = await client.GetStringAsync(pageUrl, cancellationToken);
        var regex = new Regex(
            "href=\"(?<path>/yzyray/lizzieyzy/releases/download/(?<tag>[^/]+)/(?<file>[^\"\\s]*\\.zip))\"",
            RegexOptions.IgnoreCase);
        var matches = regex.Matches(html).Cast<Match>().ToList();
        if (matches.Count == 0)
        {
            throw new InvalidOperationException("未在 LizzieYzy 最新发布页找到 zip 资产。");
        }

        var chosen = matches.FirstOrDefault(m =>
                m.Groups["file"].Value.Contains("windows64.without.engine.zip", StringComparison.OrdinalIgnoreCase))
            ?? matches.FirstOrDefault(m =>
                m.Groups["file"].Value.Contains("windows64", StringComparison.OrdinalIgnoreCase) &&
                m.Groups["file"].Value.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ?? matches.FirstOrDefault(m =>
                m.Groups["file"].Value.Contains("windows", StringComparison.OrdinalIgnoreCase))
            ?? matches[0];

        var path = WebUtility.HtmlDecode(chosen.Groups["path"].Value);
        var tag = chosen.Groups["tag"].Value;
        var version = tag.TrimStart('v', 'V');
        var url = "https://github.com" + path;

        var id = ContainsPlaceholder(original.Id)
            ? $"lizzieyzy-{version}"
            : original.Id;

        return original with
        {
            Id = id,
            Version = version,
            Urls = [url],
            Sha256 = string.Empty,
            Entry = string.IsNullOrWhiteSpace(original.Entry) ? "LizzieYzy.exe" : original.Entry
        };
    }

    private static async Task<ComponentModel> ResolveNetworkAsync(ComponentModel original, HttpClient client, CancellationToken cancellationToken)
    {
        var pageUrl = string.IsNullOrWhiteSpace(original.SourcePage)
            ? "https://katagotraining.org/networks/"
            : original.SourcePage;
        var html = await client.GetStringAsync(pageUrl, cancellationToken);
        var (networkName, url) = TryPickStrongestNetwork(html)
            ?? TryPickLatestNetwork(html)
            ?? TryPickFirstNetwork(html)
            ?? throw new InvalidOperationException("未在 KataGoTraining 页面找到网络模型下载地址。");
        url = WebUtility.HtmlDecode(url);
        var fileName = Path.GetFileName(url);
        var baseName = fileName.EndsWith(".bin.gz", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^7]
            : Path.GetFileNameWithoutExtension(fileName);
        var shouldUseStrongestId = ShouldRefreshNetworkFromSource(original) || ContainsPlaceholder(original.Id);
        var id = shouldUseStrongestId ? "kata1-strongest" : original.Id;
        var version = baseName;
        var name = string.IsNullOrWhiteSpace(networkName) ? baseName : networkName;

        return original with
        {
            Id = id,
            Name = name,
            Version = version,
            Urls = [url],
            Sha256 = string.Empty
        };
    }

    private static (string NetworkName, string Url)? TryPickStrongestNetwork(string html)
    {
        return TryPickNetworkByLabel(
            html,
            "Strongest\\s+confidently(?:\\s|[-‑–—])*rated\\s+network");
    }

    private static (string NetworkName, string Url)? TryPickLatestNetwork(string html)
    {
        return TryPickNetworkByLabel(
            html,
            "Latest\\s+network");
    }

    private static (string NetworkName, string Url)? TryPickFirstNetwork(string html)
    {
        var urlMatch = Regex.Match(
            html,
            "https://media\\.katagotraining\\.org/uploaded/networks/models/kata1/(?<name>[^\"\\s']+)\\.bin\\.gz",
            RegexOptions.IgnoreCase);
        if (!urlMatch.Success)
        {
            return null;
        }

        var name = WebUtility.HtmlDecode(urlMatch.Groups["name"].Value);
        return (name, urlMatch.Value);
    }

    private static (string NetworkName, string Url)? TryPickNetworkByLabel(string html, string labelPattern)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var labelMatch = Regex.Match(
            html,
            $"{labelPattern}\\s*:\\s*(?<tail>.{{0,2500}})",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!labelMatch.Success)
        {
            return null;
        }

        var tail = labelMatch.Groups["tail"].Value;
        var anchorMatch = Regex.Match(
            tail,
            "<a[^>]*href\\s*=\\s*\"(?<href>[^\"]+)\"[^>]*>\\s*(?<name>kata1-[^<\\s]+)\\s*</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (anchorMatch.Success)
        {
            var name = WebUtility.HtmlDecode(anchorMatch.Groups["name"].Value);
            var href = WebUtility.HtmlDecode(anchorMatch.Groups["href"].Value);
            var url = NormalizeNetworkUrl(href, name);
            if (!string.IsNullOrWhiteSpace(url))
            {
                return (name, url);
            }
        }

        var urlMatch = Regex.Match(
            tail,
            "https://media\\.katagotraining\\.org/uploaded/networks/models/kata1/(?<name>[^\"\\s'<>]+)\\.bin\\.gz",
            RegexOptions.IgnoreCase);
        if (!urlMatch.Success)
        {
            return null;
        }

        var fallbackName = WebUtility.HtmlDecode(urlMatch.Groups["name"].Value);
        return (fallbackName, urlMatch.Value);
    }

    private static string? NormalizeNetworkUrl(string href, string? fallbackName)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        if (href.StartsWith("/", StringComparison.Ordinal))
        {
            return "https://katagotraining.org" + href;
        }

        if (!string.IsNullOrWhiteSpace(fallbackName))
        {
            return $"https://media.katagotraining.org/uploaded/networks/models/kata1/{fallbackName}.bin.gz";
        }

        return null;
    }

    private static ComponentModel ResolveConfig(ComponentModel original)
    {
        var url = "https://raw.githubusercontent.com/lightvector/KataGo/master/cpp/configs/gtp_example.cfg";
        var id = ContainsPlaceholder(original.Id) ? "balanced" : original.Id;
        var version = ContainsPlaceholder(original.Version) ? "1" : original.Version;
        return original with
        {
            Id = id,
            Version = version,
            Urls = [url],
            Sha256 = string.Empty
        };
    }

    private static string NormalizeBackend(string? raw)
    {
        return (raw ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "cuda" => "cuda",
            "tensorrt" => "tensorrt",
            "trt" => "tensorrt",
            "eigen" => "eigen",
            _ => "opencl"
        };
    }

    private static KatagoAsset? SelectKatagoAsset(IReadOnlyList<KatagoAsset> candidates, string preferredBackend)
    {
        var backend = NormalizeBackend(preferredBackend);
        return backend switch
        {
            "cuda" => SelectCudaAsset(candidates),
            "tensorrt" => SelectTensorRtAsset(candidates),
            "eigen" => SelectEigenAsset(candidates),
            _ => SelectOpenClAsset(candidates)
        };
    }

    private static KatagoAsset? SelectOpenClAsset(IReadOnlyList<KatagoAsset> candidates)
    {
        return candidates.FirstOrDefault(c =>
            c.File.Contains("opencl", StringComparison.OrdinalIgnoreCase));
    }

    private static KatagoAsset? SelectCudaAsset(IReadOnlyList<KatagoAsset> candidates)
    {
        return candidates
            .Where(c =>
                c.File.Contains("cuda", StringComparison.OrdinalIgnoreCase) &&
                !c.File.Contains("trt", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => ExtractCudaVersionRank(c.File))
            .ThenByDescending(c => c.File, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static KatagoAsset? SelectTensorRtAsset(IReadOnlyList<KatagoAsset> candidates)
    {
        return candidates
            .Where(c => c.File.Contains("trt", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => ExtractTrtVersionRank(c.File))
            .ThenByDescending(c => ExtractCudaVersionRank(c.File))
            .ThenByDescending(c => c.File, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static KatagoAsset? SelectEigenAsset(IReadOnlyList<KatagoAsset> candidates)
    {
        var eigenOnly = candidates.FirstOrDefault(c =>
            c.File.Contains("eigen", StringComparison.OrdinalIgnoreCase) &&
            !c.File.Contains("avx2", StringComparison.OrdinalIgnoreCase));
        return eigenOnly ?? candidates.FirstOrDefault(c =>
            c.File.Contains("eigen", StringComparison.OrdinalIgnoreCase));
    }

    private static int ExtractCudaVersionRank(string file)
    {
        var m = Regex.Match(file, "cuda(?<major>\\d+)\\.(?<minor>\\d+)", RegexOptions.IgnoreCase);
        if (!m.Success)
        {
            return -1;
        }

        var major = int.Parse(m.Groups["major"].Value);
        var minor = int.Parse(m.Groups["minor"].Value);
        return major * 100 + minor;
    }

    private static int ExtractTrtVersionRank(string file)
    {
        var m = Regex.Match(file, "trt(?<major>\\d+)\\.(?<minor>\\d+)\\.(?<patch>\\d+)", RegexOptions.IgnoreCase);
        if (!m.Success)
        {
            return -1;
        }

        var major = int.Parse(m.Groups["major"].Value);
        var minor = int.Parse(m.Groups["minor"].Value);
        var patch = int.Parse(m.Groups["patch"].Value);
        return major * 10000 + minor * 100 + patch;
    }

    private DefaultProfilePathOverrides BuildProfilePathOverrides(
        string appRoot,
        IReadOnlyDictionary<string, string> installedPaths,
        ComponentModel katago,
        ComponentModel network,
        ComponentModel config,
        ComponentModel lizzie)
    {
        var katagoPath = ResolveInstalledFile(installedPaths, katago, katago.Entry ?? "katago.exe")
            ?? $"components/katago/{katago.Version}/{katago.Backend ?? "opencl"}/katago.exe";
        var networkPath = ResolveInstalledFile(installedPaths, network, "model.bin.gz")
            ?? $"components/networks/{network.Id}/{network.Version}/model.bin.gz";
        var configPath = ResolveInstalledFile(installedPaths, config, "config.cfg")
            ?? $"components/configs/{config.Id}/{config.Version}/config.cfg";
        var lizziePath = ResolveInstalledFile(installedPaths, lizzie, lizzie.Entry ?? "LizzieYzy.exe")
            ?? $"components/lizzieyzy/{lizzie.Version}/{lizzie.Entry ?? "LizzieYzy.exe"}";
        var lizzieWorkdir = Path.GetDirectoryName(lizziePath) ?? ".";

        return new DefaultProfilePathOverrides(
            KatagoExePath: NormalizeProfilePath(appRoot, katagoPath),
            NetworkPath: NormalizeProfilePath(appRoot, networkPath),
            ConfigPath: NormalizeProfilePath(appRoot, configPath),
            LizziePath: NormalizeProfilePath(appRoot, lizziePath),
            LizzieWorkdir: NormalizeProfilePath(appRoot, lizzieWorkdir));
    }

    private static string? ResolveInstalledFile(
        IReadOnlyDictionary<string, string> installedPaths,
        ComponentModel component,
        string expectedFileName)
    {
        if (!installedPaths.TryGetValue(component.Id, out var path) || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (File.Exists(path))
        {
            return path;
        }

        if (!Directory.Exists(path))
        {
            return null;
        }

        var direct = Path.Combine(path, expectedFileName);
        if (File.Exists(direct))
        {
            return direct;
        }

        var fallback = Directory
            .GetFiles(path, expectedFileName, SearchOption.AllDirectories)
            .FirstOrDefault();
        if (fallback is not null)
        {
            return fallback;
        }

        if (expectedFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return FindExecutableCandidate(path, expectedFileName, component.Name);
        }

        return path;
    }

    private static string? FindExecutableCandidate(string root, string expectedFileName, string? componentName)
    {
        var candidates = Directory.GetFiles(root, "*.exe", SearchOption.AllDirectories);
        if (candidates.Length == 0)
        {
            return null;
        }

        var expected = Path.GetFileName(expectedFileName);
        return candidates
            .Select(path => new
            {
                Path = path,
                Score = ScoreExecutable(path, expected, componentName)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Path.Length)
            .Select(x => x.Path)
            .FirstOrDefault();
    }

    private static int ScoreExecutable(string fullPath, string expectedFileName, string? componentName)
    {
        var score = 0;
        var fileName = Path.GetFileName(fullPath);
        var normalized = fullPath.Replace('\\', '/');

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

        if (normalized.Contains("/jre/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/jdk/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/runtime/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/jcef", StringComparison.OrdinalIgnoreCase))
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

        score -= normalized.Count(c => c == '/');
        return score;
    }

    private static string NormalizeProfilePath(string appRoot, string path)
    {
        var fullRoot = Path.GetFullPath(appRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var fullPath = Path.GetFullPath(path);
        if (IsSubPathOf(fullRoot, fullPath))
        {
            var relative = Path.GetRelativePath(fullRoot, fullPath);
            return relative.Replace('\\', '/');
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

    private static string ResolveInstallRoot(string appRoot, string? installRoot)
    {
        var target = string.IsNullOrWhiteSpace(installRoot) ? "." : installRoot.Trim();
        return Path.IsPathRooted(target)
            ? Path.GetFullPath(target)
            : Path.GetFullPath(Path.Combine(appRoot, target));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best effort
        }
    }

    private async Task PersistResolvedManifestSnapshotAsync(
        ManifestModel original,
        ComponentModel katago,
        ComponentModel lizzie,
        ComponentModel network,
        ComponentModel config,
        CancellationToken cancellationToken)
    {
        var updated = original with
        {
            UpdatedAt = DateTimeOffset.UtcNow,
            Components = original.Components with
            {
                Katago = UpsertComponent(original.Components.Katago, katago),
                Lizzieyzy = UpsertComponent(original.Components.Lizzieyzy, lizzie),
                Networks = UpsertComponent(original.Components.Networks, network),
                Configs = UpsertComponent(original.Components.Configs, config)
            },
            Defaults = original.Defaults with
            {
                KatagoComponentId = katago.Id,
                LizzieyzyComponentId = lizzie.Id,
                NetworkId = network.Id,
                ConfigId = config.Id
            }
        };

        var json = JsonSerializer.Serialize(updated, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_bootstrapService.ManifestSnapshotPath, json, cancellationToken);
    }

    private static IReadOnlyList<ComponentModel> UpsertComponent(IReadOnlyList<ComponentModel> list, ComponentModel component)
    {
        var next = list.Where(item => !item.Id.Equals(component.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        next.Insert(0, component);
        return next;
    }

    private readonly record struct TensorRtBundleInfo(string TrtVersion, string CudaVersion);
    private readonly record struct TensorRtRuntimeEnsureResult(bool Success, string Message);
    private sealed record KatagoAsset(string Url, string Tag, string File);
    private sealed record DownloadAcquireResult(bool Success, string Message, string? CachePath);
}

public sealed record InstallWorkflowResult(
    bool Success,
    string Message,
    string DataRoot,
    string ProfilesPath,
    string SettingsPath,
    string ManifestSnapshotPath,
    string InstallRoot);

public sealed record InstallProgress(string Stage, int Current, int Total, double PercentInStage);


