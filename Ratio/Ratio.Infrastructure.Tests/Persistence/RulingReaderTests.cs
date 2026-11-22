using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Application.Abstractions;
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
            "('stj_rulings', '3', '0000003-00.2020.3.00.0000', 'STJ', 'Primeira Seção', '2025-02-10', 'Ementa no mesmo dia.', 'https://processo.stj.jus.br/3', '2026-11-01T10:00:00Z'), " +
            "('stj_rulings', '1', '0000001-00.2020.3.00.0000', 'STJ', 'Terceira Turma', '2024-01-05', 'Ementa anterior.', 'https://processo.stj.jus.br/1', '2026-11-01T10:00:00Z'), " +
            "('stj_rulings', '4', NULL, 'STJ', NULL, NULL, 'Ementa sem data de julgamento.', 'https://processo.stj.jus.br/4', '2026-11-01T10:00:00Z'); " +
            "INSERT INTO dw.bridge_theme_ruling (theme_sk, ruling_sk, link_method) " +
            "SELECT t.theme_sk, r.ruling_sk, 'cnj_exact' FROM dw.dim_theme t CROSS JOIN dw.dim_ruling r " +
            "WHERE t.theme_key = 412 AND r.source_id <> '1'; " +
            "INSERT INTO dw.bridge_theme_ruling (theme_sk, ruling_sk, link_method, similarity, similarity_threshold) " +
            "SELECT t.theme_sk, r.ruling_sk, 'embedding_cosine+lexical', 0.72, 0.55 FROM dw.dim_theme t CROSS JOIN dw.dim_ruling r " +
            "WHERE t.theme_key = 412 AND r.source_id = '1'");
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    [Fact]
    public async Task Lists_rulings_in_a_stable_latest_first_order()
    {
        var result = await _reader.ListAsync(412, 20, CancellationToken.None);

        Assert.Equal(["2", "3", "1", "4"], result!.Rulings.Select(ruling => ruling.SourceId));
        Assert.Equal(4, result.Total);
        Assert.Equal("4 de 4 decisões", result.SampleLabel);
        Assert.Equal("julgamento mais recente primeiro; empate pelo tribunal e identificador da fonte", result.Ordering);
        Assert.Null(result.Rulings[^1].JudgedAt);
        Assert.Equal("STJ, Espelhos de acórdãos", result.Source.Name);
    }

    [Fact]
    public async Task Declares_how_each_ruling_is_tied_to_the_theme()
    {
        var result = await _reader.ListAsync(412, 20, CancellationToken.None);

        var exact = result!.Rulings.Single(ruling => ruling.SourceId == "2");
        var similar = result.Rulings.Single(ruling => ruling.SourceId == "1");
        Assert.Equal(new RulingTie(RulingTieMethod.CaseNumber, null, null), exact.Tie);
        Assert.Equal(RulingTieMethod.Similarity, similar.Tie.Method);
        Assert.Equal(0.72, similar.Tie.Similarity!.Value, 2);
        Assert.Equal(0.55, similar.Tie.Threshold!.Value, 2);
    }

    [Fact]
    public async Task Returns_an_empty_list_for_a_theme_without_rulings()
    {
        var result = await _reader.ListAsync(413, 20, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.Rulings);
        Assert.Equal(0, result.Total);
        Assert.Equal(0, result.Shown);
        Assert.Equal("0 de 0 decisões", result.SampleLabel);
        Assert.Null(result.Source.ExtractedAt);
    }

    [Fact]
    public async Task Returns_null_for_an_unknown_theme()
    {
        Assert.Null(await _reader.ListAsync(999, 20, CancellationToken.None));
    }
}
