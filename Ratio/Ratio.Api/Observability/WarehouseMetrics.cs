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

    private sealed record Snapshot(LastExtraction LastExtraction);
}
