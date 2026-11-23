using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class ThemePrecedentLinkSchemaTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string Link =
        "INSERT INTO dw.bridge_theme_precedent (theme_sk, precedent_sk, link_method, similarity, embedding_model) " +
        "SELECT t.theme_sk, p.precedent_sk, 'embedding_cosine+lexical', 0.72, 'paraphrase-multilingual-MiniLM-L12-v2' " +
        "FROM dw.dim_theme t CROSS JOIN dw.dim_precedent p";

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        await ExecuteAsync("INSERT INTO dw.dim_theme (theme_name, subject_area, theme_key) VALUES ('Reajuste por faixa etária', 'SAUDE', 412)");
        await ExecuteAsync(
            "INSERT INTO dw.dim_precedent (precedent_kind, precedent_number, thesis, status, source_url, extracted_at) " +
            "VALUES ('Tema', 952, 'O reajuste por faixa etária é válido.', 'Trânsito em Julgado', 'https://stj.jus.br', now())");
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> LinksAsync()
    {
        await using var connection = _dataSource.CreateConnection();
        return await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM dw.bridge_theme_precedent");
    }

    [Fact]
    public async Task Keeps_one_link_per_theme_and_precedent()
    {
        await ExecuteAsync(Link);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(Link));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Fact]
    public async Task Rejects_a_similarity_outside_zero_and_one()
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(Link.Replace("0.72", "1.5")));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Fact]
    public async Task Removes_the_links_with_the_precedent()
    {
        await ExecuteAsync(Link);

        await ExecuteAsync("DELETE FROM dw.dim_precedent");

        Assert.Equal(0, await LinksAsync());
    }

    [Fact]
    public async Task Removes_the_links_with_the_theme()
    {
        await ExecuteAsync(Link);

        await ExecuteAsync("DELETE FROM dw.dim_theme");

        Assert.Equal(0, await LinksAsync());
    }
}
