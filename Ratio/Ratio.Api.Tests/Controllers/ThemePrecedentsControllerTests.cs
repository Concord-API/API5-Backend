using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using Ratio.Application.Abstractions;

namespace Ratio.Api.Tests.Controllers;

public class ThemePrecedentsControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly ThemePrecedents Precedents = new(
        412,
        new PrecedentSource(
            "STJ, Precedentes qualificados",
            "https://dadosabertos.web.stj.jus.br/dataset/precedentes-qualificados",
            new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero)),
        [
            new ThemePrecedent(
                "Tema",
                952,
                "O reajuste por faixa etária é válido.",
                "Reajuste por faixa etária",
                "Trânsito em Julgado",
                new DateOnly(2016, 12, 14),
                "Segunda Seção",
                "https://processo.stj.jus.br/repetitivos/temas_repetitivos/pesquisa.jsp?novaConsulta=true&tipo_pesquisa=T&cod_tema_inicial=952&cod_tema_final=952")
        ]);

    [Fact]
    public async Task Returns_the_theses_of_the_theme_with_their_source()
    {
        factory.PrecedentReader
            .Setup(reader => reader.GetThemeAsync(412, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Precedents);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/themes/412/precedents");

        Assert.Equal(412, body.GetProperty("themeKey").GetInt64());
        Assert.Equal("STJ, Precedentes qualificados", body.GetProperty("source").GetProperty("name").GetString());
        Assert.Equal("2026-10-20T10:00:00+00:00", body.GetProperty("source").GetProperty("extractedAt").GetString());
        var thesis = Assert.Single(body.GetProperty("precedents").EnumerateArray());
        Assert.Equal("Tema", thesis.GetProperty("kind").GetString());
        Assert.Equal(952, thesis.GetProperty("number").GetInt32());
        Assert.Equal("O reajuste por faixa etária é válido.", thesis.GetProperty("thesis").GetString());
        Assert.Equal("2016-12-14", thesis.GetProperty("judgedAt").GetString());
        Assert.Equal("Segunda Seção", thesis.GetProperty("judgingBody").GetString());
        Assert.StartsWith("https://processo.stj.jus.br/", thesis.GetProperty("link").GetString());
    }

    [Fact]
    public async Task Returns_not_found_for_an_unknown_theme()
    {
        factory.PrecedentReader
            .Setup(reader => reader.GetThemeAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ThemePrecedents?)null);

        var response = await _client.GetAsync("/api/themes/999/precedents");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Tema não encontrado.", body.GetProperty("detail").GetString());
    }
}
