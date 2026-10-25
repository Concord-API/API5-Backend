using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Application.Abstractions;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Persistence;

public class PanelCabinetTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string Cases =
        "(VALUES " +
        "('C-1', 237, 'Gab. 04 - 26ª Câmara de Direito Privado'), " +
        "('C-2', 239, 'Gab. 04 - 26ª Câmara de Direito Privado'), " +
        "('C-3', 237, 'Gab. 02 - 26ª Câmara de Direito Privado'), " +
        "('C-4', 239, '26ª Câmara de Direito Privado'), " +
        "('C-5', 239, 'Gabinete 03 - 1ª Câmara Reservada de Direito Empresarial'), " +
        "('C-6', 239, 'Gab. da Presidência'), " +
        "('C-7', 239, 'GAB DES FULANO DE TAL')) AS seed(case_number, movement_code, body_name)";

    private const string WarehouseData =
        "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) VALUES " +
        "('Granted', 'Procedente', true), ('Denied', 'Improcedente', true); " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT m.code, m.name, o.outcome_sk, true, 'pretensao_recorrente' FROM (VALUES " +
        "(237, 'Provimento', 'Granted'), (239, 'Não-Provimento', 'Denied')) AS m(code, name, outcome) " +
        "JOIN dw.dim_decision_outcome o ON o.outcome_code = m.outcome; " +
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES ('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'); " +
        "INSERT INTO dw.dim_date (date_sk, full_date, year, quarter, month, month_name, day) " +
        "VALUES (20250615, '2025-06-15', 2025, 2, 6, 'Junho', 15); " +
        "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Tema com gabinetes', 412); " +
        "INSERT INTO dw.dim_subject (subject_name, subject_code) VALUES ('Assunto', 101); " +
        "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) " +
        "SELECT t.theme_sk, s.subject_sk FROM dw.dim_theme t CROSS JOIN dw.dim_subject s; " +
        "INSERT INTO dw.dim_judging_body (court_sk, body_name) " +
        "SELECT DISTINCT c.court_sk, seed.body_name FROM " + Cases + " CROSS JOIN dw.dim_court c; " +
        "INSERT INTO dw.dim_case (case_number, court_sk, court_level, source, extracted_at) " +
        "SELECT seed.case_number, c.court_sk, 'Second', 'datajud', now() FROM " + Cases + " CROSS JOIN dw.dim_court c; " +
        "INSERT INTO dw.bridge_case_subject (case_sk, subject_sk) " +
        "SELECT dc.case_sk, s.subject_sk FROM dw.dim_case dc CROSS JOIN dw.dim_subject s; " +
        "INSERT INTO dw.fact_case_event " +
        "(case_sk, court_sk, judging_body_sk, movement_sk, date_sk, occurred_at, source_url, extracted_at, natural_key) " +
        "SELECT dc.case_sk, dc.court_sk, jb.judging_body_sk, m.movement_sk, 20250615, TIMESTAMPTZ '2025-06-15', " +
        "'https://example.org', now(), 'evt:' || dc.case_sk FROM " + Cases +
        " JOIN dw.dim_case dc ON dc.case_number = seed.case_number " +
        "JOIN dw.dim_judging_body jb ON jb.court_sk = dc.court_sk AND jb.body_name = seed.body_name " +
        "JOIN dw.dim_movement m ON m.movement_code = seed.movement_code; " +
        "REFRESH MATERIALIZED VIEW dw.case_current_result; " +
        "REFRESH MATERIALIZED VIEW dw.theme_by_judging_body;";

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        await using var command = _dataSource.CreateCommand(WarehouseData);
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task<JudgingBodyCount[]> BodiesAsync()
    {
        var counts = await new PanelAlignmentReader(_dataSource).GetThemeAsync(412, CancellationToken.None);
        return counts!.Bodies.OrderBy(body => body.BodyName, StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public async Task Counts_the_cabinets_of_a_panel_in_the_panel()
    {
        var bodies = await BodiesAsync();

        Assert.Contains(new JudgingBodyCount("TJSP", "26ª Câmara de Direito Privado", true, 2, 2), bodies);
        Assert.Contains(new JudgingBodyCount("TJSP", "1ª Câmara Reservada de Direito Empresarial", true, 0, 1), bodies);
    }

    [Fact]
    public async Task Keeps_a_cabinet_without_a_panel_out_of_the_panels()
    {
        var bodies = await BodiesAsync();

        Assert.Contains(new JudgingBodyCount("TJSP", "Gab. da Presidência", false, 0, 1), bodies);
        Assert.Contains(new JudgingBodyCount("TJSP", "GAB DES FULANO DE TAL", false, 0, 1), bodies);
    }

    [Fact]
    public async Task Still_adds_up_to_the_court_total()
    {
        var bodies = await BodiesAsync();

        Assert.Equal(7, bodies.Sum(body => body.Upheld + body.Rejected));
        Assert.Equal(4, bodies.Length);
    }
}
