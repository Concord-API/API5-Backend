using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ratio.Api.IntegrationTests.Controllers;

public class ThemesControllerTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private RatioApiFactory _api = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _api = new RatioApiFactory(await postgres.CreateDatabaseAsync());
        _client = _api.CreateClient();
        await _api.LoadSampleAsync();
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    [Fact]
    public async Task Finds_the_debt_collection_theme_by_search()
    {
        var response = await _client.GetAsync("/api/themes?q=cobranca%20de%20divida");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var theme = Assert.Single(body.GetProperty("themes").EnumerateArray());
        Assert.Equal(1, theme.GetProperty("themeKey").GetInt64());
        Assert.Equal("Cobrança de dívida", theme.GetProperty("name").GetString());
        Assert.Equal(40, theme.GetProperty("judgedCount").GetInt64());
        Assert.Equal(34, theme.GetProperty("outcome").GetProperty("upheld").GetInt64());
        Assert.Equal(6, theme.GetProperty("outcome").GetProperty("rejected").GetInt64());
        Assert.Equal(0.85m, theme.GetProperty("outcome").GetProperty("upheldRatio").GetDecimal());
        Assert.Equal(new string?[] { "TJRJ", "TJSP" }, theme.GetProperty("courts").EnumerateArray().Select(court => court.GetString()));
        Assert.Equal(2023, theme.GetProperty("periodStartYear").GetInt32());
        Assert.Equal(2025, theme.GetProperty("periodEndYear").GetInt32());
        Assert.Equal("2025-09-02", theme.GetProperty("lastDecisionDate").GetString());
        Assert.Equal(JsonValueKind.Number, theme.GetProperty("strengthScore").ValueKind);
        Assert.Equal(JsonValueKind.String, theme.GetProperty("level").ValueKind);
    }

    [Fact]
    public async Task Describes_the_loaded_cases_and_the_declared_scope_with_the_search()
    {
        var response = await _client.GetAsync("/api/themes?q=cobranca%20de%20divida");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var provenance = body.GetProperty("provenance");
        var source = Assert.Single(provenance.GetProperty("sources").EnumerateArray());
        Assert.Equal("datajud", source.GetProperty("source").GetString());
        Assert.Equal("DataJud/CNJ", source.GetProperty("name").GetString());
        Assert.Equal("2026-10-01T12:00:00+00:00", source.GetProperty("extractedAt").GetString());
        Assert.Equal(76, source.GetProperty("count").GetInt64());
        Assert.Equal("1.0", provenance.GetProperty("methodologyVersion").GetString());
        var scope = body.GetProperty("scope");
        Assert.Equal("TJMG, TJRJ e TJSP", scope.GetProperty("statement").GetString());
        Assert.Equal("cível", scope.GetProperty("subject").GetString());
    }

    [Fact]
    public async Task Lists_the_judged_themes_without_a_search_term()
    {
        var response = await _client.GetAsync("/api/themes");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, body.GetProperty("total").GetInt32());
        Assert.Equal(new long[] { 1, 2, 3 }, body.GetProperty("themes").EnumerateArray().Select(theme => theme.GetProperty("themeKey").GetInt64()));
    }

    [Fact]
    public async Task Returns_the_detail_of_the_theme_found_by_search()
    {
        var response = await _client.GetAsync("/api/themes/1");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Cobrança de dívida", body.GetProperty("name").GetString());
        Assert.Equal(40, body.GetProperty("caseCount").GetInt64());
        Assert.Equal(40, body.GetProperty("judgedCount").GetInt64());
        Assert.Equal(2, body.GetProperty("courtCount").GetInt64());
        Assert.Equal(2023, body.GetProperty("periodStartYear").GetInt32());
        Assert.Equal(2025, body.GetProperty("periodEndYear").GetInt32());
        Assert.Equal("2025-09-02", body.GetProperty("lastDecisionDate").GetString());
        Assert.Equal(
            "Nas decisões julgadas, houve acolhimento da pretensão do autor.",
            body.GetProperty("summary").GetProperty("lead")[0].GetProperty("text").GetString());
        var source = Assert.Single(body.GetProperty("provenance").GetProperty("sources").EnumerateArray());
        Assert.Equal(40, source.GetProperty("count").GetInt64());
    }
}
