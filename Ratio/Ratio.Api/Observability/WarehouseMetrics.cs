using System.Diagnostics.Metrics;
using Ratio.Application.Abstractions;

namespace Ratio.Api.Observability;

public sealed class WarehouseMetrics
{
    public const string MeterName = "Ratio.Api";

    private static readonly (WarehouseAvailability Availability, string State)[] States =
    [
        (WarehouseAvailability.Ready, "ready"),
        (WarehouseAvailability.Empty, "empty"),
        (WarehouseAvailability.Unavailable, "unavailable")
    ];

    private volatile Snapshot? _current;

    public WarehouseMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        meter.CreateObservableGauge(
            "ratio.warehouse.availability",
            ObserveAvailability,
            description: "Current warehouse availability, one point per state with 1 for the current one.");
        meter.CreateObservableGauge(
            "ratio.warehouse.last_extraction.age",
            ObserveLastExtractionAge,
            unit: "s",
            description: "Seconds since the last extraction published in the warehouse.");
    }

    public void Record(LastExtraction lastExtraction) => _current = new Snapshot(lastExtraction);

    private IEnumerable<Measurement<int>> ObserveAvailability()
    {
        if (_current is not { } current)
        {
            return [];
        }

        return States.Select(state => new Measurement<int>(
            state.Availability == current.LastExtraction.Availability ? 1 : 0,
            new KeyValuePair<string, object?>("state", state.State)));
    }

    private IEnumerable<Measurement<double>> ObserveLastExtractionAge()
    {
        if (_current?.LastExtraction is not { Availability: WarehouseAvailability.Ready, ExtractedAt: { } extractedAt })
        {
            return [];
        }

        return [new Measurement<double>((DateTimeOffset.UtcNow - extractedAt).TotalSeconds)];
    }

    private sealed record Snapshot(LastExtraction LastExtraction);
}
