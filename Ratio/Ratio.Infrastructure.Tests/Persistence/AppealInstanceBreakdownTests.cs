using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Application.Abstractions;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Persistence;

public class AppealInstanceBreakdownTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string Cases =
        "(VALUES " +
        "('MERIT-1', 'TJSP', 219), ('MERIT-2', 'TJSP', 220), " +
        "('TJ-1', 'TJSP', 237), ('TJ-2', 'TJMG', 239), ('TJ-3', 'TJMG', 239), ('TJ-4', 'TJSP', 238), " +
        "('STJ-1', 'STJ', 237), ('STJ-2', 'STJ', 239), ('STJ-3', 'STJ', 239)) AS seed(case_number, court_code, movement_code)";

    private const string Seed =
        "INSERT INTO dw.strength_config (methodology_version, reference_year, min_judged_for_percentage) VALUES ('1.0', 2026, 2); " +
        "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) VALUES " +
        "('Granted', 'Procedente', true), ('PartiallyGranted', 'Parcialmente procedente', true), ('Denied', 'Improcedente', true); " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT m.code, m.name, o.outcome_sk, true, m.polarity FROM (VALUES " +
        "(219, 'Procedência', 'Granted', 'pretensao_autor'), (220, 'Improcedência', 'Denied', 'pretensao_autor'), " +
        "(237, 'Provimento', 'Granted', 'pretensao_recorrente'), (238, 'Provimento em Parte', 'PartiallyGranted', 'pretensao_recorrente'), " +
        "(239, 'Não-Provimento', 'Denied', 'pretensao_recorrente')) AS m(code, name, outcome, polarity) " +
        "JOIN dw.dim_decision_outcome o ON o.outcome_code = m.outcome; " +
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES " +
        "('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'), ('TJMG', 'Tribunal de Justiça de Minas Gerais', 'MG'), " +
        "('STJ', 'Superior Tribunal de Justiça', 'DF'); " +
        "INSERT INTO dw.dim_case_class (class_name, claimant_type) VALUES ('Ação de Cobrança', 'autor_particular'); " +
        "INSERT INTO dw.dim_date (date_sk, full_date, year, quarter, month, month_name, day) VALUES (20250615, '2025-06-15', 2025, 2, 6, 'Junho', 15); " +
        "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Tema com recursos', 1); " +
        "INSERT INTO dw.dim_subject (subject_name, subject_code) VALUES ('Assunto', 101); " +
        "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) SELECT t.theme_sk, s.subject_sk FROM dw.dim_theme t CROSS JOIN dw.dim_subject s; " +
        "INSERT INTO dw.dim_case (case_number, court_sk, case_class_sk, court_level, source, extracted_at) " +
        "SELECT seed.case_number, c.court_sk, cc.case_class_sk, 'Second', 'datajud', now() FROM " + Cases +
        " JOIN dw.dim_court c ON c.court_code = seed.court_code CROSS JOIN dw.dim_case_class cc; " +
        "INSERT INTO dw.bridge_case_subject (case_sk, subject_sk) SELECT dc.case_sk, s.subject_sk FROM dw.dim_case dc CROSS JOIN dw.dim_subject s; " +
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
        foreach (var view in new[] { "case_current_result", "theme_summary", "theme_strength", "theme_outcome_distribution" })
        {
            await ExecuteAsync($"REFRESH MATERIALIZED VIEW dw.{view}");
        }
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<IReadOnlyList<OutcomeBreakdown>> BreakdownAsync()
    {
        var detail = await _reader.GetThemeAsync(1, CancellationToken.None);
        return detail!.OutcomeBreakdown;
    }

    [Fact]
    public async Task Shows_the_appeals_of_the_state_courts_and_of_the_superior_court_apart()
    {
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.theme_appeal_by_instance");

        var breakdown = await BreakdownAsync();

        Assert.Equal([null, "state", "superior"], breakdown.Select(family => family.Instance));
        Assert.Equal([2L, 4L, 3L], breakdown.Select(family => family.Judged));
        Assert.Equal(
            [
                new OutcomeCategory("Procedente", 1, 0.25m),
                new OutcomeCategory("Parcialmente procedente", 1, 0.25m),
                new OutcomeCategory("Improcedente", 2, 0.5m)
            ],
            breakdown[1].Categories);
        Assert.Equal(
            [
                new OutcomeCategory("Procedente", 1, 0.3333m),
                new OutcomeCategory("Parcialmente procedente", 0, 0m),
                new OutcomeCategory("Improcedente", 2, 0.6667m)
            ],
            breakdown[2].Categories);
    }

    [Fact]
    public async Task The_instances_add_up_to_the_appeals_of_the_theme()
    {
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.theme_appeal_by_instance");

        var breakdown = await BreakdownAsync();

        Assert.Equal(7, breakdown.Where(family => family.Instance is not null).Sum(family => family.Judged));
        Assert.All(breakdown.Skip(1), family => Assert.Equal("acolhimento da pretensão de quem recorreu", family.PolarityLabel));
    }

    [Fact]
    public async Task Keeps_the_appeals_together_when_the_instance_view_was_never_refreshed()
    {
        var breakdown = await BreakdownAsync();

        Assert.Equal([null, null], breakdown.Select(family => family.Instance));
        Assert.Equal([2L, 7L], breakdown.Select(family => family.Judged));
    }
}
