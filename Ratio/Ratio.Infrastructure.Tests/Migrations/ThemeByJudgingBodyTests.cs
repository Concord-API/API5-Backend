using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class ThemeByJudgingBodyTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private sealed record BodyRow(
        string CourtCode,
        string? BodyName,
        bool IsPanel,
        long ClaimUpheldCount,
        long ClaimRejectedCount,
        long AppealUpheldCount,
        long AppealRejectedCount);

    private const string Cases =
        "(VALUES " +
        "('TJSP-1', 101, 237, 'TJSP', '3ª Câmara de Direito Privado'), " +
        "('TJSP-2', 101, 237, 'TJSP', '3ª Câmara de Direito Privado'), " +
        "('TJSP-3', 101, 239, 'TJSP', '3ª Câmara de Direito Privado'), " +
        "('TJSP-4', 101, 239, 'TJSP', 'ÓRGÃO ESPECIAL'), " +
        "('TJSP-5', 101, 219, 'TJSP', '1ª Vara Cível'), " +
        "('TJSP-6', 101, 239, 'TJSP', NULL), " +
        "('TJMG-1', 101, 239, 'TJMG', 'Turma Recursal de Belo Horizonte'), " +
        "('TJMG-2', 101, 237, 'TJMG', 'Gabinete da 1ª Vice-Presidência'), " +
        "('STJ-1', 101, 237, 'STJ', 'Terceira Turma'), " +
        "('STJ-2', 101, 239, 'STJ', 'Segunda Seção'), " +
        "('STJ-3', 101, 239, 'STJ', 'GABINETE DO MINISTRO MARCO BUZZI'), " +
        "('STJ-4', 101, 239, 'STJ', 'PRESIDÊNCIA'), " +
        "('UNVERIFIED', 102, 900, 'TJSP', '3ª Câmara de Direito Privado')) " +
        "AS seed(case_number, subject_code, movement_code, court_code, body_name)";

    private const string Seed =
        "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) VALUES " +
        "('Granted', 'Procedente', true), ('Denied', 'Improcedente', true); " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT m.code, m.name, o.outcome_sk, m.verified, m.polarity FROM (VALUES " +
        "(219, 'Procedência', 'Granted', true, 'pretensao_autor'), " +
        "(237, 'Provimento', 'Granted', true, 'pretensao_recorrente'), " +
        "(239, 'Não-Provimento', 'Denied', true, 'pretensao_recorrente'), " +
        "(900, 'Movimento não conferido', 'Granted', false, NULL)) AS m(code, name, outcome, verified, polarity) " +
        "JOIN dw.dim_decision_outcome o ON o.outcome_code = m.outcome; " +
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES " +
        "('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'), " +
        "('TJMG', 'Tribunal de Justiça de Minas Gerais', 'MG'), " +
        "('STJ', 'Superior Tribunal de Justiça', 'DF'); " +
        "INSERT INTO dw.dim_date (date_sk, full_date, year, quarter, month, month_name, day) " +
        "VALUES (20250615, '2025-06-15', 2025, 2, 6, 'Junho', 15); " +
        "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Tema com câmaras', 1), ('Tema sem julgado', 2); " +
        "INSERT INTO dw.dim_subject (subject_name, subject_code) VALUES ('Assunto com câmaras', 101), ('Assunto sem julgado', 102); " +
        "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) " +
        "SELECT t.theme_sk, s.subject_sk FROM dw.dim_theme t " +
        "JOIN dw.dim_subject s ON s.subject_code = t.theme_key + 100; " +
        "INSERT INTO dw.dim_judging_body (court_sk, body_name) " +
        "SELECT DISTINCT c.court_sk, seed.body_name FROM " + Cases +
        " JOIN dw.dim_court c ON c.court_code = seed.court_code WHERE seed.body_name IS NOT NULL; " +
        "INSERT INTO dw.dim_case (case_number, court_sk, court_level, source, extracted_at) " +
        "SELECT seed.case_number, c.court_sk, 'Second', 'datajud', now() FROM " + Cases +
        " JOIN dw.dim_court c ON c.court_code = seed.court_code; " +
        "INSERT INTO dw.bridge_case_subject (case_sk, subject_sk) " +
        "SELECT dc.case_sk, s.subject_sk FROM " + Cases +
        " JOIN dw.dim_case dc ON dc.case_number = seed.case_number " +
        "JOIN dw.dim_subject s ON s.subject_code = seed.subject_code; " +
        "INSERT INTO dw.fact_case_event " +
        "(case_sk, court_sk, judging_body_sk, movement_sk, date_sk, occurred_at, source_url, extracted_at, natural_key) " +
        "SELECT dc.case_sk, dc.court_sk, jb.judging_body_sk, m.movement_sk, 20250615, TIMESTAMPTZ '2025-06-15', " +
        "'https://example.org', now(), 'evt:' || dc.case_sk FROM " + Cases +
        " JOIN dw.dim_case dc ON dc.case_number = seed.case_number " +
        "LEFT JOIN dw.dim_judging_body jb ON jb.court_sk = dc.court_sk AND jb.body_name = seed.body_name " +
        "JOIN dw.dim_movement m ON m.movement_code = seed.movement_code;";

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        await ExecuteAsync(Seed);
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.case_current_result");
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.theme_by_judging_body");
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<BodyRow[]> ReadBodiesAsync(long themeKey)
    {
        await using var connection = _dataSource.CreateConnection();
        return (await connection.QueryAsync<BodyRow>(
            "SELECT b.court_code AS CourtCode, b.body_name AS BodyName, b.is_panel AS IsPanel, " +
            "b.claim_upheld_count AS ClaimUpheldCount, b.claim_rejected_count AS ClaimRejectedCount, " +
            "b.appeal_upheld_count AS AppealUpheldCount, b.appeal_rejected_count AS AppealRejectedCount " +
            "FROM dw.theme_by_judging_body b JOIN dw.dim_theme t ON t.theme_sk = b.theme_sk " +
            "WHERE t.theme_key = @ThemeKey ORDER BY b.court_code, b.body_name NULLS LAST",
            new { ThemeKey = themeKey })).ToArray();
    }

    [Fact]
    public async Task Creates_one_row_per_theme_court_and_judging_body()
    {
        var rows = await ReadBodiesAsync(1);

        Assert.Equal(
            [
                new BodyRow("STJ", "GABINETE DO MINISTRO MARCO BUZZI", false, 0, 0, 0, 1),
                new BodyRow("STJ", "PRESIDÊNCIA", false, 0, 0, 0, 1),
                new BodyRow("STJ", "Segunda Seção", true, 0, 0, 0, 1),
                new BodyRow("STJ", "Terceira Turma", true, 0, 0, 1, 0),
                new BodyRow("TJMG", "Gabinete da 1ª Vice-Presidência", false, 0, 0, 1, 0),
                new BodyRow("TJMG", "Turma Recursal de Belo Horizonte", false, 0, 0, 0, 1),
                new BodyRow("TJSP", "1ª Vara Cível", false, 1, 0, 0, 0),
                new BodyRow("TJSP", "3ª Câmara de Direito Privado", true, 0, 0, 2, 1),
                new BodyRow("TJSP", "ÓRGÃO ESPECIAL", true, 0, 0, 0, 1),
                new BodyRow("TJSP", null, false, 0, 0, 0, 1)
            ],
            rows);
    }

    [Fact]
    public async Task Adds_up_to_the_judged_cases_of_each_court()
    {
        await using var connection = _dataSource.CreateConnection();
        var mismatches = await connection.QueryAsync<string>(
            "WITH bodies AS (SELECT theme_sk, court_code, " +
            "sum(claim_upheld_count + claim_rejected_count + appeal_upheld_count + appeal_rejected_count) AS judged " +
            "FROM dw.theme_by_judging_body GROUP BY theme_sk, court_code), " +
            "courts AS (SELECT bts.theme_sk, c.court_code, count(DISTINCT ccr.case_sk) AS judged " +
            "FROM dw.bridge_theme_subject bts JOIN dw.bridge_case_subject bcs ON bcs.subject_sk = bts.subject_sk " +
            "JOIN dw.case_current_result ccr ON ccr.case_sk = bcs.case_sk " +
            "JOIN dw.dim_court c ON c.court_sk = ccr.court_sk GROUP BY bts.theme_sk, c.court_code) " +
            "SELECT coalesce(b.court_code, c.court_code) FROM bodies b FULL JOIN courts c " +
            "ON c.theme_sk = b.theme_sk AND c.court_code = b.court_code WHERE b.judged IS DISTINCT FROM c.judged");

        Assert.Empty(mismatches);
    }

    [Fact]
    public async Task Leaves_out_themes_without_a_verified_judgment()
    {
        Assert.Empty(await ReadBodiesAsync(2));
    }
}
