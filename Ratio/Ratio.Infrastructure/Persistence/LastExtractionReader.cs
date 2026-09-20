using Dapper;
using Npgsql;
using Ratio.Application.Abstractions;

namespace Ratio.Infrastructure.Persistence;

public sealed class LastExtractionReader(NpgsqlDataSource dataSource) : ILastExtractionReader
{
    private const string LastExtractionSql = "SELECT MAX(extracted_at) FROM dw.fact_case_event";

    public async Task<DateTimeOffset?> GetLastExtractionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            var extractedAt = await connection.ExecuteScalarAsync<DateTime?>(
                new CommandDefinition(LastExtractionSql, cancellationToken: cancellationToken));

            return extractedAt is null
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(extractedAt.Value, DateTimeKind.Utc));
        }
        catch (NpgsqlException)
        {
            return null;
        }
    }
}
