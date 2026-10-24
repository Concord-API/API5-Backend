using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using Ratio.Application.Abstractions;

namespace Ratio.Infrastructure.Persistence;

public sealed class CourtLevelReader(NpgsqlDataSource dataSource, ILogger<CourtLevelReader> logger)
    : ICourtLevelReader
{
    private const string Sql = """
        SELECT DISTINCT dc.court_level
        FROM dw.case_current_result ccr
        JOIN dw.dim_case dc ON dc.case_sk = ccr.case_sk
        ORDER BY dc.court_level
        """;

    public async Task<IReadOnlyList<string>> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            var levels = await connection.QueryAsync<string>(
                new CommandDefinition(Sql, cancellationToken: cancellationToken));

            return levels.ToArray();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ObjectNotInPrerequisiteState)
        {
            logger.LogWarning(exception, "As views do DW não foram populadas: a carga ainda não foi aplicada neste banco.");
            return [];
        }
    }
}
