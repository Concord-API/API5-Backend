using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using Ratio.Application.Abstractions;

namespace Ratio.Api.Tests.Controllers;

public class ThemeExportControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly SampleCase[] Cases =
    [
        new(
            "0001234-56.2024.8.26.0100",
            "TJSP",
            "5ª Câmara de Direito Privado",
            "Apelação Cível",
            "Second",
            new DateOnly(2025, 6, 15),
            "Procedente",
            "datajud",
            "https://esaj.tjsp.jus.br/cpopg/open.do",
            "portal"),
        SampleCase.SealedCase
    ];

    private void Returns(long themeKey, IReadOnlyList<SampleCase>? cases) =>
        factory.ThemeCasesReader
            .Setup(reader => reader.ListAllCasesAsync(themeKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cases);

    [Theory]
    [InlineData("/api/themes/412/export")]
    [InlineData("/api/themes/412/export?format=csv")]
    public async Task Returns_the_cases_of_the_theme_as_a_csv_file(string url)
    {
        Returns(412, Cases);

        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("tema-412.csv", response.Content.Headers.ContentDisposition?.FileName);
        var lines = (await response.Content.ReadAsStringAsync()).Split("\r\n");
        Assert.Equal(
            [
                "Fonte;DataJud/CNJ;https://www.cnj.jus.br/sistemas/datajud/",
                "Data de extração;28/08/2026",
                "Escopo;cível · TJMG, TJRJ e TJSP",
                "",
                "Processo;Tribunal;Órgão julgador;Classe;Grau;Data do julgamento;Resultado;Fonte;Link;Tipo do link;Sigilo",
                "0001234-56.2024.8.26.0100;TJSP;5ª Câmara de Direito Privado;Apelação Cível;Second;15/06/2025;" +
                "Procedente;datajud;https://esaj.tjsp.jus.br/cpopg/open.do;portal;não",
                ";;;;;;;;;;sim",
                ""
            ],
            lines);
    }

    [Fact]
    public async Task Returns_only_the_header_for_a_theme_without_cases()
    {
        Returns(413, []);

        var csv = await _client.GetStringAsync("/api/themes/413/export");

        Assert.Single(csv.Split("\r\n\r\n", 2)[1].Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task Leaves_the_source_empty_without_a_cases_provenance()
    {
        Returns(415, []);
        factory.ProvenanceReader
            .Setup(reader => reader.GetThemeAsync(415, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoadedProvenance(
                [new LoadedSource("doctrine", "doaj", new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero), 30)],
                "1.0"));

        var csv = await _client.GetStringAsync("/api/themes/415/export");

        Assert.StartsWith("Fonte;\r\nData de extração;\r\nEscopo;", csv);
    }

    [Fact]
    public async Task Returns_not_found_for_an_unknown_theme()
    {
        Returns(999, null);

        var response = await _client.GetAsync("/api/themes/999/export");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Tema não encontrado.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Rejects_a_format_other_than_csv()
    {
        Returns(414, Cases);

        var response = await _client.GetAsync("/api/themes/414/export?format=xlsx");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Formato inválido: use csv.", body.GetProperty("detail").GetString());
        factory.ThemeCasesReader.Verify(
            reader => reader.ListAllCasesAsync(414, It.IsAny<CancellationToken>()), Times.Never);
    }
}
