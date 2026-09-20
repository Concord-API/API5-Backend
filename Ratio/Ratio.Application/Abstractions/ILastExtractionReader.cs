namespace Ratio.Application.Abstractions;

public interface ILastExtractionReader
{
    Task<DateTimeOffset?> GetLastExtractionAsync(CancellationToken cancellationToken);
}
