using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class ThemeProvenanceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private sealed record ThemeProvenanceRow(
        string Block,
        string Source,
        DateTime ExtractedAt,
        long RowCount,
        string? MethodologyVersion);

    private const string Config = "INSERT INTO dw.strength_config (methodology_version, reference_year) VALUES ('1.0', 2026);";

    private const string Cases =
        "INSERT INTO dw.dim_decision_outcome (outcome_code, outcome_label, counts_in_metric) VALUES ('Granted', 'Procedente', true); " +
        "INSERT INTO dw.dim_movement (movement_code, movement_name, outcome_sk, code_verified, polarity_reference) " +
        "SELECT 219, 'Procedência', outcome_sk, true, 'pretensao_autor' FROM dw.dim_decision_outcome; " +
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES ('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'); " +
        "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Inscrição indevida', 412), ('Cobrança de dívida', 413), ('Tema sem processo', 414); " +
        "INSERT INTO dw.dim_subject (subject_name, subject_code) VALUES ('Inscrição indevida', 1001), ('Cobrança', 1002), ('Sem processo', 1003); " +
        "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) " +
        "SELECT t.theme_sk, s.subject_sk FROM dw.dim_theme t JOIN dw.dim_subject s ON s.subject_code = t.theme_key + 589; " +
        "INSERT INTO dw.dim_case (case_number, court_sk, court_level, source, extracted_at) " +
        "SELECT 'THEME-' || n, court_sk, 'First', 'datajud', TIMESTAMPTZ '2026-08-28 10:00:00+00' FROM generate_series(1, 5) n CROSS JOIN dw.dim_court; " +
        "INSERT INTO dw.bridge_case_subject (case_sk, subject_sk) " +
        "SELECT c.case_sk, s.subject_sk FROM dw.dim_case c JOIN dw.dim_subject s " +
        "ON s.subject_code = CASE WHEN right(c.case_number, 1)::integer <= 3 THEN 1001 ELSE 1002 END; " +
        "INSERT INTO dw.fact_case_event (case_sk, court_sk, movement_sk, occurred_at, source_url, extracted_at, natural_key) " +
        "SELECT c.case_sk, c.court_sk, m.movement_sk, TIMESTAMPTZ '2024-06-15', 'https://example.org', " +
        "TIMESTAMPTZ '2026-08-20 10:00:00+00' + (right(c.case_number, 1)::integer * INTERVAL '1 day'), 'theme:' || c.case_sk " +
        "FROM dw.dim_case c CROSS JOIN dw.dim_movement m;";

    private const string Doctrine =
        "INSERT INTO dw.dim_doctrine (title, article_url, source, extracted_at) VALUES " +
        "('Dano moral in re ipsa', 'https://doaj.org/a/1', 'doaj', TIMESTAMPTZ '2026-09-01 08:00:00+00'), " +
        "('Cobrança vexatória', 'https://doaj.org/a/2', 'doaj', TIMESTAMPTZ '2026-09-02 08:00:00+00'); " +
        "INSERT INTO dw.bridge_subject_doctrine (subject_sk, doctrine_sk, link_method) " +
        "SELECT s.subject_sk, d.doctrine_sk, 'similarity' FROM dw.dim_subject s JOIN dw.dim_doctrine d " +
        "ON (s.subject_code, d.title) IN ((1001, 'Dano moral in re ipsa'), (1002, 'Cobrança vexatória'));";

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        await ExecuteAsync(Cases);
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<ThemeProvenanceRow[]> ReadProvenanceAsync(long themeKey)
    {
        await using var connection = _dataSource.CreateConnection();
        return (await connection.QueryAsync<ThemeProvenanceRow>(
            "SELECT p.block AS Block, p.source AS Source, p.extracted_at AS ExtractedAt, p.row_count AS RowCount, " +
            "p.methodology_version AS MethodologyVersion " +
            "FROM dw.theme_provenance p JOIN dw.dim_theme t ON t.theme_sk = p.theme_sk " +
            "WHERE t.theme_key = @ThemeKey ORDER BY p.block, p.source",
            new { ThemeKey = themeKey })).ToArray();
    }

    [Fact]
    public async Task Counts_only_the_cases_of_the_theme_with_their_latest_extraction()
    {
        await ExecuteAsync(Config);

        var cases = Assert.Single(await ReadProvenanceAsync(412));

        Assert.Equal("cases", cases.Block);
        Assert.Equal("datajud", cases.Source);
        Assert.Equal(3, cases.RowCount);
        Assert.Equal(new DateTime(2026, 8, 23, 10, 0, 0, DateTimeKind.Utc), cases.ExtractedAt);
    }

    [Fact]
    public async Task Keeps_the_doctrine_of_the_theme_in_its_own_block()
    {
        await ExecuteAsync(Config);
        await ExecuteAsync(Doctrine);

        var rows = await ReadProvenanceAsync(413);

        Assert.Equal(["cases", "doctrine"], rows.Select(row => row.Block));
        var doctrine = rows[1];
        Assert.Equal("doaj", doctrine.Source);
        Assert.Equal(1, doctrine.RowCount);
        Assert.Equal(new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc), doctrine.ExtractedAt);
        Assert.Equal(2, rows[0].RowCount);
    }

    [Fact]
    public async Task Carries_the_methodology_version()
    {
        await ExecuteAsync(Config);

        var rows = await ReadProvenanceAsync(412);

        Assert.All(rows, row => Assert.Equal("1.0", row.MethodologyVersion));
    }

    [Fact]
    public async Task Keeps_the_sources_when_the_methodology_is_not_configured()
    {
        var cases = Assert.Single(await ReadProvenanceAsync(412));

        Assert.Null(cases.MethodologyVersion);
    }

    [Fact]
    public async Task Has_no_rows_for_a_theme_without_cases()
    {
        await ExecuteAsync(Config);

        Assert.Empty(await ReadProvenanceAsync(414));
    }
}
