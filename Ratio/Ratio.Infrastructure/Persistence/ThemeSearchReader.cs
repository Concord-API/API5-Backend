using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using Ratio.Application.Abstractions;

namespace Ratio.Infrastructure.Persistence;

public sealed class ThemeSearchReader(NpgsqlDataSource dataSource, ILogger<ThemeSearchReader> logger) : IThemeSearchReader
{
    private const string FilterArguments =
        "@courts::text[], @periodYears::integer, @levels::text[], @minStrength::integer";

    private const int MaxVisibleCourts = 3;

    private static readonly string SearchSql = Sql($"dw.search_themes(@query, @limit, {FilterArguments})");

    private static readonly string TopSql = Sql($"dw.top_themes(@limit, {FilterArguments})");

    private static string Sql(string source) => $"""
        SELECT s.theme_key AS ThemeKey, s.theme_name AS Name, s.subject_area AS SubjectArea,
               ts.judged AS Judged, ts.upheld AS Upheld, ts.rejected AS Rejected,
               ts.claim_polarity_label AS PolarityLabel, ts.last_decision_date AS LastDecisionDate,
               ts.score AS StrengthScore, ts.level AS Level, cfg.min_judged_for_percentage AS MinJudgedForPercentage,
               sm.period_start_year AS PeriodStartYear, sm.period_end_year AS PeriodEndYear,
               ts.agreement_basis AS AgreementBasis, courts.codes AS CourtCodes, n.lead::text AS Lead
        FROM {source} s
        JOIN dw.dim_theme t ON t.theme_key = s.theme_key
        LEFT JOIN dw.theme_strength ts ON ts.theme_sk = t.theme_sk
        LEFT JOIN dw.theme_summary sm ON sm.theme_sk = t.theme_sk
        LEFT JOIN dw.theme_narrative n ON n.theme_sk = t.theme_sk
        LEFT JOIN dw.strength_config cfg ON cfg.id = 1
        LEFT JOIN LATERAL (
            SELECT array_agg(DISTINCT c.court_code ORDER BY c.court_code) AS codes
            FROM dw.bridge_theme_subject bts
            JOIN dw.bridge_case_subject bcs ON bcs.subject_sk = bts.subject_sk
            JOIN dw.case_current_result ccr ON ccr.case_sk = bcs.case_sk
            JOIN dw.dim_court c ON c.court_sk = ccr.court_sk
            WHERE bts.theme_sk = t.theme_sk
        ) courts ON true
        ORDER BY s.position
        """;

    public Task<IReadOnlyList<ThemeSummary>> SearchThemesAsync(
        string? query, int limit, ThemeFilters filters, CancellationToken cancellationToken) =>
        QueryAsync(SearchSql, Parameters(new { query, limit }, filters), cancellationToken);

    public Task<IReadOnlyList<ThemeSummary>> TopThemesAsync(
        int limit, ThemeFilters filters, CancellationToken cancellationToken) =>
        QueryAsync(TopSql, Parameters(new { limit }, filters), cancellationToken);

    private static DynamicParameters Parameters(object template, ThemeFilters filters)
    {
        var parameters = new DynamicParameters(template);
        parameters.Add("courts", filters.Courts?.ToArray());
        parameters.Add("periodYears", filters.PeriodYears);
        parameters.Add("levels", filters.Levels?.ToArray());
        parameters.Add("minStrength", filters.MinStrength);
        return parameters;
    }

    private async Task<IReadOnlyList<ThemeSummary>> QueryAsync(
        string sql, object parameters, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            var rows = await connection.QueryAsync<ThemeSearchRow>(
                new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

            return rows.Select(Map).ToArray();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ObjectNotInPrerequisiteState)
        {
            logger.LogWarning(exception, "As views do DW não foram populadas: a carga ainda não foi aplicada neste banco.");
            return [];
        }
    }

    private static ThemeSummary Map(ThemeSearchRow row)
    {
        var judged = row.Judged ?? 0;
        var upheld = row.Upheld ?? 0;
        var rejected = row.Rejected ?? 0;
        var meetsFloor = row.MinJudgedForPercentage is not null && judged >= row.MinJudgedForPercentage;
        var hasMeritBasis = row.AgreementBasis == "pretensao_autor";
        var upheldRatio = meetsFloor && hasMeritBasis ? Math.Round((decimal)upheld / judged, 4) : (decimal?)null;

        var lastDecisionDate = row.LastDecisionDate is null ? (DateOnly?)null : DateOnly.FromDateTime(row.LastDecisionDate.Value);

        var courts = row.CourtCodes?.Cast<string>().ToArray() ?? [];

        return new ThemeSummary(
            row.ThemeKey, row.Name, row.SubjectArea, judged, row.StrengthScore, row.Level,
            new ThemeOutcome(upheld, rejected, upheldRatio, row.PolarityLabel),
            row.PeriodStartYear, row.PeriodEndYear, lastDecisionDate)
        {
            Courts = courts.Take(MaxVisibleCourts).ToArray(),
            AdditionalCourtCount = Math.Max(0, courts.Length - MaxVisibleCourts),
            Summary = row.Lead is null
                ? null
                : new ThemeResultSummary(JsonSerializer.Deserialize<JsonElement[]>(row.Lead) ?? [])
        };
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
        short? MinJudgedForPercentage,
        short? PeriodStartYear,
        short? PeriodEndYear,
        string? AgreementBasis,
        Array? CourtCodes,
        string? Lead);
}
