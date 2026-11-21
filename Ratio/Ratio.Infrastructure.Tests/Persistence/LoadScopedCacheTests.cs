using Ratio.Application.Abstractions;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Persistence;

public class LoadScopedCacheTests
{
    private static readonly DateTimeOffset FirstLoad = new(2026, 11, 1, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondLoad = new(2026, 11, 20, 3, 0, 0, TimeSpan.Zero);

    private readonly FakeLastExtractionReader _reader = new();
    private readonly FakeTimeProvider _time = new();
    private int _loads;

    private LoadScopedCache<int> CreateCache() => new(new PublishedLoadVersion(_reader, _time));

    private Task<int> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Interlocked.Increment(ref _loads));

    [Fact]
    public async Task Loads_once_for_the_same_published_load()
    {
        _reader.Next = LastExtraction.At(FirstLoad);
        var cache = CreateCache();

        var first = await cache.GetAsync(LoadAsync, CancellationToken.None);
        var second = await cache.GetAsync(LoadAsync, CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(1, second);
        Assert.Equal(1, _loads);
    }

    [Fact]
    public async Task Loads_again_when_a_new_load_is_published()
    {
        _reader.Next = LastExtraction.At(FirstLoad);
        var cache = CreateCache();
        await cache.GetAsync(LoadAsync, CancellationToken.None);

        _reader.Next = LastExtraction.At(SecondLoad);
        _time.Advance(TimeSpan.FromMinutes(1));
        var value = await cache.GetAsync(LoadAsync, CancellationToken.None);

        Assert.Equal(2, value);
        Assert.Equal(2, _loads);
    }

    [Fact]
    public async Task Does_not_keep_the_value_while_no_load_is_published()
    {
        _reader.Next = LastExtraction.Empty;
        var cache = CreateCache();

        await cache.GetAsync(LoadAsync, CancellationToken.None);
        await cache.GetAsync(LoadAsync, CancellationToken.None);

        Assert.Equal(2, _loads);
    }

    [Fact]
    public async Task Loads_once_when_requests_arrive_together()
    {
        _reader.Next = LastExtraction.At(FirstLoad);
        var cache = CreateCache();
        var release = new TaskCompletionSource();

        async Task<int> SlowLoadAsync(CancellationToken cancellationToken)
        {
            await release.Task;
            return Interlocked.Increment(ref _loads);
        }

        var requests = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => cache.GetAsync(SlowLoadAsync, CancellationToken.None)))
            .ToArray();
        await Task.Delay(100);
        release.SetResult();
        var values = await Task.WhenAll(requests);

        Assert.Equal(1, _loads);
        Assert.All(values, value => Assert.Equal(1, value));
    }
}
