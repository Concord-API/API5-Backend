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

    public static string Write(ExportHeader header, IReadOnlyList<SampleCase> cases)
    {
        var csv = new StringBuilder();

        foreach (var line in HeaderLines(header))
        {
            AppendLine(csv, line);
        }

        csv.Append(LineBreak);
        AppendLine(csv, Header);

        foreach (var sampleCase in cases)
        {
            AppendLine(csv, Cells(sampleCase));
        }

        return csv.ToString();
    }

    private static IEnumerable<string?[]> HeaderLines(ExportHeader header)
    {
        if (header.Sources.Count == 0)
        {
            yield return ["Fonte", null];
            yield return ["Data de extração", null];
        }

        foreach (var source in header.Sources)
        {
            yield return ["Fonte", source.Name, source.SourceUrl];
            yield return ["Data de extração", IsoDate(source.ExtractedAt.UtcDateTime)];
        }

        yield return ["Escopo", ScopeText(header.Scope)];
    }

    private static string ScopeText(Scope scope) =>
        scope.Statement is null ? scope.Subject : $"{scope.Subject} · {scope.Statement}";

    private static string IsoDate(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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

    private static string Escape(string? cell)
    {
        if (cell is null)
        {
            return "";
        }

        return cell.IndexOfAny(CharactersToQuote) >= 0 ? $"\"{cell.Replace("\"", "\"\"")}\"" : cell;
    }
}
