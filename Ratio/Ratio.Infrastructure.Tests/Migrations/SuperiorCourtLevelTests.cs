using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class SuperiorCourtLevelTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        await ExecuteAsync("INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES ('STJ', 'Superior Tribunal de Justiça', 'DF')");
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public Task Accepts_the_superior_court_level() =>
        ExecuteAsync(
            "INSERT INTO dw.dim_case (case_number, court_sk, court_level, source, extracted_at) " +
            "SELECT 'STJ-1', court_sk, 'Superior', 'datajud', now() FROM dw.dim_court WHERE court_code = 'STJ'");

    [Fact]
    public async Task Rejects_an_unknown_court_level()
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
            "INSERT INTO dw.dim_case (case_number, court_sk, court_level, source, extracted_at) " +
            "SELECT 'STJ-2', court_sk, 'Unknown', 'datajud', now() FROM dw.dim_court WHERE court_code = 'STJ'"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }
}
