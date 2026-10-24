namespace Ratio.Application.Abstractions;

public sealed record YearlyAlignment(short Year, long N, decimal? Percentage, string PolarityLabel);
