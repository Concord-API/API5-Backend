using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class ThemeOutcomeDistributionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private sealed record DistributionRow(
        string PolarityReference,
        long JudgedCount,
        long UpheldCount,
        long PartiallyUpheldCount,
        long RejectedCount);

    private const string Cases =
        "(VALUES ('MERIT-1', 101, 219), ('MERIT-2', 101, 221), ('MERIT-3', 101, 220), ('MERIT-4', 101, 900), " +
        "('MIXED-1', 102, 219), ('MIXED-2', 102, 237), ('MIXED-3', 102, 239), ('UNJUDGED-1', 103, 900)) " +
        "AS seed(case_number, subject_code, movement_code)";

    private const string Seed =
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

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        await ExecuteAsync(Seed);
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.case_current_result");
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.theme_summary");
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.theme_outcome_distribution");
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<DistributionRow[]> ReadDistributionAsync(long themeKey)
    {
        await using var connection = _dataSource.CreateConnection();
        return (await connection.QueryAsync<DistributionRow>(
            "SELECT d.polarity_reference AS PolarityReference, d.judged_count AS JudgedCount, d.upheld_count AS UpheldCount, " +
            "d.partially_upheld_count AS PartiallyUpheldCount, d.rejected_count AS RejectedCount " +
            "FROM dw.theme_outcome_distribution d JOIN dw.dim_theme t ON t.theme_sk = d.theme_sk " +
            "WHERE t.theme_key = @ThemeKey ORDER BY d.polarity_reference",
            new { ThemeKey = themeKey })).ToArray();
    }

    [Fact]
    public async Task Counts_partial_apart_from_upheld_within_a_family()
    {
        var rows = await ReadDistributionAsync(1);

        Assert.Equal([new DistributionRow("pretensao_autor", 3, 1, 1, 1)], rows);
    }

    [Fact]
    public async Task Keeps_merit_and_appeal_in_separate_rows()
    {
        var rows = await ReadDistributionAsync(2);

        Assert.Equal(
            [new DistributionRow("pretensao_autor", 1, 1, 0, 0), new DistributionRow("pretensao_recorrente", 2, 1, 0, 1)],
            rows);
    }

    [Fact]
    public async Task Leaves_out_themes_without_a_verified_judgment()
    {
        Assert.Empty(await ReadDistributionAsync(3));
    }
}
