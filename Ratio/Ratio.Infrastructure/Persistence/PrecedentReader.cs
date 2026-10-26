using Dapper;
using Npgsql;
using Ratio.Application.Abstractions;
using Ratio.Application.Precedents;

namespace Ratio.Infrastructure.Persistence;

public sealed class PrecedentReader(NpgsqlDataSource dataSource) : IPrecedentReader
{
    private const string SourceName = "STJ, Precedentes qualificados";
    private const string SourceUrl = "https://dadosabertos.web.stj.jus.br/dataset/precedentes-qualificados";

    private const string ThemeSql = "SELECT theme_sk FROM dw.dim_theme WHERE theme_key = @themeKey";

    private const string PrecedentsSql = """
        SELECT DISTINCT p.precedent_kind AS Kind, p.precedent_number AS Number, p.thesis AS Thesis,
               p.question AS Question, p.status AS Status, p.judged_at AS JudgedAt,
               p.judging_body AS JudgingBody, p.source_url AS Link, p.extracted_at AS ExtractedAt
        FROM dw.bridge_theme_subject bts
        JOIN dw.bridge_subject_precedent bsp ON bsp.subject_sk = bts.subject_sk
        JOIN dw.dim_precedent p ON p.precedent_sk = bsp.precedent_sk
        WHERE bts.theme_sk = @themeSk
        ORDER BY p.judged_at DESC NULLS LAST, p.precedent_number DESC
        """;

    public async Task<ThemePrecedents?> GetThemeAsync(long themeKey, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var themeSk = await connection.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(ThemeSql, new { themeKey }, cancellationToken: cancellationToken));

        if (themeSk is null)
        {
            return null;
        }

        var rows = (await connection.QueryAsync<PrecedentRow>(
            new CommandDefinition(PrecedentsSql, new { themeSk }, cancellationToken: cancellationToken))).ToArray();

        DateTimeOffset? extractedAt = rows.Length == 0
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(rows.Max(row => row.ExtractedAt), DateTimeKind.Utc));

        return new ThemePrecedents(
            themeKey,
            new PrecedentSource(SourceName, SourceUrl, extractedAt),
            rows.Select(Map).ToArray());
    }

    private static ThemePrecedent Map(PrecedentRow row) =>
        new(
            row.Kind,
            row.Number,
            row.Thesis,
            row.Question,
            row.Status,
            row.JudgedAt is null ? null : DateOnly.FromDateTime(row.JudgedAt.Value),
            JudgingBodies.Name(row.JudgingBody),
            row.Link);

    private sealed record PrecedentRow(
        string Kind,
        int Number,
        string Thesis,
        string? Question,
        string Status,
        DateTime? JudgedAt,
        string? JudgingBody,
        string Link,
        DateTime ExtractedAt);
}
