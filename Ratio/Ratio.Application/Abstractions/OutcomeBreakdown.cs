namespace Ratio.Application.Abstractions;

public sealed record OutcomeBreakdown(string PolarityLabel, long Judged, IReadOnlyList<OutcomeCategory> Categories)
{
    public string? Instance { get; init; }
}

public sealed record OutcomeCategory(string Outcome, long Count, decimal? Ratio);
