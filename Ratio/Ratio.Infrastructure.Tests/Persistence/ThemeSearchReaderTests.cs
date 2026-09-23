using Dapper;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;
using Npgsql;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ratio.Infrastructure.Tests.Persistence;

public class ThemeSearchReaderTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string Config = "INSERT INTO dw.strength_config (methodology_version, reference_year) VALUES ('1.0', 2026);";

    private const string Outcomes =
        "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) VALUES ('Granted', 'Procedente', true), ('Denied', 'Improcedente', true); " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT 219, 'Procedência', outcome_sk, true, 'pretensao_autor' FROM dw.dim_decision_outcome WHERE outcome_code = 'Granted'; " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT 220, 'Improcedência', outcome_sk, true, 'pretensao_autor' FROM dw.dim_decision_outcome WHERE outcome_code = 'Denied';";

    private const string CourtAndClass =
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES ('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'); " +
        "INSERT INTO dw.dim_case_class (class_name, claimant_type) VALUES ('Ação de Cobrança', 'autor_particular');";

    private const string Dates =
        "INSERT INTO dw.dim_date (date_sk, full_date, year, quarter, month, month_name, day) VALUES " +
        "(20240615, '2024-06-15', 2024, 2, 6, 'Junho', 15);";

    private readonly PostgresFixture _postgres;
    private NpgsqlDataSource _dataSource = null!;
    private ThemeSearchReader _reader = null!;

    public ThemeSearchReaderTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        var database = await _postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        _reader = new ThemeSearchReader(_dataSource);
        await ExecuteAsync(Config + Outcomes + CourtAndClass + Dates);
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private async Task RefreshAggregatesAsync()
    {
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.case_current_result");
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.theme_summary");
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.theme_strength");
    }

    private async Task<TopTheme[]> TopThemesAsync(int limit)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return (await connection.QueryAsync<TopTheme>(
            "SELECT theme_key AS ThemeKey, theme_name AS ThemeName, rank AS Rank, position AS Position FROM dw.top_themes(@limit)",
            new { limit })).ToArray();
    }

    private async Task InsertThemeAsync(long themeKey, string themeName)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        var subjectCode = themeKey * 100;
        await connection.ExecuteAsync(
            "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES (@themeName, @themeKey)", new { themeName, themeKey });
        await connection.ExecuteAsync(
            "INSERT INTO dw.dim_subject (subject_name, subject_code) VALUES (@subjectName, @subjectCode)",
            new { subjectName = themeName + " assunto", subjectCode });
        await connection.ExecuteAsync(
            "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) " +
            "SELECT t.theme_sk, s.subject_sk FROM dw.dim_theme t, dw.dim_subject s WHERE t.theme_key = @themeKey AND s.subject_code = @subjectCode",
            new { themeKey, subjectCode });
    }

    private async Task InsertJudgedCasesAsync(long themeKey, int upheldCount, int rejectedCount, int dateSk)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        var subjectCode = themeKey * 100;
        var casePrefix = $"CASE-{themeKey}-";
        var casePrefixPattern = casePrefix + "%";
        await connection.ExecuteAsync(
            "INSERT INTO dw.dim_case (case_number, court_sk, case_class_sk, court_level, source, extracted_at) " +
            "SELECT @casePrefix || lpad(n::text, 6, '0'), c.court_sk, cc.case_class_sk, 'First', 'datajud', now() " +
            "FROM generate_series(1, @caseCount) n CROSS JOIN dw.dim_court c CROSS JOIN dw.dim_case_class cc",
            new { casePrefix, caseCount = upheldCount + rejectedCount });
        await connection.ExecuteAsync(
            "INSERT INTO dw.bridge_case_subject (case_sk, subject_sk) " +
            "SELECT dc.case_sk, s.subject_sk FROM dw.dim_case dc CROSS JOIN dw.dim_subject s " +
            "WHERE dc.case_number LIKE @casePrefixPattern AND s.subject_code = @subjectCode",
            new { casePrefixPattern, subjectCode });
        await connection.ExecuteAsync(
            "INSERT INTO dw.fact_case_event (case_sk, court_sk, movement_sk, date_sk, occurred_at, source_url, extracted_at, natural_key) " +
            "SELECT dc.case_sk, dc.court_sk, m.movement_sk, @dateSk, TIMESTAMPTZ '2024-06-15', 'https://example.org', now(), 'evt:' || dc.case_sk " +
            "FROM dw.dim_case dc JOIN dw.dim_movement m ON m.movement_code = CASE WHEN right(dc.case_number, 6)::int <= @upheldCount THEN 219 ELSE 220 END " +
            "WHERE dc.case_number LIKE @casePrefixPattern",
            new { dateSk, upheldCount, casePrefixPattern });
    }

    private async Task InsertUnjudgedCaseAsync(long themeKey)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        var subjectCode = themeKey * 100;
        var caseNumber = $"CASE-{themeKey}-unjudged";
        await connection.ExecuteAsync(
            "INSERT INTO dw.dim_case (case_number, court_sk, court_level, source, extracted_at) " +
            "SELECT @caseNumber, court_sk, 'First', 'datajud', now() FROM dw.dim_court LIMIT 1",
            new { caseNumber });
        await connection.ExecuteAsync(
            "INSERT INTO dw.bridge_case_subject (case_sk, subject_sk) " +
            "SELECT dc.case_sk, s.subject_sk FROM dw.dim_case dc CROSS JOIN dw.dim_subject s " +
            "WHERE dc.case_number = @caseNumber AND s.subject_code = @subjectCode",
            new { caseNumber, subjectCode });
    }

    [Fact]
    public async Task Joins_the_strength_fields_by_theme_key()
    {
        await InsertThemeAsync(1, "Cobrança de dívida");
        await InsertJudgedCasesAsync(1, upheldCount: 142, rejectedCount: 2, dateSk: 20240615);
        await RefreshAggregatesAsync();

        var results = await _reader.SearchThemesAsync("cobranca de divida", 20, CancellationToken.None);

        var theme = Assert.Single(results);
        Assert.Equal(1, theme.ThemeKey);
        Assert.Equal("Cobrança de dívida", theme.Name);
        Assert.Equal(144, theme.JudgedCount);
        Assert.Equal(80, theme.StrengthScore);
        Assert.Equal("Dominante", theme.Level);
        Assert.Equal(142, theme.Outcome.Upheld);
        Assert.Equal(2, theme.Outcome.Rejected);
        Assert.Equal(0.9861m, theme.Outcome.UpheldRatio);
        Assert.Equal("acolhimento da pretensão do autor", theme.Outcome.PolarityLabel);
        Assert.Equal(new DateOnly(2024, 6, 15), theme.LastDecisionDate);
    }

    [Fact]
    public async Task Returns_null_upheld_ratio_below_the_judged_floor()
    {
        await InsertThemeAsync(2, "Cobrança indevida de tarifa");
        await InsertJudgedCasesAsync(2, upheldCount: 1, rejectedCount: 0, dateSk: 20240615);
        await RefreshAggregatesAsync();

        var theme = Assert.Single(await _reader.SearchThemesAsync("cobranca indevida de tarifa", 20, CancellationToken.None));

        Assert.Equal(1, theme.JudgedCount);
        Assert.Equal(1, theme.Outcome.Upheld);
        Assert.Null(theme.Outcome.UpheldRatio);
    }

    [Fact]
    public async Task Leaves_out_a_theme_without_judgments()
    {
        await InsertThemeAsync(3, "Tema sem julgamento algum");
        await InsertUnjudgedCaseAsync(3);
        await RefreshAggregatesAsync();

        Assert.Empty(await _reader.SearchThemesAsync("tema sem julgamento algum", 20, CancellationToken.None));
    }

    [Fact]
    public async Task Orders_by_the_strength_score_instead_of_the_theme_name()
    {
        await InsertThemeAsync(4, "Atraso de entrega de imóvel");
        await InsertThemeAsync(5, "Multa por atraso de voo");
        await InsertJudgedCasesAsync(4, upheldCount: 1, rejectedCount: 0, dateSk: 20240615);
        await InsertJudgedCasesAsync(5, upheldCount: 142, rejectedCount: 2, dateSk: 20240615);
        await RefreshAggregatesAsync();

        var results = await _reader.SearchThemesAsync("atraso", 20, CancellationToken.None);

        Assert.Equal(["Multa por atraso de voo", "Atraso de entrega de imóvel"], results.Select(theme => theme.Name));
        Assert.True(results[0].StrengthScore > results[1].StrengthScore);
    }

    [Fact]
    public async Task Orders_the_top_themes_by_judged_volume()
    {
        await InsertThemeAsync(10, "Multa por atraso de voo");
        await InsertThemeAsync(11, "Cobrança de dívida");
        await InsertThemeAsync(12, "Atraso de entrega de imóvel");
        await InsertJudgedCasesAsync(10, upheldCount: 1, rejectedCount: 0, dateSk: 20240615);
        await InsertJudgedCasesAsync(11, upheldCount: 4, rejectedCount: 1, dateSk: 20240615);
        await InsertJudgedCasesAsync(12, upheldCount: 2, rejectedCount: 1, dateSk: 20240615);
        await RefreshAggregatesAsync();

        var results = await TopThemesAsync(20);

        Assert.Equal(["Cobrança de dívida", "Atraso de entrega de imóvel", "Multa por atraso de voo"], results.Select(theme => theme.ThemeName));
        Assert.Equal([1L, 2L, 3L], results.Select(theme => theme.Position));
        Assert.All(results, theme => Assert.Null(theme.Rank));
    }

    [Fact]
    public async Task Breaks_top_theme_volume_ties_by_theme_name()
    {
        await InsertThemeAsync(13, "Tarifa bancária abusiva");
        await InsertThemeAsync(14, "Seguro prestamista não contratado");
        await InsertJudgedCasesAsync(13, upheldCount: 2, rejectedCount: 0, dateSk: 20240615);
        await InsertJudgedCasesAsync(14, upheldCount: 1, rejectedCount: 1, dateSk: 20240615);
        await RefreshAggregatesAsync();

        var results = await TopThemesAsync(20);

        Assert.Equal(["Seguro prestamista não contratado", "Tarifa bancária abusiva"], results.Select(theme => theme.ThemeName));
    }

    [Fact]
    public async Task Cuts_the_top_themes_at_the_limit()
    {
        await InsertThemeAsync(15, "Multa por atraso de voo");
        await InsertThemeAsync(16, "Cobrança de dívida");
        await InsertThemeAsync(17, "Atraso de entrega de imóvel");
        await InsertJudgedCasesAsync(15, upheldCount: 1, rejectedCount: 0, dateSk: 20240615);
        await InsertJudgedCasesAsync(16, upheldCount: 4, rejectedCount: 1, dateSk: 20240615);
        await InsertJudgedCasesAsync(17, upheldCount: 2, rejectedCount: 1, dateSk: 20240615);
        await RefreshAggregatesAsync();

        var results = await TopThemesAsync(2);

        Assert.Equal(["Cobrança de dívida", "Atraso de entrega de imóvel"], results.Select(theme => theme.ThemeName));
    }

    [Fact]
    public async Task Reads_the_top_themes_with_their_strength_fields()
    {
        await InsertThemeAsync(18, "Multa por atraso de voo");
        await InsertThemeAsync(19, "Cobrança de dívida");
        await InsertJudgedCasesAsync(18, upheldCount: 1, rejectedCount: 0, dateSk: 20240615);
        await InsertJudgedCasesAsync(19, upheldCount: 142, rejectedCount: 2, dateSk: 20240615);
        await RefreshAggregatesAsync();

        var results = await _reader.TopThemesAsync(20, CancellationToken.None);

        Assert.Equal([19L, 18L], results.Select(theme => theme.ThemeKey));
        var theme = results[0];
        Assert.Equal("Cobrança de dívida", theme.Name);
        Assert.Equal(144, theme.JudgedCount);
        Assert.Equal(80, theme.StrengthScore);
        Assert.Equal("Dominante", theme.Level);
        Assert.Equal(142, theme.Outcome.Upheld);
        Assert.Equal(2, theme.Outcome.Rejected);
        Assert.Equal(0.9861m, theme.Outcome.UpheldRatio);
        Assert.Equal(new DateOnly(2024, 6, 15), theme.LastDecisionDate);
    }

    private sealed record TopTheme(long ThemeKey, string ThemeName, float? Rank, long Position);
}
