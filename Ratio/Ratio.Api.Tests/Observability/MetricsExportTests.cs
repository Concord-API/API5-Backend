using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace Ratio.Api.Tests.Observability;

public class MetricsExportTests
{
    private static WebApplicationFactory<Program> WithEndpoint(ApiFactory factory, string endpoint) =>
        factory.WithWebHostBuilder(builder => builder.UseSetting("OpenTelemetry:Endpoint", endpoint));

    [Fact]
    public void Exports_no_metrics_without_an_endpoint()
    {
        using var factory = new ApiFactory();

        Assert.Null(factory.Services.GetService<MeterProvider>());
    }

    [Fact]
    public void Exports_metrics_when_an_endpoint_is_configured()
    {
        using var factory = new ApiFactory();
        using var configured = WithEndpoint(factory, "http://127.0.0.1:4317");

        Assert.NotNull(configured.Services.GetService<MeterProvider>());
    }
}
