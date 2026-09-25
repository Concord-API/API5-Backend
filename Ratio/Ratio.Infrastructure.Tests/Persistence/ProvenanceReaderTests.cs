using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Persistence;

public class ProvenanceReaderTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string WarehouseData =
        "INSERT INTO dw.strength_config (methodology_version, reference_year) VALUES ('1.0', 2026); " +
        "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) VALUES ('Granted', 'Procedente', true); " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT 219, 'Procedência', outcome_sk, true, 'pretensao_autor' FROM dw.dim_decision_outcome; " +
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES ('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'); " +
        "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Inscrição indevida', 412), ('Cobrança de dívida', 413); " +
        "INSERT INTO dw.dim_subject (subject_name, subject_code) VALUES ('Inscrição indevida', 1001), ('Cobrança', 1002); " +
        "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) " +
        "SELECT t.theme_sk, s.subject_sk FROM dw.dim_theme t JOIN dw.dim_subject s ON s.subject_code = t.theme_key + 589; " +
        "INSERT INTO dw.dim_case (case_number, court_sk, court_level, source, extracted_at) " +
        "SELECT 'READER-' || n, court_sk, 'First', 'datajud', TIMESTAMPTZ '2026-08-28 10:00:00+00' FROM generate_series(1, 4) n CROSS JOIN dw.dim_court; " +
        "INSERT INTO dw.bridge_case_subject (case_sk, subject_sk) " +
        "SELECT c.case_sk, s.subject_sk FROM dw.dim_case c JOIN dw.dim_subject s " +
        "ON s.subject_code = CASE WHEN right(c.case_number, 1)::integer = 1 THEN 1001 ELSE 1002 END; " +
        "INSERT INTO dw.fact_case_event (case_sk, court_sk, movement_sk, occurred_at, source_url, extracted_at, natural_key) " +
        "SELECT c.case_sk, c.court_sk, m.movement_sk, TIMESTAMPTZ '2024-06-15', 'https://example.org', " +
        "TIMESTAMPTZ '2026-08-20 10:00:00+00' + (right(c.case_number, 1)::integer * INTERVAL '1 day'), 'reader:' || c.case_sk " +
        "FROM dw.dim_case c CROSS JOIN dw.dim_movement m;";

    private string _database = null!;
    private NpgsqlDataSource _dataSource = null!;
    private ProvenanceReader _reader = null!;

    public async Task InitializeAsync()
    {
        _database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(_database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(_database);
        _reader = new ProvenanceReader(_dataSource, NullLogger<ProvenanceReader>.Instance);
        await ExecuteAsync(WarehouseData);
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Reads_the_global_provenance_with_the_methodology()
    {
        await ExecuteAsync("REFRESH MATERIALIZED VIEW dw.data_provenance");

        var provenance = await _reader.GetGlobalAsync(CancellationToken.None);

        var cases = Assert.Single(provenance.Sources);
        Assert.Equal("cases", cases.Block);
        Assert.Equal("datajud", cases.Source);
        Assert.Equal(new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero), cases.ExtractedAt);
        Assert.Equal(4, cases.Count);
        Assert.Equal("1.0", provenance.MethodologyVersion);
    }

    [Fact]
    public async Task Reads_the_provenance_of_one_theme()
    {
        var provenance = await _reader.GetThemeAsync(413, CancellationToken.None);

        var cases = Assert.Single(provenance.Sources);
        Assert.Equal("cases", cases.Block);
        Assert.Equal("datajud", cases.Source);
        Assert.Equal(new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero), cases.ExtractedAt);
        Assert.Equal(3, cases.Count);
        Assert.Equal("1.0", provenance.MethodologyVersion);
    }

    [Fact]
    public async Task Reads_no_sources_for_an_unknown_theme()
    {
        var provenance = await _reader.GetThemeAsync(999, CancellationToken.None);

        Assert.Empty(provenance.Sources);
    }

    [Fact]
    public async Task Reads_no_sources_when_the_aggregate_was_never_refreshed()
    {
        var provenance = await _reader.GetGlobalAsync(CancellationToken.None);

        Assert.Empty(provenance.Sources);
    }
}
