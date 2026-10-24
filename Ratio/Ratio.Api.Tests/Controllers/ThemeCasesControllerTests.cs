using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using Ratio.Application.Abstractions;

namespace Ratio.Api.Tests.Controllers;

public class ThemeCasesControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly ThemeCases Cases = new(
        412,
        [
            new SampleCase(
                "0001234-56.2024.8.26.0100",
                "TJSP",
                "5ª Câmara de Direito Privado",
                "Apelação Cível",
                "Second",
                new DateOnly(2025, 6, 15),
                "Procedente",
                "datajud",
                "https://esaj.tjsp.jus.br/cpopg/open.do",
                "portal")
        ],
        12418);

    [Fact]
    public async Task Returns_the_cases_of_the_theme_with_the_declared_ordering()
    {
        factory.ThemeCasesReader
            .Setup(reader => reader.ListCasesAsync(412, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Cases);

        var response = await _client.GetAsync("/api/themes/412/cases");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(412, body.GetProperty("themeKey").GetInt64());
        Assert.Equal(
            "julgamento mais recente primeiro; empate pelo número do processo",
            body.GetProperty("ordering").GetString());
        Assert.Equal(1, body.GetProperty("shown").GetInt32());
        Assert.Equal(12418, body.GetProperty("total").GetInt64());
        Assert.Equal("1 de 12.418 decisões", body.GetProperty("sampleLabel").GetString());
        var sampleCase = Assert.Single(body.GetProperty("cases").EnumerateArray());
        Assert.Equal("0001234-56.2024.8.26.0100", sampleCase.GetProperty("caseNumber").GetString());
        Assert.Equal("TJSP", sampleCase.GetProperty("courtCode").GetString());
        Assert.Equal("5ª Câmara de Direito Privado", sampleCase.GetProperty("judgingBody").GetString());
        Assert.Equal("Apelação Cível", sampleCase.GetProperty("caseClass").GetString());
        Assert.Equal("Second", sampleCase.GetProperty("courtLevel").GetString());
        Assert.Equal("2025-06-15", sampleCase.GetProperty("decisionDate").GetString());
        Assert.Equal("Procedente", sampleCase.GetProperty("outcome").GetString());
        Assert.Equal("datajud", sampleCase.GetProperty("source").GetString());
        Assert.Equal("https://esaj.tjsp.jus.br/cpopg/open.do", sampleCase.GetProperty("sourceLink").GetString());
        Assert.Equal("portal", sampleCase.GetProperty("sourceLinkType").GetString());
    }

    [Fact]
    public async Task Reads_twenty_cases_without_a_limit()
    {
        factory.ThemeCasesReader
            .Setup(reader => reader.ListCasesAsync(414, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Cases with { ThemeKey = 414 });

        await _client.GetAsync("/api/themes/414/cases");

        factory.ThemeCasesReader.Verify(reader => reader.ListCasesAsync(414, 20, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Reads_the_requested_number_of_cases()
    {
        factory.ThemeCasesReader
            .Setup(reader => reader.ListCasesAsync(415, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Cases with { ThemeKey = 415 });

        await _client.GetAsync("/api/themes/415/cases?limit=50");

        factory.ThemeCasesReader.Verify(reader => reader.ListCasesAsync(415, 50, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Returns_not_found_for_an_unknown_theme()
    {
        factory.ThemeCasesReader
            .Setup(reader => reader.ListCasesAsync(999, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ThemeCases?)null);

        var response = await _client.GetAsync("/api/themes/999/cases");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Tema não encontrado.", body.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Rejects_a_limit_outside_1_to_100(int limit)
    {
        var response = await _client.GetAsync($"/api/themes/416/cases?limit={limit}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("O parâmetro 'limit' deve estar entre 1 e 100.", body.GetProperty("detail").GetString());
        factory.ThemeCasesReader.Verify(
            reader => reader.ListCasesAsync(416, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
