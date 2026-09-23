using Dapper;
using Npgsql;
using Ratio.Application.Abstractions;

namespace Ratio.Infrastructure.Persistence;

public sealed class ThemeSearchReader(NpgsqlDataSource dataSource) : IThemeSearchReader
{
    private const string Sql = """
        SELECT s.theme_key AS ThemeKey, s.theme_name AS Name, s.subject_area AS SubjectArea,
               ts.judged AS Judged, ts.upheld AS Upheld, ts.rejected AS Rejected,
               ts.claim_polarity_label AS PolarityLabel, ts.last_decision_date AS LastDecisionDate,
               ts.score AS StrengthScore, ts.level AS Level, cfg.min_judged_for_percentage AS MinJudgedForPercentage
        FROM dw.search_themes(@query, @limit) s
        JOIN dw.dim_theme t ON t.theme_key = s.theme_key
        LEFT JOIN dw.theme_strength ts ON ts.theme_sk = t.theme_sk
        LEFT JOIN dw.strength_config cfg ON cfg.id = 1
        ORDER BY s.position
        """;

    public async Task<IReadOnlyList<ThemeSummary>> SearchThemesAsync(
        string? query, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ThemeSearchRow>(
            new CommandDefinition(Sql, new { query, limit }, cancellationToken: cancellationToken));

        return rows.Select(Map).ToArray();
    }

    private static ThemeSummary Map(ThemeSearchRow row)
    {
        var judged = row.Judged ?? 0;
        var upheld = row.Upheld ?? 0;
        var rejected = row.Rejected ?? 0;
        var meetsFloor = row.MinJudgedForPercentage is not null && judged >= row.MinJudgedForPercentage;
        var upheldRatio = meetsFloor ? Math.Round((decimal)upheld / judged, 4) : (decimal?)null;

        var lastDecisionDate = row.LastDecisionDate is null ? (DateOnly?)null : DateOnly.FromDateTime(row.LastDecisionDate.Value);

        return new ThemeSummary(
            row.ThemeKey, row.Name, row.SubjectArea, judged, row.StrengthScore, row.Level,
            new ThemeOutcome(upheld, rejected, upheldRatio, row.PolarityLabel), lastDecisionDate);
    }

    private sealed record ThemeSearchRow(
        long ThemeKey,
        string Name,
        string? SubjectArea,
        long? Judged,
        long? Upheld,
        long? Rejected,
        string? PolarityLabel,
        DateTime? LastDecisionDate,
        int? StrengthScore,
        string? Level,
        short? MinJudgedForPercentage);
}
