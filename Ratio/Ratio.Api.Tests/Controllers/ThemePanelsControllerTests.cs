using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using Ratio.Application.Abstractions;
using Ratio.Application.Panels;

namespace Ratio.Api.Tests.Controllers;

public class ThemePanelsControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly ThemePanelCounts Counts = new(
        412,
        [
            new JudgingBodyCount("TJSP", "3ª Câmara de Direito Privado", true, 9, 1),
            new JudgingBodyCount("TJSP", "5ª Câmara de Direito Privado", true, 2, 8),
            new JudgingBodyCount("TJSP", "8ª Câmara de Direito Privado", true, 1, 0),
            new JudgingBodyCount("TJSP", "1ª Vara Cível", false, 0, 2)
        ],
        new PanelRule(20, 5));

    [Fact]
    public async Task Returns_the_panels_of_each_court()
    {
        factory.PanelAlignmentReader
            .Setup(reader => reader.GetThemeAsync(412, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Counts);

        var response = await _client.GetAsync("/api/themes/412/panels");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(412, body.GetProperty("themeKey").GetInt64());
        Assert.Equal("provimento do recurso (pretensão de quem recorreu)", body.GetProperty("basis").GetString());
        Assert.Equal(20, body.GetProperty("rule").GetProperty("divergencePoints").GetInt32());
        Assert.Equal(5, body.GetProperty("rule").GetProperty("minJudgedForDivergence").GetInt32());
        var court = Assert.Single(body.GetProperty("courts").EnumerateArray());
        Assert.Equal("TJSP", court.GetProperty("courtCode").GetString());
        Assert.Equal(23, court.GetProperty("n").GetInt64());
        Assert.Equal(0.5217m, court.GetProperty("percentage").GetDecimal());
        Assert.Equal(3, court.GetProperty("outsidePanels").GetInt64());
        var panel = court.GetProperty("panels")[0];
        Assert.Equal("3ª Câmara de Direito Privado", panel.GetProperty("name").GetString());
        Assert.Equal(10, panel.GetProperty("n").GetInt64());
        Assert.Equal(0.9m, panel.GetProperty("percentage").GetDecimal());
        Assert.Equal(37.8m, panel.GetProperty("distanceInPoints").GetDecimal());
        Assert.True(panel.GetProperty("divergent").GetBoolean());
        Assert.Equal(2, court.GetProperty("panels").GetArrayLength());
    }

    [Fact]
    public async Task Returns_not_found_for_an_unknown_theme()
    {
        factory.PanelAlignmentReader
            .Setup(reader => reader.GetThemeAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ThemePanelCounts?)null);

        var response = await _client.GetAsync("/api/themes/999/panels");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Tema não encontrado.", body.GetProperty("detail").GetString());
    }
}
