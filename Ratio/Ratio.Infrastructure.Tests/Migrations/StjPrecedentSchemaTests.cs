using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class StjPrecedentSchemaTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string Precedent =
        "INSERT INTO dw.dim_precedent (precedent_kind, precedent_number, thesis, question, status, judged_at, " +
        "judging_body, source_url, extracted_at) VALUES ('Tema', 952, 'O reajuste por faixa etária é válido.', " +
        "'Reajuste por faixa etária', 'Trânsito em Julgado', '2016-12-14', 'S2', " +
        "'https://processo.stj.jus.br/repetitivos/temas_repetitivos/pesquisa.jsp?novaConsulta=true&tipo_pesquisa=T&cod_tema_inicial=952&cod_tema_final=952', now())";

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        await ExecuteAsync("INSERT INTO dw.dim_subject (subject_name, subject_code) VALUES ('Planos de Saúde', 6233)");
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Keeps_one_row_per_precedent_kind_and_number()
    {
        await ExecuteAsync(Precedent);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(Precedent));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Fact]
    public async Task Requires_the_thesis_text()
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(Precedent.Replace("'O reajuste por faixa etária é válido.'", "NULL")));

        Assert.Equal(PostgresErrorCodes.NotNullViolation, exception.SqlState);
    }

    [Fact]
    public async Task Removes_the_subject_links_with_the_precedent()
    {
        await ExecuteAsync(Precedent);
        await ExecuteAsync(
            "INSERT INTO dw.bridge_subject_precedent (subject_sk, precedent_sk) " +
            "SELECT s.subject_sk, p.precedent_sk FROM dw.dim_subject s CROSS JOIN dw.dim_precedent p");

        await ExecuteAsync("DELETE FROM dw.dim_precedent");

        await using var connection = _dataSource.CreateConnection();
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM dw.bridge_subject_precedent"));
    }
}
