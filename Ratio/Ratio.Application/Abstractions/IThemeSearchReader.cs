namespace Ratio.Application.Abstractions;

public interface IThemeSearchReader
{
    Task<IReadOnlyList<ThemeSummary>> SearchThemesAsync(string? query, int limit, CancellationToken cancellationToken);
}
