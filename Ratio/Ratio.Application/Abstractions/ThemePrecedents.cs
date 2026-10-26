namespace Ratio.Application.Abstractions;

public sealed record ThemePrecedent(
    string Kind,
    int Number,
    string Thesis,
    string? Question,
    string Status,
    DateOnly? JudgedAt,
    string? JudgingBody,
    string Link);

public sealed record PrecedentSource(string Name, string Url, DateTimeOffset? ExtractedAt);

public sealed record ThemePrecedents(long ThemeKey, PrecedentSource Source, IReadOnlyList<ThemePrecedent> Precedents);

public interface IPrecedentReader
{
    Task<ThemePrecedents?> GetThemeAsync(long themeKey, CancellationToken cancellationToken);
}
