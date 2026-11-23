using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Infrastructure.Migrations;

namespace Ratio.Infrastructure.Tests.Migrations;

public class CourtSiteCaseSchemaTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string Detail =
        "INSERT INTO dw.case_site_detail (case_sk, status, case_class, subjects, judging_body, judge, filed_at, " +
        "decision_date, decision_result, situation, source, source_url, extracted_at) " +
        "SELECT case_sk, 'found', 'Apelação Cível', ARRAY['Planos de Saúde'], '3ª Câmara de Direito Privado', " +
        "'Relator Exemplo', '2023-01-10', '2024-05-02', 'Negaram provimento ao recurso.', 'Julgado', " +
        "'esaj_tjsp_cposg', 'https://esaj.tjsp.jus.br/cposg/search.do', now() FROM dw.dim_case";

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        await ExecuteAsync("INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES ('TJSP', 'Tribunal de Justiça de São Paulo', 'SP')");
        await ExecuteAsync(
            "INSERT INTO dw.dim_case (case_number, court_sk, court_level, source, extracted_at) " +
            "SELECT '10000000020238260100', court_sk, 'Second', 'datajud', now() FROM dw.dim_court WHERE court_code = 'TJSP'");
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    private async Task ExecuteAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Keeps_one_site_detail_per_case()
    {
        await ExecuteAsync(Detail);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(Detail));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Fact]
    public async Task Accepts_only_found_not_found_and_sealed_as_the_site_status()
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(Detail.Replace("'found'", "'blocked'")));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [Fact]
    public async Task Removes_the_movements_with_the_site_detail()
    {
        await ExecuteAsync(Detail);
        await ExecuteAsync(
            "INSERT INTO dw.case_site_movement (case_sk, seq, moved_at, description) " +
            "SELECT case_sk, 1, '2024-05-02', 'Julgado' FROM dw.case_site_detail");

        await ExecuteAsync("DELETE FROM dw.case_site_detail");

        await using var connection = _dataSource.CreateConnection();
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM dw.case_site_movement"));
    }
}
