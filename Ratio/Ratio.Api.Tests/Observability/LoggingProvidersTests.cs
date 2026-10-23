using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;

namespace Ratio.Api.Tests.Observability;

public class LoggingProvidersTests
{
    [Fact]
    public void Registers_no_logging_provider_without_an_endpoint()
    {
        using var factory = new ApiFactory();

        Assert.Empty(factory.Services.GetServices<ILoggerProvider>());
    }

    [Fact]
    public void Registers_only_the_OpenTelemetry_logging_provider_when_an_endpoint_is_configured()
    {
        using var factory = new ApiFactory();
        using var configured = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("OpenTelemetry:Endpoint", "http://127.0.0.1:4317"));

        var provider = Assert.Single(configured.Services.GetServices<ILoggerProvider>());
        Assert.IsType<OpenTelemetryLoggerProvider>(provider);
    }
}
