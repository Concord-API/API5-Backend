using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ratio.Api.Hosting;

namespace Ratio.Api.Tests.Observability;

public class WindowsServiceLoggingTests
{
    private sealed class EventLogStandIn : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;

        public void Dispose()
        {
        }
    }

    [Fact]
    public void Drops_the_logging_provider_the_Windows_service_registers()
    {
        var builder = WebApplication.CreateBuilder();

        builder.UseRatioLogging(host => host.ConfigureServices(services =>
            services.AddSingleton<ILoggerProvider, EventLogStandIn>()));

        using var app = builder.Build();
        Assert.DoesNotContain(app.Services.GetServices<ILoggerProvider>(), provider => provider is EventLogStandIn);
    }
}
