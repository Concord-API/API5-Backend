using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using Ratio.Application.Abstractions;

namespace Ratio.Infrastructure.Persistence;

public sealed class LastExtractionReader(NpgsqlDataSource dataSource, ILogger<LastExtractionReader> logger)
    : ILastExtractionReader
{
    private const string LastExtractionSql = "SELECT MAX(extracted_at) FROM dw.fact_case_event";

    /// <summary>Banco de pé, mas sem o schema dw: o dump da carga nunca foi restaurado.</summary>
    private static readonly string[] NotPublishedYet =
    [
        PostgresErrorCodes.UndefinedTable,
        PostgresErrorCodes.InvalidSchemaName
    ];

    public async Task<LastExtraction> GetLastExtractionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            var extractedAt = await connection.ExecuteScalarAsync<DateTime?>(
                new CommandDefinition(LastExtractionSql, cancellationToken: cancellationToken));

            return extractedAt is null
                ? LastExtraction.Empty
                : LastExtraction.At(new DateTimeOffset(DateTime.SpecifyKind(extractedAt.Value, DateTimeKind.Utc)));
        }
        catch (PostgresException exception) when (NotPublishedYet.Contains(exception.SqlState))
        {
            logger.LogWarning(exception, "O schema dw não existe: a carga ainda não foi publicada neste banco.");
            return LastExtraction.Empty;
        }
        catch (NpgsqlException exception)
        {
            // Sem esta linha a causa (credencial errada, banco parado, permissão faltando)
            // some: no servidor do cliente o log em arquivo é o único diagnóstico disponível.
            logger.LogError(exception, "Falha ao ler a última extração do DW.");
            return LastExtraction.Unavailable;
        }
    }
}
