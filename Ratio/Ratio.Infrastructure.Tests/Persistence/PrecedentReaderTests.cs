using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Application.Abstractions;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Persistence;

public class PrecedentReaderTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string Url = "https://processo.stj.jus.br/repetitivos/temas_repetitivos/pesquisa.jsp?novaConsulta=true&tipo_pesquisa=T&cod_tema_inicial=";

    private const string WarehouseData =
        "INSERT INTO dw.dim_theme (theme_name, theme_key) VALUES ('Planos de saúde', 412), ('Tema sem tese', 413); " +
        "INSERT INTO dw.dim_subject (subject_name, subject_code) VALUES ('Planos de Saúde', 6233), ('Reajuste contratual', 7771), ('Outro', 9999); " +
        "INSERT INTO dw.bridge_theme_subject (theme_sk, subject_sk) " +
        "SELECT t.theme_sk, s.subject_sk FROM dw.dim_theme t JOIN dw.dim_subject s " +
        "ON (t.theme_key = 412 AND s.subject_code IN (6233, 7771)) OR (t.theme_key = 413 AND s.subject_code = 9999); " +
        "INSERT INTO dw.dim_precedent (precedent_kind, precedent_number, thesis, question, status, judged_at, judging_body, source_url, extracted_at) VALUES " +
        "('Tema', 952, 'O reajuste por faixa etária é válido.', 'Reajuste por faixa etária', 'Trânsito em Julgado', '2016-12-14', 'S2', '" + Url + "952&cod_tema_final=952', '2026-10-20T10:00:00Z'), " +
        "('Tema', 1016, 'A tese do Tema 952 vale para os planos coletivos.', NULL, 'Acórdão Publicado', '2022-03-23', 'S2', '" + Url + "1016&cod_tema_final=1016', '2026-10-20T10:00:00Z'), " +
        "('Tema', 1200, 'Tese sem data de julgamento.', NULL, 'Afetado', NULL, NULL, '" + Url + "1200&cod_tema_final=1200', '2026-10-20T10:00:00Z'); " +
        "INSERT INTO dw.bridge_subject_precedent (subject_sk, precedent_sk) " +
        "SELECT s.subject_sk, p.precedent_sk FROM dw.dim_subject s CROSS JOIN dw.dim_precedent p " +
        "WHERE s.subject_code IN (6233, 7771);";

    private NpgsqlDataSource _dataSource = null!;
    private PrecedentReader _reader = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        _reader = new PrecedentReader(_dataSource);
        await using var command = _dataSource.CreateCommand(WarehouseData);
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    [Fact]
    public async Task Lists_each_thesis_of_the_theme_once_with_the_latest_judgment_first()
    {
        var result = await _reader.GetThemeAsync(412, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal([1016, 952, 1200], result.Precedents.Select(precedent => precedent.Number));
    }

    [Fact]
    public async Task Reads_every_field_of_a_thesis()
    {
        var result = await _reader.GetThemeAsync(412, CancellationToken.None);

        var thesis = result!.Precedents.Single(precedent => precedent.Number == 952);
        Assert.Equal(
            new ThemePrecedent(
                "Tema",
                952,
                "O reajuste por faixa etária é válido.",
                "Reajuste por faixa etária",
                "Trânsito em Julgado",
                new DateOnly(2016, 12, 14),
                "Segunda Seção",
                Url + "952&cod_tema_final=952"),
            thesis);
    }

    [Fact]
    public async Task Declares_the_source_and_its_extraction_date()
    {
        var result = await _reader.GetThemeAsync(412, CancellationToken.None);

        Assert.Equal("STJ, Precedentes qualificados", result!.Source.Name);
        Assert.Equal("https://dadosabertos.web.stj.jus.br/dataset/precedentes-qualificados", result.Source.Url);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero), result.Source.ExtractedAt);
    }

    [Fact]
    public async Task Returns_an_empty_list_for_a_theme_without_a_thesis()
    {
        var result = await _reader.GetThemeAsync(413, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.Precedents);
        Assert.Null(result.Source.ExtractedAt);
    }

    [Fact]
    public async Task Returns_null_for_an_unknown_theme()
    {
        Assert.Null(await _reader.GetThemeAsync(999, CancellationToken.None));
    }
}
