using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class ThemeSearchColumnsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string GeneratedAlways = "428C9";

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task<long> InsertThemeAsync(string themeName)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<long>(
            "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES (@themeName, 1) RETURNING theme_sk", new { themeName });
    }

    [Fact]
    public async Task Normalizes_the_theme_name_to_lowercase_without_accents()
    {
        var themeSk = await InsertThemeAsync("Inscrição INDEVIDA");
        await using var connection = await _dataSource.OpenConnectionAsync();

        var normalized = await connection.ExecuteScalarAsync<string>(
            "SELECT theme_name_norm FROM dw.dim_theme WHERE theme_sk = @themeSk", new { themeSk });

        Assert.Equal("inscricao indevida", normalized);
    }

    [Fact]
    public async Task Matches_a_query_written_without_accents()
    {
        var themeSk = await InsertThemeAsync("Inscrição indevida em cadastro de inadimplentes");
        await using var connection = await _dataSource.OpenConnectionAsync();

        var matches = await connection.ExecuteScalarAsync<bool>(
            "SELECT search_vector @@ plainto_tsquery('dw.pt_unaccent', 'inscricao indevida') FROM dw.dim_theme WHERE theme_sk = @themeSk",
            new { themeSk });

        Assert.True(matches);
    }

    [Theory]
    [InlineData("theme_name_norm", "'inscricao indevida'")]
    [InlineData("search_vector", "'inscricao indevida'::tsvector")]
    public async Task Rejects_writing_a_generated_column(string column, string value)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            $"INSERT INTO dw.dim_theme (theme_name, theme_key, {column}) VALUES ('Inscrição indevida', 1, {value})"));

        Assert.Equal(GeneratedAlways, exception.SqlState);
    }

    [Theory]
    [InlineData("idx_dim_theme_fts", "CREATE INDEX idx_dim_theme_fts ON dw.dim_theme USING gin (search_vector)")]
    [InlineData("idx_dim_theme_name_trgm", "CREATE INDEX idx_dim_theme_name_trgm ON dw.dim_theme USING gin (theme_name_norm gin_trgm_ops)")]
    public async Task Indexes_the_search_columns(string indexName, string definition)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();

        var indexDefinition = await connection.ExecuteScalarAsync<string>(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = 'dw' AND indexname = @indexName", new { indexName });

        Assert.Equal(definition, indexDefinition);
    }
}
