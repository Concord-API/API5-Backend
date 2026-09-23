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

    private const string Reference =
        "INSERT INTO dw.strength_config (methodology_version, reference_year, volume_saturation) VALUES ('1.0', 2026, 10); " +
        "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) VALUES ('Granted', 'Procedente', true), ('Denied', 'Improcedente', true); " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT 219, 'Procedência', outcome_sk, true, 'pretensao_autor' FROM dw.dim_decision_outcome WHERE outcome_code = 'Granted'; " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT 220, 'Improcedência', outcome_sk, true, 'pretensao_autor' FROM dw.dim_decision_outcome WHERE outcome_code = 'Denied'; " +
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES ('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'); " +
        "INSERT INTO dw.dim_case_class (class_name, claimant_type) VALUES ('Ação de indenização', 'autor_particular'); " +
        "INSERT INTO dw.dim_date (date_sk, full_date, year, quarter, month, month_name, day) VALUES (20250615, '2025-06-15', 2025, 2, 6, 'Junho', 15);";

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        await ExecuteAsync(Reference);
        await InsertThemesAsync(Themes);
        await InsertJudgedCasesAsync(WrongfulListing, upheldCount: 1, rejectedCount: 1);
        await InsertJudgedCasesAsync(WrongfulNegativeRecord, upheldCount: 10, rejectedCount: 0);
        await InsertJudgedCasesAsync("Contratos Bancários", upheldCount: 1, rejectedCount: 0);
        await InsertJudgedCasesAsync("Guarda compartilhada de filhos", upheldCount: 1, rejectedCount: 0);
        await RefreshAggregatesAsync();
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private Task RefreshAggregatesAsync() => ExecuteAsync(
        "REFRESH MATERIALIZED VIEW dw.case_current_result; " +
        "REFRESH MATERIALIZED VIEW dw.theme_summary; " +
        "REFRESH MATERIALIZED VIEW dw.theme_strength;");

    private async Task InsertThemesAsync(IEnumerable<string> themeNames)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        foreach (var themeName in themeNames)
        {
            await connection.ExecuteAsync(
                "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES (@themeName, (SELECT coalesce(max(theme_key), 0) + 1 FROM dw.dim_theme))",
                new { themeName });
            await connection.ExecuteAsync(
                "INSERT INTO dw.dim_subject (subject_name, subject_code) SELECT @themeName, theme_key * 100 FROM dw.dim_theme WHERE theme_name = @themeName; " +
                "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) " +
                "SELECT t.theme_sk, s.subject_sk FROM dw.dim_theme t JOIN dw.dim_subject s ON s.subject_code = t.theme_key * 100 WHERE t.theme_name = @themeName",
                new { themeName });
        }
    }

    private async Task InsertJudgedCasesAsync(string themeName, int upheldCount, int rejectedCount)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO dw.dim_case (case_number, court_sk, case_class_sk, court_level, source, extracted_at) " +
            "SELECT 'CASE-' || t.theme_key || '-' || lpad(n::text, 6, '0'), c.court_sk, cc.case_class_sk, 'First', 'datajud', now() " +
            "FROM dw.dim_theme t CROSS JOIN generate_series(1, @caseCount) n CROSS JOIN dw.dim_court c CROSS JOIN dw.dim_case_class cc " +
            "WHERE t.theme_name = @themeName; " +
            "INSERT INTO dw.bridge_case_subject (case_sk, subject_sk) " +
            "SELECT dc.case_sk, s.subject_sk FROM dw.dim_theme t JOIN dw.dim_subject s ON s.subject_code = t.theme_key * 100 " +
            "JOIN dw.dim_case dc ON dc.case_number LIKE 'CASE-' || t.theme_key || '-%' WHERE t.theme_name = @themeName; " +
            "INSERT INTO dw.fact_case_event (case_sk, court_sk, movement_sk, date_sk, occurred_at, source_url, extracted_at, natural_key) " +
            "SELECT dc.case_sk, dc.court_sk, m.movement_sk, 20250615, TIMESTAMPTZ '2025-06-15', 'https://example.org', now(), 'evt:' || dc.case_sk " +
            "FROM dw.dim_theme t JOIN dw.dim_case dc ON dc.case_number LIKE 'CASE-' || t.theme_key || '-%' " +
            "JOIN dw.dim_movement m ON m.movement_code = CASE WHEN right(dc.case_number, 6)::int <= @upheldCount THEN 219 ELSE 220 END " +
            "WHERE t.theme_name = @themeName",
            new { themeName, upheldCount, caseCount = upheldCount + rejectedCount });
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
        await InsertJudgedCasesAsync("Multa por atraso de voo", upheldCount: 1, rejectedCount: 0);
        await InsertJudgedCasesAsync("Atraso de entrega de imóvel", upheldCount: 1, rejectedCount: 0);
        await RefreshAggregatesAsync();

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
