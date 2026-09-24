using System.Net.Http.Json;
using System.Text.Json;

namespace Ratio.Api.Tests;

public class UnavailableContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<JsonElement[]> UnavailableAsync()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/test/unavailable-probe");
        return body.GetProperty("unavailable").EnumerateArray().ToArray();
    }

    [Fact]
    public async Task Writes_the_block_the_reason_and_the_message()
    {
        var first = (await UnavailableAsync())[0];

        Assert.Equal("reporterJudge", first.GetProperty("block").GetString());
        Assert.Equal("sourceUnavailable", first.GetProperty("reason").GetString());
        Assert.Equal("O DataJud não publica o relator.", first.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Writes_each_of_the_three_reasons_with_its_contract_name()
    {
        var reasons = (await UnavailableAsync()).Select(item => item.GetProperty("reason").GetString());

        Assert.Equal(["sourceUnavailable", "notLoaded", "notApplicable"], reasons);
    }
}
