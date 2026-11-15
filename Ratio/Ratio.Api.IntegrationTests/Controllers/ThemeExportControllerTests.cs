using System.Net.Http.Json;
using System.Text.Json;

namespace Ratio.Api.IntegrationTests.Controllers;

public class ThemeExportControllerTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private RatioApiFactory _api = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _api = new RatioApiFactory(await postgres.CreateDatabaseAsync());
        _client = _api.CreateClient();
        await _api.LoadSampleAsync();
    }

    public Task DisposeAsync() => _api.DisposeAsync().AsTask();

    private async Task<string[]> ScreenCaseNumbersAsync(long themeKey)
    {
        var sample = await _client.GetFromJsonAsync<JsonElement>($"/api/themes/{themeKey}/cases?limit=100");

        return sample.GetProperty("cases").EnumerateArray()
            .Select(sampleCase => sampleCase.GetProperty("caseNumber").GetString() ?? "")
            .ToArray();
    }

    private async Task<string[]> FileCaseNumbersAsync(long themeKey)
    {
        var csv = await _client.GetStringAsync($"/api/themes/{themeKey}/export?format=csv");

        return csv.Split("\r\n\r\n", 2)[1]
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(line => line.Split(';')[0])
            .ToArray();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Exports_the_same_cases_in_the_same_order_as_the_screen(long themeKey)
    {
        var screen = await ScreenCaseNumbersAsync(themeKey);

        Assert.NotEmpty(screen);
        Assert.Equal(screen, await FileCaseNumbersAsync(themeKey));
    }

    [Fact]
    public async Task Exports_as_many_rows_as_the_total_the_screen_declares()
    {
        var sample = await _client.GetFromJsonAsync<JsonElement>("/api/themes/1/cases?limit=100");

        Assert.Equal(sample.GetProperty("total").GetInt64(), (await FileCaseNumbersAsync(1)).Length);
    }
}
