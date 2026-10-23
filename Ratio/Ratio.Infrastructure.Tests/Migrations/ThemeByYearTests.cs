using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class ThemeByYearTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private sealed record YearRow(short Year, long ClaimUpheldCount, long ClaimRejectedCount);

    private const string Seed =
        "INSERT INTO dw.strength_config (methodology_version, reference_year) VALUES ('1.0', 2026); " +
        "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) VALUES " +
        "('Granted', 'Procedente', true), ('Denied', 'Improcedente', true); " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT m.code, m.name, o.outcome_sk, true, m.polarity FROM (VALUES " +
        "(219, 'Procedência', 'Granted', 'pretensao_autor'), " +
        "(220, 'Improcedência', 'Denied', 'pretensao_autor'), " +
        "(237, 'Provimento', 'Granted', 'pretensao_recorrente'), " +
        "(239, 'Não-Provimento', 'Denied', 'pretensao_recorrente')) AS m(code, name, outcome, polarity) " +
        "JOIN dw.dim_decision_outcome o ON o.outcome_code = m.outcome; " +
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES ('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'); " +
        "INSERT INTO dw.dim_case_class (class_name, claimant_type) VALUES ('Ação de Cobrança', 'autor_particular'); " +
        "INSERT INTO dw.dim_date (date_sk, full_date, year, quarter, month, month_name, day) VALUES " +
        "(20240615, '2024-06-15', 2024, 2, 6, 'Junho', 15), (20250615, '2025-06-15', 2025, 2, 6, 'Junho', 15); " +
        "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Tema anual', 1); " +
        "INSERT INTO dw.dim_subject (subject_name, subject_code) VALUES ('Assunto anual', 101); " +
        "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) SELECT t.theme_sk, s.subject_sk FROM dw.dim_theme t CROSS JOIN dw.dim_subject s; " +
        "INSERT INTO dw.dim_case (case_number, court_sk, case_class_sk, court_level, source, extracted_at) " +
        "SELECT v.case_number, c.court_sk, cc.case_class_sk, 'First', 'datajud', now() FROM (VALUES " +
        "('MERIT-GRANTED-2024'), ('APPEAL-DENIED-2024'), ('MERIT-GRANTED-2025'), ('MERIT-DENIED-2025')) v(case_number) " +
        "CROSS JOIN dw.dim_court c CROSS JOIN dw.dim_case_class cc; " +
        "INSERT INTO dw.bridge_case_subject (case_sk, subject_sk) SELECT dc.case_sk, s.subject_sk FROM dw.dim_case dc CROSS JOIN dw.dim_subject s; " +
        "INSERT INTO dw.fact_case_event (case_sk, court_sk, movement_sk, date_sk, occurred_at, source_url, extracted_at, natural_key) " +
        "SELECT dc.case_sk, dc.court_sk, m.movement_sk, v.date_sk, v.occurred_at, 'https://example.org', now(), 'evt:' || dc.case_sk " +
        "FROM (VALUES " +
        "('MERIT-GRANTED-2024', 219, 20240615, TIMESTAMPTZ '2024-06-15'), " +
        "('APPEAL-DENIED-2024', 239, 20240615, TIMESTAMPTZ '2024-06-15'), " +
        "('MERIT-GRANTED-2025', 219, 20250615, TIMESTAMPTZ '2025-06-15'), " +
        "('MERIT-DENIED-2025', 220, 20250615, TIMESTAMPTZ '2025-06-15')) v(case_number, movement_code, date_sk, occurred_at) " +
        "JOIN dw.dim_case dc ON dc.case_number = v.case_number JOIN dw.dim_movement m ON m.movement_code = v.movement_code;";

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        await ExecuteAsync(Seed);
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.case_current_result");
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.theme_summary");
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.theme_strength");
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.theme_by_year");
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Creates_one_row_per_theme_and_year()
    {
        await using var connection = _dataSource.CreateConnection();
        var rows = (await connection.QueryAsync<YearRow>(
            "SELECT year AS Year, claim_upheld_count AS ClaimUpheldCount, claim_rejected_count AS ClaimRejectedCount " +
            "FROM dw.theme_by_year ORDER BY year")).ToArray();

        Assert.Equal(
            [new YearRow(2024, 1, 0), new YearRow(2025, 1, 1)],
            rows);
    }
}
