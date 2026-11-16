using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class QualifiedPrecedentAttributesTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string PipelineInsert =
        "INSERT INTO dw.dim_precedent (precedent_kind, precedent_number, thesis, question, status, judged_at, " +
        "judging_body, source_url, extracted_at) VALUES ('Tema', 952, 'O reajuste por faixa etária é válido.', " +
        "'Reajuste por faixa etária', 'Trânsito em Julgado', '2016-12-14', 'S2', " +
        "'https://processo.stj.jus.br/repetitivos/temas_repetitivos/pesquisa.jsp?cod_tema_inicial=952', now())";

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, parameters);
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        return (await connection.ExecuteScalarAsync<T>(sql))!;
    }

    private Task InsertTypedAsync(string type) =>
        ExecuteAsync(
            "INSERT INTO dw.dim_precedent (precedent_kind, precedent_number, precedent_type, thesis, status, " +
            "source_url, extracted_at) VALUES ('IRDR', 7, @type, 'Tese.', 'Julgado', 'https://tjsp.jus.br', now())",
            new { type });

    [Theory]
    [InlineData("sumula")]
    [InlineData("tema_repetitivo")]
    [InlineData("irdr")]
    [InlineData("iac")]
    [InlineData("repercussao_geral")]
    public async Task Accepts_each_qualified_precedent_type(string type)
    {
        await InsertTypedAsync(type);

        Assert.Equal(type, await ScalarAsync<string>("SELECT precedent_type FROM dw.dim_precedent"));
    }

    [Fact]
    public async Task Rejects_an_unknown_precedent_type()
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertTypedAsync("acordao"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Fact]
    public async Task Keeps_accepting_the_pipeline_insert_without_the_new_columns()
    {
        await ExecuteAsync(PipelineInsert);

        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM dw.dim_precedent WHERE precedent_type IS NULL"));
    }
}
