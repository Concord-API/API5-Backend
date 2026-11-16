using System.Globalization;

namespace Ratio.Application.Abstractions;

public sealed record CourtRuling(
    string SourceId,
    string? CaseNumber,
    string Court,
    string? JudgingBody,
    DateOnly? JudgedAt,
    string Summary,
    string Link);

public sealed record RulingSource(string Name, string Url, DateTimeOffset? ExtractedAt);

public sealed record ThemeRulings(
    long ThemeKey,
    RulingSource Source,
    IReadOnlyList<CourtRuling> Rulings,
    long Total)
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");

    public string Ordering { get; } = "julgamento mais recente primeiro; empate pelo tribunal e identificador da fonte";

    public int Shown => Rulings.Count;

    public string SampleLabel =>
        string.Format(Portuguese, "{0:N0} de {1:N0} {2}", Shown, Total, Total == 1 ? "decisão" : "decisões");
}

public interface IRulingReader
{
    Task<ThemeRulings?> ListAsync(long themeKey, int limit, CancellationToken cancellationToken);
}
