using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class ThemeCaseSampleTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string Reference =
        "INSERT INTO dw.strength_config (methodology_version, reference_year) VALUES ('1.0', 2026); " +
        "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) VALUES " +
        "('Granted', 'Procedente', true), ('Denied', 'Improcedente', true); " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT 219, 'Procedência', outcome_sk, true, 'pretensao_autor' FROM dw.dim_decision_outcome WHERE outcome_code = 'Granted'; " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT 220, 'Improcedência', outcome_sk, true, 'pretensao_autor' FROM dw.dim_decision_outcome WHERE outcome_code = 'Denied'; " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified) " +
        "SELECT 237, 'Provimento', outcome_sk, false FROM dw.dim_decision_outcome WHERE outcome_code = 'Granted'; " +
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES ('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'); " +
        "INSERT INTO dw.dim_judging_body (court_sk, body_name) SELECT court_sk, '5ª Câmara de Direito Privado' FROM dw.dim_court; " +
        "INSERT INTO dw.dim_case_class (class_name, claimant_type) VALUES ('Apelação Cível', 'autor_particular'); " +
        "INSERT INTO dw.dim_date (date_sk, full_date, year, quarter, month, month_name, day) VALUES " +
        "(20250615, '2025-06-15', 2025, 2, 6, 'Junho', 15), " +
        "(20250910, '2025-09-10', 2025, 3, 9, 'Setembro', 10);";

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        await ExecuteAsync(Reference);
        await InsertThemeAsync(10, "Cobrança de tarifa bancária", 1001, 1002);
        await InsertThemeAsync(20, "Dano moral por negativação indevida", 2001);
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private Task RefreshAsync() => ExecuteAsync("REFRESH MATERIALIZED VIEW dw.case_current_result");

    private async Task InsertThemeAsync(long themeKey, string themeName, params int[] subjectCodes)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES (@themeName, @themeKey)", new { themeName, themeKey });
        foreach (var subjectCode in subjectCodes)
        {
            await connection.ExecuteAsync(
                "INSERT INTO dw.dim_subject (subject_name, subject_code) VALUES (@subjectName, @subjectCode); " +
                "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) " +
                "SELECT t.theme_sk, s.subject_sk FROM dw.dim_theme t, dw.dim_subject s WHERE t.theme_key = @themeKey AND s.subject_code = @subjectCode",
                new { subjectName = $"Assunto {subjectCode}", subjectCode, themeKey });
        }
    }

    private async Task InsertCaseAsync(
        string caseNumber,
        int[] subjectCodes,
        (int MovementCode, int DateSk)[] decisions,
        bool withJudgingBody = true,
        bool withClass = true)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO dw.dim_case (case_number, court_sk, case_class_sk, court_level, secrecy_level, source, extracted_at, " +
            "case_number_formatted, source_link, source_link_type) " +
            "SELECT @caseNumber, c.court_sk, CASE WHEN @withClass THEN cc.case_class_sk END, 'Second', 0, 'datajud', now(), " +
            "'formatado-' || @caseNumber, 'https://esaj.tjsp.jus.br/' || @caseNumber, 'portal' " +
            "FROM dw.dim_court c CROSS JOIN dw.dim_case_class cc",
            new { caseNumber, withClass });
        foreach (var subjectCode in subjectCodes)
        {
            await connection.ExecuteAsync(
                "INSERT INTO dw.bridge_case_subject (case_sk, subject_sk) " +
                "SELECT dc.case_sk, s.subject_sk FROM dw.dim_case dc, dw.dim_subject s WHERE dc.case_number = @caseNumber AND s.subject_code = @subjectCode",
                new { caseNumber, subjectCode });
        }

        for (var index = 0; index < decisions.Length; index++)
        {
            await connection.ExecuteAsync(
                "INSERT INTO dw.fact_case_event (case_sk, court_sk, judging_body_sk, movement_sk, date_sk, occurred_at, source_url, extracted_at, natural_key) " +
                "SELECT dc.case_sk, dc.court_sk, CASE WHEN @withJudgingBody THEN jb.judging_body_sk END, m.movement_sk, d.date_sk, " +
                "d.full_date::timestamptz, 'https://example.org', now(), 'evt:' || dc.case_sk || ':' || @index " +
                "FROM dw.dim_case dc JOIN dw.dim_judging_body jb ON jb.court_sk = dc.court_sk " +
                "JOIN dw.dim_movement m ON m.movement_code = @movementCode JOIN dw.dim_date d ON d.date_sk = @dateSk " +
                "WHERE dc.case_number = @caseNumber",
                new { caseNumber, withJudgingBody, movementCode = decisions[index].MovementCode, dateSk = decisions[index].DateSk, index });
        }
    }

    private async Task<SampleRow[]> SampleAsync(long themeKey)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return (await connection.QueryAsync<SampleRow>(
            "SELECT case_number AS CaseNumber, case_number_formatted AS CaseNumberFormatted, court_code AS CourtCode, " +
            "court_name AS CourtName, judging_body AS JudgingBody, case_class AS CaseClass, court_level AS CourtLevel, " +
            "decision_date AS DecisionDate, outcome_code AS OutcomeCode, outcome_label AS OutcomeLabel, " +
            "polarity_reference AS PolarityReference, secrecy_level AS SecrecyLevel, source AS Source, " +
            "source_link AS SourceLink, source_link_type AS SourceLinkType " +
            "FROM dw.theme_case_sample WHERE theme_key = @themeKey ORDER BY case_number",
            new { themeKey })).ToArray();
    }

    [Fact]
    public async Task Returns_one_row_per_judged_case_with_its_audit_columns()
    {
        await InsertCaseAsync("CASE-1", [1001], [(219, 20250615)]);
        await RefreshAsync();

        var row = Assert.Single(await SampleAsync(10));

        Assert.Equal("CASE-1", row.CaseNumber);
        Assert.Equal("formatado-CASE-1", row.CaseNumberFormatted);
        Assert.Equal("TJSP", row.CourtCode);
        Assert.Equal("Tribunal de Justiça de São Paulo", row.CourtName);
        Assert.Equal("5ª Câmara de Direito Privado", row.JudgingBody);
        Assert.Equal("Apelação Cível", row.CaseClass);
        Assert.Equal("Second", row.CourtLevel);
        Assert.Equal(new DateTime(2025, 6, 15), row.DecisionDate);
        Assert.Equal("Granted", row.OutcomeCode);
        Assert.Equal("Procedente", row.OutcomeLabel);
        Assert.Equal("pretensao_autor", row.PolarityReference);
        Assert.Equal(0, row.SecrecyLevel);
        Assert.Equal("datajud", row.Source);
        Assert.Equal("https://esaj.tjsp.jus.br/CASE-1", row.SourceLink);
        Assert.Equal("portal", row.SourceLinkType);
    }

    [Fact]
    public async Task Lists_a_case_in_two_subjects_of_the_theme_once()
    {
        await InsertCaseAsync("CASE-1", [1001, 1002], [(219, 20250615)]);
        await RefreshAsync();

        Assert.Single(await SampleAsync(10));
    }

    [Fact]
    public async Task Leaves_out_a_case_without_a_verified_outcome()
    {
        await InsertCaseAsync("CASE-1", [1001], [(219, 20250615)]);
        await InsertCaseAsync("CASE-2", [1001], [(237, 20250615)]);
        await RefreshAsync();

        Assert.Equal(["CASE-1"], (await SampleAsync(10)).Select(row => row.CaseNumber));
    }

    [Fact]
    public async Task Shows_the_current_outcome_of_the_case()
    {
        await InsertCaseAsync("CASE-1", [1001], [(219, 20250615), (220, 20250910)]);
        await RefreshAsync();

        var row = Assert.Single(await SampleAsync(10));

        Assert.Equal("Denied", row.OutcomeCode);
        Assert.Equal(new DateTime(2025, 9, 10), row.DecisionDate);
    }

    [Fact]
    public async Task Keeps_a_case_without_judging_body_or_class_with_empty_columns()
    {
        await InsertCaseAsync("CASE-1", [1001], [(219, 20250615)], withJudgingBody: false, withClass: false);
        await RefreshAsync();

        var row = Assert.Single(await SampleAsync(10));

        Assert.Null(row.JudgingBody);
        Assert.Null(row.CaseClass);
    }

    [Fact]
    public async Task Keeps_the_cases_of_each_theme_apart()
    {
        await InsertCaseAsync("CASE-1", [1001], [(219, 20250615)]);
        await InsertCaseAsync("CASE-2", [2001], [(220, 20250615)]);
        await RefreshAsync();

        Assert.Equal(["CASE-1"], (await SampleAsync(10)).Select(row => row.CaseNumber));
        Assert.Equal(["CASE-2"], (await SampleAsync(20)).Select(row => row.CaseNumber));
    }

    private sealed record SampleRow(
        string CaseNumber,
        string? CaseNumberFormatted,
        string CourtCode,
        string CourtName,
        string? JudgingBody,
        string? CaseClass,
        string CourtLevel,
        DateTime? DecisionDate,
        string OutcomeCode,
        string OutcomeLabel,
        string? PolarityReference,
        short SecrecyLevel,
        string Source,
        string? SourceLink,
        string? SourceLinkType);
}
