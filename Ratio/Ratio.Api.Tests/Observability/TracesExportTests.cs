using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Ratio.Application.Abstractions;

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

    [Fact]
    public async Task Records_a_server_span_with_the_route_for_each_api_request()
    {
        var exported = new List<Activity>();
        using var factory = new ApiFactory();
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        using var configured = factory.WithWebHostBuilder(builder => builder
            .UseSetting("OpenTelemetry:Endpoint", "http://127.0.0.1:4317")
            .ConfigureTestServices(services =>
                services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(exported))));

        var traceId = ActivityTraceId.CreateRandom();
        var client = configured.CreateClient();
        client.DefaultRequestHeaders.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
        await client.GetAsync("/api/themes?q=juros");
        var provider = configured.Services.GetRequiredService<TracerProvider>();

        var span = await WaitForSpanAsync(provider, exported, traceId);

        Assert.NotNull(span);
        Assert.Equal("api/themes", span.GetTagItem("http.route"));
        Assert.Equal(ActivityKind.Server, span.Kind);
        Assert.Contains(provider.GetResource().Attributes, attribute =>
            attribute.Key == "service.name" && attribute.Value as string == "ratio-api");
    }

    private static async Task<Activity?> WaitForSpanAsync(TracerProvider provider, List<Activity> exported, ActivityTraceId traceId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            provider.ForceFlush();
            var found = exported.ToArray().SingleOrDefault(activity => activity.TraceId == traceId);
            if (found is not null)
            {
                return found;
            }

            await Task.Delay(20);
        }

        return null;
    }
}
