using Ratio.Application.Abstractions;

namespace Ratio.Api.Observability;

public sealed class WarehouseMetricsService(
    ILastExtractionReader reader, WarehouseMetrics metrics, ILogger<WarehouseMetricsService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await RefreshAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RefreshAsync(CancellationToken stoppingToken)
    {
        try
        {
            metrics.Record(await reader.GetLastExtractionAsync(stoppingToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Falha ao atualizar as métricas do DW.");
            metrics.Record(LastExtraction.Unavailable);
        }
    }
}
