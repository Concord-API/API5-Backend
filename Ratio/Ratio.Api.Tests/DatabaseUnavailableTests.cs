using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using Npgsql;

namespace Ratio.Api.Tests;

public class DatabaseUnavailableTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private void ThemeReaderThrows(Exception exception) =>
        factory.ThemeDetailReader
            .Setup(reader => reader.GetThemeAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

    [Fact]
    public async Task Answers_503_with_retry_after_when_the_database_times_out()
    {
        ThemeReaderThrows(new NpgsqlException("Exception while reading from stream", new TimeoutException()));

        var response = await _client.GetAsync("/api/themes/1");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(5), response.Headers.RetryAfter?.Delta);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Serviço indisponível", body.GetProperty("title").GetString());
        Assert.Equal(
            "O banco de dados está ocupado. Tente de novo em alguns segundos.",
            body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Keeps_answering_500_when_the_database_error_is_not_temporary()
    {
        ThemeReaderThrows(new NpgsqlException("syntax error"));

        var response = await _client.GetAsync("/api/themes/1");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}
