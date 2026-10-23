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
}
