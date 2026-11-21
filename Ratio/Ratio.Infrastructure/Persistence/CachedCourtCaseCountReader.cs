using Ratio.Application.Abstractions;

namespace Ratio.Infrastructure.Persistence;

public sealed class CachedCourtCaseCountReader(CourtCaseCountReader reader, PublishedLoadVersion publishedLoad)
    : ICourtCaseCountReader
{
    private readonly LoadScopedCache<IReadOnlyList<CourtCaseCount>> _cache = new(publishedLoad);

    public Task<IReadOnlyList<CourtCaseCount>> GetAsync(CancellationToken cancellationToken) =>
        _cache.GetAsync(reader.GetAsync, cancellationToken);
}
