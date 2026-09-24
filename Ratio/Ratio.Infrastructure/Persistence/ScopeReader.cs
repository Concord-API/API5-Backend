using Dapper;
using Npgsql;
using Ratio.Application.Abstractions;

namespace Ratio.Infrastructure.Persistence;

public sealed class ScopeReader(NpgsqlDataSource dataSource) : IScopeReader
{
    private const string Sql = """
        SELECT court_code AS Code, court_name AS Name, state_uf AS State
        FROM dw.dim_court
        ORDER BY court_code
        """;

    public async Task<IReadOnlyList<ScopeCourt>> GetCourtsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var courts = await connection.QueryAsync<ScopeCourt>(new CommandDefinition(Sql, cancellationToken: cancellationToken));

        return courts.ToArray();
    }
}
