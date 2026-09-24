using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using Ratio.Application.Abstractions;

namespace Ratio.Infrastructure.Persistence;

public sealed class ProvenanceReader(NpgsqlDataSource dataSource, ILogger<ProvenanceReader> logger) : IProvenanceReader
{
    private const string GlobalSql = """
        SELECT block AS Block, source AS Source, extracted_at AS ExtractedAt, row_count AS Count,
               (SELECT methodology_version FROM dw.strength_config LIMIT 1) AS MethodologyVersion
        FROM dw.data_provenance
        ORDER BY block, source
        """;

    private const string ThemeSql = """
        SELECT p.block AS Block, p.source AS Source, p.extracted_at AS ExtractedAt, p.row_count AS Count,
               p.methodology_version AS MethodologyVersion
        FROM dw.theme_provenance p
        JOIN dw.dim_theme t ON t.theme_sk = p.theme_sk
        WHERE t.theme_key = @themeKey
        ORDER BY p.block, p.source
        """;

    public Task<LoadedProvenance> GetGlobalAsync(CancellationToken cancellationToken) =>
        ReadAsync(new CommandDefinition(GlobalSql, cancellationToken: cancellationToken), cancellationToken);

    public Task<LoadedProvenance> GetThemeAsync(long themeKey, CancellationToken cancellationToken) =>
        ReadAsync(new CommandDefinition(ThemeSql, new { themeKey }, cancellationToken: cancellationToken), cancellationToken);

    private async Task<LoadedProvenance> ReadAsync(CommandDefinition command, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            var rows = (await connection.QueryAsync<ProvenanceRow>(command)).ToArray();

            return new LoadedProvenance(
                rows.Select(ToSource).ToArray(),
                rows.Select(row => row.MethodologyVersion).FirstOrDefault(version => version is not null));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.ObjectNotInPrerequisiteState)
        {
            logger.LogWarning(exception, "A proveniência do DW não foi populada: a carga ainda não foi aplicada neste banco.");
            return LoadedProvenance.Empty;
        }
    }

    private static LoadedSource ToSource(ProvenanceRow row) =>
        new(
            row.Block,
            row.Source,
            row.ExtractedAt is null ? null : new DateTimeOffset(DateTime.SpecifyKind(row.ExtractedAt.Value, DateTimeKind.Utc)),
            row.Count ?? 0);

    private sealed record ProvenanceRow(
        string? Block,
        string? Source,
        DateTime? ExtractedAt,
        long? Count,
        string? MethodologyVersion);
}
