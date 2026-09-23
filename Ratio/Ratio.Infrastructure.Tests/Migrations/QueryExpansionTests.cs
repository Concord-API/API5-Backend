using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class QueryExpansionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task InsertSynonymsAsync()
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO dw.search_synonym (term, expands_to) VALUES ('negativado', 'inclusao indevida cadastro inadimplentes'), ('serasa', 'cadastro inadimplentes')");
    }

    private async Task<string> ExpandAsync(string? query)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<string>("SELECT dw.expand_query(@query)", new { query }) ?? "";
    }

    [Fact]
    public async Task Replaces_each_word_that_has_a_synonym_keeping_the_word_order()
    {
        await InsertSynonymsAsync();

        Assert.Equal(
            "fui inclusao indevida cadastro inadimplentes no cadastro inadimplentes",
            await ExpandAsync("fui negativado no serasa"));
    }

    [Fact]
    public async Task Normalizes_the_word_before_looking_up_the_synonym()
    {
        await InsertSynonymsAsync();

        Assert.Equal("inclusao indevida cadastro inadimplentes", await ExpandAsync("NEGATIVADO"));
    }

    [Fact]
    public async Task Returns_the_normalized_query_when_no_word_has_a_synonym()
    {
        Assert.Equal("inscricao indevida", await ExpandAsync("Inscrição Indevida"));
    }

    [Fact]
    public async Task Returns_an_empty_text_for_a_null_query()
    {
        Assert.Equal("", await ExpandAsync(null));
    }
}
