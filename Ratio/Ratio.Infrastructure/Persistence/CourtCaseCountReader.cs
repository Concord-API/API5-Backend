using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using Ratio.Application.Abstractions;

namespace Ratio.Infrastructure.Persistence;

public sealed class CourtCaseCountReader(NpgsqlDataSource dataSource, ILogger<CourtCaseCountReader> logger)
    : ICourtCaseCountReader
{
    private const string Sql = """
        SELECT c.court_code AS Code, c.court_name AS Name, count(DISTINCT ccr.case_sk) AS CaseCount
        FROM dw.dim_court c
        LEFT JOIN dw.case_current_result ccr ON ccr.court_sk = c.court_sk
        GROUP BY c.court_code, c.court_name
        ORDER BY CaseCount DESC, c.court_code
        """;

    public async Task<IReadOnlyList<CourtCaseCount>> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            var counts = await connection.QueryAsync<CourtCaseCount>(
                new CommandDefinition(Sql, cancellationToken: cancellationToken));

            return counts.ToArray();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ObjectNotInPrerequisiteState)
        {
            logger.LogWarning(exception, "As views do DW não foram populadas: a carga ainda não foi aplicada neste banco.");
            return [];
        }
    }
}
