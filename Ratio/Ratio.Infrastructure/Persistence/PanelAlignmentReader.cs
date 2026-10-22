using Dapper;
using Npgsql;
using Ratio.Application.Abstractions;
using Ratio.Application.Panels;

namespace Ratio.Infrastructure.Persistence;

public sealed class PanelAlignmentReader(NpgsqlDataSource dataSource) : IPanelAlignmentReader
{
    private static readonly PanelRule DefaultRule = new(20, 5);

    private const string ThemeSql = """
        SELECT t.theme_sk AS ThemeSk, cfg.divergence_points AS DivergencePoints,
               cfg.min_judged_for_divergence AS MinJudgedForDivergence
        FROM dw.dim_theme t
        LEFT JOIN dw.strength_config cfg ON cfg.id = 1
        WHERE t.theme_key = @themeKey
        """;

    private const string BodiesSql = """
        SELECT court_code AS CourtCode, body_name AS BodyName, is_panel AS IsPanel,
               appeal_upheld_count AS Upheld, appeal_rejected_count AS Rejected
        FROM dw.theme_by_judging_body
        WHERE theme_sk = @themeSk AND appeal_upheld_count + appeal_rejected_count > 0
        ORDER BY court_code, body_name NULLS LAST
        """;

    public async Task<ThemePanelCounts?> GetThemeAsync(long themeKey, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var theme = await connection.QuerySingleOrDefaultAsync<ThemeRow>(
            new CommandDefinition(ThemeSql, new { themeKey }, cancellationToken: cancellationToken));

        if (theme is null)
        {
            return null;
        }

        var rule = theme.DivergencePoints is { } points && theme.MinJudgedForDivergence is { } minimum
            ? new PanelRule(points, minimum)
            : DefaultRule;

        return new ThemePanelCounts(themeKey, await ReadBodiesAsync(connection, theme.ThemeSk, cancellationToken), rule);
    }

    private static async Task<IReadOnlyList<JudgingBodyCount>> ReadBodiesAsync(
        NpgsqlConnection connection, long themeSk, CancellationToken cancellationToken)
    {
        try
        {
            var bodies = await connection.QueryAsync<JudgingBodyCount>(
                new CommandDefinition(BodiesSql, new { themeSk }, cancellationToken: cancellationToken));

            return bodies.ToArray();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ObjectNotInPrerequisiteState)
        {
            return [];
        }
    }

    private sealed record ThemeRow(long ThemeSk, short? DivergencePoints, short? MinJudgedForDivergence);
}
