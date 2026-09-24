namespace Ratio.Application.Abstractions;

public sealed record OutcomeBreakdown(string PolarityLabel, long Judged, IReadOnlyList<OutcomeCategory> Categories);

public sealed record OutcomeCategory(string Outcome, long Count, decimal? Ratio);
