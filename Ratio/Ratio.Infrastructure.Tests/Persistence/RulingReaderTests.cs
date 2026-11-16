using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Persistence;

public class RulingReaderTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private NpgsqlDataSource _dataSource = null!;
    private RulingReader _reader = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        _reader = new RulingReader(_dataSource);
        await using var command = _dataSource.CreateCommand(
            "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Planos de saúde', 412), ('Sem acórdão', 413); " +
            "INSERT INTO dw.dim_ruling (source, source_id, case_number, court_code, judging_body, judged_at, summary, source_url, extracted_at) VALUES " +
            "('stj_rulings', '2', '0000002-00.2020.3.00.0000', 'STJ', 'Segunda Seção', '2025-02-10', 'Ementa mais recente.', 'https://processo.stj.jus.br/2', '2026-11-01T10:00:00Z'), " +
            "('stj_rulings', '1', '0000001-00.2020.3.00.0000', 'STJ', 'Terceira Turma', '2024-01-05', 'Ementa anterior.', 'https://processo.stj.jus.br/1', '2026-11-01T10:00:00Z'); " +
            "INSERT INTO dw.bridge_theme_ruling (theme_sk, ruling_sk, link_method) " +
            "SELECT t.theme_sk, r.ruling_sk, 'cnj_exact' FROM dw.dim_theme t CROSS JOIN dw.dim_ruling r WHERE t.theme_key = 412");
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    [Fact]
    public async Task Lists_rulings_in_a_stable_latest_first_order()
    {
        var result = await _reader.ListAsync(412, 20, CancellationToken.None);

        Assert.Equal(["2", "1"], result!.Rulings.Select(ruling => ruling.SourceId));
        Assert.Equal(2, result.Total);
        Assert.Equal("2 de 2 decisões", result.SampleLabel);
        Assert.Equal("STJ, Espelhos de acórdãos", result.Source.Name);
    }

    [Fact]
    public async Task Returns_an_empty_list_for_a_theme_without_rulings()
    {
        var result = await _reader.ListAsync(413, 20, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.Rulings);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task Returns_null_for_an_unknown_theme()
    {
        Assert.Null(await _reader.ListAsync(999, 20, CancellationToken.None));
    }
}
