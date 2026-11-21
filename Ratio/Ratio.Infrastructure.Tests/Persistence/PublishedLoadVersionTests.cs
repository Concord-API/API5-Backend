using Ratio.Application.Abstractions;
using Ratio.Infrastructure.Persistence;

namespace Ratio.Infrastructure.Tests.Persistence;

public class PublishedLoadVersionTests
{
    private static readonly DateTimeOffset FirstLoad = new(2026, 11, 1, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondLoad = new(2026, 11, 20, 3, 0, 0, TimeSpan.Zero);

    private readonly FakeLastExtractionReader _reader = new();
    private readonly FakeTimeProvider _time = new();

    private PublishedLoadVersion CreateVersion() => new(_reader, _time);

    [Fact]
    public async Task Reads_the_published_load_once_within_a_minute()
    {
        _reader.Next = LastExtraction.At(FirstLoad);
        var version = CreateVersion();

        var first = await version.CurrentAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(59));
        var second = await version.CurrentAsync(CancellationToken.None);

        Assert.Equal(FirstLoad, first);
        Assert.Equal(FirstLoad, second);
        Assert.Equal(1, _reader.Calls);
    }

    [Fact]
    public async Task Reads_the_published_load_again_after_a_minute()
    {
        _reader.Next = LastExtraction.At(FirstLoad);
        var version = CreateVersion();
        await version.CurrentAsync(CancellationToken.None);

        _reader.Next = LastExtraction.At(SecondLoad);
        _time.Advance(TimeSpan.FromSeconds(60));
        var current = await version.CurrentAsync(CancellationToken.None);

        Assert.Equal(SecondLoad, current);
        Assert.Equal(2, _reader.Calls);
    }

    [Fact]
    public async Task Has_no_version_when_no_load_is_published()
    {
        _reader.Next = LastExtraction.Empty;

        Assert.Null(await CreateVersion().CurrentAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Has_no_version_when_the_warehouse_is_unavailable()
    {
        _reader.Next = LastExtraction.Unavailable;

        Assert.Null(await CreateVersion().CurrentAsync(CancellationToken.None));
    }
}
