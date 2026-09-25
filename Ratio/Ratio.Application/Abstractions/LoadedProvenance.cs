namespace Ratio.Application.Abstractions;

public sealed record LoadedProvenance(IReadOnlyList<LoadedSource> Sources, string? MethodologyVersion)
{
    public static LoadedProvenance Empty { get; } = new([], null);
}

public sealed record LoadedSource(string? Block, string? Source, DateTimeOffset? ExtractedAt, long Count);
