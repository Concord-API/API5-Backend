using Dapper;
using Npgsql;
using Ratio.Application.Abstractions;

namespace Ratio.Infrastructure.Persistence;

public sealed class ThemeCasesReader(NpgsqlDataSource dataSource) : IThemeCasesReader
{
    private const string ThemeSql = "SELECT EXISTS (SELECT 1 FROM dw.dim_theme WHERE theme_key = @themeKey)";

    private const string CasesSql = """
        SELECT COALESCE(case_number_formatted, case_number) AS CaseNumber, court_code AS CourtCode,
               judging_body AS JudgingBody, case_class AS CaseClass, court_level AS CourtLevel,
               decision_date AS DecisionDate, outcome_label AS Outcome, source AS Source,
               source_link AS SourceLink, source_link_type AS SourceLinkType
        FROM dw.theme_case_sample
        WHERE theme_key = @themeKey
        ORDER BY decision_date DESC NULLS LAST, case_number
        LIMIT @limit
        """;

    public async Task<ThemeCases?> ListCasesAsync(long themeKey, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var exists = await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(ThemeSql, new { themeKey }, cancellationToken: cancellationToken));

        if (!exists)
        {
            return null;
        }

        return new ThemeCases(themeKey, await ReadCasesAsync(connection, themeKey, limit, cancellationToken));
    }

    private static async Task<IReadOnlyList<SampleCase>> ReadCasesAsync(
        NpgsqlConnection connection, long themeKey, int limit, CancellationToken cancellationToken)
    {
        try
        {
            var rows = await connection.QueryAsync<CaseRow>(
                new CommandDefinition(CasesSql, new { themeKey, limit }, cancellationToken: cancellationToken));

            return rows.Select(ToSampleCase).ToArray();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ObjectNotInPrerequisiteState)
        {
            return [];
        }
    }

    private static SampleCase ToSampleCase(CaseRow row) => new(
        row.CaseNumber,
        row.CourtCode,
        row.JudgingBody,
        row.CaseClass,
        row.CourtLevel,
        row.DecisionDate is null ? null : DateOnly.FromDateTime(row.DecisionDate.Value),
        row.Outcome,
        row.Source,
        row.SourceLink,
        row.SourceLinkType);

    private sealed record CaseRow(
        string CaseNumber,
        string CourtCode,
        string? JudgingBody,
        string? CaseClass,
        string CourtLevel,
        DateTime? DecisionDate,
        string Outcome,
        string Source,
        string? SourceLink,
        string? SourceLinkType);
}
