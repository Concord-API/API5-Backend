namespace Ratio.Application.Abstractions;

public sealed record ThemeFilters(
    IReadOnlyList<string>? Courts,
    int? PeriodYears,
    IReadOnlyList<string>? Levels,
    int? MinStrength)
{
    public static ThemeFilters None { get; } = new(null, null, null, null);
}
