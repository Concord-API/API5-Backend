using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ratio.Application.Abstractions;
using Ratio.Infrastructure.Migrations;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Persistence;

public class CaseDetailReaderTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string Collected = "10000012320248260100";
    private const string NotCollected = "10000036720248260100";
    private const string Sealed = "10000048920248260100";
    private const string RioCase = "00000011220248190001";
    private const string SiteUrl = "https://esaj.tjsp.jus.br/cpopg/show.do?processo.codigo=1";

    private const string WarehouseData =
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES " +
        "('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'), ('TJRJ', 'Tribunal de Justiça do Rio de Janeiro', 'RJ'); " +
        "INSERT INTO dw.dim_case (case_number, case_number_formatted, court_sk, court_level, secrecy_level, source, extracted_at) " +
        "SELECT v.number, v.formatted, c.court_sk, v.level, v.secrecy, 'datajud', '2026-10-01T10:00:00Z' FROM (VALUES " +
        "('" + Collected + "', '1000001-23.2024.8.26.0100', 'TJSP', 'First', 0), " +
        "('" + NotCollected + "', '1000003-67.2024.8.26.0100', 'TJSP', 'First', 0), " +
        "('" + Sealed + "', '1000004-89.2024.8.26.0100', 'TJSP', 'First', 2), " +
        "('" + RioCase + "', '0000001-12.2024.8.19.0001', 'TJRJ', 'First', 0)) " +
        "AS v(number, formatted, court, level, secrecy) JOIN dw.dim_court c ON c.court_code = v.court; " +
        "INSERT INTO dw.case_site_detail (case_sk, status, case_class, subjects, judging_body, judge, filed_at, " +
        "decision_date, decision_result, situation, source, source_url, extracted_at) " +
        "SELECT case_sk, 'found', 'Procedimento Comum Cível', ARRAY['Planos de Saúde'], '5ª Vara Cível', NULL, " +
        "'2024-01-10', '2025-03-05', 'Procedente', 'Arquivado', 'esaj_tjsp_1', '" + SiteUrl + "', '2026-11-20T10:00:00Z' " +
        "FROM dw.dim_case WHERE case_number = '" + Collected + "'; " +
        "INSERT INTO dw.case_site_movement (case_sk, seq, moved_at, description) " +
        "SELECT case_sk, m.seq, m.moved_at, m.description FROM dw.dim_case CROSS JOIN (VALUES " +
        "(1, DATE '2025-03-05', 'Julgada procedente a ação'), (2, DATE '2024-01-10', 'Distribuído livremente')) " +
        "AS m(seq, moved_at, description) WHERE case_number = '" + Collected + "';";

    private NpgsqlDataSource _dataSource = null!;
    private CaseDetailReader _reader = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        await new DatabaseMigrator(database, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(CancellationToken.None);
        _dataSource = NpgsqlDataSource.Create(database);
        _reader = new CaseDetailReader(_dataSource);
        await using var command = _dataSource.CreateCommand(WarehouseData);
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => _dataSource.DisposeAsync().AsTask();

    [Fact]
    public async Task Reads_the_site_details_and_the_movements_in_the_site_order()
    {
        var detail = await _reader.GetAsync(Collected, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(CaseAvailability.Available, detail.Status);
        Assert.Equal("1000001-23.2024.8.26.0100", detail.CaseNumber);
        Assert.Equal("TJSP", detail.Court);
        Assert.Equal("First", detail.CourtLevel);
        Assert.NotNull(detail.Fields);
        Assert.Equal("Procedimento Comum Cível", detail.Fields.CaseClass);
        Assert.Equal(["Planos de Saúde"], detail.Fields.Subjects);
        Assert.Equal("5ª Vara Cível", detail.Fields.JudgingBody);
        Assert.Null(detail.Fields.Judge);
        Assert.Equal(new DateOnly(2024, 1, 10), detail.Fields.FiledAt);
        Assert.Equal(new DateOnly(2025, 3, 5), detail.Fields.DecisionDate);
        Assert.Equal("Procedente", detail.Fields.DecisionResult);
        Assert.Equal("Arquivado", detail.Fields.Situation);
        Assert.Equal(["judge"], detail.MissingFields);
        Assert.Equal(
            [new CaseMovement(new DateOnly(2025, 3, 5), "Julgada procedente a ação"),
             new CaseMovement(new DateOnly(2024, 1, 10), "Distribuído livremente")],
            detail.Movements);
        Assert.Equal(
            new CaseSource("TJSP, consulta pública do e-SAJ", SiteUrl, new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero)),
            detail.Source);
    }

    [Fact]
    public async Task Says_a_case_of_a_court_with_a_source_was_not_collected_yet()
    {
        var detail = await _reader.GetAsync(NotCollected, CancellationToken.None);

        Assert.Equal(CaseAvailability.NotLoaded, detail!.Status);
        Assert.Equal("1000003-67.2024.8.26.0100", detail.CaseNumber);
    }

    [Fact]
    public async Task Exposes_nothing_of_a_sealed_case()
    {
        var detail = await _reader.GetAsync(Sealed, CancellationToken.None);

        Assert.Equal(CaseAvailability.Sealed, detail!.Status);
        Assert.Null(detail.CaseNumber);
        Assert.Null(detail.Court);
    }

    [Fact]
    public async Task Returns_only_the_court_and_the_reason_for_a_court_without_a_source()
    {
        var detail = await _reader.GetAsync(RioCase, CancellationToken.None);

        Assert.Equal(CaseAvailability.SourceUnavailable, detail!.Status);
        Assert.Equal("TJRJ", detail.Court);
        Assert.Null(detail.CaseNumber);
    }

    [Fact]
    public async Task Returns_null_for_a_case_outside_the_sample()
    {
        Assert.Null(await _reader.GetAsync("99999999999999999999", CancellationToken.None));
    }
}
