namespace Ratio.Application.Abstractions;

public sealed record CourtAlignment(string CourtCode, long N, decimal? Percentage, string PolarityLabel)
{
    public long Decisions { get; init; }

    public DateOnly? LastDecisionDate { get; init; }
}
