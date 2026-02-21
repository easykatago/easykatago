namespace Launcher.Core.Abstractions;

public interface ILizzieConfigService
{
    Task<LizzieConfigWriteResult> TryWriteEngineAsync(
        string lizziePath,
        string gtpCommand,
        CancellationToken cancellationToken = default);

    Task<LizziePreferenceWriteResult> TryWriteKataThreadPreferenceAsync(
        string lizziePath,
        int threads,
        bool autoLoad = true,
        CancellationToken cancellationToken = default);
}

public sealed record LizzieConfigWriteResult(bool Success, string? Message, string? ManualGuideText, string? CopyableCommand);

public sealed record LizziePreferenceWriteResult(bool Success, string? Message);
