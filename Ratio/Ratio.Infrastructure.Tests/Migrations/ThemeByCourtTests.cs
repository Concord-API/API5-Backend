using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class ThemeByCourtTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private sealed record CourtRow(
        string CourtCode,
        long ClaimUpheldCount,
        long ClaimRejectedCount,
        long AppealUpheldCount,
        long AppealRejectedCount);

    private const string Cases =
        "(VALUES " +
        "('MERIT-GRANTED-TJSP', 101, 219, 'TJSP'), " +
        "('MERIT-PARTIAL-TJSP', 101, 221, 'TJSP'), " +
        "('MERIT-DENIED-TJSP', 101, 220, 'TJSP'), " +
        "('APPEAL-GRANTED-TJSP', 101, 237, 'TJSP'), " +
        "('APPEAL-DENIED-TJSP', 101, 239, 'TJSP'), " +
        "('MERIT-GRANTED-TJRJ', 101, 219, 'TJRJ'), " +
        "('UNVERIFIED-TJRJ', 102, 900, 'TJRJ')) " +
        "AS seed(case_number, subject_code, movement_code, court_code)";

    private const string Seed =
        "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) VALUES " +
        "('Granted', 'Procedente', true), ('PartiallyGranted', 'Parcialmente procedente', true), " +
        "('Denied', 'Improcedente', true); " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT m.code, m.name, o.outcome_sk, m.verified, m.polarity FROM (VALUES " +
        "(219, 'Procedência', 'Granted', true, 'pretensao_autor'), " +
        "(220, 'Improcedência', 'Denied', true, 'pretensao_autor'), " +
        "(221, 'Procedência em Parte', 'PartiallyGranted', true, 'pretensao_autor'), " +
        "(237, 'Provimento', 'Granted', true, 'pretensao_recorrente'), " +
        "(239, 'Não-Provimento', 'Denied', true, 'pretensao_recorrente'), " +
        "(900, 'Movimento não conferido', 'Granted', false, NULL)) AS m(code, name, outcome, verified, polarity) " +
        "JOIN dw.dim_decision_outcome o ON o.outcome_code = m.outcome; " +
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES " +
        "('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'), " +
        "('TJRJ', 'Tribunal de Justiça do Rio de Janeiro', 'RJ'); " +
        "INSERT INTO dw.dim_case_class (class_name, claimant_type) VALUES ('Ação de Cobrança', 'autor_particular'); " +
        "INSERT INTO dw.dim_date (date_sk, full_date, year, quarter, month, month_name, day) " +
        "VALUES (20250615, '2025-06-15', 2025, 2, 6, 'Junho', 15); " +
        "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Tema misto', 1), ('Tema sem julgado', 2); " +
        "INSERT INTO dw.dim_subject (subject_name, subject_code) VALUES ('Assunto misto', 101), ('Assunto sem julgado', 102); " +
        "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) " +
        "SELECT t.theme_sk, s.subject_sk FROM dw.dim_theme t " +
        "JOIN dw.dim_subject s ON s.subject_code = t.theme_key + 100; " +
        "INSERT INTO dw.dim_case (case_number, court_sk, case_class_sk, court_level, source, extracted_at) " +
        "SELECT seed.case_number, c.court_sk, cc.case_class_sk, 'First', 'datajud', now() FROM " + Cases +
        " JOIN dw.dim_court c ON c.court_code = seed.court_code CROSS JOIN dw.dim_case_class cc; " +
        "INSERT INTO dw.bridge_case_subject (case_sk, subject_sk) " +
        "SELECT dc.case_sk, s.subject_sk FROM " + Cases +
        " JOIN dw.dim_case dc ON dc.case_number = seed.case_number " +
        "JOIN dw.dim_subject s ON s.subject_code = seed.subject_code; " +
        "INSERT INTO dw.fact_case_event " +
        "(case_sk, court_sk, movement_sk, date_sk, occurred_at, source_url, extracted_at, natural_key) " +
        "SELECT dc.case_sk, dc.court_sk, m.movement_sk, 20250615, TIMESTAMPTZ '2025-06-15', " +
        "'https://example.org', now(), 'evt:' || dc.case_sk FROM " + Cases +
        " JOIN dw.dim_case dc ON dc.case_number = seed.case_number " +
        "JOIN dw.dim_movement m ON m.movement_code = seed.movement_code;";

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        await ExecuteAsync(Seed);
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.case_current_result");
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.theme_by_court");
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<CourtRow[]> ReadCourtsAsync(long themeKey)
    {
        await using var connection = _dataSource.CreateConnection();
        return (await connection.QueryAsync<CourtRow>(
            "SELECT c.court_code AS CourtCode, c.claim_upheld_count AS ClaimUpheldCount, " +
            "c.claim_rejected_count AS ClaimRejectedCount, c.appeal_upheld_count AS AppealUpheldCount, " +
            "c.appeal_rejected_count AS AppealRejectedCount FROM dw.theme_by_court c " +
            "JOIN dw.dim_theme t ON t.theme_sk = c.theme_sk WHERE t.theme_key = @ThemeKey ORDER BY c.court_code",
            new { ThemeKey = themeKey })).ToArray();
    }

    [Fact]
    public async Task Creates_one_row_per_theme_and_court()
    {
        var rows = await ReadCourtsAsync(1);

        Assert.Equal(["TJRJ", "TJSP"], rows.Select(row => row.CourtCode));
    }

    [Fact]
    public async Task Keeps_merit_and_appeal_counts_separate()
    {
        var rows = await ReadCourtsAsync(1);

        Assert.Equal(
            [
                new CourtRow("TJRJ", 1, 0, 0, 0),
                new CourtRow("TJSP", 2, 1, 1, 1)
            ],
            rows);
    }

    [Fact]
    public async Task Leaves_out_themes_without_a_verified_judgment()
    {
        Assert.Empty(await ReadCourtsAsync(2));
    }
}
