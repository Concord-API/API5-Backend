using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using Ratio.Application.Abstractions;

namespace Ratio.Api.Tests.Observability;

[CollectionDefinition(nameof(TelemetryContractTests), DisableParallelization = true)]
public class TelemetryContractCollection;

[Collection(nameof(TelemetryContractTests))]
public class TelemetryContractTests
{
    private const string Endpoint = "http://127.0.0.1:4317";

    private const string SearchedText = "satelite";

    public static TheoryData<string, string, MetricType, string[], bool> MetricsContract => new()
    {
        { "http.server.request.duration", "s", MetricType.Histogram, ["http.request.method", "http.route", "http.response.status_code"], false },
        { "ratio.warehouse.availability", "", MetricType.LongGauge, ["state"], true },
        { "ratio.warehouse.last_extraction.age", "s", MetricType.DoubleGauge, [], true },
        { "ratio.search.empty_results", "", MetricType.LongSum, [], true }
    };

    private static WebApplicationFactory<Program> Configured(
        ApiFactory factory, List<MetricSnapshot> exported, CapturingLogProcessor? logs = null)
    {
        factory.LastExtractionReader
            .Setup(reader => reader.GetLastExtractionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(LastExtraction.At(DateTimeOffset.UtcNow.AddHours(-1)));
        factory.ThemeSearchReader
            .Setup(reader => reader.SearchThemesAsync(SearchedText, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        return factory.WithWebHostBuilder(builder => builder
            .UseSetting("OpenTelemetry:Endpoint", Endpoint)
            .ConfigureTestServices(services =>
            {
                services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddInMemoryExporter(exported));
                if (logs is not null)
                {
                    services.ConfigureOpenTelemetryLoggerProvider(logging => logging.AddProcessor(logs));
                }
            }));
    }

    private static async Task<ExportedLog?> WaitForAsync(CapturingLogProcessor logs, Func<ExportedLog, bool> match)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var found = logs.Logs.FirstOrDefault(match);
            if (found is not null)
            {
                return found;
            }

            await Task.Delay(20);
        }

        return null;
    }

    private static async Task<List<MetricSnapshot>> ExportAfterUsingTheApi()
    {
        var exported = new List<MetricSnapshot>();
        using var factory = new ApiFactory();
        using var configured = Configured(factory, exported);
        var client = configured.CreateClient();

        await client.GetAsync("/health");
        await client.GetAsync($"/api/themes?q={SearchedText}");
        configured.Services.GetRequiredService<MeterProvider>().ForceFlush();
        return [.. exported];
    }

    private static HashSet<string> AttributeKeys(MetricSnapshot metric)
    {
        var keys = new HashSet<string>();
        foreach (var point in metric.MetricPoints)
        {
            foreach (var tag in point.Tags)
            {
                keys.Add(tag.Key);
            }
        }

        return keys;
    }

    [Theory]
    [MemberData(nameof(MetricsContract))]
    public async Task Exports_each_metric_with_the_name_unit_type_and_attributes_the_dashboards_use(
        string name, string unit, MetricType type, string[] attributes, bool exactAttributes)
    {
        var exported = await ExportAfterUsingTheApi();

        var metric = exported.FirstOrDefault(snapshot => snapshot.Name == name);
        Assert.True(metric is not null, $"A métrica '{name}' não foi exportada.");
        Assert.Equal(unit, metric.Unit ?? "");
        Assert.Equal(type, metric.MetricType);
        var keys = AttributeKeys(metric);
        if (exactAttributes)
        {
            Assert.Equal(attributes.ToHashSet(), keys);
        }
        else
        {
            Assert.Superset(attributes.ToHashSet(), keys);
        }
    }

    [Fact]
    public void Exports_the_metrics_as_the_ratio_api_service()
    {
        using var factory = new ApiFactory();
        using var configured = Configured(factory, []);

        var resource = configured.Services.GetRequiredService<MeterProvider>().GetResource();

        Assert.Contains(resource.Attributes, attribute => attribute is { Key: "service.name", Value: "ratio-api" });
    }

    [Fact]
    public void Exports_the_logs_as_the_ratio_api_service()
    {
        using var logs = new CapturingLogProcessor();
        using var factory = new ApiFactory();
        using var configured = Configured(factory, [], logs);

        var resource = configured.Services.GetRequiredService<LoggerProvider>().GetResource();

        Assert.Contains(resource.Attributes, attribute => attribute is { Key: "service.name", Value: "ratio-api" });
    }

    [Fact]
    public void Exports_each_log_with_its_level_category_formatted_message_and_properties()
    {
        using var logs = new CapturingLogProcessor();
        using var factory = new ApiFactory();
        using var configured = Configured(factory, [], logs);

        configured.Services.GetRequiredService<ILogger<TelemetryContractTests>>()
            .LogWarning("Carga {Source} atrasada", "datajud");

        var log = Assert.Single(logs.Logs, entry => entry.Category == typeof(TelemetryContractTests).FullName);
        Assert.Equal(LogLevel.Warning, log.Level);
        Assert.Contains("datajud", log.Message);
        Assert.Contains("atrasada", log.Message);
        Assert.Equal("datajud", log.Attributes["Source"]);
    }

    [Fact]
    public async Task Keeps_the_search_text_out_of_the_metrics_and_logs()
    {
        var exported = new List<MetricSnapshot>();
        using var logs = new CapturingLogProcessor();
        using var factory = new ApiFactory();
        using var configured = Configured(factory, exported, logs);

        await configured.CreateClient().GetAsync($"/api/themes?q={SearchedText}");
        var requestLog = await WaitForAsync(logs, log =>
            log.Attributes.TryGetValue("RequestPath", out var path) && Equals(path, "/api/themes"));
        configured.Services.GetRequiredService<MeterProvider>().ForceFlush();

        Assert.NotNull(requestLog);
        var metricTags = exported.SelectMany(metric => metric.MetricPoints).SelectMany(point =>
        {
            var tags = new List<KeyValuePair<string, object?>>();
            foreach (var tag in point.Tags)
            {
                tags.Add(tag);
            }

            return tags;
        }).ToList();
        Assert.DoesNotContain(metricTags, tag => tag.Key == "url.query");
        Assert.DoesNotContain(metricTags, tag => tag.Value?.ToString()?.Contains(SearchedText) == true);
        Assert.DoesNotContain(logs.Logs, log =>
            log.Message?.Contains(SearchedText) == true
            || log.Attributes.Values.Any(value => value?.ToString()?.Contains(SearchedText) == true));
    }
}
