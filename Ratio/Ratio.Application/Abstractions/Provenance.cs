namespace Ratio.Application.Abstractions;

public sealed record Provenance(IReadOnlyList<ProvenanceSource> Sources, string? MethodologyVersion)
{
    public static Provenance None { get; } = new([], null);

    public bool Covers(string block) => Sources.Any(source => source.Block == block);
}

public sealed record ProvenanceSource(
    string Block,
    string Source,
    string Name,
    string? SourceUrl,
    DateTimeOffset ExtractedAt,
    long Count);
