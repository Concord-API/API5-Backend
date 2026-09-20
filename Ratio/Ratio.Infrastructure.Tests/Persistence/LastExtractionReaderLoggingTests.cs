using Microsoft.Extensions.Logging;
using Npgsql;
using Ratio.Application.Abstractions;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Persistence;

public class LastExtractionReaderLoggingTests
{
    private static NpgsqlDataSource UnreachableDatabase() =>
        NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=ratio;Username=ratio_api;Password=x;Timeout=2");

    [Fact]
    public async Task Logs_the_error_when_the_database_is_unreachable()
    {
        var logger = new CapturingLogger<LastExtractionReader>();
        await using var dataSource = UnreachableDatabase();
        var reader = new LastExtractionReader(dataSource, logger);

        var result = await reader.GetLastExtractionAsync(CancellationToken.None);

        Assert.Equal(WarehouseAvailability.Unavailable, result.Availability);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.NotNull(entry.Exception);
    }
}

public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, exception, formatter(state, exception)));
}
