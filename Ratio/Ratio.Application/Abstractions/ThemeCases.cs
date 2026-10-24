namespace Ratio.Application.Abstractions;

public sealed record SampleCase(
    string CaseNumber,
    string CourtCode,
    string? JudgingBody,
    string? CaseClass,
    string CourtLevel,
    DateOnly? DecisionDate,
    string Outcome,
    string Source,
    string? SourceLink,
    string? SourceLinkType);

public sealed record ThemeCases(long ThemeKey, IReadOnlyList<SampleCase> Cases, long Total)
{
    public string Ordering => "julgamento mais recente primeiro; empate pelo número do processo";
}
