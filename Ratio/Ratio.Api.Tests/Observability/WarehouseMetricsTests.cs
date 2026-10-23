using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Ratio.Application.Abstractions;

namespace Ratio.Api.Tests.Observability;

[CollectionDefinition(nameof(WarehouseMetricsTests), DisableParallelization = true)]
public class WarehouseMetricsCollection;

[Collection(nameof(WarehouseMetricsTests))]
public class WarehouseMetricsTests
{
    private static LastExtraction Extraction(WarehouseAvailability availability) => availability switch
    {
        WarehouseAvailability.Ready => LastExtraction.At(DateTimeOffset.UtcNow.AddHours(-1)),
        WarehouseAvailability.Empty => LastExtraction.Empty,
        _ => LastExtraction.Unavailable
    };

    private static List<MetricSnapshot> ExportAfterStartup(ApiFactory factory, LastExtraction lastExtraction, string? endpoint)
    {
        var exported = new List<MetricSnapshot>();
        factory.LastExtractionReader
            .Setup(reader => reader.GetLastExtractionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(lastExtraction);

        var configured = factory.WithWebHostBuilder(builder =>
        {
            if (endpoint is not null)
            {
                builder.UseSetting("OpenTelemetry:Endpoint", endpoint);
            }

            builder.ConfigureTestServices(services =>
                services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddInMemoryExporter(exported)));
        });

        configured.CreateClient();
        configured.Services.GetService<MeterProvider>()?.ForceFlush();
        return exported;
    }

    private static Dictionary<string, long> Availability(IEnumerable<MetricSnapshot> exported)
    {
        var availability = new Dictionary<string, long>();
        foreach (var point in exported.Where(metric => metric.Name == "ratio.warehouse.availability").SelectMany(metric => metric.MetricPoints))
        {
            foreach (var tag in point.Tags)
            {
                if (tag.Key == "state")
                {
                    availability[(string)tag.Value!] = point.GetGaugeLastValueLong();
                }
            }
        }

        return availability;
    }

    [Fact]
    public void Does_not_query_the_warehouse_for_metrics_without_an_endpoint()
    {
        using var factory = new ApiFactory();

        ExportAfterStartup(factory, Extraction(WarehouseAvailability.Ready), endpoint: null);

        factory.LastExtractionReader.Verify(
            reader => reader.GetLastExtractionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(WarehouseAvailability.Ready, "ready")]
    [InlineData(WarehouseAvailability.Empty, "empty")]
    [InlineData(WarehouseAvailability.Unavailable, "unavailable")]
    public void Exports_the_current_warehouse_availability(WarehouseAvailability availability, string state)
    {
        using var factory = new ApiFactory();

        var exported = ExportAfterStartup(factory, Extraction(availability), "http://127.0.0.1:4317");

        var expected = new Dictionary<string, long> { ["ready"] = 0, ["empty"] = 0, ["unavailable"] = 0 };
        expected[state] = 1;
        Assert.Equal(expected, Availability(exported));
    }
}
