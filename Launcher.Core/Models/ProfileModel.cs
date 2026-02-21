namespace Launcher.Core.Models;

public sealed record ProfilesDocument
{
    public string? DefaultProfileId { get; init; }
    public IReadOnlyList<ProfileModel> Profiles { get; init; } = [];
}

public sealed record ProfileModel
{
    public required string ProfileId { get; init; }
    public required string DisplayName { get; init; }
    public required KatagoProfile Katago { get; init; }
    public required NetworkProfile Network { get; init; }
    public required ConfigProfile Config { get; init; }
    public required LizzieProfile Lizzieyzy { get; init; }
    public required TuningProfile Tuning { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record KatagoProfile
{
    public required string Version { get; init; }
    public required string Backend { get; init; }
    public required string Path { get; init; }
    public required string GtpArgs { get; init; }
}

public sealed record NetworkProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Source { get; init; }
}

public sealed record ConfigProfile
{
    public required string Id { get; init; }
    public required string Path { get; init; }
}

public sealed record LizzieProfile
{
    public required string Version { get; init; }
    public required string Type { get; init; }
    public required string Path { get; init; }
    public required string Workdir { get; init; }
}

public sealed record TuningProfile
{
    public required string Status { get; init; }
    public DateTimeOffset? LastBenchmarkAt { get; init; }
    public int? RecommendedThreads { get; init; }
}
