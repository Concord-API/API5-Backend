namespace Ratio.Application.Abstractions;

public interface ICourtCaseCountReader
{
    Task<IReadOnlyList<CourtCaseCount>> GetAsync(CancellationToken cancellationToken);
}
