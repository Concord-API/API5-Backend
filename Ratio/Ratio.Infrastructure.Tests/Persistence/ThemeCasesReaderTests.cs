using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Application.Abstractions;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Persistence;

public class ThemeCasesReaderTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string Reference =
        "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) VALUES " +
        "('Granted', 'Procedente', true), ('Denied', 'Improcedente', true); " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT m.code, m.name, o.outcome_sk, true, m.polarity FROM (VALUES " +
        "(219, 'Procedência', 'Granted', 'pretensao_autor'), (220, 'Improcedência', 'Denied', 'pretensao_autor'), " +
        "(237, 'Provimento', 'Granted', 'pretensao_recorrente'), (239, 'Não-Provimento', 'Denied', 'pretensao_recorrente')) " +
        "AS m(code, name, outcome, polarity) " +
        "JOIN dw.dim_decision_outcome o ON o.outcome_code = m.outcome; " +
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES " +
        "('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'), ('STJ', 'Superior Tribunal de Justiça', 'DF'); " +
        "INSERT INTO dw.dim_judging_body (court_sk, body_name) SELECT c.court_sk, b.body_name FROM (VALUES " +
        "('TJSP', '5ª Câmara de Direito Privado'), ('STJ', 'Terceira Turma'), " +
        "('STJ', 'GABINETE DO MINISTRO MARCO AURÉLIO BELLIZZE')) AS b(court_code, body_name) " +
        "JOIN dw.dim_court c ON c.court_code = b.court_code; " +
        "INSERT INTO dw.dim_case_class (class_name, claimant_type) VALUES ('Apelação Cível', 'autor_particular'); " +
        "INSERT INTO dw.dim_date (date_sk, full_date, year, quarter, month, month_name, day) VALUES " +
        "(20240320, '2024-03-20', 2024, 1, 3, 'Março', 20), " +
        "(20250615, '2025-06-15', 2025, 2, 6, 'Junho', 15), " +
        "(20250910, '2025-09-10', 2025, 3, 9, 'Setembro', 10); " +
        "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Cobrança de tarifa bancária', 412), ('Tema sem julgado', 413); " +
        "INSERT INTO dw.dim_subject (subject_name, subject_code) VALUES ('Tarifa bancária', 1001), ('Assunto sem julgado', 1002); " +
        "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) " +
        "SELECT t.theme_sk, s.subject_sk FROM dw.dim_theme t JOIN dw.dim_subject s " +
        "ON (t.theme_key, s.subject_code) IN ((412, 1001), (413, 1002));";

    private NpgsqlDataSource _dataSource = null!;
    private ThemeCasesReader _reader = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        _reader = new ThemeCasesReader(_dataSource);
        await ExecuteAsync(Reference);
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private Task RefreshAsync() => ExecuteAsync("REFRESH MATERIALIZED VIEW dw.case_current_result");

    private async Task InsertCaseAsync(
        string caseNumber,
        int movementCode,
        int dateSk,
        string? formatted = null,
        short secrecyLevel = 0,
        string courtCode = "TJSP",
        string bodyName = "5ª Câmara de Direito Privado")
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO dw.dim_case (case_number, court_sk, case_class_sk, court_level, secrecy_level, source, extracted_at, " +
            "case_number_formatted, source_link, source_link_type) " +
            "SELECT @caseNumber, c.court_sk, cc.case_class_sk, 'Second', @secrecyLevel, 'datajud', now(), " +
            "@formatted, 'https://esaj.tjsp.jus.br/' || @caseNumber, 'portal' " +
            "FROM dw.dim_court c CROSS JOIN dw.dim_case_class cc WHERE c.court_code = @courtCode; " +
            "INSERT INTO dw.bridge_case_subject (case_sk, subject_sk) " +
            "SELECT dc.case_sk, s.subject_sk FROM dw.dim_case dc, dw.dim_subject s " +
            "WHERE dc.case_number = @caseNumber AND s.subject_code = 1001; " +
            "INSERT INTO dw.fact_case_event (case_sk, court_sk, judging_body_sk, movement_sk, date_sk, occurred_at, source_url, extracted_at, natural_key) " +
            "SELECT dc.case_sk, dc.court_sk, jb.judging_body_sk, m.movement_sk, d.date_sk, d.full_date::timestamptz, " +
            "'https://example.org', now(), 'evt:' || dc.case_sk " +
            "FROM dw.dim_case dc JOIN dw.dim_judging_body jb ON jb.court_sk = dc.court_sk AND jb.body_name = @bodyName " +
            "JOIN dw.dim_movement m ON m.movement_code = @movementCode JOIN dw.dim_date d ON d.date_sk = @dateSk " +
            "WHERE dc.case_number = @caseNumber",
            new { caseNumber, movementCode, dateSk, formatted, secrecyLevel, courtCode, bodyName });
    }

    [Fact]
    public async Task Reads_the_audit_columns_of_a_judged_case()
    {
        await InsertCaseAsync("00012345620248260100", 219, 20250615, "0001234-56.2024.8.26.0100");
        await RefreshAsync();

        var cases = await _reader.ListCasesAsync(412, 20, CancellationToken.None);

        Assert.NotNull(cases);
        Assert.Equal(412, cases.ThemeKey);
        Assert.Equal(
            new SampleCase(
                "0001234-56.2024.8.26.0100",
                "TJSP",
                "5ª Câmara de Direito Privado",
                "Apelação Cível",
                "Second",
                new DateOnly(2025, 6, 15),
                "Procedente",
                "datajud",
                "https://esaj.tjsp.jus.br/00012345620248260100",
                "portal"),
            Assert.Single(cases.Cases));
    }

    [Fact]
    public async Task Falls_back_to_the_raw_number_without_a_formatted_one()
    {
        await InsertCaseAsync("CASE-1", 219, 20250615);
        await RefreshAsync();

        var cases = await _reader.ListCasesAsync(412, 20, CancellationToken.None);

        Assert.Equal("CASE-1", Assert.Single(cases!.Cases).CaseNumber);
    }

    [Fact]
    public async Task Orders_the_cases_from_the_latest_decision()
    {
        await InsertCaseAsync("CASE-1", 219, 20240320);
        await InsertCaseAsync("CASE-2", 220, 20250910);
        await InsertCaseAsync("CASE-3", 219, 20250615);
        await RefreshAsync();

        var cases = await _reader.ListCasesAsync(412, 20, CancellationToken.None);

        Assert.Equal(["CASE-2", "CASE-3", "CASE-1"], cases!.Cases.Select(sampleCase => sampleCase.CaseNumber));
    }

    [Fact]
    public async Task Breaks_a_tie_on_the_decision_date_by_the_case_number()
    {
        await InsertCaseAsync("CASE-B", 219, 20250615);
        await InsertCaseAsync("CASE-C", 220, 20250615);
        await InsertCaseAsync("CASE-A", 219, 20250615);
        await RefreshAsync();

        var cases = await _reader.ListCasesAsync(412, 20, CancellationToken.None);

        Assert.Equal(["CASE-A", "CASE-B", "CASE-C"], cases!.Cases.Select(sampleCase => sampleCase.CaseNumber));
    }

    [Fact]
    public async Task Returns_at_most_the_requested_number_of_cases()
    {
        await InsertCaseAsync("CASE-1", 219, 20240320);
        await InsertCaseAsync("CASE-2", 220, 20250910);
        await InsertCaseAsync("CASE-3", 219, 20250615);
        await RefreshAsync();

        var cases = await _reader.ListCasesAsync(412, 2, CancellationToken.None);

        Assert.Equal(["CASE-2", "CASE-3"], cases!.Cases.Select(sampleCase => sampleCase.CaseNumber));
    }

    [Fact]
    public async Task Counts_every_case_of_the_theme_beyond_the_limit()
    {
        await InsertCaseAsync("CASE-1", 219, 20240320);
        await InsertCaseAsync("CASE-2", 220, 20250910);
        await InsertCaseAsync("CASE-3", 219, 20250615);
        await RefreshAsync();

        var cases = await _reader.ListCasesAsync(412, 2, CancellationToken.None);

        Assert.Equal(3, cases!.Total);
    }

    [Fact]
    public async Task Hides_every_column_of_a_sealed_case()
    {
        await InsertCaseAsync("CASE-1", 219, 20250615, "0001234-56.2024.8.26.0100", secrecyLevel: 3);
        await RefreshAsync();

        var cases = await _reader.ListCasesAsync(412, 20, CancellationToken.None);

        Assert.Equal(SampleCase.SealedCase, Assert.Single(cases!.Cases));
    }

    [Fact]
    public async Task Counts_a_sealed_case_in_the_total()
    {
        await InsertCaseAsync("CASE-1", 219, 20250615);
        await InsertCaseAsync("CASE-2", 220, 20240320, secrecyLevel: 1);
        await RefreshAsync();

        var cases = await _reader.ListCasesAsync(412, 20, CancellationToken.None);

        Assert.Equal(2, cases!.Total);
        Assert.Equal([false, true], cases.Cases.Select(sampleCase => sampleCase.Sealed));
    }

    [Fact]
    public async Task Lists_a_sealed_case_after_the_public_ones()
    {
        await InsertCaseAsync("CASE-1", 219, 20240320);
        await InsertCaseAsync("CASE-2", 220, 20250910, secrecyLevel: 2);
        await InsertCaseAsync("CASE-3", 219, 20250615);
        await RefreshAsync();

        var cases = await _reader.ListCasesAsync(412, 20, CancellationToken.None);

        Assert.Equal(["CASE-3", "CASE-1", null], cases!.Cases.Select(sampleCase => sampleCase.CaseNumber));
    }

    [Fact]
    public async Task Describes_an_appeal_by_whether_it_was_granted()
    {
        await InsertCaseAsync("CASE-1", 237, 20250910);
        await InsertCaseAsync("CASE-2", 239, 20250615);
        await RefreshAsync();

        var cases = await _reader.ListCasesAsync(412, 20, CancellationToken.None);

        Assert.Equal(["Recurso provido", "Recurso não provido"], cases!.Cases.Select(sampleCase => sampleCase.Outcome));
    }

    [Fact]
    public async Task Names_the_panel_of_the_STJ_in_the_chamber_column()
    {
        await InsertCaseAsync("CASE-1", 237, 20250615, courtCode: "STJ", bodyName: "Terceira Turma");
        await RefreshAsync();

        var cases = await _reader.ListCasesAsync(412, 20, CancellationToken.None);

        var sampleCase = Assert.Single(cases!.Cases);
        Assert.Equal("STJ", sampleCase.CourtCode);
        Assert.Equal("Terceira Turma", sampleCase.JudgingBody);
    }

    [Fact]
    public async Task Leaves_the_chamber_empty_for_an_STJ_body_that_is_not_a_panel()
    {
        await InsertCaseAsync(
            "CASE-1", 237, 20250615, courtCode: "STJ", bodyName: "GABINETE DO MINISTRO MARCO AURÉLIO BELLIZZE");
        await RefreshAsync();

        var cases = await _reader.ListCasesAsync(412, 20, CancellationToken.None);

        Assert.Null(Assert.Single(cases!.Cases).JudgingBody);
    }

    [Fact]
    public async Task Returns_no_cases_for_a_theme_without_judgments()
    {
        await InsertCaseAsync("CASE-1", 219, 20250615);
        await RefreshAsync();

        var cases = await _reader.ListCasesAsync(413, 20, CancellationToken.None);

        Assert.NotNull(cases);
        Assert.Empty(cases.Cases);
        Assert.Equal(0, cases.Total);
    }

    [Fact]
    public async Task Returns_null_for_an_unknown_theme()
    {
        await RefreshAsync();

        Assert.Null(await _reader.ListCasesAsync(999, 20, CancellationToken.None));
    }

    [Fact]
    public async Task Returns_no_cases_when_the_current_result_was_never_refreshed()
    {
        await InsertCaseAsync("CASE-1", 219, 20250615);

        var cases = await _reader.ListCasesAsync(412, 20, CancellationToken.None);

        Assert.NotNull(cases);
        Assert.Empty(cases.Cases);
        Assert.Equal(0, cases.Total);
    }
}
