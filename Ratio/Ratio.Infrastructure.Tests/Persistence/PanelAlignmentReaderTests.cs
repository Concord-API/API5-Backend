using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Application.Abstractions;
using Ratio.Application.Panels;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Persistence;

public class PanelAlignmentReaderTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string Cases =
        "(VALUES " +
        "('P-1', 237, '3ª Câmara de Direito Privado'), " +
        "('P-2', 239, '3ª Câmara de Direito Privado'), " +
        "('P-3', 237, '3ª Câmara de Direito Privado'), " +
        "('P-4', 219, '1ª Vara Cível'), " +
        "('P-5', 239, NULL)) AS seed(case_number, movement_code, body_name)";

    private const string WarehouseData =
        "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) VALUES " +
        "('Granted', 'Procedente', true), ('Denied', 'Improcedente', true); " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT m.code, m.name, o.outcome_sk, true, m.polarity FROM (VALUES " +
        "(219, 'Procedência', 'Granted', 'pretensao_autor'), " +
        "(237, 'Provimento', 'Granted', 'pretensao_recorrente'), " +
        "(239, 'Não-Provimento', 'Denied', 'pretensao_recorrente')) AS m(code, name, outcome, polarity) " +
        "JOIN dw.dim_decision_outcome o ON o.outcome_code = m.outcome; " +
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES ('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'); " +
        "INSERT INTO dw.dim_date (date_sk, full_date, year, quarter, month, month_name, day) " +
        "VALUES (20250615, '2025-06-15', 2025, 2, 6, 'Junho', 15); " +
        "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Tema com câmaras', 412), ('Tema sem julgado', 413); " +
        "INSERT INTO dw.dim_subject (subject_name, subject_code) VALUES ('Assunto com câmaras', 101); " +
        "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) " +
        "SELECT t.theme_sk, s.subject_sk FROM dw.dim_theme t CROSS JOIN dw.dim_subject s WHERE t.theme_key = 412; " +
        "INSERT INTO dw.dim_judging_body (court_sk, body_name) " +
        "SELECT DISTINCT c.court_sk, seed.body_name FROM " + Cases +
        " CROSS JOIN dw.dim_court c WHERE seed.body_name IS NOT NULL; " +
        "INSERT INTO dw.dim_case (case_number, court_sk, court_level, source, extracted_at) " +
        "SELECT seed.case_number, c.court_sk, 'Second', 'datajud', now() FROM " + Cases + " CROSS JOIN dw.dim_court c; " +
        "INSERT INTO dw.bridge_case_subject (case_sk, subject_sk) " +
        "SELECT dc.case_sk, s.subject_sk FROM dw.dim_case dc CROSS JOIN dw.dim_subject s; " +
        "INSERT INTO dw.fact_case_event " +
        "(case_sk, court_sk, judging_body_sk, movement_sk, date_sk, occurred_at, source_url, extracted_at, natural_key) " +
        "SELECT dc.case_sk, dc.court_sk, jb.judging_body_sk, m.movement_sk, 20250615, TIMESTAMPTZ '2025-06-15', " +
        "'https://example.org', now(), 'evt:' || dc.case_sk FROM " + Cases +
        " JOIN dw.dim_case dc ON dc.case_number = seed.case_number " +
        "LEFT JOIN dw.dim_judging_body jb ON jb.court_sk = dc.court_sk AND jb.body_name = seed.body_name " +
        "JOIN dw.dim_movement m ON m.movement_code = seed.movement_code;";

    private NpgsqlDataSource _dataSource = null!;
    private PanelAlignmentReader _reader = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        _reader = new PanelAlignmentReader(_dataSource);
        await ExecuteAsync(WarehouseData);
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private async Task RefreshAsync()
    {
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.case_current_result");
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.theme_by_judging_body");
    }

    [Fact]
    public async Task Reads_the_appeal_counts_of_every_judging_body()
    {
        await RefreshAsync();

        var counts = await _reader.GetThemeAsync(412, CancellationToken.None);

        Assert.NotNull(counts);
        Assert.Equal(412, counts.ThemeKey);
        Assert.Equal(
            [
                new JudgingBodyCount("TJSP", "3ª Câmara de Direito Privado", true, 2, 1),
                new JudgingBodyCount("TJSP", null, false, 0, 1)
            ],
            counts.Bodies);
    }

    [Fact]
    public async Task Reads_the_divergence_rule_from_the_configuration()
    {
        await ExecuteAsync(
            "INSERT INTO dw.strength_config (methodology_version, divergence_points, min_judged_for_divergence) " +
            "VALUES ('1.0', 15, 8)");
        await RefreshAsync();

        var counts = await _reader.GetThemeAsync(412, CancellationToken.None);

        Assert.Equal(new PanelRule(15, 8), counts!.Rule);
    }

    [Fact]
    public async Task Falls_back_to_the_default_rule_without_a_configuration()
    {
        await RefreshAsync();

        var counts = await _reader.GetThemeAsync(412, CancellationToken.None);

        Assert.Equal(new PanelRule(20, 5), counts!.Rule);
    }

    [Fact]
    public async Task Returns_no_bodies_for_a_theme_without_judgments()
    {
        await RefreshAsync();

        var counts = await _reader.GetThemeAsync(413, CancellationToken.None);

        Assert.NotNull(counts);
        Assert.Empty(counts.Bodies);
    }

    [Fact]
    public async Task Returns_null_for_an_unknown_theme()
    {
        await RefreshAsync();

        Assert.Null(await _reader.GetThemeAsync(999, CancellationToken.None));
    }

    [Fact]
    public async Task Returns_no_bodies_when_the_aggregate_was_never_refreshed()
    {
        var counts = await _reader.GetThemeAsync(412, CancellationToken.None);

        Assert.NotNull(counts);
        Assert.Empty(counts.Bodies);
    }
}
