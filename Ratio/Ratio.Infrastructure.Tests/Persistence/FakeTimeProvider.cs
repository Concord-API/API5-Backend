namespace Ratio.Infrastructure.Tests.Persistence;

internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
