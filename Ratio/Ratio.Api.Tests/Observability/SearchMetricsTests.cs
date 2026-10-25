using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Ratio.Application.Abstractions;
using Ratio.Application.Filters;

namespace Ratio.Api.Tests.Observability;

[CollectionDefinition(nameof(SearchMetricsTests), DisableParallelization = true)]
public class SearchMetricsCollection;

[Collection(nameof(SearchMetricsTests))]
public class SearchMetricsTests
{
    private const string EmptyResults = "ratio.search.empty_results";

    private static readonly ThemeSummary WrongfulListing = new(
        412,
        "Inscrição indevida em cadastro de inadimplentes",
        "CONSUMIDOR",
        144,
        78,
        "Dominante",
        new ThemeOutcome(142, 2, 0.9861m, "acolhimento da pretensão do autor"),
        2021,
        2026,
        new DateOnly(2026, 8, 30));

    private static async Task<List<MetricSnapshot>> ExportAfterSearching(ApiFactory factory, string url)
    {
        var exported = new List<MetricSnapshot>();
        using var configured = factory.WithWebHostBuilder(builder => builder
            .UseSetting("OpenTelemetry:Endpoint", "http://127.0.0.1:4317")
            .ConfigureTestServices(services =>
                services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddInMemoryExporter(exported))));

        await configured.CreateClient().GetAsync(url);
        configured.Services.GetRequiredService<MeterProvider>().ForceFlush();
        return [.. exported];
    }

    private static List<MetricPoint> EmptyResultPoints(IEnumerable<MetricSnapshot> exported) =>
        exported.Where(metric => metric.Name == EmptyResults).SelectMany(metric => metric.MetricPoints).ToList();

    private static long EmptyResultCount(IEnumerable<MetricSnapshot> exported) =>
        EmptyResultPoints(exported).Sum(point => point.GetSumLong());

    [Fact]
    public async Task Counts_a_search_with_no_result()
    {
        using var factory = new ApiFactory();
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync("satelite", 20, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var exported = await ExportAfterSearching(factory, "/api/themes?q=satelite");

        Assert.Equal(1, EmptyResultCount(exported));
    }

    [Fact]
    public async Task Does_not_record_the_query_text_in_the_search_metric()
    {
        using var factory = new ApiFactory();
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync("satelite", 20, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var exported = await ExportAfterSearching(factory, "/api/themes?q=satelite");

        var point = Assert.Single(EmptyResultPoints(exported));
        Assert.Equal(0, point.Tags.Count);
    }

    [Fact]
    public async Task Does_not_count_a_search_with_results()
    {
        using var factory = new ApiFactory();
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync("inscricao", 20, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([WrongfulListing]);

        var exported = await ExportAfterSearching(factory, "/api/themes?q=inscricao");

        Assert.Equal(0, EmptyResultCount(exported));
    }

    [Fact]
    public async Task Does_not_count_the_top_themes_without_a_query()
    {
        using var factory = new ApiFactory();
        factory.ThemeSearchReader
            .Setup(reader => reader.TopThemesAsync(20, It.IsAny<ThemeFilters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var exported = await ExportAfterSearching(factory, "/api/themes");

        Assert.Equal(0, EmptyResultCount(exported));
    }
}
