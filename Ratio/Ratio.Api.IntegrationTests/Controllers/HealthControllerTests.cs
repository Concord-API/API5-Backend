using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ratio.Api.IntegrationTests.Controllers;

public class HealthControllerTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private RatioApiFactory _api = null!;

    public async Task InitializeAsync() => _api = new RatioApiFactory(await postgres.CreateDatabaseAsync());

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    [Fact]
    public async Task Reports_no_published_load_on_a_freshly_migrated_database()
    {
        var response = await _api.CreateClient().GetAsync("/health/ready");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("sem carga publicada", body.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Reports_ready_after_the_sample_load()
    {
        var client = _api.CreateClient();
        await _api.LoadSampleAsync();

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Pronto", body.GetProperty("status").GetString());
    }
}
