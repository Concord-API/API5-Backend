namespace Ratio.Application.Abstractions;

public sealed record RelatedDoctrine(double Threshold, IReadOnlyList<DoctrineEntry> Entries)
{
    public const double DeclaredThreshold = 0.55;

    public const int MaxEntries = 20;

    public static RelatedDoctrine Empty { get; } = new(DeclaredThreshold, []);
}

public sealed record DoctrineEntry(
    string Title,
    string? Authors,
    string? Journal,
    int? PublicationYear,
    string? Doi,
    string? Link,
    string Source,
    double? Similarity,
    string LinkMethod,
    string? EmbeddingModel);
