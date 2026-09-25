using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Application.Abstractions;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Persistence;

public class ThemeOutcomeBreakdownReaderTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string Cases =
        "(VALUES ('MERIT-1', 101, 219), ('MERIT-2', 101, 219), ('MERIT-3', 101, 221), " +
        "('MERIT-4', 101, 220), ('MERIT-5', 101, 220), ('MERIT-6', 101, 220), " +
        "('MIXED-1', 102, 219), ('MIXED-2', 102, 237), ('MIXED-3', 102, 238), ('MIXED-4', 102, 239), " +
        "('UNJUDGED-1', 103, 900)) AS seed(case_number, subject_code, movement_code)";

    private const string Seed =
        "INSERT INTO dw.strength_config (methodology_version, reference_year, min_judged_for_percentage) VALUES ('1.0', 2026, 3); " +
        "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) VALUES " +
        "('Granted', 'Procedente', true), ('PartiallyGranted', 'Parcialmente procedente', true), ('Denied', 'Improcedente', true); " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT m.code, m.name, o.outcome_sk, m.verified, m.polarity FROM (VALUES " +
        "(219, 'Procedência', 'Granted', true, 'pretensao_autor'), " +
        "(220, 'Improcedência', 'Denied', true, 'pretensao_autor'), " +
        "(221, 'Procedência em Parte', 'PartiallyGranted', true, 'pretensao_autor'), " +
        "(237, 'Provimento', 'Granted', true, 'pretensao_recorrente'), " +
        "(238, 'Provimento em Parte', 'PartiallyGranted', true, 'pretensao_recorrente'), " +
        "(239, 'Não-Provimento', 'Denied', true, 'pretensao_recorrente'), " +
        "(900, 'Movimento não conferido', 'Granted', false, NULL)) AS m(code, name, outcome, verified, polarity) " +
        "JOIN dw.dim_decision_outcome o ON o.outcome_code = m.outcome; " +
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES ('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'); " +
        "INSERT INTO dw.dim_case_class (class_name, claimant_type) VALUES ('Ação de Cobrança', 'autor_particular'); " +
        "INSERT INTO dw.dim_date (date_sk, full_date, year, quarter, month, month_name, day) VALUES (20250615, '2025-06-15', 2025, 2, 6, 'Junho', 15); " +
        "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Tema de mérito', 1), ('Tema misto', 2), ('Tema sem julgado', 3); " +
        "INSERT INTO dw.dim_subject (subject_name, subject_code) VALUES ('Assunto de mérito', 101), ('Assunto misto', 102), ('Assunto sem julgado', 103); " +
        "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) " +
        "SELECT t.theme_sk, s.subject_sk FROM dw.dim_theme t JOIN dw.dim_subject s ON s.subject_code = t.theme_key + 100; " +
        "INSERT INTO dw.dim_case (case_number, court_sk, case_class_sk, court_level, source, extracted_at) " +
        "SELECT seed.case_number, c.court_sk, cc.case_class_sk, 'First', 'datajud', now() FROM " + Cases +
        " CROSS JOIN dw.dim_court c CROSS JOIN dw.dim_case_class cc; " +
        "INSERT INTO dw.bridge_case_subject (case_sk, subject_sk) " +
        "SELECT dc.case_sk, s.subject_sk FROM " + Cases +
        " JOIN dw.dim_case dc ON dc.case_number = seed.case_number JOIN dw.dim_subject s ON s.subject_code = seed.subject_code; " +
        "INSERT INTO dw.fact_case_event (case_sk, court_sk, movement_sk, date_sk, occurred_at, source_url, extracted_at, natural_key) " +
        "SELECT dc.case_sk, dc.court_sk, m.movement_sk, 20250615, TIMESTAMPTZ '2025-06-15', 'https://example.org', now(), 'evt:' || dc.case_sk FROM " + Cases +
        " JOIN dw.dim_case dc ON dc.case_number = seed.case_number JOIN dw.dim_movement m ON m.movement_code = seed.movement_code;";

    private NpgsqlDataSource _dataSource = null!;
    private ThemeDetailReader _reader = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        _reader = new ThemeDetailReader(_dataSource, NullLogger<ThemeDetailReader>.Instance);
        await ExecuteAsync(Seed);
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.case_current_result");
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.theme_summary");
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.theme_strength");
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.theme_outcome_distribution");
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<IReadOnlyList<OutcomeBreakdown>> BreakdownOfAsync(long themeKey)
    {
        var detail = await _reader.GetThemeAsync(themeKey, CancellationToken.None);

        Assert.NotNull(detail);
        return detail.OutcomeBreakdown;
    }

    [Fact]
    public async Task Splits_a_family_into_upheld_partially_upheld_and_rejected()
    {
        var family = Assert.Single(await BreakdownOfAsync(1));

        Assert.Equal("acolhimento da pretensão do autor", family.PolarityLabel);
        Assert.Equal(6, family.Judged);
        Assert.Equal(
            [
                new OutcomeCategory("Procedente", 2, 0.3333m),
                new OutcomeCategory("Parcialmente procedente", 1, 0.1667m),
                new OutcomeCategory("Improcedente", 3, 0.5m)
            ],
            family.Categories);
    }

    [Fact]
    public async Task Keeps_merit_and_appeal_as_separate_entries_with_merit_first()
    {
        var breakdown = await BreakdownOfAsync(2);

        Assert.Equal(
            ["acolhimento da pretensão do autor", "acolhimento da pretensão de quem recorreu"],
            breakdown.Select(family => family.PolarityLabel));
        Assert.Equal([1L, 3L], breakdown.Select(family => family.Judged));
        Assert.Equal(
            [
                new OutcomeCategory("Procedente", 1, 0.3333m),
                new OutcomeCategory("Parcialmente procedente", 1, 0.3333m),
                new OutcomeCategory("Improcedente", 1, 0.3333m)
            ],
            breakdown[1].Categories);
    }

    [Fact]
    public async Task Hides_the_ratio_below_the_floor_and_keeps_empty_categories()
    {
        var merit = (await BreakdownOfAsync(2))[0];

        Assert.Equal(
            [
                new OutcomeCategory("Procedente", 1, null),
                new OutcomeCategory("Parcialmente procedente", 0, null),
                new OutcomeCategory("Improcedente", 0, null)
            ],
            merit.Categories);
    }

    [Fact]
    public async Task Returns_no_breakdown_for_a_theme_without_a_verified_judgment()
    {
        Assert.Empty(await BreakdownOfAsync(3));
    }
}
