using System.Globalization;
using System.Text;
using Ratio.Application.Abstractions;

namespace Ratio.Application.Cases;

public static class ThemeCasesCsv
{
    private const string LineBreak = "\r\n";

    private static readonly string[] Header =
    [
        "Processo", "Tribunal", "Órgão julgador", "Classe", "Grau", "Data do julgamento",
        "Resultado", "Fonte", "Link", "Tipo do link", "Sigilo"
    ];

    private static readonly char[] CharactersToQuote = [',', '"', '\r', '\n'];

    public static string Write(IReadOnlyList<SampleCase> cases)
    {
        var csv = new StringBuilder();
        AppendLine(csv, Header);

        foreach (var sampleCase in cases)
        {
            AppendLine(csv, Cells(sampleCase));
        }

        return csv.ToString();
    }

    private static string?[] Cells(SampleCase sampleCase) =>
    [
        sampleCase.CaseNumber,
        sampleCase.CourtCode,
        sampleCase.JudgingBody,
        sampleCase.CaseClass,
        sampleCase.CourtLevel,
        sampleCase.DecisionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        sampleCase.Outcome,
        sampleCase.Source,
        sampleCase.SourceLink,
        sampleCase.SourceLinkType,
        sampleCase.Sealed ? "sim" : "não"
    ];

    private static void AppendLine(StringBuilder csv, IEnumerable<string?> cells) =>
        csv.Append(string.Join(',', cells.Select(Escape))).Append(LineBreak);

    private static string Escape(string? cell) =>
        cell is null ? "" :
        cell.IndexOfAny(CharactersToQuote) >= 0 ? $"\"{cell.Replace("\"", "\"\"")}\"" :
        cell;
}
