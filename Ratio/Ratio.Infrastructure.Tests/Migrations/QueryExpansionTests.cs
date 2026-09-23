using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class QueryExpansionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string WrongfulListing = "Inscrição indevida em cadastro de inadimplentes";
    private const string JargonPhrase = "fui negativado no serasa";

    private static readonly string[] Themes =
    [
        WrongfulListing,
        "Dano moral por negativação indevida",
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

    private async Task InsertThemeAsync(string themeName, int upheldCount, int rejectedCount)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES (@themeName, (SELECT coalesce(max(theme_key), 0) + 1 FROM dw.dim_theme)); " +
            "INSERT INTO dw.dim_subject (subject_name, subject_code) SELECT @themeName, theme_key * 100 FROM dw.dim_theme WHERE theme_name = @themeName; " +
            "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) " +
            "SELECT t.theme_sk, s.subject_sk FROM dw.dim_theme t JOIN dw.dim_subject s ON s.subject_code = t.theme_key * 100 WHERE t.theme_name = @themeName; " +
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

    private async Task InsertSynonymsAsync()
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO dw.search_synonym (term, expands_to) VALUES ('negativado', 'inclusao indevida cadastro inadimplentes'), ('serasa', 'cadastro inadimplentes')");
    }

    private async Task InsertThemesAsync()
    {
        await InsertThemeAsync(WrongfulListing, upheldCount: 10, rejectedCount: 0);
        foreach (var themeName in Themes.Where(themeName => themeName != WrongfulListing))
        {
            await InsertThemeAsync(themeName, upheldCount: 1, rejectedCount: 1);
        }

        await RefreshAggregatesAsync();
    }

    private async Task<string> ExpandAsync(string? query)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<string>("SELECT dw.expand_query(@query)", new { query }) ?? "";
    }

    private async Task<string[]> SearchAsync(string query)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return (await connection.QueryAsync<string>(
            "SELECT theme_name FROM dw.search_themes(@query)", new { query })).ToArray();
    }

    [Fact]
    public async Task Replaces_each_word_that_has_a_synonym_keeping_the_word_order()
    {
        await InsertSynonymsAsync();

        Assert.Equal(
            "fui inclusao indevida cadastro inadimplentes no cadastro inadimplentes",
            await ExpandAsync(JargonPhrase));
    }

    [Fact]
    public async Task Finds_the_theme_first_from_a_phrase_with_jargon()
    {
        await InsertThemesAsync();
        await InsertSynonymsAsync();

        var results = await SearchAsync(JargonPhrase);

        Assert.Equal(WrongfulListing, results.FirstOrDefault());
    }

    [Fact]
    public async Task Does_not_find_the_theme_first_from_the_jargon_without_the_synonym()
    {
        await InsertThemesAsync();

        var results = await SearchAsync(JargonPhrase);

        Assert.NotEqual(WrongfulListing, results.FirstOrDefault());
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
