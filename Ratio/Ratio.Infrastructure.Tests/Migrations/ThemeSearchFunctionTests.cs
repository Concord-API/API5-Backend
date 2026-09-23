using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class ThemeSearchFunctionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string WrongfulListing = "Inscrição indevida em cadastro de inadimplentes";
    private const string WrongfulNegativeRecord = "Dano moral por negativação indevida";

    private static readonly string[] Themes =
    [
        WrongfulListing,
        WrongfulNegativeRecord,
        "Contratos Bancários",
        "Guarda compartilhada de filhos"
    ];

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        await InsertThemesAsync(Themes);
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task InsertThemesAsync(IEnumerable<string> themeNames)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        foreach (var themeName in themeNames)
        {
            await connection.ExecuteAsync(
                "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES (@themeName, (SELECT coalesce(max(theme_key), 0) + 1 FROM dw.dim_theme))",
                new { themeName });
        }
    }

    private async Task<SearchedTheme[]> SearchAsync(string? query, int? limit = null)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        var sql = limit is null
            ? "SELECT theme_key AS ThemeKey, theme_name AS ThemeName, rank, position FROM dw.search_themes(@query)"
            : "SELECT theme_key AS ThemeKey, theme_name AS ThemeName, rank, position FROM dw.search_themes(@query, @limit)";
        return (await connection.QueryAsync<SearchedTheme>(sql, new { query, limit })).ToArray();
    }

    private async Task<long> ThemeKeyAsync(string themeName)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<long>(
            "SELECT theme_key FROM dw.dim_theme WHERE theme_name = @themeName", new { themeName });
    }

    [Theory]
    [InlineData("inscrição indevida")]
    [InlineData("inscricao indevida")]
    public async Task Finds_the_same_theme_first_with_or_without_accents(string query)
    {
        var results = await SearchAsync(query);

        Assert.Equal(WrongfulListing, results[0].ThemeName);
        Assert.Equal(await ThemeKeyAsync(WrongfulListing), results[0].ThemeKey);
    }

    [Fact]
    public async Task Recovers_the_theme_from_a_misspelled_query_by_similarity()
    {
        var results = await SearchAsync("negativacao indevda");

        Assert.Equal(WrongfulNegativeRecord, results[0].ThemeName);
    }

    [Fact]
    public async Task Returns_nothing_for_an_out_of_scope_query()
    {
        Assert.Empty(await SearchAsync("contrato de arrendamento de satélite"));
    }

    [Fact]
    public async Task Returns_only_themes_ranked_at_least_one_half()
    {
        var results = await SearchAsync("inscricao indevida");

        Assert.All(results, result => Assert.True(result.Rank >= 0.5f));
        Assert.DoesNotContain(results, result => result.ThemeName == "Contratos Bancários");
    }

    [Fact]
    public async Task Positions_the_themes_by_rank_descending()
    {
        var results = await SearchAsync("inscricao indevida");

        Assert.Equal([WrongfulListing, WrongfulNegativeRecord], results.Select(result => result.ThemeName));
        Assert.Equal([1L, 2L], results.Select(result => result.Position));
    }

    [Fact]
    public async Task Breaks_rank_ties_by_theme_name()
    {
        await InsertThemesAsync(["Multa por atraso de voo", "Atraso de entrega de imóvel"]);

        var results = await SearchAsync("atraso");

        Assert.Equal(results[0].Rank, results[1].Rank);
        Assert.Equal(["Atraso de entrega de imóvel", "Multa por atraso de voo"], results.Select(result => result.ThemeName));
        Assert.Equal([1L, 2L], results.Select(result => result.Position));
    }

    [Fact]
    public async Task Cuts_the_results_at_the_limit()
    {
        var results = await SearchAsync("inscricao indevida", 1);

        Assert.Equal([WrongfulListing], results.Select(result => result.ThemeName));
    }

    [Fact]
    public async Task Returns_nothing_for_a_null_query()
    {
        Assert.Empty(await SearchAsync(null));
    }

    private sealed record SearchedTheme(long ThemeKey, string ThemeName, float Rank, long Position);
}
