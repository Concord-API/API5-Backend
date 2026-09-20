using System.Net;

namespace Ratio.Api.Tests;

public class CorsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private Task<HttpResponseMessage> GetHealthFrom(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", origin);
        return _client.SendAsync(request);
    }

    [Fact]
    public async Task Configured_origin_is_allowed()
    {
        var response = await GetHealthFrom("http://localhost:5173");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("http://localhost:5173", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task Unknown_origin_gets_no_cors_header()
    {
        var response = await GetHealthFrom("http://evil.example");

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
