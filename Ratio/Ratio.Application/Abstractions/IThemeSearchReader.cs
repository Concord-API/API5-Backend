namespace Ratio.Application.Abstractions;

public interface IThemeSearchReader
{
    Task<IReadOnlyList<ThemeSummary>> SearchThemesAsync(
        string? query, int limit, ThemeFilters filters, CancellationToken cancellationToken);

    Task<IReadOnlyList<ThemeSummary>> TopThemesAsync(int limit, ThemeFilters filters, CancellationToken cancellationToken);
}
