using System.Globalization;

namespace Ratio.Application.Abstractions;

public sealed record SampleCase(
    string CaseNumber,
    string CourtCode,
    string? JudgingBody,
    string? CaseClass,
    string CourtLevel,
    DateOnly? DecisionDate,
    string Outcome,
    string Source,
    string? SourceLink,
    string? SourceLinkType);

public sealed record ThemeCases(long ThemeKey, IReadOnlyList<SampleCase> Cases, long Total)
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");

    public string Ordering => "julgamento mais recente primeiro; empate pelo número do processo";

    public int Shown => Cases.Count;

    public string SampleLabel =>
        string.Format(Portuguese, "{0:N0} de {1:N0} {2}", Shown, Total, Total == 1 ? "decisão" : "decisões");
}
