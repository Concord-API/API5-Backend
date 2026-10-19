using System.Text.Json;

namespace Ratio.Application.Abstractions;

public sealed record ThemeSummary(
    long ThemeKey,
    string Name,
    string? SubjectArea,
    long JudgedCount,
    int? StrengthScore,
    string? Level,
    ThemeOutcome Outcome,
    int? PeriodStartYear,
    int? PeriodEndYear,
    DateOnly? LastDecisionDate)
{
    public IReadOnlyList<string> Courts { get; init; } = [];

    public int AdditionalCourtCount { get; init; }

    public ThemeResultSummary? Summary { get; init; }
}

public sealed record ThemeOutcome(long Upheld, long Rejected, decimal? UpheldRatio, string? PolarityLabel);

public sealed record ThemeResultSummary(IReadOnlyList<JsonElement> Lead);
