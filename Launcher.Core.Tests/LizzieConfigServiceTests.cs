using System.Text;
using System.Text.Json.Nodes;
using Launcher.Core.Services;

namespace Launcher.Core.Tests;

public class LizzieConfigServiceTests
{
    [Fact]
    public async Task TryWriteEngineAsync_ShouldPopulateEngineSettingsList_ForExistingConfig()
    {
        var root = Path.Combine(Path.GetTempPath(), "easykatago-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configPath = Path.Combine(root, "config.txt");
            var original = """
                           {
                             "leelaz": {
                               "engine-settings-list": []
                             },
                             "ui": {
                               "first-load-katago": true
                             }
                           }
                           """;
            await File.WriteAllTextAsync(configPath, original, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var gtpCommand = "\"components/katago/1.16.4/opencl/katago.exe\" gtp -model \"components/networks/kata1-strongest/model.bin.gz\" -config \"components/configs/balanced/1/config.cfg\"";
            var service = new LizzieConfigService();
            var result = await service.TryWriteEngineAsync(root, gtpCommand);

            Assert.True(result.Success);
            Assert.Null(result.ManualGuideText);
            Assert.Null(result.CopyableCommand);

            var bytes = await File.ReadAllBytesAsync(configPath);
            Assert.True(bytes.Length > 0);
            Assert.False(HasUtf8Bom(bytes));

            var json = await File.ReadAllTextAsync(configPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var doc = JsonNode.Parse(json)!.AsObject();
            var leelaz = doc["leelaz"]!.AsObject();
            var engineList = leelaz["engine-settings-list"]!.AsArray();
            Assert.NotEmpty(engineList);

            var engine = engineList[0]!.AsObject();
            Assert.Equal("EasyKataGo", engine["name"]!.GetValue<string>());
            Assert.Equal(gtpCommand, engine["command"]!.GetValue<string>());
            Assert.True(engine["isDefault"]!.GetValue<bool>());
            Assert.Equal(gtpCommand, doc["engineCommand"]!.GetValue<string>());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TryWriteEngineAsync_ShouldCreatePrimaryConfig_WhenMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "easykatago-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configPath = Path.Combine(root, "config.txt");
            var gtpCommand = "\"components/katago/1.16.4/opencl/katago.exe\" gtp -model \"components/networks/kata1-strongest/model.bin.gz\" -config \"components/configs/balanced/1/config.cfg\"";

            var service = new LizzieConfigService();
            var result = await service.TryWriteEngineAsync(root, gtpCommand);

            Assert.True(result.Success);
            Assert.True(File.Exists(configPath));

            var bytes = await File.ReadAllBytesAsync(configPath);
            Assert.True(bytes.Length > 0);
            Assert.False(HasUtf8Bom(bytes));

            var json = await File.ReadAllTextAsync(configPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var doc = JsonNode.Parse(json)!.AsObject();
            var engineList = doc["leelaz"]!["engine-settings-list"]!.AsArray();
            Assert.NotEmpty(engineList);
            Assert.Equal(gtpCommand, engineList[0]!["command"]!.GetValue<string>());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static bool HasUtf8Bom(byte[] bytes)
    {
        return bytes.Length >= 3 &&
               bytes[0] == 0xEF &&
               bytes[1] == 0xBB &&
               bytes[2] == 0xBF;
    }
}
