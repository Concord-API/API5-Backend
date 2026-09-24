namespace Ratio.Application.Abstractions;

public interface IScopeReader
{
    Task<IReadOnlyList<ScopeCourt>> GetCourtsAsync(CancellationToken cancellationToken);
}
