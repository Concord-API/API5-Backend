using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Application.Panels;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Persistence;

public class PanelTotalsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string Events =
        "(VALUES " +
        "('A-1', 'TJSP', '3ª Câmara de Direito Privado', 237, TIMESTAMPTZ '2025-01-10'), " +
        "('A-2', 'TJSP', '3ª Câmara de Direito Privado', 239, TIMESTAMPTZ '2025-01-10'), " +
        "('A-3', 'TJSP', '3ª Câmara de Direito Privado', 237, TIMESTAMPTZ '2025-01-10'), " +
        "('A-4', 'TJSP', '5ª Câmara de Direito Privado', 239, TIMESTAMPTZ '2025-01-10'), " +
        "('A-5', 'TJSP', '5ª Câmara de Direito Privado', 239, TIMESTAMPTZ '2025-01-10'), " +
        "('A-6', 'TJSP', '8ª Câmara de Direito Privado', 237, TIMESTAMPTZ '2025-01-10'), " +
        "('A-7', 'TJSP', 'Gabinete da Presidência', 239, TIMESTAMPTZ '2025-01-10'), " +
        "('A-8', 'TJSP', NULL, 237, TIMESTAMPTZ '2025-01-10'), " +
        "('A-9', 'TJSP', '3ª Câmara de Direito Privado', 237, TIMESTAMPTZ '2024-03-01'), " +
        "('A-9', 'STJ', 'Terceira Turma', 239, TIMESTAMPTZ '2025-05-20'), " +
        "('B-1', 'STJ', 'Terceira Turma', 237, TIMESTAMPTZ '2025-02-01'), " +
        "('B-2', 'STJ', 'Quarta Turma', 239, TIMESTAMPTZ '2025-02-01'), " +
        "('B-3', 'STJ', 'Quarta Turma', 239, TIMESTAMPTZ '2025-02-01'), " +
        "('B-4', 'STJ', 'GABINETE DO MINISTRO MARCO BUZZI', 237, TIMESTAMPTZ '2025-02-01')) " +
        "AS seed(case_number, court_code, body_name, movement_code, occurred_at)";

    private const string WarehouseData =
        "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) VALUES " +
        "('Granted', 'Procedente', true), ('Denied', 'Improcedente', true); " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT m.code, m.name, o.outcome_sk, true, 'pretensao_recorrente' FROM (VALUES " +
        "(237, 'Provimento', 'Granted'), (239, 'Não-Provimento', 'Denied')) AS m(code, name, outcome) " +
        "JOIN dw.dim_decision_outcome o ON o.outcome_code = m.outcome; " +
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES " +
        "('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'), ('STJ', 'Superior Tribunal de Justiça', 'DF'); " +
        "INSERT INTO dw.dim_date (date_sk, full_date, year, quarter, month, month_name, day) " +
        "VALUES (20250615, '2025-06-15', 2025, 2, 6, 'Junho', 15); " +
        "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Tema com câmaras', 412); " +
        "INSERT INTO dw.dim_subject (subject_name, subject_code) VALUES ('Assunto A', 101), ('Assunto B', 102); " +
        "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) " +
        "SELECT t.theme_sk, s.subject_sk FROM dw.dim_theme t CROSS JOIN dw.dim_subject s; " +
        "INSERT INTO dw.dim_judging_body (court_sk, body_name) " +
        "SELECT DISTINCT c.court_sk, seed.body_name FROM " + Events +
        " JOIN dw.dim_court c ON c.court_code = seed.court_code WHERE seed.body_name IS NOT NULL; " +
        "INSERT INTO dw.dim_case (case_number, court_sk, court_level, source, extracted_at) " +
        "SELECT DISTINCT ON (seed.case_number) seed.case_number, c.court_sk, 'Second', 'datajud', now() FROM " + Events +
        " JOIN dw.dim_court c ON c.court_code = seed.court_code ORDER BY seed.case_number, seed.occurred_at; " +
        "INSERT INTO dw.bridge_case_subject (case_sk, subject_sk) " +
        "SELECT dc.case_sk, s.subject_sk FROM dw.dim_case dc CROSS JOIN dw.dim_subject s; " +
        "INSERT INTO dw.fact_case_event " +
        "(case_sk, court_sk, judging_body_sk, movement_sk, date_sk, occurred_at, source_url, extracted_at, natural_key) " +
        "SELECT dc.case_sk, c.court_sk, jb.judging_body_sk, m.movement_sk, 20250615, seed.occurred_at, " +
        "'https://example.org', now(), 'evt:' || seed.case_number || ':' || seed.court_code FROM " + Events +
        " JOIN dw.dim_case dc ON dc.case_number = seed.case_number " +
        "JOIN dw.dim_court c ON c.court_code = seed.court_code " +
        "LEFT JOIN dw.dim_judging_body jb ON jb.court_sk = c.court_sk AND jb.body_name = seed.body_name " +
        "JOIN dw.dim_movement m ON m.movement_code = seed.movement_code;";

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        await ExecuteAsync(WarehouseData);
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.case_current_result");
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.theme_by_judging_body");
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<ThemePanels> PanelsAsync()
    {
        var counts = await new PanelAlignmentReader(_dataSource).GetThemeAsync(412, CancellationToken.None);
        return PanelAlignments.Build(counts!);
    }

    private async Task<Dictionary<string, long>> JudgedAppealsPerCourtAsync()
    {
        await using var connection = _dataSource.CreateConnection();
        var rows = await connection.QueryAsync<(string CourtCode, long Judged)>(
            "SELECT c.court_code, count(*) FROM dw.case_current_result ccr " +
            "JOIN dw.dim_court c ON c.court_sk = ccr.court_sk " +
            "WHERE ccr.polarity_reference = 'pretensao_recorrente' GROUP BY c.court_code");
        return rows.ToDictionary(row => row.CourtCode, row => row.Judged);
    }

    [Fact]
    public async Task The_panels_and_the_rest_add_up_to_the_court_total()
    {
        var panels = await PanelsAsync();

        Assert.NotEmpty(panels.Courts);
        Assert.All(panels.Courts, court =>
            Assert.Equal(court.N, court.Panels.Sum(panel => panel.N) + court.OutsidePanels));
    }

    [Fact]
    public async Task The_court_total_is_the_appeals_judged_in_that_court()
    {
        var panels = await PanelsAsync();
        var judged = await JudgedAppealsPerCourtAsync();

        Assert.Equal(judged, panels.Courts.ToDictionary(court => court.CourtCode, court => court.N));
    }

    [Fact]
    public async Task A_case_counts_once_in_the_court_of_its_last_decision()
    {
        var panels = await PanelsAsync();

        var tjsp = panels.Courts.Single(court => court.CourtCode == "TJSP");
        var stj = panels.Courts.Single(court => court.CourtCode == "STJ");
        Assert.Equal(8, tjsp.N);
        Assert.Equal(5, stj.N);
        Assert.Equal(3, tjsp.Panels.Single(panel => panel.Name == "3ª Câmara de Direito Privado").N);
        Assert.Equal(2, stj.Panels.Single(panel => panel.Name == "Terceira Turma").N);
    }

    [Fact]
    public async Task A_case_in_two_subjects_of_the_theme_counts_once()
    {
        var panels = await PanelsAsync();

        Assert.Equal(13, panels.Courts.Sum(court => court.N));
    }
}
