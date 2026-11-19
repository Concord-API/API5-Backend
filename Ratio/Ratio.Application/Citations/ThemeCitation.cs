using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ratio.Application.Abstractions;

namespace Ratio.Application.Citations;

public static partial class ThemeCitation
{
    private const string CasesBlock = "cases";

    private static readonly TimeSpan BrasiliaOffset = TimeSpan.FromHours(-3);

    private static readonly NumberFormatInfo Brazilian = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberGroupSizes = [3]
    };

    public static string? Compose(ThemeDetail theme, Provenance provenance, Scope scope)
    {
        var sources = provenance.Sources.Where(source => source.Block == CasesBlock).ToArray();

        if (sources.Length == 0)
        {
            return null;
        }

        var parts = new List<string>
        {
            $"{theme.Name} — {Finding(theme)}",
            $"Fonte: {string.Join("; ", sources.Select(Source))}."
        };

        if (!string.IsNullOrWhiteSpace(scope.Statement))
        {
            parts.Add($"Escopo: {scope.Statement}, matéria {scope.Subject}.");
        }

        if (!string.IsNullOrWhiteSpace(provenance.MethodologyVersion))
        {
            parts.Add($"Metodologia {provenance.MethodologyVersion}.");
        }

        return SingleLine(string.Join(" ", parts));
    }

    private static string Finding(ThemeDetail theme)
    {
        if (theme.Summary is null)
        {
            return $"{Count(theme.JudgedCount, "decisão julgada", "decisões julgadas")}.";
        }

        var text = new StringBuilder();

        foreach (var segment in theme.Summary.Lead)
        {
            text.Append(Segment(segment));
        }

        return SingleLine(text.ToString());
    }

    private static string Segment(JsonElement segment)
    {
        if (segment.TryGetProperty("text", out var text))
        {
            return text.GetString() ?? "";
        }

        var unit = segment.GetProperty("unit").GetString();

        if (segment.TryGetProperty("ratio", out var ratio))
        {
            var n = segment.GetProperty("n").GetInt64();
            return n < 1 ? "" : $"{Percent(ratio.GetDecimal())} das {Number(n)} {unit}";
        }

        return $"{Number(segment.GetProperty("count").GetInt64())} {unit}";
    }

    private static string Source(ProvenanceSource source) =>
        $"{source.Name}, {Count(source.Count, "processo", "processos")}, extração de {Date(source.ExtractedAt)}";

    private static string Count(long count, string singular, string plural) =>
        $"{Number(count)} {(count == 1 ? singular : plural)}";

    private static string Number(long value) => value.ToString("#,0", Brazilian);

    private static string Percent(decimal ratio) =>
        $"{Math.Round(ratio * 100, 1, MidpointRounding.AwayFromZero).ToString("0.0", Brazilian)}%";

    private static string Date(DateTimeOffset value) =>
        value.ToOffset(BrasiliaOffset).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private static string SingleLine(string text) => Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
