using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Ratio.Api.IntegrationTests.Controllers;

public class CasesControllerTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string Public = "10000012320248260100";
    private const string SealedInTheSample = "10000024520248260100";
    private const string SealedOnTheSite = "10000036720248260100";

    private static readonly string[] CaseFields =
        ["caseNumber", "court", "courtLevel", "status", "message", "fields", "missingFields", "movements", "source"];

    private static readonly string[] DetailFields =
        ["caseClass", "subjects", "judgingBody", "judge", "filedAt", "decisionDate", "decisionResult", "situation"];

    private const string WarehouseData =
        "INSERT INTO dw.dim_court (court_code, court_name, state_uf) VALUES ('TJSP', 'Tribunal de Justiça de São Paulo', 'SP'); " +
        "INSERT INTO dw.dim_case (case_number, case_number_formatted, court_sk, court_level, secrecy_level, source, extracted_at) " +
        "SELECT v.number, v.formatted, c.court_sk, 'First', v.secrecy, 'datajud', now() FROM (VALUES " +
        "('" + Public + "', '1000001-23.2024.8.26.0100', 0), " +
        "('" + SealedInTheSample + "', '1000002-45.2024.8.26.0100', 3), " +
        "('" + SealedOnTheSite + "', '1000003-67.2024.8.26.0100', 0)) AS v(number, formatted, secrecy) CROSS JOIN dw.dim_court c; " +
        "INSERT INTO dw.case_site_detail (case_sk, status, case_class, subjects, judging_body, judge, filed_at, " +
        "decision_date, decision_result, situation, source, source_url, extracted_at) " +
        "SELECT case_sk, CASE WHEN case_number = '" + SealedOnTheSite + "' THEN 'sealed' ELSE 'found' END, " +
        "'Procedimento Comum Cível', ARRAY['Planos de Saúde'], '2ª Vara Cível', 'MAGISTRADA DE TESTE', '2024-08-11', " +
        "'2025-01-20', 'Procedente', 'Em andamento', 'esaj_tjsp_cpopg', 'https://esaj.tjsp.jus.br/cpopg/show.do', now() " +
        "FROM dw.dim_case; " +
        "INSERT INTO dw.case_site_movement (case_sk, seq, moved_at, description) " +
        "SELECT case_sk, 1, '2025-01-20', 'Julgada procedente a ação' FROM dw.dim_case;";

    private RatioApiFactory _api = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        _api = new RatioApiFactory(database);
        _client = _api.CreateClient();
        await _client.GetAsync("/health");
        await using var dataSource = NpgsqlDataSource.Create(database);
        await using var command = dataSource.CreateCommand(WarehouseData);
        await command.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    [Fact]
    public async Task Returns_only_the_public_fields_of_a_case()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/cases/1000001-23.2024.8.26.0100");

        Assert.Equal("available", body.GetProperty("status").GetString());
        Assert.Equal(CaseFields, body.EnumerateObject().Select(property => property.Name));
        Assert.Equal(DetailFields, body.GetProperty("fields").EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ["date", "description"],
            body.GetProperty("movements")[0].EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("advogad", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("parte", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("1000002-45.2024.8.26.0100")]
    [InlineData("1000003-67.2024.8.26.0100")]
    public async Task Exposes_nothing_of_a_sealed_case(string number)
    {
        var body = await _client.GetFromJsonAsync<JsonElement>($"/api/cases/{number}");

        Assert.Equal("sealed", body.GetProperty("status").GetString());
        foreach (var field in new[] { "caseNumber", "court", "courtLevel", "fields", "source" })
        {
            Assert.Equal(JsonValueKind.Null, body.GetProperty(field).ValueKind);
        }

        Assert.Empty(body.GetProperty("movements").EnumerateArray());
        Assert.DoesNotContain("MAGISTRADA", body.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(number, body.GetRawText(), StringComparison.Ordinal);
    }
}
