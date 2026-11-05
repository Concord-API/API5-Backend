using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace Ratio.Api.Tests.Observability;

public class TracesExportTests
{
    [Fact]
    public void Exports_no_traces_without_an_endpoint()
    {
        using var factory = new ApiFactory();

        Assert.Null(factory.Services.GetService<TracerProvider>());
    }

    [Fact]
    public void Exports_traces_when_an_endpoint_is_configured()
    {
        using var factory = new ApiFactory();
        using var configured = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("OpenTelemetry:Endpoint", "http://127.0.0.1:4317"));

        Assert.NotNull(configured.Services.GetService<TracerProvider>());
    }
}
