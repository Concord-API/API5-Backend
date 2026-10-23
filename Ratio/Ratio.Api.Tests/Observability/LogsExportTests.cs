using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Ratio.Api.Tests.Observability;

[CollectionDefinition(nameof(LogsExportTests), DisableParallelization = true)]
public class LogsExportCollection;

[Collection(nameof(LogsExportTests))]
public class LogsExportTests
{
    private sealed record ExportedLog(string Category, string? Message, IReadOnlyDictionary<string, object?> Attributes);

    private sealed class CapturingProcessor : BaseProcessor<LogRecord>
    {
        public ConcurrentQueue<ExportedLog> Logs { get; } = new();

        public override void OnEnd(LogRecord data) =>
            Logs.Enqueue(new ExportedLog(
                data.CategoryName ?? "",
                data.FormattedMessage,
                (data.Attributes ?? []).ToDictionary(attribute => attribute.Key, attribute => attribute.Value)));
    }

    private static WebApplicationFactory<Program> Exporting(
        ApiFactory factory, CapturingProcessor processor, string? endpoint) =>
        factory.WithWebHostBuilder(builder =>
        {
            if (endpoint is not null)
            {
                builder.UseSetting("OpenTelemetry:Endpoint", endpoint);
            }

            builder.ConfigureTestServices(services =>
                services.ConfigureOpenTelemetryLoggerProvider(logging => logging.AddProcessor(processor)));
        });

    private static void LogPublishedLoad(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<ILogger<LogsExportTests>>()
            .LogInformation("Carga {Source} publicada", "datajud");

    private static bool IsPublishedLoad(ExportedLog log) =>
        log.Category == typeof(LogsExportTests).FullName;

    [Fact]
    public void Exports_no_logs_without_an_endpoint()
    {
        using var processor = new CapturingProcessor();
        using var factory = new ApiFactory();
        using var configured = Exporting(factory, processor, endpoint: null);

        LogPublishedLoad(configured);

        Assert.Empty(processor.Logs);
    }

    [Fact]
    public void Exports_the_api_logs_with_their_properties_when_an_endpoint_is_configured()
    {
        using var processor = new CapturingProcessor();
        using var factory = new ApiFactory();
        using var configured = Exporting(factory, processor, "http://127.0.0.1:4317");

        LogPublishedLoad(configured);

        var log = Assert.Single(processor.Logs, IsPublishedLoad);
        Assert.Equal("datajud", log.Attributes["Source"]);
    }
}
