namespace Ratio.Application.Abstractions;

public interface IThemeDetailReader
{
    Task<ThemeDetail?> GetThemeAsync(long themeKey, CancellationToken cancellationToken);
}
