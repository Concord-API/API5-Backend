using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class ThemeFiltersTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string BankFee = "Cobrança de tarifa bancária";
    private const string Condominium = "Cobrança de condomínio atrasado";
    private const string SmallClaims = "Cobrança em juizado especial";
    private const string LifeInsurance = "Cobrança de seguro de vida";

    private static readonly string[] AllThemes = [Condominium, LifeInsurance, BankFee, SmallClaims];

    private const string Reference =
        "INSERT INTO dw.strength_config (methodology_version, reference_year, volume_saturation) VALUES ('1.0', 2026, 10); " +
        "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) VALUES ('Granted', 'Procedente', true); " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT 219, 'Procedência', outcome_sk, true, 'pretensao_autor' FROM dw.dim_decision_outcome WHERE outcome_code = 'Granted'; " +
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES " +
        "('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'), ('TJRJ', 'Tribunal de Justiça do Rio de Janeiro', 'RJ'); " +
        "INSERT INTO dw.dim_case_class (class_name, claimant_type) VALUES ('Ação de cobrança', 'autor_particular'); " +
        "INSERT INTO dw.dim_date (date_sk, full_date, year, quarter, month, month_name, day) VALUES " +
        "(20250615, '2025-06-15', 2025, 2, 6, 'Junho', 15), " +
        "(20230615, '2023-06-15', 2023, 2, 6, 'Junho', 15), " +
        "(20200615, '2020-06-15', 2020, 2, 6, 'Junho', 15);";

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        await ExecuteAsync(Reference);
        await InsertThemesAsync(AllThemes);
        await InsertCaseAsync(BankFee, "BANK-1", "TJSP", "First", 20250615);
        await InsertCaseAsync(BankFee, "BANK-2", "TJSP", "First", 20250615);
        await InsertCaseAsync(BankFee, "BANK-3", "TJSP", "First", 20250615);
        await InsertCaseAsync(Condominium, "COND-1", "TJRJ", "Second", 20200615);
        await InsertCaseAsync(Condominium, "COND-2", "TJRJ", "Second", 20200615);
        await InsertCaseAsync(SmallClaims, "SMALL-1", "TJSP", "SpecialCourt", 20230615);
        await InsertCaseAsync(LifeInsurance, "LIFE-1", "TJRJ", "First", 20200615);
        await InsertCaseAsync(LifeInsurance, "LIFE-2", "TJSP", "Second", 20250615);
        await ExecuteAsync(
            "REFRESH MATERIALIZED VIEW dw.case_current_result; " +
            "REFRESH MATERIALIZED VIEW dw.theme_summary; " +
            "REFRESH MATERIALIZED VIEW dw.theme_strength;");
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private async Task InsertThemesAsync(IEnumerable<string> themeNames)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        foreach (var themeName in themeNames)
        {
            await connection.ExecuteAsync(
                "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES (@themeName, (SELECT coalesce(max(theme_key), 0) + 1 FROM dw.dim_theme)); " +
                "INSERT INTO dw.dim_subject (subject_name, subject_code) SELECT @themeName, theme_key * 100 FROM dw.dim_theme WHERE theme_name = @themeName; " +
                "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) " +
                "SELECT t.theme_sk, s.subject_sk FROM dw.dim_theme t JOIN dw.dim_subject s ON s.subject_code = t.theme_key * 100 WHERE t.theme_name = @themeName",
                new { themeName });
        }
    }

    private async Task InsertCaseAsync(string themeName, string caseNumber, string courtCode, string level, int dateSk)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO dw.dim_case (case_number, court_sk, case_class_sk, court_level, source, extracted_at) " +
            "SELECT @caseNumber, c.court_sk, cc.case_class_sk, @level, 'datajud', now() " +
            "FROM dw.dim_court c CROSS JOIN dw.dim_case_class cc WHERE c.court_code = @courtCode; " +
            "INSERT INTO dw.bridge_case_subject (case_sk, subject_sk) " +
            "SELECT dc.case_sk, s.subject_sk FROM dw.dim_case dc CROSS JOIN dw.dim_theme t " +
            "JOIN dw.dim_subject s ON s.subject_code = t.theme_key * 100 WHERE dc.case_number = @caseNumber AND t.theme_name = @themeName; " +
            "INSERT INTO dw.fact_case_event (case_sk, court_sk, movement_sk, date_sk, occurred_at, source_url, extracted_at, natural_key) " +
            "SELECT dc.case_sk, dc.court_sk, m.movement_sk, d.date_sk, d.full_date::timestamptz, 'https://example.org', now(), 'evt:' || dc.case_sk " +
            "FROM dw.dim_case dc JOIN dw.dim_date d ON d.date_sk = @dateSk JOIN dw.dim_movement m ON m.movement_code = 219 " +
            "WHERE dc.case_number = @caseNumber",
            new { themeName, caseNumber, courtCode, level, dateSk });
    }

    private async Task<string[]> TopThemesAsync(
        int limit = 100, string[]? courts = null, int? periodYears = null, string[]? levels = null, int? minScore = null)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return (await connection.QueryAsync<string>(
            "SELECT theme_name FROM dw.top_themes(@limit, @courts::text[], @periodYears::integer, @levels::text[], @minScore::integer) ORDER BY theme_name",
            new { limit, courts, periodYears, levels, minScore })).ToArray();
    }

    private async Task<string[]> SearchAsync(
        string query, string[]? courts = null, int? periodYears = null, string[]? levels = null, int? minScore = null)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return (await connection.QueryAsync<string>(
            "SELECT theme_name FROM dw.search_themes(@query, 100, @courts::text[], @periodYears::integer, @levels::text[], @minScore::integer) ORDER BY theme_name",
            new { query, courts, periodYears, levels, minScore })).ToArray();
    }

    private async Task<Dictionary<string, int>> StrengthScoresAsync()
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return (await connection.QueryAsync<(string Name, int Score)>(
                "SELECT t.theme_name, s.score FROM dw.theme_strength s JOIN dw.dim_theme t ON t.theme_sk = s.theme_sk"))
            .ToDictionary(row => row.Name, row => row.Score);
    }

    [Fact]
    public async Task Returns_every_judged_theme_without_filters()
    {
        Assert.Equal(AllThemes, await TopThemesAsync());
    }

    [Fact]
    public async Task Keeps_only_themes_with_a_judged_case_in_the_selected_courts()
    {
        Assert.Equal([Condominium, LifeInsurance], await TopThemesAsync(courts: ["TJRJ"]));
    }

    [Fact]
    public async Task Counts_the_last_five_years_back_from_the_reference_year()
    {
        Assert.Equal([LifeInsurance, BankFee, SmallClaims], await TopThemesAsync(periodYears: 5));
    }

    [Fact]
    public async Task Counts_the_last_two_years_back_from_the_reference_year()
    {
        Assert.Equal([LifeInsurance, BankFee], await TopThemesAsync(periodYears: 2));
    }

    [Fact]
    public async Task Selects_the_special_courts_with_the_first_instance()
    {
        Assert.Equal([LifeInsurance, BankFee, SmallClaims], await TopThemesAsync(levels: ["First", "SpecialCourt"]));
    }

    [Fact]
    public async Task Leaves_out_the_themes_below_the_minimum_strength()
    {
        var scores = await StrengthScoresAsync();
        var minScore = scores.Values.Max();
        var expected = scores.Where(score => score.Value >= minScore).Select(score => score.Key).Order().ToArray();

        Assert.True(expected.Length < AllThemes.Length);
        Assert.Equal(expected, await TopThemesAsync(minScore: minScore));
    }

    [Fact]
    public async Task Requires_a_single_case_to_match_every_case_filter()
    {
        Assert.Empty(await TopThemesAsync(courts: ["TJRJ"], periodYears: 2));
        Assert.Equal([LifeInsurance], await TopThemesAsync(courts: ["TJSP"], levels: ["Second"]));
    }

    [Fact]
    public async Task Applies_the_filters_before_the_limit()
    {
        Assert.Equal([BankFee], await TopThemesAsync(limit: 1));
        Assert.Equal([Condominium], await TopThemesAsync(limit: 1, courts: ["TJRJ"]));
    }

    [Fact]
    public async Task Filters_the_searched_themes()
    {
        Assert.Equal(AllThemes, await SearchAsync("cobranca"));
        Assert.Equal([Condominium, LifeInsurance], await SearchAsync("cobranca", courts: ["TJRJ"]));
        Assert.Equal([LifeInsurance, BankFee], await SearchAsync("cobranca", periodYears: 2));
    }
}
