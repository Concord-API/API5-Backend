using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Ratio.Api.IntegrationTests.Observability;

public class DatabaseTracesTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string SearchedText = "cobranca";

    private readonly List<Activity> _exported = [];
    private RatioApiFactory _api = null!;
    private WebApplicationFactory<Program> _configured = null!;

    public async Task InitializeAsync()
    {
        _api = new RatioApiFactory(await postgres.CreateDatabaseAsync());
        _configured = _api.WithWebHostBuilder(builder => builder
            .UseSetting("OpenTelemetry:Endpoint", "http://127.0.0.1:4317")
            .ConfigureTestServices(services =>
                services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(_exported))));
        _configured.CreateClient();
        await _api.LoadSampleAsync();
    }

    public async Task DisposeAsync()
    {
        await _configured.DisposeAsync();
        await _api.DisposeAsync();
    }

    [Fact]
    public async Task Traces_each_database_command_under_the_request_span()
    {
        var spans = await SearchWithTraceAsync();

        var request = Assert.Single(spans, span => span.Kind == ActivityKind.Server);
        var commands = spans.Where(span => span.Source.Name == "Npgsql").ToList();
        Assert.NotEmpty(commands);
        Assert.All(commands, command =>
        {
            Assert.Equal(ActivityKind.Client, command.Kind);
            Assert.Equal(request.SpanId, command.ParentSpanId);
        });
        Assert.Contains(commands, command =>
            command.TagObjects.Any(tag => tag.Value?.ToString()?.Contains("search_themes") == true));
    }

    [Fact]
    public async Task Keeps_the_search_text_out_of_the_database_spans()
    {
        var spans = await SearchWithTraceAsync();

        Assert.Contains(spans, span => span.Source.Name == "Npgsql");
        Assert.All(spans, span =>
            Assert.DoesNotContain(span.TagObjects, tag => tag.Value?.ToString()?.Contains(SearchedText) == true));
    }

    [Fact]
    public async Task Drops_the_database_commands_that_no_request_started()
    {
        await SearchWithTraceAsync();

        Assert.DoesNotContain(_exported.ToArray(), span =>
            span.Source.Name == "Npgsql" && span.ParentSpanId == default);
    }

    private async Task<List<Activity>> SearchWithTraceAsync()
    {
        var traceId = ActivityTraceId.CreateRandom();
        var client = _configured.CreateClient();
        client.DefaultRequestHeaders.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
        await client.GetAsync($"/api/themes?q={SearchedText}%20de%20divida");

        var provider = _configured.Services.GetRequiredService<TracerProvider>();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            provider.ForceFlush();
            var spans = _exported.ToArray().Where(span => span.TraceId == traceId).ToList();
            if (spans.Any(span => span.Kind == ActivityKind.Server))
            {
                return spans;
            }

            await Task.Delay(20);
        }

        return [];
    }
}
