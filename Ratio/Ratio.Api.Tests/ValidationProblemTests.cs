using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ratio.Api.Tests;

public class ValidationProblemTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Binding_error_returns_problem_title_in_portuguese()
    {
        var response = await _client.GetAsync("/test/validation-probe?limit=abc");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Requisição inválida", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Binding_error_describes_the_field_in_portuguese()
    {
        var response = await _client.GetAsync("/test/validation-probe?limit=abc");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var message = body.GetProperty("errors").GetProperty("limit")[0].GetString();

        Assert.Equal("O valor 'abc' não é válido.", message);
    }
}
