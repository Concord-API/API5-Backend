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
    public IReadOnlyList<OutcomeBreakdown> OutcomeBreakdown { get; init; } = [];

    public string PartialTreatment => "Na nota de força, a procedência em parte conta como acolhimento.";

    public IReadOnlyList<UnavailableBlock> Unavailable { get; init; } = [];

    public Provenance Provenance { get; init; } = Provenance.None;

    public Scope? Scope { get; init; }
}

public sealed record ThemeNarrative(
    IReadOnlyList<JsonElement> Lead,
    IReadOnlyList<JsonElement> Body,
    string TextOrigin,
    string MethodologyVersion,
    DateOnly GeneratedAt);
