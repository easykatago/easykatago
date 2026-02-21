using Launcher.Core.Abstractions;

namespace Launcher.Core.Services;

public sealed class DefaultCommandBuilder : ICommandBuilder
{
    public string BuildGtpCommand(string katagoPath, string modelPath, string configPath)
    {
        static string Quote(string v) => $"\"{v}\"";
        return $"{Quote(katagoPath)} gtp -model {Quote(modelPath)} -config {Quote(configPath)}";
    }
}
