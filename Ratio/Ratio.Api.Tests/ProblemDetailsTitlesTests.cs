using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;

namespace Ratio.Api.Tests;

public class ProblemDetailsTitlesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Unknown_route_returns_not_found_problem_in_portuguese()
    {
        var response = await _client.GetAsync("/rota-inexistente");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Recurso não encontrado", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Unhandled_exception_returns_internal_error_problem_in_portuguese()
    {
        factory.LastExtractionReader
            .Setup(r => r.GetLastExtractionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var response = await _client.GetAsync("/health/ready");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("Erro interno do servidor", body.GetProperty("title").GetString());
    }
}
