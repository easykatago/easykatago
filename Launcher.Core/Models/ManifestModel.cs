namespace Launcher.Core.Models;

public sealed record ManifestModel
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyList<MirrorModel> Mirrors { get; init; } = [];
    public ComponentGroupModel Components { get; init; } = new();
    public DefaultSelectionModel Defaults { get; init; } = new();
}

public sealed record MirrorModel
{
    public required string Name { get; init; }
    public IReadOnlyList<string> BaseUrls { get; init; } = [];
}

public sealed record ComponentGroupModel
{
    public IReadOnlyList<ComponentModel> Katago { get; init; } = [];
    public IReadOnlyList<ComponentModel> Lizzieyzy { get; init; } = [];
    public IReadOnlyList<ComponentModel> Networks { get; init; } = [];
    public IReadOnlyList<ComponentModel> Configs { get; init; } = [];
    public IReadOnlyList<ComponentModel> Jre { get; init; } = [];
}

public sealed record ComponentModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public string? Os { get; init; }
    public string? Arch { get; init; }
    public string? Backend { get; init; }
    public string? Type { get; init; }
    public IReadOnlyList<string> Urls { get; init; } = [];
    public string? Sha256 { get; init; }
    public long Size { get; init; }
    public string? Entry { get; init; }
    public string? Source { get; init; }
    public string? SourcePage { get; init; }
    public string? PublishedAt { get; init; }
}

public sealed record DefaultSelectionModel
{
    public string? KatagoComponentId { get; init; }
    public string? LizzieyzyComponentId { get; init; }
    public string? NetworkId { get; init; }
    public string? ConfigId { get; init; }
}
