using System.Diagnostics.Metrics;

namespace Ratio.Api.Observability;

public sealed class SearchMetrics
{
    private readonly Counter<long> _emptyResults;

    public SearchMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(WarehouseMetrics.MeterName);
        _emptyResults = meter.CreateCounter<long>(
            "ratio.search.empty_results",
            description: "Searches that returned no theme. The query text is never recorded.");
    }

    public void RecordEmptyResult() => _emptyResults.Add(1);
}
