using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using Ratio.Application.Abstractions;

namespace Ratio.Infrastructure.Persistence;

public sealed class LastExtractionReader(NpgsqlDataSource dataSource, ILogger<LastExtractionReader> logger)
    : ILastExtractionReader
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
        catch (NpgsqlException exception)
        {
            // /health/ready responde 503 de qualquer jeito, mas sem esta linha a causa
            // (credencial errada, schema ausente, banco parado) some: no servidor do
            // cliente o log em arquivo é o único diagnóstico disponível.
            logger.LogError(exception, "Falha ao ler a última extração do DW.");
            return null;
        }
    }
}
