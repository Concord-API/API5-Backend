using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class PortugueseTextSearchTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task<string> VectorAsync(string text)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<string>(
            "SELECT to_tsvector('dw.pt_unaccent', @text)::text", new { text }) ?? "";
    }

    [Fact]
    public async Task Ignores_accents()
    {
        Assert.Equal(await VectorAsync("inscricao indevida"), await VectorAsync("inscrição indevida"));
    }

    [Fact]
    public async Task Reduces_words_to_their_portuguese_stem()
    {
        Assert.Equal("'indenizaca':1", await VectorAsync("indenização"));
    }

    [Fact]
    public async Task Keeps_distinct_words_distinct()
    {
        Assert.NotEqual(await VectorAsync("indenização"), await VectorAsync("indenizado"));
    }

    [Fact]
    public async Task Removes_accents_inside_hyphenated_words()
    {
        Assert.Equal(await VectorAsync("licenca-premio"), await VectorAsync("licença-prêmio"));
    }
}
