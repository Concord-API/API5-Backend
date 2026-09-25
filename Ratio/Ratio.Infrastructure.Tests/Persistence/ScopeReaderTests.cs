using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Application.Abstractions;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Persistence;

public class ScopeReaderTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string Courts =
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES " +
        "('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'), " +
        "('TJRJ', 'Tribunal de Justiça do Rio de Janeiro', 'RJ'), " +
        "('TJMG', 'Tribunal de Justiça de Minas Gerais', 'MG');";

    private NpgsqlDataSource _dataSource = null!;
    private ScopeReader _reader = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        _reader = new ScopeReader(_dataSource);
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Reads_the_courts_from_the_court_dimension_ordered_by_code()
    {
        await ExecuteAsync(Courts);

        var courts = await _reader.GetCourtsAsync(CancellationToken.None);

        Assert.Equal(
            [
                new ScopeCourt("TJMG", "Tribunal de Justiça de Minas Gerais", "MG"),
                new ScopeCourt("TJRJ", "Tribunal de Justiça do Rio de Janeiro", "RJ"),
                new ScopeCourt("TJSP", "Tribunal de Justiça de São Paulo", "SP")
            ],
            courts);
    }

    [Fact]
    public async Task Follows_a_change_in_the_court_dimension()
    {
        await ExecuteAsync(Courts);
        await ExecuteAsync("INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES ('TJPR', 'Tribunal de Justiça do Paraná', 'PR');");

        var courts = await _reader.GetCourtsAsync(CancellationToken.None);

        Assert.Equal(["TJMG", "TJPR", "TJRJ", "TJSP"], courts.Select(court => court.Code));
    }

    [Fact]
    public async Task Returns_no_courts_before_the_load()
    {
        var courts = await _reader.GetCourtsAsync(CancellationToken.None);

        Assert.Empty(courts);
    }
}
