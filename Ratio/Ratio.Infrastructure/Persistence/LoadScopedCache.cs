namespace Ratio.Infrastructure.Persistence;

public sealed class LoadScopedCache<T>(PublishedLoadVersion publishedLoad)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Entry? _entry;

    public async Task<T> GetAsync(Func<CancellationToken, Task<T>> load, CancellationToken cancellationToken)
    {
        var version = await publishedLoad.CurrentAsync(cancellationToken);
        if (version is null)
        {
            return await load(cancellationToken);
        }

        if (_entry is { } cached && cached.Version == version)
        {
            return cached.Value;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_entry is { } again && again.Version == version)
            {
                return again.Value;
            }

            var value = await load(cancellationToken);
            _entry = new Entry(version.Value, value);
            return value;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record Entry(DateTimeOffset Version, T Value);
}
