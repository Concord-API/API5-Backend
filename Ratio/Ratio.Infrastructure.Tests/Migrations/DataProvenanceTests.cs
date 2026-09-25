using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class DataProvenanceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private sealed record DataProvenanceRow(string Block, string Source, DateTime ExtractedAt, long RowCount);

    private const string Cases =
        "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) VALUES ('Granted', 'Procedente', true); " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT 219, 'Procedência', outcome_sk, true, 'pretensao_autor' FROM dw.dim_decision_outcome; " +
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES ('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'); " +
        "INSERT INTO dw.dim_case (case_number, court_sk, court_level, source, extracted_at) " +
        "SELECT 'PROV-' || n, court_sk, 'First', 'datajud', TIMESTAMPTZ '2026-08-28 10:00:00+00' FROM generate_series(1, 3) n CROSS JOIN dw.dim_court; " +
        "INSERT INTO dw.fact_case_event (case_sk, court_sk, movement_sk, occurred_at, source_url, extracted_at, natural_key) " +
        "SELECT c.case_sk, c.court_sk, m.movement_sk, TIMESTAMPTZ '2024-06-15', 'https://example.org', " +
        "TIMESTAMPTZ '2026-08-20 10:00:00+00' + (e * INTERVAL '1 day'), 'prov:' || c.case_sk || ':' || e " +
        "FROM dw.dim_case c CROSS JOIN dw.dim_movement m CROSS JOIN generate_series(1, 2) e;";

    private const string Doctrine =
        "INSERT INTO dw.dim_doctrine (title, article_url, source, extracted_at) VALUES " +
        "('Dano moral in re ipsa', 'https://doaj.org/a/1', 'doaj', TIMESTAMPTZ '2026-09-01 08:00:00+00'), " +
        "('Cadastro de inadimplentes', 'https://doaj.org/a/2', 'doaj', TIMESTAMPTZ '2026-09-02 08:00:00+00');";

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<DataProvenanceRow[]> ReadProvenanceAsync()
    {
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.data_provenance");
        await using var connection = _dataSource.CreateConnection();
        return (await connection.QueryAsync<DataProvenanceRow>(
            "SELECT block AS Block, source AS Source, extracted_at AS ExtractedAt, row_count AS RowCount " +
            "FROM dw.data_provenance ORDER BY block, source")).ToArray();
    }

    [Fact]
    public async Task Counts_the_cases_of_each_source_with_the_latest_extraction()
    {
        await ExecuteAsync(Cases);

        var rows = await ReadProvenanceAsync();

        var cases = Assert.Single(rows);
        Assert.Equal("cases", cases.Block);
        Assert.Equal("datajud", cases.Source);
        Assert.Equal(3, cases.RowCount);
        Assert.Equal(new DateTime(2026, 8, 22, 10, 0, 0, DateTimeKind.Utc), cases.ExtractedAt);
    }

    [Fact]
    public async Task Takes_the_extraction_date_from_the_loaded_data_not_from_the_clock()
    {
        await ExecuteAsync(Cases);

        var rows = await ReadProvenanceAsync();

        Assert.All(rows, row => Assert.True(row.ExtractedAt < DateTime.UtcNow.AddDays(-1)));
    }

    [Fact]
    public async Task Lists_doctrine_as_its_own_block()
    {
        await ExecuteAsync(Cases);
        await ExecuteAsync(Doctrine);

        var rows = await ReadProvenanceAsync();

        Assert.Equal(2, rows.Length);
        var doctrine = rows.Single(row => row.Block == "doctrine");
        Assert.Equal("doaj", doctrine.Source);
        Assert.Equal(2, doctrine.RowCount);
        Assert.Equal(new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc), doctrine.ExtractedAt);
    }

    [Fact]
    public async Task Has_no_rows_when_nothing_was_loaded()
    {
        Assert.Empty(await ReadProvenanceAsync());
    }
}
