namespace Ratio.Application.Abstractions;

public interface IProvenanceReader
{
    Task<LoadedProvenance> GetGlobalAsync(CancellationToken cancellationToken);

    Task<LoadedProvenance> GetThemeAsync(long themeKey, CancellationToken cancellationToken);
}
