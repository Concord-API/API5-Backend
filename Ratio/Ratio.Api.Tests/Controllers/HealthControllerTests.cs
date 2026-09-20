using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using Ratio.Application.Abstractions;

namespace Ratio.Api.Tests.Controllers;

public class HealthControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_returns_ok_with_portuguese_body()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Saudável", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_reports_the_last_extraction_when_data_is_loaded()
    {
        var extractedAt = new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
        factory.LastExtractionReader
            .Setup(r => r.GetLastExtractionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(LastExtraction.At(extractedAt));

        var response = await _client.GetAsync("/health/ready");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Pronto", body.GetProperty("status").GetString());
        Assert.Equal(extractedAt, body.GetProperty("lastExtractionAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Ready_says_the_warehouse_is_empty_when_no_load_was_published()
    {
        factory.LastExtractionReader
            .Setup(r => r.GetLastExtractionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(LastExtraction.Empty);

        var response = await _client.GetAsync("/health/ready");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Não está pronto", body.GetProperty("title").GetString());
        Assert.Equal("sem carga publicada", body.GetProperty("reason").GetString());
        Assert.Contains("não há carga publicada", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Ready_says_the_database_is_unreachable_when_the_query_fails()
    {
        factory.LastExtractionReader
            .Setup(r => r.GetLastExtractionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(LastExtraction.Unavailable);

        var response = await _client.GetAsync("/health/ready");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Não está pronto", body.GetProperty("title").GetString());
        Assert.Equal("banco inacessível", body.GetProperty("reason").GetString());
        Assert.Contains("log da aplicação", body.GetProperty("detail").GetString());
    }
}
