using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using Ratio.Application.Abstractions;

namespace Ratio.Api.Tests.Controllers;

public class ThemeRulingsControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Returns_court_rulings_without_precedent_fields()
    {
        factory.RulingReader.Setup(reader => reader.ListAsync(412, 20, It.IsAny<CancellationToken>())).ReturnsAsync(
            new ThemeRulings(
                412,
                new RulingSource("STJ, Espelhos de acórdãos", "https://dadosabertos.web.stj.jus.br/dataset/espelhos-de-acordaos-primeira-secao", new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero)),
                [new CourtRuling("2", "0000002-00.2020.3.00.0000", "STJ", "Segunda Seção", new DateOnly(2025, 2, 10), "Ementa publicada.", "https://processo.stj.jus.br/2", new RulingTie(RulingTieMethod.Similarity, 0.72, 0.55))],
                1));

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes/412/rulings");

        Assert.Equal("Ementa publicada.", body.GetProperty("rulings")[0].GetProperty("summary").GetString());
        Assert.False(body.GetProperty("rulings")[0].TryGetProperty("thesis", out _));
        Assert.Equal("1 de 1 decisão", body.GetProperty("sampleLabel").GetString());
        var tie = body.GetProperty("rulings")[0].GetProperty("tie");
        Assert.Equal("similarity", tie.GetProperty("method").GetString());
        Assert.Equal(0.72, tie.GetProperty("similarity").GetDouble(), 2);
        Assert.Equal(0.55, tie.GetProperty("threshold").GetDouble(), 2);
    }

    [Fact]
    public async Task Returns_not_found_for_an_unknown_theme()
    {
        factory.RulingReader.Setup(reader => reader.ListAsync(999, 20, It.IsAny<CancellationToken>())).ReturnsAsync((ThemeRulings?)null);

        var response = await _client.GetAsync("/api/themes/999/rulings");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
