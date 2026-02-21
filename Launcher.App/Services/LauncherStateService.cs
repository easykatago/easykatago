using System.IO;
using System.Text.Json;
using Launcher.Core.Models;

namespace Launcher.App.Services;

public sealed class LauncherStateService
{
    public LauncherSnapshot GetSnapshot(string appRoot)
    {
        var bootstrap = new BootstrapService(appRoot);
        var hasProfiles = File.Exists(bootstrap.ProfilesPath);
        var hasSettings = File.Exists(bootstrap.SettingsPath);
        var hasManifest = File.Exists(bootstrap.ManifestSnapshotPath);
        var isInitialized = hasProfiles && hasSettings && hasManifest;

        string? defaultProfileName = null;
        string? defaultProfileId = null;
        string? engineBackend = null;
        string? networkName = null;
        string? tuningStatus = null;

        if (hasProfiles)
        {
            try
            {
                var json = File.ReadAllText(bootstrap.ProfilesPath);
                var doc = JsonSerializer.Deserialize<ProfilesDocument>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var profile = doc?.Profiles.FirstOrDefault(p => p.ProfileId == doc.DefaultProfileId) ?? doc?.Profiles.FirstOrDefault();
                if (profile is not null)
                {
                    defaultProfileName = profile.DisplayName;
                    defaultProfileId = profile.ProfileId;
                    engineBackend = profile.Katago.Backend;
                    networkName = profile.Network.Name;
                    tuningStatus = profile.Tuning.Status;
                }
            }
            catch
            {
                // Keep snapshot resilient even if profile JSON is malformed.
            }
        }

        var logFiles = Directory.Exists(bootstrap.LogsRoot)
            ? Directory.GetFiles(bootstrap.LogsRoot, "*", SearchOption.TopDirectoryOnly).Length
            : 0;

        return new LauncherSnapshot(
            IsInitialized: isInitialized,
            DataRoot: bootstrap.DataRoot,
            DefaultProfileName: defaultProfileName,
            DefaultProfileId: defaultProfileId,
            EngineBackend: engineBackend,
            NetworkName: networkName,
            TuningStatus: tuningStatus,
            LogFileCount: logFiles,
            HasManifest: hasManifest,
            HasSettings: hasSettings,
            HasProfiles: hasProfiles);
    }
}

public sealed record LauncherSnapshot(
    bool IsInitialized,
    string DataRoot,
    string? DefaultProfileName,
    string? DefaultProfileId,
    string? EngineBackend,
    string? NetworkName,
    string? TuningStatus,
    int LogFileCount,
    bool HasManifest,
    bool HasSettings,
    bool HasProfiles);
