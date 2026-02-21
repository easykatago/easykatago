using System.Diagnostics;
using System.Text;
using Launcher.Core.Abstractions;

namespace Launcher.Core.Services;

public sealed class ProcessTuningService : ITuningService
{
    public Task<TuningRunResult> RunBenchmarkAsync(
        string katagoPath,
        string modelPath,
        string configPath,
        IProgress<string>? outputProgress = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = $"benchmark -model \"{modelPath}\" -config \"{configPath}\"";
        return RunAndParseAsync(katagoPath, arguments, outputProgress, cancellationToken);
    }

    public Task<TuningRunResult> RunTunerAsync(
        string katagoPath,
        string modelPath,
        string configPath,
        IProgress<string>? outputProgress = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = $"tuner -model \"{modelPath}\" -config \"{configPath}\"";
        return RunAndParseAsync(katagoPath, arguments, outputProgress, cancellationToken);
    }

    public async Task<int?> ApplyRecommendedThreadsAsync(string configPath, int recommendedThreads, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CfgEditor.Backup(configPath, DateTimeOffset.Now);
        var input = await File.ReadAllTextAsync(configPath, cancellationToken);
        var output = CfgEditor.UpsertNumSearchThreads(input, recommendedThreads);
        await File.WriteAllTextAsync(configPath, output, cancellationToken);
        return recommendedThreads;
    }

    private static async Task<TuningRunResult> RunAndParseAsync(
        string fileName,
        string arguments,
        IProgress<string>? outputProgress,
        CancellationToken cancellationToken)
    {
        if (TensorRtRuntimeService.IsTensorRtBackend(backend: null, katagoPath: fileName))
        {
            var runtimeDirs = TensorRtRuntimeService.DiscoverRuntimeDirectories(fileName);
            TensorRtRuntimeService.PrependProcessPath(runtimeDirs, out _);
        }
        else if (CudaRuntimeService.IsCudaBackend(backend: null, katagoPath: fileName))
        {
            var runtimeDirs = CudaRuntimeService.DiscoverRuntimeDirectories(fileName);
            TensorRtRuntimeService.PrependProcessPath(runtimeDirs, out _);
        }

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var buffer = new StringBuilder();
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            buffer.AppendLine(e.Data);
            outputProgress?.Report(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            buffer.AppendLine(e.Data);
            outputProgress?.Report(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        var all = buffer.ToString();
        var recommended = BenchmarkOutputParser.ParseRecommendedThreads(all);
        return process.ExitCode == 0
            ? new TuningRunResult(true, recommended, all)
            : new TuningRunResult(false, recommended, all, $"进程退出码: {process.ExitCode}");
    }
}
