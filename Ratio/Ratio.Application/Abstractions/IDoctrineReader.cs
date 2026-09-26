namespace Ratio.Application.Abstractions;

public interface IDoctrineReader
{
    Task<IReadOnlyList<DoctrineEntry>> GetRelatedAsync(long themeKey, int limit, CancellationToken cancellationToken);
}
