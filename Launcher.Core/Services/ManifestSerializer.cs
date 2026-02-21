using System.Text.Json;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public static class ManifestSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static ManifestModel Deserialize(string json)
    {
        var manifest = JsonSerializer.Deserialize<ManifestModel>(json, JsonOptions);
        return manifest ?? throw new InvalidOperationException("manifest.json 解析失败。");
    }
}
