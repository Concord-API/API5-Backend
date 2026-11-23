using Dapper;
using Npgsql;
using Ratio.Application.Abstractions;

namespace Ratio.Infrastructure.Persistence;

public sealed class RulingReader(NpgsqlDataSource dataSource) : IRulingReader
{
    private const string SourceName = "STJ, Espelhos de acórdãos";
    private const string SourceUrl = "https://dadosabertos.web.stj.jus.br/dataset/espelhos-de-acordaos-primeira-secao";
    private const string ExactMethod = "cnj_exact";
    private const string ThemeSql = "SELECT theme_sk FROM dw.dim_theme WHERE theme_key = @themeKey";
    private const string RulingsSql = """
        SELECT r.source_id AS SourceId, r.case_number AS CaseNumber, r.court_code AS Court,
               r.judging_body AS JudgingBody, r.judged_at AS JudgedAt, r.summary AS Summary,
               r.source_url AS Link, r.extracted_at AS ExtractedAt, btr.link_method AS LinkMethod,
               round(btr.similarity::numeric, 4)::double precision AS Similarity,
               round(btr.similarity_threshold::numeric, 4)::double precision AS SimilarityThreshold
        FROM dw.bridge_theme_ruling btr
        JOIN dw.dim_ruling r ON r.ruling_sk = btr.ruling_sk
        WHERE btr.theme_sk = @themeSk
        ORDER BY r.judged_at DESC NULLS LAST, r.court_code, r.source_id
        LIMIT @limit
        """;
    private const string TotalSql = "SELECT count(*) FROM dw.bridge_theme_ruling WHERE theme_sk = @themeSk";

    public async Task<ThemeRulings?> ListAsync(long themeKey, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var themeSk = await connection.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(ThemeSql, new { themeKey }, cancellationToken: cancellationToken));

        if (themeSk is null)
        {
            return null;
        }

        var rows = (await connection.QueryAsync<RulingRow>(
            new CommandDefinition(RulingsSql, new { themeSk, limit }, cancellationToken: cancellationToken))).ToArray();
        var total = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(TotalSql, new { themeSk }, cancellationToken: cancellationToken));
        DateTimeOffset? extractedAt = rows.Length == 0
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(rows.Max(row => row.ExtractedAt), DateTimeKind.Utc));

        return new ThemeRulings(
            themeKey,
            new RulingSource(SourceName, SourceUrl, extractedAt),
            rows.Select(Map).ToArray(),
            total);
    }

    private static CourtRuling Map(RulingRow row) => new(
        row.SourceId,
        row.CaseNumber,
        row.Court,
        row.JudgingBody,
        row.JudgedAt is null ? null : DateOnly.FromDateTime(row.JudgedAt.Value),
        row.Summary,
        row.Link,
        new RulingTie(
            row.LinkMethod == ExactMethod ? RulingTieMethod.CaseNumber : RulingTieMethod.Similarity,
            row.Similarity,
            row.SimilarityThreshold));

    private sealed record RulingRow(
        string SourceId,
        string? CaseNumber,
        string Court,
        string? JudgingBody,
        DateTime? JudgedAt,
        string Summary,
        string Link,
        DateTime ExtractedAt,
        string LinkMethod,
        double? Similarity,
        double? SimilarityThreshold);
}
