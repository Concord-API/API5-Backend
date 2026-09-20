using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ratio.Api.Tests;

public class HostingConfigurationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void Api_listens_only_on_loopback_behind_the_proxy()
    {
        var configuration = factory.Services.GetRequiredService<IConfiguration>();

        var url = configuration["Kestrel:Endpoints:Http:Url"];

        Assert.StartsWith("http://127.0.0.1:", url);
    }
}
