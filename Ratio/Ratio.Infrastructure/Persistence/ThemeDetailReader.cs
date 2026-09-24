using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using Ratio.Application.Abstractions;

namespace Ratio.Infrastructure.Persistence;

public sealed class ThemeDetailReader(NpgsqlDataSource dataSource, ILogger<ThemeDetailReader> logger) : IThemeDetailReader
{
    private const string Sql = """
        SELECT t.theme_key AS ThemeKey, t.theme_name AS Name, t.subject_area AS SubjectArea,
               ts.score AS StrengthScore, ts.level AS Level, s.case_count AS CaseCount,
               ts.judged AS JudgedCount, s.court_count AS CourtCount,
               s.period_start_year AS PeriodStartYear, s.period_end_year AS PeriodEndYear,
               ts.last_decision_date AS LastDecisionDate, n.lead::text AS Lead, n.body::text AS Body,
               n.text_origin AS TextOrigin, n.methodology_version AS MethodologyVersion,
               n.generated_at AS GeneratedAt
        FROM dw.dim_theme t
        LEFT JOIN dw.theme_summary s ON s.theme_sk = t.theme_sk
        LEFT JOIN dw.theme_strength ts ON ts.theme_sk = t.theme_sk
        LEFT JOIN dw.theme_narrative n ON n.theme_sk = t.theme_sk
        WHERE t.theme_key = @themeKey
        """;

    public async Task<ThemeDetail?> GetThemeAsync(long themeKey, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            var row = await connection.QuerySingleOrDefaultAsync<ThemeDetailRow>(
                new CommandDefinition(Sql, new { themeKey }, cancellationToken: cancellationToken));

            return row is null ? null : Map(row);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ObjectNotInPrerequisiteState)
        {
            logger.LogWarning(exception, "As views do DW não foram populadas: a carga ainda não foi aplicada neste banco.");
            return null;
        }
    }

    private static ThemeDetail Map(ThemeDetailRow row) =>
        new(
            row.ThemeKey,
            row.Name,
            row.SubjectArea,
            row.StrengthScore,
            row.Level,
            row.CaseCount ?? 0,
            row.JudgedCount ?? 0,
            row.CourtCount ?? 0,
            row.PeriodStartYear,
            row.PeriodEndYear,
            ToDateOnly(row.LastDecisionDate),
            MapNarrative(row));

    private static ThemeNarrative? MapNarrative(ThemeDetailRow row) =>
        row.Lead is null
            ? null
            : new ThemeNarrative(
                JsonSerializer.Deserialize<JsonElement[]>(row.Lead) ?? [],
                JsonSerializer.Deserialize<JsonElement[]>(row.Body!) ?? [],
                row.TextOrigin!,
                row.MethodologyVersion!,
                DateOnly.FromDateTime(row.GeneratedAt!.Value));

    private static DateOnly? ToDateOnly(DateTime? value) =>
        value is null ? null : DateOnly.FromDateTime(value.Value);

    private sealed record ThemeDetailRow(
        long ThemeKey,
        string Name,
        string? SubjectArea,
        int? StrengthScore,
        string? Level,
        long? CaseCount,
        long? JudgedCount,
        long? CourtCount,
        short? PeriodStartYear,
        short? PeriodEndYear,
        DateTime? LastDecisionDate,
        string? Lead,
        string? Body,
        string? TextOrigin,
        string? MethodologyVersion,
        DateTime? GeneratedAt);
}
