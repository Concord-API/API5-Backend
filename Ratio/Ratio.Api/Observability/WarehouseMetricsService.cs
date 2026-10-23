using Ratio.Application.Abstractions;

namespace Ratio.Api.Observability;

public sealed class WarehouseMetricsService(ILastExtractionReader reader, WarehouseMetrics metrics) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            metrics.Record(await reader.GetLastExtractionAsync(stoppingToken));
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
