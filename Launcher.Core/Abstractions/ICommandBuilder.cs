namespace Launcher.Core.Abstractions;

public interface ICommandBuilder
{
    string BuildGtpCommand(string katagoPath, string modelPath, string configPath);
}
