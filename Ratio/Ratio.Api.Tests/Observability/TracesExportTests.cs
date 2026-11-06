using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    public async Task Leaves_the_health_endpoints_out_of_traces(string path)
    {
        var exported = new List<Activity>();
        using var factory = new ApiFactory();
        factory.LastExtractionReader
            .Setup(reader => reader.GetLastExtractionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(LastExtraction.At(DateTimeOffset.UtcNow.AddHours(-1)));
        using var configured = Configured(factory, exported);

        var traceId = await GetWithTraceAsync(configured, path);
        var provider = configured.Services.GetRequiredService<TracerProvider>();

        Assert.Null(await WaitForSpanAsync(provider, exported, traceId, TimeSpan.FromSeconds(1)));
    }

    private static WebApplicationFactory<Program> Configured(ApiFactory factory, List<Activity> exported) =>
        factory.WithWebHostBuilder(builder => builder
            .UseSetting("OpenTelemetry:Endpoint", "http://127.0.0.1:4317")
            .ConfigureTestServices(services =>
                services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(exported))));

    private static async Task<ActivityTraceId> GetWithTraceAsync(WebApplicationFactory<Program> api, string path)
    {
        var traceId = ActivityTraceId.CreateRandom();
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
        await client.GetAsync(path);
        return traceId;
    }

    private static async Task<Activity?> WaitForSpanAsync(
        TracerProvider provider, List<Activity> exported, ActivityTraceId traceId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
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
