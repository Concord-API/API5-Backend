using System.Text.Json;

namespace Ratio.Application.Abstractions;

public sealed record ThemeDetail(
    long ThemeKey,
    string Name,
    string? SubjectArea,
    int? StrengthScore,
    string? Level,
    long CaseCount,
    long JudgedCount,
    long CourtCount,
    int? PeriodStartYear,
    int? PeriodEndYear,
    DateOnly? LastDecisionDate,
    ThemeNarrative? Summary)
{
    public IReadOnlyList<UnavailableBlock> Unavailable { get; init; } = [];

    public Provenance Provenance { get; init; } = Provenance.None;

    public RelatedDoctrine RelatedDoctrine { get; init; } = RelatedDoctrine.Empty;
}

public sealed record ThemeNarrative(
    IReadOnlyList<JsonElement> Lead,
    IReadOnlyList<JsonElement> Body,
    string TextOrigin,
    string MethodologyVersion,
    DateOnly GeneratedAt);
