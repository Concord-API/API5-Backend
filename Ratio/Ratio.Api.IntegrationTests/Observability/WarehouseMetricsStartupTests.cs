using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ratio.Api.IntegrationTests.Observability;

[CollectionDefinition(nameof(WarehouseMetricsStartupTests), DisableParallelization = true)]
public class WarehouseMetricsStartupCollection;

[Collection(nameof(WarehouseMetricsStartupTests))]
public class WarehouseMetricsStartupTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }

    [Fact]
    public async Task Reads_the_warehouse_metrics_only_after_the_migration_on_a_fresh_database()
    {
        var logs = new CapturingLoggerProvider();
        await using var api = new RatioApiFactory(await postgres.CreateDatabaseAsync());
        await using var configured = api.WithWebHostBuilder(builder => builder
            .UseSetting("OpenTelemetry:Endpoint", "http://127.0.0.1:4317")
            .ConfigureTestServices(services => services.AddSingleton<ILoggerProvider>(logs)));

        configured.CreateClient();
        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Contains(logs.Entries, entry => entry.Message.Contains("Migration aplicada"));
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains("O schema dw não existe"));
    }
}
