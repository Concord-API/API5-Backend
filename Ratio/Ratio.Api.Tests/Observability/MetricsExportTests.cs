using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
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

    [Fact]
    public void Refuses_to_start_with_an_invalid_endpoint()
    {
        using var factory = new ApiFactory();
        using var configured = WithEndpoint(factory, "não-é-uri");

        var exception = Assert.Throws<InvalidOperationException>(() => configured.CreateClient());

        Assert.Contains("OpenTelemetry__Endpoint", exception.Message);
    }

    [Theory]
    [InlineData("http.server.request.duration")]
    [InlineData("dotnet.process.cpu.count")]
    public async Task Exports_the_http_and_runtime_meters(string metricName)
    {
        var exported = new List<Metric>();
        using var factory = new ApiFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder
            .UseSetting("OpenTelemetry:Endpoint", "http://127.0.0.1:4317")
            .ConfigureTestServices(services =>
                services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddInMemoryExporter(exported))));

        await configured.CreateClient().GetAsync("/health");
        configured.Services.GetRequiredService<MeterProvider>().ForceFlush();

        Assert.Contains(exported, metric => metric.Name == metricName);
    }

    [Fact]
    public async Task Measures_request_durations_up_to_60_seconds()
    {
        var exported = new List<Metric>();
        using var factory = new ApiFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder
            .UseSetting("OpenTelemetry:Endpoint", "http://127.0.0.1:4317")
            .ConfigureTestServices(services =>
                services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddInMemoryExporter(exported))));

        await configured.CreateClient().GetAsync("/health");
        configured.Services.GetRequiredService<MeterProvider>().ForceFlush();

        var duration = exported.First(metric => metric.Name == "http.server.request.duration");
        var bounds = new List<double>();
        foreach (var point in duration.GetMetricPoints())
        {
            foreach (var bucket in point.GetHistogramBuckets())
            {
                bounds.Add(bucket.ExplicitBound);
            }

            break;
        }

        Assert.Equal(
            [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10, 15, 30, 60, double.PositiveInfinity],
            bounds);
    }
}
