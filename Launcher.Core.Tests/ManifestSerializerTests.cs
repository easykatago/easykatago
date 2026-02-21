using Launcher.Core.Services;

namespace Launcher.Core.Tests;

public class ManifestSerializerTests
{
    [Fact]
    public void Deserialize_ShouldReadSchemaAndDefaults()
    {
        const string json = """
                            {
                              "schemaVersion": 1,
                              "updatedAt": "2026-02-19T00:00:00Z",
                              "components": {},
                              "defaults": {
                                "katagoComponentId": "k1"
                              }
                            }
                            """;

        var manifest = ManifestSerializer.Deserialize(json);
        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal("k1", manifest.Defaults.KatagoComponentId);
    }
}
