using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Persistence;

public class CourtCaseCountReaderTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
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
        "('TJRJ', 'Tribunal de Justiça do Rio de Janeiro', 'RJ'), " +
        "('TJMG', 'Tribunal de Justiça de Minas Gerais', 'MG'); " +
        "INSERT INTO dw.dim_date (date_sk, full_date, year, quarter, month, month_name, day) VALUES (20250615, '2025-06-15', 2025, 2, 6, 'Junho', 15);";

    private NpgsqlDataSource _dataSource = null!;
    private CourtCaseCountReader _reader = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        _reader = new CourtCaseCountReader(_dataSource, NullLogger<CourtCaseCountReader>.Instance);
        await ExecuteAsync(Reference);
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private Task RefreshAsync() => ExecuteAsync("REFRESH MATERIALIZED VIEW dw.case_current_result");

    private async Task InsertCaseAsync(string caseNumber, string courtCode, params int[] movementCodes)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO dw.dim_case (case_number, court_sk, court_level, source, extracted_at) " +
            "SELECT @caseNumber, court_sk, 'First', 'datajud', now() FROM dw.dim_court WHERE court_code = @courtCode",
            new { caseNumber, courtCode });
        for (var index = 0; index < movementCodes.Length; index++)
        {
            await connection.ExecuteAsync(
                "INSERT INTO dw.fact_case_event (case_sk, court_sk, movement_sk, date_sk, occurred_at, source_url, extracted_at, natural_key) " +
                "SELECT dc.case_sk, dc.court_sk, m.movement_sk, 20250615, TIMESTAMPTZ '2025-06-15' + make_interval(days => @index), " +
                "'https://example.org', now(), 'evt:' || dc.case_sk || ':' || @index " +
                "FROM dw.dim_case dc JOIN dw.dim_movement m ON m.movement_code = @movementCode WHERE dc.case_number = @caseNumber",
                new { caseNumber, movementCode = movementCodes[index], index });
        }
    }

    [Fact]
    public async Task Counts_the_judged_cases_of_each_court()
    {
        await InsertCaseAsync("SP-1", "TJSP", 219);
        await InsertCaseAsync("SP-2", "TJSP", 219);
        await InsertCaseAsync("SP-3", "TJSP", 219);
        await InsertCaseAsync("RJ-1", "TJRJ", 219);
        await RefreshAsync();

        var counts = await _reader.GetAsync(CancellationToken.None);

        Assert.Equal(3, Assert.Single(counts, court => court.Code == "TJSP").CaseCount);
        Assert.Equal(1, Assert.Single(counts, court => court.Code == "TJRJ").CaseCount);
        Assert.Equal("Tribunal de Justiça de São Paulo", Assert.Single(counts, court => court.Code == "TJSP").Name);
    }

    [Fact]
    public async Task Counts_a_case_with_several_decisions_once()
    {
        await InsertCaseAsync("SP-1", "TJSP", 219, 219);
        await RefreshAsync();

        var counts = await _reader.GetAsync(CancellationToken.None);

        Assert.Equal(1, Assert.Single(counts, court => court.Code == "TJSP").CaseCount);
    }

    [Fact]
    public async Task Leaves_out_a_case_without_a_verified_outcome()
    {
        await InsertCaseAsync("RJ-1", "TJRJ", 219);
        await InsertCaseAsync("RJ-2", "TJRJ", 237);
        await RefreshAsync();

        var counts = await _reader.GetAsync(CancellationToken.None);

        Assert.Equal(1, Assert.Single(counts, court => court.Code == "TJRJ").CaseCount);
    }

    [Fact]
    public async Task Lists_a_court_without_judged_cases_with_zero()
    {
        await InsertCaseAsync("SP-1", "TJSP", 219);
        await RefreshAsync();

        var counts = await _reader.GetAsync(CancellationToken.None);

        Assert.Equal(0, Assert.Single(counts, court => court.Code == "TJMG").CaseCount);
    }

    [Fact]
    public async Task Orders_the_courts_by_case_count_and_then_by_code()
    {
        await InsertCaseAsync("RJ-1", "TJRJ", 219);
        await InsertCaseAsync("RJ-2", "TJRJ", 219);
        await RefreshAsync();

        var counts = await _reader.GetAsync(CancellationToken.None);

        Assert.Equal(["TJRJ", "TJMG", "TJSP"], counts.Select(court => court.Code));
    }

    [Fact]
    public async Task Returns_no_courts_when_the_warehouse_was_never_loaded()
    {
        Assert.Empty(await _reader.GetAsync(CancellationToken.None));
    }
}
