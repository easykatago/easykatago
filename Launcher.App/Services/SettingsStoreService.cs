using System.IO;
using System.Text.Json;
using Launcher.Core.Models;

namespace Launcher.App.Services;

public sealed class SettingsStoreService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<SettingsModel> LoadAsync(string settingsPath, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(settingsPath, cancellationToken);
        var model = JsonSerializer.Deserialize<SettingsModel>(json, JsonOptions);
        return model ?? new SettingsModel();
    }

    public Task SaveAsync(string settingsPath, SettingsModel model, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(model, JsonOptions);
        return File.WriteAllTextAsync(settingsPath, json, cancellationToken);
    }
}
