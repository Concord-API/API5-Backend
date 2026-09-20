namespace Ratio.Application.Abstractions;

public interface ILastExtractionReader
{
    Task<LastExtraction> GetLastExtractionAsync(CancellationToken cancellationToken);
}
