using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class CourtRulingSchemaTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    [Fact]
    public async Task Keeps_one_ruling_per_source_identifier()
    {
        const string sql = "INSERT INTO dw.dim_ruling (source, source_id, court_code, summary, source_url, extracted_at) " +
                           "VALUES ('stj_rulings', '123', 'STJ', 'Ementa publicada.', 'https://processo.stj.jus.br/123', now())";
        await ExecuteAsync(sql);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(sql));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Fact]
    public async Task Removes_theme_links_with_the_ruling()
    {
        await ExecuteAsync("INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Planos de saúde', 412)");
        await ExecuteAsync("INSERT INTO dw.dim_ruling (source, source_id, court_code, summary, source_url, extracted_at) " +
                           "VALUES ('stj_rulings', '123', 'STJ', 'Ementa publicada.', 'https://processo.stj.jus.br/123', now())");
        await ExecuteAsync("INSERT INTO dw.bridge_theme_ruling (theme_sk, ruling_sk, link_method) " +
                           "SELECT theme_sk, ruling_sk, 'cnj_exact' FROM dw.dim_theme CROSS JOIN dw.dim_ruling");

        await ExecuteAsync("DELETE FROM dw.dim_ruling");

        await using var connection = _dataSource.CreateConnection();
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM dw.bridge_theme_ruling"));
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }
}
