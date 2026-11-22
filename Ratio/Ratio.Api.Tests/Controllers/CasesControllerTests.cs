using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using Ratio.Application.Abstractions;

namespace Ratio.Api.Tests.Controllers;

public class CasesControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly CaseDetail Available = new(
        "1000001-23.2024.8.26.0100",
        "TJSP",
        "First",
        CaseAvailability.Available,
        "O site do tribunal não publica estes campos para este processo.",
        new CaseFields(
            "Procedimento Comum Cível",
            ["Planos de Saúde"],
            "5ª Vara Cível",
            null,
            new DateOnly(2024, 1, 10),
            new DateOnly(2025, 3, 5),
            "Procedente",
            "Arquivado"),
        ["judge"],
        [new CaseMovement(new DateOnly(2025, 3, 5), "Julgada procedente a ação")],
        new CaseSource(
            "TJSP, consulta pública do e-SAJ",
            "https://esaj.tjsp.jus.br/cpopg/show.do?processo.codigo=1",
            new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task Returns_the_details_and_the_movements_of_a_case_by_its_formatted_number()
    {
        factory.CaseDetailReader
            .Setup(reader => reader.GetAsync("10000012320248260100", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Available);

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/cases/1000001-23.2024.8.26.0100");

        Assert.Equal("1000001-23.2024.8.26.0100", body.GetProperty("caseNumber").GetString());
        Assert.Equal("TJSP", body.GetProperty("court").GetString());
        Assert.Equal("First", body.GetProperty("courtLevel").GetString());
        Assert.Equal("available", body.GetProperty("status").GetString());
        var fields = body.GetProperty("fields");
        Assert.Equal("Procedimento Comum Cível", fields.GetProperty("caseClass").GetString());
        Assert.Equal("Planos de Saúde", Assert.Single(fields.GetProperty("subjects").EnumerateArray()).GetString());
        Assert.Equal(JsonValueKind.Null, fields.GetProperty("judge").ValueKind);
        Assert.Equal("2024-01-10", fields.GetProperty("filedAt").GetString());
        Assert.Equal("judge", Assert.Single(body.GetProperty("missingFields").EnumerateArray()).GetString());
        var movement = Assert.Single(body.GetProperty("movements").EnumerateArray());
        Assert.Equal("2025-03-05", movement.GetProperty("date").GetString());
        Assert.Equal("Julgada procedente a ação", movement.GetProperty("description").GetString());
        Assert.Equal("2026-11-20T10:00:00+00:00", body.GetProperty("source").GetProperty("extractedAt").GetString());
    }

    [Fact]
    public async Task Returns_only_the_court_and_the_reason_for_a_court_without_a_source()
    {
        factory.CaseDetailReader
            .Setup(reader => reader.GetAsync("00000011220248190001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CaseDetail(
                null,
                "TJRJ",
                null,
                CaseAvailability.SourceUnavailable,
                "Os dados deste processo não estão disponíveis no Ratio: a consulta pública do TJRJ só pode ser feita com captcha.",
                null,
                [],
                [],
                null));

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/cases/00000011220248190001");

        Assert.Equal("sourceUnavailable", body.GetProperty("status").GetString());
        Assert.Equal("TJRJ", body.GetProperty("court").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("caseNumber").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("fields").ValueKind);
        Assert.Empty(body.GetProperty("movements").EnumerateArray());
    }

    [Theory]
    [InlineData("1000001-23.2024")]
    [InlineData("processo")]
    public async Task Rejects_what_is_not_a_case_number(string number)
    {
        var response = await _client.GetAsync($"/api/cases/{number}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "Informe o número do processo no padrão CNJ, com 20 dígitos.",
            body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Returns_not_found_for_a_case_outside_the_sample()
    {
        factory.CaseDetailReader
            .Setup(reader => reader.GetAsync("99999999999999999999", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CaseDetail?)null);

        var response = await _client.GetAsync("/api/cases/99999999999999999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Processo não encontrado na amostra do Ratio.", body.GetProperty("detail").GetString());
    }
}
