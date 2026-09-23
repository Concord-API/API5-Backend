using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using Ratio.Application.Abstractions;

namespace Ratio.Api.Tests.Controllers;

public class ThemesControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly ThemeSummary WrongfulListing = new(
        412,
        "Inscrição indevida em cadastro de inadimplentes",
        "CONSUMIDOR",
        144,
        78,
        "Dominante",
        new ThemeOutcome(142, 2, 0.9861m, "acolhimento da pretensão do autor"),
        new DateOnly(2026, 8, 30));

    [Fact]
    public async Task Returns_the_contract_shape_with_theme_key_never_theme_sk()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync("inscricao indevida", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([WrongfulListing]);

        var response = await _client.GetAsync("/api/themes?q=inscricao+indevida");
        var rawBody = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(rawBody).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("theme_sk", rawBody);
        Assert.DoesNotContain("caseNumber", rawBody);

        var theme = body.GetProperty("themes")[0];
        Assert.Equal(412, theme.GetProperty("themeKey").GetInt64());
        Assert.Equal("Inscrição indevida em cadastro de inadimplentes", theme.GetProperty("name").GetString());
        Assert.Equal("CONSUMIDOR", theme.GetProperty("subjectArea").GetString());
        Assert.Equal(144, theme.GetProperty("judgedCount").GetInt64());
        Assert.Equal(78, theme.GetProperty("strengthScore").GetInt32());
        Assert.Equal("Dominante", theme.GetProperty("level").GetString());

        var outcome = theme.GetProperty("outcome");
        Assert.Equal(142, outcome.GetProperty("upheld").GetInt64());
        Assert.Equal(2, outcome.GetProperty("rejected").GetInt64());
        Assert.Equal(0.9861m, outcome.GetProperty("upheldRatio").GetDecimal());
        Assert.Equal("acolhimento da pretensão do autor", outcome.GetProperty("polarityLabel").GetString());
        Assert.Equal("2026-08-30", theme.GetProperty("lastDecisionDate").GetString());
    }

    [Fact]
    public async Task Returns_the_query_and_total_in_the_envelope()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync("inscricao indevida", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([WrongfulListing]);

        var response = await _client.GetAsync("/api/themes?q=inscricao+indevida");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("inscricao indevida", body.GetProperty("query").GetString());
        Assert.Equal(1, body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Returns_no_results_as_an_empty_list()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync("satelite", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await _client.GetAsync("/api/themes?q=satelite");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0, body.GetProperty("total").GetInt32());
        Assert.Empty(body.GetProperty("themes").EnumerateArray());
    }

    [Fact]
    public async Task Defaults_the_limit_to_20_when_absent()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync("inscricao", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await _client.GetAsync("/api/themes?q=inscricao");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        factory.ThemeSearchReader.Verify(
            reader => reader.SearchThemesAsync("inscricao", 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Rejects_a_limit_outside_1_to_100(int limit)
    {
        var response = await _client.GetAsync($"/api/themes?limit={limit}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("O parâmetro 'limit' deve estar entre 1 e 100.", body.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("  a  ")]
    public async Task Rejects_a_query_shorter_than_3_characters(string query)
    {
        var response = await _client.GetAsync($"/api/themes?q={Uri.EscapeDataString(query)}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.1", body.GetProperty("type").GetString());
        Assert.Equal("Requisição inválida", body.GetProperty("title").GetString());
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Equal("Digite ao menos 3 caracteres para buscar.", body.GetProperty("detail").GetString());
        factory.ThemeSearchReader.Verify(
            reader => reader.SearchThemesAsync(query, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Searches_a_query_with_3_characters()
    {
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync("abc", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await _client.GetAsync("/api/themes?q=abc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        factory.ThemeSearchReader.Verify(
            reader => reader.SearchThemesAsync("abc", 20, It.IsAny<CancellationToken>()), Times.Once);
    }
}
