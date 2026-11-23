using Ratio.Application.Abstractions;

namespace Ratio.Infrastructure.Persistence;

public sealed class CachedCourtLevelReader(CourtLevelReader reader, PublishedLoadVersion publishedLoad)
    : ICourtLevelReader
{
    private readonly LoadScopedCache<IReadOnlyList<string>> _cache = new(publishedLoad);

    public Task<IReadOnlyList<string>> GetAsync(CancellationToken cancellationToken) =>
        _cache.GetAsync(reader.GetAsync, cancellationToken);
}
