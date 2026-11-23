using Ratio.Application.Abstractions;

namespace Ratio.Infrastructure.Tests.Persistence;

internal sealed class FakeLastExtractionReader : ILastExtractionReader
{
    public LastExtraction Next { get; set; } = LastExtraction.Empty;

    public int Calls { get; private set; }

    public Task<LastExtraction> GetLastExtractionAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(Next);
    }
}
