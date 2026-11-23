using Ratio.Application.Abstractions;

namespace Ratio.Infrastructure.Persistence;

public sealed class PublishedLoadVersion(ILastExtractionReader reader, TimeProvider timeProvider)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Snapshot? _snapshot;

    public async Task<DateTimeOffset?> CurrentAsync(CancellationToken cancellationToken)
    {
        if (Fresh(_snapshot) is { } cached)
        {
            return cached.Version;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Fresh(_snapshot) is { } again)
            {
                return again.Version;
            }

            var lastExtraction = await reader.GetLastExtractionAsync(cancellationToken);
            var version = lastExtraction.Availability == WarehouseAvailability.Ready ? lastExtraction.ExtractedAt : null;
            _snapshot = new Snapshot(version, timeProvider.GetUtcNow());
            return version;
        }
        finally
        {
            _gate.Release();
        }
    }

    private Snapshot? Fresh(Snapshot? snapshot) =>
        snapshot is not null && timeProvider.GetUtcNow() - snapshot.ReadAt < MaxAge ? snapshot : null;

    private sealed record Snapshot(DateTimeOffset? Version, DateTimeOffset ReadAt);
}
