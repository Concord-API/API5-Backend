namespace Ratio.Application.Abstractions;

public interface IPanelAlignmentReader
{
    Task<ThemePanelCounts?> GetThemeAsync(long themeKey, CancellationToken cancellationToken);
}
