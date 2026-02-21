using System.IO;
using Launcher.Core.Abstractions;
using Launcher.Core.Models;

namespace Launcher.Core.UseCases;

public sealed class DefaultInstallOrchestrator(
    IManifestService manifestService,
    IProfileService profileService,
    ICommandBuilder commandBuilder)
{
    public Task<DefaultInstallResult> RunAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync(null, cancellationToken);
    }

    public async Task<DefaultInstallResult> RunAsync(
        DefaultProfilePathOverrides? pathOverrides,
        CancellationToken cancellationToken = default)
    {
        var manifest = await manifestService.LoadAsync(cancellationToken);
        var defaults = manifestService.GetDefaults(manifest);

        var katago = manifest.Components.Katago.FirstOrDefault(x => x.Id == defaults.KatagoComponentId);
        var lizzie = manifest.Components.Lizzieyzy.FirstOrDefault(x => x.Id == defaults.LizzieyzyComponentId);
        var network = manifest.Components.Networks.FirstOrDefault(x => x.Id == defaults.NetworkId);
        var config = manifest.Components.Configs.FirstOrDefault(x => x.Id == defaults.ConfigId);

        if (katago is null || lizzie is null || network is null || config is null)
        {
            return new DefaultInstallResult(false, "manifest 默认组件不完整。");
        }

        var defaultKatagoPath = $"components/katago/{katago.Version}/{katago.Backend ?? "opencl"}/katago.exe";
        var defaultNetworkPath = $"components/networks/{network.Id}/{network.Version}/model.bin.gz";
        var defaultConfigPath = $"components/configs/{config.Id}/{config.Version}/config.cfg";
        var defaultLizziePath = $"components/lizzieyzy/{lizzie.Version}/{lizzie.Entry ?? "LizzieYzy.exe"}";
        var defaultLizzieWorkdir = $"components/lizzieyzy/{lizzie.Version}/";

        var katagoPath = pathOverrides?.KatagoExePath ?? defaultKatagoPath;
        var networkPath = pathOverrides?.NetworkPath ?? defaultNetworkPath;
        var configPath = pathOverrides?.ConfigPath ?? defaultConfigPath;
        var lizziePath = pathOverrides?.LizziePath ?? defaultLizziePath;
        var lizzieWorkdir = pathOverrides?.LizzieWorkdir
            ?? Path.GetDirectoryName(lizziePath)
            ?? defaultLizzieWorkdir;

        var gtp = commandBuilder.BuildGtpCommand(katagoPath, networkPath, configPath);

        var now = DateTimeOffset.Now;
        var profile = new ProfileModel
        {
            ProfileId = "p_default",
            DisplayName = $"{katago.Name}({katago.Backend}) + {network.Name} + {config.Name}",
            Katago = new KatagoProfile
            {
                Version = katago.Version,
                Backend = katago.Backend ?? "opencl",
                Path = katagoPath,
                GtpArgs = gtp
            },
            Network = new NetworkProfile
            {
                Id = network.Id,
                Name = network.Name,
                Path = networkPath,
                Source = network.Source ?? "manifest"
            },
            Config = new ConfigProfile
            {
                Id = config.Id,
                Path = configPath
            },
            Lizzieyzy = new LizzieProfile
            {
                Version = lizzie.Version,
                Type = "exe",
                Path = lizziePath,
                Workdir = lizzieWorkdir
            },
            Tuning = new TuningProfile
            {
                Status = "unknown",
                LastBenchmarkAt = null,
                RecommendedThreads = null
            },
            CreatedAt = now,
            UpdatedAt = now
        };

        var currentProfiles = await profileService.LoadAsync(cancellationToken);
        var updatedProfiles = profileService.Upsert(currentProfiles, profile, setAsDefault: true);
        await profileService.SaveAsync(updatedProfiles, cancellationToken);
        return new DefaultInstallResult(true, "默认 Profile 已生成。");
    }
}

public sealed record DefaultProfilePathOverrides(
    string? KatagoExePath = null,
    string? NetworkPath = null,
    string? ConfigPath = null,
    string? LizziePath = null,
    string? LizzieWorkdir = null);

public sealed record DefaultInstallResult(bool Success, string Message);
