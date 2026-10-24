namespace Ratio.Application.Abstractions;

public interface IThemeCasesReader
{
    Task<ThemeCases?> ListCasesAsync(long themeKey, int limit, CancellationToken cancellationToken);
}
