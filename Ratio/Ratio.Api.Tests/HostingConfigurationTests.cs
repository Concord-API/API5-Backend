using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ratio.Api.Tests;

public class HostingConfigurationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void Shipped_configuration_binds_kestrel_to_loopback_only()
    {
        var contentRoot = factory.Services.GetRequiredService<IHostEnvironment>().ContentRootPath;
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(contentRoot, "appsettings.json")));

        var url = settings.RootElement
            .GetProperty("Kestrel").GetProperty("Endpoints").GetProperty("Http")
            .GetProperty("Url").GetString();

        Assert.True(
            Uri.TryCreate(url, UriKind.Absolute, out var endpoint)
                && IPAddress.TryParse(endpoint.Host, out var address)
                && IPAddress.IsLoopback(address),
            $"Kestrel precisa escutar só em loopback; o pacote está configurado para '{url}'.");
    }
}
