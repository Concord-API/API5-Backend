using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Persistence;

public class CourtLevelReaderTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string Reference =
        "INSERT INTO dw.strength_config (methodology_version, reference_year) VALUES ('1.0', 2026); " +
        "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) VALUES ('Granted', 'Procedente', true); " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT 219, 'Procedência', outcome_sk, true, 'pretensao_autor' FROM dw.dim_decision_outcome WHERE outcome_code = 'Granted'; " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified) " +
        "SELECT 237, 'Provimento', outcome_sk, false FROM dw.dim_decision_outcome WHERE outcome_code = 'Granted'; " +
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES " +
        "('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'), " +
        "('STJ', 'Superior Tribunal de Justiça', 'DF'); " +
        "INSERT INTO dw.dim_date (date_sk, full_date, year, quarter, month, month_name, day) VALUES (20250615, '2025-06-15', 2025, 2, 6, 'Junho', 15);";

    private NpgsqlDataSource _dataSource = null!;
    private CourtLevelReader _reader = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        _reader = new CourtLevelReader(_dataSource, NullLogger<CourtLevelReader>.Instance);
        await ExecuteAsync(Reference);
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private Task RefreshAsync() => ExecuteAsync("REFRESH MATERIALIZED VIEW dw.case_current_result");

    private async Task InsertCaseAsync(string caseNumber, string courtCode, string courtLevel, int movementCode)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO dw.dim_case (case_number, court_sk, court_level, source, extracted_at) " +
            "SELECT @caseNumber, court_sk, @courtLevel, 'datajud', now() FROM dw.dim_court WHERE court_code = @courtCode",
            new { caseNumber, courtCode, courtLevel });
        await connection.ExecuteAsync(
            "INSERT INTO dw.fact_case_event (case_sk, court_sk, movement_sk, date_sk, occurred_at, source_url, extracted_at, natural_key) " +
            "SELECT dc.case_sk, dc.court_sk, m.movement_sk, 20250615, TIMESTAMPTZ '2025-06-15', " +
            "'https://example.org', now(), 'evt:' || dc.case_sk " +
            "FROM dw.dim_case dc JOIN dw.dim_movement m ON m.movement_code = @movementCode WHERE dc.case_number = @caseNumber",
            new { caseNumber, movementCode });
    }

    [Fact]
    public async Task Lists_each_level_with_judged_cases_once()
    {
        await InsertCaseAsync("SP-1", "TJSP", "First", 219);
        await InsertCaseAsync("SP-2", "TJSP", "First", 219);
        await InsertCaseAsync("SP-3", "TJSP", "AppealPanel", 219);
        await RefreshAsync();

        var levels = await _reader.GetAsync(CancellationToken.None);

        Assert.Equal(["AppealPanel", "First"], levels);
    }

    [Fact]
    public async Task Leaves_out_a_level_whose_cases_have_no_verified_outcome()
    {
        await InsertCaseAsync("SP-1", "TJSP", "First", 219);
        await InsertCaseAsync("SP-2", "TJSP", "Second", 237);
        await RefreshAsync();

        var levels = await _reader.GetAsync(CancellationToken.None);

        Assert.Equal(["First"], levels);
    }

    [Fact]
    public async Task Lists_the_superior_level_only_with_a_judged_superior_court_case()
    {
        await InsertCaseAsync("SP-1", "TJSP", "Second", 219);
        await RefreshAsync();

        Assert.DoesNotContain("Superior", await _reader.GetAsync(CancellationToken.None));

        await InsertCaseAsync("STJ-1", "STJ", "Superior", 219);
        await RefreshAsync();

        Assert.Contains("Superior", await _reader.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Returns_no_levels_when_the_warehouse_was_never_loaded()
    {
        Assert.Empty(await _reader.GetAsync(CancellationToken.None));
    }
}
