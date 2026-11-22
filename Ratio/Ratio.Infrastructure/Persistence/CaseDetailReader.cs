using Dapper;
using Npgsql;
using Ratio.Application.Abstractions;
using Ratio.Application.Cases;

namespace Ratio.Infrastructure.Persistence;

public sealed class CaseDetailReader(NpgsqlDataSource dataSource) : ICaseDetailReader
{
    private const string CaseSql = """
        SELECT dc.case_sk AS CaseSk, COALESCE(dc.case_number_formatted, dc.case_number) AS CaseNumber,
               c.court_code AS Court, dc.court_level AS CourtLevel, dc.secrecy_level > 0 AS Sealed, d.status AS SiteStatus,
               d.case_class AS CaseClass, d.subjects AS Subjects, d.judging_body AS JudgingBody, d.judge AS Judge,
               d.filed_at AS FiledAt, d.decision_date AS DecisionDate, d.decision_result AS DecisionResult,
               d.situation AS Situation, d.source_url AS SourceUrl, d.extracted_at AS ExtractedAt
        FROM dw.dim_case dc
        JOIN dw.dim_court c ON c.court_sk = dc.court_sk
        LEFT JOIN dw.case_site_detail d ON d.case_sk = dc.case_sk
        WHERE dc.case_number = @digits
        """;

    private const string MovementsSql = """
        SELECT moved_at AS Date, description AS Description
        FROM dw.case_site_movement
        WHERE case_sk = @caseSk
        ORDER BY seq
        """;

    public async Task<CaseDetail?> GetAsync(string caseNumberDigits, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<CaseRow>(
            new CommandDefinition(CaseSql, new { digits = caseNumberDigits }, cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        var sealedCase = row.Sealed || row.SiteStatus == "sealed";
        IReadOnlyList<CaseMovement> movements = [];

        if (!sealedCase && row.SiteStatus == "found")
        {
            movements = (await connection.QueryAsync<MovementRow>(
                    new CommandDefinition(MovementsSql, new { caseSk = row.CaseSk }, cancellationToken: cancellationToken)))
                .Select(movement => new CaseMovement(DateOnly.FromDateTime(movement.Date), movement.Description))
                .ToArray();
        }

        return CaseView.From(new CaseSiteRecord(
            row.CaseNumber,
            row.Court,
            row.CourtLevel,
            sealedCase,
            row.SiteStatus,
            row.SiteStatus == "found" ? Fields(row) : null,
            movements,
            row.SourceUrl,
            row.ExtractedAt is null ? null : new DateTimeOffset(DateTime.SpecifyKind(row.ExtractedAt.Value, DateTimeKind.Utc))));
    }

    private static CaseFields Fields(CaseRow row) => new(
        row.CaseClass,
        row.Subjects ?? [],
        row.JudgingBody,
        row.Judge,
        Date(row.FiledAt),
        Date(row.DecisionDate),
        row.DecisionResult,
        row.Situation);

    private static DateOnly? Date(DateTime? value) => value is null ? null : DateOnly.FromDateTime(value.Value);

    private sealed class CaseRow
    {
        public long CaseSk { get; init; }

        public string CaseNumber { get; init; } = "";

        public string Court { get; init; } = "";

        public string CourtLevel { get; init; } = "";

        public bool Sealed { get; init; }

        public string? SiteStatus { get; init; }

        public string? CaseClass { get; init; }

        public string[]? Subjects { get; init; }

        public string? JudgingBody { get; init; }

        public string? Judge { get; init; }

        public DateTime? FiledAt { get; init; }

        public DateTime? DecisionDate { get; init; }

        public string? DecisionResult { get; init; }

        public string? Situation { get; init; }

        public string? SourceUrl { get; init; }

        public DateTime? ExtractedAt { get; init; }
    }

    private sealed record MovementRow(DateTime Date, string Description);
}
