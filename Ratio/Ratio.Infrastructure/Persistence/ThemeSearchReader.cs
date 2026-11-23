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

    private const string AppealPolarityLabel = "acolhimento da pretensão de quem recorreu";

    private static readonly string[] SuperiorCourts = ["STF", "STJ"];

    private static readonly string SearchSql = Sql($"dw.search_themes(@query, @limit, {FilterArguments})", scoped: false);

    private static readonly string ScopedSearchSql = Sql($"dw.search_themes(@query, @limit, {FilterArguments})", scoped: true);

    private static readonly string TopSql = Sql($"dw.top_themes(@limit, {FilterArguments})", scoped: false);

    private static readonly string ScopedTopSql = Sql($"dw.top_themes(@limit, {FilterArguments})", scoped: true);

    private const string UnscopedColumns = """
        NULL::bigint AS ScopedClaimUpheld, NULL::bigint AS ScopedClaimRejected,
               NULL::bigint AS ScopedAppealUpheld, NULL::bigint AS ScopedAppealRejected,
               NULL::text[] AS ScopedCourtCodes, NULL::date AS ScopedLastDecisionDate
        """;

    private const string ScopedColumns = """
        scoped.claim_upheld AS ScopedClaimUpheld, scoped.claim_rejected AS ScopedClaimRejected,
               scoped.appeal_upheld AS ScopedAppealUpheld, scoped.appeal_rejected AS ScopedAppealRejected,
               scoped.codes AS ScopedCourtCodes, scoped.last_decision_date AS ScopedLastDecisionDate
        """;

    private const string ScopedJoin = """
        LEFT JOIN LATERAL (
            SELECT coalesce(sum(tbc.claim_upheld_count), 0)::bigint AS claim_upheld,
                   coalesce(sum(tbc.claim_rejected_count), 0)::bigint AS claim_rejected,
                   coalesce(sum(tbc.appeal_upheld_count), 0)::bigint AS appeal_upheld,
                   coalesce(sum(tbc.appeal_rejected_count), 0)::bigint AS appeal_rejected,
                   array_agg(tbc.court_code ORDER BY tbc.court_code) FILTER (
                       WHERE tbc.claim_upheld_count + tbc.claim_rejected_count
                           + tbc.appeal_upheld_count + tbc.appeal_rejected_count > 0) AS codes,
                   (SELECT max(a.last_decision_date) FROM dw.theme_court_activity a
                    WHERE a.theme_sk = t.theme_sk AND a.court_code = ANY(@scopeCourts::text[])) AS last_decision_date
            FROM dw.theme_by_court tbc
            WHERE tbc.theme_sk = t.theme_sk AND tbc.court_code = ANY(@scopeCourts::text[])
        ) scoped ON true
        """;

    private static string Sql(string source, bool scoped) => $"""
        SELECT s.theme_key AS ThemeKey, s.theme_name AS Name, s.subject_area AS SubjectArea,
               ts.judged AS Judged, ts.upheld AS Upheld, ts.rejected AS Rejected,
               ts.claim_polarity_label AS PolarityLabel, ts.last_decision_date AS LastDecisionDate,
               ts.score AS StrengthScore, ts.level AS Level, cfg.min_judged_for_percentage AS MinJudgedForPercentage,
               cfg.min_judged_for_grade AS MinJudgedForGrade,
               sm.period_start_year AS PeriodStartYear, sm.period_end_year AS PeriodEndYear,
               ts.agreement_basis AS AgreementBasis, courts.codes AS CourtCodes, n.lead::text AS Lead,
               {(scoped ? ScopedColumns : UnscopedColumns)}
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
        {(scoped ? ScopedJoin : "")}
        ORDER BY s.position
        """;

    public Task<IReadOnlyList<ThemeSummary>> SearchThemesAsync(
        string? query, int limit, ThemeFilters filters, CancellationToken cancellationToken) =>
        QueryAsync(
            ScopeCourts(filters) is null ? SearchSql : ScopedSearchSql,
            Parameters(new { query, limit }, filters),
            ScopeCourts(filters),
            cancellationToken);

    public Task<IReadOnlyList<ThemeSummary>> TopThemesAsync(
        int limit, ThemeFilters filters, CancellationToken cancellationToken) =>
        QueryAsync(
            ScopeCourts(filters) is null ? TopSql : ScopedTopSql,
            Parameters(new { limit }, filters),
            ScopeCourts(filters),
            cancellationToken);

    private static string[]? ScopeCourts(ThemeFilters filters)
    {
        var superior = filters.Levels is { Count: > 0 } levels && levels.All(level => level == "Superior");
        IEnumerable<string>? scope = filters.Courts is { Count: > 0 } courts ? courts : null;

        if (superior)
        {
            scope = scope is null ? SuperiorCourts : scope.Intersect(SuperiorCourts);
        }

        return scope?.Distinct().Order(StringComparer.Ordinal).ToArray();
    }

    private static DynamicParameters Parameters(object template, ThemeFilters filters)
    {
        var parameters = new DynamicParameters(template);
        parameters.Add("courts", filters.Courts?.ToArray());
        parameters.Add("periodYears", filters.PeriodYears);
        parameters.Add("levels", filters.Levels?.ToArray());
        parameters.Add("minStrength", filters.MinStrength);
        parameters.Add("scopeCourts", ScopeCourts(filters));
        return parameters;
    }

    private async Task<IReadOnlyList<ThemeSummary>> QueryAsync(
        string sql, object parameters, string[]? scopeCourts, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            var rows = await connection.QueryAsync<ThemeSearchRow>(
                new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

            return rows.Select(row => scopeCourts is null ? Map(row) : MapScoped(row, scopeCourts)).ToArray();
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
        var level = row.MinJudgedForGrade is not null && judged >= row.MinJudgedForGrade ? row.Level : null;

        var lastDecisionDate = row.LastDecisionDate is null ? (DateOnly?)null : DateOnly.FromDateTime(row.LastDecisionDate.Value);

        var courts = row.CourtCodes?.Cast<string>().ToArray() ?? [];

        return new ThemeSummary(
            row.ThemeKey, row.Name, row.SubjectArea, judged, row.StrengthScore, level,
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

    private static ThemeSummary MapScoped(ThemeSearchRow row, string[] scopeCourts)
    {
        var claims = (row.ScopedClaimUpheld ?? 0) + (row.ScopedClaimRejected ?? 0);
        var appeals = (row.ScopedAppealUpheld ?? 0) + (row.ScopedAppealRejected ?? 0);
        var onClaims = claims > 0;
        var upheld = onClaims ? row.ScopedClaimUpheld ?? 0 : appeals > 0 ? row.ScopedAppealUpheld ?? 0 : 0;
        var rejected = onClaims ? row.ScopedClaimRejected ?? 0 : appeals > 0 ? row.ScopedAppealRejected ?? 0 : 0;
        var judged = upheld + rejected;
        var label = onClaims ? row.PolarityLabel : appeals > 0 ? AppealPolarityLabel : null;
        var meetsFloor = row.MinJudgedForPercentage is not null && judged >= row.MinJudgedForPercentage;
        var upheldRatio = onClaims && meetsFloor ? Math.Round((decimal)upheld / judged, 4) : (decimal?)null;
        var themeJudged = row.Judged ?? 0;
        var level = row.MinJudgedForGrade is not null && themeJudged >= row.MinJudgedForGrade ? row.Level : null;
        var lastDecisionDate = row.ScopedLastDecisionDate is null
            ? (DateOnly?)null
            : DateOnly.FromDateTime(row.ScopedLastDecisionDate.Value);
        var courts = row.ScopedCourtCodes?.Cast<string>().ToArray() ?? [];

        return new ThemeSummary(
            row.ThemeKey, row.Name, row.SubjectArea, judged, row.StrengthScore, level,
            new ThemeOutcome(upheld, rejected, upheldRatio, label),
            row.PeriodStartYear, row.PeriodEndYear, lastDecisionDate)
        {
            Courts = courts.Take(MaxVisibleCourts).ToArray(),
            AdditionalCourtCount = Math.Max(0, courts.Length - MaxVisibleCourts),
            NumbersCourts = scopeCourts
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
        short? MinJudgedForGrade,
        short? PeriodStartYear,
        short? PeriodEndYear,
        string? AgreementBasis,
        Array? CourtCodes,
        string? Lead,
        long? ScopedClaimUpheld,
        long? ScopedClaimRejected,
        long? ScopedAppealUpheld,
        long? ScopedAppealRejected,
        Array? ScopedCourtCodes,
        DateTime? ScopedLastDecisionDate);
}
