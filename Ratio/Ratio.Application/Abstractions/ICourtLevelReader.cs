namespace Ratio.Application.Abstractions;

public interface ICourtLevelReader
{
    Task<IReadOnlyList<string>> GetAsync(CancellationToken cancellationToken);
}
