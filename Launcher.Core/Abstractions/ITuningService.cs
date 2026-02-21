namespace Launcher.Core.Abstractions;

public interface ITuningService
{
    Task<TuningRunResult> RunBenchmarkAsync(
        string katagoPath,
        string modelPath,
        string configPath,
        IProgress<string>? outputProgress = null,
        CancellationToken cancellationToken = default);

    Task<TuningRunResult> RunTunerAsync(
        string katagoPath,
        string modelPath,
        string configPath,
        IProgress<string>? outputProgress = null,
        CancellationToken cancellationToken = default);

    Task<int?> ApplyRecommendedThreadsAsync(string configPath, int recommendedThreads, CancellationToken cancellationToken = default);
}

public sealed record TuningRunResult(bool Success, int? RecommendedThreads, string RawOutput, string? ErrorMessage = null);
