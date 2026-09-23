namespace Ratio.Application.Abstractions;

public sealed record ThemeSummary(
    long ThemeKey,
    string Name,
    string? SubjectArea,
    long JudgedCount,
    int? StrengthScore,
    string? Level,
    ThemeOutcome Outcome,
    DateOnly? LastDecisionDate);

public sealed record ThemeOutcome(long Upheld, long Rejected, decimal? UpheldRatio, string? PolarityLabel);
