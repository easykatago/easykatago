namespace Launcher.Core.Models;

public sealed record SettingsModel
{
    public string InstallRoot { get; init; } = ".";
    public ProxyModel Proxy { get; init; } = new();
    public DownloadModel Download { get; init; } = new();
    public CacheModel Cache { get; init; } = new();
    public CudaModel Cuda { get; init; } = new();
    public UiModel Ui { get; init; } = new();
}

public sealed record ProxyModel
{
    public string Mode { get; init; } = "system";
    public string? Address { get; init; }
}

public sealed record DownloadModel
{
    public int Concurrency { get; init; } = 3;
    public int Retries { get; init; } = 3;
    public bool AllowUnverified { get; init; } = false;
}

public sealed record CacheModel
{
    public int KeepVersions { get; init; } = 2;
}

public sealed record CudaModel
{
    public string? ManualCudaDirectory { get; init; }
    public string? ManualCudnnDirectory { get; init; }
}

public sealed record UiModel
{
    public string Theme { get; init; } = "system";
    public string Accent { get; init; } = "system";
}
